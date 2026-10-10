using System.Collections;
using System.Windows;
using System.Windows.Media;
using WolfEQ.Models;

namespace WolfEQ.Controls;

/// <summary>
/// Small, non-interactive EQ curve for library rows. Uses the same response math as
/// <see cref="EqResponseGraphControl"/> so a thumbnail matches the full graph's shape.
/// </summary>
public sealed class EqCurveThumbnail : FrameworkElement
{
    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(IEnumerable), typeof(EqCurveThumbnail),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const int Points = 48;

    public EqCurveThumbnail()
    {
        Width = 64;
        Height = 30;
        SnapsToDevicePixels = true;
        // Library rows are recycled and presets can be edited while the panel is closed;
        // redraw whenever the row becomes visible again.
        IsVisibleChanged += (_, _) => InvalidateVisual();
    }

    public IEnumerable? Bands
    {
        get => (IEnumerable?)GetValue(BandsProperty);
        set => SetValue(BandsProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width < 4 || bounds.Height < 4)
        {
            return;
        }

        dc.DrawRoundedRectangle(Brush("WolfBgBrush", Brushes.Black), null, bounds, 6, 6);
        var bands = Bands?.OfType<EqBand>().ToArray() ?? [];
        var gains = new double[Points];
        for (var i = 0; i < Points; i++)
        {
            var frequency = 20 * Math.Pow(1000, i / (double)(Points - 1));
            gains[i] = bands.Length == 0 ? 0 : EqResponseGraphControl.EstimateGain(frequency, bands);
        }

        // Scale to the curve, but never so tight that a 1 dB tilt looks dramatic.
        var range = Math.Max(8, gains.Select(Math.Abs).DefaultIfEmpty(0).Max() * 1.15);
        double X(int i) => 3 + (bounds.Width - 6) * i / (Points - 1);
        double Y(double gain) => bounds.Height / 2 - gain / range * (bounds.Height / 2 - 3);

        var zero = new Pen(Brush("WolfLineBrush", Brushes.DimGray), 1);
        dc.DrawLine(zero, new Point(3, bounds.Height / 2), new Point(bounds.Width - 3, bounds.Height / 2));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(X(0), Y(gains[0])), false, false);
            for (var i = 1; i < Points; i++)
            {
                context.LineTo(new Point(X(i), Y(gains[i])), true, true);
            }
        }

        geometry.Freeze();
        var pen = new Pen(Brush("WolfGreenBrush", Brushes.LimeGreen), 1.5) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, geometry);
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
}
