using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WolfEQ.Models;

namespace WolfEQ.Views;

public partial class BandControls : UserControl
{
    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(ObservableCollection<EqBand>), typeof(BandControls), new PropertyMetadata(null, BandsChanged));
    public static readonly DependencyProperty ProfileProperty = DependencyProperty.Register(
        nameof(Profile), typeof(FiioDeviceProfile), typeof(BandControls));
    public static readonly DependencyProperty SelectedBandProperty = DependencyProperty.Register(
        nameof(SelectedBand), typeof(EqBand), typeof(BandControls),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, SelectedBandChanged));

    // With 31 bands the knob/fader rows scroll horizontally; keep the selected band's tile in view.
    private static void SelectedBandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not EqBand band) return;
        var control = (BandControls)d;
        control.Dispatcher.BeginInvoke(new Action(() => control.BringBandIntoView(band)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BringBandIntoView(EqBand band)
    {
        // Selecting by grabbing a knob must not slide it out from under the pointer.
        if (ViewTabs?.SelectedContent is not DependencyObject root || !IsLoaded || Mouse.LeftButton == MouseButtonState.Pressed) return;
        foreach (var tile in FindTiles(root))
        {
            if (!ReferenceEquals(tile.DataContext, band)) continue;
            tile.BringIntoView();
            break;
        }
    }

    private static IEnumerable<FrameworkElement> FindTiles(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is Border { Name: "Tile" } tile) yield return tile;
            foreach (var nested in FindTiles(child)) yield return nested;
        }
    }

    public ObservableCollection<EqBand>? Bands { get => (ObservableCollection<EqBand>?)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public FiioDeviceProfile? Profile { get => (FiioDeviceProfile?)GetValue(ProfileProperty); set => SetValue(ProfileProperty, value); }
    public EqBand? SelectedBand { get => (EqBand?)GetValue(SelectedBandProperty); set => SetValue(SelectedBandProperty, value); }

    public bool IsExpanded => ControlsExpander.IsExpanded;

    private readonly HashSet<EqBand> _observed = [];
    private ObservableCollection<EqBand>? _observedCollection;
    private bool _holdKnobOrder;
    private bool _knobSortQueued;

    public BandControls()
    {
        InitializeComponent();
        Loaded += (_, _) => ObserveBands();
        Unloaded += (_, _) => StopObserving();
    }

    private static void BandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BandControls)d;
        control.ObserveBands();
    }

    private void StopObserving()
    {
        if (_observedCollection is not null) _observedCollection.CollectionChanged -= CollectionChanged;
        foreach (var band in _observed) band.PropertyChanged -= BandChanged;
        _observed.Clear();
        _observedCollection = null;
    }

    private void ObserveBands()
    {
        StopObserving();
        _observedCollection = Bands;
        if (Bands is not null)
        {
            Bands.CollectionChanged += CollectionChanged;
            foreach (var band in Bands)
                if (_observed.Add(band)) band.PropertyChanged += BandChanged;
        }
        UpdateSummary();
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ObserveBands();
    private void BandChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateSummary();
        if (!_holdKnobOrder && e.PropertyName is (nameof(EqBand.FrequencyHz) or nameof(EqBand.Number))) QueueKnobSort();
    }

    // Loading a preset changes many bands at once; sort once afterwards.
    private void QueueKnobSort()
    {
        if (_knobSortQueued) return;
        _knobSortQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _knobSortQueued = false;
            if (!_holdKnobOrder) HoldKnobOrder(false);
        });
    }

    private void UpdateSummary()
    {
        if (Summary is not null)
            Summary.Text = $"{Bands?.Count ?? 0} bands  ·  {Bands?.Count(b => b.Enabled) ?? 0} enabled";
    }

    private void SelectBand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EqBand band }) SelectedBand = band;
    }

    // Turning a knob also selects its band, so the graph shows which curve is moving.
    private void Knob_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EqBand band }) SelectedBand = band;
    }

    // Clicking a knob must not scroll the row, or the dial would slide out from under the pointer.
    // Keep this guard: BringBandIntoView above runs only from selection changes, not from knob drags.
    private void Knob_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Pressed) e.Handled = true;
    }

    private void KnobsPanel_MouseChanged(object sender, MouseEventArgs e) => UpdateKnobOrder();
    private void KnobsPanel_Changed(object sender, DependencyPropertyChangedEventArgs e) => UpdateKnobOrder();

    private void UpdateKnobOrder()
        => HoldKnobOrder(KnobsPanel.IsMouseOver
            || (KnobsPanel.IsKeyboardFocusWithin && InputManager.Current.MostRecentInputDevice is KeyboardDevice));

    /// <summary>Tiles sort by frequency, but never while a knob is in use: crossing a neighbour would swap the tiles mid-turn.</summary>
    private void HoldKnobOrder(bool hold)
    {
        _holdKnobOrder = hold;
        if (hold || ((CollectionViewSource)Resources["KnobBands"]).View is not { } view) return;
        var shown = view.Cast<EqBand>().ToArray();
        // A refresh rebuilds every tile, so only do it when the order really changed.
        if (!shown.SequenceEqual(shown.OrderBy(b => b.FrequencyHz).ThenBy(b => b.Number))) view.Refresh();
    }

    private void ViewChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string index })
            ViewTabs.SelectedIndex = int.Parse(index, CultureInfo.InvariantCulture);
        ControlsExpander.IsExpanded = true;
        e.Handled = true;
    }

    // The help "?" sits in the expander header; clicking it should not also fold the panel.
    private void HelpTip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Value_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        if (e.Key == Key.Enter)
        {
            CommitValue(box);
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void Value_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) CommitValue(box);
    }

    private void CommitValue(TextBox box)
    {
        var binding = box.GetBindingExpression(TextBox.TextProperty);
        var text = box.Text.Trim();
        if (Equals(box.Tag, "Gain") && text.EndsWith("dB", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        if (box.DataContext is EqBand band && Profile is { } profile &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && double.IsFinite(value))
        {
            switch (box.Tag)
            {
                case "Frequency": band.FrequencyHz = (int)Math.Round(Math.Clamp(value, 20, 20000)); break;
                case "Gain": band.GainDb = Math.Clamp(value, profile.MinGainDb, profile.MaxGainDb); break;
                case "Q": band.Q = Math.Clamp(value, profile.MinQ, profile.MaxQ); break;
            }
        }
        binding?.UpdateTarget();
    }
}

public sealed class BandOrdinalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => $"{(Equals(parameter, "Number") ? "" : "Band ")}{(value is int index ? index + 1 : 1)}";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BandHasGainConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is EqFilterType.Peak or EqFilterType.LowShelf or EqFilterType.HighShelf;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool enabled ? !enabled : false;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool bypassed ? !bypassed : Binding.DoNothing;
}

// Leaves room for the panel header and every row of a fader tile (number, frequency, gain,
// Hz, Q, shape and Bypass) so the Bypass row is not clipped at the bottom of the panel.
public sealed class ReferenceFaderHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double height && double.IsFinite(height) ? Math.Clamp(height - 326, 64, 900) : 100d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
