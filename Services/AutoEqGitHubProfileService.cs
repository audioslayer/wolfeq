using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
    private const string OpraDatabaseUrl = "https://raw.githubusercontent.com/opra-project/OPRA/main/dist/database_v1.jsonl";
    private const string OpraGitHubUrl = "https://github.com/opra-project/OPRA";
    private const int MaximumProfileBytes = 2 * 1024 * 1024;
    private const int MaximumApiBytes = 2 * 1024 * 1024;
    private const int MaximumIndexBytes = 50 * 1024 * 1024;
    private static readonly HttpClient Http = CreateHttpClient();
    private IReadOnlyList<AutoEqProfileIndexEntry>? _cachedIndex;
    private IReadOnlyList<AutoEqProfileIndexEntry>? _cachedOpraIndex;
    private IReadOnlyList<AutoEqProfileIndexEntry>? _cachedCombinedIndex;

    public async Task<IReadOnlyList<AutoEqProfileIndexEntry>> SearchAsync(
        string query,
        int limit = 80,
        CancellationToken cancellationToken = default)
    {
        var index = _cachedCombinedIndex ??= (await GetIndexAsync(cancellationToken).ConfigureAwait(false))
            .Concat(await GetOpraIndexAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Provider, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                entry.Provider.Contains(term, StringComparison.OrdinalIgnoreCase) ||
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

        if (string.Equals(entry.Provider, "OPRA", StringComparison.OrdinalIgnoreCase))
        {
            return ImportInlinePreset(entry);
        }

        var downloadUrl = await FindParametricEqDownloadUrlAsync(entry, cancellationToken).ConfigureAwait(false);
        var text = await GetLimitedStringAsync(
            downloadUrl,
            MaximumProfileBytes,
            "Online EQ profile",
            cancellationToken).ConfigureAwait(false);
        var importedName = BuildImportedProfileName(entry, "AutoEq");
        var parsed = EqualizerApoPresetCodec.Parse(text, importedName);

        return new EqPreset
        {
            Name = importedName,
            Category = "Online",
            SourceName = $"AutoEq / {entry.SourceSummary}",
            Description = $"Downloaded from an online AutoEq source. Source path: {entry.ShortPath}. Imported offline; no device writes were sent.",
            PreampDb = parsed.PreampDb,
            Bands = new ObservableCollection<EqBand>(parsed.Bands.Select(CloneBand))
        };
    }

    private static EqPreset ImportInlinePreset(AutoEqProfileIndexEntry entry)
    {
        if (entry.InlineBands is null || entry.InlineBands.Count == 0)
        {
            throw new FormatException($"Online profile {entry.Name} does not include inline PEQ bands.");
        }

        return new EqPreset
        {
            Name = BuildImportedProfileName(entry, "OPRA"),
            Category = "Online",
            SourceName = entry.SourceSummary,
            Description = $"Downloaded from OPRA, the Open Profiles for Revealing Audio database. Source id: {entry.EncodedRelativePath}.",
            PreampDb = entry.InlinePreampDb ?? 0,
            Bands = new ObservableCollection<EqBand>(entry.InlineBands.Select(CloneBand))
        };
    }

    private async Task<IReadOnlyList<AutoEqProfileIndexEntry>> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (_cachedIndex is not null)
        {
            return _cachedIndex;
        }

        var markdown = await GetLimitedStringAsync(
            ResultsIndexUrl,
            MaximumIndexBytes,
            "AutoEq index",
            cancellationToken).ConfigureAwait(false);
        _cachedIndex = ParseIndex(markdown);
        return _cachedIndex;
    }

    private async Task<IReadOnlyList<AutoEqProfileIndexEntry>> GetOpraIndexAsync(CancellationToken cancellationToken)
    {
        if (_cachedOpraIndex is not null)
        {
            return _cachedOpraIndex;
        }

        var jsonl = await GetLimitedStringAsync(
            OpraDatabaseUrl,
            MaximumIndexBytes,
            "OPRA index",
            cancellationToken).ConfigureAwait(false);
        _cachedOpraIndex = ParseOpraIndex(jsonl);
        return _cachedOpraIndex;
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

    private static IReadOnlyList<AutoEqProfileIndexEntry> ParseOpraIndex(string jsonl)
    {
        var vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var products = new Dictionary<string, OpraProduct>(StringComparer.OrdinalIgnoreCase);
        var eqs = new List<OpraEq>();

        foreach (var line in jsonl.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            var id = root.GetProperty("id").GetString() ?? string.Empty;
            var data = root.GetProperty("data");

            if (string.Equals(type, "vendor", StringComparison.OrdinalIgnoreCase))
            {
                vendors[id] = data.TryGetProperty("name", out var name) ? name.GetString() ?? id : id;
            }
            else if (string.Equals(type, "product", StringComparison.OrdinalIgnoreCase))
            {
                products[id] = new OpraProduct(
                    data.TryGetProperty("name", out var name) ? name.GetString() ?? id : id,
                    data.TryGetProperty("vendor_id", out var vendorId) ? vendorId.GetString() ?? string.Empty : string.Empty);
            }
            else if (string.Equals(type, "eq", StringComparison.OrdinalIgnoreCase) &&
                     data.TryGetProperty("type", out var eqType) &&
                     string.Equals(eqType.GetString(), "parametric_eq", StringComparison.OrdinalIgnoreCase) &&
                     data.TryGetProperty("parameters", out var parameters) &&
                     parameters.TryGetProperty("bands", out var bandsElement))
            {
                var bands = ParseOpraBands(bandsElement);
                if (bands.Count == 0)
                {
                    continue;
                }

                eqs.Add(new OpraEq(
                    id,
                    data.TryGetProperty("product_id", out var productId) ? productId.GetString() ?? string.Empty : string.Empty,
                    data.TryGetProperty("author", out var author) ? author.GetString() ?? "OPRA" : "OPRA",
                    data.TryGetProperty("details", out var details) ? details.GetString() ?? string.Empty : string.Empty,
                    parameters.TryGetProperty("gain_db", out var gain) ? gain.GetDouble() : 0,
                    bands));
            }
        }

        return eqs
            .Select(eq =>
            {
                products.TryGetValue(eq.ProductId, out var product);
                var vendorName = product is not null && vendors.TryGetValue(product.VendorId, out var vendor)
                    ? vendor
                    : string.Empty;
                var productName = product?.Name ?? eq.ProductId;
                var name = string.IsNullOrWhiteSpace(vendorName)
                    ? productName
                    : $"{vendorName} {productName}";
                return new AutoEqProfileIndexEntry(
                    name,
                    $"OPRA / {eq.Author}",
                    eq.Details,
                    eq.Id,
                    OpraGitHubUrl,
                    "OPRA",
                    eq.PreampDb,
                    eq.Bands);
            })
            .ToList();
    }

    private static IReadOnlyList<EqBand> ParseOpraBands(JsonElement bandsElement)
    {
        var bands = new List<EqBand>();
        var index = 1;
        foreach (var bandElement in bandsElement.EnumerateArray())
        {
            if (!bandElement.TryGetProperty("frequency", out var frequency) ||
                !bandElement.TryGetProperty("gain_db", out var gain) ||
                !bandElement.TryGetProperty("q", out var q))
            {
                continue;
            }

            bands.Add(new EqBand
            {
                Number = index++,
                Enabled = true,
                FilterType = ParseOpraFilterType(bandElement.TryGetProperty("type", out var type) ? type.GetString() : null),
                FrequencyHz = (int)Math.Round(frequency.GetDouble()),
                GainDb = gain.GetDouble(),
                Q = q.GetDouble()
            });
        }

        return bands;
    }

    private static EqFilterType ParseOpraFilterType(string? type)
        => type?.ToLowerInvariant() switch
        {
            "low_shelf" => EqFilterType.LowShelf,
            "high_shelf" => EqFilterType.HighShelf,
            "low_pass" => EqFilterType.LowPass,
            "high_pass" => EqFilterType.HighPass,
            "band_pass" => EqFilterType.BandPass,
            "all_pass" => EqFilterType.AllPass,
            _ => EqFilterType.Peak
        };

    private static string BuildImportedProfileName(AutoEqProfileIndexEntry entry, string provider)
    {
        var source = entry.SourceSummary;
        return string.IsNullOrWhiteSpace(source)
            ? $"{provider} - {entry.Name}"
            : $"{provider} - {entry.Name} ({source})";
    }

    private static async Task<string> FindParametricEqDownloadUrlAsync(
        AutoEqProfileIndexEntry entry,
        CancellationToken cancellationToken)
    {
        var apiUrl = $"{ContentsApiRoot}{entry.EncodedRelativePath}?ref={Branch}";
        var json = await GetLimitedStringAsync(
            apiUrl,
            MaximumApiBytes,
            "GitHub contents response",
            cancellationToken).ConfigureAwait(false);
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

        if (!Uri.TryCreate(parametric.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || downloadUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(downloadUri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || !downloadUri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("AutoEq returned an untrusted profile download URL.");
        }

        return downloadUri.AbsoluteUri;
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

    private static async Task<string> GetLimitedStringAsync(
        string url,
        int maximumBytes,
        string contentLabel,
        CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is not { Scheme: "https" } finalUri
            || finalUri.Host is not ("api.github.com" or "raw.githubusercontent.com"))
        {
            throw new InvalidOperationException($"{contentLabel} redirected to an untrusted host.");
        }

        if (response.Content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new InvalidOperationException($"{contentLabel} exceeded the configured safety limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > maximumBytes)
            {
                throw new InvalidOperationException($"{contentLabel} exceeded the configured safety limit.");
            }

            memory.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
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

    private sealed record OpraProduct(string Name, string VendorId);

    private sealed record OpraEq(
        string Id,
        string ProductId,
        string Author,
        string Details,
        double PreampDb,
        IReadOnlyList<EqBand> Bands);
}
