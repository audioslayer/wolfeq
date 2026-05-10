using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WolfEQ.Models;

namespace WolfEQ.Services;

public static partial class EqualizerApoPresetCodec
{
    public static EqPreset Parse(string text, string name = "Imported Equalizer APO preset")
    {
        var preampDb = 0.0;
        var bands = new List<EqBand>();

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var preampMatch = PreampRegex().Match(line);
            if (preampMatch.Success)
            {
                preampDb = ParseDouble(preampMatch.Groups[1].Value);
                continue;
            }

            var filterMatch = FilterRegex().Match(line);
            if (!filterMatch.Success)
            {
                continue;
            }

            var number = int.Parse(filterMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var enabled = string.Equals(filterMatch.Groups[2].Value, "ON", StringComparison.OrdinalIgnoreCase);
            var type = ParseFilterType(filterMatch.Groups[3].Value);
            var frequency = (int)Math.Round(ParseDouble(filterMatch.Groups[4].Value));
            var gain = ParseDouble(filterMatch.Groups[5].Value);
            var q = ParseDouble(filterMatch.Groups[6].Value);

            bands.Add(new EqBand
            {
                Number = number,
                Enabled = enabled,
                FilterType = type,
                FrequencyHz = frequency,
                GainDb = gain,
                Q = q
            });
        }

        return new EqPreset
        {
            Name = name,
            PreampDb = preampDb,
            Bands = new ObservableCollection<EqBand>(NormalizeBandNumbers(bands))
        };
    }

    public static string Export(EqPreset preset)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Preamp: {Format(preset.PreampDb)} dB");

        foreach (var band in preset.Bands.OrderBy(b => b.Number))
        {
            builder.Append("Filter ")
                .Append(band.Number)
                .Append(": ")
                .Append(band.Enabled ? "ON" : "OFF")
                .Append(' ')
                .Append(ToApoType(band.FilterType))
                .Append(" Fc ")
                .Append(Format(band.FrequencyHz))
                .Append(" Hz Gain ")
                .Append(Format(band.GainDb))
                .Append(" dB Q ")
                .Append(Format(band.Q))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static IEnumerable<EqBand> NormalizeBandNumbers(IReadOnlyCollection<EqBand> bands)
    {
        var ordered = bands.OrderBy(b => b.Number).Take(10).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var band = ordered[i];
            yield return new EqBand
            {
                Number = i + 1,
                Enabled = band.Enabled,
                FilterType = band.FilterType,
                FrequencyHz = band.FrequencyHz,
                GainDb = band.GainDb,
                Q = band.Q
            };
        }

        for (var i = ordered.Count + 1; i <= 10; i++)
        {
            yield return new EqBand
            {
                Number = i,
                Enabled = false,
                FilterType = EqFilterType.Peak,
                FrequencyHz = 1000,
                GainDb = 0,
                Q = 1
            };
        }
    }

    private static EqFilterType ParseFilterType(string value) => value.ToUpperInvariant() switch
    {
        "PK" or "PEQ" or "PEAK" => EqFilterType.Peak,
        "LS" or "LSC" or "LOW_SHELF" or "LOWSHELF" => EqFilterType.LowShelf,
        "HS" or "HSC" or "HIGH_SHELF" or "HIGHSHELF" => EqFilterType.HighShelf,
        "BP" or "BPF" or "BAND_PASS" or "BANDPASS" => EqFilterType.BandPass,
        "LP" or "LPF" or "LOW_PASS" or "LOWPASS" => EqFilterType.LowPass,
        "HP" or "HPF" or "HIGH_PASS" or "HIGHPASS" => EqFilterType.HighPass,
        "AP" or "APF" or "ALL_PASS" or "ALLPASS" => EqFilterType.AllPass,
        _ => EqFilterType.Peak
    };

    private static string ToApoType(EqFilterType type) => type switch
    {
        EqFilterType.LowShelf => "LSC",
        EqFilterType.HighShelf => "HSC",
        EqFilterType.BandPass => "BP",
        EqFilterType.LowPass => "LP",
        EqFilterType.HighPass => "HP",
        EqFilterType.AllPass => "AP",
        _ => "PK"
    };

    private static double ParseDouble(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^Preamp:\s*([-+]?\d+(?:\.\d+)?)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex PreampRegex();

    [GeneratedRegex(@"^Filter\s+(\d+)\s*:\s*(ON|OFF)\s+([A-Z_]+)\s+Fc\s+([-+]?\d+(?:\.\d+)?)\s+Hz\s+Gain\s+([-+]?\d+(?:\.\d+)?)\s+dB\s+Q\s+([-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex FilterRegex();
}
