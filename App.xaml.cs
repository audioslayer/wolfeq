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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppUpdateService.OnShutdownRequested = () => Dispatcher.Invoke(Shutdown);
        try
        {
            if (e.Args.Length > 0) AttachConsole(unchecked((uint)-1));
            var request = AutomationRequest.Parse(e.Args);
            if (request.Help) { Finish(new(0, AutomationRequest.Usage)); return; }
            _automation = new AutomationHost();
            if (!_automation.TryOwn())
            {
                Finish(await _automation.SendAsync(e.Args));
                return;
            }
            var window = new MainWindow();
            MainWindow = window;
            _viewModel = (MainViewModel)window.DataContext;
            _ = _automation.RunAsync(HandleRequestAsync);
            if (e.Args.Length == 0)
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Show();
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
                if (args.Length == 0) { MainWindow.Show(); MainWindow.Activate(); }
                return await _viewModel!.ExecuteAutomationAsync(request);
            }
            catch (ArgumentException ex) { return new AutomationResult(2, ex.Message); }
            catch (Exception ex) { return new AutomationResult(1, ex.Message); }
        }).Task.Unwrap();
    }

    private void Finish(AutomationResult result)
    {
        AutomationHost.Report(result);
        Shutdown(result.ExitCode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _automation?.Dispose();
        base.OnExit(e);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
