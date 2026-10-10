using System.IO;
using System.Runtime.CompilerServices;
using WolfEQ.Services.SystemEq;
using WolfEQ.ViewModels;

namespace WolfEQ.Tests;

/// <summary>
/// Keeps tests away from the real machine: System EQ settings go to a temp folder and
/// <c>new MainViewModel()</c> gets inert System EQ services, so no test can write into
/// Equalizer APO's config folder, pick up a developer's persisted System EQ choice, or flip
/// their saved Light/Dark theme.
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var root = Path.Combine(Path.GetTempPath(), "WolfEQ.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        SystemEqSettings.DefaultPathOverride = Path.Combine(root, "system-eq.json");
        // Theme checks flip Light/Dark; never let that reach the developer's real preference.
        WolfEQ.Services.BackgroundThemeService.PreferencePathOverride = Path.Combine(root, "background-theme.txt");
        // View checks star, rename and delete presets; never let that touch the real library.
        WolfEQ.Services.LocalPresetLibraryService.DefaultPathOverride = Path.Combine(root, "profiles.json");
        SystemEqServices.DefaultOverride = () => new SystemEqServices(
            new NullEqOutputBackend(),
            new EmptyAudioEndpointCatalog(),
            _ => new ApoEndpointStatus(false, false),
            _ => false,
            () => Task.FromResult(new ApoLaunchResult(ApoLaunchOutcome.NotFound, "test")),
            () => Task.FromResult(new ApoLaunchResult(ApoLaunchOutcome.NotFound, "test")));
    }
}
