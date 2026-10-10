using System.Windows.Input;
using WolfEQ.Models;

namespace WolfEQ.ViewModels;

/// <summary>Renaming saved presets from the library list or preview header.</summary>
public sealed partial class MainViewModel
{
    public const int MaxPresetNameLength = 80;

    private ICommand? _renameLibraryPresetCommand;

    /// <summary>Shows WolfEQ's text prompt; returns the entered text or null when cancelled. Wired by the view.</summary>
    public Func<TextPromptRequest, string?>? TextPromptDialog { get; set; }

    /// <summary>Renames the row's preset, or the library selection when no parameter is given.</summary>
    public ICommand RenameLibraryPresetCommand
        => _renameLibraryPresetCommand ??= new AsyncParameterRelayCommand(parameter =>
        {
            PromptRenameLibraryPreset(parameter as EqPreset ?? LibrarySelection);
            return Task.CompletedTask;
        });

    /// <summary>Returns why <paramref name="name"/> can't be used for <paramref name="preset"/>, or null when it can.</summary>
    internal string? ValidatePresetName(EqPreset preset, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "Enter a name.";
        }

        if (trimmed.Length > MaxPresetNameLength)
        {
            return $"Use {MaxPresetNameLength} characters or fewer.";
        }

        if (trimmed.Any(char.IsControl))
        {
            return "Names can't contain control characters.";
        }

        return Presets.Any(other => !ReferenceEquals(other, preset) && string.Equals(other.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            ? "Another preset already has this name."
            : null;
    }

    private void PromptRenameLibraryPreset(EqPreset? preset)
    {
        if (preset is null || !Presets.Contains(preset) || TextPromptDialog is not { } prompt)
        {
            return;
        }

        var newName = prompt(new TextPromptRequest(
            "Rename preset",
            string.Empty,
            preset.Name,
            "Rename",
            text => ValidatePresetName(preset, text)));
        if (newName is not null)
        {
            RenameLibraryPreset(preset, newName);
        }
    }

    /// <summary>Applies a validated rename and saves the library. Returns false when the name is rejected.</summary>
    internal bool RenameLibraryPreset(EqPreset preset, string newName)
    {
        var trimmed = newName.Trim();
        if (!Presets.Contains(preset) || ValidatePresetName(preset, trimmed) is not null)
        {
            return false;
        }

        if (string.Equals(preset.Name, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        var oldName = preset.Name;
        preset.Name = trimmed;
        Status = $"Renamed {oldName} to {trimmed}.";
        AddLog(Status);

        // EqPreset has no change notifications; refreshing rebuilds the rows with the new name.
        var selection = LibrarySelection;
        RefreshProfileLibrary();
        if (selection is not null && FilteredPresets.Cast<EqPreset>().Contains(selection))
        {
            LibrarySelection = selection;
        }

        if (ReferenceEquals(preset, SelectedPreset))
        {
            OnPropertyChanged(nameof(SelectedPreset));
        }

        RaiseLibraryDetailChanged();
        QueueProfileAutosave();
        return true;
    }
}
