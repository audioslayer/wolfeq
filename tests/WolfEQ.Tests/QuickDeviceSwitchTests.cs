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

    private const string OldPath = @"\\?\hid#vid_2972&pid_0120&mi_03&col01#9&1b9fa339&0&0000#{4d1e55b2}";
    private const string NewPath = @"\\?\hid#vid_2972&pid_0120&rev_0115&mi_03&col01#9&e644be4&0&0000#{4d1e55b2}";

    [Theory]
    [InlineData(OldPath, NewPath, true)]
    [InlineData(OldPath, OldPath, false)]
    [InlineData(OldPath, @"\\?\hid#vid_2972&pid_0104&mi_03&col01#9&1&0&0000#{4d1e55b2}", false)]
    [InlineData(OldPath, @"\\?\hid#vid_2972&pid_0120&mi_03&col02#9&1&0&0001#{4d1e55b2}", false)]
    [InlineData(null, NewPath, false)]
    public void RecognisesTheSameModelOnANewPath(string? before, string after, bool same)
        => Assert.Equal(same, DeviceBindingSettings.IsSameModelOnNewPath(before, after));

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MovedDeviceKeepsBluetoothNamesAndLights(bool oldUnitStillPluggedIn, bool kept)
    {
        var old = OldPath.Replace("1b9fa339", Guid.NewGuid().ToString("N"));
        var moved = NewPath.Replace("e644be4", Guid.NewGuid().ToString("N"));
        new DeviceSlotNameService().Save(old, FiioDeviceProfiles.K13R2R.Id, 0xA0, "Bass");
        var lights = new SlotLightingProfileService();
        lights.SetDevice(old);
        lights.Save([new SlotLightingProfileData { Slot = 1, TopColor = 3 }]);
        var model = _rig.CreateModel();
        model.SelectedDeviceProfile = FiioDeviceProfiles.K13R2R;
        var field = typeof(MainViewModel).GetField("_binding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(model, new DeviceBindingSettings { UsbPath = old, Name = "Desk", BluetoothAddress = 0xAABB, BluetoothName = "FIIO K13 R2R" });
        model.QuickDeviceConnectOverride = () => Task.CompletedTask;
        var plugged = new List<HidDeviceCandidate> { Candidate(moved, 0x0120, "FiiO K13 R2R") };
        if (oldUnitStillPluggedIn) plugged.Add(Candidate(old, 0x0120, "FiiO K13 R2R"));
        model.QuickDeviceScanOverride = () => Task.FromResult<IReadOnlyList<HidDeviceCandidate>>(plugged);
        await model.RefreshQuickDevicesAsync();

        model.SwitchToQuickDeviceCommand.Execute(model.QuickDevices.Single(device => device.UsbPath == moved));

        var binding = (DeviceBindingSettings)field.GetValue(model)!;
        Assert.Equal(moved, binding.UsbPath);
        Assert.Equal(kept ? 0xAABBul : null, binding.BluetoothAddress);
        Assert.Equal(kept, binding.Name == "Desk");
        Assert.Equal(kept, new DeviceSlotNameService().Load(moved, FiioDeviceProfiles.K13R2R.Id).ContainsKey(0xA0));
        lights.SetDevice(moved);
        Assert.Equal(kept, lights.Load().TryGetValue(1, out var light) && light.TopColor == 3);
    }

    public void Dispose() => _rig.Dispose();
}
