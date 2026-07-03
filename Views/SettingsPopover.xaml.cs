using System.Windows;
using System.Windows.Controls;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// Settings slide-over content: theme accent, device profile selection, input source,
/// device controls (volume / limit / balance / gain / DRE / slot name), profile lights,
/// and the About / update section. Expects the application's MainViewModel as
/// (inherited) DataContext.
/// <para>
/// The panel is a fixed-width, right-docked surface; the overlay hosting, slide
/// animation, and Esc/outside-click dismissal are the shell's job (U7). The shell
/// subscribes to <see cref="CloseRequested"/> to dismiss the panel.
/// </para>
/// <para>
/// The click handlers below are copies of the MainWindow Settings-tab handlers
/// (U6); the originals stay in MainWindow.xaml.cs until the tab is removed (U7/U8).
/// </para>
/// </summary>
public partial class SettingsPopover : UserControl
{
    public SettingsPopover()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user clicks the panel's ✕ button.</summary>
    public event EventHandler? CloseRequested;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void AccentColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: AccentColorOption option })
        {
            viewModel.SelectedAccentColorOption = option;
        }

        e.Handled = true;
    }

    private async void TopLedColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: LedColorOption option })
        {
            await viewModel.SetTopLedColorAsync(option);
        }

        e.Handled = true;
    }

    private async void KnobLedColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: LedColorOption option })
        {
            await viewModel.SetKnobLedColorAsync(option);
        }

        e.Handled = true;
    }

    private async void TopLedMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: LedModeOption option })
        {
            await viewModel.SetTopLedModeAsync(option);
        }

        e.Handled = true;
    }

    private async void KnobLedMode_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: LedModeOption option })
        {
            await viewModel.SetKnobLedModeAsync(option);
        }

        e.Handled = true;
    }

    private async void TopLedPower_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.SetTopLedPowerAsync(!viewModel.TopLedOn);
        }

        e.Handled = true;
    }

    private async void KnobLedPower_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.SetKnobLedPowerAsync(!viewModel.KnobLedOn);
        }

        e.Handled = true;
    }

    private async void LedScene_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: LedSceneOption scene })
        {
            await viewModel.ApplyLedSceneAsync(scene);
        }

        e.Handled = true;
    }
}
