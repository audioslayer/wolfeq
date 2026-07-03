using System.Windows;
using System.Windows.Controls;

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
}
