using System.Runtime.InteropServices;

namespace WolfEQ.Services;

/// <summary>
/// Reads the default Windows render format through Core Audio and applies a
/// user-selected format through Windows' policy configuration interface.
/// Writes are always support-checked, verified, and rolled back on failure.
/// </summary>
public sealed class WindowsAudioFormatService
{
    private const int ERender = 0;
    private const int EMultimedia = 1;
    private const int ClsctxAll = 23;
    private const int SharedMode = 0;
    private const ushort WaveFormatIeeeFloatTag = 0x0003;
    private const ushort WaveFormatExtensibleTag = 0xFFFE;
    private const int S_OK = 0;

    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly (int BitDepth, int SampleRate)[] QuickFormats =
    [
        (16, 44100),
        (24, 48000),
        (24, 96000),
        (24, 192000)
    ];

    public WindowsAudioFormatCatalog GetDefaultRenderFormats()
    {
        EnsureWindows();
        return WithDefaultRenderDevice((device, deviceId) =>
        {
            var audioClient = ActivateAudioClient(device);
            IntPtr mixFormatPtr = IntPtr.Zero;

            try
            {
                ThrowIfFailed(audioClient.GetMixFormat(out mixFormatPtr));
                var parsed = ParseFormat(mixFormatPtr);
                var current = ToOption(parsed);
                var options = BuildQuickOptions(audioClient, parsed);
                return new WindowsAudioFormatCatalog(options, current, deviceId);
            }
            finally
            {
                if (mixFormatPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(mixFormatPtr);
                }

                Release(audioClient);
            }
        });
    }

    public WindowsAudioFormatResult SetDefaultRenderFormat(WindowsAudioFormatOption requested)
    {
        EnsureWindows();
        var before = GetDefaultRenderFormats();
        var supported = before.Options.FirstOrDefault(option =>
            option.BitDepth == requested.BitDepth && option.SampleRate == requested.SampleRate)
            ?? throw new InvalidOperationException($"{requested.DisplayName} is not supported by the current default output.");

        if (Matches(before.Current, supported))
        {
            return new WindowsAudioFormatResult(before.DeviceId, supported, Changed: false);
        }

        SetDeviceFormat(supported, before.DeviceId);

        Exception? readbackError = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Thread.Sleep(100 + (attempt * 75));
            try
            {
                var readback = GetDefaultRenderFormats();
                if (!string.Equals(readback.DeviceId, before.DeviceId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The Windows default output changed while the format was being applied.");
                }

                if (Matches(readback.Current, supported))
                {
                    return new WindowsAudioFormatResult(readback.DeviceId, supported, Changed: true);
                }
            }
            catch (Exception ex)
            {
                readbackError = ex;
            }
        }

        var restored = false;
        try
        {
            var current = GetDefaultRenderFormats();
            if (string.Equals(current.DeviceId, before.DeviceId, StringComparison.Ordinal))
            {
                SetDeviceFormat(before.Current, before.DeviceId);
                restored = true;
            }
        }
        catch
        {
            // The error below accurately reports that rollback was not confirmed.
        }

        var detail = readbackError is null ? string.Empty : $" {readbackError.Message}";
        throw new InvalidOperationException(restored
            ? $"Windows did not confirm the requested format; the previous format was restored.{detail}"
            : $"Windows did not confirm the requested format or rollback. Check Windows output properties.{detail}");
    }

    private static List<WindowsAudioFormatOption> BuildQuickOptions(IAudioClient audioClient, ParsedAudioFormat current)
    {
        var options = new List<WindowsAudioFormatOption>();
        foreach (var (bitDepth, sampleRate) in QuickFormats)
        {
            var containers = bitDepth == 24 ? new[] { 24, 32 } : new[] { bitDepth };
            foreach (var containerBits in containers)
            {
                var option = new WindowsAudioFormatOption(
                    bitDepth,
                    sampleRate,
                    current.Channels,
                    current.ChannelMask,
                    containerBits,
                    IsFloat: false);

                if (IsSupported(audioClient, option))
                {
                    options.Add(option);
                    break;
                }
            }
        }

        return options;
    }

    private static void SetDeviceFormat(WindowsAudioFormatOption option, string expectedDeviceId)
    {
        WithDefaultRenderDevice((device, deviceId) =>
        {
            IPolicyConfig? policy = null;
            IAudioClient? audioClient = null;
            IntPtr formatPtr = IntPtr.Zero;
            IntPtr closestFormatPtr = IntPtr.Zero;

            try
            {
                if (!string.Equals(deviceId, expectedDeviceId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The Windows default output changed before the format could be applied.");
                }

                var format = BuildFormat(option);
                formatPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatExtensible>());
                Marshal.StructureToPtr(format, formatPtr, false);

                audioClient = ActivateAudioClient(device);
                if (audioClient.IsFormatSupported(SharedMode, formatPtr, out closestFormatPtr) != S_OK)
                {
                    throw new InvalidOperationException($"{option.DisplayName} is no longer supported by the current default output.");
                }

                var policyType = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"))
                    ?? throw new InvalidOperationException("Windows audio policy service was not found.");
                policy = (IPolicyConfig)(Activator.CreateInstance(policyType)
                    ?? throw new InvalidOperationException("Windows audio policy service could not be started."));
                ThrowIfFailed(policy.SetDeviceFormat(deviceId, formatPtr, formatPtr));
                return true;
            }
            finally
            {
                if (closestFormatPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(closestFormatPtr);
                }

                if (formatPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(formatPtr);
                }

                Release(audioClient);
                Release(policy);
            }
        });
    }

    private static bool IsSupported(IAudioClient audioClient, WindowsAudioFormatOption option)
    {
        IntPtr formatPtr = IntPtr.Zero;
        IntPtr closestFormatPtr = IntPtr.Zero;

        try
        {
            var format = BuildFormat(option);
            formatPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatExtensible>());
            Marshal.StructureToPtr(format, formatPtr, false);
            return audioClient.IsFormatSupported(SharedMode, formatPtr, out closestFormatPtr) == S_OK;
        }
        finally
        {
            if (closestFormatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(closestFormatPtr);
            }

            if (formatPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(formatPtr);
            }
        }
    }

    private static bool Matches(WindowsAudioFormatOption left, WindowsAudioFormatOption right)
        => left.BitDepth == right.BitDepth && left.SampleRate == right.SampleRate;

    private static WaveFormatExtensible BuildFormat(WindowsAudioFormatOption option)
    {
        var bytesPerSample = option.ContainerBitsPerSample / 8;
        var blockAlign = checked((ushort)(option.Channels * bytesPerSample));

        return new WaveFormatExtensible
        {
            FormatTag = WaveFormatExtensibleTag,
            Channels = option.Channels,
            SamplesPerSec = checked((uint)option.SampleRate),
            AvgBytesPerSec = checked((uint)(option.SampleRate * blockAlign)),
            BlockAlign = blockAlign,
            BitsPerSample = checked((ushort)option.ContainerBitsPerSample),
            ExtraSize = 22,
            ValidBitsPerSample = checked((ushort)option.BitDepth),
            ChannelMask = option.ChannelMask == 0 ? DefaultChannelMask(option.Channels) : option.ChannelMask,
            SubFormat = option.IsFloat ? FloatSubFormat : PcmSubFormat
        };
    }

    private static ParsedAudioFormat ParseFormat(IntPtr formatPtr)
    {
        var waveFormat = Marshal.PtrToStructure<WaveFormatEx>(formatPtr);
        var channels = waveFormat.Channels == 0 ? (ushort)2 : waveFormat.Channels;

        if (waveFormat.FormatTag == WaveFormatExtensibleTag && waveFormat.ExtraSize >= 22)
        {
            var extensible = Marshal.PtrToStructure<WaveFormatExtensible>(formatPtr);
            var validBits = extensible.ValidBitsPerSample == 0 ? extensible.BitsPerSample : extensible.ValidBitsPerSample;
            return new ParsedAudioFormat(
                checked((int)validBits),
                checked((int)extensible.BitsPerSample),
                checked((int)extensible.SamplesPerSec),
                extensible.Channels == 0 ? channels : extensible.Channels,
                extensible.ChannelMask == 0 ? DefaultChannelMask(channels) : extensible.ChannelMask,
                extensible.SubFormat == FloatSubFormat);
        }

        return new ParsedAudioFormat(
            checked((int)waveFormat.BitsPerSample),
            checked((int)waveFormat.BitsPerSample),
            checked((int)waveFormat.SamplesPerSec),
            channels,
            DefaultChannelMask(channels),
            waveFormat.FormatTag == WaveFormatIeeeFloatTag);
    }

    private static WindowsAudioFormatOption ToOption(ParsedAudioFormat format)
        => new(format.BitDepth, format.SampleRate, format.Channels, format.ChannelMask, format.ContainerBits, format.IsFloat);

    private static T WithDefaultRenderDevice<T>(Func<IMMDevice, string, T> action)
    {
        object? enumeratorObject = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;

        try
        {
            enumeratorObject = new MMDeviceEnumeratorComObject();
            enumerator = (IMMDeviceEnumerator)enumeratorObject;
            enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out device);
            device.GetId(out var deviceId);
            return action(device, deviceId);
        }
        finally
        {
            Release(device);
            Release(enumerator);
            Release(enumeratorObject);
        }
    }

    private static IAudioClient ActivateAudioClient(IMMDevice device)
    {
        var audioClientId = typeof(IAudioClient).GUID;
        device.Activate(ref audioClientId, ClsctxAll, IntPtr.Zero, out var audioClientPtr);

        try
        {
            return (IAudioClient)Marshal.GetObjectForIUnknown(audioClientPtr);
        }
        finally
        {
            Marshal.Release(audioClientPtr);
        }
    }

    private static uint DefaultChannelMask(ushort channels)
        => channels switch
        {
            1 => 0x4,
            2 => 0x3,
            4 => 0x33,
            6 => 0x3F,
            8 => 0x63F,
            _ => 0x3
        };

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows audio formats are only available on Windows.");
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result != S_OK)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumeratorComObject;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid interfaceId, int classContext, IntPtr activationParameters, out IntPtr interfacePointer);
        void OpenPropertyStore(int accessMode, out IntPtr properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, Guid audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferSize);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint currentPadding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr format);
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatExtensible
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
        public ushort ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }

    private sealed record ParsedAudioFormat(
        int BitDepth,
        int ContainerBits,
        int SampleRate,
        ushort Channels,
        uint ChannelMask,
        bool IsFloat);
}

public sealed record WindowsAudioFormatCatalog(
    IReadOnlyList<WindowsAudioFormatOption> Options,
    WindowsAudioFormatOption Current,
    string DeviceId);

public sealed record WindowsAudioFormatOption(
    int BitDepth,
    int SampleRate,
    ushort Channels,
    uint ChannelMask,
    int ContainerBitsPerSample,
    bool IsFloat)
{
    public string DisplayName => $"{BitDepth}-bit{(IsFloat ? " float" : string.Empty)} / {FormatSampleRate(SampleRate)}";

    private static string FormatSampleRate(int sampleRate)
        => sampleRate % 1000 == 0
            ? $"{sampleRate / 1000} kHz"
            : $"{sampleRate / 1000.0:0.#} kHz";
}

public sealed record WindowsAudioFormatResult(string DeviceId, WindowsAudioFormatOption Format, bool Changed);
