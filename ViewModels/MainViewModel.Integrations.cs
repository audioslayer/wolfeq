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
        : "Save your USB device in Settings > Your device, then choose a supported USER slot.";
    private string _streamDeckCopyStatus = string.Empty;
    public string StreamDeckCopyStatus { get => _streamDeckCopyStatus; private set => SetField(ref _streamDeckCopyStatus, value); }
    public ICommand CopyStreamDeckPowerShellCommand => new RelayCommand(() =>
    {
        try { Clipboard.SetText(StreamDeckPowerShell); StreamDeckCopyStatus = "Copied. Paste into Windows ScriptDeck's On Press field."; }
        catch (Exception ex) { StreamDeckCopyStatus = $"Could not copy: {ex.Message}"; }
    }, () => CanCopyStreamDeckPowerShell);
    public ICommand OpenWindowsScriptDeckCommand => new RelayCommand(() => OpenUrl(
        "https://marketplace.elgato.com/product/windows-scriptdeck-857f01dd-8fd4-44d5-8ec7-67ac850b21d3", "Windows ScriptDeck"));

    /// <summary>One Stream Deck-style key per USER slot, glowing in that slot's saved top-light colour.</summary>
    public IReadOnlyList<StreamDeckKeyOption> StreamDeckKeys => DeviceUserPresets
        .Where(preset => !string.Equals(preset.Name, "No writable slots", StringComparison.OrdinalIgnoreCase))
        .Select(preset =>
        {
            var lighting = GetSlotLightingProfile(preset.Slot);
            var glow = lighting.TopOn ? LedBrushes.For(lighting.TopColor) : LedBrushes.Off;
            return new StreamDeckKeyOption(preset.Slot, preset.SlotLabel,
                // "From USER n" just repeats the key's own label, so it adds nothing under it.
                string.IsNullOrWhiteSpace(preset.LocalEqName)
                    || preset.LocalEqName!.Trim().Equals($"From {preset.SlotLabel}", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : preset.LocalEqName!,
                glow, preset.Slot == IntegrationSlot);
        })
        .ToArray();

    public StreamDeckKeyOption? SelectedStreamDeckKey => StreamDeckKeys.FirstOrDefault(key => key.IsSelected);

    public void SelectStreamDeckKey(int slot)
    {
        if (IntegrationSlots.Contains(slot)) IntegrationSlot = slot;
    }

    private string _lastSavedShortcutPath = string.Empty;

    /// <summary>The .lnk saved most recently (shown as a tick in the Stream Deck wizard).</summary>
    public string LastSavedShortcutPath
    {
        get => _lastSavedShortcutPath;
        private set
        {
            if (SetField(ref _lastSavedShortcutPath, value)) OnPropertyChanged(nameof(HasSavedShortcut));
        }
    }

    public bool HasSavedShortcut => _lastSavedShortcutPath.Length > 0;

    private void RefreshStreamDeckScript()
    {
        OnPropertyChanged(nameof(StreamDeckKeys));
        OnPropertyChanged(nameof(SelectedStreamDeckKey));
        OnPropertyChanged(nameof(StreamDeckCommand));
        OnPropertyChanged(nameof(StreamDeckArguments));
        OnPropertyChanged(nameof(StreamDeckPowerShell));
        OnPropertyChanged(nameof(StreamDeckSetupNote));
        OnPropertyChanged(nameof(CanCopyStreamDeckPowerShell));
        OnPropertyChanged(nameof(CopyStreamDeckPowerShellCommand));
        StreamDeckCopyStatus = string.Empty;
    }

    public string DeviceBindingSummary => string.IsNullOrEmpty(_binding.UsbPath)
        ? "Choose the USB device for your DAC and save it."
        : "USB device saved.";
    public ICommand RefreshUsbDevicesCommand => new AsyncRelayCommand(RefreshUsbDevicesAsync);
    // ── Guided Bluetooth setup: find → flash → "did it flash?" → saved. No separate save step. ──

    private bool _bluetoothSearching;
    private BleDeviceChoice? _bluetoothCandidate;
    private readonly Queue<BleDeviceChoice> _bluetoothCandidates = new();
    private string _bluetoothSetupMessage = string.Empty;

    public bool IsBluetoothSearching => _bluetoothSearching;
    public bool IsBluetoothConfirming => _bluetoothCandidate is not null && !_bluetoothSearching;
    public bool ShowBluetoothFindPrompt => !IsBluetoothSaved && !_bluetoothSearching && _bluetoothCandidate is null;
    public bool ShowBluetoothSavedDevice => IsBluetoothSaved && !_bluetoothSearching && _bluetoothCandidate is null;
    public string BluetoothStatusText => _bluetoothSearching ? "Searching" : _bluetoothCandidate is not null ? "Check your K13" : IsBluetoothSaved ? "Set up" : "Not set up";
    public string SavedBluetoothDisplay => _binding.BluetoothAddress is ulong address ? $"{_binding.BluetoothName}  ·  ends in {address & 0xFFFFFF:X6}" : string.Empty;
    public string BluetoothCandidateDisplay => _bluetoothCandidate is { } c ? $"{c.Name}  ·  ends in {c.Address & 0xFFFFFF:X6}" : string.Empty;
    public string BluetoothSetupMessage => _bluetoothSetupMessage;
    public bool HasBluetoothSetupMessage => _bluetoothSetupMessage.Length > 0;

    public ICommand FindBluetoothCommand => new AsyncRelayCommand(() => IntegrationOperationAsync(FindBluetoothAsync));
    public ICommand ConfirmBluetoothCommand => new RelayCommand(ConfirmBluetoothCandidate);
    public ICommand RejectBluetoothCommand => new AsyncRelayCommand(() => IntegrationOperationAsync(RejectBluetoothCandidateAsync));
    public ICommand CancelBluetoothSetupCommand => new RelayCommand(() => EndBluetoothSetup(string.Empty));
    public ICommand FlashBluetoothCommand => new AsyncRelayCommand(() => IntegrationOperationAsync(FlashBluetoothAsync));
    public ICommand ForgetBluetoothCommand => new RelayCommand(ForgetBluetooth);

    private async Task FindBluetoothAsync()
    {
        _bluetoothCandidates.Clear();
        _bluetoothCandidate = null;
        _bluetoothSearching = true;
        SetBluetoothMessage(string.Empty);
        Status = "Looking for your K13 over Bluetooth...";
        try
        {
            var found = await _bleLightService.DiscoverAsync();
            if (found.Count == 0)
            {
                SetBluetoothMessage("No K13 found nearby. Make sure it's switched on and within a few metres of this PC, then try again.");
                Status = "No K13 found over Bluetooth.";
                return;
            }

            // Offer the strongest signal first; it's most likely the unit on this desk.
            foreach (var choice in found.OrderByDescending(c => c.Signal == 0 ? short.MinValue : c.Signal)) _bluetoothCandidates.Enqueue(choice);
        }
        finally
        {
            _bluetoothSearching = false;
            RaiseBluetoothSetupChanged();
        }

        await OfferNextBluetoothCandidateAsync();
    }

    private async Task OfferNextBluetoothCandidateAsync()
    {
        if (!_bluetoothCandidates.TryDequeue(out var candidate))
        {
            EndBluetoothSetup("That was the only K13 nearby. Check your K13 is switched on, then search again.");
            return;
        }

        _bluetoothCandidate = candidate;
        RaiseBluetoothSetupChanged();
        await FlashCandidateAsync(candidate);
    }

    private async Task FlashCandidateAsync(BleDeviceChoice candidate)
    {
        try
        {
            await new FiioK13BleLightService { SelectedAddress = candidate.Address }.IdentifyAsync();
            SetBluetoothMessage("Its lights just flashed green for a moment, then went back to normal.");
        }
        catch (Exception ex)
        {
            SetBluetoothMessage($"Found it, but it didn't respond to a test flash ({(ex.InnerException ?? ex).Message}). Try Flash again, or use it anyway if it's the only K13 here.");
        }
        Status = "Did your K13 flash green?";
    }

    private void ConfirmBluetoothCandidate()
    {
        if (_bluetoothCandidate is not { } candidate) return;
        _binding.BluetoothAddress = candidate.Address;
        _binding.BluetoothName = candidate.Name;
        SaveBinding();
        ApplyBinding();
        BluetoothDevices.Clear();
        BluetoothDevices.Add(candidate);
        SelectedBluetoothDevice = candidate;
        EndBluetoothSetup("Bluetooth is set up. You can change the lights now.");
        Status = "Bluetooth saved. Lights are ready.";
        AddLog($"Bluetooth device saved: {candidate.DisplayName}.");
    }

    private async Task RejectBluetoothCandidateAsync()
    {
        _bluetoothCandidate = null;
        RaiseBluetoothSetupChanged();
        await OfferNextBluetoothCandidateAsync();
    }

    private async Task FlashBluetoothAsync()
    {
        if (_bluetoothCandidate is { } candidate)
        {
            await FlashCandidateAsync(candidate);
            return;
        }

        if (!IsBluetoothSaved) return;
        try
        {
            await _bleLightService.IdentifyAsync();
            SetBluetoothMessage("Flashed green. That's the K13 WolfEQ is using.");
        }
        catch (Exception ex)
        {
            SetBluetoothMessage($"Couldn't reach your K13 ({(ex.InnerException ?? ex).Message}). Make sure it's switched on and nearby.");
        }
    }

    private void ForgetBluetooth()
    {
        _binding.BluetoothAddress = null;
        SaveBinding();
        ApplyBinding();
        BluetoothDevices.Clear();
        SelectedBluetoothDevice = null;
        EndBluetoothSetup("Bluetooth forgotten. The lights won't change until you set it up again.");
    }

    private void EndBluetoothSetup(string message)
    {
        _bluetoothCandidates.Clear();
        _bluetoothCandidate = null;
        _bluetoothSearching = false;
        SetBluetoothMessage(message);
    }

    private void SetBluetoothMessage(string message)
    {
        _bluetoothSetupMessage = message;
        RaiseBluetoothSetupChanged();
    }

    private void RaiseBluetoothSetupChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(IsBluetoothSearching), nameof(IsBluetoothConfirming), nameof(ShowBluetoothFindPrompt),
                     nameof(ShowBluetoothSavedDevice), nameof(BluetoothStatusText), nameof(SavedBluetoothDisplay),
                     nameof(BluetoothCandidateDisplay), nameof(BluetoothSetupMessage), nameof(HasBluetoothSetupMessage),
                     nameof(IsBluetoothSaved)
                 })
        {
            OnPropertyChanged(name);
        }
    }
    public ICommand SaveDeviceBindingCommand => new AsyncRelayCommand(() => IntegrationOperationAsync(async () =>
    {
        if (SelectedUsbDevice is null) throw new InvalidOperationException("Choose a USB device first.");
        // Only a different USB device can replace the editor's content; saving a Bluetooth
        // selection or a new nickname for the same USB device must not be blocked by EQ edits.
        var usbChanged = !string.Equals(SelectedUsbDevice.Path, _binding.UsbPath, StringComparison.OrdinalIgnoreCase);
        if (usbChanged && EditorSession.WouldReplaceUnsavedEdits)
            throw new InvalidOperationException("Save or discard editor changes before switching to a different USB device.");
        var name = DeviceNickname.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl)) throw new InvalidOperationException("Use a device name of 1–80 characters.");
        _liveDeviceEqSyncTimer.Stop();
        _pendingLiveBandNumbers.Clear();
        _deviceVolumeWriteTimer.Stop();
        var updated = new DeviceBindingSettings
        {
            UsbPath = SelectedUsbDevice.Path,
            Name = name,
            // Bluetooth is saved by its own guided setup; keep whatever is saved.
            BluetoothAddress = _binding.BluetoothAddress,
            BluetoothName = _binding.BluetoothName,
            AutoSlotLighting = _binding.AutoSlotLighting
        };
        if (IsMovedDevice(SelectedUsbDevice.Path, UsbDevices.Select(device => (string?)device.Path)))
            CarryOverDeviceData(_binding.UsbPath!, SelectedUsbDevice.Path);
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
        RaiseBluetoothSetupChanged();
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
            LastSavedShortcutPath = dialog.FileName;
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
        if (request.SystemEqBypass is not null || request.PresetName is not null) return await ExecuteSystemEqAutomationAsync(request);
        if (request.Slot is null) return new(0, "WolfEQ is running.");
        if (IsSystemEqSelected)
            return new(1, "USER slots are not available in Software EQ mode. Use --preset or --system-eq-bypass instead.");
        if (_integrationBusy || IsDeviceBusy || EditorSession.WouldReplaceUnsavedEdits)
            return new(3, "Slot unchanged: device busy or editor has unsaved changes.");
        if (request.Device is not null &&
            !string.Equals(request.Device, _binding.Name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Device, _binding.UsbPath, StringComparison.OrdinalIgnoreCase))
            return new(1, "Device does not match the saved connection. Select it in Settings > Your device first.");
        if (string.IsNullOrWhiteSpace(_binding.UsbPath)) return new(1, "Choose and save a USB device in Settings > Your device first.");
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

/// <summary>A USER slot drawn as a Stream Deck key in Settings › Shortcuts and the setup wizard.</summary>
public sealed record StreamDeckKeyOption(int Slot, string Label, string Subtitle, System.Windows.Media.Brush Glow, bool IsSelected);

public sealed record UsbDeviceChoice(string Path, string Name)
{
    // A short stable identifier distinguishes identical units without exposing a HID path in the menu.
    public string ShortId => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.ToUpperInvariant())))[..6];
    public string DisplayName => $"{Name}  \u00b7  {ShortId}";
    public override string ToString() => DisplayName;
}
