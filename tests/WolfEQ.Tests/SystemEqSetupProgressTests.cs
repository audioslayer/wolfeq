using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.Services.SystemEq;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class SystemEqSetupProgressTests : IDisposable
{
    private readonly SystemEqTestRig _rig = new();

    [Fact]
    public void DownloadProgressShowsMegabytesAndPercent()
    {
        var model = _rig.CreateModel();

        model.ApplySystemEqSetupProgress(new ApoSetupProgress(ApoSetupStage.Downloading, 5 * 1048576, 10 * 1048576));

        Assert.False(model.IsSystemEqSetupProgressIndeterminate);
        Assert.Equal(50, model.SystemEqSetupProgressPercent, 3);
        Assert.Contains("5.0 of 10.0 MB", model.SystemEqSetupProgressText);
        Assert.Contains("50%", model.SystemEqSetupProgressText);
    }

    [Fact]
    public void UnknownSizeAndSetupStagesAreIndeterminate()
    {
        var model = _rig.CreateModel();

        model.ApplySystemEqSetupProgress(new ApoSetupProgress(ApoSetupStage.Downloading, 3 * 1048576));
        Assert.True(model.IsSystemEqSetupProgressIndeterminate);
        Assert.Contains("3.0 MB", model.SystemEqSetupProgressText);

        model.ApplySystemEqSetupProgress(new ApoSetupProgress(ApoSetupStage.WaitingForSetup));
        Assert.True(model.IsSystemEqSetupProgressIndeterminate);
        Assert.Contains("Windows prompt", model.SystemEqSetupProgressText);
    }

    [Fact]
    public async Task InstallShowsBusyUntilTheInstallerFinishes()
    {
        var gate = new TaskCompletionSource<ApoLaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        IProgress<ApoSetupProgress>? reported = null;
        var services = _rig.Services with
        {
            RunInstallerWithProgress = progress =>
            {
                reported = progress;
                return gate.Task;
            }
        };
        _rig.Backend.Status = SystemEqBackendStatus.NotInstalled("Equalizer APO is not installed.");
        var model = new MainViewModel(new LocalPresetLibraryService(_rig.LibraryPath), services, _rig.SettingsPath);
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;

        model.InstallEqualizerApoCommand.Execute(null);

        Assert.True(model.IsSystemEqSetupBusy);
        Assert.NotNull(reported);
        Assert.False(model.InstallEqualizerApoCommand.CanExecute(null));

        gate.SetResult(new ApoLaunchResult(ApoLaunchOutcome.Cancelled, "canceled"));
        for (var i = 0; i < 50 && model.IsSystemEqSetupBusy; i++)
        {
            await Task.Delay(20);
        }

        Assert.False(model.IsSystemEqSetupBusy);
    }

    public void Dispose() => _rig.Dispose();
}
