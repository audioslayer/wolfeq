using WolfEQ.Controls;
using Xunit;

namespace WolfEQ.Tests;

public sealed class BandPaletteTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(31)]
    public void EveryBandGetsItsOwnColor(int count)
        => Assert.Equal(count, Enumerable.Range(0, count).Select(i => BandPalette.ColorFor(i, count)).Distinct().Count());

    [Fact]
    public void LightModeIsDeeperAndOutOfRangeIndexesAreSafe()
    {
        var dark = BandPalette.ColorFor(2, 10);
        var light = BandPalette.ColorFor(2, 10, light: true);
        Assert.True(light.R + light.G + light.B < dark.R + dark.G + dark.B);
        Assert.Equal(BandPalette.ColorFor(9, 10), BandPalette.ColorFor(99, 10));
        Assert.Equal(BandPalette.ColorFor(0, 10), BandPalette.ColorFor(-1, 10));
        _ = BandPalette.ColorFor(0, 0);
    }
}
