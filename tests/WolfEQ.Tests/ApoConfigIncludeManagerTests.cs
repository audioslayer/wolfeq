using System.Text;
using WolfEQ.Services.SystemEq;
using Xunit;

namespace WolfEQ.Tests;

public sealed class ApoConfigIncludeManagerTests
{
    private const string BlockCrlf =
        "# >>> WolfEQ (managed) >>>\r\nDevice: all\r\nInclude: WolfEQ.txt\r\nDevice: all\r\n# <<< WolfEQ <<<\r\n";

    private const string BlockLf =
        "# >>> WolfEQ (managed) >>>\nDevice: all\nInclude: WolfEQ.txt\nDevice: all\n# <<< WolfEQ <<<\n";

    private const string PeaceConfig =
        "Include: peace.txt\r\n";

    private const string DefaultApoConfig =
        "Preamp: -6 dB\r\nInclude: example.txt\r\nFilter: ON PK Fc 50 Hz Gain -3.0 dB Q 2.00\r\n";

    [Fact]
    public void EmptyConfigGetsBlockOnly()
    {
        Assert.Equal(BlockCrlf, ApoConfigIncludeManager.EnsureBlock(""));
    }

    [Fact]
    public void PeaceIncludeKeptUntouchedBelowBlock()
    {
        var result = ApoConfigIncludeManager.EnsureBlock(PeaceConfig);

        Assert.Equal(BlockCrlf + PeaceConfig, result);
        Assert.True(ApoConfigIncludeManager.HasBlock(result));
    }

    [Fact]
    public void ExistingBlockIsLeftUnchanged()
    {
        var once = ApoConfigIncludeManager.EnsureBlock(DefaultApoConfig);
        var twice = ApoConfigIncludeManager.EnsureBlock(once);

        Assert.Equal(once, twice);
        Assert.True(ApoConfigIncludeManager.HasBlock(once));
        Assert.False(ApoConfigIncludeManager.HasBlock(DefaultApoConfig));
    }

    [Fact]
    public void BlockNotAtTopIsMovedToTop()
    {
        var text = "Include: peace.txt\r\n" + BlockCrlf + "Preamp: -3 dB\r\n";

        Assert.False(ApoConfigIncludeManager.HasBlock(text));
        Assert.Equal(BlockCrlf + "Include: peace.txt\r\nPreamp: -3 dB\r\n", ApoConfigIncludeManager.EnsureBlock(text));
    }

    [Fact]
    public void HalfBlockWithoutEndMarkerIsRepaired()
    {
        var text = "# >>> WolfEQ (managed) >>>\r\nDevice: all\r\nInclude: peace.txt\r\n";

        var repaired = ApoConfigIncludeManager.EnsureBlock(text);

        Assert.Equal(BlockCrlf + "Include: peace.txt\r\n", repaired);
    }

    [Fact]
    public void OrphanEndMarkerAndBodyAreRepaired()
    {
        var text = "Include: peace.txt\r\nInclude: WolfEQ.txt\r\nDevice: all\r\n# <<< WolfEQ <<<\r\nPreamp: -2 dB\r\n";

        var repaired = ApoConfigIncludeManager.EnsureBlock(text);

        Assert.Equal(BlockCrlf + "Include: peace.txt\r\nPreamp: -2 dB\r\n", repaired);
    }

    [Fact]
    public void DuplicatedBlocksCollapseToOne()
    {
        var text = BlockCrlf + PeaceConfig + BlockCrlf + "Preamp: -1 dB\r\n" + BlockCrlf;

        var repaired = ApoConfigIncludeManager.EnsureBlock(text);

        Assert.Equal(BlockCrlf + PeaceConfig + "Preamp: -1 dB\r\n", repaired);
        Assert.Equal(PeaceConfig + "Preamp: -1 dB\r\n", ApoConfigIncludeManager.RemoveBlock(text));
    }

    [Fact]
    public void UserLinesThatLookLikeBlockBodyAreNotTouchedOutsideMarkers()
    {
        var text = "Device: all\r\nInclude: WolfEQ.txt\r\nInclude: peace.txt\r\n";

        Assert.Equal(BlockCrlf + text, ApoConfigIncludeManager.EnsureBlock(text));
        Assert.Equal(text, ApoConfigIncludeManager.RemoveBlock(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData(PeaceConfig)]
    [InlineData(DefaultApoConfig)]
    [InlineData("Preamp: -3 dB")]
    [InlineData("Preamp: -3 dB\nInclude: peace.txt\n")]
    [InlineData("  # odd   spacing \t\r\n\r\nInclude: peace.txt\r\n\r\n")]
    [InlineData("\uFEFFInclude: peace.txt\r\n")]
    [InlineData("mixed\r\nendings\nhere\rok")]
    public void RemoveRestoresOriginalExactly(string original)
    {
        var ensured = ApoConfigIncludeManager.EnsureBlock(original);

        Assert.True(ApoConfigIncludeManager.HasBlock(ensured));
        Assert.Equal(original, ApoConfigIncludeManager.RemoveBlock(ensured));
        Assert.Equal(original, ApoConfigIncludeManager.RemoveBlock(original));
    }

    [Fact]
    public void CrlfFilesKeepCrlfAndLfFilesKeepLf()
    {
        Assert.Equal(BlockCrlf + DefaultApoConfig, ApoConfigIncludeManager.EnsureBlock(DefaultApoConfig));

        var lf = "Preamp: -3 dB\nInclude: peace.txt\n";
        Assert.Equal(BlockLf + lf, ApoConfigIncludeManager.EnsureBlock(lf));
    }

    [Fact]
    public void ByteOrderMarkStaysFirst()
    {
        var text = "\uFEFFInclude: peace.txt\r\n";

        Assert.Equal("\uFEFF" + BlockCrlf + "Include: peace.txt\r\n", ApoConfigIncludeManager.EnsureBlock(text));
    }

    [Fact]
    public void LfBlockInCrlfFileIsNormalizedToDominantEnding()
    {
        var text = BlockLf + DefaultApoConfig;

        Assert.False(ApoConfigIncludeManager.HasBlock(text));
        Assert.Equal(BlockCrlf + DefaultApoConfig, ApoConfigIncludeManager.EnsureBlock(text));
    }

    [Theory]
    [InlineData(new byte[] { 0x49, 0x6E, 0x63 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x49, 0x6E, 0x63, 0xC3, 0xA9 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x49, 0x00, 0x6E, 0x00 })]
    [InlineData(new byte[] { 0x23, 0x20, 0xE9, 0x74, 0xE9, 0x0D, 0x0A })] // Windows-1252 bytes, invalid UTF-8
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x23, 0x20, 0xE9, 0x74, 0xE9, 0x0D, 0x0A })] // UTF-8 BOM, then invalid UTF-8
    public void DecodeEncodeRoundTripsBytesExactly(byte[] bytes)
    {
        var text = ApoConfigIncludeManager.DecodeConfig(bytes, out var encoding);

        Assert.Equal(bytes, ApoConfigIncludeManager.EncodeConfig(text, encoding));

        var ensured = ApoConfigIncludeManager.EncodeConfig(ApoConfigIncludeManager.EnsureBlock(text), encoding);
        var restored = ApoConfigIncludeManager.RemoveBlock(ApoConfigIncludeManager.DecodeConfig(ensured, out var again));
        Assert.Equal(bytes, ApoConfigIncludeManager.EncodeConfig(restored, again));
    }

    [Fact]
    public void Utf8BomWithInvalidBytesFallsBackToLatin1AndKeepsBom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0x23, 0x20, 0xE9, 0xFF, 0x0D, 0x0A];

        var text = ApoConfigIncludeManager.DecodeConfig(bytes, out var encoding);
        var ensured = ApoConfigIncludeManager.EncodeConfig(ApoConfigIncludeManager.EnsureBlock(text), encoding);

        byte[] expected = [0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes(BlockCrlf), 0x23, 0x20, 0xE9, 0xFF, 0x0D, 0x0A];
        Assert.Equal(ApoConfigIncludeManager.ConfigEncoding.Utf8BomLatin1, encoding);
        Assert.Equal(expected, ensured);
    }

    [Fact]
    public void NewConfigIsUtf8WithoutBom()
    {
        var bytes = ApoConfigIncludeManager.EncodeConfig(
            ApoConfigIncludeManager.EnsureBlock(""), ApoConfigIncludeManager.ConfigEncoding.Utf8);

        Assert.Equal(Encoding.ASCII.GetBytes(BlockCrlf), bytes);
    }

    [Fact]
    public void BlockFromAnOlderWolfEqIsUpgradedInPlace()
    {
        var oldBlock = "# >>> WolfEQ (managed) >>>\r\nDevice: all\r\nInclude: WolfEQ.txt\r\n# <<< WolfEQ <<<\r\nInclude: peace.txt\r\n";

        var upgraded = ApoConfigIncludeManager.EnsureBlock(oldBlock);

        Assert.Equal(BlockCrlf + "Include: peace.txt\r\n", upgraded);
        Assert.Equal("Include: peace.txt\r\n", ApoConfigIncludeManager.RemoveBlock(upgraded));
    }
}
