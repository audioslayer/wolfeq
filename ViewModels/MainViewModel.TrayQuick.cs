using WolfEQ.Models;

namespace WolfEQ.ViewModels;

/// <summary>Quick presets shown in the tray flyout while Software EQ is selected.</summary>
public sealed partial class MainViewModel
{
    public const int TrayQuickPresetLimit = 6;

    /// <summary>
    /// Up to <see cref="TrayQuickPresetLimit"/> library presets: favorites first, then the
    /// rest, each group in library order. Computed on read, so it always matches
    /// <see cref="Presets"/>; listeners re-read it on <c>Presets.CollectionChanged</c>.
    /// </summary>
    public IReadOnlyList<EqPreset> TrayQuickPresets => SelectTrayQuickPresets(Presets);

    internal static IReadOnlyList<EqPreset> SelectTrayQuickPresets(IEnumerable<EqPreset> presets, int limit = TrayQuickPresetLimit)
    {
        var all = presets as IReadOnlyCollection<EqPreset> ?? presets.ToList();
        return all.Where(preset => preset.IsFavorite)
            .Concat(all.Where(preset => !preset.IsFavorite))
            .Take(Math.Max(0, limit))
            .ToList();
    }

    /// <summary>
    /// Opens a quick preset through the normal library path, so the unsaved-edits
    /// confirmation still applies. Software EQ applies the loaded content on its own.
    /// </summary>
    public bool TryOpenTrayQuickPreset(EqPreset preset)
    {
        if (!Presets.Contains(preset)) return false;
        var previous = LibrarySelection;
        LibrarySelection = preset;
        if (TryOpenLibrarySelection()) return true;
        LibrarySelection = previous;
        return false;
    }
}
