namespace WolfEQ.Models;

/// <summary>
/// The Software EQ support type. Deliberately kept out of <see cref="FiioDeviceProfiles.All"/>
/// so FiiO hardware detection can never match it.
/// </summary>
public static class SystemEqProfiles
{
    public static readonly FiioDeviceProfile WindowsSystemEq = new(
        Id: "windows-system-eq",
        DisplayName: "Software EQ",
        ProductId: null,
        ReportId: 0x00,
        BandCount: 10,
        MinGainDb: -24.0,
        MaxGainDb: 12.0,
        MinQ: 0.1,
        MaxQ: 10.0,
        SupportedFilterTypes: Enum.GetValues<EqFilterType>().ToHashSet(),
        Slots: [],
        DisabledPresetId: 0xF0,
        SupportsLiveEqWrites: false,
        SupportsEqReadback: false,
        ReloadEqAfterSave: false,
        IsVerified: true);

    public static bool IsSystemEq(FiioDeviceProfile? profile)
        => profile is not null && string.Equals(profile.Id, WindowsSystemEq.Id, StringComparison.Ordinal);
}
