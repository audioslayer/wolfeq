using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// Settings slide-over content: theme accent, device profile selection, input source,
/// device controls (volume / limit / balance / gain / DRE / slot name), lights,
/// and the About / update section. Expects the application's MainViewModel as
/// (inherited) DataContext.
/// <para>
/// The panel is a fixed-width, right-docked surface; the overlay hosting, slide
/// animation, and Esc/outside-click dismissal are the shell's job (U7). The shell
/// subscribes to <see cref="CloseRequested"/> to dismiss the panel.
/// </para>
/// </summary>
public partial class SettingsPopover : UserControl
{
    public SettingsPopover()
    {
        InitializeComponent();
    }

    /// <summary>
    /// One-line summary shown under a settings section title while it is collapsed.
    /// Kept separate from <c>Header</c> (which stays the plain section name) and from
    /// <c>Tag</c> (which carries the section icon).
    /// </summary>
    public static readonly DependencyProperty SectionSummaryProperty = DependencyProperty.RegisterAttached(
        "SectionSummary", typeof(string), typeof(SettingsPopover), new PropertyMetadata(string.Empty));

    public static string GetSectionSummary(DependencyObject element) => (string)element.GetValue(SectionSummaryProperty);

    public static void SetSectionSummary(DependencyObject element, string value) => element.SetValue(SectionSummaryProperty, value);

    /// <summary>
    /// Fixed colour for a section's icon tile, so each section is recognisable at a glance
    /// (Lights is always amber/rainbow, Sound always blue) whatever accent theme is chosen.
    /// </summary>
    public static readonly DependencyProperty SectionColorProperty = DependencyProperty.RegisterAttached(
        "SectionColor", typeof(System.Windows.Media.Brush), typeof(SettingsPopover), new PropertyMetadata(null));

    public static System.Windows.Media.Brush? GetSectionColor(DependencyObject element) => (System.Windows.Media.Brush?)element.GetValue(SectionColorProperty);

    public static void SetSectionColor(DependencyObject element, System.Windows.Media.Brush? value) => element.SetValue(SectionColorProperty, value);

    /// <summary>Raised when the user clicks the panel's ✕ button.</summary>
    public event EventHandler? CloseRequested;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void CloseStreamDeckSetup_Click(object sender, RoutedEventArgs e) => StreamDeckSetupPopup.IsOpen = false;

    private void StreamDeckSetup_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        StreamDeckSetupPopup.IsOpen = false;
        StreamDeckSetupButton.Focus();
        e.Handled = true;
    }

    private void WindowsAudioExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            viewModel.RefreshWindowsAudioFormatsCommand.CanExecute(null))
        {
            viewModel.RefreshWindowsAudioFormatsCommand.Execute(null);
        }
    }

    private void AccentColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: AccentColorOption option })
        {
            viewModel.SelectedAccentColorOption = option;
        }

        e.Handled = true;
    }

    private async void LightEffect_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: LightEffectOption effect })
            await viewModel.SetLightEffectAsync(effect);
    }

    private async void LightColor_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: LedColorOption color })
            await viewModel.SetLightColorAsync(color);
    }

    private async void LightLook_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: LedSceneOption look })
            await viewModel.ApplyLightLookAsync(look);
    }

    private void StreamDeckKey_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { DataContext: StreamDeckKeyOption key })
            viewModel.SelectStreamDeckKey(key.Slot);
    }

    private void OpenStreamDeckWizard_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel viewModel) return;
        var wizard = new StreamDeckSetupWizard(viewModel) { Owner = Window.GetWindow(this) };
        wizard.ShowDialog();
    }

    /// <summary>From the Lights banner or the hero's Bluetooth chip: open Your device and scroll to the Bluetooth card.</summary>
    private void SetUpBluetooth_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        DeviceSection.IsExpanded = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => BluetoothCard.BringIntoView());
    }
}

/// <summary>
/// True when the first text only repeats the second (optionally after the ConverterParameter
/// prefix, e.g. "From " + "USER 1"). Used to hide subtitles that add nothing.
/// </summary>
public sealed class RepeatsTextMultiConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string first || values[1] is not string second) return false;
        if (string.IsNullOrWhiteSpace(first)) return true;
        return string.Equals(first.Trim(), ((parameter as string) + second).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
