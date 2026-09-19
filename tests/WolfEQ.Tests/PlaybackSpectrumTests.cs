using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public class PlaybackSpectrumTests
{
    [Theory]
    [InlineData(48000, 60)]
    [InlineData(48000, 1000)]
    [InlineData(96000, 12000)]
    public void SpectrumPeaksAtToneFrequencyAndSurvivesOppositeStereoPhase(int sampleRate, double frequency)
    {
        var analyzer = new PlaybackSpectrumAnalyzer(sampleRate, 2);
        var frame = new float[2];
        for (var i = 0; i < PlaybackSpectrumAnalyzer.Size * 2; i++)
        {
            frame[0] = (float)(0.5 * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
            frame[1] = -frame[0];
            analyzer.AddFrame(frame);
        }
        var peak = Array.IndexOf(analyzer.Levels, analyzer.Levels.Max());
        var peakHz = 20 * Math.Pow(1000, (peak + 0.5) / PlaybackSpectrumAnalyzer.BinCount);
        Assert.InRange(peakHz, frequency * 0.65, frequency * 1.15);
        Assert.InRange(analyzer.Levels.Max(), 0.85f, 0.95f);
        Assert.All(analyzer.Levels, level => Assert.InRange(level, 0, 1));
    }

    [Fact]
    public void SilenceAndNonFiniteSamplesProduceNoSpectrum()
    {
        var analyzer = new PlaybackSpectrumAnalyzer(48000, 2);
        var frame = new[] { float.NaN, float.PositiveInfinity };
        for (var i = 0; i < PlaybackSpectrumAnalyzer.Size; i++) analyzer.AddFrame(frame);
        Assert.All(analyzer.Levels, level => Assert.Equal(0, level));
    }

    [Fact]
    public void FrequenciesBeyondNyquistStayEmpty()
    {
        var analyzer = new PlaybackSpectrumAnalyzer(16000, 1);
        var frame = new float[1];
        for (var i = 0; i < PlaybackSpectrumAnalyzer.Size; i++)
        {
            frame[0] = (float)Math.Sin(2 * Math.PI * 2000 * i / 16000);
            analyzer.AddFrame(frame);
        }
        for (var i = 0; i < analyzer.Levels.Length; i++)
            if (20 * Math.Pow(1000, (double)i / PlaybackSpectrumAnalyzer.BinCount) >= 8000)
                Assert.Equal(0, analyzer.Levels[i]);
    }
}
