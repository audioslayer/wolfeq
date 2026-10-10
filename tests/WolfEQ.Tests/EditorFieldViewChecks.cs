using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WolfEQ.Controls;
using WolfEQ.Models;
using WolfEQ.Views;
using Xunit;

namespace WolfEQ.Tests;

/// <summary>Style C band fields: − value + with a unit tab, and the buttons step the selected band.</summary>
internal static class EditorFieldViewChecks
{
    public static void Verify(EditorWorkspace editor, EqResponseGraphControl graph, EqBand band)
    {
        graph.SelectedBand = band;
        band.FrequencyHz = 1000;
        band.GainDb = 0;
        band.Q = 1;
        StudioViewChecks.Layout(editor, 1640, 900);

        Button Find(string name) => StudioViewChecks.Descendants(editor).OfType<Button>()
            .Single(b => AutomationProperties.GetName(b) == name);
        void Click(string name) => Find(name).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        Click("Increase frequency");
        Assert.Equal(1059, band.FrequencyHz); // 1/12 octave up from 1 kHz
        Click("Decrease frequency");
        Assert.Equal(1000, band.FrequencyHz);
        Click("Increase gain");
        Click("Increase gain");
        Assert.Equal(0.2, band.GainDb, 3);
        Click("Decrease Q");
        Assert.Equal(0.99, band.Q, 3);

        var gainBox = (TextBox)editor.FindName("GainBox");
        Assert.Equal("+0.2", gainBox.Text);
        Assert.Contains(StudioViewChecks.Descendants(editor).OfType<TextBlock>(), t => t.Text == "Hz");
        Assert.Contains(StudioViewChecks.Descendants(editor).OfType<TextBlock>(), t => t.Text == "dB");

        // Render the row through a brush: rendering a nested element directly keeps its window offset.
        var capsule = (FrameworkElement)editor.FindName("BandEditorCapsule");
        var snapshot = new System.Windows.Shapes.Rectangle
        {
            Width = capsule.ActualWidth,
            Height = capsule.ActualHeight,
            Fill = new System.Windows.Media.VisualBrush(capsule) { Stretch = System.Windows.Media.Stretch.None }
        };
        StudioViewChecks.Layout(snapshot, (int)Math.Ceiling(capsule.ActualWidth), (int)Math.Ceiling(capsule.ActualHeight));
        StudioViewChecks.Render(snapshot, (int)Math.Ceiling(capsule.ActualWidth), (int)Math.Ceiling(capsule.ActualHeight), "studio-band-fields");

        var combo = StudioViewChecks.Descendants(capsule).OfType<ComboBox>().First();
        combo.IsDropDownOpen = true;
        combo.UpdateLayout();
        if (combo.Template.FindName("Popup", combo) is Popup { Child: FrameworkElement list })
        {
            StudioViewChecks.Layout(list, 260, 320);
            StudioViewChecks.Render(list, 260, 320, "studio-filter-dropdown");
            Assert.Contains(StudioViewChecks.Descendants(list).OfType<TextBlock>(), t => t.Text == "Low shelf");
        }

        combo.IsDropDownOpen = false;
    }
}
