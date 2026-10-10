using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WolfEQ.Services;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

public partial class TrayFlyout : Window
{
    private readonly TrayPanelState _state;
    private readonly Action _openEditor, _openSettings, _quit;
    private bool _disposing;

    public TrayFlyout(MainViewModel vm, Action openEditor, Action openSettings, Action quit)
    {
        InitializeComponent();
        _openEditor = openEditor;
        _openSettings = openSettings;
        _quit = quit;
        _state = new TrayPanelState(vm);
        DataContext = _state;
        Closed += (_, _) => _state.Dispose();
    }

    public void DisposeFlyout()
    {
        _disposing = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_disposing) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    public void ShowNearTray()
    {
        _state.Refresh();
        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            Show();
            Activate();
            return;
        }
        // Move the native window to the tray's monitor before measuring it; WPF then
        // applies that monitor's DPI. Keep the final bounds within its work area.
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, IntPtr.Zero, info.Work.Right - 380, info.Work.Bottom - 650, 0, 0, 0x0015);
        Show();
        FitWorkArea(info.Work);
        UpdateLayout();
        Position(handle, info.Work);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsVisible) return;
            FitWorkArea(info.Work);
            UpdateLayout();
            Position(handle, info.Work);
            Activate();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        }));
    }

    private void FitWorkArea(NativeRect work)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        MaxHeight = Math.Max(100, (work.Bottom - work.Top - 16) / dpi.DpiScaleY);
        Width = Math.Min(368, Math.Max(100, (work.Right - work.Left - 16) / dpi.DpiScaleX));
    }

    private static void Position(IntPtr handle, NativeRect work)
    {
        if (!GetWindowRect(handle, out var bounds)) return;
        var width = Math.Min(bounds.Right - bounds.Left, work.Right - work.Left);
        var height = Math.Min(bounds.Bottom - bounds.Top, work.Bottom - work.Top);
        SetWindowPos(handle, IntPtr.Zero, Math.Max(work.Left, work.Right - width - 8),
            Math.Max(work.Top, work.Bottom - height - 8), width, height, 0x0014);
    }

    private async void OnSlotClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: int slot })
            await _state.SwitchAsync(slot);
    }
    private void OnOpenEditor(object sender, RoutedEventArgs e) { Hide(); _openEditor(); }
    private void OnOpenSettings(object sender, RoutedEventArgs e) { Hide(); _openSettings(); }
    private void OnQuit(object sender, RoutedEventArgs e) { Hide(); _quit(); }
    private void OnDeactivated(object? sender, EventArgs e) => Hide();
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Hide();
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}

internal sealed class TrayPanelState : INotifyPropertyChanged, IDisposable
{
    private bool _switching;
    private string? _result;
    private string _progress = "";
    public MainViewModel Vm { get; }
    public TrayPreferences Preferences => TrayPreferences.Current;
    public ObservableCollection<TraySlotState> Slots { get; } = [];
    public bool IsWorking => _switching || !Vm.IntegrationControlsEnabled;
    public bool CanSwitch => !IsWorking && !Vm.EditorSession.WouldReplaceUnsavedEdits;
    public string ConnectionSummary => $"{Vm.SelectedDeviceProfile.DisplayName} / {Vm.ConnectionText}";
    public string Message => _switching ? _progress
        : _result ?? (Vm.EditorSession.WouldReplaceUnsavedEdits
            ? "Save or discard your editor changes before switching profiles."
            : !Vm.IsDeviceConnected ? "Connect your saved USB device to switch profiles. Open Settings to choose a device."
            : IsWorking ? "Device busy. Your profiles will be ready shortly."
            : string.Empty);
    public event PropertyChangedEventHandler? PropertyChanged;

    public TrayPanelState(MainViewModel vm)
    {
        Vm = vm;
        Vm.PropertyChanged += OnVmChanged;
        Vm.EditorSession.Changed += OnEditorChanged;
        Vm.DeviceUserPresets.CollectionChanged += OnSlotsChanged;
        RebuildSlots();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Status))
        {
            if (_switching) _progress = Vm.Status;
            else _result = null;
        }
        Refresh();
    }
    private void OnEditorChanged() => Refresh();
    private void OnSlotsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RebuildSlots();
    private void RebuildSlots()
    {
        Slots.Clear();
        foreach (var slot in Vm.DeviceUserPresets) Slots.Add(new TraySlotState(slot));
        Refresh();
    }
    public void Refresh()
    {
        foreach (var slot in Slots) slot.Update(Vm.TrayConfirmedPresetId);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public async Task SwitchAsync(int slot)
    {
        if (!CanSwitch) return;
        _switching = true;
        _result = null;
        _progress = $"Checking your device for profile {slot}...";
        Refresh();
        try
        {
            var result = await Vm.ExecuteAutomationAsync(new AutomationRequest(Slot: slot));
            _result = result.Message;
        }
        catch (Exception ex) { _result = $"Profile unchanged or unconfirmed: {ex.Message}"; }
        finally { _switching = false; Refresh(); }
    }
    public void Dispose()
    {
        Vm.PropertyChanged -= OnVmChanged;
        Vm.EditorSession.Changed -= OnEditorChanged;
        Vm.DeviceUserPresets.CollectionChanged -= OnSlotsChanged;
    }
}

internal sealed class TraySlotState(DeviceUserPresetOption option) : INotifyPropertyChanged
{
    public int Slot => option.Slot;
    public string Label => option.SlotLabel + (IsActive ? " \u00B7 Active" : "");
    public string ToolTip => $"{Label}\n{Caption}";
    public bool IsActive { get; private set; }
    public string Caption => option.LocalEqSubtitle;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(byte? activePreset)
    {
        var active = activePreset == option.PresetId;
        if (active == IsActive) return;
        IsActive = active;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
