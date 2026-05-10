using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WolfEQ.Models;
using WolfEQ.ViewModels;

namespace WolfEQ;

public partial class MainWindow : Window
{
    private const double ExpandedEqBandCardWidth = 158;
    private const double ExpandedInspectorDrawerWidth = 420;
    private const double EqBandCardGap = 8;
    private const int EqBandCardCount = 10;
    private Point _bandDragStartPoint;
    private EqBand? _bandDragCandidate;
    private bool _inspectorDrawerCollapsed = true;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetInspectorDrawerCollapsed(true);

        if (DataContext is MainViewModel viewModel &&
            viewModel.ConnectCommand.CanExecute(null))
        {
            viewModel.ConnectCommand.Execute(null);
        }
    }

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

    private void ProfileSwitcherButton_Click(object sender, RoutedEventArgs e)
    {
        UserProfileFlyout.IsOpen = !UserProfileFlyout.IsOpen;
        e.Handled = true;
    }

    private void UserProfileItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel &&
            sender is FrameworkElement { DataContext: DeviceUserPresetOption option })
        {
            viewModel.SelectedDeviceUserPreset = option;
            if (viewModel.SelectUserPresetCommand.CanExecute(null))
            {
                viewModel.SelectUserPresetCommand.Execute(null);
            }
        }

        UserProfileFlyout.IsOpen = false;
        e.Handled = true;
    }

    private void SidebarSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = SettingsTab;
        e.Handled = true;
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

    private void SlotLightingUserComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded ||
            e.AddedItems.Count == 0 ||
            DataContext is not MainViewModel viewModel ||
            !viewModel.SelectUserPresetCommand.CanExecute(null))
        {
            return;
        }

        viewModel.SelectUserPresetCommand.Execute(null);
    }

    private void ToggleInspectorDrawer_Click(object sender, RoutedEventArgs e)
    {
        SetInspectorDrawerCollapsed(!_inspectorDrawerCollapsed);
        e.Handled = true;
    }

    private void SetInspectorDrawerCollapsed(bool collapsed)
    {
        _inspectorDrawerCollapsed = collapsed;
        InspectorDrawerColumn.Width = collapsed
            ? new GridLength(0)
            : new GridLength(ExpandedInspectorDrawerWidth);
        InspectorDrawerHost.Visibility = collapsed
            ? Visibility.Collapsed
            : Visibility.Visible;
        InspectorDrawerToggleButton.Tag = collapsed ? "Collapsed" : "Expanded";

        InspectorDrawerToggleButton.ApplyTemplate();

        UpdateEqBandCardLayout();
    }

    private void EqBandScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_inspectorDrawerCollapsed)
        {
            UpdateEqBandCardLayout();
        }
    }

    private void BandValueTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
        {
            return;
        }

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void UpdateEqBandCardLayout()
    {
        if (!_inspectorDrawerCollapsed)
        {
            EqBandItemsControl.Tag = ExpandedEqBandCardWidth;
            EqBandScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            return;
        }

        var viewportWidth = EqBandScrollViewer.ActualWidth;
        if (viewportWidth <= 0)
        {
            return;
        }

        var totalGapWidth = EqBandCardGap * (EqBandCardCount - 1);
        var cardWidth = Math.Floor((viewportWidth - totalGapWidth - 2) / EqBandCardCount);
        EqBandItemsControl.Tag = Math.Max(96, cardWidth);
        EqBandScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
    }

    private void BandDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EqBand band })
        {
            return;
        }

        _bandDragCandidate = band;
        _bandDragStartPoint = e.GetPosition(this);
    }

    private void BandDragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_bandDragCandidate == null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _bandDragCandidate = null;
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _bandDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _bandDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(typeof(EqBand), _bandDragCandidate);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        _bandDragCandidate = null;
    }

    private void BandCard_DragEnter(object sender, DragEventArgs e) => UpdateBandDropTarget(sender, e);

    private void BandCard_DragOver(object sender, DragEventArgs e) => UpdateBandDropTarget(sender, e);

    private void BandCard_DragLeave(object sender, DragEventArgs e)
    {
        ClearBandDropTarget(sender);
    }

    private void BandCard_Drop(object sender, DragEventArgs e)
    {
        ClearBandDropTarget(sender);

        if (DataContext is not MainViewModel viewModel ||
            sender is not Border targetCard ||
            targetCard.DataContext is not EqBand targetBand ||
            !TryGetDraggedBand(e, out var sourceBand) ||
            ReferenceEquals(sourceBand, targetBand))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var insertAfter = e.GetPosition(targetCard).X > targetCard.ActualWidth / 2;
        viewModel.MoveBand(sourceBand, targetBand, insertAfter);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void UpdateBandDropTarget(object sender, DragEventArgs e)
    {
        if (sender is not Border targetCard ||
            targetCard.DataContext is not EqBand targetBand ||
            !TryGetDraggedBand(e, out var sourceBand) ||
            ReferenceEquals(sourceBand, targetBand))
        {
            ClearBandDropTarget(sender);
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        targetCard.BorderBrush = (Brush)FindResource("WolfFiioRedBrush");
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static bool TryGetDraggedBand(DragEventArgs e, out EqBand band)
    {
        if (e.Data.GetDataPresent(typeof(EqBand)) &&
            e.Data.GetData(typeof(EqBand)) is EqBand draggedBand)
        {
            band = draggedBand;
            return true;
        }

        band = null!;
        return false;
    }

    private static void ClearBandDropTarget(object sender)
    {
        if (sender is Border targetCard)
        {
            targetCard.ClearValue(Border.BorderBrushProperty);
        }
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

    private static T? FindVisualChild<T>(DependencyObject source, string name)
        where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T typedChild && typedChild.Name == name)
            {
                return typedChild;
            }

            var match = FindVisualChild<T>(child, name);
            if (match is not null)
            {
                return match;
            }
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
