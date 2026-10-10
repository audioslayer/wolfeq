using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// WolfEQ-styled modal: dims the owner window and shows a card with Cancel and a confirm
/// action. Destructive requests get a red action, and focus starts on Cancel so Enter never
/// deletes by accident. Prompt requests add a text box with live validation (rename).
/// Esc or a click outside the card cancels.
/// </summary>
public partial class WolfDialog : Window
{
    private const string TrashGeometry = "M3,5 H17 M7,5 V2 H13 V5 M5,5 V18 H15 V5 M8,8 V15 M12,8 V15";
    private const string WarningGeometry = "M10,2 L19,18 H1 Z M10,8 V12 M10,15 V15.5";
    private const string PencilGeometry = "M3,17 L3,13.5 L13,3.5 L16.5,7 L6.5,17 Z M11,5.5 L14.5,9";

    private readonly Func<string, string?>? _validate;

    private WolfDialog(ConfirmationRequest request, TextPromptRequest? prompt = null)
    {
        InitializeComponent();
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;
        MessageText.Visibility = string.IsNullOrWhiteSpace(request.Message) ? Visibility.Collapsed : Visibility.Visible;
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
        IconPath.Data = Geometry.Parse(prompt is not null ? PencilGeometry : request.IsDestructive ? TrashGeometry : WarningGeometry);
        IconPath.Stroke = accent;
        IconBadge.Background = new SolidColorBrush(((SolidColorBrush)accent).Color) { Opacity = 0.15 };
        System.Windows.Automation.AutomationProperties.SetName(this, request.Title);

        if (prompt is not null)
        {
            _validate = prompt.Validate;
            PromptPanel.Visibility = Visibility.Visible;
            PromptBox.Text = prompt.InitialText;
            PromptBox.KeyDown += PromptBox_KeyDown;
            Loaded += (_, _) =>
            {
                PromptBox.Focus();
                PromptBox.SelectAll();
            };
            UpdatePromptValidation();
        }
        else
        {
            Loaded += (_, _) => (request.IsDestructive ? CancelButton : ConfirmButton).Focus();
        }
    }

    /// <summary>Shows the modal over <paramref name="owner"/> and returns true when the user confirms.</summary>
    public static bool Confirm(Window? owner, ConfirmationRequest request)
        => Show(Create(request), owner);

    /// <summary>Asks for text (e.g. a new preset name); returns the trimmed text, or null when cancelled.</summary>
    public static string? Prompt(Window? owner, TextPromptRequest request)
    {
        var dialog = CreatePrompt(request);
        return Show(dialog, owner) ? dialog.PromptBox.Text.Trim() : null;
    }

    /// <summary>Builds the dialog without showing it (used by <see cref="Confirm"/> and view checks).</summary>
    internal static WolfDialog Create(ConfirmationRequest request) => new(request);

    internal static WolfDialog CreatePrompt(TextPromptRequest request)
        => new(new ConfirmationRequest(request.Title, request.Message, request.ConfirmText), request);

    /// <summary>For view checks: the prompt text box and its current validation message.</summary>
    internal (string Text, string? Error, bool CanConfirm) PromptState
        => (PromptBox.Text, PromptError.Visibility == Visibility.Visible ? PromptError.Text : null, ConfirmButton.IsEnabled);

    internal void SetPromptText(string text) => PromptBox.Text = text;

    private static bool Show(WolfDialog dialog, Window? owner)
    {
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

    private void PromptBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePromptValidation();

    private void UpdatePromptValidation()
    {
        if (_validate is null)
        {
            return;
        }

        var error = _validate(PromptBox.Text.Trim());
        PromptError.Text = error ?? string.Empty;
        PromptError.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.IsEnabled = string.IsNullOrEmpty(error);
        ConfirmButton.Opacity = ConfirmButton.IsEnabled ? 1 : 0.45;
    }

    private void PromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ConfirmButton.IsEnabled)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

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
