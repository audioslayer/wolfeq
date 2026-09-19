using NAudio.Dsp;

namespace WolfEQ.Services;

/// <summary>Windowed, overlapping FFT with channel powers combined (no stereo phase cancellation).</summary>
public sealed class PlaybackSpectrumAnalyzer
{
    public const int Size = 4096;
    public const int BinCount = 120;
    private static readonly float[] Hann = Enumerable.Range(0, Size)
        .Select(i => 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (Size - 1))).ToArray();
    private readonly (int First, int Last)[] _ranges = new (int, int)[BinCount];
    private readonly int _hop;
    private readonly float[][] _samples;
    private readonly Complex[] _fft = new Complex[Size];
    private readonly double[] _power = new double[Size / 2];
    private int _position;
    public float[] Levels { get; } = new float[BinCount];
    public PlaybackSpectrumAnalyzer(int sampleRate, int channels)
    {
        if (sampleRate <= 0 || channels < 1 || channels > 32) throw new ArgumentOutOfRangeException();
        _hop = Math.Clamp(sampleRate / 30, 1, Size / 2);
        for (var i = 0; i < BinCount; i++)
        {
            var low = 20 * Math.Pow(1000, (double)i / BinCount);
            var high = 20 * Math.Pow(1000, (double)(i + 1) / BinCount);
            if (low >= sampleRate / 2.0) { _ranges[i] = (1, 0); continue; }
            var first = Math.Clamp((int)Math.Floor(low * Size / sampleRate), 1, _power.Length - 1);
            _ranges[i] = (first, Math.Clamp((int)Math.Ceiling(high * Size / sampleRate), first, _power.Length - 1));
        }
        _samples = Enumerable.Range(0, channels).Select(_ => new float[Size]).ToArray();
    }
    public bool AddFrame(ReadOnlySpan<float> frame)
    {
        if (frame.Length != _samples.Length) throw new ArgumentException("Channel count mismatch.");
        for (var ch = 0; ch < frame.Length; ch++) _samples[ch][_position] = float.IsFinite(frame[ch]) ? frame[ch] : 0;
        if (++_position < Size) return false;
        Array.Clear(_power);
        foreach (var channel in _samples)
        {
            for (var i = 0; i < Size; i++)
            {
                _fft[i].X = channel[i] * Hann[i];
                _fft[i].Y = 0;
            }
            // Silence needs no transform, but still publishes zeroes to let the display decay.
            if (channel.Any(value => value != 0)) FastFourierTransform.FFT(true, 12, _fft);
            for (var i = 1; i < _power.Length; i++) _power[i] += (_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y) * 16.0 / _samples.Length;
            Array.Copy(channel, _hop, channel, 0, Size - _hop);
        }
        for (var i = 0; i < BinCount; i++)
        {
            var (first, last) = _ranges[i];
            double peak = 0;
            for (var bin = first; bin <= last; bin++) peak = Math.Max(peak, _power[bin]);
            Levels[i] = (float)Math.Clamp((10 * Math.Log10(Math.Max(1e-12, peak)) + 80) / 80, 0, 1);
        }
        _position = Size - _hop;
        return true;
    }
}
