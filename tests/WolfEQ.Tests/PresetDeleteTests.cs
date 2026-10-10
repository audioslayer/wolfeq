using System.IO;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class PresetDeleteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wolfeq-delete-{Guid.NewGuid():N}");

    private MainViewModel CreateModel()
    {
        Directory.CreateDirectory(_directory);
        return new MainViewModel(new LocalPresetLibraryService(Path.Combine(_directory, "library.json")));
    }

    [Fact]
    public void DeleteAsksWithADestructiveRequestNamingThePreset()
    {
        var model = CreateModel();
        var target = model.Presets.Last();
        model.LibrarySelection = target;
        ConfirmationRequest? asked = null;
        model.ConfirmationDialog = request => { asked = request; return false; };

        model.DeletePresetCommand.Execute(null);

        Assert.NotNull(asked);
        Assert.True(asked!.IsDestructive);
        Assert.Equal("Delete", asked.ConfirmText);
        Assert.Contains(target.Name, asked.Message);
        Assert.Contains(target, model.Presets);
    }

    [Fact]
    public void ConfirmedDeleteRemovesThePresetAndSelectsANeighbour()
    {
        var model = CreateModel();
        model.LibrarySelection = model.Presets[0];
        model.DuplicatePresetCommand.Execute(null);
        var count = model.Presets.Count;
        Assert.True(count >= 2);
        var target = model.Presets.Last();
        model.LibrarySelection = target;
        model.ConfirmationDialog = _ => true;

        model.DeletePresetCommand.Execute(null);

        Assert.DoesNotContain(target, model.Presets);
        Assert.Equal(count - 1, model.Presets.Count);
        Assert.NotNull(model.LibrarySelection);
        Assert.NotSame(target, model.LibrarySelection);
    }

    [Fact]
    public void DeletingTheOpenPresetMentionsTheEditor()
    {
        var model = CreateModel();
        model.LibrarySelection = model.SelectedPreset;
        ConfirmationRequest? asked = null;
        model.ConfirmationDialog = request => { asked = request; return false; };

        model.DeletePresetCommand.Execute(null);

        Assert.Contains("open in the editor", asked!.Detail);
    }

    [Fact]
    public void StarringAPresetAddsItToFavoritesAndSavesTheLibrary()
    {
        var model = CreateModel();
        var target = model.Presets[0];
        target.IsFavorite = false;
        model.LibraryTab = MainViewModel.LibraryTabFavorites;
        Assert.DoesNotContain(target, model.FilteredPresets.Cast<WolfEQ.Models.EqPreset>());

        model.ToggleLibraryFavoriteCommand.Execute(target);

        Assert.True(target.IsFavorite);
        Assert.Contains(target, model.FilteredPresets.Cast<WolfEQ.Models.EqPreset>());
        var saved = new LocalPresetLibraryService(Path.Combine(_directory, "library.json"));
        model.LibraryTab = MainViewModel.LibraryTabMine;
        model.LibrarySelection = target;
        Assert.True(model.LibraryDetailIsFavorite);

        model.ToggleLibraryFavoriteCommand.Execute(null);
        Assert.False(target.IsFavorite);
        Assert.False(model.LibraryDetailIsFavorite);
    }

    [Fact]
    public void RenamingAPresetUpdatesItAndValidatesTheName()
    {
        var model = CreateModel();
        model.LibrarySelection = model.Presets[0];
        model.DuplicatePresetCommand.Execute(null);
        var first = model.Presets[0];
        var second = model.Presets.Last();
        TextPromptRequest? asked = null;
        model.TextPromptDialog = request => { asked = request; return "  Late night  "; };

        model.RenameLibraryPresetCommand.Execute(second);

        Assert.NotNull(asked);
        Assert.Equal(second.Name, "Late night");
        Assert.Equal("Rename", asked!.ConfirmText);
        Assert.Equal("Enter a name.", asked.Validate!(""));
        Assert.Equal("Another preset already has this name.", asked.Validate!(first.Name.ToUpperInvariant()));
        Assert.Null(asked.Validate!("Something new"));
        Assert.StartsWith("Use 80 characters", model.ValidatePresetName(second, new string('x', 81)));

        Assert.False(model.RenameLibraryPreset(second, first.Name));
        Assert.Equal("Late night", second.Name);
    }

    [Fact]
    public void CancellingTheRenamePromptKeepsTheName()
    {
        var model = CreateModel();
        var preset = model.Presets[0];
        var original = preset.Name;
        model.TextPromptDialog = _ => null;

        model.RenameLibraryPresetCommand.Execute(preset);

        Assert.Equal(original, preset.Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }
}
