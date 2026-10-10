using System.IO;
using System.Windows;
using WolfEQ.Services.SystemEq;

namespace WolfEQ.ViewModels;

/// <summary>
/// Watches Equalizer APO's config.txt while Software EQ is selected. If another tool (Peace, a
/// hand edit) rewrites it and drops WolfEQ's managed block, the next apply puts it back (R10).
/// The backend only rewrites files whose content changed, so our own writes do not loop.
/// </summary>
public sealed partial class MainViewModel
{
    private FileSystemWatcher? _systemEqConfigWatcher;
    private string? _systemEqWatchedDirectory;

    private void UpdateSystemEqConfigWatcher()
    {
        var directory = IsSystemEqSelected && _systemEqBackendStatus is { IsInstalled: true } status
            ? status.ConfigDirectory
            : null;
        if (string.Equals(directory, _systemEqWatchedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopSystemEqConfigWatcher();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory, ApoConfigIncludeManager.ConfigFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
            };
            watcher.Changed += SystemEqConfigOnChanged;
            watcher.Created += SystemEqConfigOnChanged;
            watcher.Renamed += SystemEqConfigOnChanged;
            watcher.Deleted += SystemEqConfigOnChanged;
            watcher.EnableRaisingEvents = true;
            _systemEqConfigWatcher = watcher;
            _systemEqWatchedDirectory = directory;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            AddLog($"Software EQ cannot watch Equalizer APO's config.txt: {ex.Message}");
        }
    }

    private void StopSystemEqConfigWatcher()
    {
        if (_systemEqConfigWatcher is not null)
        {
            _systemEqConfigWatcher.EnableRaisingEvents = false;
            _systemEqConfigWatcher.Dispose();
            _systemEqConfigWatcher = null;
        }

        _systemEqWatchedDirectory = null;
    }

    private void SystemEqConfigOnChanged(object sender, FileSystemEventArgs e)
    {
        // Raised on a thread-pool thread; the debounce timer lives on the UI dispatcher.
        // A repair re-apply, not a user change: it waits while the startup hold is active.
        Application.Current?.Dispatcher.BeginInvoke(() => QueueSystemEqApply(isUserChange: false));
    }
}
