using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WolfEQ.Models;
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

        var dock = new DeviceDock { DataContext = model };
        StudioViewChecks.Layout(dock, 1586, 80);
        StudioViewChecks.Render(dock, 1586, 80, "studio-dock-system-eq");
        Assert.False(IsShown((FrameworkElement)dock.FindName("SlotMenuButton")));
        Assert.False(IsShown((FrameworkElement)dock.FindName("WriteToDeviceButton")));
        Assert.True(IsShown((FrameworkElement)dock.FindName("SystemEqDockPanel")));

        var tray = new TrayFlyout(model, () => { }, () => { }, () => { });
        var trayContent = (FrameworkElement)tray.Content;
        trayContent.DataContext = tray.DataContext;
        trayContent.Resources = tray.Resources;
        tray.Content = null;
        StudioViewChecks.Layout(trayContent, 368, 720);
        StudioViewChecks.Render(trayContent, 368, 720, "studio-tray-system-eq");
        Assert.DoesNotContain(StudioViewChecks.Descendants(trayContent).OfType<Button>(), button => button.Tag is int && IsShown(button));
        tray.DisposeFlyout();
    }

    /// <summary>True when the element and every visual ancestor are <see cref="Visibility.Visible"/>.</summary>
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
