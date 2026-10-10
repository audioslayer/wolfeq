using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WolfEQ.ViewModels;
using WolfEQ.Views;
using Xunit;

namespace WolfEQ.Tests;

public sealed class IntegrationSettingsTests
{
    [Fact]
    public void IntegrationPanelRendersSlotCommandAndInstructions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                var model = new MainViewModel { IntegrationSlot = 3 };
                Assert.Equal("AmpUp Green", model.SelectedAccentColorOption.Name);
                Assert.Equal((Color)ColorConverter.ConvertFromString("#00E676"), ((SolidColorBrush)app.FindResource("WolfGreenBrush")).Color);
                var view = new SettingsPopover { DataContext = model, Height = 1000 };
                view.Measure(new Size(460, 1000));
                view.Arrange(new Rect(0, 0, 460, 1000));
                view.UpdateLayout();
                var integration = Descendants(view).OfType<Expander>().Single(e => Equals(e.Header, "Integrations"));
                integration.IsExpanded = true;
                view.UpdateLayout();
                var setup = Descendants(integration).OfType<Expander>().Single();
                setup.IsExpanded = true;
                view.UpdateLayout();
                var command = Descendants(integration).OfType<TextBox>().Single();
                Assert.Contains("--slot 3", command.Text);
                Assert.Contains(Descendants(integration).OfType<TextBlock>(), t => t.Text.Contains("System > Open"));
                Assert.Contains(Descendants(integration).OfType<Button>(), b => Equals(b.Content, "Save shortcut..."));
                var bitmap = new RenderTargetBitmap(460, 1000, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "integration-settings.png"));
                encoder.Save(output);
                integration.IsExpanded = false;
                var devices = Descendants(view).OfType<Expander>().Single(e => Equals(e.Header, "Device"));
                devices.IsExpanded = true;
                model.UsbDevices.Clear();
                model.UsbDevices.Add(new UsbDeviceChoice(@"\\?\hid#vid_2972&pid_0120#test", "FiiO K13 R2R"));
                model.SelectedUsbDevice = model.UsbDevices[0];
                model.BluetoothDevices.Clear();
                model.BluetoothDevices.Add(new WolfEQ.Services.BleDeviceChoice(0xAABBCC112233, "FiiO K13 R2R", -48));
                model.SelectedBluetoothDevice = model.BluetoothDevices[0];
                view.UpdateLayout();
                Assert.DoesNotContain("hid#", model.SelectedUsbDevice.ToString());
                Assert.DoesNotContain("UsbDeviceChoice", model.SelectedUsbDevice.ToString());
                Assert.Contains(Descendants(devices).OfType<TextBlock>(), t => t.Text == model.SelectedUsbDevice.DisplayName);
                var deviceBitmap = new RenderTargetBitmap(460, 1000, 96, 96, PixelFormats.Pbgra32);
                deviceBitmap.Render(view);
                var deviceEncoder = new PngBitmapEncoder();
                deviceEncoder.Frames.Add(BitmapFrame.Create(deviceBitmap));
                using var deviceOutput = File.Create(Path.Combine(AppContext.BaseDirectory, "device-settings.png"));
                deviceEncoder.Save(deviceOutput);
                var tabs = Descendants(devices).OfType<TabControl>().Single();
                tabs.SelectedIndex = 1;
                view.UpdateLayout();
                Assert.Contains(Descendants(devices).OfType<TextBlock>(), t => t.Text == "Input");
                Assert.Contains(Descendants(devices).OfType<TextBlock>(), t => t.Text == "Volume limit");
                var controlsBitmap = new RenderTargetBitmap(460, 1000, 96, 96, PixelFormats.Pbgra32);
                controlsBitmap.Render(view);
                var controlsEncoder = new PngBitmapEncoder();
                controlsEncoder.Frames.Add(BitmapFrame.Create(controlsBitmap));
                using var controlsOutput = File.Create(Path.Combine(AppContext.BaseDirectory, "device-controls-settings.png"));
                controlsEncoder.Save(controlsOutput);
                VerifyTrayFlyout(model);
                BandControlsViewChecks.Verify(model);
                StudioViewChecks.Verify(model);
                SystemEqViewChecks.Verify();
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Settings render timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyTrayFlyout(MainViewModel model)
    {
        var opened = false;
        var settingsOpened = false;
        var quit = false;
        var flyout = new TrayFlyout(model, () => opened = true, () => settingsOpened = true, () => quit = true);
        var content = (FrameworkElement)flyout.Content;
        // Render the complete surface independently of an unshown Window's native
        // client-area clip. Keep the same data and local control resources.
        flyout.Content = null;
        content.DataContext = flyout.DataContext;
        content.Resources = flyout.Resources;
        System.Windows.Documents.TextElement.SetFontFamily(content, flyout.FontFamily);
        System.Windows.Documents.TextElement.SetForeground(content, flyout.Foreground);
        content.Measure(new Size(368, 1200));
        content.Arrange(new Rect(0, 0, 368, content.DesiredSize.Height));
        content.UpdateLayout();
        var buttons = Descendants(content).OfType<Button>().ToArray();
        var slots = buttons.Where(b => b.Tag is int).ToArray();
        Assert.Equal(model.DeviceUserPresets.Count, slots.Length);
        model.EditorSession.NotifyEdit();
        Assert.All(slots, b => Assert.False(b.IsEnabled));
        model.EditorSession.NotifySavedToLibrary();
        Assert.All(slots, b => Assert.True(b.IsEnabled));
        Assert.Contains(Descendants(content).OfType<TextBlock>(), t => t.Text == model.AppVersionText);
        Assert.Contains(Descendants(content).OfType<CheckBox>(), c => Equals(c.Content, "Close to tray"));
        Assert.Contains(Descendants(content).OfType<CheckBox>(), c => Equals(c.Content, "Minimize to tray"));

        // State notifications can invalidate the ScrollViewer measure after the
        // first arrange. Finish layout before capturing its complete surface.
        content.Measure(new Size(368, 1200));
        content.Arrange(new Rect(0, 0, 368, content.DesiredSize.Height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(368, (int)Math.Ceiling(content.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "tray-flyout.png"))) encoder.Save(output);

        buttons.Single(b => Equals(b.Content, "Open editor")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        buttons.Single(b => Equals(b.Content, "Settings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        buttons.Single(b => Equals(b.Content, "Quit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(opened);
        Assert.True(settingsOpened);
        Assert.True(quit);
        flyout.Close();
        // Alt+F4 dismisses the reusable popup; final disposal must remain possible.
        flyout.DisposeFlyout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
