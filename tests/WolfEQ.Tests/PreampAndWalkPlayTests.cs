using System.Collections.ObjectModel;
using System.Reflection;
using WolfEQ.Models;
using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class PreampAndWalkPlayTests
{
    [Fact]
    public void OnlyMeasuredDevicesUseThePreampOffset()
    {
        var offset = FiioDeviceProfiles.All.Where(profile => profile.PreampRegisterOffsetDb != 0).ToArray();

        Assert.Equal(["fiio-ka15", "fiio-ja11", "snowsky-melody"], offset.Select(profile => profile.Id));
        Assert.All(offset, profile =>
        {
            Assert.Equal(120, profile.EncodePreampTenths(profile.NormalizePreamp(0)));
            Assert.Equal(60, profile.EncodePreampTenths(profile.NormalizePreamp(-6)));
            Assert.Equal(-120, profile.EncodePreampTenths(profile.NormalizePreamp(-99)));
            // Never louder than EQ off, whatever is asked.
            Assert.Equal(120, profile.EncodePreampTenths(profile.NormalizePreamp(12)));
            Assert.Equal(-6, profile.DecodePreampRegister(6));
        });
    }

    [Fact]
    public void OtherFiioDevicesSendTheSamePreampAsBefore()
    {
        foreach (var profile in FiioDeviceProfiles.All.Where(p => p.PreampRegisterOffsetDb == 0 && p.WalkPlay is null))
        {
            for (var db = -30.0; db <= 15; db += 0.1)
            {
                var old = Math.Clamp(Math.Round(db, 1), profile.MinGainDb, profile.MaxGainDb);
                Assert.Equal((short)Math.Round(old * 10.0), profile.EncodePreampTenths(profile.NormalizePreamp(db)));
            }
        }
    }

    [Theory]
    [InlineData("fiio-ka15", -3.5, -4.0)]
    [InlineData("snowsky-melody", -3.1, -4.0)]
    [InlineData("fiio-ja11", -3.5, -3.5)]
    [InlineData("trn-black-pearl", -0.5, -1.0)]
    public void WholeDbDevicesRoundDown(string id, double asked, double sent)
        => Assert.Equal(sent, Profile(id).NormalizePreamp(asked));

    [Fact]
    public void FactoryResetCommandIsNeverUsedAsSave()
        => Assert.All(FiioDeviceProfiles.All, profile => Assert.Equal(0x19, profile.SaveCommandId));

    [Theory]
    [InlineData("trn-black-pearl", 0x43E8)]
    [InlineData("crinear-protocol-max", 0x43CC)]
    [InlineData("tanchjim-space-ii", 0x4381)]
    public void WalkPlayDevicesMatchAndStaySaveOnly(string id, int productId)
    {
        var profile = FiioDeviceProfiles.Match((ushort)productId, null, null)!;
        Assert.Equal(id, profile.Id);
        Assert.False(profile.SupportsLiveEqWrites);
        Assert.Equal(0, profile.MaxPreampDb);

        Assert.Single(FiioK13DeviceService.GetSelectableDevices([Candidate(0x3302, (ushort)productId, 64)]));
        Assert.Empty(FiioK13DeviceService.GetSelectableDevices([Candidate(0x3302, (ushort)productId, 33)]));
        Assert.Empty(FiioK13DeviceService.GetSelectableDevices([Candidate(0x2972, (ushort)productId, 64)]));
    }

    [Fact]
    public void FiioSendPathsAreBlockedForWalkPlayDevices()
    {
        var service = new FiioK13DeviceService { SelectedProfile = FiioDeviceProfiles.TrnBlackPearl };
        var guard = typeof(FiioK13DeviceService).GetMethod("EnsureFiioProtocol", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.IsType<InvalidOperationException>(
            Assert.Throws<TargetInvocationException>(() => guard.Invoke(service, null)).InnerException);
        service.SelectedProfile = FiioDeviceProfiles.K13R2R;
        guard.Invoke(service, null);
    }

    [Fact]
    public void BandFrameMatchesTheWireLayoutAndReadsBack()
    {
        var profile = FiioDeviceProfiles.TrnBlackPearl;
        var band = new K13EqBandReadback(3, 1000, -6, 2, (byte)EqFilterType.HighShelf);
        var frame = WalkPlayCodec.BandWrite(band, 0, profile.WalkPlay!);

        Assert.Equal(64, frame.Length);
        Assert.Equal([0x4B, 0x01, 0x09, 0x21, 0x00, 0x02, 0x00, 0x00], frame[..8]);
        Assert.Equal([0xE8, 0x03], frame[28..30]);
        Assert.Equal(724, frame[30] | (frame[31] << 8)); // 2 x sqrt 2 x 256
        Assert.Equal([0x00, 0xFA, 0x03, 0x00, 0x00, 0x00], frame[32..38]);
        Assert.Equal(band, WalkPlayCodec.ToBand(WalkPlayCodec.ParseRegisters(frame), profile.WalkPlay!));
    }

    [Fact]
    public void FlatPeakIsUnityInQ30()
    {
        var frame = WalkPlayCodec.BandWrite(new(1, 1000, 0, 1, 0), 0, new WalkPlayOptions());
        Assert.Equal(1 << 30, Coefficient(frame, 0) + 0);
        Assert.Equal(0, Coefficient(frame, 1) + Coefficient(frame, 3));
        Assert.Equal(0, Coefficient(frame, 2) + Coefficient(frame, 4));
    }

    [Theory]
    [InlineData(EqFilterType.Peak, 1000, 1.0, 6.0, 1000)]
    [InlineData(EqFilterType.LowShelf, 100, 0.71, -6.0, 5)]
    [InlineData(EqFilterType.HighShelf, 2000, 0.71, -6.0, 45000)]
    [InlineData(EqFilterType.LowPass, 2000, 0.71, 0.0, 5)]
    [InlineData(EqFilterType.HighPass, 2000, 0.71, 0.0, 45000)]
    public void CoefficientsPlayTheRequestedGain(EqFilterType type, int frequency, double q, double gain, double probe)
    {
        var frame = WalkPlayCodec.BandWrite(new(1, frequency, gain, q, (byte)type), 0, new WalkPlayOptions());
        var c = Enumerable.Range(0, 5).Select(i => Coefficient(frame, i) / 1073741824.0).ToArray();
        var z = System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI * probe / 96000);
        var response = (c[0] + c[1] * z + c[2] * z * z) / (1 - c[3] * z - c[4] * z * z);
        Assert.Equal(gain, 20 * Math.Log10(response.Magnitude), 1);
    }

    [Fact]
    public void OversizedBoostIsRefusedUnlessMeasuredSafe()
    {
        var band = new K13EqBandReadback(1, 10000, 10, 0.71, (byte)EqFilterType.HighShelf);
        Assert.Throws<InvalidOperationException>(() => WalkPlayCodec.BandWrite(band, 0, FiioDeviceProfiles.TrnBlackPearl.WalkPlay!));
        WalkPlayCodec.BandWrite(band, 0, FiioDeviceProfiles.CrinEarProtocolMax.WalkPlay!);
    }

    [Fact]
    public void PlanFillsEveryBandAndFlattensDisabledOnes()
    {
        var preset = new EqPreset
        {
            Bands = new ObservableCollection<EqBand>
            {
                new() { Number = 1, FrequencyHz = 100, GainDb = 5, Q = 1, FilterType = EqFilterType.Peak, Enabled = true },
                new() { Number = 2, FrequencyHz = 200, GainDb = 5, Q = 1, FilterType = EqFilterType.BandPass, Enabled = false }
            }
        };
        var bands = WalkPlayCodec.PlanBands(preset, FiioDeviceProfiles.TrnBlackPearl);

        Assert.Equal(10, bands.Count);
        Assert.Equal(5, bands[0].GainDb);
        Assert.All(bands.Skip(1), band => Assert.Equal((0.0, (byte)EqFilterType.Peak), (band.GainDb, band.FilterType)));

        preset.Bands[1].Enabled = true;
        Assert.Throws<InvalidOperationException>(() => WalkPlayCodec.PlanBands(preset, FiioDeviceProfiles.TrnBlackPearl));
    }

    [Fact]
    public void SaveSequencesMatchEachDevice()
    {
        var bands = WalkPlayCodec.PlanBands(new EqPreset { Bands = [] }, FiioDeviceProfiles.TrnBlackPearl);
        var trn = WalkPlayCodec.WriteSequence(bands, -6, 0, FiioDeviceProfiles.TrnBlackPearl.WalkPlay!);
        var max = WalkPlayCodec.WriteSequence(bands, -6, 0, FiioDeviceProfiles.CrinEarProtocolMax.WalkPlay!);

        Assert.Equal([0x0A, 0x03, 0x01, 0x04], trn.Skip(10).Select(step => (int)step.Report[2]));
        Assert.Equal([0x03, 0x05, 0x17, 0x0A, 0x01], max.Skip(10).Select(step => (int)step.Report[2]));
        Assert.Equal([0x4B, 0x01, 0x03, 0x02, 0x00, 0xFA], max[10].Report[..6]);
        Assert.Equal(1, max[^1].Report[3]);
        Assert.Equal(0x18, max[0].Report[3]);
    }

    private static FiioDeviceProfile Profile(string id) => FiioDeviceProfiles.All.Single(profile => profile.Id == id);

    private static int Coefficient(byte[] frame, int index) => BitConverter.ToInt32(frame, 8 + index * 4);

    private static HidDeviceCandidate Candidate(ushort vendor, ushort product, ushort length)
        => new($"{vendor}-{product}-{length}", vendor, product, 1, 0, null, null, null, 0x0C, 1, length, length, 0, null);
}
