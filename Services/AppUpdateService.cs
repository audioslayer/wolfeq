using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WolfEQ.Services;

public static class AppUpdateService
{
    private const string GitHubRepo = "audioslayer/wolfeq";
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
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases?per_page=20");
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
                item.Name.EndsWith(preferredExtension, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.BrowserDownloadUrl));

            if (asset is not null)
            {
                return new AppUpdate(tag, remoteVersion, asset.Name, asset.BrowserDownloadUrl);
            }
        }

        return null;
    }

    public static async Task DownloadAndInstallAsync(AppUpdate update, Action<int>? onProgress = null)
    {
        var extension = Path.GetExtension(update.AssetName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".exe";
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"WolfEQ-Update{extension}");

        using var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        long downloaded = 0;

        await using (var stream = await response.Content.ReadAsStreamAsync())
        await using (var file = File.Create(tempPath))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (totalBytes > 0)
                {
                    onProgress?.Invoke((int)(downloaded * 100 / totalBytes));
                }
            }
        }

        var batPath = Path.Combine(Path.GetTempPath(), "WolfEQ-Update.bat");
        File.WriteAllText(
            batPath,
            $"@echo off{Environment.NewLine}timeout /t 3 /nobreak >nul{Environment.NewLine}start \"\" \"{tempPath}\"{Environment.NewLine}del \"%~f0\"{Environment.NewLine}");

        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        OnShutdownRequested?.Invoke();
    }

    private static JsonSerializerOptions JsonOptions()
        => new() { PropertyNameCaseInsensitive = true };

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
