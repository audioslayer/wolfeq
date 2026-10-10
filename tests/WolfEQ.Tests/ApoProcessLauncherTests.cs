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
    [InlineData(1, ApoLaunchOutcome.Cancelled)]
    [InlineData(2, ApoLaunchOutcome.Failed)]
    [InlineData(3010, ApoLaunchOutcome.Failed)]
    [InlineData(-1, ApoLaunchOutcome.Failed)]
    public async Task InstallerExitCodes_FollowNsisMeaning(int exitCode, ApoLaunchOutcome expected)
    {
        var launcher = new ApoProcessLauncher((_, _) => Task.FromResult(exitCode), _ => true);

        Assert.Equal(expected, (await launcher.RunInstallerAsync(Installer)).Outcome);
    }

    [Fact]
    public async Task InstallerExitOne_IsCalmCancelNotReboot()
    {
        var launcher = new ApoProcessLauncher((_, _) => Task.FromResult(1), _ => true);

        var result = await launcher.RunInstallerAsync(Installer);

        Assert.Equal(ApoLaunchOutcome.Cancelled, result.Outcome);
        Assert.Equal("Equalizer APO setup was canceled, so nothing changed.", result.Message);
    }

    [Theory]
    [InlineData(0, ApoLaunchOutcome.Completed)]
    [InlineData(1, ApoLaunchOutcome.RebootRequired)]
    [InlineData(2, ApoLaunchOutcome.Failed)]
    [InlineData(-1, ApoLaunchOutcome.Failed)]
    public async Task DeviceSelectorExitCodes_OneMeansReboot(int exitCode, ApoLaunchOutcome expected)
    {
        var launcher = new ApoProcessLauncher((_, _) => Task.FromResult(exitCode), _ => true);

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
    public void FindBundledInstaller_OnlyReturnsThePinnedFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), "wolfeq-apo-launcher-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "redist", "EqualizerAPO");
        try
        {
            Directory.CreateDirectory(folder);
            // Higher-versioned or look-alike files must never be preferred over the pinned, hash-checked one.
            foreach (var name in new[] { "EqualizerAPO-x64-1.10.0.exe", "EqualizerAPO-x64-9.9.exe", "evil.exe" })
            {
                File.WriteAllText(Path.Combine(folder, name), string.Empty);
            }

            Assert.Null(ApoProcessLauncher.FindBundledInstaller(root));

            File.WriteAllText(Path.Combine(folder, ApoProcessLauncher.PinnedInstallerFileName), string.Empty);

            Assert.Equal(
                Path.Combine(folder, ApoProcessLauncher.PinnedInstallerFileName),
                ApoProcessLauncher.FindBundledInstaller(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PinnedInstallerName_MatchesCsprojAndBuildScript()
    {
        var repo = FindRepoRoot();
        var csproj = File.ReadAllText(Path.Combine(repo, "WolfEQ.csproj"));
        var script = File.ReadAllText(Path.Combine(repo, "build-installer.bat"));

        Assert.Contains($"<EqualizerApoInstallerFileName>{ApoProcessLauncher.PinnedInstallerFileName}</EqualizerApoInstallerFileName>", csproj);
        Assert.DoesNotContain(@"redist\EqualizerAPO\*", csproj);
        Assert.Matches(@"set ""APO_VERSION=1\.4\.2""", script);
        Assert.Contains(@"set ""APO_FILE=EqualizerAPO-x64-%APO_VERSION%.exe""", script);
        Assert.Equal("EqualizerAPO-x64-1.4.2.exe", ApoProcessLauncher.PinnedInstallerFileName);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WolfEQ.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("WolfEQ.csproj was not found above the test output folder.");
    }

    [Fact]
    public void FindBundledInstaller_MissingFolder_ReturnsNull()
    {
        Assert.Null(ApoProcessLauncher.FindBundledInstaller(Path.Combine(Path.GetTempPath(), "wolfeq-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
