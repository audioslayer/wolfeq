using System.IO;
using System.Security;
using WolfEQ.Models;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Applies the editor EQ through Equalizer APO by writing WolfEQ.txt and keeping WolfEQ's block in
/// config.txt (KTD2-KTD7). Calls are serialized; files are only rewritten when their bytes change.
/// </summary>
public sealed class EqualizerApoBackend : IEqOutputBackend
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private readonly Func<CancellationToken, Task<SystemEqBackendStatus>> _statusProvider;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="statusProvider">APO detection; supplies the config folder to write into.</param>
    /// <param name="maxAttempts">Attempts per file operation when APO briefly holds the file (KTD7).</param>
    /// <param name="retryDelay">Delay between attempts; the default spreads 5 attempts over about 200 ms.</param>
    public EqualizerApoBackend(
        Func<CancellationToken, Task<SystemEqBackendStatus>> statusProvider,
        int maxAttempts = 5,
        TimeSpan? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(statusProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        _statusProvider = statusProvider;
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(50);
    }

    public Task<SystemEqBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        => _statusProvider(cancellationToken);

    public async Task<SystemEqApplyResult> ApplyAsync(
        EqPreset preset,
        IReadOnlyList<string> endpointGuids,
        bool bypassed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);

        // Build the text before waiting so a later editor change cannot alter this snapshot.
        var guids = EqualizerApoConfigWriter.NormalizeEndpointGuids(endpointGuids);
        var includeBytes = EqualizerApoConfigWriter.Encoding.GetBytes(
            EqualizerApoConfigWriter.Build(preset, guids, bypassed));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? configDirectory = null;
        try
        {
            var status = await ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status is null)
            {
                return new SystemEqApplyResult(SystemEqApplyOutcome.Failed, "Couldn't check Equalizer APO.");
            }

            if (!status.IsInstalled || !ConfigDirectoryExists(status.ConfigDirectory))
            {
                return new SystemEqApplyResult(
                    SystemEqApplyOutcome.NotInstalled,
                    string.IsNullOrWhiteSpace(status.Message) ? "Equalizer APO is not installed." : status.Message);
            }

            configDirectory = status.ConfigDirectory!;

            // Include file first, so the config.txt block never points at a missing file.
            await WriteIfChangedAsync(
                ApoConfigIncludeManager.GetIncludePath(configDirectory), includeBytes, cancellationToken).ConfigureAwait(false);
            await EnsureConfigBlockAsync(configDirectory, cancellationToken).ConfigureAwait(false);

            if (bypassed)
            {
                return new SystemEqApplyResult(SystemEqApplyOutcome.Bypassed, "Bypassed. WolfEQ's EQ is off.");
            }

            return guids.Count == 0
                ? new SystemEqApplyResult(SystemEqApplyOutcome.NoEndpoints, "No outputs selected, so no EQ is applied.")
                : new SystemEqApplyResult(
                    SystemEqApplyOutcome.Applied,
                    guids.Count == 1 ? "EQ applied to 1 output." : $"EQ applied to {guids.Count} outputs.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return BlockedResult(configDirectory);
        }
        catch (IOException ex)
        {
            return new SystemEqApplyResult(SystemEqApplyOutcome.Failed, $"Couldn't update Equalizer APO's config: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SystemEqApplyResult> RemoveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? configDirectory = null;
        try
        {
            var status = await ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status is null)
            {
                return new SystemEqApplyResult(SystemEqApplyOutcome.Failed, "Couldn't check Equalizer APO.");
            }

            // Leftover config folders are cleaned too, even when APO itself is gone.
            if (!ConfigDirectoryExists(status.ConfigDirectory))
            {
                return new SystemEqApplyResult(SystemEqApplyOutcome.Applied, "Nothing to remove from Equalizer APO.");
            }

            configDirectory = status.ConfigDirectory!;

            // Block first, so config.txt never includes a file that no longer exists.
            var configPath = ApoConfigIncludeManager.GetConfigPath(configDirectory);
            var configBytes = await ReadBytesOrNullAsync(configPath, cancellationToken).ConfigureAwait(false);
            if (configBytes is not null)
            {
                var text = ApoConfigIncludeManager.DecodeConfig(configBytes, out var encoding);
                var removed = ApoConfigIncludeManager.RemoveBlock(text);
                if (!string.Equals(removed, text, StringComparison.Ordinal))
                {
                    await WriteAtomicAsync(
                        configPath, ApoConfigIncludeManager.EncodeConfig(removed, encoding), cancellationToken).ConfigureAwait(false);
                }
            }

            var includePath = ApoConfigIncludeManager.GetIncludePath(configDirectory);
            await RetryAsync(() => File.Delete(includePath), cancellationToken).ConfigureAwait(false);

            return new SystemEqApplyResult(SystemEqApplyOutcome.Applied, "Removed WolfEQ from Equalizer APO.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            return BlockedResult(configDirectory);
        }
        catch (IOException ex)
        {
            return new SystemEqApplyResult(SystemEqApplyOutcome.Failed, $"Couldn't update Equalizer APO's config: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SystemEqBackendStatus?> ReadStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _statusProvider(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Detection failures are reported as a result; the editor must keep working.
            return null;
        }
    }

    private static bool ConfigDirectoryExists(string? configDirectory)
        => !string.IsNullOrWhiteSpace(configDirectory) && Directory.Exists(configDirectory);

    private static SystemEqApplyResult BlockedResult(string? configDirectory)
        => new(
            SystemEqApplyOutcome.Blocked,
            $"Windows won't let WolfEQ write to the Equalizer APO config folder ({configDirectory ?? "unknown"}). "
            + "Check that the folder and its files aren't read-only or locked down, then try again.");

    private async Task EnsureConfigBlockAsync(string configDirectory, CancellationToken cancellationToken)
    {
        var configPath = ApoConfigIncludeManager.GetConfigPath(configDirectory);
        var bytes = await ReadBytesOrNullAsync(configPath, cancellationToken).ConfigureAwait(false);

        var encoding = ApoConfigIncludeManager.ConfigEncoding.Utf8;
        var text = bytes is null ? "" : ApoConfigIncludeManager.DecodeConfig(bytes, out encoding);
        var ensured = ApoConfigIncludeManager.EnsureBlock(text);
        if (bytes is null || !string.Equals(ensured, text, StringComparison.Ordinal))
        {
            await WriteAtomicAsync(
                configPath, ApoConfigIncludeManager.EncodeConfig(ensured, encoding), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteIfChangedAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var existing = await ReadBytesOrNullAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.AsSpan().SequenceEqual(content))
        {
            return;
        }

        await WriteAtomicAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private Task<byte[]?> ReadBytesOrNullAsync(string path, CancellationToken cancellationToken)
        => RetryAsync<byte[]?>(() =>
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }, cancellationToken);

    /// <summary>
    /// Temp file in the same folder, then a replacing move, so APO never reads a half-written file.
    /// The temp file is always removed, including when the move is denied (R12).
    /// </summary>
    private async Task WriteAtomicAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("The destination path has no parent directory.", nameof(path));
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            await RetryAsync(() => File.Move(temporaryPath, path, overwrite: true), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup must not hide the original write or replace failure.
            }
        }
    }

    private Task RetryAsync(Action action, CancellationToken cancellationToken)
        => RetryAsync<object?>(() =>
        {
            action();
            return null;
        }, cancellationToken);

    /// <summary>Retries only sharing/lock violations (APO reading the file); other failures surface at once.</summary>
    private async Task<T> RetryAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return operation();
            }
            catch (IOException ex) when (attempt < _maxAttempts && IsSharingViolation(ex))
            {
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsSharingViolation(IOException ex)
        => (ex.HResult & 0xFFFF) is ErrorSharingViolation or ErrorLockViolation;
}
