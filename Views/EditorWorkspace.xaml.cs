using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WolfEQ.Models;

namespace WolfEQ.Views;

/// <summary>
/// Primary graph-native EQ editing surface. Band selection lives on the graph's
/// <c>SelectedBand</c> dependency property and the precision editor follows the
/// selected node. Expects the application's MainViewModel as DataContext.
/// </summary>
public partial class EditorWorkspace : UserControl
{
    private bool _bandEditorPositionUpdatePending;

    public EditorWorkspace()
    {
        InitializeComponent();
        Graph.SelectedBandAnchorChanged += (_, _) => RequestBandEditorPositionUpdate();
        Graph.SizeChanged += (_, _) => RequestBandEditorPositionUpdate();
        BandEditorCapsule.SizeChanged += (_, _) => RequestBandEditorPositionUpdate();
        Loaded += (_, _) => RequestBandEditorPositionUpdate();
    }

    private void RequestBandEditorPositionUpdate()
    {
        if (!IsLoaded || _bandEditorPositionUpdatePending)
        {
            return;
        }

        _bandEditorPositionUpdatePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _bandEditorPositionUpdatePending = false;
            UpdateBandEditorPosition();
        }));
    }

    private void UpdateBandEditorPosition()
    {
        if (!Graph.TryGetSelectedBandAnchor(out var anchor) ||
            BandEditorCanvas.ActualWidth <= 1 ||
            BandEditorCanvas.ActualHeight <= 1)
        {
            return;
        }

        var capsuleWidth = BandEditorCapsule.ActualWidth > 1 ? BandEditorCapsule.ActualWidth : BandEditorCapsule.Width;
        var capsuleHeight = BandEditorCapsule.ActualHeight > 1 ? BandEditorCapsule.ActualHeight : BandEditorCapsule.Height;
        const double edgePadding = 12;
        const double nodeClearance = 34;

        var maxLeft = Math.Max(edgePadding, BandEditorCanvas.ActualWidth - capsuleWidth - edgePadding);
        var left = Math.Clamp(anchor.X - capsuleWidth / 2, edgePadding, maxLeft);
        var belowTop = anchor.Y + nodeClearance;
        var placeBelow = belowTop + capsuleHeight <= BandEditorCanvas.ActualHeight - edgePadding;
        var top = placeBelow
            ? belowTop
            : Math.Max(edgePadding, anchor.Y - nodeClearance - capsuleHeight);

        Canvas.SetLeft(BandEditorCapsule, left);
        Canvas.SetTop(BandEditorCapsule, top);

        var stemTop = placeBelow ? anchor.Y + 15 : top + capsuleHeight;
        var stemBottom = placeBelow ? top : anchor.Y - 15;
        Canvas.SetLeft(CapsuleStem, anchor.X - CapsuleStem.Width / 2);
        Canvas.SetTop(CapsuleStem, Math.Min(stemTop, stemBottom));
        CapsuleStem.Height = Math.Max(0, Math.Abs(stemBottom - stemTop));
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
