using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace WolfEQ.Services;

public sealed class AppLogService
{
    private readonly object _syncRoot = new();

    public AppLogService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDirectory = Path.Combine(localAppData, "WolfEQ", "logs");

        Directory.CreateDirectory(logDirectory);

        LogFilePath = Path.Combine(logDirectory, "wolfeq.log");

        WriteSeparator();
        WriteLine("WolfEQ session started");
        WriteLine($"Version: {GetAppVersion()}");
        WriteLine($"Process: {Environment.ProcessId}");
        WriteLine($"Base directory: {AppContext.BaseDirectory}");
    }

    public string LogFilePath { get; }

    public void WriteLine(string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}";

        try
        {
            lock (_syncRoot)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WolfEQ file logging failed: {ex.Message}");
        }
    }

    private void WriteSeparator()
    {
        try
        {
            lock (_syncRoot)
            {
                File.AppendAllText(LogFilePath, Environment.NewLine + new string('-', 88) + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WolfEQ file logging failed: {ex.Message}");
        }
    }

    private static string GetAppVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
}
