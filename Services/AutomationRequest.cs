namespace WolfEQ.Services;

public sealed record AutomationRequest(int? Slot = null, string? Device = null, bool ListDevices = false, bool Help = false)
{
    public const string Usage = "WolfEQ.exe --slot <1-10> [--device \"Desk K13\"]\nWolfEQ.exe --list-devices\nWolfEQ.exe --help\nSwitches a stored USER slot; does not write a library preset. Exit codes: 0 success, 1 failure, 2 invalid arguments, 3 unsaved edits/busy, 4 slot switched but lighting failed. Output is also saved to %LOCALAPPDATA%\\WolfEQ\\automation-result.json.";

    public static AutomationRequest Parse(string[] args)
    {
        int? slot = null;
        string? device = null;
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
                case "--list-devices" when !list: list = true; break;
                case "--help" when !help: help = true; break;
                default: throw new ArgumentException($"Unknown, repeated, or incomplete argument: {args[i]}");
            }
        }
        if ((list || help) && (slot is not null || device is not null) || list && help || device is not null && slot is null)
            throw new ArgumentException("Use --slot with an optional --device, or use --help/--list-devices alone.");
        return new(slot, device, list, help);
    }
}

public sealed record AutomationResult(int ExitCode, string Message);
