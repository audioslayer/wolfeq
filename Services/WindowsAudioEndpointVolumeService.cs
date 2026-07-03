using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WolfEQ.Services;

public sealed class WindowsAudioEndpointVolumeService
{
    private const int ClsctxAll = 23;
    private const int StgmRead = 0;
    private static readonly Guid MMDeviceEnumeratorId = Guid.Parse("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IAudioEndpointVolumeId = Guid.Parse("5CDF2C82-841E-4546-9722-0CF74078229A");
    private static readonly PropertyKey DeviceFriendlyNameKey = new(
        Guid.Parse("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    public Task<WindowsEndpointVolumeSnapshot> ReadVolumeAsync(string endpointNameFragment, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var endpoint = OpenEndpointVolume(endpointNameFragment);
            var hr = endpoint.Volume.GetMasterVolumeLevelScalar(out var scalar);
            ThrowIfFailed(hr, "Unable to read Windows endpoint volume.");
            return new WindowsEndpointVolumeSnapshot(endpoint.Name, ToPercent(scalar));
        }, cancellationToken);

    public Task<WindowsEndpointVolumeSnapshot> SetVolumeAsync(string endpointNameFragment, byte volume, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var endpoint = OpenEndpointVolume(endpointNameFragment);
            var scalar = Math.Clamp(volume, (byte)0, (byte)99) / 99.0f;
            var hr = endpoint.Volume.SetMasterVolumeLevelScalar(scalar, Guid.Empty);
            ThrowIfFailed(hr, "Unable to set Windows endpoint volume.");
            hr = endpoint.Volume.GetMasterVolumeLevelScalar(out var readback);
            ThrowIfFailed(hr, "Unable to read Windows endpoint volume after write.");
            return new WindowsEndpointVolumeSnapshot(endpoint.Name, ToPercent(readback));
        }, cancellationToken);

    private static EndpointVolumeHandle OpenEndpointVolume(string endpointNameFragment)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows endpoint volume is only available on Windows.");
        }

        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? selectedDevice = null;
        IAudioEndpointVolume? volume = null;

        try
        {
            enumerator = CreateDeviceEnumerator();
            ThrowIfFailed(enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out collection), "Unable to enumerate Windows audio endpoints.");
            ThrowIfFailed(collection.GetCount(out var count), "Unable to count Windows audio endpoints.");

            var fallbackNames = new List<string>();
            for (uint index = 0; index < count; index++)
            {
                ThrowIfFailed(collection.Item(index, out var device), "Unable to open Windows audio endpoint.");
                var name = GetFriendlyName(device);
                fallbackNames.Add(name);

                if (name.Contains(endpointNameFragment, StringComparison.OrdinalIgnoreCase))
                {
                    selectedDevice = device;
                    break;
                }

                ReleaseCom(device);
            }

            if (selectedDevice is null)
            {
                throw new InvalidOperationException(
                    $"No active Windows playback endpoint matched '{endpointNameFragment}'. Active endpoints: {string.Join(", ", fallbackNames)}");
            }

            var endpointVolumeId = IAudioEndpointVolumeId;
            ThrowIfFailed(selectedDevice.Activate(ref endpointVolumeId, ClsctxAll, IntPtr.Zero, out volume), "Unable to activate Windows endpoint volume.");
            if (volume is null)
            {
                throw new InvalidOperationException("Windows endpoint volume activation returned no interface.");
            }

            return new EndpointVolumeHandle(selectedDevice, volume, GetFriendlyName(selectedDevice));
        }
        catch
        {
            ReleaseCom(volume);
            ReleaseCom(selectedDevice);
            throw;
        }
        finally
        {
            ReleaseCom(collection);
            ReleaseCom(enumerator);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            ThrowIfFailed(device.OpenPropertyStore(StgmRead, out store), "Unable to open Windows endpoint property store.");
            var key = DeviceFriendlyNameKey;
            ThrowIfFailed(store.GetValue(ref key, out var value), "Unable to read Windows endpoint friendly name.");
            try
            {
                return value.Value ?? "(unnamed endpoint)";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            ReleaseCom(store);
        }
    }

    private static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        var type = Type.GetTypeFromCLSID(MMDeviceEnumeratorId)
            ?? throw new InvalidOperationException("Unable to locate the Windows MMDeviceEnumerator COM class.");
        return (IMMDeviceEnumerator)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Unable to create the Windows MMDeviceEnumerator COM class."));
    }

    private static byte ToPercent(float scalar)
        => (byte)Math.Clamp((int)Math.Round(Math.Clamp(scalar, 0, 1) * 99), 0, 99);

    private static void ThrowIfFailed(int hr, string message)
    {
        if (hr < 0)
        {
            throw new Win32Exception(hr, message);
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0C293D4D748")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint deviceIndex, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume endpointVolume);

        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);

        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }

    private enum EDataFlow
    {
        Render = 0
    }

    [Flags]
    private enum DeviceState
    {
        Active = 0x00000001
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid fmtid, int pid)
    {
        public Guid Fmtid = fmtid;
        public int Pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private readonly ushort _valueType;
        private readonly ushort _reserved1;
        private readonly ushort _reserved2;
        private readonly ushort _reserved3;
        private readonly IntPtr _value;

        public string? Value => _valueType == 31 && _value != IntPtr.Zero
            ? Marshal.PtrToStringUni(_value)
            : null;
    }

    private sealed class EndpointVolumeHandle(IMMDevice device, IAudioEndpointVolume volume, string name) : IDisposable
    {
        public IAudioEndpointVolume Volume { get; } = volume;
        public string Name { get; } = name;

        public void Dispose()
        {
            ReleaseCom(Volume);
            ReleaseCom(device);
        }
    }
}

public sealed record WindowsEndpointVolumeSnapshot(string EndpointName, byte Volume);
