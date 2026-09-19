using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WolfEQ.Services;

/// <summary>Read-only Windows output monitoring, using AmpUp's WASAPI loopback approach.</summary>
public sealed class PlaybackSpectrumCapture : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;
    private volatile bool _disposed;
    private WasapiLoopbackCapture? _capture;

    public void Start()
    {
        if (_worker != null || _disposed) return;
        _worker = Task.Run(async () =>
        {
            try
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    EnsureOutput();
                    await Task.Delay(2000, _shutdown.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally { Stop(); _shutdown.Dispose(); }
        });
    }
    private MMDevice? _device;
    private string? _deviceId;
    private readonly object _gate = new();
    private readonly float[] _levels = new float[PlaybackSpectrumAnalyzer.BinCount];
    private long _lastFrame;
    private volatile bool _stopped;
    public string Status { get; private set; } = "Waiting for playback";

    // Called on the capture worker; follows the default multimedia output after device switches.
    public void EnsureOutput()
    {
        if (_disposed) return;
        try
        {
            using var devices = new MMDeviceEnumerator();
            var next = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_capture != null && !_stopped && next.ID == _deviceId) { next.Dispose(); return; }
            Stop();
            _device = next;
            _deviceId = next.ID;
            var capture = new WasapiLoopbackCapture(next);
            _capture = capture;
            var format = capture.WaveFormat is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : capture.WaveFormat;
            if (!(format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32) &&
                !(format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample is 16 or 24 or 32))
                throw new NotSupportedException("This output's audio format is not supported.");
            var analyzer = new PlaybackSpectrumAnalyzer(format.SampleRate, format.Channels);
            var frame = new float[format.Channels];
            var bytes = format.BitsPerSample / 8;
            capture.DataAvailable += (_, e) =>
            {
                if (_disposed || !ReferenceEquals(_capture, capture)) return;
                for (var offset = 0; offset + format.BlockAlign <= e.BytesRecorded; offset += format.BlockAlign)
                {
                    for (var ch = 0; ch < frame.Length; ch++)
                    {
                        var at = offset + ch * bytes;
                        frame[ch] = format.Encoding == WaveFormatEncoding.IeeeFloat ? BitConverter.ToSingle(e.Buffer, at) : bytes switch
                        {
                            2 => BitConverter.ToInt16(e.Buffer, at) / 32768f,
                            3 => ((e.Buffer[at] << 8 | e.Buffer[at + 1] << 16 | e.Buffer[at + 2] << 24) >> 8) / 8388608f,
                            _ => BitConverter.ToInt32(e.Buffer, at) / 2147483648f
                        };
                    }
                    if (!analyzer.AddFrame(frame)) continue;
                    lock (_gate)
                    {
                        if (_disposed || !ReferenceEquals(_capture, capture)) return;
                        analyzer.Levels.CopyTo(_levels, 0);
                        _lastFrame = Environment.TickCount64;
                    }
                }
            };
            capture.RecordingStopped += (_, _) => { if (ReferenceEquals(_capture, capture)) _stopped = true; };
            _stopped = false;
            if (_disposed) { Stop(); return; }
            capture.StartRecording();
            Status = $"Listening to {next.FriendlyName}";
        }
        catch (Exception ex)
        {
            Stop();
            Status = $"Playback monitor unavailable: {ex.Message}. Retrying automatically.";
        }
    }
    public void CopyLevels(float[] target)
    {
        lock (_gate)
        {
            if (Environment.TickCount64 - _lastFrame > 250) Array.Clear(_levels);
            _levels.CopyTo(target, 0);
        }
    }
    private void Stop()
    {
        var old = _capture;
        _capture = null;
        try { old?.StopRecording(); } catch { }
        try { old?.Dispose(); } catch { }
        _device?.Dispose();
        _device = null;
        _deviceId = null;
        lock (_gate) { Array.Clear(_levels); _lastFrame = 0; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        lock (_gate) Array.Clear(_levels);
        // The worker releases the capture without blocking animation or window transitions.
        if (_worker == null) { Stop(); _shutdown.Dispose(); }
    }
}
