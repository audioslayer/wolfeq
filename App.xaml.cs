using System.Windows;
using System.Runtime.InteropServices;
using WolfEQ.Services;
using WolfEQ.ViewModels;

namespace WolfEQ;

public partial class App : Application
{
    private AutomationHost? _automation;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MainViewModel? _viewModel;
    private TrayIconService? _tray;
    /// <summary>Hidden flag the uninstaller passes to remove WolfEQ's block and file from Equalizer APO.</summary>
    internal const string RemoveSoftwareEqArgument = "--remove-software-eq";

    internal bool IsExiting { get; private set; }
    internal bool CanHideToTray => _tray?.IsAvailable == true;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppUpdateService.OnShutdownRequested = () => Dispatcher.Invoke(() => { IsExiting = true; Shutdown(); });
        try
        {
            if (e.Args.Length > 0) AttachConsole(unchecked((uint)-1));
            if (e.Args.Length == 1 && string.Equals(e.Args[0], RemoveSoftwareEqArgument, StringComparison.OrdinalIgnoreCase))
            {
                // Run by the uninstaller: take WolfEQ's EQ out of Equalizer APO so it doesn't keep
                // playing after WolfEQ is gone. No window, no automation pipe.
                var removal = await ViewModels.SystemEqServices.CreateDefault().Backend.RemoveAsync();
                Finish(new(removal.Succeeded ? 0 : 1, removal.Message));
                return;
            }

            var request = AutomationRequest.Parse(e.Args);
            if (request.Help) { Finish(new(0, AutomationRequest.Usage)); return; }
            _automation = new AutomationHost();
            if (!_automation.TryOwn())
            {
                Finish(await _automation.SendAsync(e.Args));
                return;
            }
            BackgroundThemeService.Apply(BackgroundThemeService.Load());
            var window = new MainWindow();
            MainWindow = window;
            _viewModel = (MainViewModel)window.DataContext;
            _ = _automation.RunAsync(HandleRequestAsync);
            if (e.Args.Length == 0)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                window.Show();
                try { _tray = new TrayIconService(window, _viewModel, RequestQuit); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Tray initialization failed: {ex.Message}"); }
                await _viewModel.AutoConnectAndReadDeviceAsync();
                _ready.TrySetResult();
            }
            else
            {
                // Cold automation runs without presenting a window or loading over unsaved UI state.
                _ready.TrySetResult();
                Finish(await _viewModel.ExecuteAutomationAsync(request));
            }
        }
        catch (ArgumentException ex) { Finish(new(2, ex.Message + "\n" + AutomationRequest.Usage)); }
        catch (Exception ex) { Finish(new(1, ex.Message)); }
    }

    private async Task<AutomationResult> HandleRequestAsync(string[] args)
    {
        await _ready.Task;
        return await Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                var request = AutomationRequest.Parse(args);
                if (args.Length == 0) RestoreEditor();
                return await _viewModel!.ExecuteAutomationAsync(request);
            }
            catch (ArgumentException ex) { return new AutomationResult(2, ex.Message); }
            catch (Exception ex) { return new AutomationResult(1, ex.Message); }
        }).Task.Unwrap();
    }

    private void Finish(AutomationResult result)
    {
        AutomationHost.Report(result);
        IsExiting = true;
        Shutdown(result.ExitCode);
    }

    internal void HideEditor() => _tray?.HideEditor();
    internal void RememberWindowState() => _tray?.RememberWindowState();

    internal void RestoreEditor()
    {
        if (_tray is not null) _tray.RestoreEditor();
        else
        {
            MainWindow.Show();
            if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        }
    }

    internal void RequestQuit()
    {
        if (IsExiting) return;
        if (_viewModel is not null && !_viewModel.IntegrationControlsEnabled)
        {
            RestoreEditor();
            MessageBox.Show(MainWindow, "Wait for the current device operation to finish before quitting.",
                "WolfEQ is busy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_viewModel?.HasUnsavedTrayEdits == true)
        {
            RestoreEditor();
            if (!WolfEQ.Views.WolfDialog.Confirm(MainWindow, new ConfirmationRequest(
                    "Quit WolfEQ?",
                    "You have unsaved changes. Quit WolfEQ and discard them?",
                    "Quit and discard",
                    IsDestructive: true)))
                return;
        }
        IsExiting = true;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _automation?.Dispose();
        base.OnExit(e);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
