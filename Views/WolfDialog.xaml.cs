using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// WolfEQ-styled confirmation modal: dims the owner window and shows a card with Cancel and a
/// confirm action. Destructive requests get a red action, and focus starts on Cancel so Enter
/// never deletes by accident. Esc or a click outside the card cancels.
/// </summary>
public partial class WolfDialog : Window
{
    private const string TrashGeometry = "M3,5 H17 M7,5 V2 H13 V5 M5,5 V18 H15 V5 M8,8 V15 M12,8 V15";
    private const string WarningGeometry = "M10,2 L19,18 H1 Z M10,8 V12 M10,15 V15.5";

    private WolfDialog(ConfirmationRequest request)
    {
        InitializeComponent();
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        if (!string.IsNullOrWhiteSpace(request.Detail))
        {
            DetailText.Text = request.Detail;
            DetailText.Visibility = Visibility.Visible;
        }

        ConfirmButton.Content = request.ConfirmText;
        CancelButton.Content = request.CancelText;
        var accent = (Brush)FindResource(request.IsDestructive ? "WolfDangerBrush" : "WolfGreenBrush");
        ConfirmButton.Background = accent;
        ConfirmButton.Foreground = request.IsDestructive ? Brushes.White : (Brush)FindResource("WolfGreenContrastBrush");
        IconPath.Data = Geometry.Parse(request.IsDestructive ? TrashGeometry : WarningGeometry);
        IconPath.Stroke = accent;
        IconBadge.Background = new SolidColorBrush(((SolidColorBrush)accent).Color) { Opacity = 0.15 };
        AutomationPropertiesHelper(request);
        Loaded += (_, _) => (request.IsDestructive ? CancelButton : ConfirmButton).Focus();
    }

    /// <summary>Shows the modal over <paramref name="owner"/> and returns true when the user confirms.</summary>
    public static bool Confirm(Window? owner, ConfirmationRequest request)
    {
        var dialog = Create(request);
        owner ??= Application.Current?.MainWindow;
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
            dialog.CoverOwner(owner);
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.SizeToContent = SizeToContent.WidthAndHeight;
            dialog.Scrim.Background = Brushes.Transparent;
        }

        return dialog.ShowDialog() == true;
    }

    /// <summary>Builds the dialog without showing it (used by <see cref="Confirm"/> and view checks).</summary>
    internal static WolfDialog Create(ConfirmationRequest request) => new(request);

    private void CoverOwner(Window owner)
    {
        if (owner.WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = owner.Left;
        Top = owner.Top;
        Width = owner.ActualWidth;
        Height = owner.ActualHeight;
    }

    private void AutomationPropertiesHelper(ConfirmationRequest request)
        => System.Windows.Automation.AutomationProperties.SetName(this, request.Title);

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DialogResult = false;

    // Clicks inside the card must not reach the scrim's cancel handler.
    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;
}
