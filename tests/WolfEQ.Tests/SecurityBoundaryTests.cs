using System.IO;
using System.Xml;
using WolfEQ.Models;
using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class SecurityBoundaryTests
{
    [Fact]
    public void FiioXmlImport_RejectsDocumentTypeDeclarations()
    {
        const string xml = """
            <!DOCTYPE FiiO_DSP [<!ENTITY injected "unexpected">]>
            <FiiO_DSP model="test"><description>&injected;</description></FiiO_DSP>
            """;

        Assert.Throws<XmlException>(() => FiioDspXmlPresetCodec.Import(xml));
    }

    [Fact]
    public void ApoImport_RejectsOversizedClipboardText()
    {
        var oversized = new string('A', BoundedTextReader.PresetMaxBytes + 1);

        Assert.Throws<FormatException>(() => EqualizerApoPresetCodec.Parse(oversized));
    }

    [Fact]
    public void EqBand_ReplacesNonFiniteValuesWithSafeDefaults()
    {
        var band = new EqBand
        {
            GainDb = double.NaN,
            Q = double.PositiveInfinity
        };

        Assert.Equal(0, band.GainDb);
        Assert.Equal(1, band.Q);
    }

    [Fact]
    public async Task Updater_RejectsUntrustedDownloadBeforeNetworkAccess()
    {
        var update = new AppUpdate(
            "v9.9.9",
            "9.9.9",
            "WolfEQ-Setup-9.9.9.exe",
            "https://example.com/WolfEQ-Setup-9.9.9.exe");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AppUpdateService.DownloadAndInstallAsync(update));
    }

    [Fact]
    public async Task DeviceWrite_RejectsNonFiniteGlobalGainBeforeDeviceAccess()
    {
        var service = new FiioK13DeviceService();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetGlobalGainAsync(double.NaN));
    }

    [Fact]
    public void AtomicWriter_ReplacesContentWithoutLeavingTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WolfEQ.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "profiles.json");
        try
        {
            AtomicFileWriter.WriteAllText(path, "first");
            AtomicFileWriter.WriteAllText(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
