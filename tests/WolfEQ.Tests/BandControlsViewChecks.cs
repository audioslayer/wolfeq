using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WolfEQ.Controls;
using WolfEQ.Models;
using WolfEQ.ViewModels;
using WolfEQ.Views;
using Xunit;

namespace WolfEQ.Tests;

// Runs within the existing STA application fixture so WPF has one Application.
internal static class BandControlsViewChecks
{
    public static void Verify(MainViewModel model)
    {
        var workspace = new EditorWorkspace { DataContext = model };
        void Layout()
        {
            workspace.Measure(new Size(1200, 650));
            workspace.Arrange(new Rect(0, 0, 1200, 650));
            workspace.UpdateLayout();
        }
        Layout();
        var controls = Descendants(workspace).OfType<BandControls>().Single();
        var expander = (Expander)controls.FindName("ControlsExpander");
        var tabs = (TabControl)controls.FindName("ViewTabs");
        var graph = Descendants(workspace).OfType<EqResponseGraphControl>().Single();
        Assert.False(expander.IsExpanded);
        expander.IsExpanded = true;
        Layout();
        var divider = (GridSplitter)workspace.FindName("BandDivider");
        Assert.Equal(Visibility.Visible, divider.Visibility);
        var bandsRow = (RowDefinition)workspace.FindName("BandsRow");
        var initialBandHeight = bandsRow.ActualHeight;
        var initialGraphHeight = graph.ActualHeight;
        bandsRow.Height = new GridLength(200);
        Layout();
        Assert.True(graph.ActualHeight > initialGraphHeight);
        Assert.True(controls.ActualHeight < initialBandHeight);
        expander.IsExpanded = false;
        Layout();
        Assert.Equal(Visibility.Collapsed, divider.Visibility);
        expander.IsExpanded = true;
        Layout();
        Assert.Equal(200, bandsRow.ActualHeight, 1);
        bandsRow.Height = new GridLength(initialBandHeight);
        Layout();
        Assert.Equal(2, tabs.Items.Count);
        Assert.Equal("_Faders", ((TabItem)tabs.SelectedItem).Header);
        tabs.SelectedIndex = 1;
        Layout();
        var first = model.Bands.OrderBy(b => b.FrequencyHz).ThenBy(b => b.Number).First();
        TextBox Box(string tag) => Descendants(controls).OfType<TextBox>().First(b => Equals(b.Tag, tag) && ReferenceEquals(b.DataContext, first));
        void Enter(string tag, string text)
        {
            var box = Box(tag);
            box.Text = text;
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }
        Enter("Gain", "999");
        Assert.Equal(model.SelectedDeviceProfile.MaxGainDb, first.GainDb);
        Enter("Gain", "NaN");
        Assert.Equal(model.SelectedDeviceProfile.MaxGainDb, first.GainDb);
        Enter("Gain", "-3");
        Assert.Equal(-3, first.GainDb);
        Assert.True(model.CanUndo);
        model.UndoCommand.Execute(null);
        Layout();
        first = model.Bands.OrderBy(b => b.FrequencyHz).ThenBy(b => b.Number).First();
        Assert.NotEqual(-3, first.GainDb);
        Enter("Q", "0");
        Assert.Equal(model.SelectedDeviceProfile.MinQ, first.Q);
        Enter("Q", "1.2");
        Enter("Gain", "-3");
        Descendants(controls).OfType<Button>().First(b => ReferenceEquals(b.DataContext, first)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(first, graph.SelectedBand);
        var hardwareNumber = first.Number;
        Enter("Frequency", "19000");
        // This unshown STA view has no message loop to execute WPF's deferred live sort.
        var ordered = (System.Windows.Data.CollectionViewSource)controls.Resources["OrderedBands"];
        Assert.True(ordered.IsLiveSorting);
        ordered.View.Refresh();
        Layout();
        Assert.Equal(hardwareNumber, first.Number);
        Assert.Equal(model.Bands.Count, graph.SelectedBandDisplayNumber);
        Assert.Equal($"Band {model.Bands.Count}", Descendants(controls).OfType<Button>().First(b => ReferenceEquals(b.DataContext, first)).Content);
        Enter("Frequency", "31");
        ordered.View.Refresh();
        Layout();
        var enabled = Descendants(controls).OfType<CheckBox>().First(c => ReferenceEquals(c.DataContext, first));
        enabled.IsChecked = false;
        Assert.False(first.Enabled);
        Assert.Contains("9 enabled", ((TextBlock)controls.FindName("Summary")).Text);
        enabled.IsChecked = true;

        for (var index = 0; index < 2; index++)
        {
            tabs.SelectedIndex = index;
            Layout();
            Assert.Equal(model.Bands.Count, Descendants(controls).OfType<CheckBox>().Count());
            Assert.StartsWith("-3.0", Box("Gain").Text);
            if (index == 0)
            {
                var bypass = Descendants(controls).OfType<CheckBox>().First(c => ReferenceEquals(c.DataContext, first));
                Assert.False(bypass.IsChecked);
                bypass.IsChecked = true;
                Assert.False(first.Enabled);
                bypass.IsChecked = false;
                Assert.True(first.Enabled);
                Enter("Gain", "-3.0 dB");
                Assert.Equal(-3, first.GainDb);
                Enter("Q", "2.4");
                Assert.Equal(2.4, first.Q);
                Enter("Q", "999");
                Assert.Equal(model.SelectedDeviceProfile.MaxQ, first.Q);
                Enter("Q", "1.2");
                Enter("Frequency", "38");
                Assert.Equal(38, first.FrequencyHz);
                var filter = Descendants(controls).OfType<ComboBox>().First(c => ReferenceEquals(c.DataContext, first));
                Assert.Equal(model.SelectedDeviceProfile.SupportedFilterTypes.Count, filter.Items.Count);
                filter.SelectedItem = EqFilterType.LowShelf;
                Assert.Equal(EqFilterType.LowShelf, first.FilterType);
                var slider = Descendants(controls).OfType<Slider>().First(s => ReferenceEquals(s.DataContext, first));
                Assert.Equal(model.SelectedDeviceProfile.MinGainDb, slider.Minimum);
                Assert.Equal(model.SelectedDeviceProfile.MaxGainDb, slider.Maximum);
                slider.Value = -2;
                Assert.Equal(-2, first.GainDb);
                first.FilterType = EqFilterType.HighPass;
                Assert.False(slider.IsEnabled);
                first.FilterType = EqFilterType.Peak;
                first.GainDb = -3;
            }
            var bitmap = new RenderTargetBitmap(1200, 650, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(workspace);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, $"eq-band-view-{index}.png"));
            encoder.Save(output);
        }
        var before = graph.ActualHeight;
        expander.IsExpanded = false;
        Layout();
        Assert.True(graph.ActualHeight > before);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
