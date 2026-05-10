using System.Collections.ObjectModel;

namespace WolfEQ.Models;

public sealed class EqPreset
{
    public string Name { get; init; } = "Untitled";
    public string Category { get; init; } = "Manual";
    public string SourceName { get; init; } = "Manual";
    public string Description { get; init; } = "Offline EQ profile";
    public bool IsFavorite { get; set; }
    public double PreampDb { get; set; }
    public ObservableCollection<EqBand> Bands { get; init; } = [];

    public static EqPreset AnandaMusic() => new()
    {
        Name = "Ananda Music - Warm Detail",
        Category = "Listening",
        SourceName = "Manual",
        Description = "Warm Ananda Stealth starting point for music. Offline only until K13 PEQ writes are mapped.",
        IsFavorite = true,
        PreampDb = -5.0,
        Bands = new ObservableCollection<EqBand>
        {
            new() { Number = 1, FilterType = EqFilterType.LowShelf, FrequencyHz = 80, GainDb = 3.0, Q = 0.70 },
            new() { Number = 2, FilterType = EqFilterType.Peak, FrequencyHz = 150, GainDb = 1.5, Q = 1.00 },
            new() { Number = 3, FilterType = EqFilterType.Peak, FrequencyHz = 2000, GainDb = -1.5, Q = 1.20 },
            new() { Number = 4, FilterType = EqFilterType.Peak, FrequencyHz = 5500, GainDb = -2.0, Q = 2.00 },
            new() { Number = 5, FilterType = EqFilterType.Peak, FrequencyHz = 8000, GainDb = -1.5, Q = 2.00 },
            new() { Number = 6, FilterType = EqFilterType.HighShelf, FrequencyHz = 10000, GainDb = -1.0, Q = 0.70 },
            new() { Number = 7, FilterType = EqFilterType.Peak, FrequencyHz = 1000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 8, FilterType = EqFilterType.Peak, FrequencyHz = 3000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 9, FilterType = EqFilterType.Peak, FrequencyHz = 6000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 10, FilterType = EqFilterType.Peak, FrequencyHz = 12000, GainDb = 0.0, Q = 1.00, Enabled = false },
        }
    };

    public static EqPreset AnandaGamingAtmos() => new()
    {
        Name = "Ananda Gaming - Atmos",
        Category = "Gaming",
        SourceName = "Manual",
        Description = "Atmos/gaming profile with softened treble glare and guarded headroom.",
        PreampDb = -5.5,
        Bands = new ObservableCollection<EqBand>
        {
            new() { Number = 1, FilterType = EqFilterType.LowShelf, FrequencyHz = 75, GainDb = 2.5, Q = 0.70 },
            new() { Number = 2, FilterType = EqFilterType.Peak, FrequencyHz = 180, GainDb = 1.0, Q = 1.00 },
            new() { Number = 3, FilterType = EqFilterType.Peak, FrequencyHz = 2500, GainDb = -1.0, Q = 1.20 },
            new() { Number = 4, FilterType = EqFilterType.Peak, FrequencyHz = 5500, GainDb = -3.0, Q = 2.00 },
            new() { Number = 5, FilterType = EqFilterType.Peak, FrequencyHz = 8000, GainDb = -2.5, Q = 2.00 },
            new() { Number = 6, FilterType = EqFilterType.HighShelf, FrequencyHz = 10000, GainDb = -1.0, Q = 0.70 },
            new() { Number = 7, FilterType = EqFilterType.Peak, FrequencyHz = 1000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 8, FilterType = EqFilterType.Peak, FrequencyHz = 3000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 9, FilterType = EqFilterType.Peak, FrequencyHz = 6000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 10, FilterType = EqFilterType.Peak, FrequencyHz = 12000, GainDb = 0.0, Q = 1.00, Enabled = false },
        }
    };

    public static EqPreset AnandaHarmanStarter() => new()
    {
        Name = "Ananda Harman Starter",
        Category = "Reference",
        SourceName = "AutoEQ-style",
        Description = "Offline Harman-style starter for Ananda-class planars. Manually curated placeholder until exact AutoEQ/measurement import is wired.",
        PreampDb = -6.0,
        Bands = new ObservableCollection<EqBand>
        {
            new() { Number = 1, FilterType = EqFilterType.LowShelf, FrequencyHz = 105, GainDb = 4.5, Q = 0.70 },
            new() { Number = 2, FilterType = EqFilterType.Peak, FrequencyHz = 520, GainDb = -1.2, Q = 1.10 },
            new() { Number = 3, FilterType = EqFilterType.Peak, FrequencyHz = 1800, GainDb = 1.6, Q = 1.30 },
            new() { Number = 4, FilterType = EqFilterType.Peak, FrequencyHz = 3000, GainDb = 2.4, Q = 1.60 },
            new() { Number = 5, FilterType = EqFilterType.Peak, FrequencyHz = 5800, GainDb = -3.2, Q = 2.40 },
            new() { Number = 6, FilterType = EqFilterType.Peak, FrequencyHz = 7900, GainDb = -2.0, Q = 2.00 },
            new() { Number = 7, FilterType = EqFilterType.HighShelf, FrequencyHz = 10000, GainDb = -1.0, Q = 0.70 },
            new() { Number = 8, FilterType = EqFilterType.Peak, FrequencyHz = 1000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 9, FilterType = EqFilterType.Peak, FrequencyHz = 4000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 10, FilterType = EqFilterType.Peak, FrequencyHz = 12000, GainDb = 0.0, Q = 1.00, Enabled = false },
        }
    };

    public static EqPreset AnandaOratoryStarter() => new()
    {
        Name = "Ananda Oratory-Style Starter",
        Category = "Reference",
        SourceName = "Oratory1990-style",
        Description = "Offline Oratory1990-inspired starter profile for Ananda-class tuning. Treat as a staging preset, not a copied measurement table.",
        PreampDb = -5.5,
        Bands = new ObservableCollection<EqBand>
        {
            new() { Number = 1, FilterType = EqFilterType.LowShelf, FrequencyHz = 95, GainDb = 3.8, Q = 0.70 },
            new() { Number = 2, FilterType = EqFilterType.Peak, FrequencyHz = 220, GainDb = -1.0, Q = 0.90 },
            new() { Number = 3, FilterType = EqFilterType.Peak, FrequencyHz = 950, GainDb = -1.4, Q = 1.20 },
            new() { Number = 4, FilterType = EqFilterType.Peak, FrequencyHz = 2100, GainDb = 1.7, Q = 1.40 },
            new() { Number = 5, FilterType = EqFilterType.Peak, FrequencyHz = 5200, GainDb = -2.8, Q = 2.20 },
            new() { Number = 6, FilterType = EqFilterType.Peak, FrequencyHz = 7400, GainDb = -2.2, Q = 2.60 },
            new() { Number = 7, FilterType = EqFilterType.HighShelf, FrequencyHz = 11000, GainDb = -1.2, Q = 0.70 },
            new() { Number = 8, FilterType = EqFilterType.Peak, FrequencyHz = 1000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 9, FilterType = EqFilterType.Peak, FrequencyHz = 3500, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 10, FilterType = EqFilterType.Peak, FrequencyHz = 14000, GainDb = 0.0, Q = 1.00, Enabled = false },
        }
    };

    public static EqPreset AnandaBassFun() => new()
    {
        Name = "Ananda Bass Fun - Safe",
        Category = "Listening",
        SourceName = "Manual",
        Description = "Taste profile for low-volume fun listening: guarded bass shelf, softened upper treble, and safe negative preamp.",
        PreampDb = -7.0,
        Bands = new ObservableCollection<EqBand>
        {
            new() { Number = 1, FilterType = EqFilterType.LowShelf, FrequencyHz = 85, GainDb = 5.5, Q = 0.70 },
            new() { Number = 2, FilterType = EqFilterType.Peak, FrequencyHz = 160, GainDb = 1.2, Q = 0.90 },
            new() { Number = 3, FilterType = EqFilterType.Peak, FrequencyHz = 450, GainDb = -0.8, Q = 1.00 },
            new() { Number = 4, FilterType = EqFilterType.Peak, FrequencyHz = 2400, GainDb = -0.8, Q = 1.20 },
            new() { Number = 5, FilterType = EqFilterType.Peak, FrequencyHz = 5600, GainDb = -2.5, Q = 2.00 },
            new() { Number = 6, FilterType = EqFilterType.Peak, FrequencyHz = 8200, GainDb = -2.0, Q = 2.00 },
            new() { Number = 7, FilterType = EqFilterType.HighShelf, FrequencyHz = 12000, GainDb = -1.0, Q = 0.70 },
            new() { Number = 8, FilterType = EqFilterType.Peak, FrequencyHz = 1000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 9, FilterType = EqFilterType.Peak, FrequencyHz = 4000, GainDb = 0.0, Q = 1.00, Enabled = false },
            new() { Number = 10, FilterType = EqFilterType.Peak, FrequencyHz = 16000, GainDb = 0.0, Q = 1.00, Enabled = false },
        }
    };
}
