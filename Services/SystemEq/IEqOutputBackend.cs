using WolfEQ.Models;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Software EQ output used by the Software EQ mode. FiiO hardware does not go through this seam.
/// </summary>
public interface IEqOutputBackend
{
    Task<SystemEqBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a full snapshot. <paramref name="endpointGuids"/> are endpoint GUIDs with braces.
    /// When <paramref name="bypassed"/> is true or no endpoints are given, WolfEQ's effect is removed
    /// while its config.txt include stays in place.
    /// </summary>
    Task<SystemEqApplyResult> ApplyAsync(
        EqPreset preset,
        IReadOnlyList<string> endpointGuids,
        bool bypassed,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes WolfEQ's include file and removes the managed block from config.txt.</summary>
    Task<SystemEqApplyResult> RemoveAsync(CancellationToken cancellationToken = default);
}
