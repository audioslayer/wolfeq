using System.IO;
using System.Text.Json;
using WolfEQ.Services;

namespace WolfEQ.ViewModels;

public sealed partial class MainViewModel
{
    private readonly DeviceSlotNameService _slotNames = new();

    private void RefreshLocalSlotNames()
    {
        if (DeviceUserPresets is null || SelectedDeviceProfile is null) return;
        Dictionary<byte, string> names;
        try { names = _slotNames.Load(_binding.UsbPath, SelectedDeviceProfile.Id); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            AddLog($"Could not load local slot names: {ex.Message}");
            names = [];
        }
        for (var index = 0; index < DeviceUserPresets.Count; index++)
        {
            var previous = DeviceUserPresets[index];
            names.TryGetValue(previous.PresetId, out var name);
            var updated = previous with { LocalEqName = name };
            if (updated == previous) continue;
            DeviceUserPresets[index] = updated;
            if (_selectedDeviceUserPreset?.PresetId == updated.PresetId)
            {
                _selectedDeviceUserPreset = updated;
                OnPropertyChanged(nameof(SelectedDeviceUserPreset));
            }
        }
    }

    private void RememberSlotName(string? devicePath, string profileId, byte slotId, string name)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return;
        try { _slotNames.Save(devicePath, profileId, slotId, name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            AddLog($"The local slot name could not be saved: {ex.Message}");
        }
        RefreshLocalSlotNames();
    }
}
