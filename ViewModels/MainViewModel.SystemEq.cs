using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.Services.SystemEq;

namespace WolfEQ.ViewModels;

/// <summary>
/// Windows System EQ mode: the editor drives Equalizer APO through <see cref="IEqOutputBackend"/>.
/// This pipeline is separate from FiiO live sync (KTD8).
/// </summary>
public sealed partial class MainViewModel
{
    private const int SystemEqApplyDebounceMilliseconds = 120;

    private SystemEqServices _systemEq = null!;
    private SystemEqSettings _systemEqSettings = null!;
    private DispatcherTimer _systemEqApplyTimer = null!;
    private ObservableCollection<EqBand>? _systemEqWatchedBands;
    private readonly HashSet<EqBand> _systemEqHookedBands = [];
    private SystemEqState _systemEqState = SystemEqState.NotSelected;
    private SystemEqBackendStatus? _systemEqBackendStatus;
    private bool _isEqualizerApoInstalled;
    private bool _systemEqApplyPending;
    private bool _systemEqApplyInFlight;
    private bool _systemEqApplyHalted;
    private bool _systemEqRebootPending;
    private bool _isLeavingSystemEqForRemoval;
    private bool _isRebuildingSystemEqEndpoints;
    private string? _systemEqNotice;
    private string? _systemEqApplyError;
    private string? _systemEqBlockedMessage;
    private Task _systemEqRefreshTask = Task.CompletedTask;
    private string _systemEqStatusText = string.Empty;
    private string _systemEqWarningText = string.Empty;

    public bool IsSystemEqSelected => SystemEqProfiles.IsSystemEq(SelectedDeviceProfile);
    public bool IsHardwareDeviceSelected => !IsSystemEqSelected;

    public ObservableCollection<SystemEqEndpointOption> SystemEqEndpoints { get; } = [];

    public bool SystemEqBypassed
    {
        get => _systemEqSettings.Bypassed;
        set
        {
            if (_systemEqSettings.Bypassed == value)
            {
                return;
            }

            _systemEqSettings.Bypassed = value;
            SaveSystemEqSettings();
            OnPropertyChanged();
            UpdateSystemEqStatus();
            QueueSystemEqApply();
        }
    }

    public SystemEqState SystemEqState
    {
        get => _systemEqState;
        private set
        {
            if (SetField(ref _systemEqState, value))
            {
                UpdateSystemEqStatus();
            }
        }
    }

    public bool IsEqualizerApoInstalled
    {
        get => _isEqualizerApoInstalled;
        private set => SetField(ref _isEqualizerApoInstalled, value);
    }

    public string SystemEqStatusText
    {
        get => _systemEqStatusText;
        private set => SetField(ref _systemEqStatusText, value);
    }

    public string SystemEqWarningText
    {
        get => _systemEqWarningText;
        private set
        {
            if (SetField(ref _systemEqWarningText, value))
            {
                OnPropertyChanged(nameof(HasSystemEqWarning));
            }
        }
    }

    public bool HasSystemEqWarning => !string.IsNullOrEmpty(SystemEqWarningText);

    public ICommand ToggleSystemEqBypassCommand { get; private set; } = null!;
    public ICommand RefreshSystemEqEndpointsCommand { get; private set; } = null!;
    public ICommand InstallEqualizerApoCommand { get; private set; } = null!;
    public ICommand OpenApoDeviceSelectorCommand { get; private set; } = null!;
    public ICommand RemoveSystemEqFromApoCommand { get; private set; } = null!;

    /// <summary>True while an editor change is waiting for the debounce to reach the backend.</summary>
    internal bool HasPendingSystemEqApply => _systemEqApplyPending;

    /// <summary>
    /// Ctor tail: wires services, settings, the debounce pipeline and commands, then restores a
    /// persisted System EQ choice before any FiiO auto-detect runs (KTD9).
    /// </summary>
    private void InitializeSystemEq(SystemEqServices? services, string? settingsPath)
    {
        _systemEq = services ?? SystemEqServices.CreateDefault();
        _systemEqSettings = SystemEqSettings.Load(settingsPath);
        _systemEqApplyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SystemEqApplyDebounceMilliseconds)
        };
        _systemEqApplyTimer.Tick += SystemEqApplyTimerOnTick;

        ToggleSystemEqBypassCommand = new RelayCommand(() => SystemEqBypassed = !SystemEqBypassed);
        RefreshSystemEqEndpointsCommand = new AsyncRelayCommand(RefreshSystemEqAsync);
        InstallEqualizerApoCommand = new AsyncRelayCommand(() => RunApoLauncherAsync(_systemEq.RunInstaller, isInstaller: true));
        OpenApoDeviceSelectorCommand = new AsyncRelayCommand(() => RunApoLauncherAsync(_systemEq.RunDeviceSelector, isInstaller: false));
        RemoveSystemEqFromApoCommand = new AsyncRelayCommand(RemoveSystemEqFromApoAsync);

        DeviceProfiles.Add(SystemEqProfiles.WindowsSystemEq);
        WatchSystemEqBands();
        PropertyChanged += SystemEqOnOwnPropertyChanged;

        if (_systemEqSettings.Selected)
        {
            ApplyDeviceProfile(SystemEqProfiles.WindowsSystemEq);
            DeviceSelectionStatus = $"Using {SystemEqProfiles.WindowsSystemEq.DisplayLabel}.";
            SetConnectionState(IsDeviceConnected);
        }

        UpdateSystemEqStatus();
    }

    /// <summary>Called at the end of <see cref="ApplyDeviceProfile"/> for every profile switch.</summary>
    private void OnDeviceProfileAppliedForSystemEq(bool wasSystemEq)
    {
        if (_systemEq is null)
        {
            // ApplyDeviceProfile ran before InitializeSystemEq; nothing to do yet.
            return;
        }

        OnPropertyChanged(nameof(IsSystemEqSelected));
        OnPropertyChanged(nameof(IsHardwareDeviceSelected));
        var isSystemEq = IsSystemEqSelected;
        if (isSystemEq == wasSystemEq)
        {
            return;
        }

        if (isSystemEq)
        {
            _systemEqSettings.Selected = true;
            SaveSystemEqSettings();
            _systemEqApplyHalted = false;
            _systemEqRefreshTask = RefreshSystemEqAsync();
            return;
        }

        // Leaving System EQ for a FiiO profile: keep the config.txt include but make it
        // harmless (header-only WolfEQ.txt). Only "Remove from Equalizer APO" removes it.
        _systemEqSettings.Selected = false;
        SaveSystemEqSettings();
        StopSystemEqConfigWatcher();
        _systemEqApplyTimer.Stop();
        _systemEqApplyPending = false;
        _systemEqNotice = null;
        _systemEqApplyError = null;
        SystemEqState = SystemEqState.NotSelected;
        if (!_isLeavingSystemEqForRemoval && !_systemEqApplyHalted)
        {
            _ = WriteSystemEqBypassOnLeaveAsync(BuildEditorPresetSnapshot());
        }

        ConnectionText = IsDeviceConnected ? "Connected" : "Disconnected";
        UpdateSystemEqStatus();
    }

    private async Task WriteSystemEqBypassOnLeaveAsync(EqPreset snapshot)
    {
        try
        {
            var result = await _systemEq.Backend.ApplyAsync(snapshot, [], bypassed: true);
            if (!result.Succeeded)
            {
                AddLog($"Windows System EQ could not be bypassed while switching devices: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"Windows System EQ could not be bypassed while switching devices: {ex.Message}");
        }
    }

    /// <summary>Records a FiiO detection while System EQ stays selected (R2); feeds the warning only.</summary>
    private void NoteFiioDetectedInSystemEq(FiioDeviceProfile profile)
    {
        DeviceSelectionStatus = $"Detected {profile.DisplayLabel}; staying in {SystemEqProfiles.WindowsSystemEq.DisplayName}.";
        UpdateSystemEqStatus();
    }

    #region Editor watch + debounce

    private void SystemEqOnOwnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(Bands, _systemEqWatchedBands))
        {
            WatchSystemEqBands();
            QueueSystemEqApply();
            return;
        }

        if (e.PropertyName is nameof(PreampDb))
        {
            QueueSystemEqApply();
        }
    }

    private void WatchSystemEqBands()
    {
        if (_systemEqWatchedBands is { } previous)
        {
            previous.CollectionChanged -= SystemEqBandsOnCollectionChanged;
        }

        foreach (var band in _systemEqHookedBands)
        {
            band.PropertyChanged -= SystemEqBandOnPropertyChanged;
        }

        _systemEqHookedBands.Clear();
        _systemEqWatchedBands = Bands;
        Bands.CollectionChanged += SystemEqBandsOnCollectionChanged;
        foreach (var band in Bands)
        {
            HookSystemEqBand(band);
        }
    }

    private void HookSystemEqBand(EqBand band)
    {
        if (_systemEqHookedBands.Add(band))
        {
            band.PropertyChanged += SystemEqBandOnPropertyChanged;
        }
    }

    private void SystemEqBandsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Clear() does not report the removed items; re-hook the whole collection.
            WatchSystemEqBands();
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (EqBand band in e.OldItems)
                {
                    if (_systemEqHookedBands.Remove(band))
                    {
                        band.PropertyChanged -= SystemEqBandOnPropertyChanged;
                    }
                }
            }

            if (e.NewItems is not null)
            {
                foreach (EqBand band in e.NewItems)
                {
                    HookSystemEqBand(band);
                }
            }
        }

        QueueSystemEqApply();
    }

    private void SystemEqBandOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => QueueSystemEqApply();

    /// <summary>
    /// Debounces an editor snapshot to the backend. Unlike FiiO live sync this also runs during
    /// <c>_isLoadingPreset</c>, because preset loads, reset and undo/redo must reach the output (R4).
    /// </summary>
    private void QueueSystemEqApply()
    {
        if (_systemEqApplyTimer is null || !IsSystemEqSelected || _systemEqApplyHalted)
        {
            return;
        }

        _systemEqApplyPending = true;
        _systemEqApplyTimer.Stop();
        _systemEqApplyTimer.Start();
    }

    private async void SystemEqApplyTimerOnTick(object? sender, EventArgs e)
    {
        _systemEqApplyTimer.Stop();
        try
        {
            await FlushSystemEqApplyAsync();
        }
        catch (Exception ex)
        {
            AddLog($"Windows System EQ apply failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends the pending editor snapshot now (the debounce tick calls this; tests call it directly).
    /// Returns null when nothing was pending or System EQ cannot apply.
    /// </summary>
    internal async Task<SystemEqApplyResult?> FlushSystemEqApplyAsync()
    {
        _systemEqApplyTimer.Stop();
        if (!IsSystemEqSelected || _systemEqApplyHalted)
        {
            _systemEqApplyPending = false;
            return null;
        }

        if (!_systemEqApplyPending || _systemEqApplyInFlight)
        {
            // An in-flight apply re-checks the pending flag when it finishes.
            return null;
        }

        SystemEqApplyResult? last = null;
        _systemEqApplyInFlight = true;
        try
        {
            while (_systemEqApplyPending && IsSystemEqSelected && !_systemEqApplyHalted)
            {
                _systemEqApplyPending = false;
                last = await ApplySystemEqSnapshotAsync();
            }
        }
        finally
        {
            _systemEqApplyInFlight = false;
        }

        return last;
    }

    /// <summary>Applies the current editor immediately (automation and explicit actions).</summary>
    private async Task<SystemEqApplyResult> ApplySystemEqNowAsync()
    {
        if (_systemEqApplyHalted)
        {
            var outcome = SystemEqState == SystemEqState.Blocked ? SystemEqApplyOutcome.Blocked : SystemEqApplyOutcome.NotInstalled;
            return new SystemEqApplyResult(outcome, SystemEqStatusText);
        }

        _systemEqApplyPending = true;
        return await FlushSystemEqApplyAsync()
               ?? new SystemEqApplyResult(SystemEqApplyOutcome.Failed, SystemEqStatusText);
    }

    private async Task<SystemEqApplyResult> ApplySystemEqSnapshotAsync()
    {
        SystemEqApplyResult result;
        try
        {
            result = await _systemEq.Backend.ApplyAsync(
                BuildEditorPresetSnapshot(),
                SelectedSystemEqEndpointGuids(),
                SystemEqBypassed);
        }
        catch (Exception ex)
        {
            result = new SystemEqApplyResult(SystemEqApplyOutcome.Failed, ex.Message);
        }

        HandleSystemEqApplyResult(result);
        return result;
    }

    private void HandleSystemEqApplyResult(SystemEqApplyResult result)
    {
        if (!IsSystemEqSelected)
        {
            return;
        }

        switch (result.Outcome)
        {
            case SystemEqApplyOutcome.Blocked:
                _systemEqApplyHalted = true;
                _systemEqApplyPending = false;
                _systemEqBlockedMessage = result.Message;
                SystemEqState = SystemEqState.Blocked;
                AddLog($"Windows System EQ stopped: {result.Message}");
                break;
            case SystemEqApplyOutcome.NotInstalled:
                _systemEqApplyHalted = true;
                _systemEqApplyPending = false;
                IsEqualizerApoInstalled = false;
                SystemEqState = SystemEqState.NeedsInstall;
                break;
            case SystemEqApplyOutcome.Failed:
                _systemEqApplyError = result.Message;
                AddLog($"Windows System EQ apply failed: {result.Message}");
                break;
            default:
                _systemEqApplyError = null;
                break;
        }

        UpdateSystemEqStatus();
    }

    private IReadOnlyList<string> SelectedSystemEqEndpointGuids()
        => SystemEqEndpoints.Where(endpoint => endpoint.IsSelected).Select(endpoint => endpoint.Guid).ToList();

    #endregion

    #region Detection + endpoints

    /// <summary>Re-detects Equalizer APO, re-enumerates outputs, and moves the setup state machine.</summary>
    private async Task RefreshSystemEqAsync()
    {
        if (!IsSystemEqSelected)
        {
            RebuildSystemEqEndpoints();
            return;
        }

        _systemEqNotice = null;
        SystemEqState = SystemEqState.Checking;

        SystemEqBackendStatus status;
        try
        {
            status = await _systemEq.Backend.GetStatusAsync();
        }
        catch (Exception ex)
        {
            status = SystemEqBackendStatus.NotInstalled(ex.Message);
        }

        _systemEqBackendStatus = status;
        IsEqualizerApoInstalled = status.IsInstalled;
        RebuildSystemEqEndpoints();
        if (!IsSystemEqSelected)
        {
            return;
        }

        _systemEqBlockedMessage = status.IsInstalled && !status.IsConfigWritable ? status.Message : null;
        var state = !status.IsInstalled
            ? SystemEqState.NeedsInstall
            : !status.IsConfigWritable
                ? SystemEqState.Blocked
                : SystemEqEndpoints.Any(endpoint => endpoint.IsApoActive)
                    ? SystemEqState.Active
                    : SystemEqState.NeedsDeviceSelection;
        if (state == SystemEqState.Active)
        {
            _systemEqRebootPending = false;
        }

        _systemEqApplyHalted = state is SystemEqState.NeedsInstall or SystemEqState.Blocked;
        _systemEqApplyError = null;
        SystemEqState = state;
        UpdateSystemEqConfigWatcher();
        UpdateSystemEqStatus();
        QueueSystemEqApply();
    }

    private void RebuildSystemEqEndpoints()
    {
        IReadOnlyList<AudioEndpointInfo> endpoints;
        try
        {
            endpoints = _systemEq.Catalog.GetRenderEndpoints();
        }
        catch (Exception ex)
        {
            AddLog($"Windows outputs could not be listed: {ex.Message}");
            endpoints = [];
        }

        _isRebuildingSystemEqEndpoints = true;
        try
        {
            foreach (var option in SystemEqEndpoints)
            {
                option.PropertyChanged -= SystemEqEndpointOnPropertyChanged;
            }

            SystemEqEndpoints.Clear();
            var selectedIds = new HashSet<string>(_systemEqSettings.EndpointIds, StringComparer.OrdinalIgnoreCase);
            foreach (var endpoint in endpoints)
            {
                bool isFiio;
                try { isFiio = _systemEq.IsFiioEndpoint(endpoint); }
                catch { isFiio = false; }

                ApoEndpointStatus health;
                try { health = _systemEq.GetEndpointStatus(endpoint.Guid); }
                catch { health = new ApoEndpointStatus(false, false); }

                var option = new SystemEqEndpointOption(
                    endpoint.Id,
                    endpoint.Guid,
                    endpoint.FriendlyName,
                    endpoint.DeviceDescription,
                    endpoint.IsDefault,
                    isFiio)
                {
                    IsSelected = selectedIds.Contains(endpoint.Id) || selectedIds.Contains(endpoint.Guid),
                    IsApoActive = health.IsApoActive,
                    EnhancementsDisabled = health.EnhancementsDisabled,
                    StatusText = health.EnhancementsDisabled
                        ? "Audio enhancements disabled"
                        : health.IsApoActive
                            ? "Equalizer APO active"
                            : "Equalizer APO not installed on this output"
                };
                option.PropertyChanged += SystemEqEndpointOnPropertyChanged;
                SystemEqEndpoints.Add(option);
            }
        }
        finally
        {
            _isRebuildingSystemEqEndpoints = false;
        }

        UpdateSystemEqStatus();
    }

    private void SystemEqEndpointOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRebuildingSystemEqEndpoints || e.PropertyName != nameof(SystemEqEndpointOption.IsSelected))
        {
            return;
        }

        // Keep ticks for outputs that are unplugged right now so they come back on reconnect.
        var present = new HashSet<string>(SystemEqEndpoints.Select(endpoint => endpoint.Id), StringComparer.OrdinalIgnoreCase);
        _systemEqSettings.EndpointIds = _systemEqSettings.EndpointIds
            .Where(id => !present.Contains(id) && !SystemEqEndpoints.Any(endpoint => string.Equals(endpoint.Guid, id, StringComparison.OrdinalIgnoreCase)))
            .Concat(SystemEqEndpoints.Where(endpoint => endpoint.IsSelected).Select(endpoint => endpoint.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        SaveSystemEqSettings();
        UpdateSystemEqStatus();
        QueueSystemEqApply();
    }

    #endregion

    #region Setup actions

    private async Task RunApoLauncherAsync(Func<Task<ApoLaunchResult>> launch, bool isInstaller)
    {
        _systemEqNotice = isInstaller
            ? "Waiting for Equalizer APO setup to finish..."
            : "Waiting for the Equalizer APO device selector to close...";
        UpdateSystemEqStatus();

        ApoLaunchResult result;
        try
        {
            result = await launch();
        }
        catch (Exception ex)
        {
            result = new ApoLaunchResult(ApoLaunchOutcome.Failed, ex.Message);
        }

        AddLog($"{(isInstaller ? "Equalizer APO setup" : "Equalizer APO device selector")}: {result.Outcome}. {result.Message}".Trim());
        switch (result.Outcome)
        {
            case ApoLaunchOutcome.Cancelled:
                _systemEqNotice = isInstaller
                    ? "Equalizer APO setup was canceled. Nothing was changed."
                    : "The device selector was canceled. Nothing was changed.";
                UpdateSystemEqStatus();
                return;
            case ApoLaunchOutcome.RebootRequired:
                _systemEqRebootPending = true;
                await RefreshSystemEqAsync();
                return;
            case ApoLaunchOutcome.NotFound:
            case ApoLaunchOutcome.Failed:
                await RefreshSystemEqAsync();
                _systemEqNotice = string.IsNullOrWhiteSpace(result.Message)
                    ? "Equalizer APO setup could not be started."
                    : result.Message;
                UpdateSystemEqStatus();
                return;
            default:
                await RefreshSystemEqAsync();
                return;
        }
    }

    /// <summary>
    /// "Remove from Equalizer APO": leaves System EQ mode and deletes WolfEQ's include file and
    /// config.txt block (R10). This is the only path that calls <see cref="IEqOutputBackend.RemoveAsync"/>.
    /// </summary>
    private async Task RemoveSystemEqFromApoAsync()
    {
        if (IsSystemEqSelected)
        {
            _isLeavingSystemEqForRemoval = true;
            try
            {
                SelectedDeviceProfile = FiioDeviceProfiles.Default;
            }
            finally
            {
                _isLeavingSystemEqForRemoval = false;
            }

            if (IsSystemEqSelected)
            {
                // The device-switch prompt was declined; keep everything as it was.
                return;
            }
        }

        SystemEqApplyResult result;
        try
        {
            result = await _systemEq.Backend.RemoveAsync();
        }
        catch (Exception ex)
        {
            result = new SystemEqApplyResult(SystemEqApplyOutcome.Failed, ex.Message);
        }

        Status = result.Succeeded
            ? "Removed WolfEQ from Equalizer APO."
            : $"Could not remove WolfEQ from Equalizer APO: {result.Message}";
        AddLog(Status);
    }

    #endregion

    #region Status + warnings

    private void UpdateSystemEqStatus()
    {
        if (_systemEqSettings is null)
        {
            return;
        }

        SystemEqStatusText = BuildSystemEqStatusText();
        SystemEqWarningText = BuildSystemEqWarningText();
        if (IsSystemEqSelected)
        {
            ConnectionText = SystemEqStatusText;
        }
    }

    private string BuildSystemEqStatusText()
    {
        if (!IsSystemEqSelected)
        {
            return "Windows System EQ is off.";
        }

        if (!string.IsNullOrWhiteSpace(_systemEqNotice))
        {
            return _systemEqNotice;
        }

        if (_systemEqRebootPending && SystemEqState != SystemEqState.Active)
        {
            return "Restart Windows to finish setting up Equalizer APO.";
        }

        return SystemEqState switch
        {
            SystemEqState.Checking => "Checking Equalizer APO...",
            SystemEqState.NeedsInstall => "Equalizer APO is not installed.",
            SystemEqState.NeedsDeviceSelection => "Equalizer APO is not active on any output yet. Use Add outputs.",
            SystemEqState.Blocked => "WolfEQ can't write to the Equalizer APO config folder.",
            SystemEqState.Active when SystemEqBypassed => "System EQ bypassed",
            SystemEqState.Active when !SystemEqEndpoints.Any(endpoint => endpoint.IsSelected)
                => "Pick an output to apply System EQ.",
            SystemEqState.Active when _systemEqApplyError is not null => $"Couldn't apply System EQ: {_systemEqApplyError}",
            SystemEqState.Active => SystemEqEndpoints.Count(endpoint => endpoint.IsSelected) == 1
                ? $"System EQ on {SystemEqEndpoints.First(endpoint => endpoint.IsSelected).Name}"
                : $"System EQ on {SystemEqEndpoints.Count(endpoint => endpoint.IsSelected)} outputs",
            _ => "Windows System EQ is off."
        };
    }

    private string BuildSystemEqWarningText()
    {
        if (!IsSystemEqSelected)
        {
            return string.Empty;
        }

        var warnings = new List<string>();
        if (SystemEqState == SystemEqState.Blocked)
        {
            var reason = string.IsNullOrWhiteSpace(_systemEqBlockedMessage) ? null : $" ({_systemEqBlockedMessage.TrimEnd('.')})";
            warnings.Add($"WolfEQ can't write to the Equalizer APO config folder{reason}. Your edits stay in the editor, but they are not reaching Windows.");
        }

        var selected = SystemEqEndpoints.Where(endpoint => endpoint.IsSelected).ToList();
        if (selected.FirstOrDefault(endpoint => endpoint.IsFiio) is { } fiio)
        {
            warnings.Add($"{fiio.Name} is a FiiO device. If its hardware EQ is also on, the EQ is applied twice.");
        }

        if (_systemEqBackendStatus?.IsPeaceIncludeActive == true)
        {
            warnings.Add("Peace is also active in Equalizer APO, so two EQs may be applied.");
        }

        if (selected.FirstOrDefault(endpoint => endpoint.EnhancementsDisabled) is { } disabled)
        {
            warnings.Add($"Audio enhancements are turned off for {disabled.Name}, so Equalizer APO can't process it.");
        }

        return string.Join(" ", warnings);
    }

    private void SaveSystemEqSettings()
    {
        try
        {
            _systemEqSettings.Save();
        }
        catch (Exception ex)
        {
            AddLog($"System EQ settings could not be saved: {ex.Message}");
        }
    }

    #endregion

    #region Automation

    private async Task<AutomationResult> ExecuteSystemEqAutomationAsync(AutomationRequest request)
    {
        if (!IsSystemEqSelected)
        {
            return new(1, "This command works in Windows System EQ mode. Select Windows System EQ in Settings first.");
        }

        await _systemEqRefreshTask;

        if (request.SystemEqBypass is { } bypass)
        {
            SystemEqBypassed = bypass switch
            {
                "on" => true,
                "off" => false,
                _ => !SystemEqBypassed
            };
            var bypassResult = await ApplySystemEqNowAsync();
            return bypassResult.Succeeded
                ? new(0, SystemEqBypassed ? "Windows System EQ bypass on." : "Windows System EQ bypass off.")
                : new(1, $"Bypass saved, but it could not be applied: {bypassResult.Message}");
        }

        var name = request.PresetName!.Trim();
        var preset = Presets.FirstOrDefault(candidate => string.Equals(candidate.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return new(1, $"No library preset is named \"{name}\".");
        }

        if (!ReferenceEquals(preset, SelectedPreset) && EditorSession.WouldReplaceUnsavedEdits)
        {
            return new(3, "Preset not loaded: the editor has unsaved changes.");
        }

        LibrarySelection = preset;
        if (!TryOpenLibrarySelection())
        {
            return new(3, "Preset not loaded: the editor has unsaved changes.");
        }

        var result = await ApplySystemEqNowAsync();
        return result.Succeeded
            ? new(0, $"Applied \"{preset.Name}\" to Windows System EQ.")
            : new(1, $"Loaded \"{preset.Name}\", but it could not be applied: {result.Message}");
    }

    #endregion
}
