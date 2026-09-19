using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WolfEQ.Services;
using Xunit;

namespace WolfEQ.Tests;

public sealed class StreamDeckScriptTests
{
    [Theory]
    [InlineData(@"C:\Program Files\WolfEQ\WolfEQ.exe", @"\\?\hid#vid_2972&pid_0120&mi_03#a&8890054#{4d1e55b2-f16f-11cf-88cb-001111000030}")]
    [InlineData(@"C:\Wolf's $folder`\WolfEQ.exe", "device with spaces ' $(`test) \"quotes\" \\")]
    public void GeneratedScriptPreservesExecutableAndNativeArguments(string executable, string device)
    {
        // Intercept Start-Process: validate real PowerShell parsing without launching WolfEQ or touching a device.
        var script = "function Start-Process { param([string]$FilePath, [string]$ArgumentList, [string]$WindowStyle, [switch]$Wait) "
            + "[pscustomobject]@{FilePath=$FilePath;Arguments=$ArgumentList;WindowStyle=$WindowStyle;Wait=[bool]$Wait} | ConvertTo-Json -Compress }\n"
            + StreamDeckScriptBuilder.Build(executable, 2, device);
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        Assert.True(process.WaitForExit(10000));
        Assert.Equal(0, process.ExitCode);
        using var result = JsonDocument.Parse(process.StandardOutput.ReadToEnd());
        Assert.Equal(executable, result.RootElement.GetProperty("FilePath").GetString());
        Assert.Equal("Hidden", result.RootElement.GetProperty("WindowStyle").GetString());
        Assert.True(result.RootElement.GetProperty("Wait").GetBoolean());
        var argv = CommandLineToArgvW("WolfEQ.exe " + result.RootElement.GetProperty("Arguments").GetString(), out var count);
        Assert.NotEqual(IntPtr.Zero, argv);
        try
        {
            var args = Enumerable.Range(1, count - 1).Select(index => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, index * IntPtr.Size))!).ToArray();
            var request = AutomationRequest.Parse(args);
            Assert.Equal(2, request.Slot);
            Assert.Equal(device, request.Device);
        }
        finally { LocalFree(argv); }
    }

    [Fact]
    public void MissingDeviceAndInvalidSlotsCannotGenerateScript()
    {
        Assert.Throws<ArgumentException>(() => StreamDeckScriptBuilder.Build("WolfEQ.exe", 1, ""));
        Assert.Throws<ArgumentOutOfRangeException>(() => StreamDeckScriptBuilder.Build("WolfEQ.exe", 11, "device"));
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
