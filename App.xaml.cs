using System.Windows;
using WolfEQ.Services;

namespace WolfEQ;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppUpdateService.OnShutdownRequested = () => Dispatcher.Invoke(Shutdown);
    }
}
