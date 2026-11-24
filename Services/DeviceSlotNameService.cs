using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WolfEQ.Services;

/// <summary>Local labels for the last EQ written to each physical device's slots.</summary>
public sealed class DeviceSlotNameService(string? directory = null)
{
    private readonly string _directory = directory ?? DeviceBindingSettings.DataDirectory;

    private string FilePath(string devicePath, string profileId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            devicePath.ToUpperInvariant() + "\n" + profileId)));
        return Path.Combine(_directory, $"slot-names-{key}.json");
    }

    public Dictionary<byte, string> Load(string? devicePath, string profileId)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return [];
        var path = FilePath(devicePath, profileId);
        if (!File.Exists(path)) return [];
        var json = BoundedTextReader.ReadAllText(path, BoundedTextReader.SettingsMaxBytes, "Device slot names");
        return JsonSerializer.Deserialize<Dictionary<byte, string>>(json) ?? [];
    }

    /// <summary>Carries a device's names to its new USB path. Never replaces names already there.</summary>
    public void CopyToNewPath(string oldPath, string newPath, string profileId)
    {
        var source = FilePath(oldPath, profileId);
        var target = FilePath(newPath, profileId);
        if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
    }

    public void Save(string devicePath, string profileId, byte slotId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var names = Load(devicePath, profileId);
        names[slotId] = name.Trim();
        AtomicFileWriter.WriteAllText(FilePath(devicePath, profileId), JsonSerializer.Serialize(names));
    }
}
