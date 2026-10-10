using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WolfEQ.ViewModels;
using WolfEQ.Views;
using Xunit;

namespace WolfEQ.Tests;

/// <summary>Renders the themed delete modal and the library's delete affordances.</summary>
internal static class PresetDeleteViewChecks
{
    public static void Verify(MainViewModel model)
    {
        var dialog = WolfDialog.Create(new ConfirmationRequest(
            "Delete preset?",
            "\"AutoEq - HIFIMAN Ananda\" will be removed from your WolfEQ library. This can't be undone.",
            "Delete",
            IsDestructive: true));
        var surface = (FrameworkElement)dialog.Content;
        dialog.Content = null;
        surface.Resources = dialog.Resources;
        System.Windows.Documents.TextElement.SetFontFamily(surface, new FontFamily("Segoe UI"));
        System.Windows.Documents.TextElement.SetForeground(surface, dialog.Foreground);
        StudioViewChecks.Layout(surface, 560, 360);
        StudioViewChecks.Render(surface, 560, 360, "studio-delete-dialog");
        var buttons = StudioViewChecks.Descendants(surface).OfType<Button>().ToArray();
        Assert.Contains(buttons, button => Equals(button.Content, "Delete"));
        Assert.Contains(buttons, button => Equals(button.Content, "Cancel") && button.IsCancel);
        dialog.Close();

        var library = new LibraryPanel { DataContext = model };
        StudioViewChecks.Layout(library, 460, 1000);
        var list = (ListBox)library.FindName("SavedPresetList");
        model.LibrarySelection = model.Presets[0];
        StudioViewChecks.Layout(library, 460, 1000);
        StudioViewChecks.Render(library, 460, 1000, "studio-library-delete");
        Assert.Null(library.FindName("DeleteSelectedPresetButton"));
        var shownRowButtons = StudioViewChecks.Descendants(list).OfType<Button>()
            .Where(button => button.Name == "RowDeleteButton" && button.Visibility == Visibility.Visible)
            .ToArray();
        Assert.Single(shownRowButtons);
        Assert.Same(model.Presets[0], shownRowButtons[0].DataContext);

        VerifyImportExport(model);
    }

    /// <summary>Import / Export: tabbed rows, and every import/export command is still reachable.</summary>
    private static void VerifyImportExport(MainViewModel model)
    {
        var library = new LibraryPanel { DataContext = model };
        StudioViewChecks.Layout(library, 460, 1000);
        StudioViewChecks.Descendants(library).OfType<Expander>().Single(e => Equals(e.Header, "Import / export")).IsExpanded = true;
        StudioViewChecks.Layout(library, 460, 1000);
        var importRows = (FrameworkElement)library.FindName("ImportRows");
        var exportRows = (FrameworkElement)library.FindName("ExportRows");
        Assert.Equal(Visibility.Visible, importRows.Visibility);
        Assert.Equal(Visibility.Collapsed, exportRows.Visibility);
        StudioViewChecks.Render(library, 460, 1000, "studio-library-import");

        ((RadioButton)library.FindName("ExportModeChip")).IsChecked = true;
        StudioViewChecks.Layout(library, 460, 1000);
        Assert.Equal(Visibility.Collapsed, importRows.Visibility);
        Assert.Equal(Visibility.Visible, exportRows.Visibility);
        StudioViewChecks.Render(library, 460, 1000, "studio-library-export");

        var commands = StudioViewChecks.Descendants(importRows).Concat(StudioViewChecks.Descendants(exportRows))
            .OfType<Button>().Select(button => button.Command).Where(command => command is not null).ToHashSet();
        foreach (var expected in new[]
                 {
                     model.ImportApoFromFileCommand, model.ImportApoFromClipboardCommand, model.ImportFiioXmlFromFileCommand,
                     model.ImportJsonFromFileCommand, model.ImportLibraryJsonCommand, model.ExportApoToFileCommand,
                     model.ExportApoToClipboardCommand, model.ExportFiioXmlToFileCommand, model.ExportJsonToFileCommand,
                     model.ExportLibraryJsonCommand, model.CopyTuningReportCommand
                 })
        {
            Assert.Contains(expected, commands);
        }
    }
}
