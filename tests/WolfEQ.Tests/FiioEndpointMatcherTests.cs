using WolfEQ.Services.SystemEq;
using Xunit;

namespace WolfEQ.Tests;

public sealed class FiioEndpointMatcherTests
{
    [Theory]
    [InlineData("Speakers (FiiO K13 R2R)", "Speakers")]
    [InlineData("Headphones (FiiO K13 R2R)", "Headphones")]
    [InlineData("Speakers (2- FIIO USB DAC)", "Speakers")]
    [InlineData("Speakers (BR15 R2R)", "Speakers")]
    [InlineData("Line Out (BR15-R2R)", "Line Out")]
    [InlineData("Speakers (KA17)", "Speakers")]
    [InlineData("Speakers", "Snowsky Melody")]
    public void FiioEndpoints_Match(string friendlyName, string description)
    {
        Assert.True(FiioEndpointMatcher.IsFiioEndpoint(Endpoint(friendlyName, description)));
    }

    [Theory]
    [InlineData("Speakers (Realtek(R) Audio)", "Speakers")]
    [InlineData("Speakers (Realtek)", "Speakers")]
    [InlineData("Headphones (Schiit Modi+)", "Headphones")]
    [InlineData("", "")]
    public void OtherEndpoints_DoNotMatch(string friendlyName, string description)
    {
        Assert.False(FiioEndpointMatcher.IsFiioEndpoint(Endpoint(friendlyName, description)));
    }

    private static AudioEndpointInfo Endpoint(string friendlyName, string description)
        => new("{0.0.0.00000000}.{6a1f2c3d-0000-4000-8000-0123456789ab}",
            "{6a1f2c3d-0000-4000-8000-0123456789ab}",
            friendlyName,
            description,
            IsDefault: false);
}
