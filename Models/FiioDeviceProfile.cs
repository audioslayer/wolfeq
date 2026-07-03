using System.Globalization;

namespace WolfEQ.Models;

public sealed record FiioDeviceSlot(byte Id, string Name, bool IsWritable);

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
    bool ReloadEqAfterSave = true,
    byte SaveCommandId = 0x19,
    bool IsVerified = false,
    IReadOnlyList<string>? ProductNameAliases = null)
{
    public string DisplayLabel => IsVerified ? DisplayName : $"{DisplayName} (experimental)";

    public IReadOnlyList<FiioDeviceSlot> WritableSlots
        => Slots.Where(slot => slot.IsWritable).ToArray();

    public string CapabilitySummary
    {
        get
        {
            var filters = SupportedFilterTypes.Count == 7 ? "all filters" : "core filters";
            var verify = IsVerified ? "verified" : "experimental";
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
    private static readonly IReadOnlySet<EqFilterType> AllFilters = Enum.GetValues<EqFilterType>().ToHashSet();

    private static readonly IReadOnlySet<EqFilterType> CoreFilters = new HashSet<EqFilterType>
    {
        EqFilterType.Peak,
        EqFilterType.LowShelf,
        EqFilterType.HighShelf
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
        IsVerified: true);

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
        DisabledPresetId: 0x0A);

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
        DisabledPresetId: 0x04);

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
        ReloadEqAfterSave: false);

    public static readonly FiioDeviceProfile SnowskyRetroNano = new(
        Id: "snowsky-retro-nano",
        DisplayName: "Snowsky Retro Nano",
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

    public static IReadOnlyList<FiioDeviceProfile> All { get; } =
    [
        K13R2R,
        Ka15,
        Ka17,
        Ja11,
        SnowskyRetroNano,
        SnowskyMelody
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
