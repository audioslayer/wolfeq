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

/// <summary>Temp settings/library paths plus fake System EQ services shared by System EQ VM tests.</summary>
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
    public int DeviceSelectorRuns { get; private set; }

    public SystemEqServices Services => new(
        Backend,
        Catalog,
        guid => EndpointHealth.TryGetValue(guid, out var health) ? health : new ApoEndpointStatus(true, false),
        endpoint => endpoint.FriendlyName.Contains("FiiO", StringComparison.OrdinalIgnoreCase),
        () => Task.FromResult(InstallerResult),
        () =>
        {
            DeviceSelectorRuns++;
            return Task.FromResult(new ApoLaunchResult(ApoLaunchOutcome.Completed, "Done."));
        });

    public MainViewModel CreateModel()
        => new(new LocalPresetLibraryService(LibraryPath), Services, SettingsPath);

    /// <summary>A model in System EQ mode with the headphones ticked and the backend call log cleared.</summary>
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

    public Task<SystemEqBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        StatusCalls++;
        return Task.FromResult(Status);
    }

    public Task<SystemEqApplyResult> ApplyAsync(EqPreset preset, IReadOnlyList<string> endpointGuids, bool bypassed, CancellationToken cancellationToken = default)
    {
        Applies.Add(new RecordedApply(preset, endpointGuids.ToList(), bypassed));
        var outcome = NextOutcome
            ?? (bypassed ? SystemEqApplyOutcome.Bypassed : endpointGuids.Count == 0 ? SystemEqApplyOutcome.NoEndpoints : SystemEqApplyOutcome.Applied);
        return Task.FromResult(new SystemEqApplyResult(outcome, outcome == SystemEqApplyOutcome.Blocked ? "Access denied." : "ok"));
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
    public IReadOnlyList<AudioEndpointInfo> GetRenderEndpoints() => Endpoints;
}
