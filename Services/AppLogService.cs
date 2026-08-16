using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace WolfEQ.Services;

public sealed class AppLogService
{
    private const long MaximumLogBytes = 5L * 1024 * 1024;
    private const int MaximumMessageCharacters = 4000;
    private readonly object _syncRoot = new();

    public AppLogService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDirectory = Path.Combine(localAppData, "WolfEQ", "logs");

        Directory.CreateDirectory(logDirectory);

        LogFilePath = Path.Combine(logDirectory, "wolfeq.log");
        RotateLogIfNeeded();

        WriteSeparator();
        WriteLine("WolfEQ session started");
        WriteLine($"Version: {GetAppVersion()}");
        WriteLine($"Process: {Environment.ProcessId}");
        WriteLine($"Base directory: {AppContext.BaseDirectory}");
    }

    public string LogFilePath { get; }

    public void WriteLine(string message)
    {
        var safeMessage = (message ?? string.Empty)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
        if (safeMessage.Length > MaximumMessageCharacters)
        {
            safeMessage = safeMessage[..MaximumMessageCharacters] + "... [truncated]";
        }

        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {safeMessage}";

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

    private void RotateLogIfNeeded()
    {
        try
        {
            var log = new FileInfo(LogFilePath);
            if (!log.Exists || log.Length <= MaximumLogBytes)
            {
                return;
            }

            var previousPath = Path.Combine(log.DirectoryName!, "wolfeq.previous.log");
            File.Move(LogFilePath, previousPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"WolfEQ log rotation failed: {ex.Message}");
        }
    }

    private static string GetAppVersion()
        => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
}
