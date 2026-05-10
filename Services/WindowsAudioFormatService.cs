using System.Runtime.InteropServices;

namespace WolfEQ.Services;

public sealed class WindowsAudioFormatService
{
    private const int ERender = 0;
    private const int EMultimedia = 1;
    private const int ClsctxAll = 23;
    private const int SharedMode = 0;
    private const ushort WaveFormatPcmTag = 0x0001;
    private const ushort WaveFormatIeeeFloatTag = 0x0003;
    private const ushort WaveFormatExtensibleTag = 0xFFFE;
    private const int S_OK = 0;

    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    public WindowsAudioFormatCatalog GetDefaultRenderFormats()
    {
        return WithDefaultRenderDevice((device, deviceId) =>
        {
            var audioClient = ActivateAudioClient(device);
            IntPtr mixFormatPtr = IntPtr.Zero;

            try
            {
                var mixResult = audioClient.GetMixFormat(out mixFormatPtr);
                ThrowIfFailed(mixResult);
                var current = ParseFormat(mixFormatPtr);
                var options = BuildSupportedOptions(audioClient, current);
                var currentOption = options.FirstOrDefault(option =>
                    option.BitDepth == current.BitDepth &&
                    option.SampleRate == current.SampleRate);

                return new WindowsAudioFormatCatalog(options, currentOption, deviceId);
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

    public WindowsAudioFormatResult SetDefaultRenderFormat(WindowsAudioFormatOption option)
    {
        return WithDefaultRenderDevice((device, deviceId) =>
        {
            var audioClient = ActivateAudioClient(device);
            IPolicyConfig? policy = null;
            IntPtr formatPtr = IntPtr.Zero;
            IntPtr closestFormatPtr = IntPtr.Zero;

            try
            {
                var format = BuildFormat(option);
                formatPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WaveFormatExtensible>());
                Marshal.StructureToPtr(format, formatPtr, false);

                var supportResult = audioClient.IsFormatSupported(SharedMode, formatPtr, out closestFormatPtr);
                if (supportResult != S_OK)
                {
                    throw new InvalidOperationException($"{option.DisplayName} is no longer supported by the default playback device.");
                }

                var policyType = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"))
                    ?? throw new InvalidOperationException("Windows audio policy service was not found.");
                policy = (IPolicyConfig)Activator.CreateInstance(policyType)!;

                var result = policy.SetDeviceFormat(deviceId, formatPtr, formatPtr);
                if (result != S_OK)
                {
                    Marshal.ThrowExceptionForHR(result);
                }

                return new WindowsAudioFormatResult(deviceId, option);
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

                Release(policy);
                Release(audioClient);
            }
        });
    }

    private static List<WindowsAudioFormatOption> BuildSupportedOptions(IAudioClient audioClient, ParsedAudioFormat current)
    {
        var options = new List<WindowsAudioFormatOption>();
        var sampleRates = new[] { current.SampleRate, 44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000 }
            .Where(rate => rate is >= 8000 and <= 384000)
            .Distinct()
            .OrderBy(rate => rate);
        var bitFormats = new[]
        {
            (BitDepth: current.BitDepth, ContainerBits: current.ContainerBits, IsFloat: current.IsFloat),
            (BitDepth: 16, ContainerBits: 16, IsFloat: false),
            (BitDepth: 24, ContainerBits: 24, IsFloat: false),
            (BitDepth: 24, ContainerBits: 32, IsFloat: false),
            (BitDepth: 32, ContainerBits: 32, IsFloat: false),
            (BitDepth: 32, ContainerBits: 32, IsFloat: true)
        }
        .Where(format => format.BitDepth is 16 or 24 or 32)
        .Distinct();

        foreach (var sampleRate in sampleRates)
        {
            foreach (var bitFormat in bitFormats)
            {
                var option = new WindowsAudioFormatOption(
                    bitFormat.BitDepth,
                    sampleRate,
                    current.Channels,
                    current.ChannelMask,
                    bitFormat.ContainerBits,
                    bitFormat.IsFloat);

                if (options.Any(existing => existing.DisplayName == option.DisplayName))
                {
                    continue;
                }

                if (IsSupported(audioClient, option))
                {
                    options.Add(option);
                }
            }
        }

        return options
            .OrderBy(option => option.SampleRate)
            .ThenBy(option => option.BitDepth)
            .ThenBy(option => option.IsFloat)
            .ToList();
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
            var isFloat = extensible.SubFormat == FloatSubFormat;
            var validBits = extensible.ValidBitsPerSample == 0 ? extensible.BitsPerSample : extensible.ValidBitsPerSample;

            return new ParsedAudioFormat(
                checked((int)validBits),
                checked((int)extensible.BitsPerSample),
                checked((int)extensible.SamplesPerSec),
                extensible.Channels == 0 ? channels : extensible.Channels,
                extensible.ChannelMask == 0 ? DefaultChannelMask(channels) : extensible.ChannelMask,
                isFloat);
        }

        return new ParsedAudioFormat(
            checked((int)waveFormat.BitsPerSample),
            checked((int)waveFormat.BitsPerSample),
            checked((int)waveFormat.SamplesPerSec),
            channels,
            DefaultChannelMask(channels),
            waveFormat.FormatTag == WaveFormatIeeeFloatTag);
    }

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
    private sealed class MMDeviceEnumeratorComObject
    {
    }

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
        [PreserveSig]
        int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, Guid audioSessionGuid);

        [PreserveSig]
        int GetBufferSize(out uint bufferSize);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out uint currentPadding);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

        [PreserveSig]
        int GetMixFormat(out IntPtr format);
    }

    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, out IntPtr format);

        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, out IntPtr format);

        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
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
    WindowsAudioFormatOption? Current,
    string DeviceId);

public sealed record WindowsAudioFormatOption(
    int BitDepth,
    int SampleRate,
    ushort Channels,
    uint ChannelMask,
    int ContainerBitsPerSample,
    bool IsFloat)
{
    public string DisplayName => $"{BitDepth}-bit{(IsFloat ? " float" : string.Empty)}, {FormatSampleRate(SampleRate)}";

    private static string FormatSampleRate(int sampleRate)
        => sampleRate % 1000 == 0
            ? $"{sampleRate / 1000} kHz"
            : $"{sampleRate / 1000.0:0.#} kHz";
}

public sealed record WindowsAudioFormatResult(string DeviceId, WindowsAudioFormatOption Format)
{
    public string DisplayText => Format.DisplayName;
}
