using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// Four-step Stream Deck key setup (the AmpUp wizard pattern): pick a USER slot, save its
/// shortcut, add it to Stream Deck with System › Open, then name and try the key.
/// One step shows at a time with Back / dots / Next, and Finish on the last step.
/// </summary>
public partial class StreamDeckSetupWizard : Window
{
    private readonly MainViewModel _viewModel;
    private readonly FrameworkElement[] _steps;
    private readonly Border[] _dots;
    private int _step;

    public StreamDeckSetupWizard(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _steps = [Step1, Step2, Step3, Step4];
        _dots = _steps.Select((_, i) => new Border
        {
            Height = 7,
            CornerRadius = new CornerRadius(3.5),
            Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        }).ToArray();
        foreach (var dot in _dots) Dots.Children.Add(dot);
        ShowStep(0);
        // Enter goes to Next (IsDefault); start with focus there so the keyboard path is obvious.
        Loaded += (_, _) => NextButton.Focus();
    }

    /// <summary>The step on screen (0-based); exposed for tests and dev mode.</summary>
    public int CurrentStep => _step;

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, _steps.Length - 1);
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
            // Current step is a green pill; done steps a dim green dot; upcoming steps a grey dot.
            _dots[i].Width = i == _step ? 20 : 7;
            _dots[i].Opacity = i < _step ? 0.45 : 1;
            _dots[i].SetResourceReference(Border.BackgroundProperty, i <= _step ? "WolfGreenBrush" : "WolfLineSoftBrush");
        }

        AutomationProperties.SetName(Dots, $"Step {_step + 1} of {_steps.Length}");
        var backHadFocus = BackButton.IsKeyboardFocusWithin;
        BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Content = _step == _steps.Length - 1 ? "Finish" : "Next";
        // Focus would be lost when Back hides on step 1 or the focused control's step collapses.
        if (IsLoaded && ((backHadFocus && _step == 0) || !IsKeyboardFocusWithin || Keyboard.FocusedElement is UIElement { IsVisible: false }))
            NextButton.Focus();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == _steps.Length - 1)
        {
            DialogResult = true;
            return;
        }

        ShowStep(_step + 1);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void Another_Click(object sender, RoutedEventArgs e) => ShowStep(0);

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StreamDeckKeyOption key }) _viewModel.SelectStreamDeckKey(key.Slot);
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_viewModel.LastSavedShortcutPath); }
        catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy; the path stays selectable */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}

/// <summary>
/// Hides a key's subtitle when it adds nothing: empty, or just the default "From USER n" name
/// that repeats the key's own label.
/// </summary>
public sealed class StreamDeckSubtitleVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not StreamDeckKeyOption key || string.IsNullOrWhiteSpace(key.Subtitle)) return Visibility.Collapsed;
        var redundant = string.Equals(key.Subtitle.Trim(), $"From {key.Label}", StringComparison.OrdinalIgnoreCase);
        return redundant ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
