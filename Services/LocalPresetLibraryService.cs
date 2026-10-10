using System.IO;
using WolfEQ.Models;

namespace WolfEQ.Services;

public sealed class LocalPresetLibraryService
{
    /// <summary>Test hook: keeps tests that build a default view model away from the user's real library.</summary>
    internal static string? DefaultPathOverride { get; set; }

    public LocalPresetLibraryService(string? libraryPath = null)
    {
        LibraryPath = libraryPath ?? DefaultPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WolfEQ",
            "profiles.json");
    }

    public string LibraryPath { get; }

    public IReadOnlyList<EqPreset> Load()
    {
        if (!File.Exists(LibraryPath))
        {
            return [];
        }

        var json = BoundedTextReader.ReadAllText(
            LibraryPath,
            BoundedTextReader.LibraryMaxBytes,
            "Local preset library");
        return WolfEqPresetJsonCodec.ImportLibrary(json, "WolfEQ local profiles");
    }

    public void Save(IEnumerable<EqPreset> presets)
    {
        AtomicFileWriter.WriteAllText(LibraryPath, WolfEqPresetJsonCodec.ExportLibrary(presets));
    }
}
