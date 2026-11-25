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
    [InlineData("FIIO BTR25", "fiio-btr25")]
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
            FiioDeviceProfiles.Btr25,
            FiioDeviceProfiles.Btr13
        };

        Assert.All(profiles, profile =>
        {
            Assert.False(profile.SupportsLiveEqWrites);
            Assert.False(profile.SupportsEqReadback);
            Assert.False(profile.ReloadEqAfterSave);
        });
    }

    // Supported but not yet confirmed on real hardware. Remove an id from this list once it is verified.
    private static readonly string[] ExperimentalProfileIds = ["fiio-k17-r2r", "fiio-btr25", "crinear-protocol-micro", "moondrop-echo-a", "jcally-jm12", "kiwi-ears-ad1"];

    [Fact]
    public void EveryListedDevice_IsLabelledWorking_ExceptExplicitExperimentalOnes()
    {
        Assert.All(FiioDeviceProfiles.All.Where(profile => !ExperimentalProfileIds.Contains(profile.Id)), profile =>
        {
            Assert.True(profile.IsVerified);
            Assert.DoesNotContain("experimental", profile.DisplayLabel, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ExperimentalDevices_AreNotLabelledWorking()
    {
        foreach (var id in ExperimentalProfileIds)
        {
            var profile = Assert.Single(FiioDeviceProfiles.All, candidate => candidate.Id == id);
            Assert.False(profile.IsVerified);
            Assert.Contains("experimental", profile.DisplayLabel, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(0x0128, "fiio-qx13")]
    [InlineData(0x0088, "fiio-btr17")]
    [InlineData(0x4016, "fiio-oak-nano")]
    [InlineData(0x0151, "fiio-jiezi")]
    [InlineData(0x0147, "snowsky-tiny-a")]
    [InlineData(0x0140, "fiio-k17-r2r")]
    public void CommunityProfiles_MatchCapturedUsbProductIds(int productId, string expectedProfileId)
        => Assert.Equal(expectedProfileId, FiioDeviceProfiles.Match((ushort)productId, null, null)?.Id);

    [Theory]
    [InlineData("FiiO K17", "fiio-k17")]
    [InlineData("FIIO K17 R2R", "fiio-k17-r2r")]
    [InlineData("FIIO K19", "fiio-k19")]
    [InlineData("FiiO FP3", "fiio-fp3")]
    [InlineData("FiiO Oak Nano", "fiio-oak-nano")]
    [InlineData("FiiO KA17", "fiio-ka17")]
    [InlineData("SNOWSKY RETRO NANO", "snowsky-retro-nano")]
    [InlineData("JadeAudio JIEZI", "fiio-jiezi")]
    [InlineData("SNOWSKY TINY A", "snowsky-tiny-a")]
    public void NewProfiles_MatchByNameWithoutStealingSimilarModels(string productName, string expectedProfileId)
        => Assert.Equal(expectedProfileId, FiioDeviceProfiles.Match(null, productName, null)?.Id);

    [Fact]
    public void NewProfiles_StartInGuardedSaveOnlyModeWithTheirLayouts()
    {
        foreach (var profile in new[] { FiioDeviceProfiles.K17, FiioDeviceProfiles.K19, FiioDeviceProfiles.Fp3, FiioDeviceProfiles.OakNano, FiioDeviceProfiles.Jiezi, FiioDeviceProfiles.SnowskyTinyA })
        {
            Assert.False(profile.SupportsLiveEqWrites);
            Assert.False(profile.SupportsEqReadback);
            Assert.NotEmpty(profile.WritableSlots);
            Assert.Contains(profile.Id, profile.ProductImagePath);
        }

        Assert.Equal(31, FiioDeviceProfiles.K17.BandCount);
        Assert.Equal(0x19, FiioDeviceProfiles.K19.SaveCommandId);
        Assert.Equal(Enumerable.Range(0x07, 10), FiioDeviceProfiles.K17.WritableSlots.Select(slot => (int)slot.Id));
        Assert.Equal(8, FiioDeviceProfiles.OakNano.BandCount);
    }

    [Fact]
    public void Btr17_UsesStandardSaveCommandAndTenUserSlots()
    {
        Assert.Equal(0x19, FiioDeviceProfiles.Btr17.SaveCommandId);
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
