using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class QuickDeviceSwitchTests : IDisposable
{
    private const string K13Path = @"\\?\hid#vid_2972&pid_0120&mi_03#k13";
    private readonly SystemEqTestRig _rig = new();

    internal static HidDeviceCandidate Candidate(string path, ushort productId, string product)
        => new(path, 0x2972, productId, 1, 3, "FiiO", product, null, 0xFF00, 1, 64, 64, 0, null);

    [Fact]
    public void ListStartsWithSystemEqThenPluggedInFiioDevices()
    {
        var options = MainViewModel.BuildQuickDeviceOptions(
            [Candidate(K13Path, 0x0120, "FiiO K13 R2R")],
            FiioDeviceProfiles.K13R2R, K13Path, "Desk K13", isConnected: true, "Applies your EQ to any Windows output");

        Assert.Equal(2, options.Count);
        Assert.Equal(QuickDeviceKind.SystemEq, options[0].Kind);
        Assert.False(options[0].IsCurrent);
        Assert.Same(FiioDeviceProfiles.K13R2R, options[1].Profile);
        Assert.True(options[1].IsCurrent);
        Assert.Equal("Connected · USB", options[1].Subtitle);
    }

    [Fact]
    public void UnknownUsbDevicesAreLeftOut()
    {
        var options = MainViewModel.BuildQuickDeviceOptions(
            [Candidate(@"\\?\hid#other", 0x9999, "Some Mouse")],
            SystemEqProfiles.WindowsSystemEq, null, null, isConnected: false, "Software EQ on DENON");

        var only = Assert.Single(options);
        Assert.True(only.IsCurrent);
        Assert.Equal("Software EQ on DENON", only.Subtitle);
    }

    [Fact]
    public void BeforeAScanTheSavedDeviceIsOfferedForHardwareMode()
    {
        var options = MainViewModel.BuildQuickDeviceOptions(
            null, FiioDeviceProfiles.K13R2R, K13Path, "Desk K13", isConnected: false, "x");

        Assert.Equal(2, options.Count);
        Assert.True(options[1].IsCurrent);
        Assert.Contains("checking", options[1].Subtitle);
    }

    [Fact]
    public async Task PickingSystemEqSwitchesTheProfile()
    {
        var model = _rig.CreateModel();
        Assert.False(model.IsSystemEqSelected);
        model.QuickDeviceScanOverride = () => Task.FromResult<IReadOnlyList<HidDeviceCandidate>>([]);
        await model.RefreshQuickDevicesAsync();

        model.SwitchToQuickDeviceCommand.Execute(model.QuickDevices[0]);

        Assert.True(model.IsSystemEqSelected);
        Assert.True(model.QuickDevices[0].IsCurrent);
        Assert.True(model.HasOnlySystemEqQuickDevice);
    }

    public void Dispose() => _rig.Dispose();
}
