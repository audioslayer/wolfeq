using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WolfEQ.Controls;

/// <summary>Draws the knob pointer: a small orb of light in the band's colour. While turning it lives on the knob instead (see BandKnob).</summary>
public static class KnobCursor
{
    private const double LogicalSize = 32;
    private static readonly Dictionary<(Color, int), Cursor> Cache = [];

    public static Cursor For(Color color, double dpiScale)
    {
        var size = (int)Math.Round(LogicalSize * Math.Clamp(double.IsFinite(dpiScale) ? dpiScale : 1, 1, 4));
        var key = (color, size);
        if (Cache.TryGetValue(key, out var cursor)) return cursor;
        try
        {
            using var stream = new MemoryStream(Encode(Draw(color, size), size));
            cursor = new Cursor(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            cursor = Cursors.Hand;
        }

        return Cache[key] = cursor;
    }

    public static BitmapSource Draw(Color color, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / LogicalSize, size / LogicalSize));
            DrawOrb(dc, new Point(16, 16), 1, color, 1);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>The orb itself, shared with the knob so the pointer and the light on the dial are the same object.</summary>
    public static void DrawOrb(DrawingContext dc, Point center, double scale, Color color, double opacity)
    {
        dc.PushOpacity(Math.Clamp(opacity, 0, 1));
        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(200, color.R, color.G, color.B), 0.25));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(70, color.R, color.G, color.B), 0.55));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        dc.DrawEllipse(halo, null, center, 14 * scale, 14 * scale);
        dc.DrawEllipse(new SolidColorBrush(color), new Pen(new SolidColorBrush(Color.FromArgb(150, 8, 14, 16)), 0.8 * scale), center, 5.6 * scale, 5.6 * scale);
        dc.DrawEllipse(Brushes.White, null, center, 3.1 * scale, 3.1 * scale);
        dc.Pop();
    }

    // A .cur file with one 32-bit image and the hotspot in the middle.
    private static byte[] Encode(BitmapSource bitmap, int size)
    {
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        var maskStride = (size + 31) / 32 * 4;
        var mask = new byte[maskStride * size];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((short)0);
        writer.Write((short)2);
        writer.Write((short)1);
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((short)0);
        writer.Write((short)(size / 2));
        writer.Write((short)(size / 2));
        writer.Write(40 + pixels.Length + mask.Length);
        writer.Write(22);
        writer.Write(40);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(pixels.Length + mask.Length);
        for (var i = 0; i < 4; i++) writer.Write(0);
        for (var y = size - 1; y >= 0; y--) writer.Write(pixels, y * size * 4, size * 4);
        for (var y = size - 1; y >= 0; y--)
        {
            for (var x = 0; x < size; x++)
                if (pixels[(y * size + x) * 4 + 3] == 0) mask[(size - 1 - y) * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
        }

        writer.Write(mask);
        writer.Flush();
        return stream.ToArray();
    }
}
