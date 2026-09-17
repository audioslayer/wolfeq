using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace WolfEQ.Services;

public sealed class TrayPreferences : INotifyPropertyChanged
{
    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "WolfEQ", "tray-settings.json");
    public static TrayPreferences Current { get; } = Load();
    private bool _closeToTray = true;
    private bool _minimizeToTray;
    private string _saveError = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string SaveError => _saveError;

    public bool CloseToTray { get => _closeToTray; set => Set(ref _closeToTray, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => Set(ref _minimizeToTray, value); }

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        try
        {
            AtomicFileWriter.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(CloseToTray, MinimizeToTray)));
            _saveError = "";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unable to save tray preferences: {ex.Message}");
            _saveError = "Tray preferences could not be saved. These choices apply until WolfEQ closes.";
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveError)));
    }

    private static TrayPreferences Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(BoundedTextReader.ReadAllText(
                    SettingsPath, BoundedTextReader.SettingsMaxBytes, "Tray preferences"));
                if (settings is not null)
                    return new TrayPreferences { _closeToTray = settings.CloseToTray, _minimizeToTray = settings.MinimizeToTray };
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Unable to load tray preferences: {ex.Message}"); }
        return new TrayPreferences();
    }

    private sealed record Settings(bool CloseToTray = true, bool MinimizeToTray = false);
}
