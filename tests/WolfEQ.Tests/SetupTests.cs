using WolfEQ.Models;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class SetupTests
{
    [Theory]
    [InlineData(@"\\?\hid#vid_2972&pid_0093&mi_02#8&1", "fiio-ka17", false)]
    [InlineData(@"\\?\hid#vid_2972&pid_0120&mi_03#8&1", "fiio-k13-r2r", false)]
    [InlineData(@"\\?\hid#vid_3302&pid_43E8#8&1", "trn-black-pearl", false)]
    [InlineData(@"\\?\hid#vid_046d&pid_0093#8&1", null, true)]
    [InlineData(null, null, true)]
    public void ASavedPathNamesItsModelOnlyForSupportedVendors(string? path, string? id, bool isNull)
    {
        var profile = MainViewModel.MatchSavedPath(path);
        Assert.Equal(isNull, profile is null);
        Assert.Equal(id, profile?.Id);
    }

    [Fact]
    public void TipsMatchWhatEachDeviceNeeds()
    {
        Assert.Contains(MainViewModel.BuildSetupTips(FiioDeviceProfiles.K17R2R), tip => tip.Contains("RS232"));
        Assert.Contains(MainViewModel.BuildSetupTips(FiioDeviceProfiles.K13R2R), tip => tip.Contains("UAC 2.0"));
        Assert.Contains(MainViewModel.BuildSetupTips(FiioDeviceProfiles.Btr25), tip => tip.Contains("experimental"));
        Assert.DoesNotContain(MainViewModel.BuildSetupTips(SystemEqProfiles.WindowsSystemEq), tip => tip.Contains("slot"));
        Assert.All(FiioDeviceProfiles.All, profile => Assert.InRange(MainViewModel.BuildSetupTips(profile).Count, 2, 6));
    }
}
