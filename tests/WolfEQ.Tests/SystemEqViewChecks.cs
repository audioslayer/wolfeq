using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.Services.SystemEq;
using WolfEQ.ViewModels;
using WolfEQ.Views;
using Xunit;

namespace WolfEQ.Tests;

/// <summary>Renders the Windows System EQ surfaces and checks hardware-only controls are hidden (R3).</summary>
internal static class SystemEqViewChecks
{
    public static void Verify()
    {
        using var rig = new SystemEqTestRig();
        var model = rig.CreateModel();
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
        Assert.True(model.IsSystemEqSelected);
        model.SystemEqEndpoints.First(endpoint => endpoint.IsDefault).IsSelected = true;

        var settings = new SettingsPopover { DataContext = model };
        StudioViewChecks.Layout(settings, 460, 1400);
        settings.DataContext = null;
        foreach (var expander in StudioViewChecks.Descendants(settings).OfType<Expander>())
            expander.IsExpanded = Equals(expander.Header, "Device");
        settings.DataContext = model;
        StudioViewChecks.Layout(settings, 460, 1400);
        StudioViewChecks.Render(settings, 460, 1400, "studio-settings-system-eq");

        var card = (FrameworkElement)settings.FindName("SystemEqCard");
        Assert.True(IsShown(card), "System EQ card must be visible in System EQ mode.");
        Assert.All(StudioViewChecks.Descendants(settings).OfType<TabControl>(), tabs => Assert.False(IsShown(tabs)));
        var endpointBoxes = StudioViewChecks.Descendants(card).OfType<CheckBox>().Where(IsShown).ToArray();
        Assert.Equal(model.SystemEqEndpoints.Count, endpointBoxes.Length);
        Assert.Contains(StudioViewChecks.Descendants(card).OfType<TextBlock>(), text => IsShown(text) && text.Text == "FiiO");
        Assert.Contains(StudioViewChecks.Descendants(settings).OfType<ComboBox>(), combo => IsShown(combo) && ReferenceEquals(combo.ItemsSource, model.DeviceProfiles));

        // Integrations stay available in System EQ mode for the bypass/preset commands (R13),
        // while the USER slot Stream Deck controls are hardware-only.
        var integrations = new SettingsPopover { DataContext = model };
        StudioViewChecks.Layout(integrations, 460, 1400);
        integrations.DataContext = null;
        foreach (var expander in StudioViewChecks.Descendants(integrations).OfType<Expander>())
            expander.IsExpanded = Equals(expander.Header, "Integrations");
        integrations.DataContext = model;
        StudioViewChecks.Layout(integrations, 460, 1400);
        StudioViewChecks.Render(integrations, 460, 1400, "studio-settings-integrations-system-eq");
        var integrationsExpander = StudioViewChecks.Descendants(integrations).OfType<Expander>().Single(expander => Equals(expander.Header, "Integrations"));
        Assert.True(IsShown(integrationsExpander), "Integrations must stay visible in System EQ mode.");
        var commandBoxes = StudioViewChecks.Descendants(integrationsExpander).OfType<TextBox>().Where(IsShown).ToArray();
        Assert.Contains(commandBoxes, box => box.IsReadOnly && box.Text.Contains("--system-eq-bypass", StringComparison.Ordinal));
        Assert.Contains(commandBoxes, box => box.IsReadOnly && box.Text.Contains("--preset", StringComparison.Ordinal));
        Assert.Contains(commandBoxes, box => box.Text.StartsWith($"\"{model.StreamDeckExecutable}\"", StringComparison.Ordinal));
        Assert.DoesNotContain(commandBoxes, box => box.Text.Contains("--slot", StringComparison.Ordinal));
        Assert.True(IsShown((FrameworkElement)integrations.FindName("SystemEqShortcutsPanel")));
        Assert.False(IsShown((FrameworkElement)integrations.FindName("HardwareStreamDeckPanel")));
        Assert.False(IsShown((FrameworkElement)integrations.FindName("StreamDeckSetupButton")));
        Assert.DoesNotContain(StudioViewChecks.Descendants(integrationsExpander).OfType<ComboBox>(), IsShown);

        VerifyTrayTips();
        VerifyInstallProgress(rig);
        VerifyDeviceSwitcher(model);

        var dock = new DeviceDock { DataContext = model };
        StudioViewChecks.Layout(dock, 1586, 80);
        StudioViewChecks.Render(dock, 1586, 80, "studio-dock-system-eq");
        Assert.False(IsShown((FrameworkElement)dock.FindName("SlotMenuButton")));
        Assert.False(IsShown((FrameworkElement)dock.FindName("WriteToDeviceButton")));
        Assert.True(IsShown((FrameworkElement)dock.FindName("SystemEqDockPanel")));

        // A fuller library: favorites lead, and the list stops at six rows.
        EqPreset[] extra =
        [
            new() { Name = "Ananda Music - Warm Detail", Category = "Listening", IsFavorite = true },
            new() { Name = "Late night podcast", Category = "Voice" },
            new() { Name = "Competitive FPS", Category = "Gaming", IsFavorite = true },
            new() { Name = "Movie night", Category = "Cinema" },
            new() { Name = "Bass lift", Category = "Listening" },
            new() { Name = "Hidden seventh", Category = "Listening" },
        ];
        foreach (var preset in extra) model.Presets.Add(preset);
        var tray = new TrayFlyout(model, () => { }, () => { }, () => { });
        var trayContent = (FrameworkElement)tray.Content;
        trayContent.DataContext = tray.DataContext;
        trayContent.Resources = tray.Resources;
        tray.Content = null;
        StudioViewChecks.Layout(trayContent, 368, 720);
        StudioViewChecks.Render(trayContent, 368, 720, "studio-tray-system-eq");
        Assert.DoesNotContain(StudioViewChecks.Descendants(trayContent).OfType<Button>(), button => button.Tag is int && IsShown(button));
        Assert.False(IsShown(TrayPart<FrameworkElement>(trayContent, "SlotsPanel")), "USER slot chips are hardware-only.");
        var quickPanel = TrayPart<FrameworkElement>(trayContent, "QuickPresetsPanel");
        Assert.True(IsShown(quickPanel), "Quick presets must be visible in System EQ mode.");
        var quickRows = StudioViewChecks.Descendants(quickPanel).OfType<Button>().Where(IsShown).ToArray();
        Assert.Equal(Math.Min(MainViewModel.TrayQuickPresetLimit, model.Presets.Count), quickRows.Length);
        Assert.Equal(model.TrayQuickPresets.Select(preset => preset.Name), quickRows.Select(AutomationProperties.GetName));
        // The EQ switch shows "on" while System EQ is not bypassed.
        var eqToggle = TrayPart<ToggleButton>(trayContent, "EqToggle");
        Assert.True(IsShown(eqToggle));
        Assert.Equal(!model.SystemEqBypassed, eqToggle.IsChecked);
        Assert.Equal(["Ananda Music - Warm Detail", "Competitive FPS"], quickRows.Take(2).Select(AutomationProperties.GetName));
        Assert.DoesNotContain(quickRows, row => AutomationProperties.GetName(row) == "Hidden seventh");
        tray.DisposeFlyout();
        foreach (var preset in extra) model.Presets.Remove(preset);
    }

    /// <summary>Tray tooltip: no doubled "System EQ" prefix, problems survive bypass, 127-char limit.</summary>
    private static void VerifyTrayTips()
    {
        Assert.Equal("WolfEQ — System EQ on Speakers",
            TrayIconService.BuildTip(true, "System EQ on Speakers", "System EQ on Speakers", false, SystemEqState.Active, false, ""));
        Assert.Equal("WolfEQ — System EQ bypassed",
            TrayIconService.BuildTip(true, "", "System EQ bypassed", true, SystemEqState.Active, false, ""));
        Assert.Equal("WolfEQ — System EQ bypassed · Equalizer APO is not installed",
            TrayIconService.BuildTip(true, "", "Equalizer APO is not installed.", true, SystemEqState.NeedsInstall, false, ""));
        Assert.Equal("WolfEQ — System EQ bypassed · Equalizer APO is not installed",
            TrayIconService.BuildTip(true, "", "System EQ bypassed", true, SystemEqState.NeedsInstall, false, ""));
        Assert.Equal("WolfEQ — System EQ bypassed · Peace is also active in Equalizer APO, so two EQs may be applied",
            TrayIconService.BuildTip(true, "", "System EQ bypassed", true, SystemEqState.Active, true, "Peace is also active in Equalizer APO, so two EQs may be applied."));
        Assert.Equal("WolfEQ — Desk K13 connected",
            TrayIconService.BuildTip(false, "Desk K13 connected", "System EQ bypassed", true, SystemEqState.NotSelected, false, ""));
        Assert.Equal("WolfEQ — quick profiles", TrayIconService.BuildTip(false, null, null, false, SystemEqState.NotSelected, false, null));

        var blocked = TrayIconService.BuildTip(true, "", "WolfEQ can't write to the Equalizer APO config folder.", true, SystemEqState.Blocked, true,
            "WolfEQ can't write to the Equalizer APO config folder (Access is denied). Your edits stay in the editor, but they are not reaching Windows. "
            + "Audio enhancements are turned off for Speakers, so Equalizer APO can't process it.");
        Assert.True(blocked.Length <= TrayIconService.MaxTipLength, blocked);
        Assert.EndsWith("…", blocked);
        Assert.StartsWith("WolfEQ — System EQ bypassed · WolfEQ can't write to the Equalizer APO config folder (Access is denied)", blocked);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(blocked, "can't write"));
        Assert.DoesNotContain("System EQ: System EQ", blocked);
    }

    /// <summary>While Install Equalizer APO runs, the card shows a progress bar instead of the button.</summary>
    private static void VerifyInstallProgress(SystemEqTestRig rig)
    {
        var services = rig.Services with
        {
            // Never completes during the check: the card stays in its busy state.
            RunInstallerWithProgress = _ => new TaskCompletionSource<ApoLaunchResult>().Task
        };
        rig.Backend.Status = SystemEqBackendStatus.NotInstalled("Equalizer APO is not installed.");
        var model = new MainViewModel(new LocalPresetLibraryService(rig.LibraryPath + ".progress"), services, rig.SettingsPath + ".progress");
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
        Assert.Equal(SystemEqState.NeedsInstall, model.SystemEqState);

        var settings = new SettingsPopover { DataContext = model };
        StudioViewChecks.Layout(settings, 460, 900);
        settings.DataContext = null;
        foreach (var expander in StudioViewChecks.Descendants(settings).OfType<Expander>())
            expander.IsExpanded = Equals(expander.Header, "Device");
        settings.DataContext = model;
        StudioViewChecks.Layout(settings, 460, 900);
        Assert.True(IsShown((FrameworkElement)settings.FindName("InstallApoButton")));
        Assert.False(IsShown((FrameworkElement)settings.FindName("ApoSetupProgressPanel")));

        model.InstallEqualizerApoCommand.Execute(null);
        model.ApplySystemEqSetupProgress(new ApoSetupProgress(ApoSetupStage.Downloading, 4_500_000, 11_980_366));
        StudioViewChecks.Layout(settings, 460, 900);
        StudioViewChecks.Render(settings, 460, 900, "studio-settings-system-eq-installing");
        Assert.False(IsShown((FrameworkElement)settings.FindName("InstallApoButton")));
        Assert.True(IsShown((FrameworkElement)settings.FindName("ApoSetupProgressPanel")));
        var bar = StudioViewChecks.Descendants(settings).OfType<ProgressBar>().Single(IsShown);
        Assert.False(bar.IsIndeterminate);
        Assert.InRange(bar.Value, 37, 38);
        rig.Backend.Status = SystemEqTestRig.InstalledStatus;
    }

    /// <summary>Device dock dropdown: System EQ first, plugged-in FiiO devices, and Manage devices.</summary>
    private static void VerifyDeviceSwitcher(MainViewModel model)
    {
        model.QuickDeviceScanOverride = () => Task.FromResult<IReadOnlyList<WolfEQ.Services.HidDeviceCandidate>>(
        [
            QuickDeviceSwitchTests.Candidate(@"\\?\hid#k13", 0x0120, "FiiO K13 R2R")
        ]);
        model.RefreshQuickDevicesAsync().GetAwaiter().GetResult();
        Assert.Equal(2, model.QuickDevices.Count);

        var dock = new DeviceDock { DataContext = model };
        StudioViewChecks.Layout(dock, 1586, 80);
        var popup = (Popup)dock.FindName("DevicePopup");
        var content = (FrameworkElement)popup.Child;
        content.DataContext = model;
        StudioViewChecks.Layout(content, 340, 300);
        StudioViewChecks.Render(content, 340, 300, "studio-device-switcher");
        var rows = StudioViewChecks.Descendants(content).OfType<Button>().Where(button => button.CommandParameter is QuickDeviceOption).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.All(rows, row => Assert.Same(model.SwitchToQuickDeviceCommand, row.Command));
        Assert.NotNull(dock.FindName("ManageDevicesButton"));
    }

    /// <summary>True when the element and every visual ancestor are <see cref="Visibility.Visible"/>.</summary>
    /// <summary>Detached tray content has no name scope of its own; find named parts in the visual tree.</summary>
    private static T TrayPart<T>(FrameworkElement root, string name) where T : FrameworkElement
        => StudioViewChecks.Descendants(root).OfType<T>().Single(element => element.Name == name);

    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }
}
