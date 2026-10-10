namespace WolfEQ.Services;

/// <param name="SystemEqBypass">Windows System EQ bypass action: "on", "off" or "toggle".</param>
/// <param name="PresetName">Library preset to load and apply in Windows System EQ mode.</param>
public sealed record AutomationRequest(
    int? Slot = null,
    string? Device = null,
    bool ListDevices = false,
    bool Help = false,
    string? SystemEqBypass = null,
    string? PresetName = null)
{
    public const string Usage = "WolfEQ.exe --slot <1-10> [--device \"Desk K13\"]\nWolfEQ.exe --system-eq-bypass <on|off|toggle>\nWolfEQ.exe --preset \"<library preset name>\"\nWolfEQ.exe --list-devices\nWolfEQ.exe --help\n--slot switches a stored USER slot on a FiiO device; it does not write a library preset. --system-eq-bypass and --preset work in Windows System EQ mode. Exit codes: 0 success, 1 failure, 2 invalid arguments, 3 unsaved edits/busy, 4 slot switched but lighting failed. Output is also saved to %LOCALAPPDATA%\\WolfEQ\\automation-result.json.";

    public static AutomationRequest Parse(string[] args)
    {
        int? slot = null;
        string? device = null, bypass = null, preset = null;
        bool list = false, help = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--slot" when slot is null && i + 1 < args.Length:
                    if (!int.TryParse(args[++i], out int value) || value is < 1 or > 10)
                        throw new ArgumentException("Slot must be between 1 and 10.");
                    slot = value;
                    break;
                case "--device" when device is null && i + 1 < args.Length:
                    device = args[++i];
                    if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("Device must have a name or USB path.");
                    break;
                case "--system-eq-bypass" when bypass is null && i + 1 < args.Length:
                    bypass = ParseBypass(args[++i]);
                    break;
                // Plan spelling: --system-eq bypass <on|off|toggle>.
                case "--system-eq" when bypass is null && i + 2 < args.Length && args[i + 1] == "bypass":
                    i++;
                    bypass = ParseBypass(args[++i]);
                    break;
                case "--preset" when preset is null && i + 1 < args.Length:
                    preset = args[++i];
                    if (string.IsNullOrWhiteSpace(preset)) throw new ArgumentException("Preset must have a name.");
                    break;
                case "--list-devices" when !list: list = true; break;
                case "--help" when !help: help = true; break;
                default: throw new ArgumentException($"Unknown, repeated, or incomplete argument: {args[i]}");
            }
        }
        var actions = (slot is not null ? 1 : 0) + (list ? 1 : 0) + (help ? 1 : 0) + (bypass is not null ? 1 : 0) + (preset is not null ? 1 : 0);
        if (actions > 1 || device is not null && slot is null)
            throw new ArgumentException("Use one action: --slot with an optional --device, --system-eq-bypass, --preset, or --help/--list-devices alone.");
        return new(slot, device, list, help, bypass, preset);
    }

    private static string ParseBypass(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "on" or "off" or "toggle"
            ? normalized
            : throw new ArgumentException("System EQ bypass must be on, off, or toggle.");
    }
}

public sealed record AutomationResult(int ExitCode, string Message);
