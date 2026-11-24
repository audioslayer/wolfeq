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
        Assert.Equal(3, tabs.Items.Count);
        Assert.Equal("_Knobs", ((TabItem)tabs.SelectedItem).Header);
        Assert.Equal(["Knobs", "Faders", "Table"], Descendants(controls).OfType<RadioButton>().Select(b => (string)b.Content));
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
        VerifyKnobs();
        var before = graph.ActualHeight;
        expander.IsExpanded = false;
        Layout();
        Assert.True(graph.ActualHeight > before);

        void VerifyKnobs()
        {
            // Opening the view must not touch any value (no clamping or rounding on load).
            string Snapshot() => string.Join("|", model.Bands.Select(b => $"{b.Number},{b.FrequencyHz},{b.GainDb},{b.Q},{b.FilterType},{b.Enabled}"));
            var values = Snapshot();
            tabs.SelectedIndex = 2;
            Layout();
            Assert.Equal(values, Snapshot());
            Assert.Equal(model.Bands.Count * 3, Descendants(controls).OfType<BandKnob>().Count());
            BandKnob Knob(string tag) => Descendants(controls).OfType<BandKnob>().First(k => Equals(k.Tag, tag) && ReferenceEquals(k.DataContext, first));
            Assert.Equal(0.5, Knob("Gain").PositionOf(0), 6);
            foreach (var gain in new[] { -24.0, -7.3, 0, 4.2, 12 })
                Assert.Equal(gain, Knob("Gain").ValueAt(Knob("Gain").PositionOf(gain)), 6);
            Knob("Gain").MoveTo(999);
            Assert.Equal(model.SelectedDeviceProfile.MaxGainDb, first.GainDb);
            Knob("Gain").MoveTo(-3);
            Assert.Equal(-3, first.GainDb);
            Knob("Q").MoveTo(1.5);
            Assert.Equal(1.5, first.Q);
            Knob("Frequency").MoveTo(37.4);
            Assert.Equal(37, first.FrequencyHz);
            first.FilterType = EqFilterType.HighPass;
            Layout();
            Assert.False(Knob("Gain").IsEnabled);
            Knob("Gain").MoveTo(5);
            Assert.Equal(-3, first.GainDb);
            first.FilterType = EqFilterType.Peak;
            Layout();
            // The first tile wears the first palette colour, like its node on the graph.
            var light = BandPalette.IsLight(Application.Current.TryFindResource("WolfBackgroundMode"));
            Assert.Equal(BandPalette.ColorFor(0, model.Bands.Count, light), ((SolidColorBrush)Knob("Gain").AccentBrush).Color);
            // Passing a neighbour's frequency must not swap tiles until the knobs are let go.
            var hold = typeof(BandControls).GetMethod("HoldKnobOrder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            EqBand[] Order() => Descendants(controls).OfType<BandKnob>().Where(k => Equals(k.Tag, "Gain"))
                .OrderBy(k => k.TranslatePoint(new Point(), controls).X).Select(k => (EqBand)k.DataContext).ToArray();
            var orderBefore = Order();
            var frequencyKnob = Knob("Frequency");
            hold.Invoke(controls, [true]);
            frequencyKnob.MoveTo(20000);
            Layout();
            Assert.Equal(orderBefore, Order());
            Assert.Same(frequencyKnob, Knob("Frequency"));
            hold.Invoke(controls, [false]);
            Layout();
            Assert.Same(first, Order()[^1]);
            Knob("Frequency").MoveTo(37);
            hold.Invoke(controls, [false]);
            Layout();
            Assert.Equal(orderBefore, Order());
            // The custom pointer must really load, not fall back to the hand.
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                Assert.NotSame(System.Windows.Input.Cursors.Hand, KnobCursor.For(Colors.DeepSkyBlue, scale));
            // Turning up raises the value, turning down lowers it, and Shift moves much less.
            var turned = new BandKnob { Minimum = 0, Maximum = 100, Step = 0.01 };
            turned.Turn(20, fine: false);
            var coarse = turned.Value;
            Assert.True(coarse > 0);
            turned.Turn(20, fine: true);
            Assert.InRange(turned.Value - coarse, 0.01, coarse / 3);
            turned.Turn(-500, fine: false);
            Assert.Equal(0, turned.Value);
            Knob("Gain").Glow = 1;
            Knob("Q").Glow = 1;
            Knob("Gain").Absorb = 1;
            typeof(EqResponseGraphControl).GetField("_dragBand", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(graph, first);
            typeof(EqResponseGraphControl).GetField("_flashBand", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(graph, first);
            graph.GrabFlash = 0.45;
            typeof(EqResponseGraphControl).GetField("_widthBand", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(graph, first);
            graph.WidthGlow = 1;
            graph.InvalidateVisual();
            Knob("Gain").Impact = 0.45;
            Knob("Gain").Trail = 0.75;
            Knob("Frequency").Absorb = 0.5;
            Layout();
            var bitmap = new RenderTargetBitmap(1800, 975, 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(workspace);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "eq-band-view-knobs.png"));

            encoder.Save(output);
        }
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
