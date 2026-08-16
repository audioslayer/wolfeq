using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WolfEQ.Services;

public static class AppUpdateService
{
    private const string GitHubRepo = "audioslayer/wolfeq";
    private const long MaximumInstallerBytes = 250L * 1024 * 1024;
    private const int MaximumReleaseMetadataBytes = 2 * 1024 * 1024;
    private static readonly HttpClient Http = new();

    public static readonly string CurrentVersion =
        (Assembly.GetEntryAssembly() ?? typeof(AppUpdateService).Assembly)
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

    public static Action? OnShutdownRequested { get; set; }

    static AppUpdateService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"WolfEQ/{CurrentVersion}");
    }

    public static async Task<AppUpdate?> CheckForUpdateAsync(string preferredExtension = ".exe")
    {
        using var response = await Http.GetAsync(
            $"https://api.github.com/repos/{GitHubRepo}/releases?per_page=20",
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is not { Scheme: "https", Host: "api.github.com" })
        {
            throw new InvalidOperationException("The release check redirected to an untrusted host.");
        }

        var json = await ReadLimitedStringAsync(response, MaximumReleaseMetadataBytes, "GitHub release metadata");
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, JsonOptions()) ?? [];
        var includePrereleases = IsPrerelease(CurrentVersion);

        foreach (var release in releases)
        {
            if (release.Draft || release.Prerelease && !includePrereleases)
            {
                continue;
            }

            var tag = release.TagName ?? string.Empty;
            var remoteVersion = tag.TrimStart('v', 'V');
            if (!IsNewer(remoteVersion, CurrentVersion))
            {
                continue;
            }

            var asset = release.Assets.FirstOrDefault(item =>
                item.Name.StartsWith("WolfEQ-Setup-", StringComparison.OrdinalIgnoreCase) &&
                item.Name.EndsWith(preferredExtension, StringComparison.OrdinalIgnoreCase) &&
                IsTrustedReleaseDownloadUri(item.BrowserDownloadUrl));

            if (asset is not null)
            {
                return new AppUpdate(tag, remoteVersion, asset.Name, asset.BrowserDownloadUrl);
            }
        }

        return null;
    }

    public static async Task DownloadAndInstallAsync(AppUpdate update, Action<int>? onProgress = null)
    {
        if (!update.AssetName.StartsWith("WolfEQ-Setup-", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(update.AssetName), ".exe", StringComparison.OrdinalIgnoreCase)
            || !IsTrustedReleaseDownloadUri(update.DownloadUrl))
        {
            throw new InvalidOperationException("The update did not point to a trusted WolfEQ installer asset.");
        }

        var updateDirectory = Path.Combine(Path.GetTempPath(), "WolfEQ", "Updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        var tempPath = Path.Combine(updateDirectory, "WolfEQ-Update.exe");

        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (!IsTrustedRedirectTarget(response.RequestMessage?.RequestUri))
        {
            throw new InvalidOperationException("The update download redirected to an untrusted host.");
        }

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        if (totalBytes > MaximumInstallerBytes)
        {
            throw new InvalidOperationException("The update installer exceeds the 250 MB safety limit.");
        }

        long downloaded = 0;

        await using (var stream = await response.Content.ReadAsStreamAsync())
        await using (var file = new FileStream(
                         tempPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (downloaded > MaximumInstallerBytes)
                {
                    throw new InvalidOperationException("The update installer exceeded the 250 MB safety limit while downloading.");
                }

                if (totalBytes > 0)
                {
                    onProgress?.Invoke((int)(downloaded * 100 / totalBytes));
                }
            }
        }

        await using (var installer = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (installer.ReadByte() != 'M' || installer.ReadByte() != 'Z')
            {
                throw new InvalidOperationException("The downloaded update is not a valid Windows executable.");
            }
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            UseShellExecute = true,
            Verb = "open"
        });

        OnShutdownRequested?.Invoke();
    }

    private static JsonSerializerOptions JsonOptions()
        => new() { PropertyNameCaseInsensitive = true };

    private static bool IsTrustedReleaseDownloadUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && uri.Scheme == Uri.UriSchemeHttps
           && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
           && uri.AbsolutePath.StartsWith($"/{GitHubRepo}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedRedirectTarget(Uri? uri)
        => uri is not null
           && uri.Scheme == Uri.UriSchemeHttps
           && (string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ReadLimitedStringAsync(
        HttpResponseMessage response,
        int maximumBytes,
        string contentLabel)
    {
        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new InvalidOperationException($"{contentLabel} exceeded the configured safety limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + read > maximumBytes)
            {
                throw new InvalidOperationException($"{contentLabel} exceeded the configured safety limit.");
            }

            memory.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
    }

    private static bool IsNewer(string remoteVersion, string localVersion)
    {
        var remoteParts = remoteVersion.Split('-', 2);
        var localParts = localVersion.Split('-', 2);
        var remoteNumbers = ParseVersionNumbers(remoteParts[0]);
        var localNumbers = ParseVersionNumbers(localParts[0]);

        var length = Math.Max(remoteNumbers.Length, localNumbers.Length);
        for (var index = 0; index < length; index++)
        {
            var remote = index < remoteNumbers.Length ? remoteNumbers[index] : 0;
            var local = index < localNumbers.Length ? localNumbers[index] : 0;
            if (remote > local) return true;
            if (remote < local) return false;
        }

        return PrereleaseRank(remoteParts) > PrereleaseRank(localParts);
    }

    private static int[] ParseVersionNumbers(string version)
        => version.Split('.')
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .ToArray();

    private static bool IsPrerelease(string version)
        => version.Contains('-', StringComparison.Ordinal);

    private static int PrereleaseRank(string[] versionParts)
    {
        if (versionParts.Length == 1)
        {
            return 100;
        }

        var label = versionParts[1].ToLowerInvariant();
        if (label.StartsWith("alpha", StringComparison.Ordinal)) return 10;
        if (label.StartsWith("beta", StringComparison.Ordinal)) return 20;
        if (label.StartsWith("preview", StringComparison.Ordinal)) return 20;
        if (label.StartsWith("rc", StringComparison.Ordinal)) return 30;
        return 1;
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}

public sealed record AppUpdate(string Tag, string Version, string AssetName, string DownloadUrl);
