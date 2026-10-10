using System.IO;
using System.Text.RegularExpressions;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Read-only detection of Equalizer APO: whether it is really installed, where its config lives,
/// and which playback endpoints have it attached. Never writes the registry.
/// </summary>
public sealed partial class EqualizerApoInstallation
{
    public const string PreMixClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
    public const string PostMixClsid = "{EC1CC9CE-FAED-4822-828A-82A81A6F018F}";

    internal const string ApoRegistryKey = @"SOFTWARE\EqualizerAPO";
    internal const string RenderEndpointsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render";

    // FxProperties value names: "{d04e05a6-...},N" hold the APO CLSIDs attached to the endpoint,
    // "{1da5d803-...},5" = 1 means "Disable all enhancements" is ticked (APO then never runs).
    private const string FxClsidPropertyKey = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";
    private const string DisableEnhancementsValueName = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    private static readonly string[] ApoClsids = [PreMixClsid, PostMixClsid];

    private readonly IRegistryReader _registry;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _readText;
    private readonly Func<string, bool> _canWriteDirectory;
    private readonly Func<string, bool> _directoryExists;

    public EqualizerApoInstallation(
        IRegistryReader registry,
        Func<string, bool>? fileExists = null,
        Func<string, string?>? readText = null,
        Func<string, bool>? canWriteDirectory = null,
        Func<string, bool>? directoryExists = null)
    {
        _registry = registry;
        _fileExists = fileExists ?? File.Exists;
        _readText = readText ?? ReadTextOrNull;
        _canWriteDirectory = canWriteDirectory ?? CanWriteDirectory;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    /// <summary>APO config folder from the registry (<c>ConfigPath</c>), or null.</summary>
    public string? ConfigDirectory => ReadPath("ConfigPath");

    /// <summary>APO install folder from the registry (<c>InstallPath</c>), or null.</summary>
    public string? InstallDirectory => ReadPath("InstallPath");

    /// <summary><c>DeviceSelector.exe</c> inside the install folder, or null when the install folder is unknown.</summary>
    public string? DeviceSelectorPath
        => InstallDirectory is { } install ? Path.Combine(install, "DeviceSelector.exe") : null;

    /// <summary>
    /// Full detection: registry, CLSIDs, the DLL, config.txt (Peace) and a write probe in the config folder.
    /// This is comparatively expensive and touches APO's watched folder, so callers run it on refresh only,
    /// never per apply.
    /// </summary>
    /// <remarks>
    /// When APO is not really installed but its registry <c>ConfigPath</c> still exists on disk, the
    /// not-installed status keeps <see cref="SystemEqBackendStatus.ConfigDirectory"/> and
    /// <see cref="SystemEqBackendStatus.InstallDirectory"/> so removal can clean WolfEQ's leftovers there.
    /// </remarks>
    public SystemEqBackendStatus GetStatus()
    {
        try
        {
            var install = InstallDirectory;
            var config = ConfigDirectory;
            if (install is null || config is null)
            {
                return NotInstalled("Equalizer APO is not installed.", install, config);
            }

            // The EqualizerAPO key survives uninstall, so require the DLL and a registered APO class.
            var hasDll = SafeFileExists(Path.Combine(install, "EqualizerAPO.dll"));
            var hasClsid = ApoClsids.Any(clsid => _registry.KeyExists(RegistryRoot.ClassesRoot, $@"CLSID\{clsid}"));
            if (!hasDll || !hasClsid)
            {
                return NotInstalled(
                    "Equalizer APO is not installed (only leftover settings from an earlier install were found).",
                    install,
                    config);
            }

            var writable = SafeCanWrite(config);
            var peace = IsPeaceIncludeActive(SafeReadText(Path.Combine(config, "config.txt")));
            var message = writable
                ? "Equalizer APO is installed."
                : "Equalizer APO is installed, but WolfEQ can't write to its config folder.";
            return new SystemEqBackendStatus(true, config, install, writable, peace, message);
        }
        catch
        {
            return SystemEqBackendStatus.NotInstalled("Couldn't check for Equalizer APO.");
        }
    }

    public ApoEndpointStatus GetEndpointStatus(string endpointGuid)
    {
        if (!Guid.TryParseExact(endpointGuid?.Trim(), "B", out _))
        {
            return new ApoEndpointStatus(false, false);
        }

        try
        {
            var fxKey = $@"{RenderEndpointsKey}\{endpointGuid.Trim()}\FxProperties";
            var active = _registry.GetValueNames(RegistryRoot.LocalMachine, fxKey)
                .Where(name => name.StartsWith(FxClsidPropertyKey + ",", StringComparison.OrdinalIgnoreCase))
                .Select(name => _registry.GetValue(RegistryRoot.LocalMachine, fxKey, name)?.Trim())
                .Any(value => value is not null && ApoClsids.Contains(value, StringComparer.OrdinalIgnoreCase));
            var disabled = _registry.GetValue(RegistryRoot.LocalMachine, fxKey, DisableEnhancementsValueName)?.Trim() == "1";
            return new ApoEndpointStatus(active, disabled);
        }
        catch
        {
            return new ApoEndpointStatus(false, false);
        }
    }

    /// <summary>True when config.txt has an uncommented <c>Include: peace.txt</c> line (Peace GUI is driving APO).</summary>
    public static bool IsPeaceIncludeActive(string? configText)
        => configText is not null && PeaceIncludeRegex().IsMatch(configText);

    [GeneratedRegex(@"^[ \t]*Include[ \t]*:[ \t]*(?:[^\r\n#]*[\\/])?peace\.txt[ \t]*\r?$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PeaceIncludeRegex();

    /// <summary>Not installed; carries the leftover folders only when the config folder is really on disk.</summary>
    private SystemEqBackendStatus NotInstalled(string message, string? install, string? config)
        => config is not null && SafeDirectoryExists(config)
            ? new SystemEqBackendStatus(false, config, install, false, false, message)
            : SystemEqBackendStatus.NotInstalled(message);

    private string? ReadPath(string valueName)
    {
        var value = _registry.GetValue(RegistryRoot.LocalMachine, ApoRegistryKey, valueName)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private bool SafeFileExists(string path)
    {
        try { return _fileExists(path); }
        catch { return false; }
    }

    private bool SafeDirectoryExists(string path)
    {
        try { return _directoryExists(path); }
        catch { return false; }
    }

    private string? SafeReadText(string path)
    {
        try { return _readText(path); }
        catch { return null; }
    }

    private bool SafeCanWrite(string directory)
    {
        try { return _canWriteDirectory(directory); }
        catch { return false; }
    }

    private static string? ReadTextOrNull(string path)
        => File.Exists(path) ? File.ReadAllText(path) : null;

    private static bool CanWriteDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        // APO watches this folder, so the probe is a hidden-style .tmp name APO never includes, it is
        // deleted on close, and it only runs from GetStatus (refresh), not on every apply.
        var probe = Path.Combine(directory, $".wolfeq-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(probe)) File.Delete(probe); } catch { }
        }
    }
}
