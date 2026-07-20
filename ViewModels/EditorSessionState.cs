namespace WolfEQ.ViewModels;

/// <summary>
/// How the editor's current settings relate to the targeted device slot.
/// </summary>
public enum DeviceSyncState
{
    Unknown,
    InSync,
    Modified
}

/// <summary>
/// Tracks the editor's relationship to the profile library and the targeted device slot.
/// Plain state logic with no WPF or service dependencies so it stays unit-testable.
/// </summary>
public sealed class EditorSessionState
{
    private bool _hasEdits;
    private bool _isDeviceConnected;

    /// <summary>Raised after any state transition so the view model can refresh bindings.</summary>
    public event Action? Changed;

    /// <summary>True when the editor differs from the library preset it came from.</summary>
    public bool LibraryDirty { get; private set; }

    /// <summary>The editor's sync state against the targeted device slot.</summary>
    public DeviceSyncState DeviceSyncState { get; private set; } = DeviceSyncState.Unknown;

    /// <summary>The device slot writes currently target, if any.</summary>
    public byte? TargetSlotId { get; private set; }

    /// <summary>True when loading new content into the editor would discard edits that are not saved anywhere.</summary>
    public bool WouldReplaceUnsavedEdits => _hasEdits;

    /// <summary>
    /// True when an automatic load-from-device on connect would not clobber anything.
    /// Only actual user edits block the auto-load: slot-loaded or library-loaded content
    /// can be reloaded harmlessly, so it must not disable the auto-load forever.
    /// </summary>
    public bool ShouldAutoLoadOnConnect => !_hasEdits;

    /// <summary>
    /// True when both the editor state and the selected device profile permit an
    /// automatic connect-time EQ load.
    /// </summary>
    public bool CanAutoLoadOnConnect(bool supportsEqReadback)
        => supportsEqReadback && ShouldAutoLoadOnConnect;

    /// <summary>Pure guard for device writes: all three conditions must hold.</summary>
    public bool CanWriteToDevice(bool isConnected, bool hardwareIoEnabled, bool hasWritableTarget)
        => isConnected && hardwareIoEnabled && hasWritableTarget;

    /// <summary>The user changed a band, the preamp, or other editor content.</summary>
    public void NotifyEdit()
    {
        _hasEdits = true;
        LibraryDirty = true;
        DeviceSyncState = DeviceSyncState.Modified;
        RaiseChanged();
    }

    /// <summary>
    /// The editor preset was written to the given slot; library state is unaffected.
    /// The edits are now on the device, so they no longer count as unsaved.
    /// </summary>
    public void NotifyWriteSucceeded(byte slotId)
    {
        TargetSlotId = slotId;
        DeviceSyncState = DeviceSyncState.InSync;
        _hasEdits = false;
        RaiseChanged();
    }

    /// <summary>The editor was filled from a device slot readback; that content is not in the library.</summary>
    public void NotifyLoadedFromSlot(byte slotId)
    {
        TargetSlotId = slotId;
        DeviceSyncState = DeviceSyncState.InSync;
        LibraryDirty = true;
        _hasEdits = false;
        RaiseChanged();
    }

    /// <summary>The editor was filled from a library preset; it now differs from whatever the device holds.</summary>
    public void NotifyLoadedFromLibrary()
    {
        LibraryDirty = false;
        _hasEdits = false;
        DeviceSyncState = TargetSlotId is null ? DeviceSyncState.Unknown : DeviceSyncState.Modified;
        RaiseChanged();
    }

    /// <summary>The editor content was saved to the library; device sync is unaffected.</summary>
    public void NotifySavedToLibrary()
    {
        LibraryDirty = false;
        _hasEdits = false;
        RaiseChanged();
    }

    /// <summary>The active hardware slot changed; the editor keeps its content and follows the new target.</summary>
    public void NotifySlotSwitched(byte slotId)
    {
        var previousTarget = TargetSlotId;
        TargetSlotId = slotId;
        if (DeviceSyncState == DeviceSyncState.InSync && previousTarget != slotId)
        {
            DeviceSyncState = DeviceSyncState.Modified;
        }

        RaiseChanged();
    }

    /// <summary>The device went away; a previously in-sync slot can no longer be trusted.</summary>
    public void NotifyDeviceDisconnected()
    {
        _isDeviceConnected = false;
        if (DeviceSyncState == DeviceSyncState.InSync)
        {
            DeviceSyncState = DeviceSyncState.Unknown;
        }

        RaiseChanged();
    }

    /// <summary>A device is connected; sync state is unchanged until something is loaded or written.</summary>
    public void NotifyDeviceConnected()
    {
        _isDeviceConnected = true;
        RaiseChanged();
    }

    /// <summary>The device profile changed; nothing known about the new device applies.</summary>
    public void Reset()
    {
        DeviceSyncState = DeviceSyncState.Unknown;
        TargetSlotId = null;
        LibraryDirty = false;
        _hasEdits = false;
        RaiseChanged();
    }

    /// <summary>Plain-English one-liner describing the editor's sync state.</summary>
    public string StatusText(string? slotName)
    {
        if (!_isDeviceConnected)
        {
            return "No device connected";
        }

        var slot = string.IsNullOrWhiteSpace(slotName) ? "the device" : slotName;
        return DeviceSyncState switch
        {
            DeviceSyncState.InSync => $"In sync with {slot}",
            DeviceSyncState.Modified when LibraryDirty || _hasEdits => "Unsaved changes — not on the device yet",
            DeviceSyncState.Modified => $"Editor differs from {slot}",
            _ => "Device slot not loaded yet"
        };
    }

    private void RaiseChanged() => Changed?.Invoke();
}
