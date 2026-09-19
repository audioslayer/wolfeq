using System.IO;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class NamedLibrarySaveTests
{
    [Fact]
    public void NewNameSavesSnapshotAndSameNameUpdatesWithoutDuplicate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wolfeq-library-{Guid.NewGuid()}.json");
        try
        {
            var library = new LocalPresetLibraryService(path);
            var model = new MainViewModel(library);
            var original = model.SelectedPreset;
            var count = model.Presets.Count;
            model.Bands[0].GainDb = 2.5;
            Assert.True(model.TrySaveNamedLibraryPreset("  My listening EQ  ", out var error), error);
            Assert.Equal(count + 1, model.Presets.Count);
            Assert.Contains(original!, model.Presets);
            Assert.Equal("My listening EQ", model.SelectedPreset!.Name);
            Assert.Equal(2.5, library.Load().Single(p => p.Name == "My listening EQ").Bands[0].GainDb);
            model.Bands[0].GainDb = 3;
            Assert.True(model.TrySaveNamedLibraryPreset("My listening EQ", out error), error);
            Assert.Equal(count + 1, model.Presets.Count);
            Assert.Equal(3, library.Load().Single(p => p.Name == "My listening EQ").Bands[0].GainDb);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InvalidNameAndFailedWriteLeaveSelectionAndLibraryIntact()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wolfeq-library-{Guid.NewGuid()}");
        var model = new MainViewModel(new LocalPresetLibraryService(path));
        model.Presets.Add(new WolfEQ.Models.EqPreset { Name = "Another preset" });
        var selected = model.SelectedPreset;
        var count = model.Presets.Count;
        Assert.False(model.TrySaveNamedLibraryPreset("  ", out var emptyError));
        Assert.Contains("Enter a name", emptyError);
        var other = model.Presets.First(p => !ReferenceEquals(p, selected));
        Assert.False(model.TrySaveNamedLibraryPreset(other.Name.ToUpperInvariant(), out var conflictError));
        Assert.Contains("already", conflictError);
        Directory.CreateDirectory(path); // A directory cannot be replaced with a library file.
        try
        {
            Assert.False(model.TrySaveNamedLibraryPreset("Cannot persist", out var writeError));
            Assert.Contains("Couldn't save", writeError);
            Assert.Same(selected, model.SelectedPreset);
            Assert.Equal(count, model.Presets.Count);
        }
        finally { Directory.Delete(path); }
    }
}
