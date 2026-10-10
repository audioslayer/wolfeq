using System.Globalization;
using WolfEQ.Services.SystemEq;

namespace WolfEQ.ViewModels;

/// <summary>
/// Progress shown on the System EQ card while "Install Equalizer APO" downloads, verifies, and
/// waits for the installer, so the click never looks frozen.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _isSystemEqSetupBusy;
    private string _systemEqSetupProgressText = string.Empty;
    private double _systemEqSetupProgressPercent;
    private bool _isSystemEqSetupProgressIndeterminate = true;

    public bool IsSystemEqSetupBusy
    {
        get => _isSystemEqSetupBusy;
        private set => SetField(ref _isSystemEqSetupBusy, value);
    }

    public string SystemEqSetupProgressText
    {
        get => _systemEqSetupProgressText;
        private set => SetField(ref _systemEqSetupProgressText, value);
    }

    public double SystemEqSetupProgressPercent
    {
        get => _systemEqSetupProgressPercent;
        private set => SetField(ref _systemEqSetupProgressPercent, value);
    }

    public bool IsSystemEqSetupProgressIndeterminate
    {
        get => _isSystemEqSetupProgressIndeterminate;
        private set => SetField(ref _isSystemEqSetupProgressIndeterminate, value);
    }

    private async Task<ApoLaunchResult> RunApoInstallerWithProgressAsync()
    {
        if (_systemEq.RunInstallerWithProgress is null)
        {
            return await _systemEq.RunInstaller();
        }

        // Progress<T> is created on the UI thread, so reports land on the dispatcher.
        var progress = new Progress<ApoSetupProgress>(ApplySystemEqSetupProgress);
        ApplySystemEqSetupProgress(new ApoSetupProgress(ApoSetupStage.Verifying));
        IsSystemEqSetupBusy = true;
        try
        {
            return await _systemEq.RunInstallerWithProgress(progress);
        }
        finally
        {
            IsSystemEqSetupBusy = false;
        }
    }

    internal void ApplySystemEqSetupProgress(ApoSetupProgress progress)
    {
        switch (progress.Stage)
        {
            case ApoSetupStage.Downloading when progress.TotalBytes is > 0:
                IsSystemEqSetupProgressIndeterminate = false;
                SystemEqSetupProgressPercent = Math.Clamp(100.0 * progress.BytesReceived / progress.TotalBytes.Value, 0, 100);
                SystemEqSetupProgressText = string.Format(CultureInfo.CurrentCulture,
                    "Downloading Equalizer APO... {0:0.0} of {1:0.0} MB ({2:0}%)",
                    progress.BytesReceived / 1048576.0, progress.TotalBytes.Value / 1048576.0, SystemEqSetupProgressPercent);
                break;
            case ApoSetupStage.Downloading:
                IsSystemEqSetupProgressIndeterminate = true;
                SystemEqSetupProgressText = string.Format(CultureInfo.CurrentCulture,
                    "Downloading Equalizer APO... {0:0.0} MB", progress.BytesReceived / 1048576.0);
                break;
            case ApoSetupStage.WaitingForSetup:
                IsSystemEqSetupProgressIndeterminate = true;
                SystemEqSetupProgressText = "Approve the Windows prompt, then finish Equalizer APO setup. Tick your outputs when it asks.";
                break;
            default:
                IsSystemEqSetupProgressIndeterminate = true;
                SystemEqSetupProgressText = "Checking the Equalizer APO installer...";
                break;
        }
    }
}
