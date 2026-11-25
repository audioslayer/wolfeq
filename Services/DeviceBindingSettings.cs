using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WolfEQ.Services;

public sealed class DeviceBindingSettings
{
    public string? UsbPath { get; set; }
    public ulong? BluetoothAddress { get; set; }
    public string BluetoothName { get; set; } = "K13";
    public string Name { get; set; } = "Desk K13";
    public bool AutoSlotLighting { get; set; }
    /// <summary>Last used model, so an unplugged device still opens as itself.</summary>
    public string? ProfileId { get; set; }
    public bool SetupCompleted { get; set; }

    internal static bool Exists => File.Exists(SettingsPath);

    /// <summary>Test hook: keeps tests away from the user's saved device, slot names and lights.</summary>
    internal static string? DataDirectoryOverride { get; set; }

    internal static string DataDirectory => DataDirectoryOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WolfEQ");

    private static string SettingsPath => Path.Combine(DataDirectory, "device-binding.json");

    /// <summary>
    /// True when a USB path changed but still names the same model and interface. Windows changes the rest of the path
    /// after a driver update or on another port, and may add a revision (rev_0115).
    /// </summary>
    internal static bool IsSameModelOnNewPath(string? oldPath, string? newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)
            || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) return false;
        static string? Part(string path, string key)
        {
            var match = Regex.Match(path, $@"[#&]{key}_?([0-9a-f]+)(?=[#&])", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }
        return Part(oldPath, "vid") is not null && Part(oldPath, "pid") is not null
            && new[] { "vid", "pid", "mi", "col" }.All(key => Part(oldPath, key) == Part(newPath, key));
    }

    public static DeviceBindingSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        return JsonSerializer.Deserialize<DeviceBindingSettings>(BoundedTextReader.ReadAllText(
            SettingsPath, BoundedTextReader.SettingsMaxBytes, "Device binding")) ?? new();
    }

    public void Save() => AtomicFileWriter.WriteAllText(SettingsPath,
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
