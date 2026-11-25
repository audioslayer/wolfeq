using System.Collections.ObjectModel;
using System.Numerics;
using System.Reflection;
using WolfEQ.Models;
using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class KtMicroTests
{
    private static EqBand Band(int number, EqFilterType type, int hz, double gain, double q, bool on = true)
        => new() { Number = number, FilterType = type, FrequencyHz = hz, GainDb = gain, Q = q, Enabled = on };

    private static EqPreset Preset(params EqBand[] bands) => new() { Bands = new ObservableCollection<EqBand>(bands) };

    [Fact]
    public void FramesMatchTheRegisterLayout()
    {
        Assert.Equal([0x4B, 0x24, 0, 0, 0, 0x52, 0, 3, 0, 0, 0], KtMicroCodec.ReadRequest(0x24));
        Assert.Equal([0x4B, 0x26, 0, 0, 0, 0x52, 0, 0, 0, 0, 0], KtMicroCodec.ReadRequest(0x26));
        Assert.Equal([0x4B, 0, 0, 0, 0, 0x53, 0, 0, 0, 0, 0], KtMicroCodec.SaveRequest());

        // -6 dB peak at 1 kHz, Q 2: gain -60, frequency halved to 500, Q 2000.
        var registers = KtMicroCodec.PlanBands(Preset(Band(1, EqFilterType.Peak, 1000, -6, 2)), FiioDeviceProfiles.JcallyJm12);
        Assert.Equal(10, registers.Count);
        Assert.Equal([0x4B, 0x26, 0, 0, 0, 0x57, 0, 0xC4, 0xFF, 0xF4, 0x01], KtMicroCodec.WriteRequest(registers[0]));
        Assert.Equal([0x4B, 0x27, 0, 0, 0, 0x57, 0, 0xD0, 0x07, 0x00, 0x00], KtMicroCodec.WriteRequest(registers[1]));
        Assert.Equal(new(1, 1000, -6, 2, 0), KtMicroCodec.DecodeBand(0, registers[0], registers[1]));
    }

    [Fact]
    public void OnlyReadsEqWritesAndSaveCanBeSent()
    {
        Assert.True(KtMicroCodec.IsAllowed(KtMicroCodec.SaveRequest()));
        for (var register = 0; register < 256; register++)
        {
            var allowed = register == 0x24 || register is >= 0x26 and <= 0x2F;
            Assert.Equal(allowed, KtMicroCodec.IsAllowed(KtMicroCodec.WriteRequest(new((byte)register, 0, 0, 0, 0))));
        }

        for (var operation = 0; operation < 256; operation++)
        {
            byte[] frame = [0x4B, 0, 0, 0, 0, (byte)operation, 0, 0, 0, 0, 0];
            Assert.Equal(operation is 0x52 or 0x53, KtMicroCodec.IsAllowed(frame));
        }

        Assert.False(KtMicroCodec.IsAllowed([0x54, 0, 0, 0, 0, 0x52, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void LimitsAreCheckedBeforeAnythingIsSent()
    {
        var profile = FiioDeviceProfiles.KiwiEarsAd1;
        Assert.Throws<InvalidOperationException>(() => KtMicroCodec.PlanBands(Preset(Band(1, EqFilterType.LowShelf, 100, 7, 0.7)), profile));
        Assert.Throws<InvalidOperationException>(() => KtMicroCodec.PlanBands(Preset(Band(1, EqFilterType.HighShelf, 8000, 3, 2)), profile));
        Assert.Throws<InvalidOperationException>(() => KtMicroCodec.PlanBands(Preset(Band(1, EqFilterType.LowPass, 100, 0, 0.7)), profile));
        var six = Enumerable.Range(1, 6).Select(n => Band(n, EqFilterType.Peak, 100 * n, 1, 1)).ToArray();
        Assert.Throws<InvalidOperationException>(() => KtMicroCodec.PlanBands(Preset(six), profile));
        six[5].Enabled = false;
        Assert.Equal(10, KtMicroCodec.PlanBands(Preset(six), profile).Count);
    }

    [Fact]
    public void RepliesMustMatchAndBeAcknowledged()
    {
        byte[] ack = [0x4B, 0x26, 0, 0, 0, 0x57, 0, 3, 0, 0, 0];
        Assert.True(KtMicroCodec.IsWriteReply(ack, 0x26));
        Assert.False(KtMicroCodec.IsWriteReply(ack, 0x27));
        Assert.False(KtMicroCodec.IsReadReply(ack, 0x26));
        Assert.True(KtMicroCodec.IsAcknowledged(ack));
        ack[7] = 2;
        Assert.False(KtMicroCodec.IsAcknowledged(ack));
        Assert.True(KtMicroCodec.HasZeroDigitalGain(new(0x66, 0, 2, 0, 0), new(1, 0, 2, 0, 0)));
        Assert.False(KtMicroCodec.HasZeroDigitalGain(new(0x66, 0, 2, 0, 0), null));
        Assert.Null(KtMicroCodec.ParseEqOn(new(0x24, 7, 0, 0, 0)));
    }

    [Theory]
    [InlineData(0x31B2, 0x0111, "KT02H20 HIFI Audio", 11, "jcally-jm12")]
    [InlineData(0x31B2, 0x0111, "Allegro Mini", 11, null)] // same chip IDs, different device
    [InlineData(0x31B2, 0x0111, "KT02H20 HIFI Audio", 64, null)]
    [InlineData(0x3302, 0x20E3, "AD1", 11, "kiwi-ears-ad1")]
    [InlineData(0x3302, 0x20E3, "AD1 Pro", 11, null)]
    [InlineData(0x3302, 0x43E8, "AD1", 11, null)]
    [InlineData(0x3302, 0xC20F, "anything", 64, "crinear-protocol-micro")]
    [InlineData(0x3302, 0x12C5, "ECHO-A", 64, "moondrop-echo-a")]
    public void NewDevicesAreMatchedStrictly(int vendor, int product, string name, int length, string? id)
    {
        var candidate = new HidDeviceCandidate("path", (ushort)vendor, (ushort)product, 1, 3, null, name, null, 0x0C, 1, (ushort)length, (ushort)length, 0, null);
        var found = FiioK13DeviceService.GetSelectableDevices([candidate]);
        Assert.Equal(id, found.Count == 0 ? null : FiioDeviceProfiles.Match((ushort)product, name, 3)!.Id);
    }

    [Fact]
    public void NewDevicesAreSaveOnlyAndNeverGetFiioFrames()
    {
        var guard = typeof(FiioK13DeviceService).GetMethod("EnsureFiioProtocol", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var profile in new[] { FiioDeviceProfiles.JcallyJm12, FiioDeviceProfiles.KiwiEarsAd1, FiioDeviceProfiles.CrinEarProtocolMicro, FiioDeviceProfiles.MoondropEchoA })
        {
            Assert.False(profile.SupportsLiveEqWrites);
            Assert.False(profile.IsVerified);
            Assert.Throws<TargetInvocationException>(() => guard.Invoke(new FiioK13DeviceService { SelectedProfile = profile }, null));
        }

        Assert.Equal(1000, FiioDeviceProfiles.Btr25.SelectSettleMs);
        Assert.Equal(0, FiioDeviceProfiles.K13R2R.SelectSettleMs);
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(-6.0)]
    public void AnEmulatedHighShelfPlaysTheSameCurve(double gain)
    {
        var profile = FiioDeviceProfiles.CrinEarProtocolMicro;
        var bands = WalkPlayCodec.PlanBands(Preset(Band(1, EqFilterType.HighShelf, 4000, gain, 0.71)), profile);
        var (realized, preamp) = WalkPlayCodec.Realize(bands, -8, profile);
        Assert.Equal((byte)EqFilterType.LowShelf, realized[0].FilterType);
        Assert.Equal(-8 + gain, preamp);

        // A high shelf leaves the lows alone and moves the highs by its gain, here 8 dB down.
        Assert.Equal(-8, Response(realized[0], 30) + preamp, 1);
        Assert.Equal(-8 + gain / 2, Response(realized[0], 4000) + preamp, 1);
        Assert.Equal(-8 + gain, Response(realized[0], 46000) + preamp, 1);

        Assert.Throws<InvalidOperationException>(() => WalkPlayCodec.Realize(bands, gain > 0 ? -1 : -28, profile));

        double Response(K13EqBandReadback band, double hz)
        {
            var frame = WalkPlayCodec.BandWrite(band, 0, profile.WalkPlay!);
            var c = Enumerable.Range(0, 5).Select(i => BitConverter.ToInt32(frame, 8 + i * 4) / 1073741824.0).ToArray();
            var z = Complex.FromPolarCoordinates(1, -2 * Math.PI * hz / 93750);
            return 20 * Math.Log10(((c[0] + c[1] * z + c[2] * z * z) / (1 - c[3] * z - c[4] * z * z)).Magnitude);
        }
    }

    [Fact]
    public void ExtraWalkPlayBandsAreRefusedInsteadOfDropped()
    {
        var nine = Enumerable.Range(1, 9).Select(n => Band(n, EqFilterType.Peak, 100 * n, 1, 1)).ToArray();
        Assert.Throws<InvalidOperationException>(() => WalkPlayCodec.PlanBands(Preset(nine), FiioDeviceProfiles.MoondropEchoA));
        Assert.Equal(8, WalkPlayCodec.PlanBands(Preset(nine[..8]), FiioDeviceProfiles.MoondropEchoA).Count);
    }
}
