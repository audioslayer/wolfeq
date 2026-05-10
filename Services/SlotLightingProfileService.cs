using System.IO;
using System.Text.Json;

namespace WolfEQ.Services;

public sealed class SlotLightingProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SlotLightingProfileService()
    {
        StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WolfEQ",
            "slot-lighting.json");
    }

    public string StoragePath { get; }

    public IReadOnlyDictionary<int, SlotLightingProfileData> Load()
    {
        if (!File.Exists(StoragePath))
        {
            return new Dictionary<int, SlotLightingProfileData>();
        }

        var json = File.ReadAllText(StoragePath);
        var profiles = JsonSerializer.Deserialize<List<SlotLightingProfileData>>(json) ?? [];
        return profiles
            .Where(profile => profile.Slot is >= 1 and <= 10)
            .GroupBy(profile => profile.Slot)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    public void Save(IEnumerable<SlotLightingProfileData> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        File.WriteAllText(StoragePath, JsonSerializer.Serialize(profiles.OrderBy(profile => profile.Slot), JsonOptions));
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
