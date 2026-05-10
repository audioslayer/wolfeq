namespace WolfEQ.Models;

public sealed record AutoEqProfileIndexEntry(
    string Name,
    string SourceName,
    string Measurement,
    string EncodedRelativePath,
    string HtmlUrl)
{
    public string SourceSummary
        => string.IsNullOrWhiteSpace(Measurement)
            ? SourceName
            : $"{SourceName} on {Measurement}";

    public string ShortPath
        => Uri.UnescapeDataString(EncodedRelativePath).Replace('/', '\\');
}
