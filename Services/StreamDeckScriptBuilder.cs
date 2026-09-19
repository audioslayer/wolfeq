using System.Globalization;
using System.Text;

namespace WolfEQ.Services;

public static class StreamDeckScriptBuilder
{
    public static string Build(string executable, int slot, string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        if (slot is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(slot));
        var arguments = string.Join(" ", new[] { "--slot", slot.ToString(CultureInfo.InvariantCulture), "--device", devicePath }.Select(QuoteArgument));
        return $"Start-Process -FilePath {PowerShellLiteral(executable)} -ArgumentList {PowerShellLiteral(arguments)} -WindowStyle Hidden -Wait";
    }

    private static string PowerShellLiteral(string text) => "'" + text.Replace("'", "''") + "'";

    // Start-Process joins ArgumentList into one native command line. Preserve its
    // quoting explicitly, including spaces, embedded quotes, and trailing slashes.
    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
