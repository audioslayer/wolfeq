using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WolfEQ.Services.SystemEq;
using WolfEQ.ViewModels;
using WolfEQ.Views;

namespace WolfEQ.Services;

/// <summary>Owns the native notification icon independently of the editor's visibility.</summary>
public sealed class TrayIconService : IDisposable
{
    private const int CallbackMessage = 0x8001;
    /// <summary>NOTIFYICONDATA.szTip holds 128 UTF-16 units including the terminator.</summary>
    internal const int MaxTipLength = 127;
    private readonly MainWindow _editor;
    private readonly MainViewModel _viewModel;
    private readonly HwndSource _source;
    private readonly TrayFlyout _flyout;
    private readonly DispatcherTimer _clickTimer;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _disposed;
    private bool _ignoreNextLeftUp;
    private NotifyIconData _data;
    private WindowState _restoreState = WindowState.Normal;

    public bool IsAvailable { get; private set; }

    public TrayIconService(MainWindow editor, MainViewModel viewModel, Action quit)
    {
        _editor = editor;
        _viewModel = viewModel;
        _flyout = new TrayFlyout(viewModel, RestoreEditor, () => { RestoreEditor(); editor.OpenSettings(); }, quit);
        // A hidden top-level window receives TaskbarCreated broadcasts; a message-only window does not.
        _source = new HwndSource(new HwndSourceParameters("WolfEQ tray messages")
        {
            Width = 0, Height = 0, WindowStyle = 0, ParentWindow = IntPtr.Zero
        });
        _source.AddHook(WindowProc);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()) };
        _clickTimer.Tick += OnClickTimer;

        var executable = Environment.ProcessPath;
        if (executable is not null)
        {
            var icons = new IntPtr[1];
            if (ExtractIconEx(executable, 0, null, icons, 1) > 0)
            {
                _icon = icons[0];
                _ownsIcon = _icon != IntPtr.Zero;
            }
        }
        if (_icon == IntPtr.Zero) _icon = LoadIcon(IntPtr.Zero, new IntPtr(32512));
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _source.Handle,
            Id = 1, Flags = 1 | 2 | 4, Callback = CallbackMessage, Icon = _icon,
            Tip = "WolfEQ — quick profiles", Info = "", InfoTitle = ""
        };
        AddIcon();
        viewModel.PropertyChanged += OnViewModelChanged;
        UpdateTip();
    }

    public void RememberWindowState()
    {
        if (_editor.WindowState != WindowState.Minimized) _restoreState = _editor.WindowState;
    }

    public void HideEditor()
    {
        if (!IsAvailable) return;
        RememberWindowState();
        _editor.Hide();
    }

    public void RestoreEditor()
    {
        _flyout.Hide();
        _editor.Show();
        if (_editor.WindowState == WindowState.Minimized) _editor.WindowState = _restoreState;
        _editor.Activate();
        SetForegroundWindow(new WindowInteropHelper(_editor).Handle);
    }

    private void AddIcon()
    {
        IsAvailable = _icon != IntPtr.Zero && Shell_NotifyIcon(0, ref _data);
        if (!IsAvailable)
        {
            Debug.WriteLine("WolfEQ notification icon is unavailable; keeping editor accessible.");
            RestoreEditor();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ConnectionText):
            case nameof(MainViewModel.IsSystemEqSelected):
            case nameof(MainViewModel.SelectedDeviceProfile):
            case nameof(MainViewModel.SystemEqStatusText):
            case nameof(MainViewModel.SystemEqBypassed):
            case nameof(MainViewModel.SystemEqState):
            case nameof(MainViewModel.HasSystemEqWarning):
            case nameof(MainViewModel.SystemEqWarningText):
            case null:
            case "":
                UpdateTip();
                break;
        }
    }

    private void UpdateTip()
    {
        if (_disposed || !IsAvailable) return;
        var tip = BuildTip(_viewModel.IsSystemEqSelected, _viewModel.ConnectionText, _viewModel.SystemEqStatusText,
            _viewModel.SystemEqBypassed, _viewModel.SystemEqState, _viewModel.HasSystemEqWarning, _viewModel.SystemEqWarningText);
        if (tip == _data.Tip) return;
        _data.Tip = tip;
        Shell_NotifyIcon(1, ref _data);
    }

    /// <summary>
    /// Builds the notification-area tooltip, already truncated to <see cref="MaxTipLength"/>.
    /// Hardware mode shows the connection text. System EQ mode shows the status line as-is (it
    /// already names System EQ), and when bypassed still surfaces setup problems or warnings so
    /// "bypassed" never hides that the EQ could not reach Windows anyway.
    /// </summary>
    internal static string BuildTip(bool isSystemEq, string? connectionText, string? statusText, bool bypassed,
        SystemEqState state, bool hasWarning, string? warningText)
    {
        string detail;
        if (!isSystemEq)
        {
            detail = connectionText?.Trim() ?? "";
        }
        else
        {
            const string bypassedText = "System EQ bypassed";
            var status = TrimSentence(statusText);
            if (bypassed && (status.Length == 0 || status.StartsWith(bypassedText, StringComparison.OrdinalIgnoreCase)))
            {
                // A bypass-only status must not hide that setup is incomplete.
                status = state switch
                {
                    SystemEqState.NeedsInstall => "Equalizer APO is not installed",
                    SystemEqState.NeedsDeviceSelection => "Equalizer APO is not active on any output yet",
                    SystemEqState.Blocked => "WolfEQ can't write to the Equalizer APO config folder",
                    _ => status
                };
            }
            var warning = hasWarning ? TrimSentence(warningText) : "";
            var parts = new List<string>(3);
            if (bypassed) parts.Add(bypassedText);
            // The status line already says "System EQ bypassed" once Active, and the Blocked
            // warning repeats the Blocked status with more detail; show each fact once.
            if (status.Length > 0 && !status.StartsWith(bypassedText, StringComparison.OrdinalIgnoreCase)
                && !warning.StartsWith(status, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(status);
            }
            if (warning.Length > 0) parts.Add(warning);
            detail = parts.Count == 0 ? "Windows System EQ" : string.Join(" · ", parts);
        }

        return Truncate($"WolfEQ — {(detail.Length == 0 ? "quick profiles" : detail)}", MaxTipLength);
    }

    private static string TrimSentence(string? text)
    {
        var trimmed = text?.Trim() ?? "";
        // Drop a single closing period but keep an ellipsis such as "Checking Equalizer APO...".
        return trimmed.EndsWith('.') && !trimmed.EndsWith("..", StringComparison.Ordinal) ? trimmed[..^1] : trimmed;
    }

    /// <summary>Cuts at a word boundary when one is close, ending with an ellipsis.</summary>
    internal static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var cut = maxLength - 1;
        if (char.IsLowSurrogate(text[cut])) cut--;
        var space = text.LastIndexOf(' ', cut);
        if (space > maxLength - 24) cut = space;
        return text[..cut].TrimEnd(' ', '·', ',', ';', ':', '—', '-', '.') + "…";
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_disposed) return IntPtr.Zero;
        if ((uint)message == _taskbarCreated)
        {
            AddIcon();
        }
        else if (message == CallbackMessage)
        {
            switch (lParam.ToInt32())
            {
                case 0x202: // WM_LBUTTONUP: defer to distinguish double click.
                    if (_ignoreNextLeftUp) { _ignoreNextLeftUp = false; break; }
                    _clickTimer.Stop();
                    _clickTimer.Start();
                    break;
                case 0x203: // WM_LBUTTONDBLCLK
                    _clickTimer.Stop();
                    _ignoreNextLeftUp = true;
                    RestoreEditor();
                    break;
                case 0x205: // WM_RBUTTONUP
                case 0x7B:  // WM_CONTEXTMENU
                case 0x400: // NIN_SELECT
                case 0x401: // NIN_KEYSELECT
                    _clickTimer.Stop();
                    ShowFlyout();
                    break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnClickTimer(object? sender, EventArgs e)
    {
        _clickTimer.Stop();
        ShowFlyout();
    }

    private void ShowFlyout()
    {
        if (_flyout.IsVisible) _flyout.Hide();
        else _flyout.ShowNearTray();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clickTimer.Stop();
        _clickTimer.Tick -= OnClickTimer;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        Shell_NotifyIcon(2, ref _data);
        IsAvailable = false;
        _flyout.DisposeFlyout();
        _source.RemoveHook(WindowProc);
        _source.Dispose();
        if (_ownsIcon) DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? largeIcons, IntPtr[]? smallIcons, uint count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
