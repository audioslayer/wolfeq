using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class DevModeServerTests
{
    [Fact]
    public void ExtractFlag_RemovesDevFlagSoAutomationNeverSeesIt()
    {
        var args = DevModeServer.ExtractFlag(["--DEV"], out var enabled);

        Assert.True(enabled);
        Assert.Empty(args);
        Assert.Equal(new AutomationRequest(), AutomationRequest.Parse(args));
    }

    [Fact]
    public void ExtractFlag_KeepsOtherArgumentsInOrder()
    {
        var args = DevModeServer.ExtractFlag(["--slot", "2", "--dev", "--device", "Desk K13"], out var enabled);

        Assert.True(enabled);
        Assert.Equal(["--slot", "2", "--device", "Desk K13"], args);
    }

    [Fact]
    public void ExtractFlag_IsOffWithoutTheFlag()
    {
        string[] original = ["--slot", "3"];

        var args = DevModeServer.ExtractFlag(original, out var enabled);

        Assert.False(enabled);
        Assert.Same(original, args);
    }
}
