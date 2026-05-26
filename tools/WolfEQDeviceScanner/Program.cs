using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WolfEQDeviceScanner;

internal static class Program
{
    private static readonly string[] InterestingTerms =
    [
        "nano", "retro", "vsd", "treaslin", "usb audio", "hid", "5548", "1001", "wolfeq"
    ];

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var capture = args.Any(a => a.Equals("--capture", StringComparison.OrdinalIgnoreCase));
        var outputRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "WolfEQDeviceScanner-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outputRoot);

        var log = new ReportWriter(Path.Combine(outputRoot, "WolfEQ-device-report.txt"));
        log.Section("WolfEQ Device Scanner");
        log.Line($"Timestamp: {DateTimeOffset.Now:O}");
        log.Line($"Machine: {Environment.MachineName}");
        log.Line($"User: {Environment.UserName}");
        log.Line($"OS: {RuntimeInformation.OSDescription}");
        log.Line($"Process: {Environment.ProcessPath}");
        log.Line();

        if (!OperatingSystem.IsWindows())
        {
            log.Line("This scanner is intended for Windows test PCs.");
            log.Save();
            Console.WriteLine($"Report written to {log.Path}");
            return 2;
        }

        await CollectPowerShellAsync(outputRoot, log);
        HidScanner.Collect(log, outputRoot);
        await UsbPcapScanner.CollectAsync(log, outputRoot, capture);

        log.Section("What To Send Back");
        log.Line("Send the generated .zip file in this folder back to the WolfEQ debug channel.");
        log.Line("If USBPcap capture is missing, install USBPcap from the Wireshark installer and run again with --capture.");
        log.Save();

        var zipPath = outputRoot + ".zip";
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }
        ZipFile.CreateFromDirectory(outputRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);

        Console.WriteLine();
        Console.WriteLine("WolfEQ scan complete.");
        Console.WriteLine($"Folder: {outputRoot}");
        Console.WriteLine($"Zip:    {zipPath}");
        Console.WriteLine();
        Console.WriteLine("Send the zip back in Discord.");
        return 0;
    }

    private static async Task CollectPowerShellAsync(string outputRoot, ReportWriter log)
    {
        log.Section("Windows Inventory");

        var scripts = new Dictionary<string, string>
        {
            ["pnp-devices.txt"] = "Get-PnpDevice -PresentOnly | Sort-Object Class,FriendlyName | Format-List *",
            ["usb-cim.txt"] = "Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPDeviceID -match 'USB|HID|5548|1001|VID_|PID_' -or $_.Name -match 'Nano|Retro|VSD|TreasLin|USB Audio|HID' } | Sort-Object Name | Format-List *",
            ["sound-devices.txt"] = "Get-CimInstance Win32_SoundDevice | Format-List *",
            ["usb-controllers.txt"] = "Get-CimInstance Win32_USBController,Win32_USBHub | Format-List *",
            ["mmdevices-registry.txt"] = "Get-ChildItem 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\MMDevices\\Audio\\Render' -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.Name; Get-ItemProperty $_.PsPath | Format-List * }",
            ["usb-device-properties.txt"] = "Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match 'USB|HID|5548|1001|VID_|PID_' -or $_.FriendlyName -match 'Nano|Retro|VSD|TreasLin|USB Audio|HID' } | ForEach-Object { '### ' + $_.FriendlyName + ' [' + $_.InstanceId + ']'; Get-PnpDeviceProperty -InstanceId $_.InstanceId -ErrorAction SilentlyContinue | Format-List * }"
        };

        foreach (var (fileName, script) in scripts)
        {
            var path = Path.Combine(outputRoot, fileName);
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var result = await RunProcessAsync("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded, timeoutMs: 60000);
            await File.WriteAllTextAsync(path, result.StdOut + result.StdErr, Encoding.UTF8);
            log.Line($"{fileName}: exit {result.ExitCode}, {new FileInfo(path).Length} bytes");
        }
        log.Line();
    }

    internal static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, int timeoutMs)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new ProcessResult(-1, "", $"Failed to start {fileName}");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();
            var exited = await Task.WhenAny(exitTask, Task.Delay(timeoutMs)) == exitTask;
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return new ProcessResult(-2, await stdoutTask, (await stderrTask) + $"{fileName} timed out.");
            }

            return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.ToString());
        }
    }

    internal readonly record struct ProcessResult(int ExitCode, string StdOut, string StdErr);

    internal sealed class ReportWriter(string path)
    {
        private readonly StringBuilder _builder = new();
        public string Path { get; } = path;
        public void Section(string title)
        {
            _builder.AppendLine();
            _builder.AppendLine("## " + title);
        }
        public void Line(string value = "") => _builder.AppendLine(value);
        public void Save() => File.WriteAllText(Path, _builder.ToString(), Encoding.UTF8);
    }

    private static bool LooksInteresting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return InterestingTerms.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    internal static class UsbPcapScanner
    {
        public static async Task CollectAsync(ReportWriter log, string outputRoot, bool capture)
        {
            log.Section("USBPcap");
            var usbPcap = FindUsbPcapCmd();
            if (usbPcap is null)
            {
                log.Line("USBPcapCMD.exe not found. Install USBPcap through the Wireshark installer, then run with --capture.");
                log.Line();
                return;
            }

            log.Line($"USBPcapCMD: {usbPcap}");
            var list = await RunProcessAsync(usbPcap, "-l", timeoutMs: 30000);
            var listPath = Path.Combine(outputRoot, "usbpcap-list.txt");
            await File.WriteAllTextAsync(listPath, list.StdOut + list.StdErr, Encoding.UTF8);
            log.Line($"usbpcap-list.txt: exit {list.ExitCode}, {new FileInfo(listPath).Length} bytes");

            if (!capture)
            {
                log.Line("Capture not started because --capture was not supplied.");
                log.Line("Run: WolfEQDeviceScanner.exe --capture");
                log.Line();
                return;
            }

            log.Line("Interactive capture requested.");
            Console.WriteLine();
            Console.WriteLine("USBPcap capture mode");
            Console.WriteLine("Open the official Retro Nano app, then press Enter here.");
            Console.ReadLine();
            Console.WriteLine("If USBPcap asks for an interface, choose the USB bus containing the Retro Nano.");
            Console.WriteLine("Change each control once: volume, gain, DRE, limiter, balance.");
            Console.WriteLine("When done, return here and press Enter. The scanner will stop capture.");

            var pcapPath = Path.Combine(outputRoot, "retro-nano-usbpcap.pcapng");
            var psi = new ProcessStartInfo(usbPcap, $"-o \"{pcapPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };

            try
            {
                using var process = Process.Start(psi);
                if (process is null)
                {
                    log.Line("Failed to start USBPcap capture.");
                    return;
                }

                Console.ReadLine();
                try { process.StandardInput.WriteLine(); } catch { }
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }

                log.Line(File.Exists(pcapPath)
                    ? $"Capture saved: {pcapPath}, {new FileInfo(pcapPath).Length} bytes"
                    : "USBPcap capture did not produce a pcapng file.");
            }
            catch (Exception ex)
            {
                log.Line("USBPcap capture failed:");
                log.Line(ex.ToString());
            }
            log.Line();
        }

        private static string? FindUsbPcapCmd()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "USBPcap", "USBPcapCMD.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "USBPcap", "USBPcapCMD.exe"),
                "USBPcapCMD.exe"
            };

            return candidates.FirstOrDefault(File.Exists);
        }
    }

    internal static class HidScanner
    {
        public static void Collect(ReportWriter log, string outputRoot)
        {
            log.Section("HID Interfaces");
            try
            {
                var hidPath = Path.Combine(outputRoot, "hid-interfaces.txt");
                using var writer = new StreamWriter(hidPath, false, Encoding.UTF8);
                foreach (var path in EnumerateHidDevicePaths())
                {
                    AppendDevice(writer, path);
                }
                log.Line($"hid-interfaces.txt: {new FileInfo(hidPath).Length} bytes");
            }
            catch (Exception ex)
            {
                log.Line(ex.ToString());
            }
            log.Line();
        }

        private static void AppendDevice(TextWriter writer, string path)
        {
            writer.WriteLine("================================================================================");
            writer.WriteLine(path);
            using var handle = CreateFile(path, 0x80000000 | 0x40000000, 0x00000001 | 0x00000002, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                writer.WriteLine("Open: failed " + Marshal.GetLastWin32Error());
                using var readHandle = CreateFile(path, 0x80000000, 0x00000001 | 0x00000002, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (readHandle.IsInvalid)
                {
                    writer.WriteLine();
                    return;
                }
                WriteHidDetails(writer, readHandle);
            }
            else
            {
                WriteHidDetails(writer, handle);
            }
            writer.WriteLine();
        }

        private static void WriteHidDetails(TextWriter writer, SafeFileHandle handle)
        {
            var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (HidD_GetAttributes(handle, ref attributes))
            {
                writer.WriteLine($"VID: 0x{attributes.VendorID:X4}");
                writer.WriteLine($"PID: 0x{attributes.ProductID:X4}");
                writer.WriteLine($"Version: 0x{attributes.VersionNumber:X4}");
            }
            WriteString(writer, handle, "Manufacturer", HidD_GetManufacturerString);
            WriteString(writer, handle, "Product", HidD_GetProductString);
            WriteString(writer, handle, "Serial", HidD_GetSerialNumberString);

            if (!HidD_GetPreparsedData(handle, out var preparsed))
            {
                writer.WriteLine("PreparsedData: failed " + Marshal.GetLastWin32Error());
                return;
            }

            try
            {
                if (HidP_GetCaps(preparsed, out var caps) == 0)
                {
                    writer.WriteLine($"UsagePage: 0x{caps.UsagePage:X4}");
                    writer.WriteLine($"Usage: 0x{caps.Usage:X4}");
                    writer.WriteLine($"InputReportByteLength: {caps.InputReportByteLength}");
                    writer.WriteLine($"OutputReportByteLength: {caps.OutputReportByteLength}");
                    writer.WriteLine($"FeatureReportByteLength: {caps.FeatureReportByteLength}");
                    TryReadFeatureReports(writer, handle, caps.FeatureReportByteLength);
                }
                else
                {
                    writer.WriteLine("HidP_GetCaps failed.");
                }
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
        }

        private static void TryReadFeatureReports(TextWriter writer, SafeFileHandle handle, ushort length)
        {
            if (length <= 0 || length > 1024)
            {
                writer.WriteLine("FeatureReportRead: skipped due to unsupported length.");
                return;
            }

            writer.WriteLine("FeatureReportRead: trying report IDs 0..15 (read-only)");
            for (byte reportId = 0; reportId <= 15; reportId++)
            {
                var buffer = new byte[length];
                buffer[0] = reportId;
                if (HidD_GetFeature(handle, buffer, buffer.Length))
                {
                    writer.WriteLine($"Feature[{reportId}]: {Convert.ToHexString(buffer)}");
                }
            }
        }

        private static void WriteString(TextWriter writer, SafeFileHandle handle, string name, HidStringGetter getter)
        {
            var buffer = new byte[512];
            if (getter(handle, buffer, buffer.Length))
            {
                writer.WriteLine($"{name}: {Encoding.Unicode.GetString(buffer).TrimEnd('\0')}");
            }
        }

        private delegate bool HidStringGetter(SafeFileHandle handle, byte[] buffer, int bufferLength);

        private static IEnumerable<string> EnumerateHidDevicePaths()
        {
            HidD_GetHidGuid(out var hidGuid);
            var info = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (info == IntPtr.Zero || info == new IntPtr(-1)) yield break;

            try
            {
                var index = 0u;
                while (true)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, index++, ref data))
                    {
                        yield break;
                    }

                    SetupDiGetDeviceInterfaceDetail(info, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                    var detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(info, ref data, detail, required, out _, IntPtr.Zero))
                        {
                            yield return Marshal.PtrToStringUni(detail + 4) ?? "";
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
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

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

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

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    }
}
