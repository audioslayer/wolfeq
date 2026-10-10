using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using WolfEQ.Models;
using WolfEQ.Services.SystemEq;
using Xunit;

namespace WolfEQ.Tests;

public sealed class EqualizerApoConfigWriterTests
{
    private const string GuidA = "{11111111-2222-3333-4444-555555555555}";
    private const string GuidB = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    [Fact]
    public void AnandaMusicWithTwoEndpointsProducesExactText()
    {
        var text = EqualizerApoConfigWriter.Build(EqPreset.AnandaMusic(), [GuidA, GuidB], bypassed: false);

        var expected = string.Join("\r\n",
            "# Managed by WolfEQ - changes here are overwritten",
            $"Device: {GuidA}; {GuidB}",
            "Preamp: -5 dB",
            "Filter 1: ON LSC Fc 80 Hz Gain 3 dB Q 0.7",
            "Filter 2: ON PK Fc 150 Hz Gain 1.5 dB Q 1",
            "Filter 3: ON PK Fc 2000 Hz Gain -1.5 dB Q 1.2",
            "Filter 4: ON PK Fc 5500 Hz Gain -2 dB Q 2",
            "Filter 5: ON PK Fc 8000 Hz Gain -1.5 dB Q 2",
            "Filter 6: ON HSC Fc 10000 Hz Gain -1 dB Q 0.7",
            "Filter 7: OFF PK Fc 1000 Hz Gain 0 dB Q 1",
            "Filter 8: OFF PK Fc 3000 Hz Gain 0 dB Q 1",
            "Filter 9: OFF PK Fc 6000 Hz Gain 0 dB Q 1",
            "Filter 10: OFF PK Fc 12000 Hz Gain 0 dB Q 1") + "\r\n";
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(EqFilterType.Peak, "Filter 3: ON PK Fc 440 Hz Gain -2.5 dB Q 1.41")]
    [InlineData(EqFilterType.LowShelf, "Filter 3: ON LSC Fc 440 Hz Gain -2.5 dB Q 1.41")]
    [InlineData(EqFilterType.HighShelf, "Filter 3: ON HSC Fc 440 Hz Gain -2.5 dB Q 1.41")]
    [InlineData(EqFilterType.BandPass, "Filter 3: ON BP Fc 440 Hz Q 1.41")]
    [InlineData(EqFilterType.LowPass, "Filter 3: ON LPQ Fc 440 Hz Q 1.41")]
    [InlineData(EqFilterType.HighPass, "Filter 3: ON HPQ Fc 440 Hz Q 1.41")]
    [InlineData(EqFilterType.AllPass, "Filter 3: ON AP Fc 440 Hz Q 1.41")]
    public void EachFilterTypeUsesItsApoLineShape(EqFilterType type, string expected)
    {
        var band = new EqBand { Number = 3, FilterType = type, FrequencyHz = 440, GainDb = -2.5, Q = 1.41 };

        Assert.Equal(expected, EqualizerApoConfigWriter.FormatFilterLine(band));
    }

    [Fact]
    public void DisabledBandEmitsOffWithSameShape()
    {
        var band = new EqBand { Number = 2, FilterType = EqFilterType.HighPass, FrequencyHz = 30, Q = 0.71, Enabled = false };

        Assert.Equal("Filter 2: OFF HPQ Fc 30 Hz Q 0.71", EqualizerApoConfigWriter.FormatFilterLine(band));
    }

    [Theory]
    [InlineData(1234.5678, "1234.57")]
    [InlineData(1234.0, "1234")]
    [InlineData(0.1, "0.1")]
    [InlineData(-0.001, "0")]
    [InlineData(-24.0, "-24")]
    [InlineData(20000.0, "20000")]
    public void NumbersUseInvariantCultureWithAtMostTwoDecimals(double value, string expected)
    {
        Assert.Equal(expected, EqualizerApoConfigWriter.FormatNumber(value));
    }

    [Fact]
    public void GermanCultureStillWritesDotDecimalsAndNoGrouping()
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

            Assert.Equal("1234.57", EqualizerApoConfigWriter.FormatNumber(1234.5678));
            var preset = new EqPreset
            {
                PreampDb = -5.5,
                Bands = new ObservableCollection<EqBand>
                {
                    new() { Number = 1, FilterType = EqFilterType.Peak, FrequencyHz = 12500, GainDb = -1.5, Q = 0.71 }
                }
            };
            var text = EqualizerApoConfigWriter.Build(preset, [GuidA], bypassed: false);

            Assert.Contains("Preamp: -5.5 dB", text);
            Assert.Contains("Filter 1: ON PK Fc 12500 Hz Gain -1.5 dB Q 0.71", text);
            Assert.DoesNotContain(",", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void BypassedWritesHeaderOnly()
    {
        var text = EqualizerApoConfigWriter.Build(EqPreset.AnandaMusic(), [GuidA, GuidB], bypassed: true);

        Assert.Equal(EqualizerApoConfigWriter.HeaderComment + "\r\n", text);
        Assert.DoesNotContain("Device:", text);
        Assert.DoesNotContain("Preamp:", text);
        Assert.DoesNotContain("Filter", text);
    }

    [Fact]
    public void NoEndpointsWritesHeaderOnly()
    {
        Assert.Equal(
            EqualizerApoConfigWriter.HeaderComment + "\r\n",
            EqualizerApoConfigWriter.Build(EqPreset.AnandaMusic(), [], bypassed: false));
    }

    [Fact]
    public void EndpointGuidsAreValidatedTrimmedAndDeduplicated()
    {
        var guids = EqualizerApoConfigWriter.NormalizeEndpointGuids(
        [
            $"  {GuidA} ",
            GuidA.ToLowerInvariant(),
            "11111111-2222-3333-4444-666666666666",
            "{not-a-guid}\r\nInclude: evil.txt",
            "",
            GuidB
        ]);

        Assert.Equal([GuidA, "{11111111-2222-3333-4444-666666666666}", GuidB], guids);
    }

    [Fact]
    public void OnlyInvalidEndpointsWritesHeaderOnly()
    {
        var text = EqualizerApoConfigWriter.Build(EqPreset.AnandaMusic(), ["bogus; Device: all"], bypassed: false);

        Assert.Equal(EqualizerApoConfigWriter.HeaderComment + "\r\n", text);
    }

    [Fact]
    public void OutputEncodedAsUtf8HasNoBom()
    {
        var text = EqualizerApoConfigWriter.Build(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        var bytes = EqualizerApoConfigWriter.Encoding.GetBytes(text);

        Assert.Empty(EqualizerApoConfigWriter.Encoding.GetPreamble());
        Assert.Equal((byte)'#', bytes[0]);
        Assert.Equal(text, Encoding.UTF8.GetString(bytes));
    }
}
