using System.IO;
using System.Text;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Keeps WolfEQ's marked block at the top of Equalizer APO's config.txt (KTD3, R10).
/// The text transforms are pure; every line outside the block is preserved byte-for-byte,
/// including its own line ending, and the block uses the file's dominant line ending.
/// </summary>
public static class ApoConfigIncludeManager
{
    public const string ConfigFileName = "config.txt";
    public const string IncludeFileName = "WolfEQ.txt";
    public const string StartMarker = "# >>> WolfEQ (managed) >>>";
    public const string EndMarker = "# <<< WolfEQ <<<";
    public const string DeviceLine = "Device: all";
    public const string IncludeLine = "Include: " + IncludeFileName;

    private const char ByteOrderMark = '\uFEFF';

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>How config.txt was stored on disk, so a rewrite keeps the exact same bytes.</summary>
    public enum ConfigEncoding
    {
        Utf8,
        Utf8Bom,
        Utf16LeBom,
        Utf16BeBom,
        // Not valid UTF-8 (for example an ANSI comment); Latin-1 round-trips every byte unchanged.
        Latin1,
        // UTF-8 BOM followed by bytes that are not valid UTF-8; the BOM is kept, the rest round-trips as Latin-1.
        Utf8BomLatin1
    }

    private readonly record struct Line(string Content, string Ending);

    /// <summary>
    /// Returns the text with exactly one canonical block at the very top. Half, misplaced or duplicated
    /// blocks are removed first. Returns the input unchanged when it is already correct.
    /// </summary>
    public static string EnsureBlock(string configText)
    {
        ArgumentNullException.ThrowIfNull(configText);

        var (bom, body) = SplitByteOrderMark(configText);
        var remaining = RemainingLines(body);
        var ending = DominantEnding(remaining);

        var builder = new StringBuilder(configText.Length + 96);
        builder.Append(bom);
        // Device: all again after the Include, so whatever follows in config.txt (Peace, hand-written
        // filters) keeps applying to every output no matter what WolfEQ.txt selects.
        foreach (var blockLine in new[] { StartMarker, DeviceLine, IncludeLine, DeviceLine, EndMarker })
        {
            builder.Append(blockLine).Append(ending);
        }

        AppendLines(builder, remaining);
        var result = builder.ToString();
        return string.Equals(result, configText, StringComparison.Ordinal) ? configText : result;
    }

    /// <summary>Removes every WolfEQ block or block fragment. Restores the exact pre-EnsureBlock text.</summary>
    public static string RemoveBlock(string configText)
    {
        ArgumentNullException.ThrowIfNull(configText);

        var (bom, body) = SplitByteOrderMark(configText);
        var builder = new StringBuilder(configText.Length);
        builder.Append(bom);
        AppendLines(builder, RemainingLines(body));
        return builder.ToString();
    }

    /// <summary>True when the canonical block sits at the top and no stray fragments exist elsewhere (test helper).</summary>
    internal static bool HasBlock(string configText)
        => string.Equals(EnsureBlock(configText), configText, StringComparison.Ordinal);

    public static string GetConfigPath(string configDirectory) => Path.Combine(configDirectory, ConfigFileName);

    public static string GetIncludePath(string configDirectory) => Path.Combine(configDirectory, IncludeFileName);

    /// <summary>Decodes config.txt bytes and reports how to re-encode them identically.</summary>
    public static string DecodeConfig(byte[] bytes, out ConfigEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var span = bytes.AsSpan();

        if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            try
            {
                var text = StrictUtf8.GetString(span[3..]);
                encoding = ConfigEncoding.Utf8Bom;
                return text;
            }
            catch (DecoderFallbackException)
            {
                encoding = ConfigEncoding.Utf8BomLatin1;
                return Encoding.Latin1.GetString(span[3..]);
            }
        }

        if (span.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            encoding = ConfigEncoding.Utf16LeBom;
            return Encoding.Unicode.GetString(span[2..]);
        }

        if (span.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            encoding = ConfigEncoding.Utf16BeBom;
            return Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        try
        {
            var text = StrictUtf8.GetString(span);
            encoding = ConfigEncoding.Utf8;
            return text;
        }
        catch (DecoderFallbackException)
        {
            encoding = ConfigEncoding.Latin1;
            return Encoding.Latin1.GetString(span);
        }
    }

    public static byte[] EncodeConfig(string text, ConfigEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);

        return encoding switch
        {
            ConfigEncoding.Utf8Bom => [0xEF, 0xBB, 0xBF, .. StrictUtf8.GetBytes(text)],
            ConfigEncoding.Utf16LeBom => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)],
            ConfigEncoding.Utf16BeBom => [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)],
            ConfigEncoding.Latin1 => Encoding.Latin1.GetBytes(text),
            ConfigEncoding.Utf8BomLatin1 => [0xEF, 0xBB, 0xBF, .. Encoding.Latin1.GetBytes(text)],
            _ => StrictUtf8.GetBytes(text)
        };
    }

    private static (string Bom, string Body) SplitByteOrderMark(string text)
        => text.Length > 0 && text[0] == ByteOrderMark ? (text[..1], text[1..]) : ("", text);

    /// <summary>All lines that are not part of a WolfEQ block or block fragment.</summary>
    private static List<Line> RemainingLines(string text)
    {
        var lines = SplitLines(text);
        var managed = new bool[lines.Count];

        for (var i = 0; i < lines.Count; i++)
        {
            if (IsLine(lines[i], StartMarker))
            {
                // Start marker, then the body lines in order (Device: all, Include, and the trailing
                // Device: all added in 0.8; older blocks lack it), then the end marker if present.
                managed[i] = true;
                var j = i + 1;
                if (j < lines.Count && IsDeviceAll(lines[j])) managed[j++] = true;
                if (j < lines.Count && IsOurInclude(lines[j]))
                {
                    managed[j++] = true;
                    if (j < lines.Count && IsDeviceAll(lines[j])) managed[j++] = true;
                }

                if (j < lines.Count && IsLine(lines[j], EndMarker)) managed[j++] = true;
                i = j - 1;
            }
            else if (IsLine(lines[i], EndMarker))
            {
                // Orphan end marker: its start was deleted, so take the body lines directly above it.
                managed[i] = true;
                var k = i - 1;
                if (k >= 1 && !managed[k] && IsDeviceAll(lines[k]) && IsOurInclude(lines[k - 1])) managed[k--] = true;
                if (k >= 0 && !managed[k] && IsOurInclude(lines[k])) managed[k--] = true;
                if (k >= 0 && !managed[k] && IsDeviceAll(lines[k])) managed[k] = true;
            }
        }

        var remaining = new List<Line>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!managed[i])
            {
                remaining.Add(lines[i]);
            }
        }

        return remaining;
    }

    private static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(new Line(text[start..i], "\n"));
                start = i + 1;
            }
            else if (text[i] == '\r')
            {
                var ending = i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
                lines.Add(new Line(text[start..i], ending));
                i += ending.Length - 1;
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(new Line(text[start..], ""));
        }

        return lines;
    }

    /// <summary>CRLF unless LF-only endings outnumber CRLF; new files get CRLF like Windows tools write.</summary>
    private static string DominantEnding(IReadOnlyList<Line> lines)
    {
        var crlf = 0;
        var lf = 0;
        foreach (var line in lines)
        {
            if (line.Ending == "\r\n") crlf++;
            else if (line.Ending == "\n") lf++;
        }

        return lf > crlf ? "\n" : "\r\n";
    }

    private static void AppendLines(StringBuilder builder, IEnumerable<Line> lines)
    {
        foreach (var line in lines)
        {
            builder.Append(line.Content).Append(line.Ending);
        }
    }

    private static bool IsLine(Line line, string expected)
        => string.Equals(line.Content.Trim(), expected, StringComparison.Ordinal);

    private static bool IsDeviceAll(Line line) => Compact(line.Content).Equals("Device:all", StringComparison.OrdinalIgnoreCase);

    private static bool IsOurInclude(Line line)
        => Compact(line.Content).Equals("Include:" + IncludeFileName, StringComparison.OrdinalIgnoreCase);

    private static string Compact(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
}
