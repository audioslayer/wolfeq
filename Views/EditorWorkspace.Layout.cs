using System.Windows;
using System.Windows.Controls.Primitives;

namespace WolfEQ.Views;

public partial class EditorWorkspace
{
    private double _expandedBandHeight = 380;

    private void BandExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (BandPanel.IsExpanded)
        {
            BandDivider.Visibility = Visibility.Visible;
            BandPanel.Margin = new Thickness(0, 0, 0, 12);
            BandsRow.MinHeight = 180;
            BandsRow.Height = new GridLength(_expandedBandHeight);
            ConstrainBandHeight();
        }
        else
        {
            if (BandsRow.Height.IsAbsolute) _expandedBandHeight = BandsRow.Height.Value;
            BandDivider.Visibility = Visibility.Collapsed;
            BandPanel.Margin = new Thickness(0, 12, 0, 12);
            BandsRow.MinHeight = 0;
            BandsRow.MaxHeight = double.PositiveInfinity;
            BandsRow.Height = GridLength.Auto;
        }
        GraphRow.Height = new GridLength(1, GridUnitType.Star);
    }

    private void ConstrainBandHeight()
    {
        if (!BandPanel.IsExpanded || WorkspaceGrid.ActualHeight <= 0) return;
        var maximum = Math.Max(180, WorkspaceGrid.ActualHeight - GraphRow.MinHeight - 12 - Math.Max(66, PreampPanel.ActualHeight));
        BandsRow.MaxHeight = maximum;
        if (BandsRow.Height.IsAbsolute && BandsRow.Height.Value > maximum)
            BandsRow.Height = new GridLength(maximum);
    }

    private void BandDivider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (BandPanel.IsExpanded) _expandedBandHeight = BandsRow.ActualHeight;
    }
}
