using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WolfEQ.Services;

// Read-only HID diagnostic. Lists every HID interface of a supported vendor with its usage and report
// lengths, and issues GET-feature reads (never SET, never output reports) on feature-only interfaces.
// Built so users with unrecognised devices (e.g. K17 R2R, which reports in/out/feature 0/0/65) can send
// one report that shows what the device really exposes.
public sealed partial class FiioK13DeviceService
{
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, byte[] reportBuffer, int reportBufferLength);

    private static readonly byte[] DiagnosticFeatureReportIds = [0x00, 0x07, 0x01];

    public static string BuildHidDiagnosticReport(string appVersion, CancellationToken cancellationToken = default)
    {
        var report = new StringBuilder();
        report.AppendLine("WolfEQ device diagnostic (read-only: HID metadata + GET-feature reads, nothing is written to the device)");
        report.AppendLine($"WolfEQ {appVersion} | {Environment.OSVersion} | {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine();

        try
        {
            HidD_GetHidGuid(out var hidGuid);
            var deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (deviceInfoSet == InvalidHandleValue)
            {
                report.AppendLine(GetWin32ErrorMessage("Unable to enumerate HID devices"));
                return report.ToString();
            }

            var scanned = 0;
            var listed = 0;
            try
            {
                for (uint index = 0; ; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var interfaceData = new SpDeviceInterfaceData { CbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                    if (!SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref interfaceData))
                    {
                        break;
                    }

                    scanned++;
                    var devicePath = GetDevicePath(deviceInfoSet, ref interfaceData);

                    // Same rule as the normal scan: do not open HID devices from unrelated vendors.
                    if (ParseHexId(devicePath, "vid") is not null && !PathHasSupportedHidDeviceId(devicePath))
                    {
                        continue;
                    }

                    var device = ReadHidMetadata(devicePath);
                    if (!IsSupportedHidDevice(device) && !PathHasSupportedHidDeviceId(devicePath))
                    {
                        continue;
                    }

                    listed++;
                    AppendCandidate(report, listed, device);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }

            report.AppendLine();
            report.AppendLine($"Scanned {scanned} HID interface(s); {listed} from supported vendors.");
        }
        catch (Exception ex)
        {
            report.AppendLine($"Diagnostic error: {ex.GetType().Name}: {ex.Message}");
        }

        report.AppendLine();
        report.AppendLine("USB serial ports (listed only, never opened):");
        var ports = FindUsbSerialPorts();
        foreach (var port in ports)
        {
            report.AppendLine($"    {port.PortName}: {port.VidPid} {port.FriendlyName}{(port.LooksLikeFiioDsp ? " <- possible FiiO DSP port" : "")}");
        }

        if (ports.Count == 0) report.AppendLine("    none");
        return report.ToString();
    }

    private static void AppendCandidate(StringBuilder report, int number, HidDeviceCandidate device)
    {
        report.AppendLine($"[{number}] {device.VendorProductDisplay} {device.InterfaceDisplay} rev 0x{(device.VersionNumber ?? 0):X4}");
        report.AppendLine($"    name: {device.Manufacturer ?? "?"} / {device.Product ?? "?"}");
        report.AppendLine($"    {device.UsageDisplay}, {device.ReportLengthDisplay}");
        report.AppendLine($"    path: {device.DevicePath}");
        if (!string.IsNullOrEmpty(device.ReadbackError))
        {
            report.AppendLine($"    metadata error: {device.ReadbackError}");
        }

        if (device.FeatureReportLength is not ushort featureLength || featureLength == 0)
        {
            return;
        }

        try
        {
            using var handle = CreateFile(
                device.DevicePath,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                report.AppendLine($"    feature probe: could not open ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
                return;
            }

            foreach (var reportId in DiagnosticFeatureReportIds)
            {
                var buffer = new byte[featureLength];
                buffer[0] = reportId;
                if (HidD_GetFeature(handle, buffer, buffer.Length))
                {
                    report.AppendLine($"    feature GET id 0x{reportId:X2}: OK {Convert.ToHexString(buffer)}");
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    report.AppendLine($"    feature GET id 0x{reportId:X2}: failed ({error}: {new Win32Exception(error).Message})");
                }
            }
        }
        catch (Exception ex)
        {
            report.AppendLine($"    feature probe error: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
