using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// Permanent device dock strip: connection status + Detect, hardware slot chips,
/// Load from slot, the editor/device sync indicator, the live-sync toggle, and the
/// Save to Library / Write to device actions. Expects the application's
/// MainViewModel as (inherited) DataContext.
/// <para>
/// <see cref="EditorSessionState"/> is a plain class (Changed event, no
/// INotifyPropertyChanged), so the sync-indicator colors and the Write button's
/// disabled-reason line are refreshed from code-behind instead of XAML triggers.
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

    /// <summary>Updates the sync-indicator colors and the Write button's disabled-reason line.</summary>
    private void RefreshDeviceState()
    {
        var dimBrush = BrushResource("WolfDimBrush", Brushes.Gray);
        var mutedBrush = BrushResource("WolfMutedBrush", Brushes.Gray);

        if (_viewModel is null)
        {
            // Design time / no DataContext yet: neutral indicator, no reason line.
            SyncStateDot.Fill = dimBrush;
            SyncStateText.Foreground = mutedBrush;
            WriteDisabledReasonText.Visibility = Visibility.Collapsed;
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

        // Disabled-reason line: plain-English explanation whenever Write cannot run.
        var reason = _viewModel.WriteToDeviceCommand.CanExecute(null)
            ? null
            : DescribeWriteBlocker(_viewModel);
        WriteDisabledReasonText.Text = reason ?? string.Empty;
        WriteDisabledReasonText.Visibility = reason is null ? Visibility.Collapsed : Visibility.Visible;
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

    /// <summary>Mirrors the view model's writable-target check for the reason line.</summary>
    private static bool HasWritableTargetSlot(MainViewModel viewModel)
    {
        var targetSlotId = viewModel.EditorSession.TargetSlotId ?? viewModel.SelectedDeviceUserPreset?.PresetId;
        return targetSlotId is byte slotId
               && viewModel.SelectedDeviceProfile.WritableSlots.Any(slot => slot.Id == slotId);
    }

    /// <summary>Routes the vertical mouse wheel to horizontal chip scrolling (scrollbars are hidden).</summary>
    private void SlotChipScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller && scroller.ScrollableWidth > 0)
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private Brush BrushResource(string key, Brush fallback)
        => TryFindResource(key) as Brush ?? fallback;
}
