using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WolfEQ.Controls;

/// <summary>
/// AmpUp-style single-thumb slider drawn directly for a compact dark control surface.
/// </summary>
public sealed class StyledSlider : FrameworkElement
{
    private const double TrackHeight = 5.0;
    private const double ThumbRadius = 7.5;
    private const double TrackMargin = 10.0;

    private static readonly Typeface Typeface = new("Bahnschrift");
    private bool _dragging;

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(StyledSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(StyledSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(StyledSlider),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnValueChanged));

    public static readonly DependencyProperty AccentColorProperty =
        DependencyProperty.Register(nameof(AccentColor), typeof(Color), typeof(StyledSlider),
            new FrameworkPropertyMetadata(Color.FromRgb(0xC8, 0x10, 0x2E), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowLabelProperty =
        DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(StyledSlider),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CenterOriginProperty =
        DependencyProperty.Register(nameof(CenterOrigin), typeof(bool), typeof(StyledSlider),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(StyledSlider),
            new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public bool ShowLabel
    {
        get => (bool)GetValue(ShowLabelProperty);
        set => SetValue(ShowLabelProperty, value);
    }

    /// <summary>Draws the filled range outward from the midpoint, useful for balance controls.</summary>
    public bool CenterOrigin
    {
        get => (bool)GetValue(CenterOriginProperty);
        set => SetValue(CenterOriginProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public string Suffix { get; set; } = string.Empty;
    public double Step { get; set; } = 1.0;
    public string LabelFormat { get; set; } = "F0";

    public event EventHandler? ValueChanged;

    private double TrackLeft => TrackMargin;
    private double TrackRight => Math.Max(TrackMargin, ActualWidth - TrackMargin);
    private double TrackWidth => Math.Max(1, TrackRight - TrackLeft);
    private double TrackY => ShowLabel ? 14.0 : ActualHeight / 2;

    public StyledSlider()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (Orientation == Orientation.Vertical)
        {
            DrawVertical(dc);
            return;
        }

        DrawHorizontal(dc);
    }

    private void DrawHorizontal(DrawingContext dc)
    {
        var cy = TrackY;
        var trackTop = cy - TrackHeight / 2;
        var valueX = ValueToX(Value);
        var accentBrush = new SolidColorBrush(AccentColor);
        accentBrush.Freeze();

        dc.DrawRoundedRectangle(GetTrackBrush(), null,
            new Rect(TrackLeft, trackTop, TrackWidth, TrackHeight), 1.5, 1.5);

        var fillStart = CenterOrigin ? ValueToX((Minimum + Maximum) / 2) : TrackLeft;
        var fillLeft = Math.Min(fillStart, valueX);
        var fillWidth = Math.Abs(valueX - fillStart);
        if (fillWidth > 0.5)
        {
            dc.DrawRoundedRectangle(accentBrush, null,
                new Rect(fillLeft, trackTop, fillWidth, TrackHeight), TrackHeight / 2, TrackHeight / 2);
        }

        if (IsMouseOver || _dragging || IsKeyboardFocused)
        {
            var halo = new SolidColorBrush(Color.FromArgb(0x28, AccentColor.R, AccentColor.G, AccentColor.B));
            halo.Freeze();
            dc.DrawEllipse(halo, null, new Point(valueX, cy), ThumbRadius + 4, ThumbRadius + 4);
        }

        var thumbPen = new Pen(accentBrush, 2);
        thumbPen.Freeze();
        dc.DrawEllipse(GetThumbFillBrush(), thumbPen, new Point(valueX, cy), ThumbRadius, ThumbRadius);

        dc.DrawEllipse(accentBrush, null, new Point(valueX, cy), 2.2, 2.2);

        if (ShowLabel)
        {
            var textBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            textBrush.Freeze();
            var text = new FormattedText($"{Value.ToString(LabelFormat, CultureInfo.InvariantCulture)}{Suffix}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface,
                10,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(valueX - text.Width / 2, cy + ThumbRadius + 4));
        }
    }

    private void DrawVertical(DrawingContext dc)
    {
        var cx = ActualWidth / 2;
        var trackTop = TrackMargin;
        var trackBottom = Math.Max(TrackMargin, ActualHeight - TrackMargin);
        var trackHeight = Math.Max(1, trackBottom - trackTop);
        var valueY = ValueToY(Value, trackTop, trackHeight);
        var accentBrush = new SolidColorBrush(AccentColor);
        accentBrush.Freeze();

        dc.DrawRoundedRectangle(GetTrackBrush(), null,
            new Rect(cx - TrackHeight / 2, trackTop, TrackHeight, trackHeight), 1.5, 1.5);

        if (valueY < trackBottom)
        {
            dc.DrawRoundedRectangle(accentBrush, null,
                new Rect(cx - TrackHeight / 2, valueY, TrackHeight, trackBottom - valueY), 1.5, 1.5);
        }

        var thumbPen = new Pen(accentBrush, 2);
        thumbPen.Freeze();
        dc.DrawEllipse(GetThumbFillBrush(), thumbPen, new Point(cx, valueY), ThumbRadius, ThumbRadius);

        dc.DrawEllipse(accentBrush, null, new Point(cx, valueY), 2.2, 2.2);

        if (ShowLabel)
        {
            var textBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            textBrush.Freeze();
            var text = new FormattedText($"{Value.ToString(LabelFormat, CultureInfo.InvariantCulture)}{Suffix}",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface,
                10,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(cx - text.Width / 2, Math.Min(ActualHeight - text.Height, valueY + ThumbRadius + 4)));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        Value = PositionToValue(e.GetPosition(this));
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        Value = PositionToValue(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var handled = true;
        Value = e.Key switch
        {
            Key.Left or Key.Down => SnapValue(Value - Step),
            Key.Right or Key.Up => SnapValue(Value + Step),
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => Value
        };
        handled = e.Key is Key.Left or Key.Down or Key.Right or Key.Up or Key.Home or Key.End;
        e.Handled = handled;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Orientation == Orientation.Vertical)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 28 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 180 : availableSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, ShowLabel ? 40 : 34);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StyledSlider)d).ValueChanged?.Invoke(d, EventArgs.Empty);
    }

    private double ValueToX(double value)
    {
        var range = Maximum - Minimum;
        if (range <= 0)
        {
            return TrackLeft;
        }

        var ratio = (value - Minimum) / range;
        return TrackLeft + ratio * TrackWidth;
    }

    private double XToValue(double x)
    {
        var ratio = Math.Clamp((x - TrackLeft) / TrackWidth, 0, 1);
        return Minimum + ratio * (Maximum - Minimum);
    }

    private double ValueToY(double value, double trackTop, double trackHeight)
    {
        var range = Maximum - Minimum;
        if (range <= 0)
        {
            return trackTop + trackHeight;
        }

        var ratio = (value - Minimum) / range;
        return trackTop + (1 - ratio) * trackHeight;
    }

    private double YToValue(double y)
    {
        var trackTop = TrackMargin;
        var trackBottom = Math.Max(TrackMargin, ActualHeight - TrackMargin);
        var trackHeight = Math.Max(1, trackBottom - trackTop);
        var ratio = Math.Clamp((trackBottom - y) / trackHeight, 0, 1);
        return Minimum + ratio * (Maximum - Minimum);
    }

    private double PositionToValue(Point point)
        => SnapValue(Orientation == Orientation.Vertical ? YToValue(point.Y) : XToValue(point.X));

    private double SnapValue(double value)
    {
        value = Math.Clamp(value, Minimum, Maximum);
        if (Step > 0)
        {
            value = Math.Round(value / Step) * Step;
        }

        return Math.Clamp(value, Minimum, Maximum);
    }

    private static Brush GetTrackBrush()
        => (Brush)Application.Current.FindResource("WolfLineSoftBrush");

    private static Brush GetThumbFillBrush()
        => (Brush)Application.Current.FindResource("WolfShellBrush");
}
