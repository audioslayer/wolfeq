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
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Settings render timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
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
