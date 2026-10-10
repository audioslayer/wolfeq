namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Setup state of the Windows System EQ mode; drives the System EQ card in Settings.
/// </summary>
public enum SystemEqState
{
    NotSelected,
    Checking,
    NeedsInstall,
    NeedsDeviceSelection,
    Blocked,
    Active
}

public enum SystemEqApplyOutcome
{
    Applied,
    Bypassed,
    NoEndpoints,
    NotInstalled,
    Blocked,
    Failed
}

/// <param name="IsInstalled">Equalizer APO is really installed (DLL + CLSID), not just leftover registry/config.</param>
/// <param name="ConfigDirectory">APO config folder (registry ConfigPath), or null when unknown.</param>
/// <param name="InstallDirectory">APO install folder (registry InstallPath), or null when unknown.</param>
/// <param name="IsConfigWritable">WolfEQ can write into <paramref name="ConfigDirectory"/> without elevation.</param>
/// <param name="IsPeaceIncludeActive">config.txt has an active <c>Include: peace.txt</c> line.</param>
public sealed record SystemEqBackendStatus(
    bool IsInstalled,
    string? ConfigDirectory,
    string? InstallDirectory,
    bool IsConfigWritable,
    bool IsPeaceIncludeActive,
    string Message)
{
    public static SystemEqBackendStatus NotInstalled(string message)
        => new(false, null, null, false, false, message);
}

public sealed record SystemEqApplyResult(SystemEqApplyOutcome Outcome, string Message)
{
    public bool Succeeded => Outcome is SystemEqApplyOutcome.Applied
        or SystemEqApplyOutcome.Bypassed
        or SystemEqApplyOutcome.NoEndpoints;
}

/// <param name="Id">Full IMMDevice id, e.g. <c>{0.0.0.00000000}.{guid}</c>.</param>
/// <param name="Guid">The trailing endpoint GUID including braces, used in APO <c>Device:</c> lines.</param>
/// <param name="FriendlyName">e.g. "Headphones (FiiO K13 R2R)".</param>
/// <param name="DeviceDescription">e.g. "Headphones".</param>
public sealed record AudioEndpointInfo(
    string Id,
    string Guid,
    string FriendlyName,
    string DeviceDescription,
    bool IsDefault);

/// <summary>Per-endpoint Equalizer APO health read from the endpoint's FxProperties.</summary>
public sealed record ApoEndpointStatus(bool IsApoActive, bool EnhancementsDisabled);

public enum ApoLaunchOutcome
{
    Completed,
    RebootRequired,
    Cancelled,
    NotFound,
    Failed
}

public sealed record ApoLaunchResult(ApoLaunchOutcome Outcome, string Message);
