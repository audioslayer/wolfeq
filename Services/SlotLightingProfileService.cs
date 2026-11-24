using System.IO;
using System.Text.Json;

namespace WolfEQ.Services;

public sealed class SlotLightingProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SlotLightingProfileService()
    {
        StoragePath = Path.Combine(DeviceBindingSettings.DataDirectory, "slot-lighting.json");
    }

    public string StoragePath { get; private set; }

    public void SetDevice(string? devicePath) => StoragePath = PathFor(devicePath);

    /// <summary>Carries a device's lights to its new USB path. Never replaces lights already there.</summary>
    public static void CopyToNewPath(string oldPath, string newPath)
    {
        var source = PathFor(oldPath);
        var target = PathFor(newPath);
        if (File.Exists(source) && !File.Exists(target)) File.Copy(source, target);
    }

    private static string PathFor(string? devicePath)
    {
        var key = string.IsNullOrEmpty(devicePath) ? "unassigned" : Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(devicePath.ToUpperInvariant())))[..24];
        return Path.Combine(DeviceBindingSettings.DataDirectory, $"slot-lighting-{key}.json");
    }

    public IReadOnlyDictionary<int, SlotLightingProfileData> Load()
    {
        if (!File.Exists(StoragePath))
        {
            return new Dictionary<int, SlotLightingProfileData>();
        }

        var json = BoundedTextReader.ReadAllText(
            StoragePath,
            BoundedTextReader.SettingsMaxBytes,
            "Slot lighting settings");
        var profiles = JsonSerializer.Deserialize<List<SlotLightingProfileData>>(json) ?? [];
        return profiles
            .Where(profile => profile.Slot is >= 1 and <= 10)
            .GroupBy(profile => profile.Slot)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    public void Save(IEnumerable<SlotLightingProfileData> profiles)
    {
        AtomicFileWriter.WriteAllText(
            StoragePath,
            JsonSerializer.Serialize(profiles.OrderBy(profile => profile.Slot), JsonOptions));
    }
}

public sealed record SlotLightingProfileData
{
    public int Slot { get; init; }
    public bool TopOn { get; init; } = true;
    public byte TopColor { get; init; } = 0x07;
    public byte TopMode { get; init; }
    public bool KnobOn { get; init; } = true;
    public byte KnobColor { get; init; } = 0x03;
    public byte KnobMode { get; init; }
}
