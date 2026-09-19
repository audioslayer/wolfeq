using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WolfEQ.Services;

namespace WolfEQ.Views;

public partial class EditorWorkspace
{
    private readonly DispatcherTimer _spectrumVisibilityTimer = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly float[] _spectrumTarget = new float[PlaybackSpectrumAnalyzer.BinCount];
    private readonly float[] _spectrumDisplay = new float[PlaybackSpectrumAnalyzer.BinCount];
    private PlaybackSpectrumCapture? _spectrumCapture;
    private Window? _spectrumWindow;
    private TimeSpan _lastSpectrumRender;
    private string? _spectrumStatus;
    private static string SpectrumPreferencePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WolfEQ", "spectrum-enabled.txt");

    private void InitializeSpectrum()
    {
        SpectrumToggle.IsChecked = true;
        try { SpectrumToggle.IsChecked = File.ReadAllText(SpectrumPreferencePath).Trim() != "false"; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        SpectrumToggle.Checked += SpectrumSettingChanged;
        SpectrumToggle.Unchecked += SpectrumSettingChanged;
        _spectrumVisibilityTimer.Tick += (_, _) => UpdateSpectrumMonitoring();
        Loaded += (_, _) =>
        {
            _spectrumWindow = Window.GetWindow(this);
            if (_spectrumWindow != null)
            {
                _spectrumWindow.StateChanged += SpectrumWindowChanged;
                _spectrumWindow.Activated += SpectrumWindowChanged;
                _spectrumWindow.Deactivated += SpectrumWindowChanged;
                _spectrumWindow.Closed += SpectrumWindowClosed;
            }
            UpdateSpectrumMonitoring();
        };
        Unloaded += (_, _) => DetachSpectrum();
        IsVisibleChanged += (_, _) => UpdateSpectrumMonitoring();
    }
    private void SpectrumWindowChanged(object? sender, EventArgs e) => UpdateSpectrumMonitoring();
    private void SpectrumWindowClosed(object? sender, EventArgs e) => DetachSpectrum();
    private void DetachSpectrum()
    {
        if (_spectrumWindow != null)
        {
            _spectrumWindow.StateChanged -= SpectrumWindowChanged;
            _spectrumWindow.Activated -= SpectrumWindowChanged;
            _spectrumWindow.Deactivated -= SpectrumWindowChanged;
            _spectrumWindow.Closed -= SpectrumWindowClosed;
        }
        _spectrumWindow = null;
        _spectrumVisibilityTimer.Stop();
        StopSpectrum();
    }
    private void SpectrumSettingChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SpectrumPreferencePath)!);
            File.WriteAllText(SpectrumPreferencePath, SpectrumToggle.IsChecked == true ? "true" : "false");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.TraceWarning($"Could not save spectrum preference: {ex.Message}"); }
        UpdateSpectrumMonitoring();
    }
    private bool IsSpectrumOnScreen()
    {
        if (!IsLoaded || !Graph.IsVisible || _spectrumWindow is not { IsVisible: true, IsActive: true }
            || _spectrumWindow.WindowState == WindowState.Minimized || Graph.ActualWidth <= 0 || Graph.ActualHeight <= 0) return false;
        // WPF IsVisible alone stays true when a ScrollViewer clips the graph away.
        var visible = new Rect(0, 0, Graph.ActualWidth, Graph.ActualHeight);
        for (DependencyObject? parent = VisualTreeHelper.GetParent(Graph); parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not FrameworkElement element || (!element.ClipToBounds && parent != _spectrumWindow)) continue;
            var viewport = element.TransformToDescendant(Graph).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            visible.Intersect(viewport);
            if (visible.IsEmpty || visible.Width < 1 || visible.Height < 1) return false;
        }
        return true;
    }
    private void UpdateSpectrumMonitoring()
    {
        var eligible = IsLoaded && IsVisible && SpectrumToggle.IsChecked == true && _spectrumWindow is { IsActive: true, IsVisible: true }
            && _spectrumWindow.WindowState != WindowState.Minimized;
        if (eligible) _spectrumVisibilityTimer.Start(); else _spectrumVisibilityTimer.Stop();
        if (!eligible || !IsSpectrumOnScreen()) { StopSpectrum(); return; }
        if (_spectrumCapture != null) return;
        _spectrumCapture = new PlaybackSpectrumCapture();
        _spectrumCapture.Start();
        _lastSpectrumRender = TimeSpan.Zero;
        CompositionTarget.Rendering += RenderSpectrumFrame;
    }
    private void RenderSpectrumFrame(object? sender, EventArgs e)
    {
        if (_spectrumCapture == null || e is not RenderingEventArgs frame) return;
        var elapsed = (frame.RenderingTime - _lastSpectrumRender).TotalSeconds;
        if (elapsed < 1.0 / 65) return; // Match display refresh, capped near 60 fps on high-refresh monitors.
        _lastSpectrumRender = frame.RenderingTime;
        elapsed = Math.Clamp(elapsed, 0.001, 0.1);
        _spectrumCapture.CopyLevels(_spectrumTarget);
        var attack = (float)(1 - Math.Exp(-elapsed / 0.045));
        var release = (float)(1 - Math.Exp(-elapsed / 0.26));
        var changed = false;
        for (var i = 0; i < _spectrumDisplay.Length; i++)
        {
            var before = _spectrumDisplay[i];
            var target = _spectrumTarget[i];
            _spectrumDisplay[i] += (target - before) * (target > before ? attack : release);
            if (_spectrumDisplay[i] < 0.001f) _spectrumDisplay[i] = 0;
            changed |= Math.Abs(before - _spectrumDisplay[i]) > 0.00001f || (before != 0 && _spectrumDisplay[i] == 0);
        }
        if (changed) Graph.SetSpectrumFrame(_spectrumDisplay);
        if (_spectrumStatus != _spectrumCapture.Status)
        {
            _spectrumStatus = _spectrumCapture.Status;
            SpectrumToggle.ToolTip = _spectrumStatus + "\nPauses while the window is in the background or the graph is offscreen.\nVisual display only; the spectrum uses a separate level scale from EQ gain.";
        }
    }
    private void StopSpectrum()
    {
        if (_spectrumCapture == null) return;
        CompositionTarget.Rendering -= RenderSpectrumFrame;
        _spectrumCapture.Dispose();
        _spectrumCapture = null;
        Array.Clear(_spectrumDisplay);
        Graph.SetSpectrumFrame(null);
    }
}
