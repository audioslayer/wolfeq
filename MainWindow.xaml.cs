using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WolfEQ.ViewModels;

namespace WolfEQ;

public partial class MainWindow : Window
{
    private static readonly Duration PanelSlideDuration = new(TimeSpan.FromMilliseconds(180));

    /// <summary>The overlay panel currently open (LibraryHost or SettingsHost), if any.</summary>
    private FrameworkElement? _openOverlayPanel;

    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel
        {
            ConfirmDialog = (title, message) =>
                MessageBox.Show(this, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning)
                == MessageBoxResult.OK
        };
        DataContext = viewModel;

        LibraryHost.CloseRequested += (_, _) => CloseOverlayPanel();
        SettingsHost.CloseRequested += (_, _) => CloseOverlayPanel();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.AutoConnectAndReadDeviceAsync();
        }
    }

    // --- Overlay panels (Library / Settings right slide-overs) ---

    private void LibraryHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleOverlayPanel(LibraryHost);
        e.Handled = true;
    }

    private void SettingsHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleOverlayPanel(SettingsHost);
        e.Handled = true;
    }

    private void OverlayDimmer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseOverlayPanel();
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Only intercept Esc while a slide-over is open; the editor's selected-band
        // strip uses Esc to revert a typed value, and that must keep working.
        if (e.Key == Key.Escape && _openOverlayPanel is not null)
        {
            CloseOverlayPanel();
            e.Handled = true;
        }
    }

    /// <summary>Opens the panel, or closes it when it is already the open one. Only one panel is open at a time.</summary>
    private void ToggleOverlayPanel(FrameworkElement panel)
    {
        if (ReferenceEquals(_openOverlayPanel, panel))
        {
            CloseOverlayPanel();
            return;
        }

        if (_openOverlayPanel is not null)
        {
            HideOverlayPanel(_openOverlayPanel, animated: false);
        }

        _openOverlayPanel = panel;
        OverlayLayer.Visibility = Visibility.Visible;
        panel.Visibility = Visibility.Visible;
        AnimatePanel(panel, toX: 0, onCompleted: null);
    }

    private void CloseOverlayPanel()
    {
        if (_openOverlayPanel is null)
        {
            return;
        }

        var panel = _openOverlayPanel;
        _openOverlayPanel = null;
        HideOverlayPanel(panel, animated: true);
    }

    private void HideOverlayPanel(FrameworkElement panel, bool animated)
    {
        var offscreenX = GetOffscreenX(panel);

        if (!animated)
        {
            if (panel.RenderTransform is TranslateTransform transform)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = offscreenX;
            }

            panel.Visibility = Visibility.Collapsed;
            CollapseOverlayIfIdle();
            return;
        }

        AnimatePanel(panel, offscreenX, onCompleted: () =>
        {
            // Skip the collapse when the same panel was reopened mid-animation.
            if (!ReferenceEquals(_openOverlayPanel, panel))
            {
                panel.Visibility = Visibility.Collapsed;
                CollapseOverlayIfIdle();
            }
        });
    }

    private void CollapseOverlayIfIdle()
    {
        if (_openOverlayPanel is null)
        {
            OverlayLayer.Visibility = Visibility.Collapsed;
        }
    }

    private static void AnimatePanel(FrameworkElement panel, double toX, Action? onCompleted)
    {
        if (panel.RenderTransform is not TranslateTransform transform)
        {
            onCompleted?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(toX, PanelSlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        if (onCompleted is not null)
        {
            animation.Completed += (_, _) => onCompleted();
        }

        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private static double GetOffscreenX(FrameworkElement panel)
    {
        var width = panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width;
        return (double.IsNaN(width) ? 480 : width) + panel.Margin.Right + 8;
    }

    // --- Window chrome ---

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindVisualAncestor<ButtonBase>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the pointer state changes during the click.
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreWindow_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

public sealed class ReferenceEqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 2 && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
