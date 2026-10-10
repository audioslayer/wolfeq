using System.Reflection;
using WolfEQ.Models;
using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class AutomationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("11")]
    [InlineData("-1")]
    [InlineData("headphones")]
    public void InvalidSlotsAreRejected(string value)
        => Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--slot", value]));

    [Fact]
    public void NamedDeviceAndSlotAreParsed()
    {
        var request = AutomationRequest.Parse(["--device", "Desk K13", "--slot", "10"]);
        Assert.Equal(10, request.Slot);
        Assert.Equal("Desk K13", request.Device);
    }

    [Theory]
    [InlineData("--slot")]
    [InlineData("--unknown")]
    [InlineData("--device")]
    public void IncompleteCommandsAreRejected(string value)
        => Assert.Throws<ArgumentException>(() => AutomationRequest.Parse([value]));

    [Fact]
    public void ConflictingOrRepeatedActionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--slot", "1", "--slot", "2"]));
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--slot", "1", "--list-devices"]));
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--device", "Desk"]));
    }

    [Fact]
    public async Task PipeForwardsCommandsAndResults()
    {
        using var host = new AutomationHost(Guid.NewGuid().ToString("N"));
        var server = host.RunAsync(args => Task.FromResult(new AutomationResult(3, string.Join("|", args))));
        var result = await host.SendAsync(["--slot", "2"]);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("--slot|2", result.Message);
        var next = await host.SendAsync(["--slot", "3"]);
        Assert.Equal("--slot|3", next.Message);
        host.Dispose();
        await server.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UnsavedEditorChangesRejectAutomationBeforeDeviceAccess()
    {
        var model = new WolfEQ.ViewModels.MainViewModel();
        model.EditorSession.NotifyEdit();
        var result = await model.ExecuteAutomationAsync(new AutomationRequest(Slot: 2));
        Assert.Equal(3, result.ExitCode);
        Assert.True(model.EditorSession.WouldReplaceUnsavedEdits);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("OFF")]
    [InlineData("toggle")]
    public void SystemEqBypassActionsAreParsed(string value)
    {
        Assert.Equal(value.ToLowerInvariant(), AutomationRequest.Parse(["--system-eq-bypass", value]).SystemEqBypass);
        Assert.Equal(value.ToLowerInvariant(), AutomationRequest.Parse(["--system-eq", "bypass", value]).SystemEqBypass);
    }

    [Fact]
    public void SystemEqArgumentsAreValidated()
    {
        Assert.Equal("Bright Test", AutomationRequest.Parse(["--preset", "Bright Test"]).PresetName);
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--system-eq-bypass", "maybe"]));
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--preset", " "]));
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--preset", "A", "--slot", "1"]));
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--preset", "A", "--system-eq-bypass", "on"]));
        Assert.Throws<ArgumentException>(() => AutomationRequest.Parse(["--device", "Desk", "--preset", "A"]));
    }

    [Fact]
    public async Task SystemEqAutomationRefusesSlotsAndDrivesBypassAndPresets()
    {
        using var rig = new SystemEqTestRig();
        var model = await rig.CreateSystemEqModelAsync();
        var preset = SystemEqTestRig.Preset("Bright Test", gainDb: 2, preampDb: -2);
        model.Presets.Add(preset);

        var slot = await model.ExecuteAutomationAsync(AutomationRequest.Parse(["--slot", "1"]));
        Assert.Equal(1, slot.ExitCode);
        Assert.Contains("not available in Windows System EQ mode", slot.Message);

        var bypass = await model.ExecuteAutomationAsync(AutomationRequest.Parse(["--system-eq-bypass", "toggle"]));
        Assert.Equal(0, bypass.ExitCode);
        Assert.True(model.SystemEqBypassed);
        Assert.True(rig.Backend.Applies[^1].Bypassed);
        await model.ExecuteAutomationAsync(AutomationRequest.Parse(["--system-eq-bypass", "off"]));
        Assert.False(model.SystemEqBypassed);

        rig.Backend.Applies.Clear();
        var applied = await model.ExecuteAutomationAsync(AutomationRequest.Parse(["--preset", "bright test"]));
        Assert.Equal(0, applied.ExitCode);
        Assert.Same(preset, model.SelectedPreset);
        Assert.Equal(2, Assert.Single(rig.Backend.Applies).Preset.Bands[0].GainDb);

        var unknown = await model.ExecuteAutomationAsync(AutomationRequest.Parse(["--preset", "No Such Preset"]));
        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("No Such Preset", unknown.Message);
    }

    [Fact]
    public async Task SystemEqAutomationWaitsForAnInFlightApplyAndReportsTheRealOutcome()
    {
        using var rig = new SystemEqTestRig();
        var model = await rig.CreateSystemEqModelAsync();
        var gate = new TaskCompletionSource();
        rig.Backend.ApplyGate = gate;

        model.Bands[0].GainDb = 5;
        var debounced = model.FlushSystemEqApplyAsync();
        Assert.False(debounced.IsCompleted);

        var bypass = model.ExecuteAutomationAsync(AutomationRequest.Parse(["--system-eq-bypass", "on"]));
        var bypassOff = model.ExecuteAutomationAsync(AutomationRequest.Parse(["--system-eq-bypass", "off"]));
        Assert.False(bypass.IsCompleted);
        Assert.False(bypassOff.IsCompleted);

        gate.SetResult();
        await debounced;
        var bypassResult = await bypass;
        var bypassOffResult = await bypassOff;

        // The in-flight apply picked up the bypass instead of the command reporting Failed,
        // and the second command ran only after the first finished.
        Assert.Equal((0, "Windows System EQ bypass on."), (bypassResult.ExitCode, bypassResult.Message));
        Assert.Equal((0, "Windows System EQ bypass off."), (bypassOffResult.ExitCode, bypassOffResult.Message));
        Assert.Equal([false, true, false], rig.Backend.Applies.Select(apply => apply.Bypassed));
        Assert.Equal(5, rig.Backend.Applies[0].Preset.Bands[0].GainDb);
    }

    [Fact]
    public async Task SystemEqPresetAutomationRefusesUnsavedEditsToTheOpenPreset()
    {
        using var rig = new SystemEqTestRig();
        var model = await rig.CreateSystemEqModelAsync();
        var open = model.SelectedPreset!;
        var storedGain = open.Bands[0].GainDb;
        model.Bands[0].GainDb = storedGain + 4;
        await model.FlushSystemEqApplyAsync();
        rig.Backend.Applies.Clear();

        var result = await model.ExecuteAutomationAsync(AutomationRequest.Parse(["--preset", open.Name]));

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("unsaved", result.Message);
        Assert.Empty(rig.Backend.Applies);
    }

    [Fact]
    public async Task SystemEqAutomationNeedsSystemEqMode()
    {
        using var rig = new SystemEqTestRig();
        var model = rig.CreateModel();
        var result = await model.ExecuteAutomationAsync(AutomationRequest.Parse(["--system-eq-bypass", "on"]));
        Assert.Equal(1, result.ExitCode);
        Assert.False(model.SystemEqBypassed);
        Assert.Empty(rig.Backend.Applies);
    }

    [Fact]
    public void SecondInstanceCannotAcquireOwnership()
    {
        var key = Guid.NewGuid().ToString("N");
        using var owner = new AutomationHost(key);
        Assert.True(owner.TryOwn());
        bool? acquired = null;
        var thread = new Thread(() =>
        {
            using var other = new AutomationHost(key);
            acquired = other.TryOwn();
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.False(acquired);
    }

    [Fact]
    public void LightingStorageIsScopedToDeviceAndCaseInsensitive()
    {
        var service = new SlotLightingProfileService();
        service.SetDevice("device-a");
        var first = service.StoragePath;
        service.SetDevice("DEVICE-A");
        Assert.Equal(first, service.StoragePath);
        service.SetDevice("device-b");
        Assert.NotEqual(first, service.StoragePath);
    }

    [Fact]
    public async Task BluetoothWithoutExplicitSelectionRefusesConnection()
    {
        var service = new FiioK13BleLightService();
        Assert.Null(service.SelectedAddress);
        var error = await Assert.ThrowsAsync<K13BleOperationException>(() => service.ReadLightsAsync());
        Assert.Contains("Choose and save", error.InnerException!.Message);
    }

    [Fact]
    public void UsbSelectionNeverFallsBackFromMissingPinnedDevice()
    {
        var service = new FiioK13DeviceService { SelectedDevicePath = "my-device" };
        Assert.Empty(Restrict(service, [Candidate("other-device")]));
        Assert.Single(Restrict(service, [Candidate("other-device"), Candidate("my-device")]));
    }

    [Fact]
    public void MultipleUsbDevicesRequireSelection()
    {
        var service = new FiioK13DeviceService();
        Assert.Empty(Restrict(service, [Candidate("one"), Candidate("two")]));
        Assert.Single(Restrict(service, [Candidate("one")]));
    }

    private static IReadOnlyList<HidDeviceCandidate> Restrict(FiioK13DeviceService service, HidDeviceCandidate[] candidates)
        => (IReadOnlyList<HidDeviceCandidate>)typeof(FiioK13DeviceService)
            .GetMethod("RestrictToSelectedDevice", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [candidates])!;

    private static HidDeviceCandidate Candidate(string path) => new(path, 0x2972,
        FiioDeviceProfiles.K13R2R.ProductId, 1, 3, "FiiO", "K13 R2R", null,
        0xFF00, 1, 33, 33, 0, null);
}
