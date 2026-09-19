using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WolfEQ.Services;

/// <summary>Local labels for the last EQ written to each physical device's slots.</summary>
public sealed class DeviceSlotNameService(string? directory = null)
{
    private readonly string _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WolfEQ");

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

    public void Save(string devicePath, string profileId, byte slotId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var names = Load(devicePath, profileId);
        names[slotId] = name.Trim();
        AtomicFileWriter.WriteAllText(FilePath(devicePath, profileId), JsonSerializer.Serialize(names));
    }
}
