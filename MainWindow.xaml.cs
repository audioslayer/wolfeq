using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WolfEQ.ViewModels;
using WolfEQ.Services;

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
            ConfirmDialog = (title, message) => Views.WolfDialog.Confirm(this, new ConfirmationRequest(title, message)),
            ConfirmationDialog = request => Views.WolfDialog.Confirm(this, request),
            TextPromptDialog = request => Views.WolfDialog.Prompt(this, request)
        };
        DataContext = viewModel;

        viewModel.OnlineProfileOpenedInEditor += (_, _) =>
        {
            if (ReferenceEquals(_openOverlayPanel, LibraryHost)) CloseOverlayPanel();
        };
        LibraryHost.CloseRequested += (_, _) => CloseOverlayPanel();
        SettingsHost.CloseRequested += (_, _) => CloseOverlayPanel();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += (_, e) =>
        {
            if (Application.Current is not App app || app.IsExiting) return;
            e.Cancel = true;
            if (TrayPreferences.Current.CloseToTray && app.CanHideToTray) app.HideEditor();
            else Dispatcher.BeginInvoke(new Action(app.RequestQuit));
        };
        StateChanged += (_, _) =>
        {
            if (Application.Current is not App app || app.IsExiting) return;
            if (WindowState == WindowState.Minimized && TrayPreferences.Current.MinimizeToTray && app.CanHideToTray)
                app.HideEditor();
            else app.RememberWindowState();
        };
    }

    public void OpenSettings()
    {
        if (!ReferenceEquals(_openOverlayPanel, SettingsHost)) ToggleOverlayPanel(SettingsHost);
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
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.L:
                ToggleOverlayPanel(LibraryHost);
                e.Handled = true;
                break;
            case Key.OemComma:
                ToggleOverlayPanel(SettingsHost);
                e.Handled = true;
                break;
            case Key.S when DataContext is MainViewModel viewModel
                            && viewModel.SaveToLibraryCommand.CanExecute(null):
                DeviceToolbar.OpenLibrarySave();
                e.Handled = true;
                break;
            case Key.Z when DataContext is MainViewModel undoViewModel
                            && undoViewModel.UndoCommand.CanExecute(null):
                undoViewModel.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Y when DataContext is MainViewModel redoViewModel
                            && redoViewModel.RedoCommand.CanExecute(null):
                redoViewModel.RedoCommand.Execute(null);
                e.Handled = true;
                break;
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

        var overlayWasCollapsed = OverlayLayer.Visibility != Visibility.Visible;
        _openOverlayPanel = panel;
        LibraryHeaderButton.IsChecked = ReferenceEquals(panel, LibraryHost);
        SettingsHeaderButton.IsChecked = ReferenceEquals(panel, SettingsHost);
        OverlayLayer.Visibility = Visibility.Visible;
        if (overlayWasCollapsed)
        {
            OverlayDimmer.BeginAnimation(OpacityProperty, null);
            OverlayDimmer.Opacity = 0;
        }

        AnimateOverlayDimmer(1);
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
        LibraryHeaderButton.IsChecked = false;
        SettingsHeaderButton.IsChecked = false;
        AnimateOverlayDimmer(0);
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
            OverlayDimmer.BeginAnimation(OpacityProperty, null);
            OverlayDimmer.Opacity = 0;
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

    private void AnimateOverlayDimmer(double toOpacity)
    {
        var animation = new DoubleAnimation(toOpacity, PanelSlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        OverlayDimmer.BeginAnimation(OpacityProperty, animation);
    }

    private static double GetOffscreenX(FrameworkElement panel)
    {
        var width = panel.ActualWidth > 0 ? panel.ActualWidth : panel.Width;
        return (double.IsNaN(width) ? 480 : width) + panel.Margin.Right + 8;
    }

    // --- Window chrome ---

    private void UndoHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.UndoCommand.CanExecute(null))
        {
            viewModel.UndoCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void RedoHeaderButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && viewModel.RedoCommand.CanExecute(null))
        {
            viewModel.RedoCommand.Execute(null);
        }

        e.Handled = true;
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


}

public sealed class ReferenceEqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 2 && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
