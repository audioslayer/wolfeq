using System.Runtime.CompilerServices;
using WolfEQ.Models;
using WolfEQ.Services.SystemEq;

[assembly: InternalsVisibleTo("WolfEQ.Tests")]

namespace WolfEQ.ViewModels;

/// <summary>
/// The Software EQ services the view model talks to. Tests pass fakes; the app uses
/// <see cref="CreateDefault"/>.
/// </summary>
/// <param name="GetEndpointStatus">Per-endpoint APO health, keyed by the endpoint GUID (with braces).</param>
/// <param name="IsFiioEndpoint">True when the endpoint is a FiiO device (double-EQ warning).</param>
/// <param name="RunInstaller">Runs the bundled Equalizer APO installer and waits for it to exit.</param>
/// <param name="RunInstallerWithProgress">Same as <paramref name="RunInstaller"/>, reporting download progress; preferred when set.</param>
/// <param name="RunDeviceSelector">Runs APO's DeviceSelector ("Add outputs") and waits for it to exit.</param>
public sealed record SystemEqServices(
    IEqOutputBackend Backend,
    IAudioEndpointCatalog Catalog,
    Func<string, ApoEndpointStatus> GetEndpointStatus,
    Func<AudioEndpointInfo, bool> IsFiioEndpoint,
    Func<Task<ApoLaunchResult>> RunInstaller,
    Func<Task<ApoLaunchResult>> RunDeviceSelector,
    Func<IProgress<ApoSetupProgress>, Task<ApoLaunchResult>>? RunInstallerWithProgress = null)
{
    /// <summary>Test hook: when set, <see cref="CreateDefault"/> returns this instead of the real services.</summary>
    internal static Func<SystemEqServices>? DefaultOverride { get; set; }

    /// <summary>
    /// Test hook: run endpoint enumeration and per-endpoint health reads on the caller's thread
    /// instead of the thread pool, so fake-backed view-model tests stay synchronous.
    /// </summary>
    internal bool QueryEndpointsInline { get; init; }

    /// <summary>The real Equalizer APO services (registry detection, NAudio endpoints, elevated launchers).</summary>
    public static SystemEqServices CreateDefault()
    {
        if (DefaultOverride is not null)
        {
            return DefaultOverride();
        }

        var installation = new EqualizerApoInstallation(new WindowsRegistryReader());
        var launcher = new ApoProcessLauncher();
        var downloader = new ApoInstallerDownloader();
        return new(
            new EqualizerApoBackend(_ => Task.Run(installation.GetStatus)),
            new WindowsAudioEndpointCatalog(),
            installation.GetEndpointStatus,
            FiioEndpointMatcher.IsFiioEndpoint,
            () => RunInstallerAsync(launcher, downloader, null),
            () => installation.DeviceSelectorPath is { } selector
                ? launcher.RunDeviceSelectorAsync(selector)
                : Task.FromResult(new ApoLaunchResult(ApoLaunchOutcome.NotFound,
                    "Equalizer APO's device selector was not found. Install Equalizer APO first.")),
            progress => RunInstallerAsync(launcher, downloader, progress));
    }

    /// <summary>
    /// Runs the bundled installer when it is present and verified; otherwise downloads the
    /// official pinned installer, verifies its SHA-256, and runs that.
    /// </summary>
    internal static async Task<ApoLaunchResult> RunInstallerAsync(
        ApoProcessLauncher launcher,
        ApoInstallerDownloader downloader,
        IProgress<ApoSetupProgress>? progress)
    {
        progress?.Report(new ApoSetupProgress(ApoSetupStage.Verifying));
        var bundled = ApoProcessLauncher.FindBundledInstaller(AppContext.BaseDirectory);
        string? installer = bundled is not null && await ApoInstallerDownloader.MatchesPinnedHashAsync(bundled)
            ? bundled
            : null;
        if (installer is null)
        {
            var (downloaded, message) = await downloader.GetVerifiedInstallerAsync(progress);
            if (downloaded is null)
            {
                return new ApoLaunchResult(ApoLaunchOutcome.Failed, message);
            }

            installer = downloaded;
        }

        // Lock the file against writes, renames and deletes, then re-check its hash under the lock and
        // keep it locked until setup exits, so nothing can be swapped in between the check and the
        // elevated launch. Setup can still open it for reading and execution.
        System.IO.FileStream guard;
        try
        {
            guard = new System.IO.FileStream(installer, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return new ApoLaunchResult(ApoLaunchOutcome.Failed, $"Couldn't open the Equalizer APO installer: {ex.Message}");
        }

        using (guard)
        {
            var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(guard));
            if (!string.Equals(hash, ApoInstallerDownloader.PinnedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ApoLaunchResult(ApoLaunchOutcome.Failed,
                    "The Equalizer APO installer failed its integrity check, so it was not run.");
            }

            progress?.Report(new ApoSetupProgress(ApoSetupStage.WaitingForSetup));
            return await launcher.RunInstallerAsync(installer);
        }
    }
}

/// <summary>Safe stand-in used until the Equalizer APO backend is wired: reports "not installed" and writes nothing.</summary>
internal sealed class NullEqOutputBackend : IEqOutputBackend
{
    public const string UnavailableMessage = "Equalizer APO support is not available in this build.";

    public Task<SystemEqBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SystemEqBackendStatus.NotInstalled(UnavailableMessage));

    public Task<SystemEqApplyResult> ApplyAsync(
        EqPreset preset,
        IReadOnlyList<string> endpointGuids,
        bool bypassed,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new SystemEqApplyResult(SystemEqApplyOutcome.NotInstalled, UnavailableMessage));

    public Task<SystemEqApplyResult> RemoveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new SystemEqApplyResult(SystemEqApplyOutcome.Bypassed, "Nothing to remove."));
}

/// <summary>Endpoint catalog with no outputs.</summary>
internal sealed class EmptyAudioEndpointCatalog : IAudioEndpointCatalog
{
    public IReadOnlyList<AudioEndpointInfo> GetRenderEndpoints() => [];
}
