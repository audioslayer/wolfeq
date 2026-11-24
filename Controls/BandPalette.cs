using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WolfEQ.Controls;

/// <summary>
/// One colour per band, warm for the lowest frequency through cool for the highest. Colours follow
/// the band's on-screen number, so a number and its colour always agree across every view.
/// </summary>
public static class BandPalette
{
    private static readonly Color[] Anchors =
    [
        Color.FromRgb(0xFF, 0x6B, 0x6B), Color.FromRgb(0xFF, 0x9F, 0x43), Color.FromRgb(0xFF, 0xD4, 0x3B),
        Color.FromRgb(0xA3, 0xE6, 0x35), Color.FromRgb(0x34, 0xD3, 0x99), Color.FromRgb(0x22, 0xD3, 0xEE),
        Color.FromRgb(0x4D, 0xA3, 0xFF), Color.FromRgb(0x81, 0x8C, 0xF8), Color.FromRgb(0xC0, 0x84, 0xFC),
        Color.FromRgb(0xF4, 0x72, 0xB6)
    ];

    /// <param name="index">Zero-based position, lowest frequency first.</param>
    /// <param name="count">Number of bands; any count spreads over the whole range.</param>
    /// <param name="light">True on a light background.</param>
    public static Color ColorFor(int index, int count, bool light = false)
    {
        var position = count <= 1 ? 0 : Math.Clamp(index, 0, count - 1) / (double)(count - 1) * (Anchors.Length - 1);
        var left = Math.Min((int)position, Anchors.Length - 1);
        var a = Anchors[left];
        var b = Anchors[Math.Min(left + 1, Anchors.Length - 1)];
        var fraction = position - left;
        // Light backgrounds need deeper colours, or yellow and lime disappear.
        var scale = light ? 0.66 : 1;
        byte Mix(byte x, byte y) => (byte)Math.Round((x + (y - x) * fraction) * scale);
        return Color.FromRgb(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B));
    }

    public static bool IsLight(object? backgroundMode) => Equals(backgroundMode, "Light");
}

/// <summary>
/// Values: band index, band count, background mode. Parameter "Display" means the index is a
/// one-based on-screen number.
/// </summary>
public sealed class BandBrushConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not int index || values[1] is not int count) return DependencyProperty.UnsetValue;
        if (Equals(parameter, "Display")) index--;
        var brush = new SolidColorBrush(BandPalette.ColorFor(index, count, BandPalette.IsLight(values.Length > 2 ? values[2] : null)));
        brush.Freeze();
        return brush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
