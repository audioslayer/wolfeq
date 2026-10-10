using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// Permanent device dock strip: connection status + Detect, a USER-slot menu,
/// Load from slot, the editor/device sync indicator, the live-sync toggle, and the
/// Save to Library / Write to device actions. Expects the application's
/// MainViewModel as (inherited) DataContext.
/// <para>
/// <see cref="EditorSessionState"/> is a plain class (Changed event, no
/// INotifyPropertyChanged), so the sync-indicator colors and the Write button's
/// tooltip are refreshed from code-behind instead of XAML triggers.
/// </para>
/// </summary>
public partial class DeviceDock : UserControl
{
    private MainViewModel? _viewModel;

    public DeviceDock()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        RefreshDeviceState();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.EditorSession.Changed -= OnEditorSessionChanged;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.WriteToDeviceCommand.CanExecuteChanged -= OnWriteCanExecuteChanged;
        }

        _viewModel = e.NewValue as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.EditorSession.Changed += OnEditorSessionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.WriteToDeviceCommand.CanExecuteChanged += OnWriteCanExecuteChanged;
        }

        RefreshDeviceState();
    }

    private void OnEditorSessionChanged()
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshDeviceState();
        }
        else
        {
            Dispatcher.Invoke(RefreshDeviceState);
        }
    }

    private void OnWriteCanExecuteChanged(object? sender, EventArgs e) => RefreshDeviceState();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(MainViewModel.IsDeviceConnected)
                or nameof(MainViewModel.SelectedDeviceUserPreset)
                or nameof(MainViewModel.SelectedDeviceProfile)
                or nameof(MainViewModel.EditorSyncStatusText))
        {
            RefreshDeviceState();
        }

    }

    /// <summary>Updates the sync-indicator colors and the Write button's tooltip.</summary>
    private void RefreshDeviceState()
    {
        var dimBrush = BrushResource("WolfDimBrush", Brushes.Gray);
        var mutedBrush = BrushResource("WolfMutedBrush", Brushes.Gray);

        if (_viewModel is null)
        {
            // Design time / no DataContext yet: neutral indicator, no tooltip.
            SyncStateDot.Fill = dimBrush;
            SyncStateText.Foreground = mutedBrush;
            WriteToDeviceButton.ToolTip = "Write the editor's EQ to the device slot";
            return;
        }

        // Sync indicator: green when in sync, amber when the editor has drifted, gray otherwise.
        if (!_viewModel.IsDeviceConnected)
        {
            SyncStateDot.Fill = dimBrush;
            SyncStateText.Foreground = mutedBrush;
        }
        else
        {
            switch (_viewModel.EditorSession.DeviceSyncState)
            {
                case DeviceSyncState.InSync:
                    var greenBrush = BrushResource("WolfGreenBrush", Brushes.LimeGreen);
                    SyncStateDot.Fill = greenBrush;
                    SyncStateText.Foreground = greenBrush;
                    break;
                case DeviceSyncState.Modified:
                    var amberBrush = BrushResource("WolfWarningBrush", Brushes.Goldenrod);
                    SyncStateDot.Fill = amberBrush;
                    SyncStateText.Foreground = amberBrush;
                    break;
                default:
                    SyncStateDot.Fill = dimBrush;
                    SyncStateText.Foreground = mutedBrush;
                    break;
            }
        }

        // Disabled-tooltip: plain-English explanation whenever Write cannot run.
        var reason = _viewModel.WriteToDeviceCommand.CanExecute(null)
            ? null
            : DescribeWriteBlocker(_viewModel);
        WriteToDeviceButton.ToolTip = reason ?? "Write the editor's EQ to the device slot";
    }

    /// <summary>
    /// Explains why the Write button is disabled. The hardware-safety flag is private
    /// to the view model, so it is inferred: LoadFromSlotCommand's guard is exactly
    /// "connected AND safety flag", meaning a connected device with Load disabled can
    /// only be the safety build. Returns null for transient states (a write already
    /// running) that need no explanation.
    /// </summary>
    private static string? DescribeWriteBlocker(MainViewModel viewModel)
    {
        if (!viewModel.IsDeviceConnected)
        {
            return "Connect a device to write";
        }

        if (!viewModel.HardwareIoEnabled)
        {
            return "Hardware writes are off in this safety build";
        }

        if (!HasWritableTargetSlot(viewModel))
        {
            return "Pick a USER slot to write to";
        }

        return null;
    }

    /// <summary>Mirrors the view model's writable-target check for the tooltip.</summary>
    private static bool HasWritableTargetSlot(MainViewModel viewModel)
    {
        var targetSlotId = viewModel.EditorSession.TargetSlotId ?? viewModel.SelectedDeviceUserPreset?.PresetId;
        return targetSlotId is byte slotId
               && viewModel.SelectedDeviceProfile.WritableSlots.Any(slot => slot.Id == slotId);
    }

    public void OpenLibrarySave()
    {
        if (_viewModel is null || !_viewModel.SaveToLibraryCommand.CanExecute(null)) return;
        LibraryPresetName.Text = _viewModel.SuggestedLibraryName;
        LibrarySaveError.Visibility = Visibility.Collapsed;
        SaveLibraryPopup.IsOpen = true;
    }

    private void SaveLibraryButton_Click(object sender, RoutedEventArgs e) => OpenLibrarySave();

    private void SaveLibraryPopup_Opened(object? sender, EventArgs e)
    {
        LibraryPresetName.Focus();
        LibraryPresetName.SelectAll();
    }

    private void SaveLibraryPopup_Closed(object? sender, EventArgs e) => SaveLibraryButton.Focus();

    private void CancelLibrarySave_Click(object sender, RoutedEventArgs e) => SaveLibraryPopup.IsOpen = false;

    private void ConfirmLibrarySave_Click(object sender, RoutedEventArgs e) => ConfirmLibrarySave();

    private void ConfirmLibrarySave()
    {
        if (_viewModel is null) return;
        if (_viewModel.TrySaveNamedLibraryPreset(LibraryPresetName.Text, out var error))
            SaveLibraryPopup.IsOpen = false;
        else
        {
            LibrarySaveError.Text = error;
            LibrarySaveError.Visibility = Visibility.Visible;
            LibraryPresetName.Focus();
        }
    }

    private void SaveLibraryPopup_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape) SaveLibraryPopup.IsOpen = false;
        else if (e.Key == System.Windows.Input.Key.Enter) ConfirmLibrarySave();
        else return;
        e.Handled = true;
    }

    private void LiveSyncHelp_Click(object sender, RoutedEventArgs e)
    {
        if (LiveSyncHelp.ToolTip is ToolTip tip)
        {
            tip.PlacementTarget = LiveSyncHelp;
            tip.IsOpen = true;
        }
    }

    private void SlotMenuItem_Click(object sender, RoutedEventArgs e) => SlotPopup.IsOpen = false;

    private void DeviceSettings_Click(object sender, RoutedEventArgs e)
    {
        DevicePopup.IsOpen = false;
        if (Window.GetWindow(this) is MainWindow window) window.OpenSettings();
    }

    // Opening the menu shows the known devices at once, then refreshes with a read-only USB scan.
    private void DeviceMenuButton_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model && model.RefreshQuickDevicesCommand.CanExecute(null))
            model.RefreshQuickDevicesCommand.Execute(null);
    }

    private void DeviceMenuItem_Click(object sender, RoutedEventArgs e) => DevicePopup.IsOpen = false;

    private void DevicePopup_Closed(object? sender, EventArgs e) => DeviceMenuButton.IsChecked = false;

    private void SlotPopup_Closed(object? sender, EventArgs e) => SlotMenuButton.IsChecked = false;

    private Brush BrushResource(string key, Brush fallback)
        => TryFindResource(key) as Brush ?? fallback;
}
