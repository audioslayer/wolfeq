using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WolfEQ.Models;

namespace WolfEQ.Views;

/// <summary>
/// Primary graph-native EQ editing surface. Band selection lives on the graph's
/// <c>SelectedBand</c> dependency property and the precision editor sits below the
/// response curve. Expects the application's MainViewModel as DataContext.
/// </summary>
public partial class EditorWorkspace : UserControl
{
    public EditorWorkspace()
    {
        InitializeComponent();
        InitializeSpectrum();
        BandPanel.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler(BandExpansionChanged));
        BandPanel.AddHandler(Expander.CollapsedEvent, new RoutedEventHandler(BandExpansionChanged));
        WorkspaceGrid.SizeChanged += (_, _) => ConstrainBandHeight();
        BandExpansionChanged(this, new RoutedEventArgs());
        Loaded += (_, _) => Graph.SelectedBand ??= Graph.Bands?.OrderBy(b => b.FrequencyHz).ThenBy(b => b.Number).FirstOrDefault();
    }

    private void PreviousBand_Click(object sender, RoutedEventArgs e) => SelectAdjacentBand(-1);
    private void NextBand_Click(object sender, RoutedEventArgs e) => SelectAdjacentBand(1);
    private void SelectAdjacentBand(int direction)
    {
        var bands = Graph.Bands?.OrderBy(b => b.FrequencyHz).ThenBy(b => b.Number).ToList();
        if (bands is not { Count: > 0 }) return;
        var index = Graph.SelectedBand is null ? 0 : bands.IndexOf(Graph.SelectedBand);
        Graph.SelectedBand = bands[(index + direction + bands.Count) % bands.Count];
    }

    private void DismissBandEditor_Click(object sender, RoutedEventArgs e)
    {
        Graph.SelectedBand = null;
        Graph.Focus();
        e.Handled = true;
    }

    private void ToolMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ToolsPopup.IsOpen = false;
    }

    private void ToolsPopup_Closed(object? sender, EventArgs e)
    {
        ToolsButton.IsChecked = false;
    }

    private void StripValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;

        if (e.Key is Key.Enter or Key.Return)
        {
            CommitStripValue(box);
            Graph.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            Graph.Focus();
            e.Handled = true;
        }
    }

    /// <summary>− / + buttons beside a strip value: step the band in that field.</summary>
    private void StripStepButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string dirText, Parent: Panel panel }
            || !int.TryParse(dirText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direction))
        {
            return;
        }

        var box = panel.Children.OfType<TextBox>().FirstOrDefault();
        if (box is not null)
        {
            StepStripValue(box, direction, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
    }

    // Up/Down arrows nudge the value (Shift for bigger steps) instead of moving the caret.
    private void StripValueBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox box && e.Key is Key.Up or Key.Down)
        {
            StepStripValue(box, e.Key == Key.Up ? 1 : -1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            box.SelectAll();
            e.Handled = true;
        }
    }

    // The wheel only steps a box the user is editing, so scrolling the page never changes a value.
    private void StripValueBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is TextBox { IsKeyboardFocused: true } box && e.Delta != 0)
        {
            StepStripValue(box, e.Delta > 0 ? 1 : -1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Steps the selected band's field: frequency by 1/12 octave (1/3 with Shift), gain by
    /// 0.1 dB (1 dB), Q by 0.01 (0.1). Typed-but-uncommitted text is committed first, and the
    /// result is clamped to the device profile's range like a typed value.
    /// </summary>
    private void StepStripValue(TextBox box, int direction, bool large)
    {
        if (Graph.SelectedBand is not EqBand band)
        {
            return;
        }

        if (box.IsKeyboardFocused)
        {
            CommitStripValue(box);
        }

        switch (box.Tag as string)
        {
            case "Frequency":
            {
                var next = (int)Math.Round(band.FrequencyHz * Math.Pow(2, direction * (large ? 1.0 / 3 : 1.0 / 12)));
                if (next == band.FrequencyHz) next += direction;
                band.FrequencyHz = (int)Math.Round(Math.Clamp(next, Graph.MinFrequencyHz, Graph.MaxFrequencyHz));
                break;
            }
            case "Gain":
                band.GainDb = Math.Clamp(Math.Round(band.GainDb + direction * (large ? 1.0 : 0.1), 1), Graph.MinGainDb, Graph.MaxGainDb);
                break;
            case "Q":
                band.Q = Math.Clamp(Math.Round(band.Q + direction * (large ? 0.1 : 0.01), 2), Graph.MinQ, Graph.MaxQ);
                break;
        }

        box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
    }

    private void StripValueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            CommitStripValue(box);
        }
    }

    /// <summary>
    /// Commits a typed strip value: numeric input is clamped to the active device
    /// profile's range (with a brief highlight when clamping occurred); non-numeric
    /// input reverts the box to the band's current value.
    /// </summary>
    private void CommitStripValue(TextBox box)
    {
        var expression = box.GetBindingExpression(TextBox.TextProperty);
        if (expression is null) return;

        if (Graph.SelectedBand is not EqBand band)
        {
            expression.UpdateTarget();
            return;
        }

        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var typed) || !double.IsFinite(typed))
        {
            // Non-numeric input: revert to the previous value.
            expression.UpdateTarget();
            return;
        }

        var clamped = false;
        switch (box.Tag as string)
        {
            case "Frequency":
            {
                var target = (int)Math.Round(Math.Clamp(typed, Graph.MinFrequencyHz, Graph.MaxFrequencyHz));
                band.FrequencyHz = target;
                clamped = Math.Abs(band.FrequencyHz - Math.Round(typed)) > 0.5;
                break;
            }
            case "Gain":
            {
                band.GainDb = Math.Clamp(typed, Graph.MinGainDb, Graph.MaxGainDb);
                clamped = Math.Abs(band.GainDb - Math.Round(typed, 1)) > 0.001;
                break;
            }
            case "Q":
            {
                band.Q = Math.Clamp(typed, Graph.MinQ, Graph.MaxQ);
                clamped = Math.Abs(band.Q - Math.Round(typed, 2)) > 0.001;
                break;
            }
        }

        expression.UpdateTarget();

        if (clamped)
        {
            FlashClampHighlight(box);
        }
    }

    /// <summary>Briefly tints the text box with the accent color to signal a clamped value.</summary>
    private void FlashClampHighlight(TextBox box)
    {
        var accent = TryFindResource("WolfGreen") is Color color ? color : Colors.LimeGreen;
        var flash = Color.FromArgb(0x59, accent.R, accent.G, accent.B);

        var brush = new SolidColorBrush(flash);
        box.Background = brush;

        var animation = new ColorAnimation(flash, Colors.Transparent, TimeSpan.FromMilliseconds(320))
        {
            BeginTime = TimeSpan.FromMilliseconds(80)
        };
        animation.Completed += (_, _) => box.ClearValue(BackgroundProperty);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
}
