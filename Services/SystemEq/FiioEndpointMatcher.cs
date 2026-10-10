using WolfEQ.Models;

namespace WolfEQ.Services.SystemEq;

/// <summary>Spots playback endpoints that are FiiO hardware, for the double-EQ warning (KTD12).</summary>
public static class FiioEndpointMatcher
{
    public static bool IsFiioEndpoint(AudioEndpointInfo endpoint)
        => NameMatches(endpoint.FriendlyName) || NameMatches(endpoint.DeviceDescription);

    private static bool NameMatches(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // ProductNameMatches strips spaces/dashes and looks for the model as a substring,
        // so "Headphones (FiiO K13 R2R)" matches the K13 R2R profile directly.
        return name.Contains("FiiO", StringComparison.OrdinalIgnoreCase)
            || FiioDeviceProfiles.All.Any(profile => FiioDeviceProfiles.ProductNameMatches(profile, name));
    }
}
