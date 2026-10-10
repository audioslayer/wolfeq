using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using WolfEQ.Services.SystemEq;
using Xunit;

namespace WolfEQ.Tests;

public sealed class ApoProcessLauncherTests
{
    private const string Installer = @"C:\App\redist\EqualizerAPO\EqualizerAPO-x64-1.4.2.exe";
    private const string DeviceSelector = @"C:\Program Files\EqualizerAPO\DeviceSelector.exe";

    [Fact]
    public async Task UacCancel_ReturnsCancelledWithoutThrowing()
    {
        var launcher = new ApoProcessLauncher((_, _) => throw new Win32Exception(1223), _ => true);

        var result = await launcher.RunInstallerAsync(Installer);

        Assert.Equal(ApoLaunchOutcome.Cancelled, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task OtherWin32Error_ReturnsFailed()
    {
        var launcher = new ApoProcessLauncher((_, _) => throw new Win32Exception(5), _ => true);

        var result = await launcher.RunDeviceSelectorAsync(DeviceSelector);

        Assert.Equal(ApoLaunchOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task MissingFile_ReturnsNotFoundWithoutStarting()
    {
        var started = false;
        var launcher = new ApoProcessLauncher((_, _) => { started = true; return Task.FromResult(0); }, _ => false);

        var result = await launcher.RunInstallerAsync(Installer);

        Assert.Equal(ApoLaunchOutcome.NotFound, result.Outcome);
        Assert.False(started);
    }

    [Theory]
    [InlineData(0, ApoLaunchOutcome.Completed)]
    [InlineData(1, ApoLaunchOutcome.RebootRequired)]
    [InlineData(2, ApoLaunchOutcome.Failed)]
    [InlineData(-1, ApoLaunchOutcome.Failed)]
    public async Task ExitCodes_MapToOutcomes(int exitCode, ApoLaunchOutcome expected)
    {
        var launcher = new ApoProcessLauncher((_, _) => Task.FromResult(exitCode), _ => true);

        Assert.Equal(expected, (await launcher.RunInstallerAsync(Installer)).Outcome);
        Assert.Equal(expected, (await launcher.RunDeviceSelectorAsync(DeviceSelector)).Outcome);
    }

    [Fact]
    public async Task Launch_IsElevatedShellExecuteWithNoArguments()
    {
        ProcessStartInfo? captured = null;
        var launcher = new ApoProcessLauncher((info, _) => { captured = info; return Task.FromResult(0); }, _ => true);

        await launcher.RunDeviceSelectorAsync(DeviceSelector);

        Assert.NotNull(captured);
        Assert.Equal(DeviceSelector, captured!.FileName);
        Assert.True(captured.UseShellExecute);
        Assert.Equal("runas", captured.Verb);
        Assert.Equal(string.Empty, captured.Arguments);
    }

    [Fact]
    public void FindBundledInstaller_PicksHighestVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "wolfeq-apo-launcher-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "redist", "EqualizerAPO");
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var name in new[] { "EqualizerAPO-x64-1.4.2.exe", "EqualizerAPO-x64-1.10.0.exe", "EqualizerAPO-x64-1.3.exe", "EqualizerAPO-x86-9.9.9.exe" })
            {
                File.WriteAllText(Path.Combine(folder, name), string.Empty);
            }

            Assert.Equal(Path.Combine(folder, "EqualizerAPO-x64-1.10.0.exe"), ApoProcessLauncher.FindBundledInstaller(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindBundledInstaller_MissingFolder_ReturnsNull()
    {
        Assert.Null(ApoProcessLauncher.FindBundledInstaller(Path.Combine(Path.GetTempPath(), "wolfeq-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
