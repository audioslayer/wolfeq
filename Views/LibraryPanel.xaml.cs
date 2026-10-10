using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// Library slide-over content: the My Library and Online profile sources with shared
/// search, per-selection Load / Write / Download actions, and the single import-export
/// cluster. Expects the application's MainViewModel as (inherited) DataContext.
/// <para>
/// The panel is a fixed-width, right-docked surface; the overlay hosting, slide
/// animation, and Esc/outside-click dismissal are the shell's job (U7). The shell
/// subscribes to <see cref="CloseRequested"/> to dismiss the panel.
/// </para>
/// </summary>
public partial class LibraryPanel : UserControl
{
    public LibraryPanel()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks the panel's ✕ button.</summary>
    public event EventHandler? CloseRequested;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OpenPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model && model.TryOpenLibrarySelection())
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // Import / Export menu rows open file dialogs; close the menu first so it isn't left floating.
    private void IoAction_Click(object sender, RoutedEventArgs e)
    {
        ImportPopup.IsOpen = false;
        ExportPopup.IsOpen = false;
    }

    // Double-clicking a saved preset opens it, like the Open in editor button.
    private void SavedPresetList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SavedPresetList, source) is ListBoxItem)
            OpenPresetButton_Click(sender, e);
    }

    /// <summary>Online row download button: select that result, then save it to My Library.</summary>
    private void RowSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model || sender is not FrameworkElement { DataContext: Models.AutoEqProfileIndexEntry entry })
        {
            return;
        }

        model.SelectedGitHubProfile = entry;
        if (model.ImportGitHubProfileCommand.CanExecute(null))
        {
            model.ImportGitHubProfileCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>Row trash button: select that row, then run the normal confirmed delete.</summary>
    private void RowDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model || sender is not FrameworkElement { DataContext: Models.EqPreset preset })
        {
            return;
        }

        model.LibrarySelection = preset;
        if (model.DeletePresetCommand.CanExecute(null))
        {
            model.DeletePresetCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void SavedPresetList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || DataContext is not MainViewModel model || !model.DeletePresetCommand.CanExecute(null))
        {
            return;
        }

        model.DeletePresetCommand.Execute(null);
        e.Handled = true;
    }

    private void PreviewView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string index } && int.TryParse(index, out var selectedIndex))
            OnlinePreviewTabs.SelectedIndex = selectedIndex;
    }

    private void OnlineSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.SearchGitHubProfilesCommand.CanExecute(null))
        {
            viewModel.SearchGitHubProfilesCommand.Execute(null);
            e.Handled = true;
        }
    }
}
