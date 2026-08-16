using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using WolfEQ.Controls;
using WolfEQ.Models;
using Xunit;

namespace WolfEQ.Tests;

public sealed class EqResponseGraphControlTests
{
    [Fact]
    public void ResponseAnchoredDrag_PreservesGainAtCurrentCombinedCurveHeight()
        => RunSta(() =>
        {
            var shelf = new EqBand
            {
                Number = 1,
                Enabled = true,
                FilterType = EqFilterType.LowShelf,
                FrequencyHz = 100,
                GainDb = 6,
                Q = 0.7
            };
            var peak = new EqBand
            {
                Number = 2,
                Enabled = true,
                FilterType = EqFilterType.Peak,
                FrequencyHz = 100,
                GainDb = -2,
                Q = 1
            };
            var graph = new EqResponseGraphControl
            {
                Bands = new ObservableCollection<EqBand> { shelf, peak },
                MinGainDb = -12,
                MaxGainDb = 12
            };

            var estimate = typeof(EqResponseGraphControl).GetMethod(
                "EstimateGain",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(double)],
                modifiers: null)!;
            var solve = typeof(EqResponseGraphControl).GetMethod(
                "SolveBandGainForTargetResponse",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var currentResponse = (double)estimate.Invoke(graph, [100d])!;

            var solvedGain = (double)solve.Invoke(graph, [peak, 100d, currentResponse])!;

            Assert.InRange(solvedGain, peak.GainDb - 0.1, peak.GainDb + 0.1);
        });

    [Fact]
    public void DisplayNumbers_FollowFrequencyOrderWithoutChangingHardwareNumbers()
        => RunSta(() =>
        {
            var high = CreateBand(number: 1, frequencyHz: 1000);
            var low = CreateBand(number: 5, frequencyHz: 100);
            var middle = CreateBand(number: 2, frequencyHz: 500);
            var graph = new EqResponseGraphControl
            {
                Bands = new ObservableCollection<EqBand> { high, low, middle },
                SelectedBand = high
            };
            var getDisplayNumber = typeof(EqResponseGraphControl).GetMethod(
                "GetDisplayNumber",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.Equal(1, (int)getDisplayNumber.Invoke(graph, [low])!);
            Assert.Equal(2, (int)getDisplayNumber.Invoke(graph, [middle])!);
            Assert.Equal(3, (int)getDisplayNumber.Invoke(graph, [high])!);
            Assert.Equal(3, graph.SelectedBandDisplayNumber);

            middle.FrequencyHz = 2000;

            Assert.Equal(2, graph.SelectedBandDisplayNumber);
            Assert.Equal(1, high.Number);
            Assert.Equal(5, low.Number);
            Assert.Equal(2, middle.Number);
        });

    private static EqBand CreateBand(int number, int frequencyHz)
        => new()
        {
            Number = number,
            Enabled = true,
            FilterType = EqFilterType.Peak,
            FrequencyHz = frequencyHz,
            GainDb = 0,
            Q = 1
        };

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
