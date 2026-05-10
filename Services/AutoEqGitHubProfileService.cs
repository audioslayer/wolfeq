using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using WolfEQ.Models;

namespace WolfEQ.Services;

public sealed partial class AutoEqGitHubProfileService
{
    private const string Owner = "jaakkopasanen";
    private const string Repo = "AutoEq";
    private const string Branch = "master";
    private const string ResultsIndexUrl = "https://raw.githubusercontent.com/jaakkopasanen/AutoEq/master/results/INDEX.md";
    private const string GitHubResultsRoot = "https://github.com/jaakkopasanen/AutoEq/tree/master/results/";
    private const string ContentsApiRoot = "https://api.github.com/repos/jaakkopasanen/AutoEq/contents/results/";
    private static readonly HttpClient Http = CreateHttpClient();
    private IReadOnlyList<AutoEqProfileIndexEntry>? _cachedIndex;

    public async Task<IReadOnlyList<AutoEqProfileIndexEntry>> SearchAsync(
        string query,
        int limit = 80,
        CancellationToken cancellationToken = default)
    {
        var index = await GetIndexAsync(cancellationToken).ConfigureAwait(false);
        var terms = query
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        if (terms.Length == 0)
        {
            return index.Take(limit).ToList();
        }

        return index
            .Where(entry => terms.All(term =>
                entry.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.SourceName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                entry.Measurement.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(limit)
            .ToList();
    }

    public async Task<EqPreset> ImportParametricEqAsync(
        AutoEqProfileIndexEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var downloadUrl = await FindParametricEqDownloadUrlAsync(entry, cancellationToken).ConfigureAwait(false);
        var text = await Http.GetStringAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        var parsed = EqualizerApoPresetCodec.Parse(text, $"AutoEq - {entry.Name}");

        return new EqPreset
        {
            Name = $"AutoEq - {entry.Name}",
            Category = "Online",
            SourceName = $"AutoEq / {entry.SourceSummary}",
            Description = $"Downloaded from an online AutoEq source. Source path: {entry.ShortPath}. Imported offline; no device writes were sent.",
            PreampDb = parsed.PreampDb,
            Bands = new ObservableCollection<EqBand>(parsed.Bands.Select(CloneBand))
        };
    }

    private async Task<IReadOnlyList<AutoEqProfileIndexEntry>> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (_cachedIndex is not null)
        {
            return _cachedIndex;
        }

        var markdown = await Http.GetStringAsync(ResultsIndexUrl, cancellationToken).ConfigureAwait(false);
        _cachedIndex = ParseIndex(markdown);
        return _cachedIndex;
    }

    private static IReadOnlyList<AutoEqProfileIndexEntry> ParseIndex(string markdown)
    {
        var entries = new List<AutoEqProfileIndexEntry>();
        foreach (var line in markdown.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = IndexLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var path = match.Groups["path"].Value.Trim();
            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path[2..];
            }

            entries.Add(new AutoEqProfileIndexEntry(
                match.Groups["name"].Value.Trim(),
                match.Groups["source"].Value.Trim(),
                match.Groups["measurement"].Value.Trim(),
                path,
                GitHubResultsRoot + path));
        }

        return entries;
    }

    private static async Task<string> FindParametricEqDownloadUrlAsync(
        AutoEqProfileIndexEntry entry,
        CancellationToken cancellationToken)
    {
        var apiUrl = $"{ContentsApiRoot}{entry.EncodedRelativePath}?ref={Branch}";
        var json = await Http.GetStringAsync(apiUrl, cancellationToken).ConfigureAwait(false);
        var files = JsonSerializer.Deserialize<List<GitHubContentDto>>(json, JsonOptions())
            ?? throw new FormatException("Online contents response could not be parsed.");

        var parametric = files.FirstOrDefault(file =>
            string.Equals(file.Type, "file", StringComparison.OrdinalIgnoreCase) &&
            file.Name.EndsWith("ParametricEQ.txt", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(file.DownloadUrl));

        if (parametric is null)
        {
            throw new FileNotFoundException($"No ParametricEQ.txt file was found in AutoEq path {entry.ShortPath}.");
        }

        return parametric.DownloadUrl!;
    }

    private static EqBand CloneBand(EqBand band) => new()
    {
        Number = band.Number,
        Enabled = band.Enabled,
        FilterType = band.FilterType,
        FrequencyHz = band.FrequencyHz,
        GainDb = band.GainDb,
        Q = band.Q
    };

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WolfEQ", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    [GeneratedRegex(@"^-\s+\[(?<name>.+?)\]\(\./(?<path>.+?)\)\s+by\s+(?<source>.+?)(?:\s+on\s+(?<measurement>.+))?$")]
    private static partial Regex IndexLineRegex();

    private sealed class GitHubContentDto
    {
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public string? DownloadUrl { get; init; }
    }
}
