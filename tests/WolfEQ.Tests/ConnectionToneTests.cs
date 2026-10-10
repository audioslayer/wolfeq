using System.Reflection;
using WolfEQ.Models;
using WolfEQ.Services.SystemEq;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class ConnectionToneTests : IDisposable
{
    private readonly SystemEqTestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void HardwareToneFollowsConnectionAndRaisesChange()
    {
        var model = _rig.CreateModel();
        Assert.False(model.IsSystemEqSelected);
        Assert.Equal(ConnectionTone.Idle, model.ConnectionTone);

        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        SetConnectionState(model, true);
        Assert.Equal("Connected", model.ConnectionText);
        Assert.Equal(ConnectionTone.Active, model.ConnectionTone);
        Assert.Contains(nameof(MainViewModel.ConnectionTone), raised);

        SetConnectionState(model, false);
        Assert.Equal("Disconnected", model.ConnectionText);
        Assert.Equal(ConnectionTone.Idle, model.ConnectionTone);
    }

    [Fact]
    public async Task SystemEqActiveOnATickedOutputIsActive()
    {
        var model = await _rig.CreateSystemEqModelAsync();
        Assert.Equal(SystemEqState.Active, model.SystemEqState);
        Assert.False(model.HasSystemEqWarning);
        Assert.Equal(ConnectionTone.Active, model.ConnectionTone);
    }

    [Fact]
    public void SystemEqNeedsInstallNeedsAttention()
    {
        _rig.Backend.Status = SystemEqBackendStatus.NotInstalled("Not installed.");
        var model = _rig.CreateModel();
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
        Assert.Equal(SystemEqState.NeedsInstall, model.SystemEqState);
        Assert.Equal(ConnectionTone.Attention, model.ConnectionTone);
    }

    [Fact]
    public async Task SystemEqBypassIsIdleAndRaisesChange()
    {
        var model = await _rig.CreateSystemEqModelAsync();
        var raised = new List<string?>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        model.SystemEqBypassed = true;
        Assert.Equal(ConnectionTone.Idle, model.ConnectionTone);
        Assert.Contains(nameof(MainViewModel.ConnectionTone), raised);

        model.SystemEqBypassed = false;
        Assert.Equal(ConnectionTone.Active, model.ConnectionTone);
    }

    [Fact]
    public async Task SystemEqWithoutApoOnTheTickedOutputNeedsAttention()
    {
        _rig.EndpointHealth["{22222222-2222-2222-2222-222222222222}"] = new ApoEndpointStatus(false, false);
        var model = await _rig.CreateSystemEqModelAsync();
        model.SystemEqEndpoints.Single(endpoint => endpoint.IsFiio).IsSelected = true;
        model.SystemEqEndpoints.Single(endpoint => endpoint.IsDefault).IsSelected = false;

        Assert.True(model.HasSystemEqWarning);
        Assert.Equal(ConnectionTone.Attention, model.ConnectionTone);
    }

    private static void SetConnectionState(MainViewModel model, bool connected)
        => typeof(MainViewModel)
            .GetMethod("SetConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [connected]);
}
