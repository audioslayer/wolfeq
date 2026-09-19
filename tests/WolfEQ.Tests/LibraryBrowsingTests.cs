using System.Collections.ObjectModel;
using System.IO;
using WolfEQ.Models;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class LibraryBrowsingTests
{
    [Fact]
    public void BrowsingDoesNotLoadAndExplicitOpenHonorsUnsavedChanges()
    {
        var model = new MainViewModel();
        var original = model.SelectedPreset;
        var other = new EqPreset { Name = "Browse candidate", Bands = new ObservableCollection<EqBand> { new() { Number = 1, FrequencyHz = 500, GainDb = 4, Q = 1 } } };
        model.Presets.Add(other);
        model.Bands[0].GainDb += 1;
        var gain = model.Bands[0].GainDb;
        model.LibrarySelection = other;
        Assert.Same(original, model.SelectedPreset);
        Assert.Equal(gain, model.Bands[0].GainDb);
        var prompts = 0;
        model.ConfirmDialog = (_, _) => { prompts++; return false; };
        Assert.False(model.TryOpenLibrarySelection());
        Assert.Same(original, model.SelectedPreset);
        model.ConfirmDialog = (_, _) => { prompts++; return true; };
        Assert.True(model.TryOpenLibrarySelection());
        Assert.Same(other, model.SelectedPreset);
        Assert.Equal(4, model.Bands[0].GainDb);
        Assert.Equal(2, prompts);
        Assert.True(model.TryOpenLibrarySelection());
        Assert.Equal(2, prompts);
    }

    [Fact]
    public void DuplicateAndNewStayInLibraryUntilOpened()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wolfeq-browse-{Guid.NewGuid()}.json");
        try
        {
            var model = new MainViewModel(new LocalPresetLibraryService(path));
            var editorPreset = model.SelectedPreset;
            model.DuplicatePresetCommand.Execute(null);
            Assert.Same(editorPreset, model.SelectedPreset);
            Assert.NotSame(editorPreset, model.LibrarySelection);
            Assert.StartsWith("Copy -", model.LibrarySelection!.Name);
            model.CreateFlatPresetCommand.Execute(null);
            Assert.Same(editorPreset, model.SelectedPreset);
            Assert.NotSame(editorPreset, model.LibrarySelection);
        }
        finally { File.Delete(path); }
    }
}
