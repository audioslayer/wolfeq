namespace WolfEQ.ViewModels;

public sealed partial class MainViewModel
{
    // The editor's selected destination can differ from the last confirmed hardware slot.
    public byte? TrayConfirmedPresetId => IsDeviceConnected ? _confirmedDevicePresetId : null;
    public bool HasUnsavedTrayEdits => EditorSession.WouldReplaceUnsavedEdits || IsProfileLibraryDirty;
}
