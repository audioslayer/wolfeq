using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using WolfEQ.Models;
using WolfEQ.Services;

namespace WolfEQ.ViewModels;

/// <summary>
/// State for the setup wizard. Device picks go through the quick switcher's guarded, read-only connect, and
/// nothing here writes to a DAC.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _isScanningSetup;
    private bool _isFirstRun;

    /// <summary>Supported devices plugged in right now.</summary>
    public ObservableCollection<QuickDeviceOption> SetupDevices { get; } = [];

    public bool HasSetupDevices => SetupDevices.Count > 0;

    public IEnumerable<FiioDeviceProfile> SetupModels => DeviceProfiles.Where(profile => !SystemEqProfiles.IsSystemEq(profile));

    /// <summary>A new user: nothing saved, nothing chosen, and the wizard has not been closed before.</summary>
    public bool ShouldOfferSetup => _isFirstRun && !_binding.SetupCompleted && !IsSystemEqSelected;

    /// <summary>False until a device has been found, saved or chosen, so a new user is not told they own a K13.</summary>
    public bool HasChosenDevice => IsSystemEqSelected || IsDeviceConnected || _binding.UsbPath is not null || _binding.ProfileId is not null
                                   || !ReferenceEquals(SelectedDeviceProfile, FiioDeviceProfiles.Default);

    public string DeviceDockTitle => HasChosenDevice ? SelectedDeviceProfile.DisplayLabel : "No device selected";

    public string DeviceDockImagePath => HasChosenDevice
        ? SelectedDeviceProfile.ProductImagePath
        : "pack://application:,,,/WolfEQ;component/Assets/devices/fiio-generic.png";

    public IReadOnlyList<string> SetupTips => BuildSetupTips(SelectedDeviceProfile);

    public string SetupConnectionText => IsSystemEqSelected
        ? SystemEqStatusText
        : IsDeviceConnected ? "Connected" : "Not connected yet. You can finish setup and plug it in later.";

    internal static IReadOnlyList<string> BuildSetupTips(FiioDeviceProfile profile)
    {
        var tips = new List<string>();
        if (SystemEqProfiles.IsSystemEq(profile))
        {
            tips.Add("Software EQ works with any Windows output through Equalizer APO, which is free and included.");
            tips.Add("Your EQ plays on this PC only. It is not stored on a device.");
            return tips;
        }

        if (profile.UsesDspSerial)
        {
            tips.Add("Connect two USB cables: your normal audio cable, and one from the rear RS232 port to this PC.");
            tips.Add("Close FiiO's DSP app and web control first. Only one app can use that port.");
            tips.Add("This device has no preamp. Turn its volume down before you save a boosted EQ.");
        }
        else if (profile.Id == FiioDeviceProfiles.K13R2R.Id)
        {
            tips.Add("On the K13, set USB to UAC 2.0 in its menu, or Windows may not find it.");
            tips.Add("Bluetooth is optional. It adds lights and input switching, in Settings › Your device.");
        }
        else if (profile.Id == FiioDeviceProfiles.TrnBlackPearl.Id)
        {
            tips.Add("Plug headphones into it first, or Windows will not see it.");
        }

        if (profile.KtMicro is not null)
        {
            tips.Add("Set its Windows output to 48 kHz. At other rates the EQ plays at the wrong frequencies.");
            tips.Add("It has no preamp. Pause your music and turn the volume down before you save: it restarts each time.");
        }
        else if (profile.WalkPlay is { EmulateHighShelf: true })
        {
            tips.Add("High shelves are stored as a low shelf plus preamp, so they look different when you load them back.");
        }

        tips.Add(profile.SupportsLiveEqWrites
            ? "Live sync: you hear changes on the device as you make them. Press Write to keep them in a slot."
            : "Shape your EQ, then press Write to save it to the device.");
        tips.Add(profile.WritableSlots.Count == 1
            ? "It has one slot, so each save replaces the last one."
            : $"It has {profile.WritableSlots.Count} USER slots. Start with one you don't mind replacing.");
        if (!profile.IsVerified)
        {
            tips.Add("Support is experimental. Start with a low-gain EQ and your volume down.");
        }

        return tips;
    }

    /// <summary>Read-only scan. The list only changes when the devices do, so it does not flicker.</summary>
    internal async Task RefreshSetupDevicesAsync()
    {
        if (_isScanningSetup || _deviceBusy || _integrationBusy) return;
        _isScanningSetup = true;
        try
        {
            var candidates = QuickDeviceScanOverride is { } scan
                ? await scan()
                : FiioK13DeviceService.GetSelectableDevices((await new FiioK13DeviceService().DetectUsbAsync()).Candidates);
            var options = BuildQuickDeviceOptions(candidates, SelectedDeviceProfile, _binding.UsbPath, _binding.Name, IsDeviceConnected, string.Empty)
                .Where(option => option.Kind == QuickDeviceKind.Usb)
                .ToArray();
            if (!options.SequenceEqual(SetupDevices))
            {
                SetupDevices.Clear();
                foreach (var option in options) SetupDevices.Add(option);
                OnPropertyChanged(nameof(HasSetupDevices));
            }
        }
        catch (Exception ex)
        {
            AddLog($"Setup device scan failed: {ex.Message}");
        }
        finally
        {
            _isScanningSetup = false;
        }
    }

    internal async Task ChooseSetupDeviceAsync(QuickDeviceOption option)
    {
        if (option.IsCurrent && !IsDeviceConnected)
        {
            // Already chosen, but plugged in since: the quick switcher would do nothing.
            await (QuickDeviceConnectOverride?.Invoke() ?? DetectDeviceAsync());
        }
        else
        {
            await SwitchToQuickDeviceAsync(option);
        }

        if (ReferenceEquals(SelectedDeviceProfile, option.Profile)) RememberModel(option.Profile);
        await RefreshSetupDevicesAsync();
    }

    /// <summary>Picks a model that is not plugged in, or Software EQ.</summary>
    internal void ChooseSetupModel(FiioDeviceProfile profile)
    {
        SelectedDeviceProfile = profile;
        if (ReferenceEquals(SelectedDeviceProfile, profile)) RememberModel(profile);
    }

    /// <summary>Saves the previewed headphone preset to the library and loads it in the editor. False when that did not happen.</summary>
    internal async Task<bool> LoadSetupPresetAsync()
    {
        var loaded = false;
        void OnLoaded(object? sender, EventArgs e) => loaded = true;
        OnlineProfileOpenedInEditor += OnLoaded;
        try { await LoadOnlineProfileIntoEditorAsync(); }
        finally { OnlineProfileOpenedInEditor -= OnLoaded; }
        return loaded;
    }

    internal void CompleteSetup()
    {
        if (_binding.SetupCompleted) return;
        _binding.SetupCompleted = true;
        SaveBinding();
    }

    private void RememberModel(FiioDeviceProfile profile)
    {
        if (!SystemEqProfiles.IsSystemEq(profile) && _binding.ProfileId != profile.Id)
        {
            _binding.ProfileId = profile.Id;
            SaveBinding();
        }

        RaiseSetupChanged();
    }

    /// <summary>Opens on the last used model even when it is unplugged. Older settings only saved its USB path.</summary>
    internal void RestoreRememberedModel()
    {
        if (IsSystemEqSelected) return;
        var profile = FiioDeviceProfiles.All.FirstOrDefault(candidate => candidate.Id == _binding.ProfileId)
                      ?? MatchSavedPath(_binding.UsbPath);
        if (profile is not null && !ReferenceEquals(profile, SelectedDeviceProfile)) ApplyDeviceProfile(profile);
    }

    internal static FiioDeviceProfile? MatchSavedPath(string? path)
    {
        var match = Regex.Match(path ?? string.Empty, "vid_(2972|3302)&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase);
        return match.Success ? FiioDeviceProfiles.Match(Convert.ToUInt16(match.Groups[2].Value, 16), null, null) : null;
    }

    internal void RaiseSetupChanged()
    {
        OnPropertyChanged(nameof(HasChosenDevice));
        OnPropertyChanged(nameof(DeviceDockTitle));
        OnPropertyChanged(nameof(DeviceDockImagePath));
        OnPropertyChanged(nameof(SetupTips));
        OnPropertyChanged(nameof(SetupConnectionText));
    }
}
