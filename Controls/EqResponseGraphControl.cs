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
    private const double DisplayGraphMinGain = -18.0;
    private const double DisplayGraphMaxGain = 12.0;
    private const double FilterPreviewMinGain = -24.0;
    private const double FilterPreviewMaxGain = 12.0;
    private const double PreviewSampleRate = 48000.0;
    private EqBand? _dragBand;
    private EqBand? _hoverBand;
    private Point? _hoverPoint;

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
            control._hoverPoint = null;
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
        control.ClearInteractionState();
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
        control.ClearInteractionState();
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
        var gainScale = GetGainScale();
        DrawGrid(dc, plot, gainScale);
        DrawLabels(dc, plot, gainScale);
        DrawCompareCurve(dc, plot, gainScale);
        DrawCurve(dc, plot, gainScale);
        DrawBands(dc, plot, gainScale);
        DrawLegend(dc, plot);
        DrawHoverReadout(dc, plot, gainScale);
    }

    private static void DrawGrid(DrawingContext dc, Rect plot, GraphGainScale gainScale)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x24, 0xE6, 0xF7, 0xFB)), 1);
        var zeroPen = new Pen(WithAlpha(GetAccentColor(), 0xB0), 1.4);

        var start = Math.Ceiling(gainScale.Min / 6.0) * 6.0;
        for (var db = start; db <= gainScale.Max; db += 6)
        {
            var y = GainToY(db, plot, gainScale);
            dc.DrawLine(db == 0 ? zeroPen : gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        foreach (var frequency in new[] { 20d, 50d, 100d, 200d, 500d, 1000d, 2000d, 5000d, 10000d, 20000d })
        {
            var x = FrequencyToX(frequency, plot);
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private static void DrawLabels(DrawingContext dc, Rect plot, GraphGainScale gainScale)
    {
        var typeface = new Typeface("Segoe UI");
        var textBrush = new SolidColorBrush(Color.FromRgb(0x8D, 0xB6, 0xC4));
        const double pixelsPerDip = 1.0;

        var start = Math.Ceiling(gainScale.Min / 6.0) * 6.0;
        for (var db = start; db <= gainScale.Max; db += 6)
        {
            var text = new FormattedText($"{db:+#;-#;0} dB", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip);
            dc.DrawText(text, new Point(7, GainToY(db, plot, gainScale) - 7));
        }

        foreach (var label in new[] { (20d, "20"), (100d, "100"), (1000d, "1k"), (10000d, "10k"), (20000d, "20k") })
        {
            var text = new FormattedText(label.Item2, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, textBrush, pixelsPerDip);
            dc.DrawText(text, new Point(FrequencyToX(label.Item1, plot) - text.Width / 2, plot.Bottom + 8));
        }
    }

    private void DrawCurve(DrawingContext dc, Rect plot, GraphGainScale gainScale)
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
                var y = GainToY(EstimateGain(frequency), plot, gainScale);
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

    private void DrawCompareCurve(DrawingContext dc, Rect plot, GraphGainScale gainScale)
    {
        if (CompareBands is not { Count: > 0 })
        {
            return;
        }

        var geometry = BuildCurveGeometry(plot, gainScale, frequency => EstimateGain(frequency, CompareBands));
        var comparePen = new Pen(new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16)), 2.0)
        {
            DashStyle = DashStyles.Dash,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawGeometry(null, comparePen, geometry);
    }

    private void DrawBands(DrawingContext dc, Rect plot, GraphGainScale gainScale)
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
        var responseMarkerPen = new Pen(WithAlpha(accent, 0x88), 1);
        var responseStemPen = new Pen(WithAlpha(accent, 0x55), 1.1)
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
            var editPoint = BandToEditPoint(band, plot, gainScale);
            var responsePoint = BandToResponsePoint(band, plot, gainScale);
            var isActiveDrag = ReferenceEquals(band, _dragBand);
            var isHover = ReferenceEquals(band, _hoverBand);
            var fill = isActiveDrag ? activeDragFill : band.Enabled ? activeFill : disabledFill;
            var radius = isActiveDrag || isHover ? 7.2 : DotRadius;

            if ((isActiveDrag || isHover) && band.Enabled && Math.Abs(editPoint.Y - responsePoint.Y) > 2)
            {
                dc.DrawLine(responseStemPen, editPoint, responsePoint);
                dc.DrawEllipse(null, responseMarkerPen, responsePoint, 3.1, 3.1);
            }

            dc.DrawEllipse(fill, isActiveDrag || isHover ? focusOutline : outline, editPoint, radius, radius);

            if (isActiveDrag || isHover)
            {
                var text = new FormattedText($"B{band.Number}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 10, labelBrush, pixelsPerDip);
                dc.DrawText(text, new Point(editPoint.X - text.Width / 2, editPoint.Y - 22));
            }
        }
    }

    private void DrawHoverReadout(DrawingContext dc, Rect plot, GraphGainScale gainScale)
    {
        if (_hoverPoint is not Point hoverPoint || Bands is not { Count: > 0 } || !plot.Contains(hoverPoint))
        {
            return;
        }

        var frequency = XToFrequency(hoverPoint.X, plot);
        var responseGain = EstimateGain(frequency);
        var responsePoint = new Point(hoverPoint.X, GainToY(responseGain, plot, gainScale));
        var accent = GetAccentColor();
        var guidePen = new Pen(WithAlpha(accent, 0x66), 1)
        {
            DashStyle = DashStyles.Dot,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var markerBrush = new SolidColorBrush(Lighten(accent, 0.28));
        markerBrush.Freeze();

        dc.DrawLine(guidePen, new Point(hoverPoint.X, plot.Top), new Point(hoverPoint.X, plot.Bottom));
        dc.DrawEllipse(markerBrush, new Pen(Brushes.White, 1), responsePoint, 3.6, 3.6);

        var typeface = new Typeface("Segoe UI");
        var titleBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0xFB, 0xFF));
        var detailBrush = new SolidColorBrush(Color.FromRgb(0x99, 0xC7, 0xD3));
        const double pixelsPerDip = 1.0;

        string title;
        string detail;
        if (_hoverBand is EqBand band)
        {
            title = $"Band {band.Number}  {FormatDb(band.GainDb)}";
            detail = $"{FormatFrequency(band.FrequencyHz)}  {FormatFilterType(band.FilterType)}  Q {band.Q:0.##}";
        }
        else
        {
            title = $"{FormatFrequency(frequency)}  {FormatDb(responseGain)}";
            detail = "Combined response";
        }

        var titleText = new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 11, titleBrush, pixelsPerDip);
        var detailText = new FormattedText(detail, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 10, detailBrush, pixelsPerDip);
        var width = Math.Max(titleText.Width, detailText.Width) + 20;
        const double height = 43;
        var x = Math.Clamp(hoverPoint.X + 14, plot.Left + 4, plot.Right - width - 4);
        var y = responsePoint.Y < plot.Top + 62
            ? responsePoint.Y + 16
            : responsePoint.Y - height - 16;
        y = Math.Clamp(y, plot.Top + 4, plot.Bottom - height - 4);
        var tooltipRect = new Rect(x, y, width, height);

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(0xEA, 0x08, 0x13, 0x18)),
            new Pen(WithAlpha(accent, 0x88), 1),
            tooltipRect,
            7,
            7);
        dc.DrawText(titleText, new Point(tooltipRect.Left + 10, tooltipRect.Top + 7));
        dc.DrawText(detailText, new Point(tooltipRect.Left + 10, tooltipRect.Top + 24));
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
        var gainScale = GetGainScale();
        var point = e.GetPosition(this);
        var band = FindNearestBand(point, plot, gainScale);
        if (band is null)
        {
            return;
        }

        _dragBand = band;
        _hoverBand = band;
        band.Enabled = true;
        CaptureMouse();
        UpdateDraggedBand(point, plot, gainScale);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var plot = GetPlotRect(new Rect(0, 0, ActualWidth, ActualHeight));
        var gainScale = GetGainScale();
        var point = e.GetPosition(this);

        if (IsInteractive && _dragBand is not null && IsMouseCaptured)
        {
            _hoverPoint = plot.Contains(point) ? point : (Point?)null;
            _hoverBand = _dragBand;
            UpdateDraggedBand(point, plot, gainScale);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var hoverPoint = Bands is { Count: > 0 } && plot.Contains(point) ? point : (Point?)null;
        var hover = hoverPoint is not null && Bands is { Count: > 0 }
            ? FindNearestBand(point, plot, gainScale)
            : null;

        if (!Equals(hoverPoint, _hoverPoint) || !ReferenceEquals(hover, _hoverBand))
        {
            _hoverPoint = hoverPoint;
            _hoverBand = hover;
            Cursor = IsInteractive && hover is not null ? Cursors.Hand : Cursors.Arrow;
            InvalidateVisual();
        }

        if (!IsInteractive)
        {
            return;
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
        if (_dragBand is null)
        {
            _hoverBand = null;
            _hoverPoint = null;
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

    private void ClearInteractionState()
    {
        _dragBand = null;
        _hoverBand = null;
        _hoverPoint = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = IsInteractive ? Cursors.Hand : Cursors.Arrow;
    }

    private void UpdateDraggedBand(Point point, Rect plot, GraphGainScale gainScale)
    {
        if (_dragBand is null)
        {
            return;
        }

        var frequency = (int)Math.Round(XToFrequency(point.X, plot));
        var gain = Math.Round(YToGain(point.Y, plot, gainScale), 1);
        _dragBand.FrequencyHz = frequency;
        _dragBand.GainDb = gain;
    }

    private EqBand? FindNearestBand(Point point, Rect plot, GraphGainScale gainScale)
    {
        if (Bands is not { Count: > 0 })
        {
            return null;
        }

        EqBand? nearest = null;
        var nearestDistance = DotHitRadius * DotHitRadius;
        foreach (var band in Bands)
        {
            var bandPoint = BandToEditPoint(band, plot, gainScale);
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

    private Point BandToEditPoint(EqBand band, Rect plot, GraphGainScale gainScale)
        => new(FrequencyToX(band.FrequencyHz, plot), GainToY(band.GainDb, plot, gainScale));

    private Point BandToResponsePoint(EqBand band, Rect plot, GraphGainScale gainScale)
        => new(FrequencyToX(band.FrequencyHz, plot), GainToY(EstimateGain(band.FrequencyHz), plot, gainScale));

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
        var sum = 0.0;
        foreach (var band in Bands!.Where(b => b.Enabled))
        {
            sum += EstimateBandGainDb(frequency, band);
        }

        var scale = GetGainScale();
        return Math.Clamp(sum, scale.Min, scale.Max);
    }

    private static StreamGeometry BuildCurveGeometry(Rect plot, GraphGainScale gainScale, Func<double, double> gainAtFrequency)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var px = 0; px <= (int)plot.Width; px++)
            {
                var x = plot.Left + px;
                var frequency = XToFrequency(x, plot);
                var y = GainToY(gainAtFrequency(frequency), plot, gainScale);
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

    private static double EstimateGain(double frequency, IEnumerable<EqBand> bands)
    {
        var sum = 0.0;
        foreach (var band in bands.Where(b => b.Enabled))
        {
            sum += EstimateBandGainDb(frequency, band);
        }

        return sum;
    }

    private static double EstimateBandGainDb(double frequency, EqBand band)
    {
        var f0 = Math.Clamp(band.FrequencyHz, 20, PreviewSampleRate / 2 - 100);
        var q = Math.Clamp(band.Q, 0.1, 10.0);
        var gainDb = band.GainDb;
        var omega0 = 2 * Math.PI * f0 / PreviewSampleRate;
        var sin = Math.Sin(omega0);
        var cos = Math.Cos(omega0);
        var alpha = sin / (2 * q);
        var a = Math.Pow(10, gainDb / 40.0);

        return band.FilterType switch
        {
            EqFilterType.Peak => MagnitudeDb(frequency, 1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a),
            EqFilterType.LowShelf => LowShelfMagnitudeDb(frequency, a, sin, cos, q),
            EqFilterType.HighShelf => HighShelfMagnitudeDb(frequency, a, sin, cos, q),
            EqFilterType.BandPass => MagnitudeDb(frequency, alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha),
            EqFilterType.LowPass => MagnitudeDb(frequency, (1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha),
            EqFilterType.HighPass => MagnitudeDb(frequency, (1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha),
            EqFilterType.AllPass => 0,
            _ => 0
        };
    }

    private static double LowShelfMagnitudeDb(double frequency, double a, double sin, double cos, double q)
    {
        var alpha = ShelfAlpha(a, sin, q);
        var beta = 2 * Math.Sqrt(a) * alpha;
        var b0 = a * ((a + 1) - (a - 1) * cos + beta);
        var b1 = 2 * a * ((a - 1) - (a + 1) * cos);
        var b2 = a * ((a + 1) - (a - 1) * cos - beta);
        var a0 = (a + 1) + (a - 1) * cos + beta;
        var a1 = -2 * ((a - 1) + (a + 1) * cos);
        var a2 = (a + 1) + (a - 1) * cos - beta;
        return MagnitudeDb(frequency, b0, b1, b2, a0, a1, a2);
    }

    private static double HighShelfMagnitudeDb(double frequency, double a, double sin, double cos, double q)
    {
        var alpha = ShelfAlpha(a, sin, q);
        var beta = 2 * Math.Sqrt(a) * alpha;
        var b0 = a * ((a + 1) + (a - 1) * cos + beta);
        var b1 = -2 * a * ((a - 1) + (a + 1) * cos);
        var b2 = a * ((a + 1) + (a - 1) * cos - beta);
        var a0 = (a + 1) - (a - 1) * cos + beta;
        var a1 = 2 * ((a - 1) - (a + 1) * cos);
        var a2 = (a + 1) - (a - 1) * cos - beta;
        return MagnitudeDb(frequency, b0, b1, b2, a0, a1, a2);
    }

    private static double ShelfAlpha(double a, double sin, double q)
    {
        var slope = Math.Clamp(q, 0.1, 1.0);
        var root = Math.Max(0, (a + 1 / a) * (1 / slope - 1) + 2);
        return sin / 2 * Math.Sqrt(root);
    }

    private static double MagnitudeDb(
        double frequency,
        double b0,
        double b1,
        double b2,
        double a0,
        double a1,
        double a2)
    {
        if (Math.Abs(a0) < double.Epsilon)
        {
            return 0;
        }

        b0 /= a0;
        b1 /= a0;
        b2 /= a0;
        a1 /= a0;
        a2 /= a0;

        var omega = 2 * Math.PI * Math.Clamp(frequency, 20, PreviewSampleRate / 2 - 100) / PreviewSampleRate;
        var cos1 = Math.Cos(omega);
        var sin1 = Math.Sin(omega);
        var cos2 = Math.Cos(2 * omega);
        var sin2 = Math.Sin(2 * omega);

        var numeratorReal = b0 + b1 * cos1 + b2 * cos2;
        var numeratorImaginary = -b1 * sin1 - b2 * sin2;
        var denominatorReal = 1 + a1 * cos1 + a2 * cos2;
        var denominatorImaginary = -a1 * sin1 - a2 * sin2;
        var numerator = numeratorReal * numeratorReal + numeratorImaginary * numeratorImaginary;
        var denominator = denominatorReal * denominatorReal + denominatorImaginary * denominatorImaginary;
        if (denominator <= double.Epsilon || numerator <= double.Epsilon)
        {
            return 0;
        }

        return Math.Clamp(10 * Math.Log10(numerator / denominator), FilterPreviewMinGain, FilterPreviewMaxGain);
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

    private GraphGainScale GetGainScale()
        => new(DisplayGraphMinGain, DisplayGraphMaxGain);

    private static double GainToY(double gain, Rect plot, GraphGainScale gainScale)
        => plot.Top + (gainScale.Max - Math.Clamp(gain, gainScale.Min, gainScale.Max)) / (gainScale.Max - gainScale.Min) * plot.Height;

    private static double YToGain(double y, Rect plot, GraphGainScale gainScale)
    {
        var t = Math.Clamp((y - plot.Top) / plot.Height, 0, 1);
        return gainScale.Max - t * (gainScale.Max - gainScale.Min);
    }

    private static Rect GetPlotRect(Rect bounds)
        => new(48, 24, Math.Max(1, bounds.Width - 70), Math.Max(1, bounds.Height - 58));

    private static string FormatFrequency(double frequency)
        => frequency >= 1000
            ? $"{frequency / 1000:0.##} kHz"
            : $"{frequency:0} Hz";

    private static string FormatDb(double gain)
        => $"{gain:+0.0;-0.0;0.0} dB";

    private static string FormatFilterType(EqFilterType filterType)
        => filterType switch
        {
            EqFilterType.Peak => "Peak",
            EqFilterType.LowShelf => "Low shelf",
            EqFilterType.HighShelf => "High shelf",
            EqFilterType.LowPass => "Low pass",
            EqFilterType.HighPass => "High pass",
            EqFilterType.BandPass => "Band pass",
            EqFilterType.AllPass => "All pass",
            _ => filterType.ToString()
        };

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

    private sealed record GraphGainScale(double Min, double Max);
}
