using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class EditorSessionStateTests
{
    private const byte SlotA = 0xA0;
    private const byte SlotB = 0xA1;

    private static EditorSessionState ConnectedSession()
    {
        var session = new EditorSessionState();
        session.NotifyDeviceConnected();
        return session;
    }

    // --- Edit / write transitions ---

    [Fact]
    public void EditAfterLoad_BecomesModified_AndLibraryDirty()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);

        session.NotifyEdit();

        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
        Assert.True(session.LibraryDirty);
    }

    [Fact]
    public void WriteSucceeded_BecomesInSync_LibraryDirtyUnchanged()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);
        session.NotifyEdit();
        Assert.True(session.LibraryDirty);

        session.NotifyWriteSucceeded(SlotA);

        Assert.Equal(DeviceSyncState.InSync, session.DeviceSyncState);
        Assert.True(session.LibraryDirty);
        Assert.Equal(SlotA, session.TargetSlotId);
    }

    [Fact]
    public void WriteSucceeded_WhenLibraryClean_LeavesLibraryClean()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromLibrary();
        Assert.False(session.LibraryDirty);

        session.NotifyWriteSucceeded(SlotA);

        Assert.Equal(DeviceSyncState.InSync, session.DeviceSyncState);
        Assert.False(session.LibraryDirty);
    }

    [Fact]
    public void WriteFailure_OnlyNotifyWriteSucceededTransitionsToInSync()
    {
        var session = ConnectedSession();
        session.NotifyEdit();

        // A failed write calls no transition method; the state must remain Modified.
        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
        Assert.True(session.LibraryDirty);
        Assert.True(session.WouldReplaceUnsavedEdits);
    }

    // --- Slot switching ---

    [Fact]
    public void SlotSwitch_WithUnsavedEdits_StaysModified_TargetUpdates()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);
        session.NotifyEdit();

        session.NotifySlotSwitched(SlotB);

        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
        Assert.Equal(SlotB, session.TargetSlotId);
        Assert.True(session.WouldReplaceUnsavedEdits);
    }

    [Fact]
    public void SlotSwitch_WhileInSync_ToDifferentSlot_BecomesModified()
    {
        var session = ConnectedSession();
        session.NotifyWriteSucceeded(SlotA);

        session.NotifySlotSwitched(SlotB);

        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
        Assert.Equal(SlotB, session.TargetSlotId);
    }

    [Fact]
    public void SlotSwitch_WhileInSync_ToSameSlot_StaysInSync()
    {
        var session = ConnectedSession();
        session.NotifyWriteSucceeded(SlotA);

        session.NotifySlotSwitched(SlotA);

        Assert.Equal(DeviceSyncState.InSync, session.DeviceSyncState);
        Assert.Equal(SlotA, session.TargetSlotId);
    }

    // --- CanWriteToDevice truth table ---

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void CanWriteToDevice_RequiresAllThreeConditions(bool connected, bool hardwareIo, bool writableTarget, bool expected)
    {
        var session = new EditorSessionState();

        Assert.Equal(expected, session.CanWriteToDevice(connected, hardwareIo, writableTarget));
    }

    // --- Auto-load guard ---

    [Fact]
    public void ShouldAutoLoadOnConnect_TrueWhenClean()
    {
        var session = ConnectedSession();

        Assert.True(session.ShouldAutoLoadOnConnect);
    }

    [Fact]
    public void ShouldAutoLoadOnConnect_FalseWhenModified()
    {
        var session = ConnectedSession();
        session.NotifyEdit();

        Assert.False(session.ShouldAutoLoadOnConnect);
    }

    [Fact]
    public void ShouldAutoLoadOnConnect_FalseWhenLibraryDirty()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);

        Assert.False(session.ShouldAutoLoadOnConnect);
    }

    // --- Reset (device profile switch) ---

    [Fact]
    public void Reset_ClearsTargetDirtyAndSyncState()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);
        session.NotifyEdit();

        session.Reset();

        Assert.Equal(DeviceSyncState.Unknown, session.DeviceSyncState);
        Assert.Null(session.TargetSlotId);
        Assert.False(session.LibraryDirty);
        Assert.False(session.WouldReplaceUnsavedEdits);
    }

    // --- Disconnect / reconnect ---

    [Fact]
    public void Disconnect_WhileInSync_BecomesUnknown()
    {
        var session = ConnectedSession();
        session.NotifyWriteSucceeded(SlotA);

        session.NotifyDeviceDisconnected();

        Assert.Equal(DeviceSyncState.Unknown, session.DeviceSyncState);
    }

    [Fact]
    public void Disconnect_WhileModified_StaysModified()
    {
        var session = ConnectedSession();
        session.NotifyEdit();

        session.NotifyDeviceDisconnected();

        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
    }

    [Fact]
    public void ReconnectWhileClean_AllowsAutoLoadOnce()
    {
        var session = ConnectedSession();
        session.NotifyDeviceDisconnected();
        session.NotifyDeviceConnected();

        Assert.True(session.ShouldAutoLoadOnConnect);

        // The auto-load happens: after loading from the slot, a second auto-load is not allowed.
        session.NotifyLoadedFromSlot(SlotA);

        Assert.False(session.ShouldAutoLoadOnConnect);
    }

    [Fact]
    public void ReconnectWithUnsavedEdits_SuppressesAutoLoad()
    {
        var session = ConnectedSession();
        session.NotifyEdit();
        session.NotifyDeviceDisconnected();
        session.NotifyDeviceConnected();

        Assert.False(session.ShouldAutoLoadOnConnect);
        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
    }

    // --- Load transitions ---

    [Fact]
    public void LoadedFromSlot_IsInSync_ButLibraryDirty_WithoutUnsavedEdits()
    {
        var session = ConnectedSession();

        session.NotifyLoadedFromSlot(SlotA);

        Assert.Equal(DeviceSyncState.InSync, session.DeviceSyncState);
        Assert.True(session.LibraryDirty);
        Assert.Equal(SlotA, session.TargetSlotId);
        Assert.False(session.WouldReplaceUnsavedEdits);
    }

    [Fact]
    public void LoadedFromLibrary_WithoutTarget_IsUnknown_AndClean()
    {
        var session = ConnectedSession();
        session.NotifyEdit();

        session.NotifyLoadedFromLibrary();

        Assert.Equal(DeviceSyncState.Unknown, session.DeviceSyncState);
        Assert.False(session.LibraryDirty);
        Assert.False(session.WouldReplaceUnsavedEdits);
    }

    [Fact]
    public void LoadedFromLibrary_WithTarget_IsModifiedAgainstDevice_AndClean()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);

        session.NotifyLoadedFromLibrary();

        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
        Assert.False(session.LibraryDirty);
        Assert.False(session.WouldReplaceUnsavedEdits);
    }

    [Fact]
    public void SavedToLibrary_ClearsLibraryDirty_DeviceSyncUnchanged()
    {
        var session = ConnectedSession();
        session.NotifyEdit();

        session.NotifySavedToLibrary();

        Assert.False(session.LibraryDirty);
        Assert.Equal(DeviceSyncState.Modified, session.DeviceSyncState);
        Assert.False(session.WouldReplaceUnsavedEdits);
    }

    // --- StatusText ---

    [Fact]
    public void StatusText_Disconnected_SaysNoDeviceConnected()
    {
        var session = new EditorSessionState();

        Assert.Equal("No device connected", session.StatusText("Slot 1 - USER 1"));
    }

    [Fact]
    public void StatusText_InSync_NamesTheSlot()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);

        Assert.Equal("In sync with Slot 1 - USER 1", session.StatusText("Slot 1 - USER 1"));
    }

    [Fact]
    public void StatusText_WithUnsavedEdits_SaysNotOnDeviceYet()
    {
        var session = ConnectedSession();
        session.NotifyLoadedFromSlot(SlotA);
        session.NotifyEdit();

        Assert.Equal("Unsaved changes — not on the device yet", session.StatusText("Slot 1 - USER 1"));
    }

    [Fact]
    public void StatusText_CleanEditorDifferentTarget_SaysEditorDiffers()
    {
        var session = ConnectedSession();
        session.NotifyWriteSucceeded(SlotA);
        session.NotifySlotSwitched(SlotB);

        Assert.Equal("Editor differs from Slot 2 - USER 2", session.StatusText("Slot 2 - USER 2"));
    }

    // --- Changed event ---

    [Fact]
    public void Changed_FiresOnEveryTransition()
    {
        var session = new EditorSessionState();
        var raised = 0;
        session.Changed += () => raised++;

        session.NotifyDeviceConnected();
        session.NotifyLoadedFromSlot(SlotA);
        session.NotifyEdit();
        session.NotifyWriteSucceeded(SlotA);
        session.NotifySlotSwitched(SlotB);
        session.NotifyLoadedFromLibrary();
        session.NotifySavedToLibrary();
        session.NotifyDeviceDisconnected();
        session.Reset();

        Assert.Equal(9, raised);
    }
}
