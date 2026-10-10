using System.ComponentModel;
using System.Reflection;
using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

/// <summary>One-line summaries under the collapsed Settings section titles.</summary>
public sealed class SettingsSummaryTests : IDisposable
{
    private readonly SystemEqTestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void HardwareSummariesDescribeDeviceAndDefaults()
    {
        var model = HardwareModel();

        Assert.Equal($"{model.SelectedDeviceProfile.DisplayName} · Disconnected", model.DeviceSectionSummary);
        Assert.Equal("Default output format and spatial sound", model.WindowsAudioSectionSummary);
        Assert.Equal("Stream Deck shortcuts", model.IntegrationsSectionSummary);
        Assert.Equal($"{model.SelectedBackgroundMode} · AmpUp Green", model.AppearanceSectionSummary);
        Assert.Equal($"WolfEQ {model.AppVersionText}", model.AboutSectionSummary);
    }

    [Fact]
    public void HardwareDeviceUsesSavedNicknameAndConnectionState()
    {
        var model = HardwareModel(new DeviceBindingSettings { UsbPath = @"\\?\hid#test", Name = "Desk K13" });

        SetPrivate(model, nameof(MainViewModel.IsDeviceConnected), true);

        Assert.Equal("Desk K13 · Connected", model.DeviceSectionSummary);
    }

    [Fact]
    public void LightsSummaryFollowsZonePowerColorAndMode()
    {
        var model = HardwareModel();
        Assert.True(model.SelectedDeviceSupportsBleLighting);

        SetPrivate(model, nameof(MainViewModel.TopLedOn), false);
        SetPrivate(model, nameof(MainViewModel.KnobLedOn), false);
        Assert.Equal("Lights off", model.LightsSectionSummary);

        SetPrivate(model, nameof(MainViewModel.SelectedTopLedColorOption), model.LedColorOptions.Single(option => option.Name == "Green"));
        SetPrivate(model, nameof(MainViewModel.SelectedTopLedModeOption), model.LedModeOptions.Single(option => option.Name == "Pulse"));
        var raised = Track(model);
        SetPrivate(model, nameof(MainViewModel.TopLedOn), true);

        Assert.Equal("Top Green pulse · Knob off", model.LightsSectionSummary);
        Assert.Contains(nameof(MainViewModel.LightsSectionSummary), raised);
    }

    [Fact]
    public void IntegrationsSummaryShowsReadySlotWhenUsbIsSaved()
    {
        var model = HardwareModel(new DeviceBindingSettings { UsbPath = @"\\?\hid#test", Name = "Desk K13" });
        var slot = model.IntegrationSlots.First(candidate => candidate != model.IntegrationSlot);
        var raised = Track(model);

        model.IntegrationSlot = slot;

        Assert.Equal($"Stream Deck · USER {slot} ready", model.IntegrationsSectionSummary);
        Assert.Contains(nameof(MainViewModel.IntegrationsSectionSummary), raised);
    }

    [Fact]
    public void WindowsAudioSummaryShowsCurrentFormatOnceKnown()
    {
        var model = HardwareModel();
        var raised = Track(model);

        SetPrivate(model, nameof(MainViewModel.WindowsAudioCurrentFormat), "24-bit, 48000 Hz");

        Assert.Equal("Default output · 24-bit, 48000 Hz", model.WindowsAudioSectionSummary);
        Assert.Contains(nameof(MainViewModel.WindowsAudioSectionSummary), raised);

        SetPrivate(model, nameof(MainViewModel.WindowsAudioCurrentFormat), "Unavailable");
        Assert.Equal("Default output format and spatial sound", model.WindowsAudioSectionSummary);
    }

    [Fact]
    public void AboutSummaryShortensUpdateStatus()
    {
        var model = HardwareModel();
        var raised = Track(model);

        SetPrivate(model, nameof(MainViewModel.UpdateStatus), "WolfEQ is up to date.");
        Assert.Equal($"WolfEQ {model.AppVersionText} · Up to date", model.AboutSectionSummary);
        Assert.Contains(nameof(MainViewModel.AboutSectionSummary), raised);

        SetPrivate(model, nameof(MainViewModel.UpdateStatus), "WolfEQ v9.9.9 is available.");
        Assert.Equal($"WolfEQ {model.AppVersionText} · v9.9.9 is available", model.AboutSectionSummary);
    }

    [Fact]
    public void AppearanceSummaryUpdatesWhenAccentChanges()
    {
        var model = HardwareModel();
        var violet = model.AccentColorOptions.Single(option => option.Name == "Violet");
        var raised = Track(model);

        // The public setter repaints Application resources, which unit tests do not host;
        // swap the backing field and raise the same notification the setter raises.
        typeof(MainViewModel).GetField("_selectedAccentColorOption", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, violet);
        RaisePropertyChanged(model, nameof(MainViewModel.SelectedAccentColorOption));

        Assert.Equal($"{model.SelectedBackgroundMode} · Violet", model.AppearanceSectionSummary);
        Assert.Contains(nameof(MainViewModel.AppearanceSectionSummary), raised);
    }

    [Fact]
    public async Task SystemEqSummariesFollowStatusAndBypass()
    {
        var model = await _rig.CreateSystemEqModelAsync();

        Assert.Equal("System EQ on Headphones (USB Audio)", model.DeviceSectionSummary);
        Assert.Equal("Bypass and preset shortcuts", model.IntegrationsSectionSummary);
        Assert.Equal("Not available for this device", model.LightsSectionSummary);

        var raised = Track(model);
        model.SystemEqBypassed = true;

        Assert.Equal("System EQ bypassed", model.DeviceSectionSummary);
        Assert.Contains(nameof(MainViewModel.DeviceSectionSummary), raised);
    }

    [Fact]
    public void SwitchingToSystemEqRaisesSectionSummaries()
    {
        var model = _rig.CreateModel();
        var raised = Track(model);

        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;

        Assert.StartsWith("Windows System EQ", model.DeviceSectionSummary);
        Assert.Contains(nameof(MainViewModel.DeviceSectionSummary), raised);
        Assert.Contains(nameof(MainViewModel.LightsSectionSummary), raised);
        Assert.Contains(nameof(MainViewModel.IntegrationsSectionSummary), raised);
    }

    private static MainViewModel HardwareModel(DeviceBindingSettings? binding = null)
    {
        var model = new MainViewModel();
        // Ignore any device connection saved on the developer's machine.
        typeof(MainViewModel).GetField("_binding", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, binding ?? new DeviceBindingSettings());
        RaisePropertyChanged(model, nameof(MainViewModel.DeviceBindingSummary));
        return model;
    }

    private static List<string> Track(MainViewModel model)
    {
        var raised = new List<string>();
        model.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);
        return raised;
    }

    private static void SetPrivate(MainViewModel model, string property, object value)
        => typeof(MainViewModel).GetProperty(property)!.SetValue(model, value);

    private static void RaisePropertyChanged(MainViewModel model, string property)
        => typeof(MainViewModel).GetMethod("OnPropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [property]);
}
