using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WolfEQ.Controls;

/// <summary>
/// Rotary value control: drag and the light follows the pointer around the dial (Shift for fine, Esc to cancel), arrow keys, or the
/// wheel once it has focus. The pointer flies into the light on the dial while turning and comes back out of it, so a drag never hits a screen edge.
/// It never coerces: Value changes only when the user moves the knob, so loading a band or changing
/// the device's limits cannot rewrite the EQ.
/// </summary>
public sealed class BandKnob : FrameworkElement
{
    private const double SweepDegrees = 270;
    private const double StartDegrees = 135;
    private const double PixelsForFullRange = 220;
    private const double DragThreshold = 3;
    private const double CenterDetent = 0.006;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(BandKnob),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0d);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 1d);
    public static readonly DependencyProperty StepProperty = Register(nameof(Step), 0.1);
    public static readonly DependencyProperty IsLogarithmicProperty = Register(nameof(IsLogarithmic), false);

    /// <summary>The arc grows from this value (0 for gain); NaN grows it from the minimum.</summary>
    public static readonly DependencyProperty OriginProperty = Register(nameof(Origin), double.NaN);
    public static readonly DependencyProperty AccentBrushProperty = Register<Brush>(nameof(AccentBrush), Brushes.MediumSpringGreen);
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush>(nameof(TrackBrush), Brushes.Gray);
    public static readonly DependencyProperty FaceBrushProperty = Register<Brush>(nameof(FaceBrush), Brushes.Black);
    public static readonly DependencyProperty PointerBrushProperty = Register<Brush>(nameof(PointerBrush), Brushes.White);

    /// <summary>0 at rest, 1 while turning; animated so the glow fades in and out.</summary>
    public static readonly DependencyProperty GlowProperty = Register(nameof(Glow), 0d);

    /// <summary>0 when the pointer is loose, 1 once it has flown into the light on the dial.</summary>
    public static readonly DependencyProperty AbsorbProperty = Register(nameof(Absorb), 0d);

    /// <summary>The flash when the light lands on or leaves the dial: runs 0 to 1, then rests at 0.</summary>
    public static readonly DependencyProperty ImpactProperty = Register(nameof(Impact), 0d);

    /// <summary>Slow breathing while the light is held.</summary>
    public static readonly DependencyProperty PulseProperty = Register(nameof(Pulse), 0d);

    /// <summary>Lags behind the light so a turn leaves a tail.</summary>
    public static readonly DependencyProperty TrailProperty = Register(nameof(Trail), 0d);

    private NativePoint _anchor;
    private Point _grabPoint;
    private double _dragPosition;
    private double _dragStartValue;
    private int _turnId;
    private bool _turning;

    static BandKnob() => FocusableProperty.OverrideMetadata(typeof(BandKnob), new FrameworkPropertyMetadata(true));

    public BandKnob()
    {
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
        SetResourceReference(TrackBrushProperty, "WolfLineSoftBrush");
        SetResourceReference(FaceBrushProperty, "WolfPanel2Brush");
        SetResourceReference(PointerBrushProperty, "WolfTextBrush");
        IsEnabledChanged += (_, _) => { AnimateGlow(); InvalidateVisual(); };
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public bool IsLogarithmic { get => (bool)GetValue(IsLogarithmicProperty); set => SetValue(IsLogarithmicProperty, value); }
    public double Origin { get => (double)GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    public double Glow { get => (double)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    public double Absorb { get => (double)GetValue(AbsorbProperty); set => SetValue(AbsorbProperty, value); }
    public double Impact { get => (double)GetValue(ImpactProperty); set => SetValue(ImpactProperty, value); }
    public double Pulse { get => (double)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    public double Trail { get => (double)GetValue(TrailProperty); set => SetValue(TrailProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush FaceBrush { get => (Brush)GetValue(FaceBrushProperty); set => SetValue(FaceBrushProperty, value); }
    public Brush PointerBrush { get => (Brush)GetValue(PointerBrushProperty); set => SetValue(PointerBrushProperty, value); }

    private static DependencyProperty Register<T>(string name, T fallback) => DependencyProperty.Register(name, typeof(T), typeof(BandKnob),
        new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    // A dial with an origin inside its range points straight up there, even for -24 to +12 dB.
    private bool HasCenter => double.IsFinite(Origin) && Origin > Minimum && Origin < Maximum;

    /// <summary>Where a value sits on the dial, from 0 to 1.</summary>
    public double PositionOf(double value)
    {
        if (!(Maximum > Minimum) || !double.IsFinite(value)) return 0;
        var clamped = Math.Clamp(value, Minimum, Maximum);
        if (HasCenter)
            return clamped < Origin ? (clamped - Minimum) / (Origin - Minimum) / 2 : 0.5 + (clamped - Origin) / (Maximum - Origin) / 2;
        return IsLogarithmic && Minimum > 0
            ? Math.Log(clamped / Minimum) / Math.Log(Maximum / Minimum)
            : (clamped - Minimum) / (Maximum - Minimum);
    }

    public double ValueAt(double position)
    {
        position = Math.Clamp(position, 0, 1);
        if (HasCenter)
            return position < 0.5 ? Minimum + (Origin - Minimum) * position * 2 : Origin + (Maximum - Origin) * (position - 0.5) * 2;
        return IsLogarithmic && Minimum > 0
            ? Minimum * Math.Pow(Maximum / Minimum, position)
            : Minimum + (Maximum - Minimum) * position;
    }

    /// <summary>The only path that changes Value: user input, clamped and rounded to the step.</summary>
    public void MoveTo(double value)
    {
        if (!IsEnabled || !(Maximum > Minimum) || !double.IsFinite(value)) return;
        if (Step > 0) value = Math.Round(value / Step) * Step;
        SetCurrentValue(ValueProperty, Math.Clamp(Math.Round(value, 4), Minimum, Maximum));
    }

    private void Nudge(int direction, bool fine)
    {
        if (!IsLogarithmic)
        {
            MoveTo(Value + direction * Step * (fine ? 1 : 5));
            return;
        }

        var target = ValueAt(PositionOf(Value) + direction * (fine ? 0.002 : 0.01));
        // A log step can round back to the same value at the low end; always move at least one step.
        if (Math.Abs(target - Value) < Step) target = Value + direction * Step;
        MoveTo(target);
    }

    private void ShowKnobCursor()
        => Cursor = KnobCursor.For((AccentBrush as SolidColorBrush)?.Color ?? Colors.MediumSpringGreen, VisualTreeHelper.GetDpi(this).DpiScaleX);

    private void AnimateAbsorb(bool absorb)
    {
        BeginAnimation(PulseProperty, null);
        BeginAnimation(TrailProperty, null);
        Trail = PositionOf(Value);
        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(AbsorbProperty, null);
            Absorb = absorb ? 1 : 0;
            return;
        }

        if (!absorb)
        {
            BeginAnimation(AbsorbProperty, null);
            Absorb = 0;
            Flash();
            return;
        }

        var flight = new DoubleAnimation(0.01, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        var turn = ++_turnId;
        flight.Completed += (_, _) =>
        {
            // A quick second drag must not be caught by the first one finishing.
            if (!_turning || turn != _turnId) return;
            Flash();
            BeginAnimation(PulseProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        };
        BeginAnimation(AbsorbProperty, flight);
    }

    private void Flash()
        => BeginAnimation(ImpactProperty, new DoubleAnimation(0.01, 1, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });

    // The light takes the short way to its place, in a gentle curve.
    private Point FlightPoint(Point center, double radius, double position, double t)
    {
        var from = _grabPoint - center;
        var startAngle = from.Length < 1 ? -Math.PI / 2 : Math.Atan2(from.Y, from.X);
        var endAngle = (StartDegrees + SweepDegrees * position) * Math.PI / 180;
        var sweep = (endAngle - startAngle) % (2 * Math.PI);
        if (sweep > Math.PI) sweep -= 2 * Math.PI;
        if (sweep < -Math.PI) sweep += 2 * Math.PI;
        var angle = startAngle + sweep * t;
        // Bows out slightly past the rim before settling on it.
        var distance = from.Length + (radius - from.Length) * t + Math.Sin(t * Math.PI) * radius * 0.12;
        return center + new Vector(Math.Cos(angle), Math.Sin(angle)) * distance;
    }

    private Point DialTip()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        return PointOn(new Point(ActualWidth / 2, ActualHeight / 2), size / 2 - Math.Max(2.5, size * 0.07), PositionOf(Value));
    }

    // The orb leaves a short fading tail, both on its way in and along the arc.
    private void DrawAbsorbedOrb(DrawingContext dc, Point center, double radius, double position, double size, Color accent)
    {
        var scale = Math.Clamp(size / 62, 0.72, 1.1);
        if (Absorb < 1)
        {
            for (var i = 9; i >= 0; i--)
            {
                var t = Absorb - i * 0.03;
                if (t <= 0 && i > 0) continue;
                KnobCursor.DrawOrb(dc, FlightPoint(center, radius, position, Math.Max(0, t)), scale * (1 - i * 0.075), accent, i == 0 ? 1 : 0.42 - i * 0.04);
            }

            return;
        }

        for (var i = 6; i >= 1; i--)
        {
            var t = position + (Trail - position) * i / 6;
            if (Math.Abs(t - position) > 0.004) KnobCursor.DrawOrb(dc, PointOn(center, radius, t), scale * (1 - i * 0.1), accent, 0.4 - i * 0.05);
        }

        KnobCursor.DrawOrb(dc, PointOn(center, radius, position), scale * (1 + 0.16 * Pulse), accent, 1);
    }

    internal static void DrawImpact(DrawingContext dc, Point center, Point tip, double size, double progress, Color accent)
    {
        var fade = 1 - progress;
        // A ring bursts from the light, another washes out from the whole dial, and sparks fly off.
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(accent, 0.9 * fade)), 0.5 + 2.5 * fade), tip, 4 + size * 0.34 * progress, 4 + size * 0.34 * progress);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(accent, 0.55 * fade)), 0.5 + 3 * fade), center, size * (0.42 + 0.26 * progress), size * (0.42 + 0.26 * progress));
        for (var i = 0; i < 9; i++)
        {
            var angle = i * 2 * Math.PI / 9 + 0.4;
            var reach = size * (i % 2 == 0 ? 0.42 : 0.28);
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            dc.DrawLine(RoundPen(new SolidColorBrush(WithAlpha(i % 2 == 0 ? Colors.White : accent, fade)), 1.6),
                tip + direction * (5 + reach * Math.Max(0, progress - 0.18)), tip + direction * (5 + reach * progress));
        }
    }

    private void AnimateGlow()
    {
        var target = !IsEnabled ? 0 : IsMouseCaptured ? 1 : IsKeyboardFocused ? 0.7 : IsMouseOver ? 0.4 : 0;
        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(GlowProperty, null);
            Glow = target;
            return;
        }

        BeginAnimation(GlowProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(target > Glow ? 120 : 260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 12) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var stroke = Math.Max(2.5, size * 0.07);
        var radius = size / 2 - stroke;
        var glow = Math.Clamp(Glow, 0, 1);
        var accent = (AccentBrush as SolidColorBrush)?.Color ?? Colors.MediumSpringGreen;
        if (!IsEnabled) dc.PushOpacity(0.35);

        if (glow > 0.01)
        {
            // Soft halo behind the dial; it fades to nothing just outside the arc.
            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(WithAlpha(accent, 0.10 * glow), 0));
            halo.GradientStops.Add(new GradientStop(WithAlpha(accent, 0.34 * glow), 0.68));
            halo.GradientStops.Add(new GradientStop(WithAlpha(accent, 0), 1));
            dc.DrawEllipse(halo, null, center, size * 0.64, size * 0.64);
        }

        dc.DrawGeometry(null, RoundPen(TrackBrush, stroke), Arc(center, radius, 0, 1));
        var position = PositionOf(Value);
        var origin = double.IsNaN(Origin) ? 0 : PositionOf(Origin);
        if (Math.Abs(position - origin) > 0.002)
        {
            var arc = Arc(center, radius, Math.Min(position, origin), Math.Max(position, origin));
            if (glow > 0.01) dc.DrawGeometry(null, RoundPen(new SolidColorBrush(WithAlpha(accent, 0.30 * glow)), stroke * 2.2), arc);
            dc.DrawGeometry(null, RoundPen(AccentBrush, stroke), arc);
        }

        var face = Math.Max(2, radius - stroke * 1.5);
        dc.DrawEllipse(FaceBrush, new Pen(TrackBrush, 1), center, face, face);
        if (glow > 0.01)
        {
            var sheen = new RadialGradientBrush(WithAlpha(accent, 0.30 * glow), WithAlpha(accent, 0))
            {
                GradientOrigin = new Point(0.5, 0.2), Center = new Point(0.5, 0.2), RadiusX = 0.75, RadiusY = 0.75
            };
            dc.DrawEllipse(sheen, null, center, face, face);
        }

        if (Absorb > 0)
        {
            // The held light shines a beam from the hub and tints the rim.
            var lit = Math.Clamp(Absorb, 0, 1);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(WithAlpha(accent, 0.75 * lit)), 1.5), center, face, face);
            dc.DrawLine(RoundPen(new SolidColorBrush(WithAlpha(accent, 0.45 * lit)), Math.Max(5, size * 0.12)), PointOn(center, face * 0.4, position), PointOn(center, face * 0.86, position));
        }

        var pointer = RoundPen(PointerBrush, Math.Max(1.8, size * 0.045));
        dc.DrawLine(pointer, PointOn(center, face * 0.45, position), PointOn(center, face * 0.82, position));
        if (Absorb > 0)
        {
            DrawAbsorbedOrb(dc, center, radius, position, size, accent);
        }
        else if (glow > 0.01)
        {
            // Bright bead where the arc ends, like a lit indicator.
            var tip = PointOn(center, radius, position);
            dc.DrawEllipse(new SolidColorBrush(WithAlpha(accent, 0.45 * glow)), null, tip, stroke * 1.5, stroke * 1.5);
            dc.DrawEllipse(new SolidColorBrush(WithAlpha(Colors.White, glow)), null, tip, stroke * 0.55, stroke * 0.55);
        }

        if (Impact > 0) DrawImpact(dc, center, PointOn(center, radius, position), size, Impact, accent);
        if (!IsEnabled) dc.Pop();
    }

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), color.R, color.G, color.B);

    private static Pen RoundPen(Brush brush, double thickness)
        => new(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private static Point PointOn(Point center, double radius, double position)
    {
        var radians = (StartDegrees + SweepDegrees * position) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static StreamGeometry Arc(Point center, double radius, double from, double to)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOn(center, radius, from), false, false);
            context.ArcTo(PointOn(center, radius, to), new Size(radius, radius), 0,
                (to - from) * SweepDegrees > 180, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
        if (e.ClickCount == 2 && !double.IsNaN(Origin))
        {
            MoveTo(Origin);
            return;
        }

        if (!GetCursorPos(out _anchor)) return;
        _grabPoint = e.GetPosition(this);
        _dragStartValue = Value;
        _dragPosition = PositionOf(Value);
        _turning = false;
        if (CaptureMouse()) AnimateGlow();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || !GetCursorPos(out var now)) return;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        // The light follows the pointer around the dial: only movement along the rim turns it.
        var angle = (StartDegrees + SweepDegrees * _dragPosition) * Math.PI / 180;
        var delta = new Vector(now.X - _anchor.X, now.Y - _anchor.Y) / scale;
        var moved = delta * new Vector(-Math.Sin(angle), Math.Cos(angle));
        if (!_turning)
        {
            // A plain click, or a slightly shaky one, must not change the value.
            if (delta.Length < DragThreshold) return;
            _turning = true;
            Cursor = Cursors.None;
            AnimateAbsorb(true);
            moved = Math.Clamp(moved, -1, 1);
        }

        // Put the pointer back, so a turn can go on forever. Read it back rather than assume:
        // remote desktop and pen input may refuse the move, and then the drag simply continues from there.
        SetCursorPos(_anchor.X, _anchor.Y);
        if (GetCursorPos(out var actual)) _anchor = actual;
        if (moved == 0) return;
        Turn(moved, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    /// <summary>Turns by a pointer distance: slow movement is finer, quick movement covers more.</summary>
    public void Turn(double pixels, bool fine)
    {
        var speed = fine ? 0.12 : Math.Clamp(0.45 + Math.Abs(pixels) / 12, 0.45, 1.6);
        _dragPosition = Math.Clamp(_dragPosition + pixels / PixelsForFullRange * speed, 0, 1);
        // Gain rests at 0 dB when you pass slowly over it.
        MoveTo(!fine && HasCenter && Math.Abs(_dragPosition - 0.5) < CenterDetent ? Origin : ValueAt(_dragPosition));
        if (!SystemParameters.ClientAreaAnimation) return;
        BeginAnimation(TrailProperty, new DoubleAnimation(PositionOf(Value), TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_turning)
        {
            // The pointer comes back out of the light, where the eye already is.
            if (PresentationSource.FromVisual(this) is not null && Window.GetWindow(this)?.IsActive == true)
            {
                var tip = PointToScreen(DialTip());
                SetCursorPos((int)Math.Round(tip.X), (int)Math.Round(tip.Y));
            }

            AnimateAbsorb(false);
        }

        _turning = false;
        ShowKnobCursor();
        AnimateGlow();
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); ShowKnobCursor(); AnimateGlow(); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); AnimateGlow(); }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // Without focus the wheel scrolls the panel: passing over a knob must not edit the EQ.
        if (!IsKeyboardFocused) return;
        Nudge(Math.Sign(e.Delta), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Up or Key.Right: Nudge(1, fine); break;
            case Key.Down or Key.Left: Nudge(-1, fine); break;
            case Key.Escape when IsMouseCaptured:
                MoveTo(_dragStartValue);
                ReleaseMouseCapture();
                break;
            case Key.Home: MoveTo(Minimum); break;
            case Key.End: MoveTo(Maximum); break;
            default: return;
        }

        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        AnimateGlow();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        AnimateGlow();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private sealed class Peer(BandKnob owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private BandKnob Knob => (BandKnob)Owner;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(BandKnob);
        public override object? GetPattern(PatternInterface pattern) => pattern == PatternInterface.RangeValue ? this : base.GetPattern(pattern);
        public bool IsReadOnly => !Knob.IsEnabled;
        public double Value => Knob.Value;
        public double Minimum => Knob.Minimum;
        public double Maximum => Knob.Maximum;
        public double SmallChange => Knob.Step;
        public double LargeChange => Knob.Step * 5;
        public void SetValue(double value) => Knob.MoveTo(value);
    }
}
