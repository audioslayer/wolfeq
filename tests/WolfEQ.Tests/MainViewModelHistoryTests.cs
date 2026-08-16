using WolfEQ.ViewModels;
using System.Reflection;
using Xunit;

namespace WolfEQ.Tests;

public sealed class MainViewModelHistoryTests
{
    [Fact]
    public void UndoAndRedo_RestoreGroupedBandAndPreampEdits()
    {
        var viewModel = new MainViewModel();
        var originalGain = viewModel.Bands[0].GainDb;
        var originalPreamp = viewModel.PreampDb;
        var editedPreamp = originalPreamp < 12 ? originalPreamp + 1.5 : originalPreamp - 1.5;

        viewModel.Bands[0].GainDb = originalGain + 1.5;
        viewModel.PreampDb = editedPreamp;

        Assert.True(viewModel.CanUndo);
        Assert.True(viewModel.UndoCommand.CanExecute(null));
        viewModel.UndoCommand.Execute(null);
        Assert.Equal(originalGain, viewModel.Bands[0].GainDb);
        Assert.Equal(originalPreamp, viewModel.PreampDb);

        Assert.True(viewModel.CanRedo);
        Assert.True(viewModel.RedoCommand.CanExecute(null));
        viewModel.RedoCommand.Execute(null);
        Assert.Equal(originalGain + 1.5, viewModel.Bands[0].GainDb);
        Assert.Equal(editedPreamp, viewModel.PreampDb);
    }

    [Fact]
    public void NewEditAfterUndo_ClearsRedoHistory()
    {
        var viewModel = new MainViewModel();
        var originalGain = viewModel.Bands[0].GainDb;

        viewModel.Bands[0].GainDb = originalGain + 1;
        viewModel.UndoCommand.Execute(null);
        Assert.True(viewModel.RedoCommand.CanExecute(null));

        viewModel.Bands[0].GainDb = originalGain - 1;
        viewModel.RedoCommand.Execute(null);

        Assert.False(viewModel.RedoCommand.CanExecute(null));
        Assert.Equal(originalGain - 1, viewModel.Bands[0].GainDb);
    }

    [Fact]
    public void OptimizeHeadroom_MatchesStrongestEnabledBoost()
    {
        var viewModel = new MainViewModel();
        foreach (var band in viewModel.Bands)
        {
            band.Enabled = true;
            band.GainDb = 0;
        }

        viewModel.Bands[0].GainDb = 2.5;
        viewModel.Bands[1].GainDb = 4.0;
        viewModel.Bands[2].GainDb = -7.0;
        viewModel.Bands[3].GainDb = 8.0;
        viewModel.Bands[3].Enabled = false;
        viewModel.PreampDb = 0;

        Assert.Equal(4.0, viewModel.MaxEnabledBoostDb);
        Assert.Equal(-4.0, viewModel.RecommendedPreampDb);
        Assert.Equal("Peak +4.0 dB → preamp -4.0 dB", viewModel.OptimizedHeadroomPreviewText);

        viewModel.ApplyAutoHeadroomCommand.Execute(null);

        Assert.Equal(-4.0, viewModel.PreampDb);
        Assert.Equal("0.0 dB margin", viewModel.HeadroomMarginText);
        Assert.Equal("Safe Headroom", viewModel.HeadroomGuardianTitle);
    }

    [Fact]
    public void OptimizeHeadroom_WithLiveSync_QueuesPreampWrite()
    {
        var viewModel = new MainViewModel();
        foreach (var band in viewModel.Bands)
        {
            band.Enabled = true;
            band.GainDb = 0;
        }

        viewModel.PreampDb = 0;
        typeof(MainViewModel)
            .GetMethod("SetConnectionState", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(viewModel, [true]);

        viewModel.Bands[0].GainDb = 4;
        viewModel.ApplyAutoHeadroomCommand.Execute(null);

        var pending = (bool)typeof(MainViewModel)
            .GetField("_pendingLivePreampSync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewModel)!;
        Assert.True(pending);
        Assert.Equal(-4, viewModel.PreampDb);
        Assert.Equal("Live sync queued", viewModel.LiveDeviceEqSyncStatus);
    }
}
