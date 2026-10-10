using NAudio.CoreAudioApi;

namespace WolfEQ.Services.SystemEq;

/// <summary>Lists active playback endpoints through NAudio's MMDeviceEnumerator. Returns empty on COM failures.</summary>
public sealed class WindowsAudioEndpointCatalog : IAudioEndpointCatalog
{
    public IReadOnlyList<AudioEndpointInfo> GetRenderEndpoints()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = TryGetDefaultRenderId(enumerator);
            var endpoints = new List<AudioEndpointInfo>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        var id = device.ID;
                        if (!TryParseEndpointGuid(id, out var guid))
                        {
                            continue;
                        }

                        endpoints.Add(new AudioEndpointInfo(
                            id,
                            guid,
                            device.FriendlyName ?? string.Empty,
                            device.DeviceFriendlyName ?? string.Empty,
                            string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                    }
                    catch
                    {
                        // One unreadable endpoint should not hide the rest.
                    }
                }
            }

            return endpoints;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Extracts the trailing endpoint GUID (braced, lower-case as Windows stores it) from an
    /// IMMDevice id like <c>{0.0.0.00000000}.{guid}</c>. Returns false for anything else.
    /// </summary>
    public static bool TryParseEndpointGuid(string? endpointId, out string guid)
    {
        guid = string.Empty;
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return false;
        }

        var trimmed = endpointId.Trim();
        var separator = trimmed.LastIndexOf("}.{", StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        if (!Guid.TryParseExact(trimmed[(separator + 2)..], "B", out var parsed))
        {
            return false;
        }

        guid = parsed.ToString("B");
        return true;
    }

    private static string? TryGetDefaultRenderId(MMDeviceEnumerator enumerator)
    {
        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                return null;
            }

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }
}
