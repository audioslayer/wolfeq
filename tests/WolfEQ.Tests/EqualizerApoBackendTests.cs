using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using WolfEQ.Models;
using WolfEQ.Services.SystemEq;
using Xunit;

namespace WolfEQ.Tests;

public sealed class EqualizerApoBackendTests : IDisposable
{
    private const string GuidA = "{11111111-2222-3333-4444-555555555555}";
    private const string GuidB = "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wolfeq-apo-{Guid.NewGuid():N}");

    public EqualizerApoBackendTests() => Directory.CreateDirectory(_directory);

    private string IncludePath => Path.Combine(_directory, "WolfEQ.txt");

    private string ConfigPath => Path.Combine(_directory, "config.txt");

    public void Dispose()
    {
        if (!Directory.Exists(_directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_directory, recursive: true);
    }

    private EqualizerApoBackend CreateBackend(int maxAttempts = 5, TimeSpan? retryDelay = null)
        => new(_ => Task.FromResult(InstalledStatus()), maxAttempts, retryDelay);

    private SystemEqBackendStatus InstalledStatus()
        => new(true, _directory, @"C:\Fake\EqualizerAPO", true, false, "Equalizer APO is installed.");

    [Fact]
    public async Task ApplyWritesIncludeWithoutBomAndCreatesConfigBlock()
    {
        var preset = EqPreset.AnandaMusic();

        var result = await CreateBackend().ApplyAsync(preset, [GuidA, GuidB], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.Applied, result.Outcome);
        Assert.True(result.Succeeded);
        var bytes = File.ReadAllBytes(IncludePath);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(EqualizerApoConfigWriter.Build(preset, [GuidA, GuidB], false), Encoding.UTF8.GetString(bytes));
        Assert.Equal(
            "# >>> WolfEQ (managed) >>>\r\nDevice: all\r\nInclude: WolfEQ.txt\r\n# <<< WolfEQ <<<\r\n",
            File.ReadAllText(ConfigPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ApplyKeepsPeaceIncludeAndRemoveRestoresConfigBytesExactly()
    {
        var original = Encoding.UTF8.GetBytes("Include: peace.txt\r\n# user comment\r\n");
        File.WriteAllBytes(ConfigPath, original);
        var backend = CreateBackend();

        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.StartsWith("# >>> WolfEQ (managed) >>>\r\n", File.ReadAllText(ConfigPath));
        Assert.EndsWith("# <<< WolfEQ <<<\r\nInclude: peace.txt\r\n# user comment\r\n", File.ReadAllText(ConfigPath));

        var removed = await backend.RemoveAsync();

        Assert.True(removed.Succeeded);
        Assert.Equal(original, File.ReadAllBytes(ConfigPath));
        Assert.False(File.Exists(IncludePath));
    }

    [Fact]
    public async Task BypassWritesHeaderOnlyAndKeepsBlock()
    {
        var backend = CreateBackend();
        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        var result = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: true);

        Assert.Equal(SystemEqApplyOutcome.Bypassed, result.Outcome);
        Assert.Equal(EqualizerApoConfigWriter.HeaderComment + "\r\n", File.ReadAllText(IncludePath));
        Assert.True(ApoConfigIncludeManager.HasBlock(File.ReadAllText(ConfigPath)));
    }

    [Fact]
    public async Task NoEndpointsWritesHeaderOnly()
    {
        var result = await CreateBackend().ApplyAsync(EqPreset.AnandaMusic(), [], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.NoEndpoints, result.Outcome);
        Assert.Equal(EqualizerApoConfigWriter.HeaderComment + "\r\n", File.ReadAllText(IncludePath));
    }

    [Fact]
    public async Task NotInstalledOrMissingConfigFolderWritesNothing()
    {
        var notInstalled = new EqualizerApoBackend(_ => Task.FromResult(SystemEqBackendStatus.NotInstalled("APO missing")));
        var missingFolder = new EqualizerApoBackend(_ => Task.FromResult(
            InstalledStatus() with { ConfigDirectory = Path.Combine(_directory, "missing") }));

        var first = await notInstalled.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        var second = await missingFolder.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.NotInstalled, first.Outcome);
        Assert.Equal("APO missing", first.Message);
        Assert.Equal(SystemEqApplyOutcome.NotInstalled, second.Outcome);
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }

    [Fact]
    public async Task StatusProviderFailureReturnsFailed()
    {
        var backend = new EqualizerApoBackend(_ => throw new InvalidOperationException("registry broke"));

        var result = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task GetStatusDelegatesToProvider()
    {
        var status = InstalledStatus();
        var backend = new EqualizerApoBackend(_ => Task.FromResult(status));

        Assert.Same(status, await backend.GetStatusAsync());
    }

    [Fact]
    public async Task ReadOnlyIncludeIsBlockedAndLeavesNoTempFile()
    {
        File.WriteAllText(IncludePath, "# old content\r\n");
        File.SetAttributes(IncludePath, FileAttributes.ReadOnly);

        var result = await CreateBackend().ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.Blocked, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Contains(_directory, result.Message);
        Assert.Equal("# old content\r\n", File.ReadAllText(IncludePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task ReadOnlyConfigIsBlockedAndLeavesNoTempFile()
    {
        File.WriteAllText(ConfigPath, "Include: peace.txt\r\n");
        File.SetAttributes(ConfigPath, FileAttributes.ReadOnly);

        var result = await CreateBackend().ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.Blocked, result.Outcome);
        Assert.Equal("Include: peace.txt\r\n", File.ReadAllText(ConfigPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task LockedIncludeFileIsRetriedUntilReleased()
    {
        File.WriteAllText(IncludePath, "# old content\r\n");
        var backend = CreateBackend();

        Task<SystemEqApplyResult> apply;
        using (new FileStream(IncludePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            apply = Task.Run(() => backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false));
            await Task.Delay(100);
        }

        var result = await apply;

        Assert.Equal(SystemEqApplyOutcome.Applied, result.Outcome);
        Assert.Contains("Filter 1: ON LSC", File.ReadAllText(IncludePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task LockThatOutlastsRetriesFailsWithoutTempFile()
    {
        File.WriteAllText(IncludePath, "# old content\r\n");
        var backend = CreateBackend(maxAttempts: 3, retryDelay: TimeSpan.FromMilliseconds(10));

        SystemEqApplyResult result;
        using (new FileStream(IncludePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        }

        Assert.Equal(SystemEqApplyOutcome.Failed, result.Outcome);
        Assert.Equal("# old content\r\n", File.ReadAllText(IncludePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task UnchangedContentIsNotRewritten()
    {
        var backend = CreateBackend();
        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        var old = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(IncludePath, old);
        File.SetLastWriteTimeUtc(ConfigPath, old);

        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(old, File.GetLastWriteTimeUtc(IncludePath));
        Assert.Equal(old, File.GetLastWriteTimeUtc(ConfigPath));

        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: true);

        Assert.NotEqual(old, File.GetLastWriteTimeUtc(IncludePath));
        Assert.Equal(old, File.GetLastWriteTimeUtc(ConfigPath));
    }

    [Fact]
    public async Task ApplyRepairsBlockRemovedByAnotherTool()
    {
        var backend = CreateBackend();
        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        File.WriteAllText(ConfigPath, "Include: peace.txt\r\n");

        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(
            "# >>> WolfEQ (managed) >>>\r\nDevice: all\r\nInclude: WolfEQ.txt\r\n# <<< WolfEQ <<<\r\nInclude: peace.txt\r\n",
            File.ReadAllText(ConfigPath));
    }

    [Fact]
    public async Task ConcurrentAppliesAreSerializedAndLeaveOneCompleteFile()
    {
        var backend = CreateBackend();
        var presets = Enumerable.Range(0, 16).Select(i => new EqPreset
        {
            PreampDb = -i,
            Bands = new ObservableCollection<EqBand>
            {
                new() { Number = 1, FilterType = EqFilterType.Peak, FrequencyHz = 100 + i, GainDb = 1, Q = 1 }
            }
        }).ToList();

        var results = await Task.WhenAll(presets.Select(p => Task.Run(() => backend.ApplyAsync(p, [GuidA], false))));

        Assert.All(results, r => Assert.Equal(SystemEqApplyOutcome.Applied, r.Outcome));
        var finalText = File.ReadAllText(IncludePath);
        Assert.Contains(presets, p => EqualizerApoConfigWriter.Build(p, [GuidA], false) == finalText);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task ManyAppliesCallStatusProviderOnce()
    {
        var calls = 0;
        var backend = new EqualizerApoBackend(_ => { calls++; return Task.FromResult(InstalledStatus()); });

        for (var i = 0; i < 10; i++)
        {
            var result = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: i % 2 == 0);
            Assert.True(result.Succeeded);
        }

        Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(_directory, ".wolfeq-write-test-*"));
    }

    [Fact]
    public async Task GetStatusAlwaysRefreshesAndWarmsTheApplyCache()
    {
        var calls = 0;
        var backend = new EqualizerApoBackend(_ => { calls++; return Task.FromResult(InstalledStatus()); });

        await backend.GetStatusAsync();
        await backend.GetStatusAsync();
        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidB], bypassed: false);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CachedNotInstalledStatusIsRefreshedByGetStatus()
    {
        var installed = false;
        var backend = new EqualizerApoBackend(_ => Task.FromResult(
            installed ? InstalledStatus() : SystemEqBackendStatus.NotInstalled("APO missing")));

        Assert.Equal(SystemEqApplyOutcome.NotInstalled, (await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], false)).Outcome);

        installed = true;
        await backend.GetStatusAsync();

        Assert.Equal(SystemEqApplyOutcome.Applied, (await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], false)).Outcome);
    }

    [Fact]
    public async Task AccessDeniedInvalidatesCacheSoNextApplyRedetects()
    {
        var calls = 0;
        var backend = new EqualizerApoBackend(
            _ => { calls++; return Task.FromResult(InstalledStatus()); }, maxAttempts: 2, retryDelay: TimeSpan.FromMilliseconds(1));
        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        File.SetAttributes(IncludePath, FileAttributes.ReadOnly);

        var blocked = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidB], bypassed: false);
        File.SetAttributes(IncludePath, FileAttributes.Normal);
        var retried = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidB], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.Blocked, blocked.Outcome);
        Assert.Equal(SystemEqApplyOutcome.Applied, retried.Outcome);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MissingConfigFolderInvalidatesCache()
    {
        var calls = 0;
        var backend = new EqualizerApoBackend(_ => { calls++; return Task.FromResult(InstalledStatus()); });
        await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        Directory.Delete(_directory, recursive: true);

        var missing = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        Directory.CreateDirectory(_directory);
        var again = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.NotInstalled, missing.Outcome);
        Assert.Equal(SystemEqApplyOutcome.Applied, again.Outcome);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TransientAccessDeniedOnReplaceIsRetried()
    {
        File.WriteAllText(IncludePath, "# old content\r\n");
        File.SetAttributes(IncludePath, FileAttributes.ReadOnly);
        var backend = CreateBackend(maxAttempts: 10, retryDelay: TimeSpan.FromMilliseconds(30));

        // Simulates antivirus briefly denying the replace, then letting go.
        var release = Task.Run(async () =>
        {
            await Task.Delay(80);
            File.SetAttributes(IncludePath, FileAttributes.Normal);
        });
        var result = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        await release;

        Assert.Equal(SystemEqApplyOutcome.Applied, result.Outcome);
        Assert.Contains("Filter 1: ON LSC", File.ReadAllText(IncludePath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task PersistentAccessDeniedInWritableFolderIsFailedNotBlocked()
    {
        // A folder squatting on WolfEQ.txt makes the replace fail with access denied while the folder stays writable.
        Directory.CreateDirectory(IncludePath);
        var backend = CreateBackend(maxAttempts: 3, retryDelay: TimeSpan.FromMilliseconds(1));

        var result = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task PersistentAccessDeniedWithUnwritableFolderIsBlocked()
    {
        Directory.CreateDirectory(IncludePath);
        var backend = new EqualizerApoBackend(
            _ => Task.FromResult(InstalledStatus()), maxAttempts: 3, retryDelay: TimeSpan.FromMilliseconds(1), canWriteDirectory: _ => false);

        var result = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);

        Assert.Equal(SystemEqApplyOutcome.Blocked, result.Outcome);
    }

    [Fact]
    public async Task RemoveCleansLeftoverConfigFolderWhenApoIsNotInstalled()
    {
        var original = Encoding.UTF8.GetBytes("Include: peace.txt\r\n");
        File.WriteAllBytes(ConfigPath, original);
        await CreateBackend().ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        var leftover = new SystemEqBackendStatus(false, _directory, @"C:\Fake\EqualizerAPO", false, false, "Not installed.");
        var backend = new EqualizerApoBackend(_ => Task.FromResult(leftover));

        var applied = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidB], bypassed: false);
        var removed = await backend.RemoveAsync();

        Assert.Equal(SystemEqApplyOutcome.NotInstalled, applied.Outcome);
        Assert.True(removed.Succeeded);
        Assert.Equal(original, File.ReadAllBytes(ConfigPath));
        Assert.False(File.Exists(IncludePath));
    }

    [Fact]
    public async Task Utf8BomConfigWithInvalidBytesIsHandledAndRestoredExactly()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, 0x23, 0x20, 0xE9, 0xFF, 0x0D, 0x0A];
        File.WriteAllBytes(ConfigPath, original);
        var backend = CreateBackend();

        var applied = await backend.ApplyAsync(EqPreset.AnandaMusic(), [GuidA], bypassed: false);
        var removed = await backend.RemoveAsync();

        Assert.Equal(SystemEqApplyOutcome.Applied, applied.Outcome);
        Assert.True(removed.Succeeded);
        Assert.Equal(original, File.ReadAllBytes(ConfigPath));
    }

    [Fact]
    public async Task RemoveWithNothingPresentSucceeds()
    {
        var result = await CreateBackend().RemoveAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(Directory.GetFileSystemEntries(_directory));
    }
}
