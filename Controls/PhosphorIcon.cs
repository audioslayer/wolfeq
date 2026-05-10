using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Path = System.Windows.Shapes.Path;

namespace WolfEQ.Controls;

public sealed class PhosphorIcon : Grid
{
    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
        nameof(IconName),
        typeof(string),
        typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(string.Empty, OnIconChanged));

    public static readonly DependencyProperty IconColorProperty = DependencyProperty.Register(
        nameof(IconColor),
        typeof(Color),
        typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(Color.FromRgb(0xE8, 0xE8, 0xE8), OnIconChanged));

    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register(
        nameof(IconBrush),
        typeof(Brush),
        typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(null, OnIconChanged));

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize),
        typeof(double),
        typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(20.0, OnIconChanged));

    public static readonly DependencyProperty BaseOpacityProperty = DependencyProperty.Register(
        nameof(BaseOpacity),
        typeof(double),
        typeof(PhosphorIcon),
        new FrameworkPropertyMetadata(0.22, OnIconChanged));

    private static ResourceDictionary? iconResources;
    private readonly Path basePath = new() { Stretch = Stretch.Uniform };
    private readonly Path detailPath = new() { Stretch = Stretch.Uniform };

    public PhosphorIcon()
    {
        Width = 20;
        Height = 20;
        Children.Add(basePath);
        Children.Add(detailPath);
    }

    public string IconName
    {
        get => (string)GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public Brush? IconBrush
    {
        get => (Brush?)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double BaseOpacity
    {
        get => (double)GetValue(BaseOpacityProperty);
        set => SetValue(BaseOpacityProperty, value);
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PhosphorIcon)d).UpdateIcon();

    private void UpdateIcon()
    {
        Width = IconSize;
        Height = IconSize;

        if (string.IsNullOrWhiteSpace(IconName))
        {
            basePath.Data = null;
            detailPath.Data = null;
            return;
        }

        basePath.Data = FindGeometry($"Ph.{IconName}.Base");
        detailPath.Data = FindGeometry($"Ph.{IconName}.Detail");

        var detailBrush = CloneBrush(IconBrush) ?? new SolidColorBrush(IconColor);
        var baseBrush = detailBrush.CloneCurrentValue();
        baseBrush.Opacity = BaseOpacity;

        basePath.Fill = baseBrush;
        detailPath.Fill = detailBrush;
    }

    private Geometry? FindGeometry(string key)
    {
        if (TryFindResource(key) is Geometry liveGeometry)
        {
            return liveGeometry;
        }

        iconResources ??= LoadIconResources();
        return iconResources?[key] as Geometry;
    }

    private static ResourceDictionary? LoadIconResources()
    {
        try
        {
            return (ResourceDictionary)Application.LoadComponent(
                new Uri("/WolfEQ;component/Icons/PhosphorIcons.xaml", UriKind.Relative));
        }
        catch
        {
            return null;
        }
    }

    private static Brush? CloneBrush(Brush? brush)
    {
        if (brush is null)
        {
            return null;
        }

        return brush.CloneCurrentValue();
    }
}
