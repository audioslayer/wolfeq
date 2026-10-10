using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Fallback when the bundled Equalizer APO installer is missing (developer builds, trimmed
/// portable copies): downloads the official pinned installer and only hands it back when its
/// SHA-256 matches the pinned value, so nothing unverified is ever launched elevated.
/// </summary>
public sealed class ApoInstallerDownloader
{
    public const string DownloadUrl =
        "https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download";

    /// <summary>Matches redist/EqualizerAPO/README.md and build-installer.bat.</summary>
    public const string PinnedSha256 = "7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa";

    private const long MaxInstallerBytes = 64L * 1024 * 1024;

    private readonly Func<HttpClient> _createClient;
    private readonly string _cacheDirectory;

    public ApoInstallerDownloader(Func<HttpClient>? createClient = null, string? cacheDirectory = null)
    {
        _createClient = createClient ?? CreateDefaultClient;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WolfEQ", "redist");
    }

    public string CachedInstallerPath => Path.Combine(_cacheDirectory, ApoProcessLauncher.PinnedInstallerFileName);

    /// <summary>
    /// Returns a verified installer path, reusing a previously downloaded copy when it still
    /// matches the pinned hash. Returns null with a plain message when the download fails.
    /// </summary>
    public async Task<(string? Path, string Message)> GetVerifiedInstallerAsync(
        IProgress<ApoSetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = CachedInstallerPath;
        progress?.Report(new ApoSetupProgress(ApoSetupStage.Verifying));
        if (await MatchesPinnedHashAsync(target, cancellationToken).ConfigureAwait(false))
        {
            return (target, "Using the verified Equalizer APO installer.");
        }

        var temporary = target + ".download";
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            using (var client = _createClient())
            using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength;
                progress?.Report(new ApoSetupProgress(ApoSetupStage.Downloading, 0, totalBytes));
                if (response.Content.Headers.ContentLength is > MaxInstallerBytes)
                {
                    return (null, "The Equalizer APO download was unexpectedly large, so it was not used.");
                }

                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var destination = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None);
                await CopyBoundedAsync(source, destination, totalBytes, progress, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new ApoSetupProgress(ApoSetupStage.Verifying));
            if (!await MatchesPinnedHashAsync(temporary, cancellationToken).ConfigureAwait(false))
            {
                return (null, "The downloaded Equalizer APO installer failed its integrity check, so it was not run.");
            }

            File.Move(temporary, target, overwrite: true);
            return (target, "Downloaded and verified the Equalizer APO installer.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException or InvalidDataException)
        {
            return (null, $"Couldn't download Equalizer APO ({ex.Message}). Check your connection or install it from sourceforge.net/projects/equalizerapo.");
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static async Task<bool> MatchesPinnedHashAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return string.Equals(Convert.ToHexString(hash), PinnedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        IProgress<ApoSetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Report about every 256 KB so the UI gets a smooth bar without a flood of dispatcher posts.
        const long reportEvery = 256 * 1024;
        long nextReport = reportEvery;
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxInstallerBytes)
            {
                throw new InvalidDataException("download exceeded the size limit");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            if (total >= nextReport)
            {
                nextReport = total + reportEvery;
                progress?.Report(new ApoSetupProgress(ApoSetupStage.Downloading, total, totalBytes));
            }
        }

        progress?.Report(new ApoSetupProgress(ApoSetupStage.Downloading, total, totalBytes ?? total));
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        // SourceForge serves an HTML landing page to browser user agents; a tool agent gets the file.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("curl/8.0");
        return client;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale partial download is overwritten next time.
        }
    }
}
