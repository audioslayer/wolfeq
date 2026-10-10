using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WolfEQ.Models;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Persisted Software EQ choice (<c>%LOCALAPPDATA%\WolfEQ\system-eq.json</c>):
/// whether the mode is selected, which output endpoints are targeted, bypass, and the last
/// editor curve that was successfully applied (restored into the editor at startup).
/// </summary>
public sealed class SystemEqSettings
{
    /// <summary>Test hook: redirects the default settings path away from the real profile.</summary>
    internal static string? DefaultPathOverride { get; set; }

    public static string DefaultPath => DefaultPathOverride ?? Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "WolfEQ", "system-eq.json");

    [JsonPropertyName("selected")]
    public bool Selected { get; set; }

    /// <summary>Full IMMDevice ids of the ticked endpoints, including ones that are currently unplugged.</summary>
    [JsonPropertyName("endpointIds")]
    public List<string> EndpointIds { get; set; } = [];

    [JsonPropertyName("bypassed")]
    public bool Bypassed { get; set; }

    /// <summary>The last non-bypassed editor snapshot Equalizer APO accepted, or null before the first apply.</summary>
    [JsonPropertyName("lastApplied")]
    public SystemEqAppliedSnapshot? LastApplied { get; set; }

    [JsonIgnore]
    public string FilePath { get; private set; } = DefaultPath;

    /// <summary>Loads the settings; a missing, oversized, or corrupt file yields defaults.</summary>
    public static SystemEqSettings Load(string? path = null)
    {
        var filePath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        SystemEqSettings? settings = null;
        try
        {
            if (File.Exists(filePath))
            {
                settings = JsonSerializer.Deserialize<SystemEqSettings>(BoundedTextReader.ReadAllText(
                    filePath, BoundedTextReader.SettingsMaxBytes, "Software EQ settings"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            settings = null;
        }

        settings ??= new SystemEqSettings();
        settings.EndpointIds = (settings.EndpointIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.LastApplied = settings.LastApplied?.Sanitized();
        settings.FilePath = filePath;
        return settings;
    }

    public void Save() => AtomicFileWriter.WriteAllText(FilePath,
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>Compact persisted editor curve: name, preamp, and up to <see cref="MaxBands"/> bands.</summary>
public sealed class SystemEqAppliedSnapshot
{
    public const int MaxBands = 32;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("preampDb")]
    public double PreampDb { get; set; }

    [JsonPropertyName("bands")]
    public List<SystemEqAppliedBand> Bands { get; set; } = [];

    public static SystemEqAppliedSnapshot FromPreset(EqPreset preset) => new()
    {
        Name = preset.Name,
        PreampDb = preset.PreampDb,
        Bands = preset.Bands.Take(MaxBands).Select(band => new SystemEqAppliedBand
        {
            Number = band.Number,
            Enabled = band.Enabled,
            FilterType = band.FilterType,
            FrequencyHz = band.FrequencyHz,
            GainDb = band.GainDb,
            Q = band.Q
        }).ToList()
    };

    /// <summary>Rebuilds an editor preset; <see cref="EqBand"/> clamps every value into range.</summary>
    public EqPreset ToPreset() => new()
    {
        Name = Name,
        PreampDb = PreampDb,
        Bands = new ObservableCollection<EqBand>(Bands.Select((band, index) => new EqBand
        {
            Number = index + 1,
            Enabled = band.Enabled,
            FilterType = band.FilterType,
            FrequencyHz = band.FrequencyHz,
            GainDb = band.GainDb,
            Q = band.Q
        }))
    };

    /// <summary>Drops a corrupt snapshot (no bands, unknown filter types) and bounds the rest.</summary>
    internal SystemEqAppliedSnapshot? Sanitized()
    {
        var bands = (Bands ?? []).Where(band => band is not null).Take(MaxBands).ToList();
        if (bands.Count == 0 || bands.Any(band => !Enum.IsDefined(band.FilterType)))
        {
            return null;
        }

        return new SystemEqAppliedSnapshot
        {
            Name = (Name ?? string.Empty).Trim() is { Length: > 0 and <= 200 } name ? name : "Software EQ",
            PreampDb = double.IsFinite(PreampDb) ? Math.Clamp(PreampDb, -24.0, 12.0) : 0,
            Bands = bands
        };
    }
}

public sealed class SystemEqAppliedBand
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter<EqFilterType>))]
    public EqFilterType FilterType { get; set; }

    [JsonPropertyName("frequencyHz")]
    public int FrequencyHz { get; set; } = 1000;

    [JsonPropertyName("gainDb")]
    public double GainDb { get; set; }

    [JsonPropertyName("q")]
    public double Q { get; set; } = 1;
}
