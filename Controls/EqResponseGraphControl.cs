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
    private const double DotRadius = 9.0;
    private const double DotHitRadius = 16.0;
    private const double DisplayGraphMinGain = -18.0;
    private const double DisplayGraphMaxGain = 12.0;
    private const double FilterPreviewMinGain = -24.0;
    private const double FilterPreviewMaxGain = 12.0;
    private const double PreviewSampleRate = 48000.0;
    private const int MaximumCurveSegments = 900;
    private const double QWheelFactor = 1.12;
    private const double FrequencyNudgeStep = 1.1;
    private const double FrequencyNudgeStepFine = 1.02;
    private const double GainNudgeStepDb = 0.5;
    private const double GainNudgeStepFineDb = 0.1;
    private const double SelectionRingPadding = 4.0;
    private const double QHandleMinHalfWidth = 30.0;
    private const double QHandleMaxHalfWidth = 112.0;
    private const double QHandleHitRadius = 12.0;
    private EqBand? _dragBand;
    private EqBand? _qDragBand;
    private EqBand? _hoverBand;
    private Point? _hoverPoint;

    /// <summary>
    /// Raised whenever the selected band or its graph position changes. The workspace
    /// uses this to keep the contextual band editor attached to the selected node.
    /// </summary>
    public event EventHandler? SelectedBandAnchorChanged;

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

    public static readonly DependencyProperty SelectedBandProperty = DependencyProperty.Register(
        nameof(SelectedBand),
        typeof(EqBand),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            OnSelectedBandChanged));

    private static readonly DependencyPropertyKey SelectedBandDisplayNumberPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedBandDisplayNumber),
        typeof(int),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(0));

    public static readonly DependencyProperty SelectedBandDisplayNumberProperty =
        SelectedBandDisplayNumberPropertyKey.DependencyProperty;

    public static readonly DependencyProperty MinQProperty = DependencyProperty.Register(
        nameof(MinQ),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(0.1));

    public static readonly DependencyProperty MaxQProperty = DependencyProperty.Register(
        nameof(MaxQ),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(10.0));

    public static readonly DependencyProperty MinFrequencyHzProperty = DependencyProperty.Register(
        nameof(MinFrequencyHz),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(20.0));

    public static readonly DependencyProperty MaxFrequencyHzProperty = DependencyProperty.Register(
        nameof(MaxFrequencyHz),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(20000.0));

    public static readonly DependencyProperty MinGainDbProperty = DependencyProperty.Register(
        nameof(MinGainDb),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(DisplayGraphMinGain, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxGainDbProperty = DependencyProperty.Register(
        nameof(MaxGainDb),
        typeof(double),
        typeof(EqResponseGraphControl),
        new FrameworkPropertyMetadata(DisplayGraphMaxGain, FrameworkPropertyMetadataOptions.AffectsRender));

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

    public EqBand? SelectedBand
    {
        get => (EqBand?)GetValue(SelectedBandProperty);
        set => SetValue(SelectedBandProperty, value);
    }

    public int SelectedBandDisplayNumber
        => (int)GetValue(SelectedBandDisplayNumberProperty);

    public double MinQ
    {
        get => (double)GetValue(MinQProperty);
        set => SetValue(MinQProperty, value);
    }

    public double MaxQ
    {
        get => (double)GetValue(MaxQProperty);
        set => SetValue(MaxQProperty, value);
    }

    public double MinFrequencyHz
    {
        get => (double)GetValue(MinFrequencyHzProperty);
        set => SetValue(MinFrequencyHzProperty, value);
    }

    public double MaxFrequencyHz
    {
        get => (double)GetValue(MaxFrequencyHzProperty);
        set => SetValue(MaxFrequencyHzProperty, value);
    }

    public double MinGainDb
    {
        get => (double)GetValue(MinGainDbProperty);
        set => SetValue(MinGainDbProperty, value);
    }

    public double MaxGainDb
    {
        get => (double)GetValue(MaxGainDbProperty);
        set => SetValue(MaxGainDbProperty, value);
    }

    public EqResponseGraphControl()
    {
        MinHeight = 250;
        Focusable = true;
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) =>
        {
            InvalidateVisual();
            SelectedBandAnchorChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    private static void OnSelectedBandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EqResponseGraphControl)d;
        control.UpdateSelectedBandDisplayNumber();
        control.InvalidateVisual();
        control.SelectedBandAnchorChanged?.Invoke(control, EventArgs.Empty);
    }

    private static void OnIsInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (EqResponseGraphControl)d;
        if (e.NewValue is false)
        {
            control._dragBand = null;
            control._qDragBand = null;
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

        if (control.SelectedBand is EqBand selected &&
            (e.NewValue is not ObservableCollection<EqBand> bands || !bands.Contains(selected)))
        {
            control.SelectedBand = null;
        }

        control.UpdateSelectedBandDisplayNumber();
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

        if (SelectedBand is EqBand selected && (Bands is null || !Bands.Contains(selected)))
        {
            SelectedBand = null;
        }

        UpdateSelectedBandDisplayNumber();
        InvalidateVisual();
    }

    private void OnBandChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateSelectedBandDisplayNumber();
        InvalidateVisual();
        if (ReferenceEquals(sender, SelectedBand))
        {
            SelectedBandAnchorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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

        var geometry = BuildCurveGeometry(plot, gainScale, EstimateGain);
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
        var selection = GetSelectionColor();
        var activeFill = new SolidColorBrush(Color.FromRgb(0x0A, 0x3C, 0x2E));
        var disabledFill = new SolidColorBrush(Color.FromRgb(0x11, 0x1A, 0x21));
        var activeDragFill = new SolidColorBrush(Lighten(accent, 0.28));
        var activeOutline = new Pen(new SolidColorBrush(accent), 1.8);
        var disabledOutline = new Pen(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)), 1.5);
        var hoverOutline = new Pen(WithAlpha(selection, 0xCC), 1.6);
        var selectionPen = new Pen(new SolidColorBrush(selection), 2.4);
        var bypassSlashPen = new Pen(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)), 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var typeface = new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xF7, 0xFB));
        const double pixelsPerDip = 1.0;

        foreach (var band in Bands)
        {
            var editPoint = BandToEditPoint(band, plot, gainScale);
            var responsePoint = BandToResponsePoint(band, plot, gainScale);
            var nodePoint = band.Enabled ? responsePoint : editPoint;
            var isActiveDrag = ReferenceEquals(band, _dragBand);
            var isHover = ReferenceEquals(band, _hoverBand);
            var isSelected = IsInteractive && ReferenceEquals(band, SelectedBand);
            var fill = isActiveDrag ? activeDragFill : band.Enabled ? activeFill : disabledFill;
            var radius = isSelected ? 10.5 : isActiveDrag || isHover ? 9.8 : DotRadius;

            if (isSelected)
            {
                DrawQHandles(dc, nodePoint, band.Q, plot, selection);
            }

            var outline = band.Enabled ? activeOutline : disabledOutline;
            dc.DrawEllipse(fill, isHover && !isSelected ? hoverOutline : outline, nodePoint, radius, radius);

            if (!band.Enabled)
            {
                var offset = radius * 0.52;
                dc.DrawLine(
                    bypassSlashPen,
                    new Point(nodePoint.X - offset, nodePoint.Y + offset),
                    new Point(nodePoint.X + offset, nodePoint.Y - offset));
            }

            if (isSelected)
            {
                dc.DrawEllipse(null, selectionPen, nodePoint, radius + SelectionRingPadding, radius + SelectionRingPadding);
            }

            var text = new FormattedText($"{GetDisplayNumber(band)}", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, 10, labelBrush, pixelsPerDip);
            dc.DrawText(text, new Point(nodePoint.X - text.Width / 2, nodePoint.Y - text.Height / 2));
        }
    }

    private void DrawQHandles(DrawingContext dc, Point center, double q, Rect plot, Color selection)
    {
        var halfWidth = Math.Min(GetQHandleHalfWidth(q), Math.Max(QHandleMinHalfWidth, plot.Width * 0.18));
        var left = new Point(Math.Max(plot.Left + 4, center.X - halfWidth), center.Y);
        var right = new Point(Math.Min(plot.Right - 4, center.X + halfWidth), center.Y);
        var pen = new Pen(WithAlpha(selection, 0xD8), 1.6)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        var handleBrush = new SolidColorBrush(selection);

        dc.DrawLine(pen, left, right);
        dc.DrawEllipse(handleBrush, null, left, 4, 4);
        dc.DrawEllipse(handleBrush, null, right, 4, 4);
    }

    private void DrawHoverReadout(DrawingContext dc, Rect plot, GraphGainScale gainScale)
    {
        if (_hoverPoint is not Point hoverPoint ||
            _hoverBand is not EqBand band ||
            ReferenceEquals(band, SelectedBand) ||
            !plot.Contains(hoverPoint))
        {
            return;
        }

        var editPoint = BandToDisplayPoint(band, plot, gainScale);
        var selection = GetSelectionColor();
        var typeface = new Typeface("Segoe UI");
        var titleBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0xFB, 0xFF));
        const double pixelsPerDip = 1.0;
        var title = $"B{GetDisplayNumber(band)}  ·  {FormatFrequency(band.FrequencyHz)}{(band.Enabled ? string.Empty : "  ·  Bypassed")}";
        var titleText = new FormattedText(title, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 11, titleBrush, pixelsPerDip);
        var width = titleText.Width + 20;
        const double height = 29;
        var x = Math.Clamp(editPoint.X - width / 2, plot.Left + 4, plot.Right - width - 4);
        var y = editPoint.Y < plot.Top + 48 ? editPoint.Y + 18 : editPoint.Y - height - 18;
        y = Math.Clamp(y, plot.Top + 4, plot.Bottom - height - 4);
        var tooltipRect = new Rect(x, y, width, height);

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(0xEA, 0x08, 0x13, 0x18)),
            new Pen(WithAlpha(selection, 0x88), 1),
            tooltipRect,
            7,
            7);
        dc.DrawText(titleText, new Point(tooltipRect.Left + 10, tooltipRect.Top + 7));
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

        if (TryFindSelectedQHandle(point, plot, gainScale, out var qBand))
        {
            SelectedBand = qBand;
            _qDragBand = qBand;
            _hoverBand = qBand;
            CaptureMouse();
            UpdateQFromHandle(point, plot, gainScale);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        var band = FindNearestBand(point, plot, gainScale);
        if (band is null)
        {
            SelectedBand = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        SelectedBand = band;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            band.Enabled = !band.Enabled;
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        _dragBand = band;
        _hoverBand = band;
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

        if (IsInteractive && _qDragBand is not null && IsMouseCaptured)
        {
            _hoverPoint = plot.Contains(point) ? point : (Point?)null;
            _hoverBand = _qDragBand;
            UpdateQFromHandle(point, plot, gainScale);
            Cursor = Cursors.SizeWE;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

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
        var overQHandle = hoverPoint is not null && TryFindSelectedQHandle(point, plot, gainScale, out _);
        var hover = hoverPoint is not null && Bands is { Count: > 0 }
            ? FindNearestBand(point, plot, gainScale)
            : null;

        if (!Equals(hoverPoint, _hoverPoint) || !ReferenceEquals(hover, _hoverBand))
        {
            _hoverPoint = hoverPoint;
            _hoverBand = hover;
            Cursor = IsInteractive && overQHandle ? Cursors.SizeWE : IsInteractive && hover is not null ? Cursors.Hand : Cursors.Arrow;
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

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsInteractive || e.Delta == 0 || Bands is not { Count: > 0 })
        {
            return;
        }

        var plot = GetPlotRect(new Rect(0, 0, ActualWidth, ActualHeight));
        var gainScale = GetGainScale();
        var point = e.GetPosition(this);
        var band = FindNearestBand(point, plot, gainScale);
        if (band is null && SelectedBand is EqBand selected && IsWithinSelectedBandQZone(point, selected, plot, gainScale))
        {
            band = selected;
        }
        if (band is null)
        {
            return;
        }

        var factor = Math.Pow(QWheelFactor, e.Delta / 120.0);
        band.Q = Math.Clamp(band.Q * factor, MinQ, MaxQ);
        SelectedBand = band;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsInteractive || SelectedBand is not EqBand band)
        {
            return;
        }

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
            {
                var factor = shift ? FrequencyNudgeStepFine : FrequencyNudgeStep;
                NudgeFrequency(band, e.Key == Key.Right ? factor : 1.0 / factor);
                e.Handled = true;
                break;
            }

            case Key.Up:
            case Key.Down:
            {
                var step = shift ? GainNudgeStepFineDb : GainNudgeStepDb;
                var delta = e.Key == Key.Up ? step : -step;
                band.GainDb = Math.Clamp(band.GainDb + delta, MinGainDb, MaxGainDb);
                e.Handled = true;
                break;
            }

            case Key.Space:
                band.Enabled = !band.Enabled;
                e.Handled = true;
                break;
        }
    }

    private void NudgeFrequency(EqBand band, double factor)
    {
        var next = Math.Round(band.FrequencyHz * factor);
        if ((int)next == band.FrequencyHz)
        {
            next = factor > 1.0 ? band.FrequencyHz + 1 : band.FrequencyHz - 1;
        }

        band.FrequencyHz = (int)Math.Clamp(next, MinFrequencyHz, MaxFrequencyHz);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_dragBand is null && _qDragBand is null)
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
        if (_dragBand is null && _qDragBand is null)
        {
            return;
        }

        _dragBand = null;
        _qDragBand = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        InvalidateVisual();
    }

    private void ClearInteractionState()
    {
        _dragBand = null;
        _qDragBand = null;
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

        var frequency = (int)Math.Clamp(Math.Round(XToFrequency(point.X, plot)), MinFrequencyHz, MaxFrequencyHz);
        _dragBand.FrequencyHz = frequency;
        var targetResponseGain = YToGain(point.Y, plot, gainScale);
        _dragBand.GainDb = _dragBand.Enabled
            ? SolveBandGainForTargetResponse(_dragBand, frequency, targetResponseGain)
            : Math.Clamp(Math.Round(targetResponseGain, 1), MinGainDb, MaxGainDb);
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
            var bandPoint = BandToDisplayPoint(band, plot, gainScale);
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

    private Point BandToDisplayPoint(EqBand band, Rect plot, GraphGainScale gainScale)
        => band.Enabled ? BandToResponsePoint(band, plot, gainScale) : BandToEditPoint(band, plot, gainScale);

    private int GetDisplayNumber(EqBand band)
    {
        if (Bands is not { Count: > 0 })
        {
            return band.Number;
        }

        var displayNumber = 1;
        foreach (var candidate in Bands.OrderBy(candidate => candidate.FrequencyHz).ThenBy(candidate => candidate.Number))
        {
            if (ReferenceEquals(candidate, band))
            {
                return displayNumber;
            }

            displayNumber++;
        }

        return band.Number;
    }

    private void UpdateSelectedBandDisplayNumber()
        => SetValue(
            SelectedBandDisplayNumberPropertyKey,
            SelectedBand is EqBand selected ? GetDisplayNumber(selected) : 0);

    /// <summary>Returns the selected node position in coordinates relative to this graph.</summary>
    public bool TryGetSelectedBandAnchor(out Point point)
    {
        point = default;
        if (SelectedBand is not EqBand band || Bands is null || !Bands.Contains(band) || ActualWidth <= 1 || ActualHeight <= 1)
        {
            return false;
        }

        var plot = GetPlotRect(new Rect(0, 0, ActualWidth, ActualHeight));
        point = BandToDisplayPoint(band, plot, GetGainScale());
        return true;
    }

    private bool TryFindSelectedQHandle(Point point, Rect plot, GraphGainScale gainScale, out EqBand band)
    {
        band = SelectedBand!;
        if (SelectedBand is not EqBand selected)
        {
            return false;
        }

        band = selected;
        var center = BandToDisplayPoint(selected, plot, gainScale);
        var halfWidth = Math.Min(GetQHandleHalfWidth(selected.Q), Math.Max(QHandleMinHalfWidth, plot.Width * 0.18));
        var left = new Point(Math.Max(plot.Left + 4, center.X - halfWidth), center.Y);
        var right = new Point(Math.Min(plot.Right - 4, center.X + halfWidth), center.Y);
        return DistanceSquared(point, left) <= QHandleHitRadius * QHandleHitRadius ||
               DistanceSquared(point, right) <= QHandleHitRadius * QHandleHitRadius;
    }

    private bool IsWithinSelectedBandQZone(Point point, EqBand band, Rect plot, GraphGainScale gainScale)
    {
        var center = BandToDisplayPoint(band, plot, gainScale);
        var halfWidth = Math.Min(GetQHandleHalfWidth(band.Q), Math.Max(QHandleMinHalfWidth, plot.Width * 0.18));
        return Math.Abs(point.Y - center.Y) <= 14 && Math.Abs(point.X - center.X) <= halfWidth + 8;
    }

    private void UpdateQFromHandle(Point point, Rect plot, GraphGainScale gainScale)
    {
        if (_qDragBand is not EqBand band)
        {
            return;
        }

        var center = BandToDisplayPoint(band, plot, gainScale);
        var maxHalfWidth = Math.Min(QHandleMaxHalfWidth, Math.Max(QHandleMinHalfWidth, plot.Width * 0.18));
        var halfWidth = Math.Clamp(Math.Abs(point.X - center.X), QHandleMinHalfWidth, maxHalfWidth);
        var t = (maxHalfWidth - halfWidth) / Math.Max(1, maxHalfWidth - QHandleMinHalfWidth);
        var logMin = Math.Log(Math.Max(0.01, MinQ));
        var logMax = Math.Log(Math.Max(MinQ + 0.01, MaxQ));
        band.Q = Math.Clamp(Math.Exp(logMin + t * (logMax - logMin)), MinQ, MaxQ);
    }

    private double GetQHandleHalfWidth(double q)
    {
        var logMin = Math.Log(Math.Max(0.01, MinQ));
        var logMax = Math.Log(Math.Max(MinQ + 0.01, MaxQ));
        var t = Math.Clamp((Math.Log(Math.Clamp(q, MinQ, MaxQ)) - logMin) / (logMax - logMin), 0, 1);
        return QHandleMaxHalfWidth + (QHandleMinHalfWidth - QHandleMaxHalfWidth) * t;
    }

    private static double DistanceSquared(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return dx * dx + dy * dy;
    }

    private Point BandToResponsePoint(EqBand band, Rect plot, GraphGainScale gainScale)
        => new(FrequencyToX(band.FrequencyHz, plot), GainToY(EstimateGain(band.FrequencyHz), plot, gainScale));

    private double SolveBandGainForTargetResponse(EqBand band, double frequency, double targetResponseGain)
    {
        var low = MinGainDb;
        var high = MaxGainDb;
        var lowResponse = EstimateGainWithBandOverride(frequency, band, low);
        var highResponse = EstimateGainWithBandOverride(frequency, band, high);

        if (Math.Abs(highResponse - lowResponse) < 0.001)
        {
            return Math.Clamp(Math.Round(targetResponseGain, 1), MinGainDb, MaxGainDb);
        }

        var increasing = highResponse > lowResponse;
        var target = Math.Clamp(targetResponseGain, Math.Min(lowResponse, highResponse), Math.Max(lowResponse, highResponse));
        for (var iteration = 0; iteration < 18; iteration++)
        {
            var middle = (low + high) / 2;
            var response = EstimateGainWithBandOverride(frequency, band, middle);
            if ((response < target) == increasing)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return Math.Clamp(Math.Round((low + high) / 2, 1), MinGainDb, MaxGainDb);
    }

    private double EstimateGainWithBandOverride(double frequency, EqBand editedBand, double editedGainDb)
    {
        var sum = 0.0;
        foreach (var band in Bands!)
        {
            if (band.Enabled)
            {
                sum += EstimateBandGainDb(
                    frequency,
                    band,
                    ReferenceEquals(band, editedBand) ? editedGainDb : band.GainDb);
            }
        }

        return sum;
    }

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
        foreach (var band in Bands!)
        {
            if (band.Enabled)
            {
                sum += EstimateBandGainDb(frequency, band);
            }
        }

        var scale = GetGainScale();
        return Math.Clamp(sum, scale.Min, scale.Max);
    }

    private static StreamGeometry BuildCurveGeometry(Rect plot, GraphGainScale gainScale, Func<double, double> gainAtFrequency)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var segmentCount = Math.Clamp((int)Math.Ceiling(plot.Width), 2, MaximumCurveSegments);
            for (var segment = 0; segment <= segmentCount; segment++)
            {
                var x = plot.Left + plot.Width * segment / segmentCount;
                var frequency = XToFrequency(x, plot);
                var y = GainToY(gainAtFrequency(frequency), plot, gainScale);
                if (segment == 0)
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
        foreach (var band in bands)
        {
            if (band.Enabled)
            {
                sum += EstimateBandGainDb(frequency, band);
            }
        }

        return sum;
    }

    private static double EstimateBandGainDb(double frequency, EqBand band, double? gainOverrideDb = null)
    {
        var f0 = Math.Clamp(band.FrequencyHz, 20, PreviewSampleRate / 2 - 100);
        var q = Math.Clamp(band.Q, 0.1, 10.0);
        var gainDb = gainOverrideDb ?? band.GainDb;
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
        => new(MinGainDb, MaxGainDb);

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

    private static Color GetSelectionColor()
        => Application.Current.TryFindResource("WolfCyan") is Color color
            ? color
            : Color.FromRgb(0x06, 0xB6, 0xD4);

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
