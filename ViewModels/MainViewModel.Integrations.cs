using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using WolfEQ.Services;

namespace WolfEQ.ViewModels;

public sealed partial class MainViewModel
{
    private DeviceBindingSettings _binding = new();
    private UsbDeviceChoice? _usbChoice;
    private BleDeviceChoice? _bluetoothChoice;
    private string _deviceName = "Desk K13";
    private bool _integrationBusy;
    private int _integrationSlot = 1;
    public ObservableCollection<UsbDeviceChoice> UsbDevices { get; } = [];
    public ObservableCollection<BleDeviceChoice> BluetoothDevices { get; } = [];
    public UsbDeviceChoice? SelectedUsbDevice { get => _usbChoice; set => SetField(ref _usbChoice, value); }
    public BleDeviceChoice? SelectedBluetoothDevice { get => _bluetoothChoice; set => SetField(ref _bluetoothChoice, value); }
    public string DeviceNickname { get => _deviceName; set => SetField(ref _deviceName, value); }
    public bool IntegrationControlsEnabled => !_integrationBusy && !IsDeviceBusy;
    public bool AutoSlotLighting
    {
        get => _binding.AutoSlotLighting;
        set { _binding.AutoSlotLighting = value; SaveBinding(); OnPropertyChanged(); }
    }
    public int IntegrationSlot
    {
        get => _integrationSlot;
        set { if (SetField(ref _integrationSlot, value)) RefreshStreamDeckScript(); }
    }
    public IEnumerable<int> IntegrationSlots => DeviceUserPresets.Select(p => p.Slot);
    public string StreamDeckExecutable => Path.Combine(AppContext.BaseDirectory, "WolfEQ.exe");
    public string StreamDeckArguments => $"--slot {IntegrationSlot}" +
        (string.IsNullOrWhiteSpace(_binding.UsbPath) ? "" : $" --device \"{_binding.UsbPath}\"");
    public string StreamDeckCommand => $"\"{StreamDeckExecutable}\" {StreamDeckArguments}";
    public bool CanCopyStreamDeckPowerShell => !string.IsNullOrWhiteSpace(_binding.UsbPath) && IntegrationSlots.Contains(IntegrationSlot);
    public string StreamDeckPowerShell => CanCopyStreamDeckPowerShell
        ? StreamDeckScriptBuilder.Build(StreamDeckExecutable, IntegrationSlot, _binding.UsbPath!) : string.Empty;
    public string StreamDeckSetupNote => CanCopyStreamDeckPowerShell
        ? $"Ready for USER {IntegrationSlot} on {_binding.Name}."
        : "Save your USB device in Device > Connections, then choose a supported USER slot.";
    private string _streamDeckCopyStatus = string.Empty;
    public string StreamDeckCopyStatus { get => _streamDeckCopyStatus; private set => SetField(ref _streamDeckCopyStatus, value); }
    public ICommand CopyStreamDeckPowerShellCommand => new RelayCommand(() =>
    {
        try { Clipboard.SetText(StreamDeckPowerShell); StreamDeckCopyStatus = "Copied. Paste into Windows ScriptDeck's On Press field."; }
        catch (Exception ex) { StreamDeckCopyStatus = $"Could not copy: {ex.Message}"; }
    }, () => CanCopyStreamDeckPowerShell);
    public ICommand OpenWindowsScriptDeckCommand => new RelayCommand(() => OpenUrl(
        "https://marketplace.elgato.com/product/windows-scriptdeck-857f01dd-8fd4-44d5-8ec7-67ac850b21d3", "Windows ScriptDeck"));

    private void RefreshStreamDeckScript()
    {
        OnPropertyChanged(nameof(StreamDeckCommand));
        OnPropertyChanged(nameof(StreamDeckArguments));
        OnPropertyChanged(nameof(StreamDeckPowerShell));
        OnPropertyChanged(nameof(StreamDeckSetupNote));
        OnPropertyChanged(nameof(CanCopyStreamDeckPowerShell));
        OnPropertyChanged(nameof(CopyStreamDeckPowerShellCommand));
        StreamDeckCopyStatus = string.Empty;
    }

    public string DeviceBindingSummary => string.IsNullOrEmpty(_binding.UsbPath)
        ? "Choose the USB and Bluetooth connections for your DAC."
        : _binding.BluetoothAddress is not null
            ? "USB and Bluetooth connections saved."
            : "USB saved. Select Bluetooth to enable lighting controls.";
    public ICommand RefreshUsbDevicesCommand => new AsyncRelayCommand(RefreshUsbDevicesAsync);
    public ICommand ScanBluetoothDevicesCommand => new AsyncRelayCommand(() => IntegrationOperationAsync(async () =>
    {
        Status = "Scanning for K13 Bluetooth devices (8 seconds)...";
        var choices = await _bleLightService.DiscoverAsync();
        BluetoothDevices.Clear();
        foreach (var choice in choices) BluetoothDevices.Add(choice);
        SelectedBluetoothDevice = choices.FirstOrDefault(d => d.Address == _binding.BluetoothAddress);
        Status = choices.Count == 0 ? "No K13 advertisements found. Check Bluetooth and device power." : "Select your K13, identify it, then save the connection.";
    }));
    public ICommand IdentifyBluetoothDeviceCommand => new AsyncRelayCommand(() => IntegrationOperationAsync(async () =>
    {
        if (SelectedBluetoothDevice is null) throw new InvalidOperationException("Select a Bluetooth device first.");
        var service = new FiioK13BleLightService { SelectedAddress = SelectedBluetoothDevice.Address };
        await service.IdentifyAsync();
        Status = "Identification complete; previous lighting restored.";
    }));
    public ICommand SaveDeviceBindingCommand => new AsyncRelayCommand(() => IntegrationOperationAsync(async () =>
    {
        if (SelectedUsbDevice is null) throw new InvalidOperationException("Choose a USB device first.");
        if (EditorSession.WouldReplaceUnsavedEdits) throw new InvalidOperationException("Save or discard editor changes before changing the device connection.");
        var name = DeviceNickname.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new InvalidOperationException("Use a device name of 1–80 characters.");
        _liveDeviceEqSyncTimer.Stop();
        _pendingLiveBandNumbers.Clear();
        _deviceVolumeWriteTimer.Stop();
        _deviceControlsWriteTimer.Stop();
        var updated = new DeviceBindingSettings
        {
            UsbPath = SelectedUsbDevice.Path,
            Name = name,
            BluetoothAddress = SelectedBluetoothDevice?.Address,
            BluetoothName = SelectedBluetoothDevice?.Name ?? "K13",
            AutoSlotLighting = _binding.AutoSlotLighting
        };
        updated.Save();
        _binding = updated;
        ApplyBinding();
        LoadSlotLightingProfiles();
        LoadSelectedSlotLightingProfileIntoUi();
        await DetectDeviceAsync();
        Status = "Device connection saved. " + Status;
    }));
    public ICommand CopyStreamDeckCommand => new RelayCommand(() =>
    {
        try { Clipboard.SetText(StreamDeckCommand); Status = "Stream Deck command copied."; }
        catch (Exception ex) { Status = $"Could not copy command: {ex.Message}"; }
    });
    public ICommand SaveStreamDeckShortcutCommand => new RelayCommand(SaveStreamDeckShortcut);

    private void InitializeIntegrations()
    {
        try { _binding = DeviceBindingSettings.Load(); }
        catch (Exception ex) { AddLog($"Device connection settings could not be loaded: {ex.Message}"); }
        DeviceNickname = _binding.Name;
        ApplyBinding();
        if (_binding.UsbPath is string path)
        {
            SelectedUsbDevice = new(path, _binding.Name);
            UsbDevices.Add(SelectedUsbDevice);
        }
        if (_binding.BluetoothAddress is ulong address)
        {
            SelectedBluetoothDevice = new(address, _binding.BluetoothName, 0);
            BluetoothDevices.Add(SelectedBluetoothDevice);
        }
    }

    private void ApplyBinding()
    {
        _deviceService.SelectedDevicePath = _binding.UsbPath;
        _bleLightService.SelectedAddress = _binding.BluetoothAddress;
        _slotLightingProfileService.SetDevice(_binding.UsbPath);
        RefreshLocalSlotNames();
        OnPropertyChanged(nameof(DeviceBindingSummary));
        RefreshStreamDeckScript();
        OnPropertyChanged(nameof(IntegrationSlots));
    }

    private void SaveBinding()
    {
        try { _binding.Save(); }
        catch (Exception ex) { Status = $"Could not save device settings: {ex.Message}"; AddLog(Status); }
    }

    private void RememberDetectedDevice(HidDetectionResult result)
    {
        if (_binding.UsbPath is null && result.MatchedCandidate is { } candidate)
        {
            _binding.UsbPath = candidate.DevicePath;
            SaveBinding();
            ApplyBinding();
            LoadSlotLightingProfiles();
            LoadSelectedSlotLightingProfileIntoUi();
            SelectedUsbDevice = new(candidate.DevicePath, candidate.Product ?? "USB device");
            UsbDevices.Add(SelectedUsbDevice);
        }
    }

    private async Task RefreshUsbDevicesAsync() => await IntegrationOperationAsync(async () =>
    {
        var result = await _deviceService.DetectUsbAsync();
        UsbDevices.Clear();
        foreach (var device in FiioK13DeviceService.GetSelectableDevices(result.Candidates))
            UsbDevices.Add(new(device.DevicePath, device.Product ?? "USB device"));
        SelectedUsbDevice = UsbDevices.FirstOrDefault(d => string.Equals(d.Path, _binding.UsbPath, StringComparison.OrdinalIgnoreCase));
        Status = "USB scan complete. Select the intended device and save the connection.";
    });

    private async Task IntegrationOperationAsync(Func<Task> operation)
    {
        if (_integrationBusy || IsDeviceBusy || _bleLightService.IsBusy) { Status = "Device busy; try again shortly."; return; }
        _integrationBusy = true;
        OnPropertyChanged(nameof(IntegrationControlsEnabled));
        try { await operation(); }
        catch (Exception ex) { Status = ex.Message; AddLog(Status); }
        finally { _integrationBusy = false; OnPropertyChanged(nameof(IntegrationControlsEnabled)); }
    }

    private void SaveStreamDeckShortcut()
    {
        var dialog = new SaveFileDialog { Filter = "Windows shortcut (*.lnk)|*.lnk", FileName = $"WolfEQ USER {IntegrationSlot}.lnk" };
        if (dialog.ShowDialog() != true) return;
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            dynamic automation = shell!;
            shortcut = automation.CreateShortcut(dialog.FileName);
            dynamic link = shortcut;
            link.TargetPath = StreamDeckExecutable;
            link.Arguments = StreamDeckArguments;
            link.WorkingDirectory = Path.GetDirectoryName(StreamDeckExecutable);
            link.Description = $"Switch WolfEQ to USER {IntegrationSlot}";
            link.Save();
            Status = "Shortcut saved. Assign it to System > Open in Stream Deck.";
        }
        catch (Exception ex) { Status = $"Could not save shortcut: {ex.Message}"; }
        finally
        {
            if (shortcut is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    public async Task<AutomationResult> ExecuteAutomationAsync(AutomationRequest request)
    {
        if (request.Help) return new(0, AutomationRequest.Usage);
        if (request.ListDevices)
        {
            var scan = await new FiioK13DeviceService().DetectUsbAsync();
            return new(0, string.Join(Environment.NewLine, FiioK13DeviceService.GetSelectableDevices(scan.Candidates)
                .Select(d => $"{d.Product}: {d.DevicePath}")));
        }
        if (request.Slot is null) return new(0, "WolfEQ is running.");
        if (_integrationBusy || IsDeviceBusy || EditorSession.WouldReplaceUnsavedEdits)
            return new(3, "Slot unchanged: device busy or editor has unsaved changes.");
        if (request.Device is not null &&
            !string.Equals(request.Device, _binding.Name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Device, _binding.UsbPath, StringComparison.OrdinalIgnoreCase))
            return new(1, "Device does not match the saved connection. Select it in Settings > Device > Connections first.");
        if (string.IsNullOrWhiteSpace(_binding.UsbPath)) return new(1, "Choose and save a USB device in Settings > Device > Connections first.");
        // Discover metadata only so cold commands use the saved device's actual slot limits.
        var detection = await _deviceService.DetectUsbAsync();
        if (!detection.IsDetected || detection.MatchedProfile is null)
            return new(1, "Selected USB device unavailable. No other device was contacted.");
        if (EditorSession.WouldReplaceUnsavedEdits || IsDeviceBusy || _integrationBusy)
            return new(3, "Slot unchanged: device busy or editor has unsaved changes.");
        SelectDetectedDeviceProfile(detection.MatchedProfile, detection.MatchedCandidate);
        var slot = DeviceUserPresets.FirstOrDefault(p => p.Slot == request.Slot);
        if (slot is null) return new(2, "This device does not support the requested USER slot.");
        await SwitchSlotAsync(slot);
        return new(!_lastSlotSwitchConfirmed ? 1 : _lastSlotLightingSucceeded ? 0 : 4, Status);
    }
}

public sealed record UsbDeviceChoice(string Path, string Name)
{
    // A short stable identifier distinguishes identical units without exposing a HID path in the menu.
    public string ShortId => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.ToUpperInvariant())))[..6];
    public string DisplayName => $"{Name}  \u00b7  {ShortId}";
    public override string ToString() => DisplayName;
}
