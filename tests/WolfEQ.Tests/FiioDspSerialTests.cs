using System.Collections.ObjectModel;
using System.Reflection;
using WolfEQ.Models;
using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class FiioDspSerialTests
{
    [Fact]
    public void FramesMatchTheDeviceLayout()
    {
        Assert.Equal([0xFC, 0xFA, 0x06, 0x00, 0xA7, 0x00, 0x30, 0x11, 0x00, 0x1E], FiioDspSerialCodec.Connect());
        Assert.Equal([0xFC, 0xFA, 0x04, 0x00, 0xA0, 0x00, 0x02, 0x64], FiioDspSerialCodec.ReadPreset());
        Assert.Equal([0xFC, 0xFA, 0x06, 0x00, 0xA7, 0x00, 0x02, 0x07], FiioDspSerialCodec.SelectPreset(7)[..8]);
        Assert.Equal([0xFC, 0xFA, 0x06, 0x00, 0xA7, 0x00, 0x01, 0x10], FiioDspSerialCodec.SaveToPreset(16)[..8]);
    }

    [Fact]
    public void TablesHaveTheDevicesSizesAndOrder()
    {
        foreach (var (table, count, first, last) in new[]
        {
            (FiioDspSerialCodec.Frequencies, 362, 19.7, 20600.0), (FiioDspSerialCodec.Gains, 181, -24.0, 12.0), (FiioDspSerialCodec.Widths, 101, 0.4, 128.0)
        })
        {
            Assert.Equal((count, first, last), (table.Length, table[0], table[^1]));
            Assert.Equal(table.Order(), table);
        }
    }

    [Fact]
    public void AnEchoOfTheRequestIsNeverAnAnswer()
    {
        // The K17 R2R's HID interface echoed requests back, which used to read as "Jazz".
        Assert.Null(FiioDspSerialCodec.ParsePreset(FiioDspSerialCodec.ReadPreset()));
        Assert.Null(FiioDspSerialCodec.ParseBands(FiioDspSerialCodec.ReadBands()));
        Assert.Null(FiioDspSerialCodec.TryTakeFrame([0xBB, 0x0B, 0x00, 0x00, 0x16, 0x00, 0x00, 0xEE]));
    }

    [Fact]
    public void RepliesAreFoundAfterNoiseAndOnlyWhenComplete()
    {
        var reply = FiioDspSerialCodec.Frame(0xA0, 0, 2, 9, 0);
        byte[] noisy = [0xBB, 0x00, .. reply];
        Assert.Null(FiioDspSerialCodec.TryTakeFrame(noisy.AsSpan(0, noisy.Length - 1)));
        Assert.Equal(9, FiioDspSerialCodec.ParsePreset(FiioDspSerialCodec.TryTakeFrame(noisy)!));
    }

    [Fact]
    public void PlannedBandsSurviveTheRoundTrip()
    {
        var preset = new EqPreset
        {
            Bands = new ObservableCollection<EqBand>
            {
                new() { Number = 1, FrequencyHz = 1000, GainDb = -6, Q = 2, FilterType = EqFilterType.Peak, Enabled = true },
                new() { Number = 2, FrequencyHz = 105, GainDb = 5.6, Q = 0.71, FilterType = EqFilterType.LowShelf, Enabled = true },
                new() { Number = 3, FrequencyHz = 3000, GainDb = 9, Q = 1, FilterType = EqFilterType.Peak, Enabled = false }
            }
        };
        var planned = FiioDspSerialCodec.PlanBands(preset, FiioDeviceProfiles.K17R2R);
        Assert.Equal(31, planned.Count);
        Assert.Equal(new(1, 1000, -6, 2, 0), FiioDspSerialCodec.ToBand(0, planned[0]));
        var shelf = FiioDspSerialCodec.ToBand(1, planned[1]);
        Assert.Equal((5.6, 0.71, (byte)1), (shelf.GainDb, shelf.Q, shelf.FilterType));
        Assert.InRange(shelf.FrequencyHz, 103, 107);
        Assert.All(Enumerable.Range(2, 29), index => Assert.Equal(0, FiioDspSerialCodec.ToBand(index, planned[index]).GainDb));

        // The device uploads: index word, pass word, then the records.
        byte[] data = [0, 0, 0, 0, .. planned.SelectMany(band => FiioDspSerialCodec.WriteBand(0, band)[9..19])];
        Assert.Equal(planned, FiioDspSerialCodec.ParseBands(FiioDspSerialCodec.Frame(0xA0, 1, 0, data)));
    }

    [Fact]
    public void UnsupportedFiltersAndTooManyBandsAreRefused()
    {
        var preset = new EqPreset { Bands = new ObservableCollection<EqBand> { new() { Number = 1, FrequencyHz = 100, Q = 1, FilterType = EqFilterType.LowPass, Enabled = true } } };
        Assert.Throws<InvalidOperationException>(() => FiioDspSerialCodec.PlanBands(preset, FiioDeviceProfiles.K17));
        var large = new EqPreset { Bands = new ObservableCollection<EqBand>(Enumerable.Range(1, 32).Select(n => new EqBand { Number = n, FrequencyHz = 100, Q = 1, Enabled = true })) };
        Assert.Throws<InvalidOperationException>(() => FiioDspSerialCodec.PlanBands(large, FiioDeviceProfiles.K17));
    }

    [Theory]
    [InlineData("VID_1A86&PID_55D3", "USB-Enhanced-SERIAL CH343 (COM5)", true)]
    [InlineData("VID_2972&PID_0140&MI_04", "USB Serial Device (COM6)", true)]
    [InlineData("VID_1A86&PID_7523", "USB-SERIAL CH340 (COM3)", false)] // common in Arduinos and 3D printers
    [InlineData("VID_0403&PID_6001", "USB Serial Port (COM4)", false)]
    public void OnlyLikelyFiioPortsAreEverOpened(string id, string name, bool expected)
        => Assert.Equal(expected, new UsbSerialPort("COM9", id, name).LooksLikeFiioDsp);

    [Fact]
    public void SerialDevicesAreSaveOnlyAndNeverDrivenOverHid()
    {
        var guard = typeof(FiioK13DeviceService).GetMethod("EnsureFiioProtocol", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var profile in new[] { FiioDeviceProfiles.K17, FiioDeviceProfiles.K17R2R, FiioDeviceProfiles.K19 })
        {
            Assert.True(profile.UsesDspSerial);
            Assert.False(profile.SupportsLiveEqWrites);
            Assert.Throws<TargetInvocationException>(() => guard.Invoke(new FiioK13DeviceService { SelectedProfile = profile }, null));
        }

        Assert.Single(FiioDeviceProfiles.All, profile => profile.Id == "fiio-k17-r2r");
        Assert.DoesNotContain(FiioDeviceProfiles.All, profile => profile.UsesDspSerial && !profile.Id.StartsWith("fiio-k1", StringComparison.Ordinal));
    }

    [Fact]
    public void K17R2RIsRecognisedByItsFeatureOnlyHidInterface()
    {
        // From a user's log: in/out/feature 0/0/65.
        var candidate = new HidDeviceCandidate("path", 0x2972, 0x0140, 0x0104, 3, "FiiO", "FIIO K17 R2R", null, 0xFF00, 1, 0, 0, 65, null);
        Assert.Single(FiioK13DeviceService.GetSelectableDevices([candidate]));
    }

    [Fact]
    public void SeveralHidCollectionsOfOneSerialDeviceAreOneChoice()
    {
        HidDeviceCandidate Collection(string path, ushort usagePage) => new(path, 0x2972, 0x0140, 0x0104, 3, "FiiO", "FIIO K17", null, usagePage, 1, 0, 0, 65, null);
        // Product ID is only known for the K17 R2R, so use an unknown one to match the K17 by name.
        var found = FiioK13DeviceService.GetSelectableDevices([Collection("a", 0x000C) with { ProductId = 0x0FFF }, Collection("b", 0xFF00) with { ProductId = 0x0FFF }]);
        Assert.Single(found);
    }

    [Fact]
    public void AnUnpluggedSerialDeviceIsNotPresent()
        => Assert.False(FiioK13DeviceService.IsDevicePresent(@"USB\VID_1A86&PID_55D3\NOT-CONNECTED-WOLFEQ"));
}
