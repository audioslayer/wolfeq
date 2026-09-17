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
