using System.IO;
using WolfEQ.Models;

namespace WolfEQ.Services;

public sealed class LocalPresetLibraryService
{
    public LocalPresetLibraryService()
    {
        LibraryPath = Path.Combine(
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
