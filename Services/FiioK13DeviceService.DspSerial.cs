using System.IO;
using System.IO.Ports;
using Microsoft.Win32;
using WolfEQ.Models;

namespace WolfEQ.Services;

/// <summary>A USB serial port present on this PC, with the USB IDs Windows recorded for it.</summary>
internal sealed record UsbSerialPort(string PortName, string VidPid, string FriendlyName)
{
    // The bridge FiiO uses for its DSP port (WCH CH343), FiiO's own vendor ID, or a port that names the device.
    public bool LooksLikeFiioDsp
        => VidPid.StartsWith("VID_1A86&PID_55D3", StringComparison.OrdinalIgnoreCase)
           || VidPid.StartsWith("VID_2972", StringComparison.OrdinalIgnoreCase)
           || new[] { "FIIO", "K17", "K19" }.Any(name => FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase));
}

public sealed partial class FiioK13DeviceService
{
    private async Task<K13EqReadback> ReadDspSerialEqAsync(FiioDeviceProfile profile, CancellationToken cancellationToken)
    {
        var log = new List<string> { $"DSP serial readback: {profile.DisplayName}." };
        var candidate = await RequireDspCandidateAsync(profile, cancellationToken).ConfigureAwait(false);
        return await WithDspPortAsync(profile, log, async link =>
        {
            var preset = await link.ReadPresetAsync().ConfigureAwait(false);
            var bands = await link.ReadBandsAsync().ConfigureAwait(false);
            return new K13EqReadback(candidate, profile, (byte)preset, null, 0, preset != profile.DisabledPresetId,
                bands.Select((band, index) => FiioDspSerialCodec.ToBand(index, band)).ToArray(), log);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<K13PresetSelectResult> SelectDspSerialPresetAsync(FiioDeviceProfile profile, byte presetId, CancellationToken cancellationToken)
    {
        var log = new List<string> { $"DSP serial preset select: {profile.GetPresetDisplayName(presetId)}." };
        var candidate = await RequireDspCandidateAsync(profile, cancellationToken).ConfigureAwait(false);
        return await WithDspPortAsync(profile, log, async link =>
        {
            var before = await link.ReadPresetAsync().ConfigureAwait(false);
            await link.SelectPresetAsync(presetId).ConfigureAwait(false);
            var after = await link.ReadPresetAsync().ConfigureAwait(false);
            return new K13PresetSelectResult(candidate, profile, (byte)before, presetId, (byte)after, after == presetId, log);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<K13PresetWriteResult> SaveDspSerialPresetAsync(FiioDeviceProfile profile, EqPreset preset, byte presetId, CancellationToken cancellationToken)
    {
        var bands = FiioDspSerialCodec.PlanBands(preset, profile);
        var log = new List<string> { $"DSP serial save: {bands.Count} bands to {profile.GetPresetDisplayName(presetId)}. This device has no preamp; none is sent." };
        var candidate = await RequireDspCandidateAsync(profile, cancellationToken).ConfigureAwait(false);
        return await WithDspPortAsync(profile, log, async link =>
        {
            await link.SelectPresetAsync(presetId).ConfigureAwait(false);
            var active = await link.ReadPresetAsync().ConfigureAwait(false);
            if (active != presetId)
            {
                throw new InvalidOperationException($"The device did not switch to {profile.GetPresetDisplayName(presetId)} (active preset {active}). Nothing was saved.");
            }

            // The pacing FiiO's own app uses for a full preset.
            for (var index = 0; index < bands.Count; index++)
            {
                await link.SendAsync(FiioDspSerialCodec.WriteBand(index, bands[index]), $"band {index + 1}").ConfigureAwait(false);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            await link.SendAsync(FiioDspSerialCodec.SaveToPreset(presetId), "save").ConfigureAwait(false);
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);

            var after = await link.ReadBandsAsync().ConfigureAwait(false);
            for (var index = 0; index < bands.Count; index++)
            {
                if (after[index] != bands[index])
                {
                    log.Add($"Band {index + 1} sent {bands[index]}, read back {after[index]}.");
                    throw new K13DspVerificationException($"Band {index + 1} did not read back as saved.", log);
                }
            }

            log.Add($"Saved {preset.Name} and verified all {bands.Count} bands.");
            return new K13PresetWriteResult(candidate, profile, presetId, bands.Count, 0, log);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HidDeviceCandidate> RequireDspCandidateAsync(FiioDeviceProfile profile, CancellationToken cancellationToken)
    {
        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        return SelectReadbackCandidate(detection.Candidates)
            ?? throw new InvalidOperationException($"{profile.DisplayName} was not found over USB. Connect its USB audio cable too.");
    }

    private static async Task<T> WithDspPortAsync<T>(FiioDeviceProfile profile, List<string> log, Func<DspSerialLink, Task<T>> action, CancellationToken cancellationToken)
    {
        var ports = FindUsbSerialPorts();
        foreach (var port in ports) log.Add($"Serial port {port.PortName}: {port.VidPid} {port.FriendlyName}{(port.LooksLikeFiioDsp ? " (trying)" : "")}");
        var matches = ports.Where(port => port.LooksLikeFiioDsp).ToArray();
        if (matches.Length == 0)
        {
            throw new K13DspVerificationException(
                $"{profile.DisplayName} takes its EQ through the rear RS232 USB port. Connect a second USB cable from that port to this PC, then try again.", log);
        }

        if (matches.Length > 1)
        {
            // Never guess: the same USB serial chip is used in other gadgets.
            throw new K13DspVerificationException(
                $"More than one matching serial port was found ({string.Join(", ", matches.Select(port => port.PortName))}). Unplug other USB serial devices, then try again.", log);
        }

        try
        {
            using var link = new DspSerialLink(matches[0].PortName, log, cancellationToken);
            await link.ConnectAsync().ConfigureAwait(false);
            try
            {
                return await action(link).ConfigureAwait(false);
            }
            finally
            {
                await link.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or TimeoutException or InvalidOperationException and not K13DspVerificationException)
        {
            var message = ex is UnauthorizedAccessException
                ? $"{matches[0].PortName} is in use. Close FiiO's DSP app or web control, then try again."
                : ex.Message;
            throw new K13DspVerificationException(message, log, ex);
        }
    }

    internal static IReadOnlyList<UsbSerialPort> FindUsbSerialPorts()
    {
        var present = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<UsbSerialPort>();
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            foreach (var id in usb?.GetSubKeyNames() ?? [])
            {
                using var device = usb!.OpenSubKey(id);
                foreach (var instanceName in device?.GetSubKeyNames() ?? [])
                {
                    using var instance = device!.OpenSubKey(instanceName);
                    using var parameters = instance?.OpenSubKey("Device Parameters");
                    if (parameters?.GetValue("PortName") is string port && present.Contains(port)
                        && IsDevicePresent($@"USB\{id}\{instanceName}"))
                    {
                        found.Add(new(port, id, instance!.GetValue("FriendlyName") as string ?? ""));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Ports without USB details are never opened.
        }

        // List each port once, likely matches first.
        return found.OrderByDescending(port => port.LooksLikeFiioDsp).DistinctBy(port => port.PortName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    [System.Runtime.InteropServices.DllImport("cfgmgr32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint deviceNode, string deviceId, uint flags);

    // Windows remembers unplugged devices and reuses their COM numbers, so an old FiiO entry could name
    // another gadget's port. Only a device that is connected now counts.
    internal static bool IsDevicePresent(string instanceId) => CM_Locate_DevNodeW(out _, instanceId, 0) == 0;

    private sealed class DspSerialLink : IDisposable
    {
        private readonly SerialPort _port;
        private readonly List<string> _log;
        private readonly CancellationToken _cancellation;

        public DspSerialLink(string portName, List<string> log, CancellationToken cancellation)
        {
            _log = log;
            _cancellation = cancellation;
            // DTR and RTS stay off so opening the port cannot reset another device.
            _port = new SerialPort(portName, FiioDspSerialCodec.BaudRate, Parity.None, 8, StopBits.One)
            {
                DtrEnable = false, RtsEnable = false, ReadTimeout = 100, WriteTimeout = 2000
            };
            _port.Open();
            _log.Add($"Opened {portName} at {FiioDspSerialCodec.BaudRate} baud.");
        }

        public async Task ConnectAsync()
        {
            await SendAsync(FiioDspSerialCodec.Connect(), "connect", requireAck: false).ConfigureAwait(false);
            await Task.Delay(1000, _cancellation).ConfigureAwait(false);
            // Only the DSP answers this; nothing else is sent until it does.
            var preset = await ReadPresetAsync().ConfigureAwait(false);
            _log.Add($"DSP answered. Active preset {preset}.");
        }

        public async Task DisconnectAsync()
        {
            try
            {
                Write(FiioDspSerialCodec.Disconnect(), "disconnect");
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                _log.Add($"Disconnect failed: {ex.Message}");
            }
        }

        public async Task SelectPresetAsync(byte presetId)
        {
            await SendAsync(FiioDspSerialCodec.SelectPreset(presetId), "select preset").ConfigureAwait(false);
            await Task.Delay(500, _cancellation).ConfigureAwait(false);
        }

        public Task<int> ReadPresetAsync()
            => QueryAsync(FiioDspSerialCodec.ReadPreset(), "read preset", 3000, frame => FiioDspSerialCodec.ParsePreset(frame) ?? -1, value => value >= 0);

        public Task<IReadOnlyList<FiioDspSerialCodec.BandRecord>> ReadBandsAsync()
            => QueryAsync(FiioDspSerialCodec.ReadBands(), "read bands", 10000, frame => FiioDspSerialCodec.ParseBands(frame) ?? [], value => value.Count > 0);

        public async Task SendAsync(byte[] frame, string label, bool requireAck = true)
        {
            Write(frame, label);
            var received = await ReceiveAsync(requireAck ? 2000 : 500, bytes => bytes.Contains(FiioDspSerialCodec.Ack)).ConfigureAwait(false);
            _log.Add($"RX {label}: {FormatHex(received)}");
            if (requireAck && !received.Contains(FiioDspSerialCodec.Ack))
            {
                throw new TimeoutException($"The device did not confirm {label}. Nothing more was sent.");
            }
        }

        private async Task<T> QueryAsync<T>(byte[] request, string label, int timeoutMs, Func<byte[], T> parse, Func<T, bool> isValid)
        {
            Write(request, label);
            var received = await ReceiveAsync(timeoutMs, bytes => FiioDspSerialCodec.TryTakeFrame(bytes) is not null).ConfigureAwait(false);
            _log.Add($"RX {label}: {FormatHex(received)}");
            var frame = FiioDspSerialCodec.TryTakeFrame(received);
            if (frame is not null && !frame.AsSpan().SequenceEqual(request))
            {
                var value = parse(frame);
                if (isValid(value)) return value;
            }

            throw new TimeoutException("The device's DSP did not answer. Check the RS232 USB cable and that the device is on.");
        }

        private void Write(byte[] frame, string label)
        {
            _cancellation.ThrowIfCancellationRequested();
            _port.DiscardInBuffer();
            _log.Add($"TX {label}: {FormatHex(frame)}");
            _port.Write(frame, 0, frame.Length);
        }

        private async Task<byte[]> ReceiveAsync(int timeoutMs, Func<byte[], bool> isComplete)
        {
            var buffer = new List<byte>();
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                _cancellation.ThrowIfCancellationRequested();
                var available = _port.BytesToRead;
                if (available > 0)
                {
                    var chunk = new byte[available];
                    var read = _port.Read(chunk, 0, available);
                    buffer.AddRange(chunk.Take(read));
                    if (isComplete(buffer.ToArray())) break;
                }
                else
                {
                    await Task.Delay(10, _cancellation).ConfigureAwait(false);
                }
            }

            return buffer.ToArray();
        }

        public void Dispose() => _port.Dispose();
    }
}

/// <summary>A serial DSP failure that carries its transport log, so a user's log shows what happened.</summary>
public sealed class K13DspVerificationException(string message, IReadOnlyList<string> transportLog, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public IReadOnlyList<string> TransportLog { get; } = transportLog;
}
