using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WolfEQ.Models;

namespace WolfEQ.Views;

/// <summary>
/// Primary EQ editing surface: interactive response graph, selected-band strip,
/// band chips, the app's single preamp control, Headroom Guardian summary, and
/// the Tools menu. Expects the application's MainViewModel as DataContext.
/// Band selection lives on the graph's <c>SelectedBand</c> dependency property.
/// </summary>
public partial class EditorWorkspace : UserControl
{
    public EditorWorkspace()
    {
        InitializeComponent();
    }

    private void BandChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EqBand band })
        {
            Graph.SelectedBand = band;
            Graph.Focus();
        }
    }

    private void ToolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
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

        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var typed))
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
