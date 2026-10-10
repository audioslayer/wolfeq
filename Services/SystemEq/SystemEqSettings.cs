using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Persisted Windows System EQ choice (<c>%LOCALAPPDATA%\WolfEQ\system-eq.json</c>):
/// whether the mode is selected, which output endpoints are targeted, and bypass.
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
                    filePath, BoundedTextReader.SettingsMaxBytes, "System EQ settings"));
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
        settings.FilePath = filePath;
        return settings;
    }

    public void Save() => AtomicFileWriter.WriteAllText(FilePath,
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
