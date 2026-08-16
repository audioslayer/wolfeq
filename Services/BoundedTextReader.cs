using System.IO;
using System.Text;

namespace WolfEQ.Services;

public static class BoundedTextReader
{
    public const int PresetMaxBytes = 2 * 1024 * 1024;
    public const int LibraryMaxBytes = 25 * 1024 * 1024;
    public const int SettingsMaxBytes = 1024 * 1024;

    public static string ReadAllText(string path, int maxBytes, string contentLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes)
        {
            throw new FormatException($"{contentLabel} is too large. Maximum supported size is {FormatSize(maxBytes)}.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        if (text.Length > maxBytes)
        {
            throw new FormatException($"{contentLabel} expands beyond the {FormatSize(maxBytes)} safety limit.");
        }

        return text;
    }

    public static void EnsureTextWithinLimit(string text, int maxCharacters, string contentLabel)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > maxCharacters)
        {
            throw new FormatException($"{contentLabel} exceeds the {FormatSize(maxCharacters)} safety limit.");
        }
    }

    private static string FormatSize(int bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / (1024 * 1024)} MB"
            : bytes >= 1024
                ? $"{bytes / 1024} KB"
                : $"{bytes} bytes";
}
