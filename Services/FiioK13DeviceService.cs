using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WolfEQ.Models;

namespace WolfEQ.Services;

public sealed class FiioK13DeviceService
{
    public const int VendorId = 0x2972;
    public const int ExpectedHidInterface = 3;
    public const byte HidReportId = 0x07;
    public const byte HidOutEndpoint = 0x02;
    public const byte HidInEndpoint = 0x83;

    public bool IsConnected { get; private set; }

    public Task<HidDetectionResult> DetectUsbAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => DetectUsb(cancellationToken), cancellationToken);

    public async Task ConnectUsbAsync(CancellationToken cancellationToken = default)
    {
        var result = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        IsConnected = result.IsDetected;
    }

    public async Task<K13UsbProbeSnapshot> ProbeUsbGetRangeAsync(
        byte startCommand = 0x00,
        byte endCommand = 0x40,
        CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string>
        {
            $"USB HID GET probe started. Commands 0x{startCommand:X2}-0x{endCommand:X2}; no SET/save packets will be sent."
        };

        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        var candidate = SelectReadbackCandidate(detection.Candidates);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No readable FiiO K13 HID interface found. Candidates: {detection.Candidates.Count}.");
        }

        var inputReportLength = candidate.InputReportLength ?? 33;
        var outputReportLength = candidate.OutputReportLength ?? 33;

        transportLog.Add(
            $"Opening {candidate.InterfaceDisplay} {candidate.UsageDisplay} with report lengths in/out {inputReportLength}/{outputReportLength}.");

        using var handle = CreateFile(
            candidate.DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open K13 HID probe handle.");
        }

        await using var stream = new FileStream(
            handle,
            FileAccess.ReadWrite,
            Math.Max(inputReportLength, outputReportLength),
            isAsync: true);

        var responses = new List<K13UsbProbeResponse>();

        for (var command = startCommand; command <= endCommand; command++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[]? response;
            try
            {
                response = await TrySendGetProbeAsync(
                    stream,
                    inputReportLength,
                    outputReportLength,
                    (byte)command,
                    [],
                    transportLog,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                transportLog.Add($"USB GET 0x{command:X2} stopped: {ex.Message}");
                break;
            }

            if (response is not null)
            {
                var dataLength = response.Length > 5 ? response[5] : 0;
                var payload = response.Length > 6
                    ? response[6..Math.Min(response.Length, 6 + dataLength)]
                    : [];
                responses.Add(new K13UsbProbeResponse((byte)command, payload, response));
                transportLog.Add($"USB GET 0x{command:X2} RX: {FormatHex(response)} payload={FormatHex(payload)}");
            }

            await Task.Delay(35, cancellationToken).ConfigureAwait(false);

            if (command == byte.MaxValue)
            {
                break;
            }
        }

        transportLog.Add($"USB HID GET probe complete. Responses: {responses.Count}.");
        return new K13UsbProbeSnapshot(candidate, responses, transportLog);
    }

    public async Task<K13EqReadback> ReadCurrentEqAsync(CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string>
        {
            "Readback mode: sending GET packets only. No SET/save commands will be sent."
        };

        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        var candidate = SelectReadbackCandidate(detection.Candidates);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No readable FiiO K13 HID interface found. Candidates: {detection.Candidates.Count}.");
        }

        var inputReportLength = candidate.InputReportLength ?? 33;
        var outputReportLength = candidate.OutputReportLength ?? 33;

        transportLog.Add(
            $"Opening {candidate.InterfaceDisplay} {candidate.UsageDisplay} with report lengths in/out {inputReportLength}/{outputReportLength}.");

        using var handle = CreateFile(
            candidate.DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open K13 HID readback handle.");
        }

        await using var stream = new FileStream(
            handle,
            FileAccess.ReadWrite,
            Math.Max(inputReportLength, outputReportLength),
            isAsync: true);

        var countResponse = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqCount,
            [],
            transportLog,
            cancellationToken).ConfigureAwait(false);

        var bandCount = countResponse.Length > 6 ? countResponse[6] : 10;
        bandCount = (byte)Math.Clamp(bandCount, 1, 10);

        var presetResponse = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqPreset,
            [],
            transportLog,
            cancellationToken).ConfigureAwait(false);
        var presetId = presetResponse.Length > 6 ? presetResponse[6] : (byte)160;

        var globalGainResponse = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqGlobalGain,
            [],
            transportLog,
            cancellationToken).ConfigureAwait(false);
        var globalGainDb = globalGainResponse.Length > 7
            ? ParseSignedTenths(globalGainResponse[6], globalGainResponse[7])
            : 0.0;

        var eqSwitchResponse = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqSwitch,
            [],
            transportLog,
            cancellationToken).ConfigureAwait(false);
        var eqEnabled = eqSwitchResponse.Length > 6 && eqSwitchResponse[6] != 0;

        var presetName = await TryReadPresetNameAsync(
            stream,
            inputReportLength,
            outputReportLength,
            presetId,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        var bands = new List<K13EqBandReadback>();
        for (var index = 0; index < bandCount; index++)
        {
            var response = await SendGetAsync(
                stream,
                inputReportLength,
                outputReportLength,
                CmdEqBandItem,
                [(byte)index],
                transportLog,
                cancellationToken).ConfigureAwait(false);

            if (response.Length < 14)
            {
                throw new InvalidOperationException($"Band {index + 1} response was too short: {FormatHex(response)}");
            }

            bands.Add(new K13EqBandReadback(
                Number: response[6] + 1,
                FrequencyHz: ParseUInt16BigEndian(response[9], response[10]),
                GainDb: ParseSignedTenths(response[7], response[8]),
                Q: ParseHundredths(response[11], response[12]),
                FilterType: response[13]));
        }

        transportLog.Add(
            $"Decoded preset {K13EqReadback.GetPresetDisplayName(presetId, presetName)}, preamp {globalGainDb:+0.0;-0.0;0.0} dB, EQ enabled={eqEnabled}, bands={bands.Count}.");

        return new K13EqReadback(
            candidate,
            presetId,
            presetName,
            globalGainDb,
            eqEnabled,
            bands,
            transportLog);
    }

    public Task SavePresetAsync(EqPreset preset, int slot, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "Hardware writes are disabled. No SET packets were sent; verify read-only HID detection/readback first.");
    }

    public async Task<K13PresetSelectResult> SelectUserPresetAsync(
        byte presetId,
        CancellationToken cancellationToken = default)
    {
        if (presetId is < 160 or > 169)
        {
            throw new InvalidOperationException("Preset select is limited to USER 1 through USER 10.");
        }

        var transportLog = new List<string>
        {
            "Preset select mode: one guarded SET packet to command 0x16, USER slots only. No save command will be sent."
        };

        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        var candidate = SelectReadbackCandidate(detection.Candidates);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No writable FiiO K13 HID interface found. Candidates: {detection.Candidates.Count}.");
        }

        var inputReportLength = candidate.InputReportLength ?? 33;
        var outputReportLength = candidate.OutputReportLength ?? 33;

        transportLog.Add(
            $"Opening {candidate.InterfaceDisplay} {candidate.UsageDisplay} with report lengths in/out {inputReportLength}/{outputReportLength}.");

        using var handle = CreateFile(
            candidate.DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open K13 HID preset-select handle.");
        }

        await using var stream = new FileStream(
            handle,
            FileAccess.ReadWrite,
            Math.Max(inputReportLength, outputReportLength),
            isAsync: true);

        var beforePresetId = await ReadPresetIdAsync(
            stream,
            inputReportLength,
            outputReportLength,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add(
            $"Current preset before select: {K13EqReadback.GetPresetDisplayName(beforePresetId)}.");

        await SendSetOnlyAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqPreset,
            [presetId],
            transportLog,
            cancellationToken).ConfigureAwait(false);

        await Task.Delay(200, cancellationToken).ConfigureAwait(false);

        var afterPresetId = await ReadPresetIdAsync(
            stream,
            inputReportLength,
            outputReportLength,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add(
            $"Requested {K13EqReadback.GetPresetDisplayName(presetId)}. Device readback: {K13EqReadback.GetPresetDisplayName(afterPresetId)}.");

        return new K13PresetSelectResult(
            candidate,
            beforePresetId,
            presetId,
            afterPresetId,
            afterPresetId == presetId,
            transportLog);
    }

    public async Task<K13EqSwitchResult> SetEqEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string>
        {
            $"EQ switch mode: one guarded SET packet to command 0x1A, enabled={enabled}. No save command will be sent."
        };

        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        var candidate = SelectReadbackCandidate(detection.Candidates);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No writable FiiO K13 HID interface found. Candidates: {detection.Candidates.Count}.");
        }

        var inputReportLength = candidate.InputReportLength ?? 33;
        var outputReportLength = candidate.OutputReportLength ?? 33;

        transportLog.Add(
            $"Opening {candidate.InterfaceDisplay} {candidate.UsageDisplay} with report lengths in/out {inputReportLength}/{outputReportLength}.");

        using var handle = CreateFile(
            candidate.DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open K13 HID EQ-switch handle.");
        }

        await using var stream = new FileStream(
            handle,
            FileAccess.ReadWrite,
            Math.Max(inputReportLength, outputReportLength),
            isAsync: true);

        var before = await ReadEqEnabledAsync(
            stream,
            inputReportLength,
            outputReportLength,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add($"EQ switch before write: {before}.");

        await SendSetOnlyAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqSwitch,
            [enabled ? (byte)1 : (byte)0],
            transportLog,
            cancellationToken).ConfigureAwait(false);

        await Task.Delay(200, cancellationToken).ConfigureAwait(false);

        var after = await ReadEqEnabledAsync(
            stream,
            inputReportLength,
            outputReportLength,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add($"Requested EQ enabled={enabled}. Device readback: {after}.");

        return new K13EqSwitchResult(
            candidate,
            before,
            enabled,
            after,
            after == enabled,
            transportLog);
    }

    public async Task<K13GlobalGainWriteResult> SetGlobalGainAsync(
        double gainDb,
        CancellationToken cancellationToken = default)
    {
        if (gainDb is < -24.0 or > 12.0)
        {
            throw new ArgumentOutOfRangeException(nameof(gainDb), "Global gain must be between -24.0 and +12.0 dB.");
        }

        var requested = Math.Round(gainDb, 1);
        var transportLog = new List<string>
        {
            $"Global gain mode: one guarded SET packet to command 0x17, gain={requested:F1} dB. No save command will be sent."
        };

        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        var candidate = SelectReadbackCandidate(detection.Candidates);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No writable FiiO K13 HID interface found. Candidates: {detection.Candidates.Count}.");
        }

        var inputReportLength = candidate.InputReportLength ?? 33;
        var outputReportLength = candidate.OutputReportLength ?? 33;

        transportLog.Add(
            $"Opening {candidate.InterfaceDisplay} {candidate.UsageDisplay} with report lengths in/out {inputReportLength}/{outputReportLength}.");

        using var handle = CreateFile(
            candidate.DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open K13 HID global-gain handle.");
        }

        await using var stream = new FileStream(
            handle,
            FileAccess.ReadWrite,
            Math.Max(inputReportLength, outputReportLength),
            isAsync: true);

        var before = await ReadGlobalGainDbAsync(
            stream,
            inputReportLength,
            outputReportLength,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add($"Global gain before write: {before:F1} dB.");

        var payload = new byte[2];
        WriteInt16BigEndian(payload, 0, (short)Math.Round(requested * 10.0));

        await SendSetOnlyAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqGlobalGain,
            payload,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        await Task.Delay(200, cancellationToken).ConfigureAwait(false);

        var after = await ReadGlobalGainDbAsync(
            stream,
            inputReportLength,
            outputReportLength,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add($"Requested global gain {requested:F1} dB. Device readback: {after:F1} dB.");

        return new K13GlobalGainWriteResult(
            candidate,
            before,
            requested,
            after,
            Math.Abs(after - requested) < 0.05,
            transportLog);
    }

    public async Task<K13EqBandWriteResult> SetBandAsync(
        K13EqBandReadback band,
        CancellationToken cancellationToken = default)
    {
        if (band.Number is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(band), "Band number must be between 1 and 10.");
        }

        var requested = NormalizeBand(band);
        var transportLog = new List<string>
        {
            $"Band write mode: one guarded SET packet to command 0x15, band={requested.Number}. No save command will be sent."
        };

        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        var candidate = SelectReadbackCandidate(detection.Candidates);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No writable FiiO K13 HID interface found. Candidates: {detection.Candidates.Count}.");
        }

        var inputReportLength = candidate.InputReportLength ?? 33;
        var outputReportLength = candidate.OutputReportLength ?? 33;

        transportLog.Add(
            $"Opening {candidate.InterfaceDisplay} {candidate.UsageDisplay} with report lengths in/out {inputReportLength}/{outputReportLength}.");

        using var handle = CreateFile(
            candidate.DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open K13 HID band-write handle.");
        }

        await using var stream = new FileStream(
            handle,
            FileAccess.ReadWrite,
            Math.Max(inputReportLength, outputReportLength),
            isAsync: true);

        var before = await ReadBandAsync(
            stream,
            inputReportLength,
            outputReportLength,
            requested.Number,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add($"Band {requested.Number} before write: {FormatBand(before)}.");

        await SendSetOnlyAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqBandItem,
            EncodeBand(requested),
            transportLog,
            cancellationToken).ConfigureAwait(false);

        await Task.Delay(200, cancellationToken).ConfigureAwait(false);

        var after = await ReadBandAsync(
            stream,
            inputReportLength,
            outputReportLength,
            requested.Number,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add($"Requested band {requested.Number}: {FormatBand(requested)}. Device readback: {FormatBand(after)}.");

        return new K13EqBandWriteResult(
            candidate,
            before,
            requested,
            after,
            BandsMatch(requested, after),
            transportLog);
    }

    public async Task<K13PresetNameWriteResult> RenameUserPresetAsync(
        byte presetId,
        string name,
        CancellationToken cancellationToken = default)
    {
        if (presetId is < 160 or > 169)
        {
            throw new InvalidOperationException("Preset rename is limited to USER 1 through USER 10.");
        }

        var sanitizedName = SanitizePresetName(name);
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            throw new InvalidOperationException("Preset name must contain at least one ASCII letter, number, space, dash, or underscore.");
        }

        var transportLog = new List<string>
        {
            "Preset rename mode: one guarded SET packet to command 0x30, USER slots only."
        };

        var detection = await DetectUsbAsync(cancellationToken).ConfigureAwait(false);
        var candidate = SelectReadbackCandidate(detection.Candidates);

        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"No writable FiiO K13 HID interface found. Candidates: {detection.Candidates.Count}.");
        }

        var inputReportLength = candidate.InputReportLength ?? 33;
        var outputReportLength = candidate.OutputReportLength ?? 33;

        transportLog.Add(
            $"Opening {candidate.InterfaceDisplay} {candidate.UsageDisplay} with report lengths in/out {inputReportLength}/{outputReportLength}.");

        using var handle = CreateFile(
            candidate.DevicePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOverlapped,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to open K13 HID rename handle.");
        }

        await using var stream = new FileStream(
            handle,
            FileAccess.ReadWrite,
            Math.Max(inputReportLength, outputReportLength),
            isAsync: true);

        var data = new byte[1 + sanitizedName.Length];
        data[0] = presetId;
        Encoding.ASCII.GetBytes(sanitizedName, data.AsSpan(1));

        await SendSetOnlyAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdPresetName,
            data,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        var readBackName = await TryReadPresetNameAsync(
            stream,
            inputReportLength,
            outputReportLength,
            presetId,
            transportLog,
            cancellationToken).ConfigureAwait(false);

        transportLog.Add(
            $"Requested USER {presetId - 159} name '{sanitizedName}'. Device readback: '{readBackName ?? "(blank)"}'.");

        return new K13PresetNameWriteResult(
            candidate,
            presetId,
            sanitizedName,
            readBackName,
            transportLog);
    }

    public static byte[] EncodeBand(EqBand band)
    {
        var buffer = new byte[7];
        WriteUInt16BigEndian(buffer, 0, (ushort)band.FrequencyHz);
        WriteInt16BigEndian(buffer, 2, (short)Math.Round(band.GainDb * 10));
        WriteUInt16BigEndian(buffer, 4, (ushort)Math.Round(band.Q * 100));
        buffer[6] = (byte)band.FilterType;
        return buffer;
    }

    public static byte[] EncodeBand(K13EqBandReadback band)
    {
        var buffer = new byte[8];
        buffer[0] = (byte)(band.Number - 1);
        WriteInt16BigEndian(buffer, 1, (short)Math.Round(band.GainDb * 10));
        WriteUInt16BigEndian(buffer, 3, (ushort)band.FrequencyHz);
        WriteUInt16BigEndian(buffer, 5, (ushort)Math.Round(band.Q * 100));
        buffer[7] = band.FilterType;
        return buffer;
    }

    private static K13EqBandReadback NormalizeBand(K13EqBandReadback band)
        => band with
        {
            FrequencyHz = Math.Clamp(band.FrequencyHz, 20, 20000),
            GainDb = Math.Clamp(Math.Round(band.GainDb, 1), -24.0, 12.0),
            Q = Math.Clamp(Math.Round(band.Q, 2), 0.1, 10.0)
        };

    private static bool BandsMatch(K13EqBandReadback requested, K13EqBandReadback actual)
        => requested.Number == actual.Number
           && requested.FrequencyHz == actual.FrequencyHz
           && Math.Abs(requested.GainDb - actual.GainDb) < 0.05
           && Math.Abs(requested.Q - actual.Q) < 0.005
           && requested.FilterType == actual.FilterType;

    private static string FormatBand(K13EqBandReadback band)
        => $"{band.FilterTypeDisplay} {band.FrequencyHz} Hz {band.GainDb:+0.0;-0.0;0.0} dB Q {band.Q:F2}";

    private HidDetectionResult DetectUsb(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return HidDetectionResult.NotAvailable("Windows HID detection is only available on Windows.");
        }

        HidD_GetHidGuid(out var hidGuid);

        var deviceInfoSet = SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate HID devices.");
        }

        var candidates = new List<HidDeviceCandidate>();
        var scannedDeviceCount = 0;

        try
        {
            for (uint index = 0; ; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var interfaceData = new SpDeviceInterfaceData
                {
                    CbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref interfaceData))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }

                    throw new Win32Exception(error, "Unable to enumerate HID device interface.");
                }

                scannedDeviceCount++;

                var devicePath = GetDevicePath(deviceInfoSet, ref interfaceData);
                var device = ReadHidMetadata(devicePath);

                if (device.VendorId == VendorId || PathHasFiioVendorId(devicePath))
                {
                    candidates.Add(device);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        var result = new HidDetectionResult(scannedDeviceCount, candidates);
        IsConnected = result.IsDetected;
        return result;
    }

    private static string GetDevicePath(IntPtr deviceInfoSet, ref SpDeviceInterfaceData interfaceData)
    {
        SetupDiGetDeviceInterfaceDetail(
            deviceInfoSet,
            ref interfaceData,
            IntPtr.Zero,
            0,
            out var requiredSize,
            IntPtr.Zero);

        var error = Marshal.GetLastWin32Error();
        if (requiredSize == 0 && error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "Unable to get HID device path length.");
        }

        var detailData = Marshal.AllocHGlobal((int)requiredSize);

        try
        {
            Marshal.WriteInt32(detailData, IntPtr.Size == 8 ? 8 : 6);

            if (!SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    detailData,
                    requiredSize,
                    out _,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to get HID device path.");
            }

            return Marshal.PtrToStringUni(IntPtr.Add(detailData, 4)) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(detailData);
        }
    }

    private static HidDeviceCandidate ReadHidMetadata(string devicePath)
    {
        var vendorId = ParseHexId(devicePath, "vid");
        var productId = ParseHexId(devicePath, "pid");
        var interfaceNumber = ParseHexId(devicePath, "mi");
        ushort? versionNumber = null;
        ushort? usagePage = null;
        ushort? usage = null;
        ushort? inputReportLength = null;
        ushort? outputReportLength = null;
        ushort? featureReportLength = null;
        string? manufacturer = null;
        string? product = null;
        string? serialNumber = null;
        string? readbackError = null;

        using var handle = CreateFile(
            devicePath,
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            readbackError = GetWin32ErrorMessage("Unable to open HID metadata handle");
            return new HidDeviceCandidate(
                devicePath,
                vendorId,
                productId,
                versionNumber,
                interfaceNumber,
                manufacturer,
                product,
                serialNumber,
                usagePage,
                usage,
                inputReportLength,
                outputReportLength,
                featureReportLength,
                readbackError);
        }

        var attributes = new HiddAttributes
        {
            Size = Marshal.SizeOf<HiddAttributes>()
        };

        if (HidD_GetAttributes(handle, ref attributes))
        {
            vendorId = attributes.VendorId;
            productId = attributes.ProductId;
            versionNumber = attributes.VersionNumber;
        }
        else
        {
            readbackError = AppendError(readbackError, GetWin32ErrorMessage("Unable to read HID attributes"));
        }

        manufacturer = ReadHidString(handle, HidStringKind.Manufacturer);
        product = ReadHidString(handle, HidStringKind.Product);
        serialNumber = ReadHidString(handle, HidStringKind.SerialNumber);

        if (HidD_GetPreparsedData(handle, out var preparsedData))
        {
            try
            {
                var capsStatus = HidP_GetCaps(preparsedData, out var caps);
                if (capsStatus == HidpStatusSuccess)
                {
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    inputReportLength = caps.InputReportByteLength;
                    outputReportLength = caps.OutputReportByteLength;
                    featureReportLength = caps.FeatureReportByteLength;
                }
                else
                {
                    readbackError = AppendError(readbackError, $"Unable to read HID capabilities: 0x{capsStatus:X8}");
                }
            }
            finally
            {
                HidD_FreePreparsedData(preparsedData);
            }
        }
        else
        {
            readbackError = AppendError(readbackError, GetWin32ErrorMessage("Unable to read HID preparsed data"));
        }

        return new HidDeviceCandidate(
            devicePath,
            vendorId,
            productId,
            versionNumber,
            interfaceNumber,
            manufacturer,
            product,
            serialNumber,
            usagePage,
            usage,
            inputReportLength,
            outputReportLength,
            featureReportLength,
            readbackError);
    }

    private static string? ReadHidString(SafeFileHandle handle, HidStringKind kind)
    {
        var buffer = new byte[256];
        var success = kind switch
        {
            HidStringKind.Manufacturer => HidD_GetManufacturerString(handle, buffer, buffer.Length),
            HidStringKind.Product => HidD_GetProductString(handle, buffer, buffer.Length),
            HidStringKind.SerialNumber => HidD_GetSerialNumberString(handle, buffer, buffer.Length),
            _ => false
        };

        if (!success)
        {
            return null;
        }

        var value = Encoding.Unicode.GetString(buffer);
        var terminatorIndex = value.IndexOf('\0', StringComparison.Ordinal);
        return terminatorIndex >= 0 ? value[..terminatorIndex] : value.TrimEnd('\0');
    }

    private static HidDeviceCandidate? SelectReadbackCandidate(IReadOnlyList<HidDeviceCandidate> candidates)
        => candidates
            .Where(candidate =>
                candidate.VendorId == VendorId &&
                candidate.InterfaceNumber == ExpectedHidInterface &&
                candidate.InputReportLength > 0 &&
                candidate.OutputReportLength > 0)
            .OrderByDescending(candidate => candidate.InputReportLength == 33 && candidate.OutputReportLength == 33)
            .ThenByDescending(candidate => candidate.UsagePage == 0x0001)
            .FirstOrDefault();

    private static async Task<string?> TryReadPresetNameAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        byte presetId,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendGetAsync(
                stream,
                inputReportLength,
                outputReportLength,
                CmdPresetName,
                [presetId],
                transportLog,
                cancellationToken).ConfigureAwait(false);

            if (response.Length <= 7)
            {
                return null;
            }

            var dataLength = response[5];
            var start = 7;
            var end = Math.Min(response.Length, start + Math.Min(8, Math.Max(0, dataLength - 1)));
            var nameBytes = response[start..end]
                .TakeWhile(value => value is not 0x00 and not Stop)
                .ToArray();
            var name = Encoding.UTF8.GetString(nameBytes).Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or Win32Exception or InvalidOperationException)
        {
            transportLog.Add($"Preset name read skipped: {ex.Message}");
            return null;
        }
    }

    private static async Task<byte> ReadPresetIdAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqPreset,
            [],
            transportLog,
            cancellationToken).ConfigureAwait(false);

        return response.Length > 6 ? response[6] : (byte)160;
    }

    private static async Task<bool> ReadEqEnabledAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqSwitch,
            [],
            transportLog,
            cancellationToken).ConfigureAwait(false);

        return response.Length > 6 && response[6] != 0;
    }

    private static async Task<double> ReadGlobalGainDbAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqGlobalGain,
            [],
            transportLog,
            cancellationToken).ConfigureAwait(false);

        return response.Length > 7
            ? ParseSignedTenths(response[6], response[7])
            : 0.0;
    }

    private static async Task<K13EqBandReadback> ReadBandAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        int bandNumber,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var response = await SendGetAsync(
            stream,
            inputReportLength,
            outputReportLength,
            CmdEqBandItem,
            [(byte)(bandNumber - 1)],
            transportLog,
            cancellationToken).ConfigureAwait(false);

        if (response.Length < 14)
        {
            throw new InvalidOperationException($"Band {bandNumber} response was too short: {FormatHex(response)}");
        }

        return new K13EqBandReadback(
            Number: response[6] + 1,
            FrequencyHz: ParseUInt16BigEndian(response[9], response[10]),
            GainDb: ParseSignedTenths(response[7], response[8]),
            Q: ParseHundredths(response[11], response[12]),
            FilterType: response[13]);
    }

    private static async Task<byte[]> SendGetAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        byte command,
        byte[] data,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        await DrainStaleReportsAsync(stream, inputReportLength, transportLog, cancellationToken).ConfigureAwait(false);

        var packet = BuildGetPacket(command, data);
        if (packet.Length + 1 > outputReportLength)
        {
            throw new InvalidOperationException(
                $"GET 0x{command:X2} packet length {packet.Length} does not fit HID output report length {outputReportLength}.");
        }

        var outputReport = new byte[outputReportLength];
        outputReport[0] = HidReportId;
        packet.CopyTo(outputReport.AsSpan(1));

        transportLog.Add($"GET 0x{command:X2} TX: {FormatHex(packet)}");
        await stream.WriteAsync(outputReport, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var rawReport = await ReadReportWithTimeoutAsync(
                stream,
                inputReportLength,
                TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);

            if (rawReport is null)
            {
                transportLog.Add($"GET 0x{command:X2} RX timeout on attempt {attempt}.");
                continue;
            }

            var response = StripReportId(rawReport);
            transportLog.Add($"GET 0x{command:X2} RX: {FormatHex(response)}");

            if (response.Length > 4 && response[0] == GetHead && response[4] == command)
            {
                return response;
            }

            transportLog.Add(
                $"GET 0x{command:X2} ignored non-matching response on attempt {attempt}: {FormatHex(response)}");
        }

        throw new TimeoutException($"No matching response for GET 0x{command:X2}.");
    }

    private static async Task<byte[]?> TrySendGetProbeAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        byte command,
        byte[] data,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        await DrainStaleReportsAsync(stream, inputReportLength, transportLog, cancellationToken).ConfigureAwait(false);

        var packet = BuildGetPacket(command, data);
        if (packet.Length + 1 > outputReportLength)
        {
            transportLog.Add($"GET 0x{command:X2} skipped: packet too long for HID output report.");
            return null;
        }

        var outputReport = new byte[outputReportLength];
        outputReport[0] = HidReportId;
        packet.CopyTo(outputReport.AsSpan(1));

        await stream.WriteAsync(outputReport, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var rawReport = await ReadReportWithTimeoutAsync(
            stream,
            inputReportLength,
            TimeSpan.FromMilliseconds(350),
            cancellationToken).ConfigureAwait(false);

        if (rawReport is null)
        {
            return null;
        }

        var response = StripReportId(rawReport);
        return response.Length > 4 && response[0] == GetHead && response[4] == command
            ? response
            : null;
    }

    private static async Task SendSetOnlyAsync(
        FileStream stream,
        int inputReportLength,
        int outputReportLength,
        byte command,
        byte[] data,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        await DrainStaleReportsAsync(stream, inputReportLength, transportLog, cancellationToken).ConfigureAwait(false);

        var packet = BuildSetPacket(command, data);
        if (packet.Length + 1 > outputReportLength)
        {
            throw new InvalidOperationException(
                $"SET 0x{command:X2} packet length {packet.Length} does not fit HID output report length {outputReportLength}.");
        }

        var outputReport = new byte[outputReportLength];
        outputReport[0] = HidReportId;
        packet.CopyTo(outputReport.AsSpan(1));

        transportLog.Add($"SET 0x{command:X2} TX: {FormatHex(packet)}");
        await stream.WriteAsync(outputReport, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DrainStaleReportsAsync(
        FileStream stream,
        int inputReportLength,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var drained = 0;
        while (true)
        {
            var rawReport = await ReadReportWithTimeoutAsync(
                stream,
                inputReportLength,
                TimeSpan.FromMilliseconds(40),
                cancellationToken).ConfigureAwait(false);

            if (rawReport is null)
            {
                break;
            }

            drained++;
        }

        if (drained > 0)
        {
            transportLog.Add($"Drained {drained} stale HID report(s).");
        }
    }

    private static async Task<byte[]?> ReadReportWithTimeoutAsync(
        FileStream stream,
        int inputReportLength,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[inputReportLength];

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var length = await stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
            return length > 0 ? buffer[..length] : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static byte[] StripReportId(byte[] report)
        => report.Length > 0 && report[0] == HidReportId ? report[1..] : report;

    private static byte[] BuildGetPacket(byte command, byte[] data)
    {
        var packet = new byte[8 + data.Length];
        packet[0] = GetHead;
        packet[1] = GetStart;
        packet[4] = command;
        packet[5] = (byte)data.Length;
        data.CopyTo(packet.AsSpan(6));
        packet[^2] = 0x00;
        packet[^1] = Stop;
        return packet;
    }

    private static byte[] BuildSetPacket(byte command, byte[] data)
    {
        var packet = new byte[8 + data.Length];
        packet[0] = SetHead;
        packet[1] = SetStart;
        packet[4] = command;
        packet[5] = (byte)data.Length;
        data.CopyTo(packet.AsSpan(6));
        packet[^2] = 0x00;
        packet[^1] = Stop;
        return packet;
    }

    private static string SanitizePresetName(string name)
    {
        var sanitized = new string(name
            .Trim()
            .Where(character => character is >= ' ' and <= '~')
            .Take(8)
            .ToArray());

        return sanitized;
    }

    private static string FormatHex(IReadOnlyList<byte> bytes)
        => string.Join(" ", bytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));

    private static double ParseSignedTenths(byte high, byte low)
        => (short)((high << 8) | low) / 10.0;

    private static double ParseHundredths(byte high, byte low)
        => ParseUInt16BigEndian(high, low) / 100.0;

    private static int ParseUInt16BigEndian(byte high, byte low)
        => (high << 8) | low;

    private static ushort? ParseHexId(string devicePath, string key)
    {
        var marker = $"{key}_";
        var markerIndex = devicePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var start = markerIndex + marker.Length;
        var end = start;

        while (end < devicePath.Length && Uri.IsHexDigit(devicePath[end]))
        {
            end++;
        }

        if (end == start)
        {
            return null;
        }

        return ushort.TryParse(
            devicePath[start..end],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static bool PathHasFiioVendorId(string devicePath)
        => devicePath.Contains("vid_2972", StringComparison.OrdinalIgnoreCase);

    private static string GetWin32ErrorMessage(string prefix)
    {
        var error = Marshal.GetLastWin32Error();
        return $"{prefix}: {new Win32Exception(error).Message} (0x{error:X8})";
    }

    private static string? AppendError(string? existing, string next)
        => string.IsNullOrWhiteSpace(existing) ? next : $"{existing}; {next}";

    private static void WriteUInt16BigEndian(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)(value & 0xFF);
    }

    private static void WriteInt16BigEndian(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 1] = (byte)(value & 0xFF);
    }

    private enum HidStringKind
    {
        Manufacturer,
        Product,
        SerialNumber
    }

    private const int DigcfPresent = 0x00000002;
    private const int DigcfDeviceInterface = 0x00000010;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private const int HidpStatusSuccess = 0x00110000;
    private const byte GetHead = 0xBB;
    private const byte GetStart = 0x0B;
    private const byte SetHead = 0xAA;
    private const byte SetStart = 0x0A;
    private const byte Stop = 0xEE;
    private const byte CmdEqBandItem = 0x15;
    private const byte CmdEqPreset = 0x16;
    private const byte CmdEqGlobalGain = 0x17;
    private const byte CmdEqCount = 0x18;
    private const byte CmdEqSwitch = 0x1A;
    private const byte CmdPresetName = 0x30;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetManufacturerString(SafeFileHandle hidDeviceObject, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle hidDeviceObject, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetSerialNumberString(SafeFileHandle hidDeviceObject, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidpCaps capabilities);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int CbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }
}

public sealed record HidDetectionResult(
    int ScannedDeviceCount,
    IReadOnlyList<HidDeviceCandidate> Candidates,
    string? ScanError = null)
{
    public bool IsDetected => ScanError is null && Candidates.Count > 0;
    public bool HasExpectedInterface => Candidates.Any(candidate => candidate.InterfaceNumber == FiioK13DeviceService.ExpectedHidInterface);

    public string StatusMessage
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ScanError))
            {
                return ScanError;
            }

            if (!IsDetected)
            {
                return $"No FiiO K13 HID device found. Scanned {ScannedDeviceCount} HID interface(s).";
            }

            return HasExpectedInterface
                ? $"FiiO VID_2972 detected with HID interface {FiioK13DeviceService.ExpectedHidInterface}. Writes disabled."
                : $"FiiO VID_2972 detected, but HID interface {FiioK13DeviceService.ExpectedHidInterface} was not visible. Writes disabled.";
        }
    }

    public static HidDetectionResult NotAvailable(string message)
        => new(0, [], message);
}

public sealed record HidDeviceCandidate(
    string DevicePath,
    ushort? VendorId,
    ushort? ProductId,
    ushort? VersionNumber,
    ushort? InterfaceNumber,
    string? Manufacturer,
    string? Product,
    string? SerialNumber,
    ushort? UsagePage,
    ushort? Usage,
    ushort? InputReportLength,
    ushort? OutputReportLength,
    ushort? FeatureReportLength,
    string? ReadbackError)
{
    public string DisplayName
        => !string.IsNullOrWhiteSpace(Product)
            ? Product
            : !string.IsNullOrWhiteSpace(Manufacturer)
                ? Manufacturer
                : "Unknown HID device";

    public string VendorProductDisplay
        => $"{FormatHex(VendorId)}:{FormatHex(ProductId)}";

    public string InterfaceDisplay
        => InterfaceNumber is ushort interfaceNumber ? $"MI_{interfaceNumber:X2}" : "MI_??";

    public string UsageDisplay
        => UsagePage is ushort usagePage && Usage is ushort usage
            ? $"usage 0x{usagePage:X4}/0x{usage:X4}"
            : "usage unknown";

    public string ReportLengthDisplay
        => $"reports in/out/feature {InputReportLength?.ToString(CultureInfo.InvariantCulture) ?? "?"}/" +
           $"{OutputReportLength?.ToString(CultureInfo.InvariantCulture) ?? "?"}/" +
           $"{FeatureReportLength?.ToString(CultureInfo.InvariantCulture) ?? "?"}";

    public bool IsExpectedInterface
        => InterfaceNumber == FiioK13DeviceService.ExpectedHidInterface;

    public static HidDeviceCandidate Error(string message)
        => new(string.Empty, null, null, null, null, null, null, null, null, null, null, null, null, message);

    private static string FormatHex(ushort? value)
        => value is ushort number ? $"0x{number:X4}" : "0x????";
}

public sealed record K13EqReadback(
    HidDeviceCandidate Candidate,
    byte PresetId,
    string? PresetName,
    double GlobalGainDb,
    bool EqEnabled,
    IReadOnlyList<K13EqBandReadback> Bands,
    IReadOnlyList<string> TransportLog)
{
    public string PresetDisplayName => GetPresetDisplayName(PresetId, PresetName);

    public static string GetPresetDisplayName(byte presetId, string? presetName = null)
    {
        if (!string.IsNullOrWhiteSpace(presetName))
        {
            return $"{presetName} (0x{presetId:X2})";
        }

        return presetId switch
        {
            240 => "BYPASS (0xF0)",
            0 => "Jazz (0x00)",
            1 => "Pop (0x01)",
            2 => "Rock (0x02)",
            3 => "Dance (0x03)",
            4 => "R&B (0x04)",
            5 => "Classic (0x05)",
            6 => "HipHop (0x06)",
            8 => "Retro (0x08)",
            9 => "sDamp-1 (0x09)",
            10 => "sDamp-2 (0x0A)",
            >= 160 and <= 169 => $"USER {presetId - 159} (0x{presetId:X2})",
            _ => $"Unknown preset 0x{presetId:X2}"
        };
    }
}

public sealed record K13EqBandReadback(
    int Number,
    int FrequencyHz,
    double GainDb,
    double Q,
    byte FilterType)
{
    public string FilterTypeDisplay => FilterType switch
    {
        0 => "Peak",
        1 => "LowShelf",
        2 => "HighShelf",
        3 => "BandPass",
        4 => "LowPass",
        5 => "HighPass",
        6 => "AllPass",
        _ => $"Unknown({FilterType})"
    };
}

public sealed record K13PresetNameWriteResult(
    HidDeviceCandidate Candidate,
    byte PresetId,
    string RequestedName,
    string? ReadBackName,
    IReadOnlyList<string> TransportLog)
{
    public string UserSlotDisplay => $"USER {PresetId - 159}";
}

public sealed record K13PresetSelectResult(
    HidDeviceCandidate Candidate,
    byte BeforePresetId,
    byte RequestedPresetId,
    byte AfterPresetId,
    bool Confirmed,
    IReadOnlyList<string> TransportLog)
{
    public string BeforeDisplay => K13EqReadback.GetPresetDisplayName(BeforePresetId);
    public string RequestedDisplay => K13EqReadback.GetPresetDisplayName(RequestedPresetId);
    public string AfterDisplay => K13EqReadback.GetPresetDisplayName(AfterPresetId);
}

public sealed record K13GlobalGainWriteResult(
    HidDeviceCandidate Candidate,
    double BeforeGainDb,
    double RequestedGainDb,
    double AfterGainDb,
    bool Confirmed,
    IReadOnlyList<string> TransportLog);

public sealed record K13EqBandWriteResult(
    HidDeviceCandidate Candidate,
    K13EqBandReadback BeforeBand,
    K13EqBandReadback RequestedBand,
    K13EqBandReadback AfterBand,
    bool Confirmed,
    IReadOnlyList<string> TransportLog);

public sealed record K13UsbProbeSnapshot(
    HidDeviceCandidate Candidate,
    IReadOnlyList<K13UsbProbeResponse> Responses,
    IReadOnlyList<string> TransportLog);

public sealed record K13UsbProbeResponse(
    byte Command,
    IReadOnlyList<byte> Payload,
    IReadOnlyList<byte> RawResponse)
{
    public string PayloadHex => string.Join(" ", Payload.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
    public string RawHex => string.Join(" ", RawResponse.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
}

public sealed record K13EqSwitchResult(
    HidDeviceCandidate Candidate,
    bool BeforeEnabled,
    bool RequestedEnabled,
    bool AfterEnabled,
    bool Confirmed,
    IReadOnlyList<string> TransportLog);
