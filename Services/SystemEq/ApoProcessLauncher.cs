using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Runs the bundled Equalizer APO installer or its DeviceSelector elevated (KTD11) and maps the
/// outcome to a calm <see cref="ApoLaunchResult"/>. A cancelled UAC prompt is a result, not an error.
/// </summary>
public sealed class ApoProcessLauncher
{
    /// <summary>
    /// The only installer WolfEQ will run. It must match the <c>EqualizerApoInstallerFileName</c> property in
    /// WolfEQ.csproj and the file build-installer.bat downloads and hash-checks (KTD14).
    /// </summary>
    public const string PinnedInstallerFileName = "EqualizerAPO-x64-1.4.2.exe";

    private const int ErrorCancelled = 1223;

    // The installer is NSIS: 1 = the user cancelled the wizard, 2 = the script aborted.
    private const int InstallerExitCancelled = 1;

    // DeviceSelector returns 1 when the endpoint changes need a reboot to take effect.
    private const int DeviceSelectorExitRebootRequired = 1;

    private enum LaunchKind
    {
        Installer,
        DeviceSelector
    }

    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> _runProcess;
    private readonly Func<string, bool> _fileExists;

    /// <param name="runProcess">Starts the process and returns its exit code; injectable for tests.</param>
    /// <param name="fileExists">File check; injectable for tests.</param>
    public ApoProcessLauncher(
        Func<ProcessStartInfo, CancellationToken, Task<int>>? runProcess = null,
        Func<string, bool>? fileExists = null)
    {
        _runProcess = runProcess ?? RunAndWaitAsync;
        _fileExists = fileExists ?? File.Exists;
    }

    public Task<ApoLaunchResult> RunInstallerAsync(string installerPath, CancellationToken cancellationToken = default)
        => RunElevatedAsync(installerPath, LaunchKind.Installer, cancellationToken);

    public Task<ApoLaunchResult> RunDeviceSelectorAsync(string deviceSelectorPath, CancellationToken cancellationToken = default)
        => RunElevatedAsync(deviceSelectorPath, LaunchKind.DeviceSelector, cancellationToken);

    /// <summary>
    /// Finds exactly <c>redist\EqualizerAPO\</c><see cref="PinnedInstallerFileName"/> under the app folder.
    /// No other file is ever picked, because the result is run elevated.
    /// </summary>
    public static string? FindBundledInstaller(string appBaseDirectory)
    {
        try
        {
            var path = Path.Combine(appBaseDirectory, "redist", "EqualizerAPO", PinnedInstallerFileName);
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    internal static ApoLaunchResult MapInstallerExitCode(int exitCode) => exitCode switch
    {
        0 => new ApoLaunchResult(ApoLaunchOutcome.Completed, "The Equalizer APO installer finished."),
        InstallerExitCancelled => new ApoLaunchResult(ApoLaunchOutcome.Cancelled, "Equalizer APO setup was canceled, so nothing changed."),
        _ => new ApoLaunchResult(ApoLaunchOutcome.Failed, $"The Equalizer APO installer exited with code {exitCode}.")
    };

    internal static ApoLaunchResult MapDeviceSelectorExitCode(int exitCode) => exitCode switch
    {
        0 => new ApoLaunchResult(ApoLaunchOutcome.Completed, "The Equalizer APO Device Selector finished."),
        DeviceSelectorExitRebootRequired => new ApoLaunchResult(ApoLaunchOutcome.RebootRequired,
            "Restart Windows to finish setting up Equalizer APO."),
        _ => new ApoLaunchResult(ApoLaunchOutcome.Failed, $"The Equalizer APO Device Selector exited with code {exitCode}.")
    };

    private async Task<ApoLaunchResult> RunElevatedAsync(string path, LaunchKind kind, CancellationToken cancellationToken)
    {
        var displayName = kind == LaunchKind.Installer ? "Equalizer APO installer" : "Equalizer APO Device Selector";
        if (string.IsNullOrWhiteSpace(path) || !_fileExists(path))
        {
            return new ApoLaunchResult(ApoLaunchOutcome.NotFound, $"The {displayName} wasn't found.");
        }

        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
        };

        try
        {
            var exitCode = await _runProcess(startInfo, cancellationToken).ConfigureAwait(false);
            return kind == LaunchKind.Installer ? MapInstallerExitCode(exitCode) : MapDeviceSelectorExitCode(exitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new ApoLaunchResult(ApoLaunchOutcome.Cancelled, "Administrator permission was declined, so nothing changed.");
        }
        catch (OperationCanceledException)
        {
            return new ApoLaunchResult(ApoLaunchOutcome.Cancelled, $"Stopped waiting for the {displayName}.");
        }
        catch (Exception ex)
        {
            return new ApoLaunchResult(ApoLaunchOutcome.Failed, $"Couldn't start the {displayName}: {ex.Message}");
        }
    }

    private static async Task<int> RunAndWaitAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The process did not start.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
