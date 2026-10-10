using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Runs the bundled Equalizer APO installer or its DeviceSelector elevated (KTD11) and maps the
/// outcome to a calm <see cref="ApoLaunchResult"/>. A cancelled UAC prompt is a result, not an error.
/// </summary>
public sealed partial class ApoProcessLauncher
{
    private const int ErrorCancelled = 1223;

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
        => RunElevatedAsync(installerPath, "Equalizer APO installer", cancellationToken);

    public Task<ApoLaunchResult> RunDeviceSelectorAsync(string deviceSelectorPath, CancellationToken cancellationToken = default)
        => RunElevatedAsync(deviceSelectorPath, "Equalizer APO Device Selector", cancellationToken);

    /// <summary>
    /// Finds <c>redist\EqualizerAPO\EqualizerAPO-x64-*.exe</c> under the app folder, preferring the highest version.
    /// </summary>
    public static string? FindBundledInstaller(string appBaseDirectory)
    {
        try
        {
            var folder = Path.Combine(appBaseDirectory, "redist", "EqualizerAPO");
            if (!Directory.Exists(folder))
            {
                return null;
            }

            return Directory.EnumerateFiles(folder, "EqualizerAPO-x64-*.exe")
                .OrderByDescending(ParseInstallerVersion)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    internal static Version ParseInstallerVersion(string path)
    {
        var match = InstallerVersionRegex().Match(Path.GetFileName(path));
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version)
            ? version
            : new Version(0, 0);
    }

    [GeneratedRegex(@"^EqualizerAPO-x64-(\d+(?:\.\d+){1,3})\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerVersionRegex();

    private async Task<ApoLaunchResult> RunElevatedAsync(string path, string displayName, CancellationToken cancellationToken)
    {
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
            return exitCode switch
            {
                0 => new ApoLaunchResult(ApoLaunchOutcome.Completed, $"The {displayName} finished."),
                1 => new ApoLaunchResult(ApoLaunchOutcome.RebootRequired,
                    "Restart Windows to finish setting up Equalizer APO."),
                _ => new ApoLaunchResult(ApoLaunchOutcome.Failed, $"The {displayName} exited with code {exitCode}.")
            };
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
