using System.Collections.ObjectModel;
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
    private readonly SemaphoreSlim _systemEqAutomationGate = new(1, 1);
    private SystemEqState _systemEqState = SystemEqState.NotSelected;
    private SystemEqBackendStatus? _systemEqBackendStatus;
    private bool _isEqualizerApoInstalled;
    private bool _systemEqApplyPending;
    private bool _systemEqApplyHalted;
    // Startup without a persisted lastApplied: APO keeps its previous file until the user
    // changes something, so refreshes and config.txt repairs must not write the editor.
    private bool _systemEqHoldStartupApply;
    private bool _isInitializingSystemEq;
    private bool _systemEqLaunchInFlight;
    private int _systemEqRefreshGeneration;
    private Task<SystemEqApplyResult?> _systemEqApplyLoop = Task.FromResult<SystemEqApplyResult?>(null);
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
            ClearSystemEqNotice();
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

    /// <summary>The latest detection/endpoint refresh (tests await it; automation waits for it).</summary>
    internal Task SystemEqRefreshTask => _systemEqRefreshTask;

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
        RefreshSystemEqEndpointsCommand = new AsyncRelayCommand(StartSystemEqRefresh);
        InstallEqualizerApoCommand = new AsyncRelayCommand(() => RunApoLauncherAsync(RunApoInstallerWithProgressAsync, isInstaller: true));
        OpenApoDeviceSelectorCommand = new AsyncRelayCommand(() => RunApoLauncherAsync(_systemEq.RunDeviceSelector, isInstaller: false));
        RemoveSystemEqFromApoCommand = new AsyncRelayCommand(RemoveSystemEqFromApoAsync);

        // First in the picker: it works with any output, FiiO hardware follows.
        DeviceProfiles.Insert(0, SystemEqProfiles.WindowsSystemEq);

        if (_systemEqSettings.Selected)
        {
            _isInitializingSystemEq = true;
            try
            {
                // Re-apply what Windows last received, not whatever the library opened with.
                var lastApplied = _systemEqSettings.LastApplied?.ToPreset();
                var restoredLibraryPreset = lastApplied is not null && RestoreSystemEqEditorContent(lastApplied);
                _systemEqHoldStartupApply = lastApplied is null;
                SwitchDeviceProfile(SystemEqProfiles.WindowsSystemEq);
                if (lastApplied is not null && !restoredLibraryPreset)
                {
                    // Not in the library: guard it like any other unsaved editor content.
                    EditorSession.NotifyEdit();
                }
            }
            finally
            {
                _isInitializingSystemEq = false;
            }
        }

        UpdateSystemEqStatus();
    }

    /// <summary>
    /// Loads a persisted System EQ curve into the editor. A library preset with the same name
    /// and curve is selected (the normal library-open path); anything else becomes detached
    /// editor content. Returns true when a library preset was selected.
    /// </summary>
    private bool RestoreSystemEqEditorContent(EqPreset snapshot)
    {
        var match = Presets.FirstOrDefault(preset =>
            string.Equals(preset.Name.Trim(), snapshot.Name.Trim(), StringComparison.Ordinal)
            && Math.Abs(preset.PreampDb - snapshot.PreampDb) < 0.005
            && preset.Bands.Count == snapshot.Bands.Count
            && preset.Bands.Zip(snapshot.Bands).All(pair => BandsMatch(pair.First, pair.Second)));
        if (match is not null)
        {
            LibrarySelection = match;
            SelectPresetInternal(match);
            EditorSession.NotifyLoadedFromLibrary();
            // Startup selection, not a user change to the saved library state.
            _savedSelectedPreset = SelectedPreset;
            IsProfileLibraryDirty = false;
            return true;
        }

        DetachEditorFromLibrary();
        _isLoadingPreset = true;
        try
        {
            UnwatchBands(Bands);
            Bands = new ObservableCollection<EqBand>(snapshot.Bands.Select(band => band.Clone()));
            WatchBands(Bands);
            PreampDb = snapshot.PreampDb;
        }
        finally
        {
            _isLoadingPreset = false;
        }

        OnPropertyChanged(nameof(Bands));
        OnPropertyChanged(nameof(DeviceBandEditorTitle));
        OnPropertyChanged(nameof(BandCardWidth));
        OnPropertyChanged(nameof(SupportsK13PresetTools));
        RefreshHeadroomProperties();
        ResetEditHistory();
        return false;
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
            if (!_isInitializingSystemEq)
            {
                // The user picked System EQ: the editor is what they want applied.
                _systemEqHoldStartupApply = false;
            }

            StartSystemEqRefresh();
            return;
        }

        // Leaving System EQ for a FiiO profile: keep the config.txt include but make it
        // harmless (header-only WolfEQ.txt). Only "Remove from Equalizer APO" removes it.
        _systemEqSettings.Selected = false;
        SaveSystemEqSettings();
        _systemEqRefreshGeneration++;
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

        // The caller's SetConnectionState restores the hardware connection text.
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

    #region Debounce + apply

    /// <summary>
    /// Debounces an editor snapshot to the backend. The editor hooks in MainViewModel
    /// (band/collection changes, preamp, Bands replacement) call this. Unlike FiiO live sync it
    /// also runs during <c>_isLoadingPreset</c>, because preset loads, reset and undo/redo must
    /// reach the output (R4, KTD8). <paramref name="isUserChange"/> is false for re-applies
    /// (refresh, config.txt repair), which wait while the startup hold is active.
    /// </summary>
    private void QueueSystemEqApply(bool isUserChange = true)
    {
        if (_systemEqApplyTimer is null || !IsSystemEqSelected || _systemEqApplyHalted)
        {
            return;
        }

        if (_systemEqHoldStartupApply)
        {
            if (!isUserChange || _isInitializingSystemEq)
            {
                return;
            }

            _systemEqHoldStartupApply = false;
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
    /// Returns null when nothing was pending, an apply is already in flight (it re-checks the
    /// pending flag when it finishes), or System EQ cannot apply.
    /// </summary>
    internal async Task<SystemEqApplyResult?> FlushSystemEqApplyAsync()
    {
        _systemEqApplyTimer.Stop();
        if (!IsSystemEqSelected || _systemEqApplyHalted)
        {
            _systemEqApplyPending = false;
            return null;
        }

        if (!_systemEqApplyPending || !_systemEqApplyLoop.IsCompleted)
        {
            return null;
        }

        var completion = new TaskCompletionSource<SystemEqApplyResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _systemEqApplyLoop = completion.Task;
        SystemEqApplyResult? last = null;
        try
        {
            while (_systemEqApplyPending && IsSystemEqSelected && !_systemEqApplyHalted)
            {
                _systemEqApplyPending = false;
                last = await ApplySystemEqSnapshotAsync();
            }
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            throw;
        }

        completion.TrySetResult(last);
        return last;
    }

    /// <summary>
    /// Applies the current editor immediately (automation and explicit actions). When an apply
    /// is already in flight this waits for that loop, which picks up the pending snapshot, and
    /// returns its real outcome.
    /// </summary>
    private async Task<SystemEqApplyResult> ApplySystemEqNowAsync()
    {
        if (!_systemEqApplyHalted && IsSystemEqSelected)
        {
            _systemEqApplyPending = true;
            var result = _systemEqApplyLoop.IsCompleted
                ? await FlushSystemEqApplyAsync()
                : await _systemEqApplyLoop;
            if (result is not null)
            {
                return result;
            }
        }

        var outcome = SystemEqState switch
        {
            SystemEqState.Blocked => SystemEqApplyOutcome.Blocked,
            SystemEqState.NeedsInstall => SystemEqApplyOutcome.NotInstalled,
            _ => SystemEqApplyOutcome.Failed
        };
        return new SystemEqApplyResult(outcome, SystemEqStatusText);
    }

    private async Task<SystemEqApplyResult> ApplySystemEqSnapshotAsync()
    {
        var snapshot = BuildEditorPresetSnapshot();
        var bypassed = SystemEqBypassed;
        SystemEqApplyResult result;
        try
        {
            result = await _systemEq.Backend.ApplyAsync(snapshot, SelectedSystemEqEndpointGuids(), bypassed);
        }
        catch (Exception ex)
        {
            result = new SystemEqApplyResult(SystemEqApplyOutcome.Failed, ex.Message);
        }

        if (result.Succeeded && !bypassed && IsSystemEqSelected)
        {
            RememberSystemEqLastApplied(snapshot);
        }

        HandleSystemEqApplyResult(result);
        return result;
    }

    /// <summary>Persists the curve Windows now has, so the next startup re-applies the same EQ.</summary>
    private void RememberSystemEqLastApplied(EqPreset snapshot)
    {
        var applied = SystemEqAppliedSnapshot.FromPreset(snapshot);
        if (_systemEqSettings.LastApplied is { } previous
            && previous.Name == applied.Name
            && previous.PreampDb.Equals(applied.PreampDb)
            && previous.Bands.Count == applied.Bands.Count
            && previous.Bands.Zip(applied.Bands).All(pair =>
                pair.First.Number == pair.Second.Number
                && pair.First.Enabled == pair.Second.Enabled
                && pair.First.FilterType == pair.Second.FilterType
                && pair.First.FrequencyHz == pair.Second.FrequencyHz
                && pair.First.GainDb.Equals(pair.Second.GainDb)
                && pair.First.Q.Equals(pair.Second.Q)))
        {
            return;
        }

        _systemEqSettings.LastApplied = applied;
        SaveSystemEqSettings();
    }

    private void HandleSystemEqApplyResult(SystemEqApplyResult result)
    {
        if (!IsSystemEqSelected)
        {
            return;
        }

        if (!result.Succeeded)
        {
            // Errors outrank a launcher notice (e.g. "setup was canceled").
            ClearSystemEqNotice();
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

    private Task StartSystemEqRefresh() => _systemEqRefreshTask = RefreshSystemEqAsync();

    /// <summary>
    /// Re-detects Equalizer APO, re-enumerates outputs, and moves the setup state machine.
    /// Overlapping refreshes are latest-wins: an older one stops at its next await.
    /// </summary>
    private async Task RefreshSystemEqAsync()
    {
        var generation = ++_systemEqRefreshGeneration;
        if (!IsSystemEqSelected)
        {
            await RebuildSystemEqEndpointsAsync(generation);
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

        if (generation != _systemEqRefreshGeneration || !IsSystemEqSelected)
        {
            return;
        }

        _systemEqBackendStatus = status;
        IsEqualizerApoInstalled = status.IsInstalled;
        if (!await RebuildSystemEqEndpointsAsync(generation) || !IsSystemEqSelected)
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
        QueueSystemEqApply(isUserChange: false);
    }

    private readonly record struct SystemEqEndpointRow(AudioEndpointInfo Endpoint, bool IsFiio, ApoEndpointStatus Health);

    /// <summary>
    /// Enumerates outputs (MMDevice COM) and reads per-endpoint APO health (registry) off the UI
    /// thread, then rebuilds <see cref="SystemEqEndpoints"/> on the caller's (UI) context.
    /// Returns false when a newer refresh started meanwhile.
    /// </summary>
    private async Task<bool> RebuildSystemEqEndpointsAsync(int generation)
    {
        var services = _systemEq;
        var (rows, error) = services.QueryEndpointsInline
            ? QuerySystemEqEndpoints(services)
            : await Task.Run(() => QuerySystemEqEndpoints(services));
        if (generation != _systemEqRefreshGeneration)
        {
            return false;
        }

        if (error is not null)
        {
            AddLog($"Windows outputs could not be listed: {error}");
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
            foreach (var (endpoint, isFiio, health) in rows)
            {
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
        return true;
    }

    private static (IReadOnlyList<SystemEqEndpointRow> Rows, string? Error) QuerySystemEqEndpoints(SystemEqServices services)
    {
        IReadOnlyList<AudioEndpointInfo> endpoints;
        try
        {
            endpoints = services.Catalog.GetRenderEndpoints();
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }

        var rows = new List<SystemEqEndpointRow>(endpoints.Count);
        foreach (var endpoint in endpoints)
        {
            bool isFiio;
            try { isFiio = services.IsFiioEndpoint(endpoint); }
            catch { isFiio = false; }

            ApoEndpointStatus health;
            try { health = services.GetEndpointStatus(endpoint.Guid); }
            catch { health = new ApoEndpointStatus(false, false); }

            rows.Add(new SystemEqEndpointRow(endpoint, isFiio, health));
        }

        return (rows, null);
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
        ClearSystemEqNotice();
        UpdateSystemEqStatus();
        QueueSystemEqApply();
    }

    #endregion

    #region Setup actions

    private async Task RunApoLauncherAsync(Func<Task<ApoLaunchResult>> launch, bool isInstaller)
    {
        _systemEqLaunchInFlight = true;
        try
        {
            await RunApoLauncherCoreAsync(launch, isInstaller);
        }
        finally
        {
            _systemEqLaunchInFlight = false;
        }
    }

    private async Task RunApoLauncherCoreAsync(Func<Task<ApoLaunchResult>> launch, bool isInstaller)
    {
        _systemEqNotice = isInstaller
            ? "Setting up Equalizer APO..."
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
                await StartSystemEqRefresh();
                return;
            case ApoLaunchOutcome.NotFound:
            case ApoLaunchOutcome.Failed:
                await StartSystemEqRefresh();
                _systemEqNotice = string.IsNullOrWhiteSpace(result.Message)
                    ? "Equalizer APO setup could not be started."
                    : result.Message;
                UpdateSystemEqStatus();
                return;
            default:
                await StartSystemEqRefresh();
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

    /// <summary>
    /// A launcher notice (setup canceled, waiting, failed) shows until the next meaningful change:
    /// bypass, output ticks, an apply error, a refresh, or leaving System EQ. While a launcher is
    /// still running its "waiting" text stays.
    /// </summary>
    private void ClearSystemEqNotice()
    {
        if (!_systemEqLaunchInFlight)
        {
            _systemEqNotice = null;
        }
    }

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
            SystemEqState.Active => BuildActiveSystemEqStatusText(),
            _ => "Windows System EQ is off."
        };
    }

    /// <summary>Names only the ticked outputs Equalizer APO actually processes.</summary>
    private string BuildActiveSystemEqStatusText()
    {
        var selected = SystemEqEndpoints.Where(endpoint => endpoint.IsSelected).ToList();
        var reached = selected.Where(endpoint => endpoint.IsApoActive).ToList();
        return reached.Count switch
        {
            0 => selected.Count == 1
                ? "Equalizer APO isn't active on the selected output"
                : "Equalizer APO isn't active on the selected outputs",
            1 => $"System EQ on {reached[0].Name}",
            _ => $"System EQ on {reached.Count} outputs"
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

        if (SystemEqState == SystemEqState.Active)
        {
            foreach (var inactive in selected.Where(endpoint => !endpoint.IsApoActive && !endpoint.EnhancementsDisabled))
            {
                warnings.Add($"Equalizer APO isn't active on {inactive.Name}. Use Add outputs to enable it.");
            }
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

        // Concurrent automation commands run one after another, never interleaved.
        await _systemEqAutomationGate.WaitAsync();
        try
        {
            await _systemEqRefreshTask;
            if (!IsSystemEqSelected)
            {
                return new(1, "This command works in Windows System EQ mode. Select Windows System EQ in Settings first.");
            }

            return request.SystemEqBypass is { } bypass
                ? await ExecuteSystemEqBypassAsync(bypass)
                : await ExecuteSystemEqPresetAsync(request.PresetName!.Trim());
        }
        finally
        {
            _systemEqAutomationGate.Release();
        }
    }

    private async Task<AutomationResult> ExecuteSystemEqBypassAsync(string bypass)
    {
        SystemEqBypassed = bypass switch
        {
            "on" => true,
            "off" => false,
            _ => !SystemEqBypassed
        };
        var result = await ApplySystemEqNowAsync();
        return result.Succeeded
            ? new(0, SystemEqBypassed ? "Windows System EQ bypass on." : "Windows System EQ bypass off.")
            : new(1, $"Bypass saved, but it could not be applied: {result.Message}");
    }

    private async Task<AutomationResult> ExecuteSystemEqPresetAsync(string name)
    {
        var preset = Presets.FirstOrDefault(candidate => string.Equals(candidate.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return new(1, $"No library preset is named \"{name}\".");
        }

        // Same rule as --slot: automation never replaces unsaved editor edits, including edits
        // to the preset that is already open (they would otherwise be applied under its name).
        if (EditorSession.WouldReplaceUnsavedEdits)
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
