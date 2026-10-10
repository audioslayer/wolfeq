using WolfEQ.Models;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

public sealed class TrayQuickPresetsTests : IDisposable
{
    private readonly SystemEqTestRig _rig = new();

    private static EqPreset Preset(string name, bool favorite = false) => new() { Name = name, IsFavorite = favorite };

    [Fact]
    public void FavoritesComeFirstThenLibraryOrder()
    {
        var list = MainViewModel.SelectTrayQuickPresets(
            [Preset("A"), Preset("B", favorite: true), Preset("C"), Preset("D", favorite: true)]);

        Assert.Equal(["B", "D", "A", "C"], list.Select(p => p.Name));
    }

    [Fact]
    public void ListIsCappedAtSix()
    {
        var presets = Enumerable.Range(1, 10).Select(i => Preset($"P{i}", favorite: i == 9)).ToList();

        var list = MainViewModel.SelectTrayQuickPresets(presets);

        Assert.Equal(MainViewModel.TrayQuickPresetLimit, list.Count);
        Assert.Equal(6, list.Count);
        Assert.Equal("P9", list[0].Name);
        Assert.Equal(["P9", "P1", "P2", "P3", "P4", "P5"], list.Select(p => p.Name));
    }

    [Fact]
    public void EmptyLibraryGivesEmptyList()
        => Assert.Empty(MainViewModel.SelectTrayQuickPresets([]));

    [Fact]
    public void ViewModelListFollowsPresetChanges()
    {
        var model = _rig.CreateModel();
        while (model.Presets.Count > 0) model.Presets.RemoveAt(0);
        Assert.Empty(model.TrayQuickPresets);

        var plain = Preset("Plain");
        model.Presets.Add(plain);
        Assert.Same(plain, Assert.Single(model.TrayQuickPresets));

        var favorite = Preset("Fav", favorite: true);
        model.Presets.Add(favorite);
        Assert.Equal([favorite, plain], model.TrayQuickPresets);

        for (var i = 0; i < 8; i++) model.Presets.Add(Preset($"Extra {i}"));
        Assert.Equal(6, model.TrayQuickPresets.Count);
        Assert.Same(favorite, model.TrayQuickPresets[0]);

        model.Presets.Remove(favorite);
        Assert.Same(plain, model.TrayQuickPresets[0]);
        Assert.DoesNotContain(favorite, model.TrayQuickPresets);
    }

    [Fact]
    public void OpeningAQuickPresetLoadsItIntoTheEditor()
    {
        var model = _rig.CreateModel();
        var target = Preset("Target");
        model.Presets.Add(target);

        Assert.True(model.TryOpenTrayQuickPreset(target));

        Assert.Same(target, model.SelectedPreset);
        Assert.Same(target, model.LibrarySelection);
    }

    [Fact]
    public void DecliningTheUnsavedEditsPromptKeepsTheEditor()
    {
        var model = _rig.CreateModel();
        var original = model.SelectedPreset;
        var target = Preset("Target");
        model.Presets.Add(target);
        model.EditorSession.NotifyEdit();
        model.ConfirmDialog = (_, _) => false;

        Assert.False(model.TryOpenTrayQuickPreset(target));

        Assert.Same(original, model.SelectedPreset);
    }

    public void Dispose() => _rig.Dispose();
}
