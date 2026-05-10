using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WolfEQ.Models;

namespace WolfEQ.Controls;

public sealed class EqResponseGraphControl : FrameworkElement
{
    private const double DotRadius = 5.5;
    private const double DotHitRadius = 14.0;
    private EqBand? _dragBand;
    private EqBand? _hoverBand;

    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands),
        typeof(ObservableCollection<EqBand>),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnBandsChanged));

    public static readonly DependencyProperty PreampDbProperty = DependencyProperty.Register(
        nameof(PreampDb),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CompareBandsProperty = DependencyProperty.Register(
        nameof(CompareBands),
        typeof(ObservableCollection<EqBand>),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnCompareBandsChanged));

    public static readonly DependencyProperty ComparePreampDbProperty = DependencyProperty.Register(
        nameof(ComparePreampDb),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive),
        typeof(bool),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnIsInteractiveChanged));

    public ObservableCollection<EqBand>? Bands
    {
        get => (ObservableCollection<EqBand>?)GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    public double PreampDb
    {
        get => (double)GetValue(PreampDbProperty);
        set => SetValue(PreampDbProperty, value);
    }

    public ObservableCollection<EqBand>? CompareBands
    {
        get => (ObservableCollection<EqBand>?)GetValue(CompareBandsProperty);
        set => SetValue(CompareBandsProperty, value);
    }

    public double ComparePreampDb
    {
        get => (double)GetValue(ComparePreampDbProperty);
        set => SetValue(ComparePreampDbProperty, value);
    }

    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public EqResponseGraphControl()
    {
        MinHeight = 250;
        Focusable = true;
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    private static void OnIsInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EqResponseGraphControl)d;
        if (e.NewValue is false)
        {
            control._dragBand = null;
            control._hoverBand = null;
            if (control.IsMouseCaptured)
            {
                control.ReleaseMouseCapture();
            }
        }

        control.Cursor = control.IsInteractive ? Cursors.Hand : Cursors.Arrow;
    }

    private static void OnBandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EqResponseGraphControl)d;
        if (e.OldValue is ObservableCollection<EqBand> oldBands)
        {
            oldBands.CollectionChanged -= control.OnBandsCollectionChanged;
            foreach (var band in oldBands)
            {
                band.PropertyChanged -= control.OnBandChanged;
            }
        }

        if (e.NewValue is ObservableCollection<EqBand> newBands)
        {
            newBands.CollectionChanged += control.OnBandsCollectionChanged;
            foreach (var band in newBands)
            {
                band.PropertyChanged += control.OnBandChanged;
            }
        }

        control.InvalidateVisual();
    }

    private static void OnCompareBandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EqResponseGraphControl)d;
        if (e.OldValue is ObservableCollection<EqBand> oldBands)
        {
            oldBands.CollectionChanged -= control.OnCompareBandsCollectionChanged;
            foreach (var band in oldBands)
            {
                band.PropertyChanged -= control.OnCompareBandChanged;
            }
        }

        if (e.NewValue is ObservableCollection<EqBand> newBands)
        {
            newBands.CollectionChanged += control.OnCompareBandsCollectionChanged;
            foreach (var band in newBands)
            {
                band.PropertyChanged += control.OnCompareBandChanged;
            }
        }

        control.InvalidateVisual();
    }

    private void OnBandsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (EqBand band in e.OldItems)
            {
                band.PropertyChanged -= OnBandChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (EqBand band in e.NewItems)
            {
                band.PropertyChanged += OnBandChanged;
            }
        }

        InvalidateVisual();
    }

    private void OnBandChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void OnCompareBandsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (EqBand band in e.OldItems)
            {
                band.PropertyChanged -= OnCompareBandChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (EqBand band in e.NewItems)
            {
                band.PropertyChanged += OnCompareBandChanged;
            }
        }

        InvalidateVisual();
    }

    private void OnCompareBandChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var background = new LinearGradientBrush(
            Color.FromRgb(0x07, 0x11, 0x18),
            Color.FromRgb(0x10, 0x25, 0x33),
            42);
        dc.DrawRoundedRectangle(background, new Pen(WithAlpha(GetAccentColor(), 0x55), 1), bounds, 16, 16);

        var plot = GetPlotRect(bounds);
        DrawGrid(dc, plot);
        DrawLabels(dc, plot);
        DrawCompareCurve(dc, plot);
        DrawCurve(dc, plot);
        DrawBands(dc, plot);
        DrawLegend(dc, plot);
    }

    private static void DrawGrid(DrawingContext dc, Rect plot)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x24, 0xE6, 0xF7, 0xFB)), 1);
        var zeroPen = new Pen(WithAlpha(GetAccentColor(), 0xB0), 1.4);

        for (var db = -12; db <= 12; db += 6)
        {
            var y = GainToY(db, plot);
            dc.DrawLine(db == 0 ? zeroPen : gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        foreach (var frequency in new[] { 20d, 50d, 100d, 200d, 500d, 1000d, 2000d, 5000d, 10000d, 20000d })
        {
            var x = FrequencyToX(frequency, plot);
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private static void DrawLabels(DrawingContext dc, Rect plot)
    {
        var typeface = new Typeface("Segoe UI");
        var textBrush = new SolidColorBrush(Color.FromRgb(0x8D, 0xB6, 0xC4));
        const double pixelsPerDip = 1.0;

        foreach (var db in new[] { 12, 6, 0, -6, -12 })
        {
            var text = new FormattedText($"{db:+#;-#;0} dB", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip);
            dc.DrawText(text, new Point(7, GainToY(db, plot) - 7));
        }

        foreach (var label in new[] { (20d, "20"), (100d, "100"), (1000d, "1k"), (10000d, "10k"), (20000d, "20k") })
        {
            var text = new FormattedText(label.Item2, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip);
            dc.DrawText(text, new Point(FrequencyToX(label.Item1, plot) - text.Width / 2, plot.Bottom + 8));
        }
    }

    private void DrawCurve(DrawingContext dc, Rect plot)
    {
        if (Bands is not { Count: > 0 })
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var px = 0; px <= (int)plot.Width; px++)
            {
                var x = plot.Left + px;
                var frequency = XToFrequency(x, plot);
                var y = GainToY(EstimateGain(frequency), plot);
                if (px == 0)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        var accent = GetAccentColor();
        var glowPen = new Pen(WithAlpha(accent, 0x55), 8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var curvePen = new Pen(new SolidColorBrush(Lighten(accent, 0.38)), 2.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawGeometry(null, glowPen, geometry);
        dc.DrawGeometry(null, curvePen, geometry);
    }

    private void DrawCompareCurve(DrawingContext dc, Rect plot)
    {
        if (CompareBands is not { Count: > 0 })
        {
            return;
        }

        var geometry = BuildCurveGeometry(plot, frequency => EstimateGain(frequency, CompareBands, ComparePreampDb));
        var comparePen = new Pen(new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)), 2.0)
        {
            DashStyle = DashStyles.Dash,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawGeometry(null, comparePen, geometry);
    }

    private void DrawBands(DrawingContext dc, Rect plot)
    {
        if (Bands == null)
        {
            return;
        }

        var accent = GetAccentColor();
        var activeFill = new SolidColorBrush(accent);
        var disabledFill = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        var activeDragFill = new SolidColorBrush(Lighten(accent, 0.28));
        var outline = new Pen(Brushes.White, 1);
        var focusOutline = new Pen(new SolidColorBrush(Lighten(accent, 0.55)), 1.8);
        var stemPen = new Pen(WithAlpha(accent, 0x66), 1.2)
        {
            DashStyle = DashStyles.Dot,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var typeface = new Typeface("Segoe UI");
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF7, 0xFB));
        const double pixelsPerDip = 1.0;

        foreach (var band in Bands)
        {
            var point = BandToResponsePoint(band, plot);
            var editPoint = BandToEditPoint(band, plot);
            var isActiveDrag = ReferenceEquals(band, _dragBand);
            var isHover = ReferenceEquals(band, _hoverBand);
            var fill = isActiveDrag ? activeDragFill : band.Enabled ? activeFill : disabledFill;
            var radius = isActiveDrag || isHover ? 7.2 : DotRadius;

            if ((isActiveDrag || isHover) && band.Enabled && Math.Abs(editPoint.Y - point.Y) > 2)
            {
                dc.DrawLine(stemPen, editPoint, point);
                dc.DrawEllipse(null, new Pen(WithAlpha(accent, 0x88), 1), editPoint, 3.2, 3.2);
            }

            dc.DrawEllipse(fill, isActiveDrag || isHover ? focusOutline : outline, point, radius, radius);

            if (isActiveDrag || isHover)
            {
                var text = new FormattedText($"B{band.Number}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 10, labelBrush, pixelsPerDip);
                dc.DrawText(text, new Point(point.X - text.Width / 2, point.Y - 22));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInteractive)
        {
            return;
        }

        Focus();

        if (Bands is not { Count: > 0 })
        {
            return;
        }

        var plot = GetPlotRect(new Rect(0, 0, ActualWidth, ActualHeight));
        var point = e.GetPosition(this);
        var band = FindNearestBand(point, plot);
        if (band is null)
        {
            return;
        }

        _dragBand = band;
        _hoverBand = band;
        band.Enabled = true;
        CaptureMouse();
        UpdateDraggedBand(point, plot);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsInteractive)
        {
            return;
        }

        var plot = GetPlotRect(new Rect(0, 0, ActualWidth, ActualHeight));
        var point = e.GetPosition(this);

        if (_dragBand is not null && IsMouseCaptured)
        {
            UpdateDraggedBand(point, plot);
            e.Handled = true;
            return;
        }

        var hover = Bands is { Count: > 0 } ? FindNearestBand(point, plot) : null;
        if (!ReferenceEquals(hover, _hoverBand))
        {
            _hoverBand = hover;
            Cursor = hover is null ? Cursors.Arrow : Cursors.Hand;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsInteractive)
        {
            return;
        }

        FinishDrag();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsInteractive)
        {
            return;
        }

        if (_dragBand is null)
        {
            _hoverBand = null;
            Cursor = Cursors.Arrow;
            InvalidateVisual();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!IsInteractive)
        {
            return;
        }

        FinishDrag();
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        => new PointHitTestResult(this, hitTestParameters.HitPoint);

    private void FinishDrag()
    {
        if (_dragBand is null)
        {
            return;
        }

        _dragBand = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        InvalidateVisual();
    }

    private void UpdateDraggedBand(Point point, Rect plot)
    {
        if (_dragBand is null)
        {
            return;
        }

        var frequency = (int)Math.Round(XToFrequency(point.X, plot));
        var gain = Math.Round(YToGain(point.Y, plot) - PreampDb, 1);
        _dragBand.FrequencyHz = frequency;
        _dragBand.GainDb = gain;
    }

    private EqBand? FindNearestBand(Point point, Rect plot)
    {
        if (Bands is not { Count: > 0 })
        {
            return null;
        }

        EqBand? nearest = null;
        var nearestDistance = DotHitRadius * DotHitRadius;
        foreach (var band in Bands)
        {
            var bandPoint = BandToResponsePoint(band, plot);
            var dx = point.X - bandPoint.X;
            var dy = point.Y - bandPoint.Y;
            var distance = dx * dx + dy * dy;
            if (distance <= nearestDistance)
            {
                nearest = band;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private Point BandToEditPoint(EqBand band, Rect plot)
        => new(FrequencyToX(band.FrequencyHz, plot), GainToY(band.GainDb + PreampDb, plot));

    private Point BandToResponsePoint(EqBand band, Rect plot)
        => new(FrequencyToX(band.FrequencyHz, plot), GainToY(EstimateGain(band.FrequencyHz), plot));

    private void DrawLegend(DrawingContext dc, Rect plot)
    {
        if (CompareBands is not { Count: > 0 })
        {
            return;
        }

        var typeface = new Typeface("Segoe UI");
        var textBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF7, 0xFB));
        const double pixelsPerDip = 1.0;
        var x = plot.Right - 156;
        var y = plot.Top + 10;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x88, 0x08, 0x17, 0x20)), null, new Rect(x - 10, y - 7, 150, 46), 8, 8);
        dc.DrawLine(new Pen(new SolidColorBrush(Lighten(GetAccentColor(), 0.38)), 2.4), new Point(x, y + 4), new Point(x + 24, y + 4));
        dc.DrawText(new FormattedText("Current", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip), new Point(x + 32, y - 3));
        var comparePen = new Pen(new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)), 2.0) { DashStyle = DashStyles.Dash };
        dc.DrawLine(comparePen, new Point(x, y + 24), new Point(x + 24, y + 24));
        dc.DrawText(new FormattedText("A/B reference", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip), new Point(x + 32, y + 17));
    }

    private double EstimateGain(double frequency)
    {
        var sum = PreampDb;
        foreach (var band in Bands!.Where(b => b.Enabled))
        {
            var octaves = Math.Log(frequency / band.FrequencyHz, 2);
            var width = Math.Max(0.18, 1.0 / Math.Max(0.1, band.Q));
            var influence = Math.Exp(-(octaves * octaves) / (2 * width * width));
            sum += band.GainDb * influence;
        }

        return Math.Clamp(sum, -12, 12);
    }

    private static StreamGeometry BuildCurveGeometry(Rect plot, Func<double, double> gainAtFrequency)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var px = 0; px <= (int)plot.Width; px++)
            {
                var x = plot.Left + px;
                var frequency = XToFrequency(x, plot);
                var y = GainToY(gainAtFrequency(frequency), plot);
                if (px == 0)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static double EstimateGain(double frequency, IEnumerable<EqBand> bands, double preampDb)
    {
        var sum = preampDb;
        foreach (var band in bands.Where(b => b.Enabled))
        {
            var octaves = Math.Log(frequency / band.FrequencyHz, 2);
            var width = Math.Max(0.18, 1.0 / Math.Max(0.1, band.Q));
            var influence = Math.Exp(-(octaves * octaves) / (2 * width * width));
            sum += band.GainDb * influence;
        }

        return Math.Clamp(sum, -12, 12);
    }

    private static double FrequencyToX(double frequency, Rect plot)
    {
        var min = Math.Log10(20);
        var max = Math.Log10(20000);
        return plot.Left + (Math.Log10(Math.Clamp(frequency, 20, 20000)) - min) / (max - min) * plot.Width;
    }

    private static double XToFrequency(double x, Rect plot)
    {
        var min = Math.Log10(20);
        var max = Math.Log10(20000);
        var t = Math.Clamp((x - plot.Left) / plot.Width, 0, 1);
        return Math.Pow(10, min + t * (max - min));
    }

    private static double GainToY(double gain, Rect plot) => plot.Top + (12 - Math.Clamp(gain, -12, 12)) / 24.0 * plot.Height;

    private static double YToGain(double y, Rect plot)
    {
        var t = Math.Clamp((y - plot.Top) / plot.Height, 0, 1);
        return 12 - t * 24;
    }

    private static Rect GetPlotRect(Rect bounds)
        => new(48, 24, Math.Max(1, bounds.Width - 70), Math.Max(1, bounds.Height - 58));

    private static Color GetAccentColor()
        => Application.Current.TryFindResource("WolfGreen") is Color color
            ? color
            : Color.FromRgb(0x00, 0xE6, 0x76);

    private static SolidColorBrush WithAlpha(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Color Lighten(Color color, double amount)
        => Color.FromRgb(
            (byte)Math.Min(255, color.R + (255 - color.R) * amount),
            (byte)Math.Min(255, color.G + (255 - color.G) * amount),
            (byte)Math.Min(255, color.B + (255 - color.B) * amount));
}
