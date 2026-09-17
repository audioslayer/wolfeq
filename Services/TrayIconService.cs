using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WolfEQ.ViewModels;
using WolfEQ.Views;

namespace WolfEQ.Services;

/// <summary>Owns the native notification icon independently of the editor's visibility.</summary>
public sealed class TrayIconService : IDisposable
{
    private const int CallbackMessage = 0x8001;
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
        if (_disposed || !IsAvailable || (e.PropertyName is not null && e.PropertyName != nameof(MainViewModel.ConnectionText))) return;
        var tip = $"WolfEQ — {_viewModel.ConnectionText}";
        _data.Tip = tip.Length > 127 ? tip[..127] : tip;
        Shell_NotifyIcon(1, ref _data);
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
