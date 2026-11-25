using System.Reflection;
using WolfEQ.Controls;
using WolfEQ.Models;
using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class QScaleTests
{
    [Theory]
    [InlineData("fiio-k13-r2r", EqFilterType.Peak, 141)]
    [InlineData("fiio-k13-r2r", EqFilterType.LowShelf, 100)]
    [InlineData("fiio-k13-r2r", EqFilterType.LowPass, 100)]
    [InlineData("fiio-ka15", EqFilterType.HighShelf, 141)]
    [InlineData("fiio-ka15", EqFilterType.Peak, 100)]
    [InlineData("snowsky-melody", EqFilterType.LowShelf, 141)]
    [InlineData("snowsky-tiny-a", EqFilterType.LowShelf, 141)]
    [InlineData("fiio-jiezi", EqFilterType.LowShelf, 141)]
    [InlineData("fiio-ja11", EqFilterType.LowShelf, 100)]
    [InlineData("snowsky-retro-nano", EqFilterType.Peak, 100)]
    [InlineData("fiio-ka17", EqFilterType.Peak, 100)]
    [InlineData("fiio-btr17", EqFilterType.LowShelf, 100)]
    [InlineData("fiio-btr25", EqFilterType.Peak, 100)]
    public void OnlyMeasuredFiltersAreScaled(string id, EqFilterType type, int register)
    {
        var frame = FiioK13DeviceService.EncodeBand(new(1, 1000, 3, 1, (byte)type), Profile(id));
        Assert.Equal(register, (frame[5] << 8) | frame[6]);
    }

    [Theory]
    [InlineData("fiio-k13-r2r", EqFilterType.Peak)]
    [InlineData("fiio-ka15", EqFilterType.LowShelf)]
    [InlineData("fiio-ka15", EqFilterType.Peak)]
    public void EveryQSurvivesSaveAndLoad(string id, EqFilterType type)
    {
        var profile = Profile(id);
        for (var step = 10; step <= Math.Round(profile.MaxQFor(type) * 100); step++)
        {
            var band = new K13EqBandReadback(2, 1000, -3.5, step / 100.0, (byte)type);
            var sent = FiioK13DeviceService.EncodeBand(band, profile);
            Assert.True(((sent[5] << 8) | sent[6]) <= 1000);
            Assert.Equal(band, FiioK13DeviceService.DecodeBand([0xBB, 0x0B, 0, 0, 0x15, 8, .. sent], profile));
        }
    }

    [Fact]
    public void QLimitAndReadbackSlackFollowTheRegister()
    {
        var k13 = FiioDeviceProfiles.K13R2R;
        Assert.Equal(7.07, k13.MaxQFor(EqFilterType.Peak));
        Assert.Equal(10, k13.MaxQFor(EqFilterType.LowShelf));
        Assert.True(k13.QMatches(EqFilterType.Peak, 7.0, k13.DecodeQRegister(EqFilterType.Peak, 9.89)));
        Assert.False(k13.QMatches(EqFilterType.Peak, 1.0, k13.DecodeQRegister(EqFilterType.Peak, 1.0)));
    }

    [Fact]
    public void GraphShelvesKeepChangingAboveQOne()
    {
        var gain = typeof(EqResponseGraphControl).GetMethod("EstimateBandGainDb", BindingFlags.Static | BindingFlags.NonPublic)!;
        double At(double q, double hz) => (double)gain.Invoke(null, [hz, new EqBand { Number = 1, Enabled = true, FilterType = EqFilterType.LowShelf, FrequencyHz = 100, GainDb = 6, Q = q }, null])!;
        Assert.InRange(At(0.7071, 20), 5.5, 6.1);
        Assert.InRange(At(0.7071, 100), 2.9, 3.1);
        Assert.True(Math.Abs(At(1, 70) - At(2, 70)) > 0.3);
    }

    private static FiioDeviceProfile Profile(string id) => FiioDeviceProfiles.All.Single(profile => profile.Id == id);
}
