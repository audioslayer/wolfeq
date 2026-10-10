using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.Services.SystemEq;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class SystemEqViewModelTests : IDisposable
{
    private const string HeadphonesId = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}";
    private const string HeadphonesGuid = "{11111111-1111-1111-1111-111111111111}";
    private const string K13Id = "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}";

    private readonly SystemEqTestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void SelectingSystemEqPersistsChoiceAndHidesSlots()
    {
        var model = _rig.CreateModel();

        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;

        Assert.True(model.IsSystemEqSelected);
        Assert.False(model.IsHardwareDeviceSelected);
        Assert.Contains(SystemEqProfiles.WindowsSystemEq, model.DeviceProfiles);
        Assert.Single(model.DeviceUserPresets);
        Assert.Equal("No writable slots", model.DeviceUserPresets[0].Name);
        Assert.True(SystemEqSettings.Load(_rig.SettingsPath).Selected);
        Assert.Equal(SystemEqState.Active, model.SystemEqState);
        Assert.True(model.IsEqualizerApoInstalled);
        Assert.Equal(model.SystemEqStatusText, model.ConnectionText);
        Assert.Equal(2, model.SystemEqEndpoints.Count);
    }

    [Fact]
    public async Task BandEditsAreDebouncedIntoOneApply()
    {
        var model = await _rig.CreateSystemEqModelAsync();

        for (var step = 1; step <= 5; step++)
        {
            model.Bands[2].GainDb = step;
        }

        Assert.True(model.HasPendingSystemEqApply);
        Assert.Empty(_rig.Backend.Applies);
        await model.FlushSystemEqApplyAsync();
        await model.FlushSystemEqApplyAsync();

        var apply = Assert.Single(_rig.Backend.Applies);
        Assert.Equal(5, apply.Preset.Bands[2].GainDb);
        Assert.Equal([HeadphonesGuid], apply.EndpointGuids);
        Assert.False(apply.Bypassed);
    }

    [Fact]
    public async Task PreampEditApplies()
    {
        var model = await _rig.CreateSystemEqModelAsync();

        model.PreampDb = -4.5;
        await model.FlushSystemEqApplyAsync();

        Assert.Equal(-4.5, Assert.Single(_rig.Backend.Applies).Preset.PreampDb);
    }

    [Fact]
    public async Task LibraryLoadResetAndUndoAllApply()
    {
        var model = await _rig.CreateSystemEqModelAsync();
        var preset = SystemEqTestRig.Preset("Bright Test", gainDb: 3, preampDb: -3);
        model.Presets.Add(preset);

        model.LibrarySelection = preset;
        Assert.True(model.TryOpenLibrarySelection());
        await model.FlushSystemEqApplyAsync();
        var loaded = Assert.Single(_rig.Backend.Applies);
        Assert.Equal(3, loaded.Preset.Bands[0].GainDb);
        Assert.Equal(-3, loaded.Preset.PreampDb);

        _rig.Backend.Applies.Clear();
        model.Bands[0].GainDb = 7;
        await model.FlushSystemEqApplyAsync();
        _rig.Backend.Applies.Clear();
        model.UndoCommand.Execute(null);
        await model.FlushSystemEqApplyAsync();
        Assert.Equal(3, Assert.Single(_rig.Backend.Applies).Preset.Bands[0].GainDb);

        _rig.Backend.Applies.Clear();
        model.ResetCommand.Execute(null);
        await model.FlushSystemEqApplyAsync();
        var reset = Assert.Single(_rig.Backend.Applies);
        Assert.All(reset.Preset.Bands, band => Assert.Equal(0, band.GainDb));
    }

    [Fact]
    public async Task BypassAppliesPersistsAndSurvivesRecreate()
    {
        var model = await _rig.CreateSystemEqModelAsync();

        model.ToggleSystemEqBypassCommand.Execute(null);
        await model.FlushSystemEqApplyAsync();

        Assert.True(model.SystemEqBypassed);
        Assert.True(Assert.Single(_rig.Backend.Applies).Bypassed);
        Assert.Contains("bypassed", model.SystemEqStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.True(SystemEqSettings.Load(_rig.SettingsPath).Bypassed);

        var restarted = _rig.CreateModel();
        Assert.True(restarted.IsSystemEqSelected);
        Assert.True(restarted.SystemEqBypassed);
        Assert.True(restarted.SystemEqEndpoints.Single(endpoint => endpoint.Id == HeadphonesId).IsSelected);
    }

    [Fact]
    public async Task FiioDetectionKeepsSystemEqAndFiioEndpointWarns()
    {
        await _rig.CreateSystemEqModelAsync();
        var model = _rig.CreateModel();
        Assert.True(model.IsSystemEqSelected);
        Assert.False(model.HasSystemEqWarning);

        typeof(MainViewModel)
            .GetMethod("SelectDetectedDeviceProfile", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(model, [FiioDeviceProfiles.K13R2R, null]);

        Assert.Same(SystemEqProfiles.WindowsSystemEq, model.SelectedDeviceProfile);
        model.SystemEqEndpoints.Single(endpoint => endpoint.Id == K13Id).IsSelected = true;
        Assert.True(model.HasSystemEqWarning);
        Assert.Contains("FiiO", model.SystemEqWarningText);
    }

    [Fact]
    public async Task PeaceIncludeAndDisabledEnhancementsWarn()
    {
        _rig.Backend.Status = _rig.Backend.Status with { IsPeaceIncludeActive = true };
        _rig.EndpointHealth[HeadphonesGuid] = new ApoEndpointStatus(true, EnhancementsDisabled: true);
        var model = await _rig.CreateSystemEqModelAsync();

        Assert.Contains("Peace", model.SystemEqWarningText);
        Assert.Contains("enhancements", model.SystemEqWarningText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Audio enhancements disabled", model.SystemEqEndpoints.Single(e => e.Id == HeadphonesId).StatusText);
    }

    [Fact]
    public async Task SwitchingToFiioBypassesOnceThenStopsApplying()
    {
        var model = await _rig.CreateSystemEqModelAsync();

        model.SelectedDeviceProfile = FiioDeviceProfiles.K13R2R;
        Assert.False(model.IsSystemEqSelected);
        Assert.True(model.IsHardwareDeviceSelected);
        Assert.Equal(SystemEqState.NotSelected, model.SystemEqState);
        var bypass = Assert.Single(_rig.Backend.Applies);
        Assert.True(bypass.Bypassed);
        Assert.False(SystemEqSettings.Load(_rig.SettingsPath).Selected);

        model.Bands[0].GainDb = 4;
        model.PreampDb = -2;
        Assert.False(model.HasPendingSystemEqApply);
        Assert.Null(await model.FlushSystemEqApplyAsync());
        Assert.Single(_rig.Backend.Applies);
        Assert.Equal(0, _rig.Backend.RemoveCount);
    }

    [Fact]
    public async Task FiioOnlySessionNeverTouchesBackend()
    {
        var model = _rig.CreateModel();

        model.Bands[0].GainDb = 4;
        model.PreampDb = -2;

        Assert.False(model.HasPendingSystemEqApply);
        Assert.Null(await model.FlushSystemEqApplyAsync());
        Assert.Empty(_rig.Backend.Applies);
        Assert.Equal(0, _rig.Backend.StatusCalls);
        Assert.False(File.Exists(_rig.SettingsPath));
    }

    [Fact]
    public async Task BlockedApplyStopsTheDebounce()
    {
        var model = await _rig.CreateSystemEqModelAsync();
        _rig.Backend.NextOutcome = SystemEqApplyOutcome.Blocked;

        model.Bands[0].GainDb = 2;
        await model.FlushSystemEqApplyAsync();

        Assert.Equal(SystemEqState.Blocked, model.SystemEqState);
        Assert.True(model.HasSystemEqWarning);
        Assert.Contains("can't write", model.SystemEqWarningText);
        Assert.Single(_rig.Backend.Applies);

        model.Bands[0].GainDb = 3;
        Assert.False(model.HasPendingSystemEqApply);
        await model.FlushSystemEqApplyAsync();
        Assert.Single(_rig.Backend.Applies);
    }

    [Fact]
    public async Task UnwritableConfigIsBlockedOnRefresh()
    {
        _rig.Backend.Status = _rig.Backend.Status with { IsConfigWritable = false, Message = "Access denied." };
        var model = await _rig.CreateSystemEqModelAsync(flush: false);

        Assert.Equal(SystemEqState.Blocked, model.SystemEqState);
        Assert.Contains("Access denied", model.SystemEqWarningText);
        Assert.Null(await model.FlushSystemEqApplyAsync());
        Assert.Empty(_rig.Backend.Applies);
    }

    [Fact]
    public async Task NoEndpointSelectedAsksToPickAnOutputAndAppliesHeaderOnly()
    {
        var model = _rig.CreateModel();
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;

        Assert.Contains("Pick an output", model.SystemEqStatusText);
        await model.FlushSystemEqApplyAsync();
        Assert.Empty(Assert.Single(_rig.Backend.Applies).EndpointGuids);
    }

    [Fact]
    public async Task TickingAnEndpointPersistsAndApplies()
    {
        var model = _rig.CreateModel();
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
        await model.FlushSystemEqApplyAsync();
        _rig.Backend.Applies.Clear();

        model.SystemEqEndpoints.Single(endpoint => endpoint.Id == HeadphonesId).IsSelected = true;
        await model.FlushSystemEqApplyAsync();

        Assert.Equal([HeadphonesGuid], Assert.Single(_rig.Backend.Applies).EndpointGuids);
        Assert.Equal([HeadphonesId], SystemEqSettings.Load(_rig.SettingsPath).EndpointIds);
    }

    [Fact]
    public void MissingApoNeedsInstallAndNoEndpointWithApoNeedsDeviceSelection()
    {
        _rig.Backend.Status = SystemEqBackendStatus.NotInstalled("Not installed.");
        var model = _rig.CreateModel();
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
        Assert.Equal(SystemEqState.NeedsInstall, model.SystemEqState);
        Assert.False(model.IsEqualizerApoInstalled);
        Assert.False(model.HasPendingSystemEqApply);

        _rig.Backend.Status = SystemEqTestRig.InstalledStatus;
        _rig.EndpointHealth[HeadphonesGuid] = new ApoEndpointStatus(false, false);
        _rig.EndpointHealth["{22222222-2222-2222-2222-222222222222}"] = new ApoEndpointStatus(false, false);
        model.RefreshSystemEqEndpointsCommand.Execute(null);
        Assert.Equal(SystemEqState.NeedsDeviceSelection, model.SystemEqState);
    }

    [Fact]
    public void InstallerRebootAndCancelUpdateStatusCalmly()
    {
        _rig.Backend.Status = SystemEqBackendStatus.NotInstalled("Not installed.");
        var model = _rig.CreateModel();
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;

        _rig.InstallerResult = new ApoLaunchResult(ApoLaunchOutcome.Cancelled, "UAC canceled.");
        var callsBefore = _rig.Backend.StatusCalls;
        model.InstallEqualizerApoCommand.Execute(null);
        Assert.Contains("canceled", model.SystemEqStatusText);
        Assert.Equal(callsBefore, _rig.Backend.StatusCalls);

        _rig.InstallerResult = new ApoLaunchResult(ApoLaunchOutcome.RebootRequired, "Reboot needed.");
        model.InstallEqualizerApoCommand.Execute(null);
        Assert.Equal(callsBefore + 1, _rig.Backend.StatusCalls);
        Assert.Contains("Restart Windows", model.SystemEqStatusText);

        _rig.Backend.Status = SystemEqTestRig.InstalledStatus;
        model.OpenApoDeviceSelectorCommand.Execute(null);
        Assert.Equal(1, _rig.DeviceSelectorRuns);
        Assert.Equal(SystemEqState.Active, model.SystemEqState);
        Assert.DoesNotContain("Restart", model.SystemEqStatusText);
    }

    [Fact]
    public async Task RemoveLeavesSystemEqAndRemovesInsteadOfBypassing()
    {
        var model = await _rig.CreateSystemEqModelAsync();

        model.RemoveSystemEqFromApoCommand.Execute(null);

        Assert.False(model.IsSystemEqSelected);
        Assert.Equal(1, _rig.Backend.RemoveCount);
        Assert.Empty(_rig.Backend.Applies);
        Assert.False(SystemEqSettings.Load(_rig.SettingsPath).Selected);
    }

    [Fact]
    public async Task StartupReappliesLastAppliedCurveInsteadOfFirstLibraryPreset()
    {
        var applied = SystemEqTestRig.Preset("Not In Library", gainDb: 3, preampDb: -3);
        WriteSettings(selected: true, lastApplied: applied);

        var model = _rig.CreateModel();
        var firstLibraryPreset = model.Presets[0];
        Assert.NotEqual(applied.Name, firstLibraryPreset.Name);
        Assert.Equal(3, model.Bands[0].GainDb);
        Assert.Equal(-3, model.PreampDb);
        // Not a library preset: guarded like unsaved editor content.
        Assert.True(model.EditorSession.WouldReplaceUnsavedEdits);

        await model.FlushSystemEqApplyAsync();
        var apply = Assert.Single(_rig.Backend.Applies);
        Assert.Equal(3, apply.Preset.Bands[0].GainDb);
        Assert.Equal(-3, apply.Preset.PreampDb);
        Assert.DoesNotContain(_rig.Backend.Applies, recorded => recorded.Preset.Name == firstLibraryPreset.Name);
    }

    [Fact]
    public async Task StartupSelectsTheLibraryPresetMatchingLastApplied()
    {
        var target = SystemEqTestRig.Preset("Second Preset", gainDb: 2, preampDb: -2);
        new LocalPresetLibraryService(_rig.LibraryPath).Save(
            [SystemEqTestRig.Preset("First Preset", gainDb: -1, preampDb: 0), target]);
        WriteSettings(selected: true, lastApplied: target);

        var model = _rig.CreateModel();
        Assert.Equal(target.Name, model.SelectedPreset!.Name);
        Assert.NotEqual(model.Presets[0].Name, model.SelectedPreset.Name);
        Assert.False(model.EditorSession.WouldReplaceUnsavedEdits);

        await model.FlushSystemEqApplyAsync();
        var apply = Assert.Single(_rig.Backend.Applies);
        Assert.Equal(target.Name, apply.Preset.Name);
        Assert.Equal(target.PreampDb, apply.Preset.PreampDb);
    }

    [Fact]
    public async Task StartupWithoutLastAppliedWritesNothingUntilTheUserEdits()
    {
        WriteSettings(selected: true, lastApplied: null);

        var model = _rig.CreateModel();
        Assert.True(model.IsSystemEqSelected);
        Assert.Equal(SystemEqState.Active, model.SystemEqState);
        Assert.False(model.HasPendingSystemEqApply);
        Assert.Null(await model.FlushSystemEqApplyAsync());

        model.RefreshSystemEqEndpointsCommand.Execute(null);
        Assert.Null(await model.FlushSystemEqApplyAsync());
        Assert.Empty(_rig.Backend.Applies);

        model.Bands[1].GainDb = 2;
        await model.FlushSystemEqApplyAsync();
        Assert.Equal(2, Assert.Single(_rig.Backend.Applies).Preset.Bands[1].GainDb);
    }

    [Fact]
    public async Task SuccessfulApplyPersistsLastAppliedEvenWhileBypassed()
    {
        var model = await _rig.CreateSystemEqModelAsync();
        model.Bands[0].GainDb = 6;
        await model.FlushSystemEqApplyAsync();
        Assert.Equal(6, SystemEqSettings.Load(_rig.SettingsPath).LastApplied!.Bands[0].GainDb);

        // Edits made while bypassed are the editor's curve too; restart must not roll them back.
        model.SystemEqBypassed = true;
        model.Bands[0].GainDb = -6;
        await model.FlushSystemEqApplyAsync();
        Assert.Equal(-6, SystemEqSettings.Load(_rig.SettingsPath).LastApplied!.Bands[0].GainDb);
    }

    [Fact]
    public async Task TickedOutputsStayTargetedWhileUnplugged()
    {
        var model = await _rig.CreateSystemEqModelAsync();
        model.SystemEqEndpoints.Single(endpoint => endpoint.Id == K13Id).IsSelected = true;
        await model.FlushSystemEqApplyAsync();

        // The K13 disappears (unplugged) and the outputs are re-listed.
        _rig.Catalog.Endpoints.RemoveAll(endpoint => endpoint.Id == K13Id);
        model.RefreshSystemEqEndpointsCommand.Execute(null);
        await model.SystemEqRefreshTask;
        _rig.Backend.Applies.Clear();
        model.Bands[0].GainDb = 3;
        await model.FlushSystemEqApplyAsync();

        Assert.Contains("{22222222-2222-2222-2222-222222222222}", _rig.Backend.Applies.Last().EndpointGuids, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TickedOutputWithoutApoWarnsAndIsLeftOutOfStatus()
    {
        _rig.EndpointHealth["{22222222-2222-2222-2222-222222222222}"] = new ApoEndpointStatus(false, false);
        var model = await _rig.CreateSystemEqModelAsync();
        Assert.Equal(SystemEqState.Active, model.SystemEqState);

        model.SystemEqEndpoints.Single(endpoint => endpoint.Id == K13Id).IsSelected = true;
        Assert.Equal("Software EQ on Headphones (USB Audio)", model.SystemEqStatusText);
        Assert.Contains("Equalizer APO isn't active on Speakers (FiiO K13 R2R). Use Add outputs to enable it.", model.SystemEqWarningText);

        model.SystemEqEndpoints.Single(endpoint => endpoint.Id == HeadphonesId).IsSelected = false;
        Assert.Equal("Equalizer APO isn't active on the selected output", model.SystemEqStatusText);
        Assert.True(model.HasSystemEqWarning);
    }

    [Fact]
    public async Task BypassAndApplyErrorsReplaceACanceledLauncherNotice()
    {
        var model = await _rig.CreateSystemEqModelAsync();
        _rig.DeviceSelectorResult = new ApoLaunchResult(ApoLaunchOutcome.Cancelled, "UAC canceled.");
        model.OpenApoDeviceSelectorCommand.Execute(null);
        Assert.Contains("canceled", model.SystemEqStatusText);

        model.ToggleSystemEqBypassCommand.Execute(null);
        Assert.Equal("Software EQ bypassed", model.SystemEqStatusText);
        model.ToggleSystemEqBypassCommand.Execute(null);
        await model.FlushSystemEqApplyAsync();

        model.OpenApoDeviceSelectorCommand.Execute(null);
        Assert.Contains("canceled", model.SystemEqStatusText);
        _rig.Backend.NextOutcome = SystemEqApplyOutcome.Failed;
        model.Bands[0].GainDb = 1;
        await model.FlushSystemEqApplyAsync();
        Assert.StartsWith("Couldn't apply Software EQ", model.SystemEqStatusText);
    }

    [Fact]
    public async Task EndpointQueriesRunOffTheCallingThreadAndOverlappingRefreshesSettle()
    {
        _rig.InlineEndpointQueries = false;
        using var gate = new ManualResetEventSlim(false);
        _rig.Catalog.Gate = gate;
        var model = _rig.CreateModel();

        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
        var first = model.SystemEqRefreshTask;
        model.RefreshSystemEqEndpointsCommand.Execute(null);
        var latest = model.SystemEqRefreshTask;
        Assert.NotSame(first, latest);
        // Inline queries would have blocked on the gate inside the setter and finished.
        Assert.False(first.IsCompleted);
        Assert.False(latest.IsCompleted);
        Assert.Empty(model.SystemEqEndpoints);

        gate.Set();
        await Task.WhenAll(first, latest);

        Assert.Equal(2, model.SystemEqEndpoints.Count);
        Assert.Equal(SystemEqState.Active, model.SystemEqState);
    }

    private void WriteSettings(bool selected, EqPreset? lastApplied)
    {
        var settings = SystemEqSettings.Load(_rig.SettingsPath);
        settings.Selected = selected;
        settings.EndpointIds = [HeadphonesId];
        settings.LastApplied = lastApplied is null ? null : SystemEqAppliedSnapshot.FromPreset(lastApplied);
        settings.Save();
    }

    [Fact]
    public void SettingsRoundTripUsesPlanJsonNames()
    {
        var settings = SystemEqSettings.Load(_rig.SettingsPath);
        settings.Selected = true;
        settings.Bypassed = true;
        settings.EndpointIds = [HeadphonesId];
        settings.Save();

        var json = File.ReadAllText(_rig.SettingsPath);
        Assert.Contains("\"selected\"", json);
        Assert.Contains("\"endpointIds\"", json);
        Assert.Contains("\"bypassed\"", json);
        var loaded = SystemEqSettings.Load(_rig.SettingsPath);
        Assert.True(loaded.Selected && loaded.Bypassed);
        Assert.Equal([HeadphonesId], loaded.EndpointIds);

        File.WriteAllText(_rig.SettingsPath, "{ not json");
        Assert.False(SystemEqSettings.Load(_rig.SettingsPath).Selected);
    }
}

/// <summary>Temp settings/library paths plus fake Software EQ services shared by Software EQ VM tests.</summary>

public sealed class SystemEqPickerOrderTests
{
    [Fact]
    public void WindowsSystemEqIsFirstInTheDevicePicker()
    {
        using var rig = new SystemEqTestRig();
        var model = rig.CreateModel();

        Assert.Same(SystemEqProfiles.WindowsSystemEq, model.DeviceProfiles[0]);
        Assert.Single(model.DeviceProfiles, profile => SystemEqProfiles.IsSystemEq(profile));
    }
}

internal sealed class SystemEqTestRig : IDisposable
{
    public static readonly SystemEqBackendStatus InstalledStatus =
        new(true, @"C:\Program Files\EqualizerAPO\config", @"C:\Program Files\EqualizerAPO", true, false, "Equalizer APO is installed.");

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wolfeq-systemeq-{Guid.NewGuid():N}");

    public SystemEqTestRig()
    {
        Directory.CreateDirectory(_directory);
        Catalog.Endpoints.Add(new AudioEndpointInfo(
            "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}", "{11111111-1111-1111-1111-111111111111}",
            "Headphones (USB Audio)", "Headphones", true));
        Catalog.Endpoints.Add(new AudioEndpointInfo(
            "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}", "{22222222-2222-2222-2222-222222222222}",
            "Speakers (FiiO K13 R2R)", "Speakers", false));
    }

    public string SettingsPath => Path.Combine(_directory, "system-eq.json");
    public string LibraryPath => Path.Combine(_directory, "library.json");
    public FakeEqOutputBackend Backend { get; } = new();
    public FakeEndpointCatalog Catalog { get; } = new();
    public Dictionary<string, ApoEndpointStatus> EndpointHealth { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ApoLaunchResult InstallerResult { get; set; } = new(ApoLaunchOutcome.Completed, "Done.");
    public ApoLaunchResult DeviceSelectorResult { get; set; } = new(ApoLaunchOutcome.Completed, "Done.");
    public int DeviceSelectorRuns { get; private set; }

    /// <summary>Endpoint queries run inline by default so most tests stay synchronous.</summary>
    public bool InlineEndpointQueries { get; set; } = true;

    public SystemEqServices Services => new(
        Backend,
        Catalog,
        guid => EndpointHealth.TryGetValue(guid, out var health) ? health : new ApoEndpointStatus(true, false),
        endpoint => endpoint.FriendlyName.Contains("FiiO", StringComparison.OrdinalIgnoreCase),
        () => Task.FromResult(InstallerResult),
        () =>
        {
            DeviceSelectorRuns++;
            return Task.FromResult(DeviceSelectorResult);
        })
    {
        QueryEndpointsInline = InlineEndpointQueries
    };

    public MainViewModel CreateModel()
        => new(new LocalPresetLibraryService(LibraryPath), Services, SettingsPath);

    /// <summary>A model in Software EQ mode with the headphones ticked and the backend call log cleared.</summary>
    public async Task<MainViewModel> CreateSystemEqModelAsync(bool flush = true)
    {
        var model = CreateModel();
        model.SelectedDeviceProfile = SystemEqProfiles.WindowsSystemEq;
        var headphones = model.SystemEqEndpoints.FirstOrDefault(endpoint => endpoint.IsDefault);
        if (headphones is not null)
        {
            headphones.IsSelected = true;
        }

        if (flush)
        {
            await model.FlushSystemEqApplyAsync();
        }

        Backend.Applies.Clear();
        return model;
    }

    public static EqPreset Preset(string name, double gainDb, double preampDb) => new()
    {
        Name = name,
        PreampDb = preampDb,
        Bands = new ObservableCollection<EqBand>(Enumerable.Range(1, 10).Select(number => new EqBand
        {
            Number = number,
            Enabled = true,
            FilterType = EqFilterType.Peak,
            FrequencyHz = 31 * (1 << (number - 1)),
            GainDb = gainDb,
            Q = 1
        }))
    };

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed record RecordedApply(EqPreset Preset, IReadOnlyList<string> EndpointGuids, bool Bypassed);

internal sealed class FakeEqOutputBackend : IEqOutputBackend
{
    public SystemEqBackendStatus Status { get; set; } = SystemEqTestRig.InstalledStatus;
    public SystemEqApplyOutcome? NextOutcome { get; set; }
    public List<RecordedApply> Applies { get; } = [];
    public int RemoveCount { get; private set; }
    public int StatusCalls { get; private set; }

    /// <summary>One-shot gate: the next apply is recorded, then waits until the gate completes.</summary>
    public TaskCompletionSource? ApplyGate { get; set; }

    public Task<SystemEqBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        return Task.FromResult(Status);
    }

    public async Task<SystemEqApplyResult> ApplyAsync(EqPreset preset, IReadOnlyList<string> endpointGuids, bool bypassed, CancellationToken cancellationToken = default)
    {
        Applies.Add(new RecordedApply(preset, endpointGuids.ToList(), bypassed));
        if (ApplyGate is { } gate)
        {
            ApplyGate = null;
            await gate.Task;
        }

        var outcome = NextOutcome
            ?? (bypassed ? SystemEqApplyOutcome.Bypassed : endpointGuids.Count == 0 ? SystemEqApplyOutcome.NoEndpoints : SystemEqApplyOutcome.Applied);
        return new SystemEqApplyResult(outcome, outcome == SystemEqApplyOutcome.Blocked ? "Access denied." : "ok");
    }

    public Task<SystemEqApplyResult> RemoveAsync(CancellationToken cancellationToken = default)
    {
        RemoveCount++;
        return Task.FromResult(new SystemEqApplyResult(SystemEqApplyOutcome.Applied, "Removed."));
    }
}

internal sealed class FakeEndpointCatalog : IAudioEndpointCatalog
{
    public List<AudioEndpointInfo> Endpoints { get; } = [];
    public ManualResetEventSlim? Gate { get; set; }

    public IReadOnlyList<AudioEndpointInfo> GetRenderEndpoints()
    {
        Gate?.Wait(TimeSpan.FromSeconds(10));
        return Endpoints.ToList();
    }
}
