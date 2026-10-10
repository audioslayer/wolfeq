using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

public partial class TrayFlyout : Window
{
    private readonly TrayPanelState _state;
    private readonly Action _openEditor, _openSettings, _quit;
    private bool _disposing;
    private IntPtr _handle;
    private NativeRect? _work;

    public TrayFlyout(MainViewModel vm, Action openEditor, Action openSettings, Action quit)
    {
        InitializeComponent();
        _openEditor = openEditor;
        _openSettings = openSettings;
        _quit = quit;
        _state = new TrayPanelState(vm);
        DataContext = _state;
        Closed += (_, _) => _state.Dispose();
        // Expanding "Tray options" or a longer hint grows the window; keep it on screen.
        SizeChanged += (_, _) => { if (IsVisible && _work is { } work && _handle != IntPtr.Zero) Position(_handle, work); };
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
        TrayOptionsToggle.IsChecked = false;
        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            _work = null;
            Show();
            Activate();
            return;
        }
        // Move the native window to the tray's monitor before measuring it; WPF then
        // applies that monitor's DPI. Keep the final bounds within its work area.
        var handle = new WindowInteropHelper(this).EnsureHandle();
        _handle = handle;
        _work = info.Work;
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
    private void OnQuickPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrayPresetState preset })
            _state.OpenPreset(preset.Preset);
    }
    private void OnTrayOptionsToggled(object sender, RoutedEventArgs e)
        => TrayOptionsPanel.Visibility = TrayOptionsToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
    public ObservableCollection<TrayPresetState> QuickPresets { get; } = [];
    public bool HasNoQuickPresets => QuickPresets.Count == 0;
    public bool IsWorking => _switching || !Vm.IntegrationControlsEnabled;
    public bool CanSwitch => !IsWorking && !Vm.EditorSession.WouldReplaceUnsavedEdits;
    public string ConnectionSummary => $"{Vm.SelectedDeviceProfile.DisplayName} / {Vm.ConnectionText}";

    public string DeviceTitle => !Vm.IsSystemEqSelected && !string.IsNullOrWhiteSpace(Vm.DeviceNickname)
        ? Vm.DeviceNickname.Trim()
        : Vm.SelectedDeviceProfile.DisplayLabel;

    /// <summary>The confirmed hardware slot when known, else the editor's target slot.</summary>
    private DeviceUserPresetOption? ActiveSlot
        => Vm.DeviceUserPresets.FirstOrDefault(p => p.PresetId == Vm.TrayConfirmedPresetId) ?? Vm.SelectedDeviceUserPreset;

    public string NowTitle
    {
        get
        {
            if (Vm.IsSystemEqSelected) return Vm.SelectedPreset?.Name ?? "Custom EQ";
            var slot = ActiveSlot;
            if (slot is null) return "No preset";
            return string.IsNullOrWhiteSpace(slot.LocalEqName) ? slot.SlotLabel : slot.LocalEqName!;
        }
    }

    public string NowSubtitle
    {
        get
        {
            if (Vm.IsSystemEqSelected)
            {
                // The device row already shows the Software EQ status line; say what this is.
                return Vm.EditorSession.WouldReplaceUnsavedEdits ? "Unsaved changes in the editor"
                    : Vm.SystemEqBypassed ? "EQ is off. Turn it on to hear it."
                    : Vm.SelectedPreset is { } preset && Vm.Presets.Contains(preset) ? $"From your library · {preset.Category}"
                    : "Editor content";
            }
            var slot = ActiveSlot;
            if (slot is null) return Vm.ConnectionText;
            var where = !Vm.IsDeviceConnected ? "Device not connected"
                : Vm.TrayConfirmedPresetId == slot.PresetId ? "Active on device"
                : "Editor target";
            return string.IsNullOrWhiteSpace(slot.LocalEqName) ? where : $"{slot.SlotLabel} · {where}";
        }
    }

    public string Message => _switching ? _progress
        : _result ?? (Vm.EditorSession.WouldReplaceUnsavedEdits
            ? "Save or discard editor changes to switch presets."
            : !Vm.IsDeviceConnected ? "Connect your device to switch presets."
            : IsWorking ? "Device busy. Presets will be ready shortly."
            : string.Empty);
    public event PropertyChangedEventHandler? PropertyChanged;

    public TrayPanelState(MainViewModel vm)
    {
        Vm = vm;
        Vm.PropertyChanged += OnVmChanged;
        Vm.EditorSession.Changed += OnEditorChanged;
        Vm.DeviceUserPresets.CollectionChanged += OnSlotsChanged;
        Vm.Presets.CollectionChanged += OnPresetsChanged;
        RebuildSlots();
    }

    // The view model raises many changes while a band is dragged; only these affect the flyout.
    private static readonly HashSet<string> FlyoutProperties = new(StringComparer.Ordinal)
    {
        nameof(MainViewModel.Status), nameof(MainViewModel.SelectedPreset), nameof(MainViewModel.Presets),
        nameof(MainViewModel.SelectedDeviceProfile), nameof(MainViewModel.SelectedDeviceUserPreset),
        nameof(MainViewModel.TrayConfirmedPresetId), nameof(MainViewModel.IsDeviceConnected), nameof(MainViewModel.ConnectionText),
        nameof(MainViewModel.ConnectionTone), nameof(MainViewModel.IsSystemEqSelected), nameof(MainViewModel.IsHardwareDeviceSelected), nameof(MainViewModel.SystemEqBypassed),
        nameof(MainViewModel.SystemEqStatusText), nameof(MainViewModel.IntegrationControlsEnabled), nameof(MainViewModel.DeviceNickname),
        nameof(MainViewModel.FavoritePresetCount), nameof(MainViewModel.IsProfileLibraryDirty)
    };

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && !FlyoutProperties.Contains(name))
        {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.Status))
        {
            if (_switching) _progress = Vm.Status;
            else _result = null;
        }
        Refresh();
    }
    private void OnEditorChanged() => Refresh();
    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildSlots();
    private void OnPresetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();
    private void RebuildSlots()
    {
        Slots.Clear();
        foreach (var slot in Vm.DeviceUserPresets) Slots.Add(new TraySlotState(slot));
        Refresh();
    }

    /// <summary>Syncs the quick-preset rows with the VM list; only rebuilds when the set or order changed.</summary>
    private void SyncQuickPresets()
    {
        var next = Vm.TrayQuickPresets;
        // Rows snapshot name and favorite (EqPreset has no change notifications), so a rename or
        // star change rebuilds them too.
        if (!next.SequenceEqual(QuickPresets.Select(row => row.Preset))
            || QuickPresets.Any(row => row.IsFavorite != row.Preset.IsFavorite || row.Name != row.Preset.Name))
        {
            QuickPresets.Clear();
            foreach (var preset in next) QuickPresets.Add(new TrayPresetState(preset));
        }
        foreach (var row in QuickPresets) row.Update(Vm.SelectedPreset);
    }

    public void Refresh()
    {
        foreach (var slot in Slots) slot.Update(Vm.TrayConfirmedPresetId);
        SyncQuickPresets();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public async Task SwitchAsync(int slot)
    {
        if (!CanSwitch) return;
        _switching = true;
        _result = null;
        _progress = $"Checking your device for preset {slot}...";
        Refresh();
        try
        {
            var result = await Vm.ExecuteAutomationAsync(new AutomationRequest(Slot: slot));
            _result = result.Message;
        }
        catch (Exception ex) { _result = $"Preset unchanged or unconfirmed: {ex.Message}"; }
        finally { _switching = false; Refresh(); }
    }

    /// <summary>Opens a quick preset in the editor; Software EQ applies it automatically.</summary>
    public void OpenPreset(EqPreset preset)
    {
        Vm.TryOpenTrayQuickPreset(preset);
        Refresh();
    }

    public void Dispose()
    {
        Vm.PropertyChanged -= OnVmChanged;
        Vm.EditorSession.Changed -= OnEditorChanged;
        Vm.DeviceUserPresets.CollectionChanged -= OnSlotsChanged;
        Vm.Presets.CollectionChanged -= OnPresetsChanged;
    }
}

internal sealed class TraySlotState(DeviceUserPresetOption option) : INotifyPropertyChanged
{
    private bool HasName => !string.IsNullOrWhiteSpace(option.LocalEqName);
    public int Slot => option.Slot;
    public string Title => HasName ? option.LocalEqName! : option.SlotLabel;
    public string Subtitle => HasName ? option.SlotLabel : "Saved on device";
    public string Label => option.SlotLabel + (IsActive ? " · Active" : "");
    public string ToolTip => HasName ? $"{Label}\n{option.LocalEqName}" : Label;
    public bool IsActive { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(byte? activePreset)
    {
        var active = activePreset == option.PresetId;
        if (active == IsActive) return;
        IsActive = active;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

internal sealed class TrayPresetState(EqPreset preset) : INotifyPropertyChanged
{
    public EqPreset Preset => preset;
    public string Name { get; } = preset.Name;
    public bool IsFavorite { get; } = preset.IsFavorite;
    public string IconName => IsFavorite ? "Heart" : "SlidersHorizontal";
    public string Subtitle => IsFavorite ? $"Favorite · {preset.Category}" : preset.Category;
    public bool IsCurrent { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(EqPreset? current)
    {
        var isCurrent = ReferenceEquals(current, preset);
        if (isCurrent == IsCurrent) return;
        IsCurrent = isCurrent;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
    }
}
