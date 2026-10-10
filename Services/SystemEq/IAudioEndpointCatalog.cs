namespace WolfEQ.Services.SystemEq;

/// <summary>Lists active Windows playback endpoints. Implementations must not throw on COM failures.</summary>
public interface IAudioEndpointCatalog
{
    IReadOnlyList<AudioEndpointInfo> GetRenderEndpoints();
}
