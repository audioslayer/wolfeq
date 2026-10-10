using System.IO;
using System.Net;
using System.Net.Http;
using WolfEQ.Services.SystemEq;
using Xunit;

namespace WolfEQ.Tests;

public sealed class ApoInstallerDownloaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wolfeq-apo-download-{Guid.NewGuid():N}");

    [Fact]
    public async Task TamperedDownloadIsRejectedAndNotLeftOnDisk()
    {
        var downloader = new ApoInstallerDownloader(
            () => new HttpClient(new StubHandler(HttpStatusCode.OK, "not the real installer"u8.ToArray())), _directory);

        var (path, message) = await downloader.GetVerifiedInstallerAsync();

        Assert.Null(path);
        Assert.Contains("integrity check", message);
        Assert.False(File.Exists(downloader.CachedInstallerPath));
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task HttpFailureReturnsPlainMessage()
    {
        var downloader = new ApoInstallerDownloader(
            () => new HttpClient(new StubHandler(HttpStatusCode.NotFound, [])), _directory);

        var (path, message) = await downloader.GetVerifiedInstallerAsync();

        Assert.Null(path);
        Assert.StartsWith("Couldn't download Equalizer APO", message);
    }

    [Fact]
    public async Task UnverifiedCachedFileIsNotTrusted()
    {
        Directory.CreateDirectory(_directory);
        var downloader = new ApoInstallerDownloader(
            () => new HttpClient(new StubHandler(HttpStatusCode.NotFound, [])), _directory);
        await File.WriteAllTextAsync(downloader.CachedInstallerPath, "planted");

        Assert.False(await ApoInstallerDownloader.MatchesPinnedHashAsync(downloader.CachedInstallerPath));
        var (path, _) = await downloader.GetVerifiedInstallerAsync();
        Assert.Null(path);
    }

    [Fact]
    public void PinnedHashMatchesBuildScript()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "build-installer.bat")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        Assert.Contains(ApoInstallerDownloader.PinnedSha256, File.ReadAllText(Path.Combine(root!, "build-installer.bat")), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}
