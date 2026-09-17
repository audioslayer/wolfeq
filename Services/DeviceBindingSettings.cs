using System.IO;
using System.Text.Json;

namespace WolfEQ.Services;

public sealed class DeviceBindingSettings
{
    public string? UsbPath { get; set; }
    public ulong? BluetoothAddress { get; set; }
    public string BluetoothName { get; set; } = "K13";
    public string Name { get; set; } = "Desk K13";
    public bool AutoSlotLighting { get; set; }
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "WolfEQ", "device-binding.json");

    public static DeviceBindingSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new();
        return JsonSerializer.Deserialize<DeviceBindingSettings>(BoundedTextReader.ReadAllText(
            SettingsPath, BoundedTextReader.SettingsMaxBytes, "Device binding")) ?? new();
    }

    public void Save() => AtomicFileWriter.WriteAllText(SettingsPath,
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
}
