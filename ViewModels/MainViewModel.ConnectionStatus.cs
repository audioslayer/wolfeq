using System.ComponentModel;
using WolfEQ.Services.SystemEq;

namespace WolfEQ.ViewModels;

/// <summary>How the connection line under the device name should read: working, needs a look, or quiet.</summary>
public enum ConnectionTone
{
    Active,
    Attention,
    Idle
}

public sealed partial class MainViewModel
{
    /// <summary>
    /// Color tone for <see cref="ConnectionText"/> and the dock's status dot.
    /// Hardware: Active when connected, Idle otherwise. System EQ: Active only when
    /// Equalizer APO is processing at least one ticked output and bypass is off;
    /// Attention when setup is incomplete or a warning is showing; Idle while checking
    /// or bypassed.
    /// </summary>
    public ConnectionTone ConnectionTone
    {
        get
        {
            if (!IsSystemEqSelected)
            {
                return IsDeviceConnected ? ConnectionTone.Active : ConnectionTone.Idle;
            }

            switch (SystemEqState)
            {
                case SystemEqState.NeedsInstall or SystemEqState.NeedsDeviceSelection or SystemEqState.Blocked:
                    return ConnectionTone.Attention;
                case SystemEqState.Checking or SystemEqState.NotSelected:
                    return ConnectionTone.Idle;
            }

            if (SystemEqBypassed)
            {
                return ConnectionTone.Idle;
            }

            if (HasSystemEqWarning || _systemEqApplyError is not null)
            {
                return ConnectionTone.Attention;
            }

            return SystemEqEndpoints.Any(endpoint => endpoint.IsSelected && endpoint.IsApoActive)
                ? ConnectionTone.Active
                : ConnectionTone.Attention;
        }
    }

    /// <summary>Called once from the constructor: re-evaluates the tone whenever its inputs change.</summary>
    private void InitializeConnectionStatus()
    {
        PropertyChanged += OnConnectionStatusInputChanged;
    }

    private void OnConnectionStatusInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionText)
            or nameof(IsDeviceConnected)
            or nameof(SystemEqState)
            or nameof(SystemEqBypassed)
            or nameof(HasSystemEqWarning)
            or nameof(SelectedDeviceProfile))
        {
            OnPropertyChanged(nameof(ConnectionTone));
        }
    }
}
