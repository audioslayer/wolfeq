using System.Globalization;

namespace WolfEQ.Models;

public sealed record FiioDeviceSlot(byte Id, string Name, bool IsWritable);

/// <summary>Per-device WalkPlay details, measured by Contour. <see cref="FiioDeviceProfile.WalkPlay"/> null means the FiiO protocol.</summary>
public sealed record WalkPlayOptions(
    byte BandLength = 0x18,
    bool TrnSequence = false,
    double ShelfQScale = 1.0,
    bool WrapCoefficients = false,
    double CoefficientRateHz = 96000.0,
    // No high shelf in the DSP: sent as the opposite low shelf, with the gain moved into the preamp.
    bool EmulateHighShelf = false,
    // The slot byte has no known meaning; it is sent back as read.
    bool EchoSlot = false);

/// <summary>A KT Micro device. Its chip's USB IDs are shared, so the exact USB product name is required too.</summary>
public sealed record KtMicroOptions(ushort VendorId, ushort ProductId, string ProductName);

public sealed record FiioDeviceProfile(
    string Id,
    string DisplayName,
    ushort? ProductId,
    byte ReportId,
    int BandCount,
    double MinGainDb,
    double MaxGainDb,
    double MinQ,
    double MaxQ,
    IReadOnlySet<EqFilterType> SupportedFilterTypes,
    IReadOnlyList<FiioDeviceSlot> Slots,
    byte DisabledPresetId,
    int? HidInterfaceNumber = null,
    bool HasBleInput = false,
    bool HasBleLighting = false,
    bool HasBleDeviceControls = false,
    bool SupportsUsbPresetNames = false,
    bool SupportsLiveEqWrites = true,
    bool SupportsEqReadback = true,
    bool ReloadEqAfterSave = true,
    byte SaveCommandId = 0x19,
    // Listed devices are labelled working. Safety is separate: SupportsLiveEqWrites and
    // SupportsEqReadback keep unconfirmed devices in guarded save-only mode.
    bool IsVerified = true,
    IReadOnlyList<string>? ProductNameAliases = null,
    string? ImageId = null,
    // Some FiiO models play USER presets 12 dB below EQ off: register = wanted preamp + offset.
    double PreampRegisterOffsetDb = 0,
    bool PreampWholeDb = false,
    double? PreampFloorDb = null,
    double? PreampCeilingDb = null,
    bool SupportsEqSwitchRead = true,
    int SaveSettleMs = 350,
    WalkPlayOptions? WalkPlay = null,
    // K17 family: the EQ is reached through the rear RS232 USB port, not HID.
    bool UsesDspSerial = false,
    // Measured by Contour: these filters play Q = register / scale, so the register holds the wanted Q x scale.
    double PeakQScale = 1.0,
    double ShelfQScale = 1.0,
    KtMicroOptions? KtMicro = null,
    // Wait after switching slot, before the first band is written.
    int SelectSettleMs = 0)
{
    public bool HasNoPreamp => UsesDspSerial || KtMicro is not null;

    public double QScale(EqFilterType type) => type switch
    {
        EqFilterType.Peak => PeakQScale,
        EqFilterType.LowShelf or EqFilterType.HighShelf => ShelfQScale,
        _ => 1.0
    };

    /// <summary>The highest real Q the register can hold for this filter (the register itself stops at <see cref="MaxQ"/>).</summary>
    public double MaxQFor(EqFilterType type) => Math.Floor(MaxQ / QScale(type) * 100 + 1e-6) / 100;

    public ushort EncodeQRegister(EqFilterType type, double q) => (ushort)Math.Round(q * QScale(type) * 100);

    public double DecodeQRegister(EqFilterType type, double register) => Math.Round(register / QScale(type), 2);

    /// <summary>Some devices read a Q back slightly off (990 as 989, 605 as 607), so a match allows 1 + 0.4% register steps.</summary>
    public bool QMatches(EqFilterType type, double requested, double actual)
    {
        var register = EncodeQRegister(type, requested);
        return Math.Abs(register - EncodeQRegister(type, actual)) <= 1 + register / 250;
    }

    public double MinPreampDb => PreampFloorDb ?? MinGainDb;

    public double MaxPreampDb => PreampCeilingDb ?? MaxGainDb;

    /// <summary>The nearest preamp the device can hold. Whole-dB devices round down, so the result is never louder than asked.</summary>
    public double NormalizePreamp(double preampDb)
    {
        var value = double.IsFinite(preampDb) ? preampDb : 0;
        value = PreampWholeDb ? Math.Floor(value + 1e-6) : Math.Round(value, 1);
        return Math.Clamp(value, MinPreampDb, MaxPreampDb);
    }

    public short EncodePreampTenths(double preampDb)
        => (short)Math.Round((preampDb + PreampRegisterOffsetDb) * 10.0);

    public double DecodePreampRegister(double registerDb) => Math.Round(registerDb - PreampRegisterOffsetDb, 1);

    public string DisplayLabel => IsVerified ? DisplayName : $"{DisplayName} (experimental)";

    /// <summary>Bundled product picture; profiles without a photo use <see cref="ImageId"/> (e.g. the generic device drawing).</summary>
    public string ProductImagePath => $"pack://application:,,,/WolfEQ;component/Assets/devices/{ImageId ?? Id}.png";

    public IReadOnlyList<FiioDeviceSlot> WritableSlots
        => Slots.Where(slot => slot.IsWritable).ToArray();

    public string CapabilitySummary
    {
        get
        {
            var filters = SupportedFilterTypes.Count switch
            {
                7 => "all filters",
                3 => "core filters",
                _ => $"{SupportedFilterTypes.Count} filter types"
            };
            var verify = IsVerified ? "working" : "experimental";
            return $"{BandCount} bands, {MinGainDb:+0;-0;0} to {MaxGainDb:+0;-0;0} dB, {filters}, report {ReportId}, {verify}";
        }
    }

    public bool SupportsFilter(EqFilterType filterType)
        => SupportedFilterTypes.Contains(filterType);

    public override string ToString() => DisplayLabel;

    public string GetPresetDisplayName(byte presetId, string? presetName = null)
    {
        if (!string.IsNullOrWhiteSpace(presetName))
        {
            return presetName;
        }

        var slot = Slots.FirstOrDefault(candidate => candidate.Id == presetId);
        return slot is not null
            ? slot.Name
            : "Unknown preset";
    }
}

public static class FiioDeviceProfiles
{
    private const double Sqrt2 = 1.4142135623730951;

    private static readonly IReadOnlySet<EqFilterType> AllFilters = Enum.GetValues<EqFilterType>().ToHashSet();

    private static readonly IReadOnlySet<EqFilterType> CoreFilters = new HashSet<EqFilterType>
    {
        EqFilterType.Peak,
        EqFilterType.LowShelf,
        EqFilterType.HighShelf
    };

    private static readonly IReadOnlySet<EqFilterType> WalkPlayFilters = new HashSet<EqFilterType>
    {
        EqFilterType.Peak,
        EqFilterType.LowShelf,
        EqFilterType.HighShelf,
        EqFilterType.LowPass,
        EqFilterType.HighPass
    };

    private static readonly IReadOnlySet<EqFilterType> AllFiltersExceptBandPass = new HashSet<EqFilterType>
    {
        EqFilterType.Peak,
        EqFilterType.LowShelf,
        EqFilterType.HighShelf,
        EqFilterType.LowPass,
        EqFilterType.HighPass,
        EqFilterType.AllPass
    };

    public static readonly FiioDeviceProfile K13R2R = new(
        Id: "fiio-k13-r2r",
        DisplayName: "FiiO K13 R2R",
        ProductId: 0x0120,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFilters,
        Slots:
        [
            new(0xF0, "BYPASS", false),
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0x08, "Retro", false),
            new(0x09, "sDamp-1", false),
            new(0x0A, "sDamp-2", false),
            new(0xA0, "USER 1", true),
            new(0xA1, "USER 2", true),
            new(0xA2, "USER 3", true),
            new(0xA3, "USER 4", true),
            new(0xA4, "USER 5", true),
            new(0xA5, "USER 6", true),
            new(0xA6, "USER 7", true),
            new(0xA7, "USER 8", true),
            new(0xA8, "USER 9", true),
            new(0xA9, "USER 10", true)
        ],
        DisabledPresetId: 0xF0,
        HidInterfaceNumber: 3,
        HasBleInput: true,
        HasBleLighting: true,
        HasBleDeviceControls: true,
        SupportsUsbPresetNames: true,
        IsVerified: true,
        PeakQScale: Sqrt2);

    // Community configurations below are adapted from Pragmatic Audio's 0BSD-licensed
    // DevicePEQ project and cross-checked against FiiO's published PEQ documentation.
    // WolfEQ keeps automatic readback and live writes disabled until each Windows HID
    // endpoint is physically verified; guarded USER-slot saves remain available.
    public static readonly FiioDeviceProfile Br15R2R = new(
        Id: "fiio-br15-r2r",
        DisplayName: "FiiO BR15 R2R",
        ProductId: null,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFiltersExceptBandPass,
        Slots:
        [
            new(0xF0, "BYPASS", false),
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0x08, "Retro", false),
            new(0x09, "sDamp-1", false),
            new(0x0A, "sDamp-2", false),
            new(0xA0, "USER 1", true),
            new(0xA1, "USER 2", true),
            new(0xA2, "USER 3", true),
            new(0xA3, "USER 4", true),
            new(0xA4, "USER 5", true),
            new(0xA5, "USER 6", true),
            new(0xA6, "USER 7", true),
            new(0xA7, "USER 8", true),
            new(0xA8, "USER 9", true),
            new(0xA9, "USER 10", true)
        ],
        DisabledPresetId: 0xF0,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["FIIO BR15 R2R", "BR15 R2R"]);

    public static readonly FiioDeviceProfile Qx13 = new(
        Id: "fiio-qx13",
        DisplayName: "FiiO QX13",
        ProductId: 0x0128,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFilters,
        Slots:
        [
            new(0xF0, "BYPASS", false),
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0x08, "Retro", false),
            new(0xA0, "USER 1", true),
            new(0xA1, "USER 2", true),
            new(0xA2, "USER 3", true),
            new(0xA3, "USER 4", true),
            new(0xA4, "USER 5", true),
            new(0xA5, "USER 6", true),
            new(0xA6, "USER 7", true),
            new(0xA7, "USER 8", true),
            new(0xA8, "USER 9", true),
            new(0xA9, "USER 10", true)
        ],
        DisabledPresetId: 0xF0,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["FIIO QX13", "QX13"]);

    public static readonly FiioDeviceProfile Btr17 = new(
        Id: "fiio-btr17",
        DisplayName: "FiiO BTR17",
        ProductId: 0x0088,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: CoreFilters,
        Slots:
        [
            new(0xF0, "BYPASS", false),
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0xA0, "USER 1", true),
            new(0xA1, "USER 2", true),
            new(0xA2, "USER 3", true),
            new(0xA3, "USER 4", true),
            new(0xA4, "USER 5", true),
            new(0xA5, "USER 6", true),
            new(0xA6, "USER 7", true),
            new(0xA7, "USER 8", true),
            new(0xA8, "USER 9", true),
            new(0xA9, "USER 10", true)
        ],
        DisabledPresetId: 0xF0,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["FIIO BTR17", "BTR17"]);

    // BTR25: layout and commands from FiiO's web app, where it shares the BTR19's handling. Nobody has tested it
    // yet, so it is save-only and matched by name (its USB product ID is not published).
    public static readonly FiioDeviceProfile Btr25 = Btr17 with
    {
        Id = "fiio-btr25",
        DisplayName = "FiiO BTR25",
        ProductId = null,
        SupportedFilterTypes = AllFilters,
        ProductNameAliases = ["FIIO BTR25", "BTR25"],
        IsVerified = false,
        SelectSettleMs = 1000
    };

    public static readonly FiioDeviceProfile Btr13 = new(
        Id: "fiio-btr13",
        DisplayName: "FiiO BTR13",
        ProductId: null,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFilters,
        Slots:
        [
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0x07, "USER 1", true),
            new(0x08, "USER 2", true),
            new(0x09, "USER 3", true),
            new(0x0B, "Close EQ", false)
        ],
        DisabledPresetId: 0x0B,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["FIIO BTR13", "BTR13"]);

    public static readonly FiioDeviceProfile Ka15 = new(
        Id: "fiio-ka15",
        DisplayName: "FiiO KA15",
        ProductId: 0x0104,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFilters,
        Slots:
        [
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0x07, "USER 1", true),
            new(0x08, "USER 2", true),
            new(0x09, "USER 3", true),
            new(0x0A, "Close EQ", false)
        ],
        DisabledPresetId: 0x0A,
        PreampRegisterOffsetDb: 12.0,
        PreampWholeDb: true,
        PreampFloorDb: -24.0,
        PreampCeilingDb: 0.0,
        SupportsEqSwitchRead: false,
        SaveSettleMs: 2000,
        ShelfQScale: Sqrt2);

    public static readonly FiioDeviceProfile Ka17 = new(
        Id: "fiio-ka17",
        DisplayName: "FiiO KA17",
        ProductId: 0x0093,
        ReportId: 0x01,
        BandCount: 10,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFilters,
        Slots:
        [
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x05, "R&B", false),
            new(0x06, "Classic", false),
            new(0x07, "HipHop", false),
            new(0x04, "USER 1", true),
            new(0x08, "USER 2", true),
            new(0x09, "USER 3", true),
            new(0x0A, "BYPASS", false)
        ],
        DisabledPresetId: 0x0A);

    public static readonly FiioDeviceProfile Ja11 = new(
        Id: "fiio-ja11",
        DisplayName: "FiiO JA11",
        ProductId: 0x0102,
        ReportId: 0x02,
        BandCount: 5,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: CoreFilters,
        Slots:
        [
            new(0x00, "Vocal", false),
            new(0x01, "Classic", false),
            new(0x02, "Bass", false),
            new(0x03, "USER 1", true),
            new(0x04, "Off", false)
        ],
        DisabledPresetId: 0x04,
        // The unit drops off USB for about 2 s after a save.
        ReloadEqAfterSave: false,
        PreampRegisterOffsetDb: 12.0,
        PreampFloorDb: -24.0,
        PreampCeilingDb: 0.0,
        SupportsEqSwitchRead: false);

    public static readonly FiioDeviceProfile SnowskyMelody = new(
        Id: "snowsky-melody",
        DisplayName: "Snowsky Melody",
        ProductId: 0x0126,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFilters,
        Slots:
        [
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0xA0, "USER 1", true),
            new(0xA1, "USER 2", true),
            new(0xA2, "USER 3", true),
            new(0xF0, "Close EQ", false)
        ],
        DisabledPresetId: 0xF0,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        PreampRegisterOffsetDb: 12.0,
        PreampWholeDb: true,
        PreampFloorDb: -24.0,
        PreampCeilingDb: 0.0,
        ShelfQScale: Sqrt2);

    public static readonly FiioDeviceProfile SnowskyRetroNano = new(
        Id: "snowsky-retro-nano",
        DisplayName: "Snowsky Retro Nano",
        ProductId: null,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        // Measured by Contour: Q outside 0.25-8 is refused and band pass has no effect.
        MinQ: 0.25,
        MaxQ: 8.0,
        SupportedFilterTypes: AllFiltersExceptBandPass,
        Slots:
        [
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0x08, "Retro", false),
            new(0x09, "sDamp-1", false),
            new(0x0A, "sDamp-2", false),
            new(0xA0, "USER 1", true),
            new(0xA1, "USER 2", true),
            new(0xA2, "USER 3", true),
            new(0x0B, "Close EQ", false)
        ],
        DisabledPresetId: 0x0B,
        HidInterfaceNumber: 3,
        HasBleDeviceControls: true,
        SupportsUsbPresetNames: true,
        SupportsLiveEqWrites: false,
        ReloadEqAfterSave: false,
        ProductNameAliases:
        [
            "RETRO NANO",
            "SNOWSKY RETRO NANO",
            "FiiO SNOWSKY RETRO NANO"
        ]);

    // ── Added from community protocol data (devicePEQ 0BSD, fiioctl MIT). Guarded save-only:
    //    no automatic readback or live writes until confirmed on real hardware. ──

    // K17 / K19: 31-band PEQ over the RS232 USB port, as FiiO's web control does it. USER 1-10 are presets 7-16,
    // EQ off is 17. There is no preamp. Q 0.4-128 in the device's own steps; 10 is the editor's limit.
    private static IReadOnlyList<FiioDeviceSlot> K17FamilySlots() =>
    [
        new(0x00, "Jazz", false),
        new(0x01, "Rock", false),
        new(0x02, "R&B", false),
        new(0x03, "Hip-hop", false),
        new(0x04, "Pop", false),
        new(0x05, "Dance", false),
        new(0x06, "Classic", false),
        .. Enumerable.Range(0, 10).Select(i => new FiioDeviceSlot((byte)(0x07 + i), $"USER {i + 1}", true)),
        new(0x11, "EQ off", false)
    ];

    public static readonly FiioDeviceProfile K17 = new(
        Id: "fiio-k17",
        DisplayName: "FiiO K17",
        ProductId: null,
        ReportId: 0x07,
        BandCount: 31,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.4,
        MaxQ: 10.0,
        SupportedFilterTypes: CoreFilters,
        Slots: K17FamilySlots(),
        DisabledPresetId: 0x11,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["FIIO K17", "K17"],
        UsesDspSerial: true);

    public static readonly FiioDeviceProfile K19 = K17 with
    {
        Id = "fiio-k19",
        DisplayName = "FiiO K19",
        ProductNameAliases = ["FIIO K19", "K19"]
    };

    // K17 R2R (USB 0x2972:0x0140). Its HID interface only echoes what it is sent; the EQ is on the RS232 USB port.
    public static readonly FiioDeviceProfile K17R2R = K17 with
    {
        Id = "fiio-k17-r2r",
        DisplayName = "FiiO K17 R2R",
        ProductId = 0x0140,
        HidInterfaceNumber = 3,
        ProductNameAliases = ["FIIO K17 R2R", "K17 R2R", "K17R2R"],
        IsVerified = false
    };

    // FP3: 10 bands, one USER slot (0xA0). No EQ-off preset is known; DisabledPresetId is only a placeholder.
    public static readonly FiioDeviceProfile Fp3 = new(
        Id: "fiio-fp3",
        DisplayName: "FiiO FP3",
        ProductId: null,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: CoreFilters,
        Slots:
        [
            .. Enumerable.Range(0, 7).Select(i => new FiioDeviceSlot((byte)i, $"Preset {i + 1}", false)),
            new(0xA0, "USER 1", true)
        ],
        DisabledPresetId: 0xA0,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["FIIO FP3", "FP3"]);

    // Oak Nano: USB PID 0x4016 from a hardware capture, which also shows 8 bands (not the 10 in config).
    public static readonly FiioDeviceProfile OakNano = new(
        Id: "fiio-oak-nano",
        DisplayName: "FiiO Oak Nano",
        ProductId: 0x4016,
        ReportId: 0x07,
        BandCount: 8,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: CoreFilters,
        Slots:
        [
            .. Enumerable.Range(0, 7).Select(i => new FiioDeviceSlot((byte)i, $"Preset {i + 1}", false)),
            new(0xA0, "USER 1", true)
        ],
        DisabledPresetId: 0xA0,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["FIIO OAK NANO", "OAK NANO", "OAKNANO"]);

    // JIEZI and Tiny A: limits and USB IDs measured on hardware by the Contour project (Apache-2.0).
    // WolfEQ has not tested them itself, so they stay in guarded save-only mode.
    public static readonly FiioDeviceProfile Jiezi = new(
        Id: "fiio-jiezi",
        DisplayName: "JadeAudio JIEZI",
        ProductId: 0x0151,
        ReportId: 0x07,
        BandCount: 10,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: AllFilters,
        Slots:
        [
            new(0x00, "Jazz", false),
            new(0x01, "Pop", false),
            new(0x02, "Rock", false),
            new(0x03, "Dance", false),
            new(0x04, "R&B", false),
            new(0x05, "Classic", false),
            new(0x06, "HipHop", false),
            new(0xA0, "USER 1", true),
            new(0xA1, "USER 2", true),
            new(0xA2, "USER 3", true),
            new(0xF0, "Close EQ", false)
        ],
        DisabledPresetId: 0xF0,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        ProductNameAliases: ["JadeAudio JIEZI", "JIEZI"],
        ShelfQScale: Sqrt2);

    public static readonly FiioDeviceProfile SnowskyTinyA = Jiezi with
    {
        Id = "snowsky-tiny-a",
        DisplayName = "Snowsky Tiny A",
        ProductId = 0x0147,
        ProductNameAliases = ["SNOWSKY TINY A", "TINY A"]
    };

    // WalkPlay DACs (USB vendor 0x3302). Identity, limits and sequences measured by Contour (Apache-2.0).
    // Not tested by WolfEQ: no live sync. Every save is read back and checked.
    public static readonly FiioDeviceProfile TrnBlackPearl = new(
        Id: "trn-black-pearl",
        DisplayName: "TRN Black Pearl",
        ProductId: 0x43E8,
        ReportId: 0x4B,
        BandCount: 10,
        MinGainDb: -10.0,
        MaxGainDb: 10.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: CoreFilters,
        Slots: [new(0x00, "Custom", true)],
        DisabledPresetId: 0xF0,
        SupportsLiveEqWrites: false,
        ReloadEqAfterSave: false,
        PreampWholeDb: true,
        PreampFloorDb: -30.0,
        PreampCeilingDb: 0.0,
        WalkPlay: new(BandLength: 0x21, TrnSequence: true, ShelfQScale: 1.4142135623730951));

    public static readonly FiioDeviceProfile CrinEarProtocolMax = TrnBlackPearl with
    {
        Id = "crinear-protocol-max",
        DisplayName = "CrinEar Protocol Max",
        ProductId = 0x43CC,
        MinGainDb = -12.0,
        MaxGainDb = 12.0,
        SupportedFilterTypes = WalkPlayFilters,
        WalkPlay = new(ShelfQScale: 1.4142135623730951, WrapCoefficients: true)
    };

    public static readonly FiioDeviceProfile TanchjimSpace2 = TrnBlackPearl with
    {
        Id = "tanchjim-space-ii",
        DisplayName = "Tanchjim SPACE II",
        ProductId = 0x4381,
        SupportedFilterTypes = WalkPlayFilters,
        WalkPlay = new()
    };

    // Protocol Micro and Echo-A: measured by Contour, saving over a replug is not. The DSP plays the coefficients at 93,750 Hz.
    public static readonly FiioDeviceProfile CrinEarProtocolMicro = TrnBlackPearl with
    {
        Id = "crinear-protocol-micro",
        DisplayName = "CrinEar Protocol Micro",
        ProductId = 0xC20F,
        BandCount = 8,
        SupportedFilterTypes = WalkPlayFilters,
        ImageId = null,
        IsVerified = false,
        WalkPlay = new(CoefficientRateHz: 93750.0, EmulateHighShelf: true, EchoSlot: true)
    };

    public static readonly FiioDeviceProfile MoondropEchoA = CrinEarProtocolMicro with
    {
        Id = "moondrop-echo-a",
        DisplayName = "Moondrop Echo-A",
        ProductId = 0x12C5
    };

    // KT Micro devices: registers and limits measured by Contour. Each save restarts the device.
    public static readonly FiioDeviceProfile JcallyJm12 = new(
        Id: "jcally-jm12",
        DisplayName: "JCALLY JM12",
        ProductId: null,
        ReportId: 0x4B,
        BandCount: 5,
        MinGainDb: -12.0,
        MaxGainDb: 12.0,
        MinQ: 0.2,
        MaxQ: 10.0,
        SupportedFilterTypes: CoreFilters,
        Slots: [new(0x00, "Custom", true)],
        DisabledPresetId: 0xF0,
        HidInterfaceNumber: 3,
        SupportsLiveEqWrites: false,
        ReloadEqAfterSave: false,
        PreampFloorDb: -24.0,
        PreampCeilingDb: 0.0,
        IsVerified: false,
        KtMicro: new(0x31B2, 0x0111, "KT02H20 HIFI Audio"));

    public static readonly FiioDeviceProfile KiwiEarsAd1 = JcallyJm12 with
    {
        Id = "kiwi-ears-ad1",
        DisplayName = "Kiwi Ears AD1",
        KtMicro = new(0x3302, 0x20E3, "AD1")
    };

    public static IReadOnlyList<FiioDeviceProfile> All { get; } =
    [
        K13R2R,
        K17R2R,
        K17,
        K19,
        Br15R2R,
        Qx13,
        Btr17,
        Btr25,
        Btr13,
        Ka15,
        Ka17,
        Ja11,
        Fp3,
        Jiezi,
        OakNano,
        SnowskyRetroNano,
        SnowskyMelody,
        SnowskyTinyA,
        TrnBlackPearl,
        CrinEarProtocolMax,
        TanchjimSpace2,
        CrinEarProtocolMicro,
        MoondropEchoA,
        JcallyJm12,
        KiwiEarsAd1
    ];

    public static FiioDeviceProfile Default => K13R2R;

    public static FiioDeviceProfile? Match(ushort? productId, string? productName, int? interfaceNumber)
    {
        var productMatch = All.FirstOrDefault(profile =>
            profile.ProductId is ushort expectedProductId && productId == expectedProductId);
        if (productMatch is not null)
        {
            return productMatch;
        }

        if (!string.IsNullOrWhiteSpace(productName))
        {
            var nameMatch = All.FirstOrDefault(profile => ProductNameMatches(profile, productName));
            if (nameMatch is not null)
            {
                return nameMatch;
            }
        }

        return null;
    }

    public static bool ProductNameMatches(FiioDeviceProfile profile, string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return false;
        }

        if (profile.KtMicro is { } ktMicro)
        {
            return string.Equals(productName.Trim(), ktMicro.ProductName, StringComparison.OrdinalIgnoreCase);
        }

        var normalized = NormalizeProductName(productName);
        var displayName = NormalizeProductName(profile.DisplayName)
            .Replace("FiiO", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalized.Contains(displayName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return profile.ProductNameAliases?.Any(alias =>
            normalized.Contains(NormalizeProductName(alias), StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string NormalizeProductName(string value)
        => value.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);

    public static string FormatProductId(ushort? productId)
        => productId is ushort value ? $"0x{value.ToString("X4", CultureInfo.InvariantCulture)}" : "unknown";
}
