using WolfEQ.Models;
using Xunit;

namespace WolfEQ.Tests;

public sealed class FiioDeviceProfileTests
{
    [Fact]
    public void SnowskyMelody_HasWritableUserSlots()
    {
        var writableSlots = FiioDeviceProfiles.SnowskyMelody.WritableSlots;

        Assert.Equal([0xA0, 0xA1, 0xA2], writableSlots.Select(slot => (int)slot.Id));
        Assert.All(writableSlots, slot => Assert.StartsWith("USER ", slot.Name));
    }

    [Fact]
    public void SnowskyMelody_DisablesEqReadback()
    {
        Assert.False(FiioDeviceProfiles.SnowskyMelody.SupportsEqReadback);
    }

    [Fact]
    public void NormalProfiles_KeepEqReadbackEnabledByDefault()
    {
        Assert.True(FiioDeviceProfiles.K13R2R.SupportsEqReadback);
        Assert.True(FiioDeviceProfiles.Ka15.SupportsEqReadback);
        Assert.True(FiioDeviceProfiles.Ka17.SupportsEqReadback);
        Assert.True(FiioDeviceProfiles.Ja11.SupportsEqReadback);
        Assert.True(FiioDeviceProfiles.SnowskyRetroNano.SupportsEqReadback);
    }
}
