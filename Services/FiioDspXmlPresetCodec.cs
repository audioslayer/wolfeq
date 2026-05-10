using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml.Linq;
using WolfEQ.Models;

namespace WolfEQ.Services;

public static class FiioDspXmlPresetCodec
{
    private const string DefaultModelName = "FIIO K13 R2R";

    public static EqPreset Import(string xml, string fallbackName = "Imported FiiO profile")
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new ArgumentException("FiiO XML is empty.", nameof(xml));
        }

        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new FormatException("FiiO XML did not contain a root element.");
        if (!string.Equals(root.Name.LocalName, "FiiO_DSP", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("This does not look like a FiiO DSP XML profile.");
        }

        var model = root.Attribute("model")?.Value;
        var description = root.Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "description", StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

        var eqGroup = root.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "eqGroup", StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException("FiiO XML did not contain an EQ group.");

        var preamp = ReadNamedParam(eqGroup, "masterGain", defaultValue: 0);
        var eqList = eqGroup.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "eqList", StringComparison.OrdinalIgnoreCase))
            ?? throw new FormatException("FiiO XML did not contain an EQ list.");

        var bands = eqList.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "eq", StringComparison.OrdinalIgnoreCase))
            .OrderBy(element => ParseInt(element.Attribute("index")?.Value, int.MaxValue))
            .Take(10)
            .Select((element, index) => new EqBand
            {
                Number = index + 1,
                Enabled = true,
                FilterType = ParseFilterType(ReadNamedParam(element, "type", defaultValue: 0)),
                FrequencyHz = (int)Math.Round(ReadNamedParam(element, "freq", defaultValue: 1000)),
                GainDb = ReadNamedParam(element, "gain", defaultValue: 0),
                Q = ReadNamedParam(element, "q", defaultValue: 1)
            })
            .ToList();

        if (bands.Count == 0)
        {
            throw new FormatException("FiiO XML did not contain any EQ bands.");
        }

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
            Name = string.IsNullOrWhiteSpace(model) ? fallbackName : $"{fallbackName} ({model})",
            Category = "Imported",
            SourceName = "FiiO XML",
            Description = string.IsNullOrWhiteSpace(description)
                ? "Imported from a FiiO Control DSP XML profile."
                : description,
            PreampDb = preamp,
            Bands = new ObservableCollection<EqBand>(bands)
        };
    }

    public static string Export(EqPreset preset, string modelName = DefaultModelName)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var root = new XElement("FiiO_DSP",
            new XAttribute("model", string.IsNullOrWhiteSpace(modelName) ? DefaultModelName : modelName),
            new XAttribute("version", "0.0.1"));

        var eqGroup = new XElement("eqGroup",
            new XElement("param",
                new XAttribute("name", "masterGain"),
                Format(preset.PreampDb)));

        var eqList = new XElement("eqList");
        foreach (var band in preset.Bands.OrderBy(band => band.Number).Take(10).Select((band, index) => (band, index)))
        {
            eqList.Add(new XElement("eq",
                new XAttribute("index", band.index),
                new XElement("param", new XAttribute("name", "type"), ((int)band.band.FilterType).ToString(CultureInfo.InvariantCulture)),
                new XElement("param", new XAttribute("name", "freq"), band.band.FrequencyHz.ToString(CultureInfo.InvariantCulture)),
                new XElement("param", new XAttribute("name", "gain"), Format(band.band.Enabled ? band.band.GainDb : 0)),
                new XElement("param", new XAttribute("name", "q"), Format(band.band.Q))));
        }

        eqGroup.Add(eqList);
        root.Add(new XElement("module", eqGroup));
        root.Add(new XElement("description", $"Exported from WolfEQ: {preset.Name}"));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString();
    }

    private static double ReadNamedParam(XElement parent, string name, double defaultValue)
    {
        var param = parent.Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "param", StringComparison.OrdinalIgnoreCase)
                && string.Equals(element.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));

        return param is null ? defaultValue : ParseDouble(param.Value, defaultValue);
    }

    private static EqFilterType ParseFilterType(double value)
    {
        var type = (int)Math.Round(value);
        return Enum.IsDefined(typeof(EqFilterType), type)
            ? (EqFilterType)type
            : EqFilterType.Peak;
    }

    private static int ParseInt(string? value, int defaultValue)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    private static double ParseDouble(string? value, double defaultValue)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    private static string Format(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
