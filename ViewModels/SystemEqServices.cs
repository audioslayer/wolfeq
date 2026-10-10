using System.Runtime.CompilerServices;
using WolfEQ.Models;
using WolfEQ.Services.SystemEq;

[assembly: InternalsVisibleTo("WolfEQ.Tests")]

namespace WolfEQ.ViewModels;

/// <summary>
/// The Windows System EQ services the view model talks to. Tests pass fakes; the app uses
/// <see cref="CreateDefault"/>.
/// </summary>
/// <param name="GetEndpointStatus">Per-endpoint APO health, keyed by the endpoint GUID (with braces).</param>
/// <param name="IsFiioEndpoint">True when the endpoint is a FiiO device (double-EQ warning).</param>
/// <param name="RunInstaller">Runs the bundled Equalizer APO installer and waits for it to exit.</param>
/// <param name="RunDeviceSelector">Runs APO's DeviceSelector ("Add outputs") and waits for it to exit.</param>
public sealed record SystemEqServices(
    IEqOutputBackend Backend,
    IAudioEndpointCatalog Catalog,
    Func<string, ApoEndpointStatus> GetEndpointStatus,
    Func<AudioEndpointInfo, bool> IsFiioEndpoint,
    Func<Task<ApoLaunchResult>> RunInstaller,
    Func<Task<ApoLaunchResult>> RunDeviceSelector)
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
        return new(
            new EqualizerApoBackend(_ => Task.Run(installation.GetStatus)),
            new WindowsAudioEndpointCatalog(),
            installation.GetEndpointStatus,
            FiioEndpointMatcher.IsFiioEndpoint,
            () => ApoProcessLauncher.FindBundledInstaller(AppContext.BaseDirectory) is { } installer
                ? launcher.RunInstallerAsync(installer)
                : Task.FromResult(new ApoLaunchResult(ApoLaunchOutcome.NotFound,
                    "The bundled Equalizer APO installer is missing. Reinstall WolfEQ or install Equalizer APO manually.")),
            () => installation.DeviceSelectorPath is { } selector
                ? launcher.RunDeviceSelectorAsync(selector)
                : Task.FromResult(new ApoLaunchResult(ApoLaunchOutcome.NotFound,
                    "Equalizer APO's device selector was not found. Install Equalizer APO first.")));
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
