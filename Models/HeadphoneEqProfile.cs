using System.Collections.ObjectModel;

namespace WolfEQ.Models;

public sealed class HeadphoneEqProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Untitled profile";
    public string Brand { get; init; } = "";
    public string Model { get; init; } = "";
    public string SourceName { get; init; } = "Manual";
    public string SourceUrl { get; init; } = "";
    public string SourceLicense { get; init; } = "";
    public string Measurement { get; init; } = "";
    public string Target { get; init; } = "";
    public string Notes { get; init; } = "";
    public double PreampDb { get; init; }
    public ObservableCollection<EqBand> Filters { get; init; } = [];

    public EqPreset ToPreset() => new()
    {
        Name = Name,
        PreampDb = PreampDb,
        Bands = new ObservableCollection<EqBand>(Filters.Select(CloneBand))
    };

    public static HeadphoneEqProfile FromPreset(EqPreset preset, string sourceName = "Manual") => new()
    {
        Name = preset.Name,
        SourceName = sourceName,
        PreampDb = preset.PreampDb,
        Filters = new ObservableCollection<EqBand>(preset.Bands.Select(CloneBand))
    };

    private static EqBand CloneBand(EqBand band) => new()
    {
        Number = band.Number,
        Enabled = band.Enabled,
        FilterType = band.FilterType,
        FrequencyHz = band.FrequencyHz,
        GainDb = band.GainDb,
        Q = band.Q
    };
}
