using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using WolfEQ.Models;

namespace WolfEQ.Services;

public static class WolfEqPresetJsonCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Export(EqPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var dto = ToDto(preset);
        return JsonSerializer.Serialize(dto, Options);
    }

    public static string ExportLibrary(IEnumerable<EqPreset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var dto = new LibraryDto
        {
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Presets = presets.Select(ToDto).ToList()
        };

        return JsonSerializer.Serialize(dto, Options);
    }

    public static IReadOnlyList<EqPreset> ImportLibrary(string json, string fallbackName = "Imported WolfEQ library")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("WolfEQ library JSON is empty.", nameof(json));
        }

        var library = JsonSerializer.Deserialize<LibraryDto>(json, Options)
            ?? throw new FormatException("WolfEQ library JSON could not be parsed.");

        if (library.Presets.Count == 0)
        {
            throw new FormatException("WolfEQ library JSON did not contain any presets.");
        }

        return library.Presets
            .Select((preset, index) => FromDto(preset, $"{fallbackName} {index + 1}"))
            .ToList();
    }

    public static EqPreset Import(string json, string fallbackName = "Imported WolfEQ preset")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("WolfEQ preset JSON is empty.", nameof(json));
        }

        var dto = JsonSerializer.Deserialize<PresetDto>(json, Options)
            ?? throw new FormatException("WolfEQ preset JSON could not be parsed.");

        return FromDto(dto, fallbackName);
    }

    private static EqPreset FromDto(PresetDto dto, string fallbackName)
    {
        var bands = dto.Filters
            .OrderBy(filter => filter.Number)
            .Take(10)
            .Select((filter, index) => new EqBand
            {
                Number = index + 1,
                Enabled = filter.Enabled,
                FilterType = filter.Type,
                FrequencyHz = filter.FrequencyHz,
                GainDb = filter.GainDb,
                Q = filter.Q
            })
            .ToList();

        for (var i = bands.Count + 1; i <= 10; i++)
        {
            bands.Add(new EqBand
            {
                Number = i,
                Enabled = false,
                FilterType = EqFilterType.Peak,
                FrequencyHz = 1000,
                GainDb = 0,
                Q = 1
            });
        }

        return new EqPreset
        {
            Name = string.IsNullOrWhiteSpace(dto.Name) ? fallbackName : dto.Name,
            Category = string.IsNullOrWhiteSpace(dto.Category) ? "Imported" : dto.Category,
            SourceName = string.IsNullOrWhiteSpace(dto.Source?.Name) ? "WolfEQ JSON" : dto.Source!.Name,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? "Imported WolfEQ JSON preset. Offline only until hardware writes are explicitly enabled." : dto.Description,
            PreampDb = dto.PreampDb,
            Bands = new ObservableCollection<EqBand>(bands)
        };
    }

    private static PresetDto ToDto(EqPreset preset) => new()
    {
        Schema = "wolfeq.preset.v1",
        Name = preset.Name,
        Category = preset.Category,
        Description = preset.Description,
        Source = new SourceDto { Name = preset.SourceName },
        PreampDb = preset.PreampDb,
        Filters = preset.Bands
            .OrderBy(band => band.Number)
            .Select(band => new FilterDto
            {
                Number = band.Number,
                Enabled = band.Enabled,
                Type = band.FilterType,
                FrequencyHz = band.FrequencyHz,
                GainDb = band.GainDb,
                Q = band.Q
            })
            .ToList()
    };

    private sealed class LibraryDto
    {
        public string Schema { get; init; } = "wolfeq.library.v1";
        public DateTimeOffset ExportedAtUtc { get; init; }
        public List<PresetDto> Presets { get; init; } = [];
    }

    private sealed class PresetDto
    {
        public string Schema { get; init; } = "wolfeq.preset.v1";
        public string Name { get; init; } = "";
        public string Category { get; init; } = "";
        public string Description { get; init; } = "";
        public SourceDto? Source { get; init; }
        public double PreampDb { get; init; }
        public List<FilterDto> Filters { get; init; } = [];
    }

    private sealed class SourceDto
    {
        public string Name { get; init; } = "";
        public string Url { get; init; } = "";
        public string License { get; init; } = "";
        public string Measurement { get; init; } = "";
        public string Target { get; init; } = "";
    }

    private sealed class FilterDto
    {
        public int Number { get; init; }
        public bool Enabled { get; init; }
        public EqFilterType Type { get; init; } = EqFilterType.Peak;
        public int FrequencyHz { get; init; } = 1000;
        public double GainDb { get; init; }
        public double Q { get; init; } = 1;
    }
}
