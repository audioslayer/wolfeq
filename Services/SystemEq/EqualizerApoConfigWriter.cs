using System.Globalization;
using System.Text;
using WolfEQ.Models;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Builds the text of WolfEQ.txt, the Equalizer APO include file WolfEQ owns (KTD4-KTD6).
/// Pure: no IO. The caller writes the result with <see cref="Encoding"/> (UTF-8 without BOM).
/// </summary>
public static class EqualizerApoConfigWriter
{
    public const string HeaderComment = "# Managed by WolfEQ - changes here are overwritten";

    private const string NewLine = "\r\n";

    /// <summary>UTF-8 without BOM (KTD6).</summary>
    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Header, <c>Device:</c> line, <c>Preamp:</c>, then one <c>Filter N:</c> line per band.
    /// Bypassed or no valid endpoints produces the header only, which removes WolfEQ's effect.
    /// </summary>
    public static string Build(EqPreset preset, IReadOnlyList<string> endpointGuids, bool bypassed)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var builder = new StringBuilder();
        builder.Append(HeaderComment).Append(NewLine);

        var guids = NormalizeEndpointGuids(endpointGuids);
        if (bypassed || guids.Count == 0)
        {
            return builder.ToString();
        }

        // Device: inside an included file is scoped to that file, so it never leaks into config.txt.
        builder.Append("Device: ").Append(string.Join("; ", guids)).Append(NewLine);
        builder.Append("Preamp: ").Append(FormatNumber(preset.PreampDb)).Append(" dB").Append(NewLine);

        foreach (var band in preset.Bands.OrderBy(b => b.Number))
        {
            builder.Append(FormatFilterLine(band)).Append(NewLine);
        }

        return builder.ToString();
    }

    /// <summary>
    /// One APO filter line. Peak and shelves carry Gain; band-pass, low/high-pass and all-pass do not,
    /// because APO rejects a Gain token on those types.
    /// </summary>
    public static string FormatFilterLine(EqBand band)
    {
        ArgumentNullException.ThrowIfNull(band);

        var (code, hasGain) = band.FilterType switch
        {
            EqFilterType.Peak => ("PK", true),
            EqFilterType.LowShelf => ("LSC", true),
            EqFilterType.HighShelf => ("HSC", true),
            EqFilterType.BandPass => ("BP", false),
            EqFilterType.LowPass => ("LPQ", false),
            EqFilterType.HighPass => ("HPQ", false),
            EqFilterType.AllPass => ("AP", false),
            _ => ("PK", true)
        };

        var builder = new StringBuilder()
            .Append("Filter ").Append(band.Number.ToString(CultureInfo.InvariantCulture)).Append(": ")
            .Append(band.Enabled ? "ON" : "OFF").Append(' ')
            .Append(code)
            .Append(" Fc ").Append(FormatNumber(band.FrequencyHz)).Append(" Hz");

        if (hasGain)
        {
            builder.Append(" Gain ").Append(FormatNumber(band.GainDb)).Append(" dB");
        }

        return builder.Append(" Q ").Append(FormatNumber(band.Q)).ToString();
    }

    /// <summary>
    /// Invariant culture, no grouping, at most 2 decimals. APO reads a 3-decimal Fc such as
    /// <c>1.234</c> as a thousands separator, so more precision is never written.
    /// </summary>
    public static string FormatNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            return "0";
        }

        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        // Avoid "-0" for tiny negatives and negative zero.
        if (rounded == 0)
        {
            rounded = 0;
        }

        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Keeps only real GUIDs, braced, trimmed and de-duplicated (case-insensitive), in input order.
    /// Anything else is dropped so a bad id can never inject extra lines or <c>;</c>-separated devices.
    /// </summary>
    public static IReadOnlyList<string> NormalizeEndpointGuids(IEnumerable<string>? endpointGuids)
    {
        var result = new List<string>();
        if (endpointGuids is null)
        {
            return result;
        }

        var seen = new HashSet<Guid>();
        foreach (var raw in endpointGuids)
        {
            var candidate = raw?.Trim();
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            if (Guid.TryParseExact(candidate, "B", out var braced))
            {
                if (seen.Add(braced))
                {
                    result.Add(candidate);
                }
            }
            else if (Guid.TryParseExact(candidate, "D", out var bare))
            {
                if (seen.Add(bare))
                {
                    result.Add("{" + candidate + "}");
                }
            }
        }

        return result;
    }
}
