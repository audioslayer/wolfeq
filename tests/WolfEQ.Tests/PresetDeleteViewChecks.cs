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
        model.LibraryTab = MainViewModel.LibraryTabMine;
        StudioViewChecks.Layout(library, 860, 820);
        var list = (ListBox)library.FindName("SavedPresetList");
        model.LibrarySelection = model.Presets[0];
        StudioViewChecks.Layout(library, 860, 820);
        StudioViewChecks.Render(library, 860, 820, "studio-library-delete");
        Assert.Null(library.FindName("DeleteSelectedPresetButton"));
        var shownRowButtons = StudioViewChecks.Descendants(list).OfType<Button>()
            .Where(button => button.Name == "RowDeleteButton" && button.Visibility == Visibility.Visible)
            .ToArray();
        Assert.Single(shownRowButtons);
        Assert.Same(model.Presets[0], shownRowButtons[0].DataContext);

        VerifyImportExport(model);
    }

    /// <summary>Import / Export menus in the library header still reach every import/export command.</summary>
    private static void VerifyImportExport(MainViewModel model)
    {
        var library = new LibraryPanel { DataContext = model };
        StudioViewChecks.Layout(library, 860, 820);
        var importRows = (FrameworkElement)library.FindName("ImportRows");
        var exportRows = (FrameworkElement)library.FindName("ExportRows");
        foreach (var popupName in new[] { "ImportPopup", "ExportPopup" })
        {
            var child = (FrameworkElement)((System.Windows.Controls.Primitives.Popup)library.FindName(popupName)).Child;
            child.DataContext = model;
            StudioViewChecks.Layout(child, 440, 420);
        }

        StudioViewChecks.Render(exportRows, 420, 420, "studio-library-export-menu");
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
