using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WolfEQ.Controls;

/// <summary>
/// Small "?" that shows a help card on hover or keyboard focus (the AmpUp pattern). Use it instead
/// of explanatory paragraphs so panels stay compact: <c>&lt;controls:HelpTip Title="Volume limit" Text="..."/&gt;</c>.
/// </summary>
public sealed class HelpTip : Border
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(HelpTip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(HelpTip), new PropertyMetadata(string.Empty));

    private readonly TextBlock _glyph;

    public HelpTip()
    {
        Width = 16;
        Height = 16;
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1.3);
        Background = Brushes.Transparent;
        Cursor = Cursors.Help;
        Focusable = true;
        // Focus is shown by the accent ring and the open card; the default dashed rectangle looks broken on a 16 px circle.
        FocusVisualStyle = null;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(6, 0, 0, 0);
        SetResourceReference(BorderBrushProperty, "WolfMutedBrush");

        _glyph = new TextBlock
        {
            Text = "?",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 0, 0)
        };
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, "WolfMutedBrush");
        Child = _glyph;

        ToolTip = BuildCard();
        ToolTipService.SetInitialShowDelay(this, 120);
        ToolTipService.SetShowDuration(this, 60000);
        // Help should still explain a control that is disabled (that's often when it's needed).
        ToolTipService.SetShowOnDisabled(this, true);
        ToolTipService.SetPlacement(this, System.Windows.Controls.Primitives.PlacementMode.Top);
        System.Windows.Automation.AutomationProperties.SetName(this, "Help");

        MouseEnter += (_, _) => Highlight(true);
        MouseLeave += (_, _) => Highlight(IsKeyboardFocused);
        GotKeyboardFocus += (_, _) => { Highlight(true); if (ToolTip is ToolTip tip) tip.IsOpen = true; };
        LostKeyboardFocus += (_, _) => { Highlight(false); if (ToolTip is ToolTip tip) tip.IsOpen = false; };
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void Highlight(bool on)
    {
        var key = on ? "WolfGreenBrush" : "WolfMutedBrush";
        SetResourceReference(BorderBrushProperty, key);
        _glyph.SetResourceReference(TextBlock.ForegroundProperty, key);
    }

    /// <summary>Help card: panel background, accent hairline on top, bold title and wrapped text.</summary>
    private ToolTip BuildCard()
    {
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 0, 0, 3) };
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Title)) { Source = this });
        title.SetResourceReference(TextBlock.ForegroundProperty, "WolfTextBrush");
        title.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Title))
        {
            Source = this,
            Converter = new EmptyToCollapsedConverter()
        });

        var text = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Text)) { Source = this });
        text.SetResourceReference(TextBlock.ForegroundProperty, "WolfMutedBrush");

        var card = new Border
        {
            MaxWidth = 260,
            Padding = new Thickness(12, 9, 12, 11),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Child = new StackPanel { Children = { title, text } }
        };
        card.SetResourceReference(Border.CornerRadiusProperty, "RadiusCard");
        card.SetResourceReference(Border.BackgroundProperty, "WolfPanel2Brush");
        card.SetResourceReference(Border.BorderBrushProperty, "WolfGreenBrush");

        var tip = new ToolTip
        {
            Content = card,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HasDropShadow = true
        };
        tip.PlacementTarget = this;
        return tip;
    }

    private sealed class EmptyToCollapsedConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
