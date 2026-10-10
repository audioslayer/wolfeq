using System.Collections.ObjectModel;
using System.Windows.Input;
using WolfEQ.Models;
using WolfEQ.Services;

namespace WolfEQ.ViewModels;

/// <summary>
/// The device dock's quick-switch menu: Software EQ plus every FiiO device currently
/// plugged in over USB. Picking one goes through the same guarded profile switch and the
/// same read-only USB connect as Settings; nothing is written to a device by switching.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _isScanningQuickDevices;
    private ICommand? _refreshQuickDevicesCommand;
    private ICommand? _switchToQuickDeviceCommand;

    public ObservableCollection<QuickDeviceOption> QuickDevices { get; } = [];

    /// <summary>True while the menu has nothing but Software EQ, so it can hint at plugging in a device.</summary>
    public bool HasOnlySystemEqQuickDevice => QuickDevices.Count == 1 && !IsScanningQuickDevices;

    public bool IsScanningQuickDevices
    {
        get => _isScanningQuickDevices;
        private set
        {
            SetField(ref _isScanningQuickDevices, value);
            OnPropertyChanged(nameof(HasOnlySystemEqQuickDevice));
        }
    }

    public ICommand RefreshQuickDevicesCommand
        => _refreshQuickDevicesCommand ??= new AsyncRelayCommand(RefreshQuickDevicesAsync);

    public ICommand SwitchToQuickDeviceCommand
        => _switchToQuickDeviceCommand ??= new AsyncParameterRelayCommand(
            parameter => parameter is QuickDeviceOption option ? SwitchToQuickDeviceAsync(option) : Task.CompletedTask);

    /// <summary>Test hook: replaces the real read-only USB scan.</summary>
    internal Func<Task<IReadOnlyList<HidDeviceCandidate>>>? QuickDeviceScanOverride { get; set; }

    /// <summary>Test hook: replaces the real connect after a FiiO device is picked.</summary>
    internal Func<Task>? QuickDeviceConnectOverride { get; set; }

    /// <summary>Rebuilds the list immediately from what is known, then rescans USB.</summary>
    internal async Task RefreshQuickDevicesAsync()
    {
        RebuildQuickDevices(null);
        if (IsScanningQuickDevices || _deviceBusy)
        {
            return;
        }

        IsScanningQuickDevices = true;
        try
        {
            IReadOnlyList<HidDeviceCandidate> candidates;
            if (QuickDeviceScanOverride is { } scan)
            {
                candidates = await scan();
            }
            else
            {
                var result = await _deviceService.DetectUsbAsync();
                candidates = FiioK13DeviceService.GetSelectableDevices(result.Candidates);
            }

            RebuildQuickDevices(candidates);
        }
        catch (Exception ex)
        {
            AddLog($"Device scan for the quick switcher failed: {ex.Message}");
        }
        finally
        {
            IsScanningQuickDevices = false;
        }
    }

    private void RebuildQuickDevices(IReadOnlyList<HidDeviceCandidate>? usbCandidates)
    {
        var options = BuildQuickDeviceOptions(
            usbCandidates,
            SelectedDeviceProfile,
            _binding.UsbPath,
            _binding.Name,
            IsDeviceConnected,
            IsSystemEqSelected ? SystemEqStatusText : "Applies your EQ to any Windows output");
        QuickDevices.Clear();
        foreach (var option in options)
        {
            QuickDevices.Add(option);
        }

        OnPropertyChanged(nameof(HasOnlySystemEqQuickDevice));
    }

    /// <summary>
    /// Pure list builder. When no scan has run yet, the saved USB device is still offered so the
    /// menu is useful instantly; a scan then replaces it with what is actually plugged in.
    /// </summary>
    internal static IReadOnlyList<QuickDeviceOption> BuildQuickDeviceOptions(
        IReadOnlyList<HidDeviceCandidate>? usbCandidates,
        FiioDeviceProfile currentProfile,
        string? boundUsbPath,
        string? boundName,
        bool isConnected,
        string systemEqSubtitle)
    {
        var isSystemEq = SystemEqProfiles.IsSystemEq(currentProfile);
        var options = new List<QuickDeviceOption>
        {
            new(QuickDeviceKind.SystemEq, SystemEqProfiles.WindowsSystemEq, null,
                SystemEqProfiles.WindowsSystemEq.DisplayName, systemEqSubtitle, isSystemEq)
        };

        if (usbCandidates is null)
        {
            if (!string.IsNullOrWhiteSpace(boundUsbPath) && !isSystemEq)
            {
                options.Add(new(QuickDeviceKind.Usb, currentProfile, boundUsbPath,
                    currentProfile.DisplayLabel,
                    isConnected ? "Connected · USB" : "Saved device · checking...",
                    IsCurrent: true));
            }

            return options;
        }

        foreach (var candidate in usbCandidates)
        {
            var profile = FiioDeviceProfiles.Match(candidate.ProductId, candidate.Product, candidate.InterfaceNumber);
            if (profile is null)
            {
                continue;
            }

            var isBound = string.Equals(candidate.DevicePath, boundUsbPath, StringComparison.OrdinalIgnoreCase);
            var isCurrent = !isSystemEq && isBound && currentProfile.Id == profile.Id;
            var subtitle = isCurrent && isConnected
                ? "Connected · USB"
                : isBound && !string.IsNullOrWhiteSpace(boundName) && !string.Equals(boundName, profile.DisplayName, StringComparison.Ordinal)
                    ? $"{boundName} · plugged in"
                    : "Plugged in · USB";
            options.Add(new(QuickDeviceKind.Usb, profile, candidate.DevicePath, profile.DisplayLabel, subtitle, isCurrent));
        }

        return options;
    }

    private async Task SwitchToQuickDeviceAsync(QuickDeviceOption option)
    {
        if (option.IsCurrent)
        {
            return;
        }

        if (option.Kind == QuickDeviceKind.SystemEq)
        {
            SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
            RebuildQuickDevices(null);
            return;
        }

        var sameProfile = ReferenceEquals(SelectedDeviceProfile, option.Profile);
        if (sameProfile && EditorSession.WouldReplaceUnsavedEdits
            && !Confirm(new ConfirmationRequest(
                "Switch device?",
                "Switching devices resets the editor. Your unsaved changes will be lost.",
                "Switch device")))
        {
            return;
        }

        // The profile setter asks about unsaved edits itself; a "no" leaves the profile unchanged.
        SelectedDeviceProfile = option.Profile;
        if (!ReferenceEquals(SelectedDeviceProfile, option.Profile))
        {
            return;
        }

        if (!string.Equals(option.UsbPath, _binding.UsbPath, StringComparison.OrdinalIgnoreCase))
        {
            // A different physical unit: its Bluetooth pairing would belong to the old one.
            _binding = new DeviceBindingSettings
            {
                UsbPath = option.UsbPath,
                Name = option.Profile.DisplayName,
                AutoSlotLighting = _binding.AutoSlotLighting
            };
            SaveBinding();
            DeviceNickname = _binding.Name;
            ApplyBinding();
            LoadSlotLightingProfiles();
            LoadSelectedSlotLightingProfileIntoUi();
        }

        if (QuickDeviceConnectOverride is { } connect)
        {
            await connect();
        }
        else
        {
            await DetectDeviceAsync();
        }

        RebuildQuickDevices(null);
    }
}

public enum QuickDeviceKind
{
    SystemEq,
    Usb
}

/// <summary>One row in the device dock's quick-switch menu.</summary>
public sealed record QuickDeviceOption(
    QuickDeviceKind Kind,
    FiioDeviceProfile Profile,
    string? UsbPath,
    string Title,
    string Subtitle,
    bool IsCurrent)
{
    public string ImagePath => Profile.ProductImagePath;
}
