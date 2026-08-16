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

    [Theory]
    [InlineData("FIIO BR15 R2R", "fiio-br15-r2r")]
    [InlineData("FiiO QX13", "fiio-qx13")]
    [InlineData("FIIO BTR17", "fiio-btr17")]
    [InlineData("BTR13", "fiio-btr13")]
    public void CommunityProfiles_MatchPublishedUsbProductNames(string productName, string expectedProfileId)
    {
        var profile = FiioDeviceProfiles.Match(null, productName, null);

        Assert.NotNull(profile);
        Assert.Equal(expectedProfileId, profile.Id);
    }

    [Fact]
    public void CommunityProfiles_StartInGuardedSaveOnlyMode()
    {
        var profiles = new[]
        {
            FiioDeviceProfiles.Br15R2R,
            FiioDeviceProfiles.Qx13,
            FiioDeviceProfiles.Btr17,
            FiioDeviceProfiles.Btr13
        };

        Assert.All(profiles, profile =>
        {
            Assert.False(profile.IsVerified);
            Assert.False(profile.SupportsLiveEqWrites);
            Assert.False(profile.SupportsEqReadback);
            Assert.False(profile.ReloadEqAfterSave);
        });
    }

    [Fact]
    public void Btr17_UsesV2SaveCommandAndTenUserSlots()
    {
        Assert.Equal(0x21, FiioDeviceProfiles.Btr17.SaveCommandId);
        Assert.Equal(
            Enumerable.Range(0xA0, 10),
            FiioDeviceProfiles.Btr17.WritableSlots.Select(slot => (int)slot.Id));
    }

    [Fact]
    public void Br15R2R_ExcludesUnsupportedBandPassFilter()
    {
        Assert.False(FiioDeviceProfiles.Br15R2R.SupportsFilter(EqFilterType.BandPass));
        Assert.True(FiioDeviceProfiles.Br15R2R.SupportsFilter(EqFilterType.AllPass));
    }
}
