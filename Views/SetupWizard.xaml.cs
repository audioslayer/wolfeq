using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WolfEQ.Models;
using WolfEQ.ViewModels;

namespace WolfEQ.Views;

/// <summary>
/// Four-step setup: find the device, what to know about it, a headphone preset, ready. Shown once to new users
/// and from Settings. It never writes to a DAC.
/// </summary>
public partial class SetupWizard : Window
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(4);

    private readonly MainViewModel _viewModel;
    private readonly FrameworkElement[] _steps;
    private readonly Border[] _dots;
    private readonly DispatcherTimer _scanTimer = new() { Interval = ScanInterval };
    private int _step;
    private readonly HashSet<string> _triedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;

    public SetupWizard(MainViewModel viewModel)
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
            VerticalAlignment = VerticalAlignment.Center
        }).ToArray();
        foreach (var dot in _dots) Dots.Children.Add(dot);
        _scanTimer.Tick += async (_, _) => await ScanAsync();
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        if (viewModel.GitHubProfiles.Count == 0) viewModel.GitHubProfileSearchText = string.Empty;
        UpdatePresetButton();
        ShowStep(0);
        Loaded += async (_, _) =>
        {
            NextButton.Focus();
            StartPulse();
            _scanTimer.Start();
            await ScanAsync();
        };
        Closed += (_, _) =>
        {
            _scanTimer.Stop();
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.CompleteSetup();
        };
    }

    /// <summary>The step on screen (0-based).</summary>
    public int CurrentStep => _step;

    private bool IsPickingModel => ModelPicker.Visibility == Visibility.Visible;

    // Looks again only while nothing is plugged in; finding a device stops the polling.
    private async Task ScanAsync()
    {
        if (_busy || _step != 0 || IsPickingModel || (_viewModel.HasSetupDevices && _scanTimer.IsEnabled && _viewModel.IsDeviceConnected)) return;
        _busy = true;
        try
        {
            await _viewModel.RefreshSetupDevicesAsync();
            // The only device, and either nothing is chosen yet or it is the chosen model: connect it once.
            if (_viewModel.SetupDevices.Count == 1 && !_viewModel.IsDeviceConnected && !_viewModel.IsSystemEqSelected
                && _viewModel.SetupDevices[0] is { UsbPath: { } path } only
                && (!_viewModel.HasChosenDevice || ReferenceEquals(only.Profile, _viewModel.SelectedDeviceProfile))
                && _triedPaths.Add(path))
            {
                await _viewModel.ChooseSetupDeviceAsync(only);
            }
        }
        finally
        {
            _busy = false;
            UpdateDeviceText();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.GitHubPreviewPreset)) UpdatePresetButton();
        if (e.PropertyName is nameof(MainViewModel.IsDeviceConnected) or nameof(MainViewModel.SelectedDeviceProfile) or nameof(MainViewModel.SystemEqStatusText))
        {
            _viewModel.RaiseSetupChanged();
            UpdateDeviceText();
        }
    }

    private void UpdatePresetButton() => UsePresetButton.IsEnabled = _viewModel.GitHubPreviewPreset is not null;

    private void UpdateDeviceText()
    {
        var chosen = _viewModel.HasChosenDevice;
        DeviceTitle.Text = !chosen
            ? _viewModel.SetupDevices.Count > 1 ? "Which one is yours?" : "Plug in your DAC"
            : _viewModel.IsSystemEqSelected || _viewModel.IsDeviceConnected
                ? $"{_viewModel.SelectedDeviceProfile.DisplayName} is ready"
                : $"{_viewModel.SelectedDeviceProfile.DisplayName} isn't connected";
        var isDone = _viewModel.IsSystemEqSelected || _viewModel.IsDeviceConnected;
        DeviceBody.Text = isDone
            ? "Not the one you want? Pick another below."
            : "WolfEQ is looking for it over USB. Nothing is written to your device during setup.";
        Pulse1.Visibility = Pulse2.Visibility = isDone ? Visibility.Hidden : Visibility.Visible;
    }

    private void StartPulse()
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        Pulse(Pulse1, TimeSpan.Zero);
        Pulse(Pulse2, TimeSpan.FromSeconds(1.1));

        static void Pulse(UIElement ring, TimeSpan delay)
        {
            DoubleAnimation Animate(double from, double to) => new(from, to, TimeSpan.FromSeconds(2.2))
            {
                BeginTime = delay,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ring.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Animate(1, 1.55));
            ring.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, Animate(1, 1.55));
            ring.BeginAnimation(OpacityProperty, Animate(0.7, 0));
        }
    }

    private void ShowStep(int step)
    {
        var forward = step >= _step;
        ModelPicker.Visibility = Visibility.Collapsed;
        NextButton.Visibility = Visibility.Visible;
        _step = Math.Clamp(step, 0, _steps.Length - 1);
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;
            _dots[i].Width = i == _step ? 20 : 7;
            _dots[i].Opacity = i < _step ? 0.45 : 1;
            _dots[i].SetResourceReference(Border.BackgroundProperty, i <= _step ? "WolfGreenBrush" : "WolfLineSoftBrush");
        }

        _viewModel.RaiseSetupChanged();
        UpdateDeviceText();
        AutomationProperties.SetName(Dots, $"Step {_step + 1} of {_steps.Length}");
        BackButton.Content = _step == 0 ? "Skip" : "Back";
        NextButton.Content = _step == _steps.Length - 1 ? "Start tuning" : "Next";
        if (!IsLoaded) return;
        if (_step == 2) SearchBox.Focus();
        else NextButton.Focus();

        SlideIn(forward);
    }

    private void SlideIn(bool forward)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform();
        Steps.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(forward ? 28 : -28, 0, duration) { EasingFunction = ease });
        Steps.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == _steps.Length - 1) Close();
        // The device page has nothing to say until there is a device.
        else ShowStep(_step == 0 && !_viewModel.HasChosenDevice ? 2 : _step + 1);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (IsPickingModel) ShowStep(0);
        else if (_step == 0) Close();
        else ShowStep(_step == 2 && !_viewModel.HasChosenDevice ? 0 : _step - 1);
    }

    private async void Device_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not FrameworkElement { DataContext: QuickDeviceOption option }) return;
        _busy = true;
        NextButton.IsEnabled = false;
        try { await _viewModel.ChooseSetupDeviceAsync(option); }
        finally
        {
            _busy = false;
            NextButton.IsEnabled = true;
            _viewModel.RaiseSetupChanged();
            UpdateDeviceText();
        }
    }

    private void SoftwareEq_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) _viewModel.ChooseSetupModel(SystemEqProfiles.WindowsSystemEq);
    }

    private void ShowModels_Click(object sender, RoutedEventArgs e)
    {
        Step1.Visibility = Visibility.Collapsed;
        ModelPicker.Visibility = Visibility.Visible;
        // A model is picked by clicking it, so there is nothing for Next to do here.
        NextButton.Visibility = Visibility.Hidden;
        BackButton.Content = "Back";
        ModelSearchBox.Clear();
        FilterModels();
        ModelScroll.ScrollToTop();
        if (!IsLoaded) return;
        ModelSearchBox.Focus();
        SlideIn(true);
    }

    private void FilterModels()
    {
        var search = ModelSearchBox.Text.Replace(" ", "").Replace("-", "");
        var models = _viewModel.SetupModels
            .Where(profile => profile.DisplayName.Replace(" ", "").Replace("-", "").Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ModelList.ItemsSource = models;
        NoModelsText.Visibility = models.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ModelSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterModels();
        ModelScroll.ScrollToTop();
    }

    private void ModelSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (ModelList.Items.Count == 1) ChooseModel((FiioDeviceProfile)ModelList.Items[0]!);
    }

    private void Model_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FiioDeviceProfile profile }) ChooseModel(profile);
    }

    private void ChooseModel(FiioDeviceProfile profile)
    {
        if (_busy) return;
        _viewModel.ChooseSetupModel(profile);
        if (ReferenceEquals(_viewModel.SelectedDeviceProfile, profile)) ShowStep(1);
    }

    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Enter searches here instead of pressing Next.
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_viewModel.SearchGitHubProfilesCommand.CanExecute(null)) _viewModel.SearchGitHubProfilesCommand.Execute(null);
    }

    private async void UsePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGitHubProfile is not { } profile) return;
        UsePresetButton.IsEnabled = false;
        try
        {
            if (!await _viewModel.LoadSetupPresetAsync()) return;
            EqSummary.Text = $"Starting from {profile.Name}";
            ShowStep(3);
        }
        finally { UpdatePresetButton(); }
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
        if (e.Key != Key.Escape) return;
        if (IsPickingModel) ShowStep(0);
        else Close();
        e.Handled = true;
    }
}
