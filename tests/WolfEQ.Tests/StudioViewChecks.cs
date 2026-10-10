using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WolfEQ.Controls;
using WolfEQ.ViewModels;
using WolfEQ.Views;
using Xunit;

namespace WolfEQ.Tests;

internal static class StudioViewChecks
{
    public static void Verify(MainViewModel model)
    {
        var window = new MainWindow();
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.DataContext = model;
        System.Windows.Documents.TextElement.SetFontFamily(content, new FontFamily("Segoe UI"));
        System.Windows.Documents.TextElement.SetFontSize(content, 14);
        Layout(content, 1640, 900);
        var editor = Descendants(content).OfType<EditorWorkspace>().Single();
        var graph = (EqResponseGraphControl)editor.FindName("Graph");
        graph.SelectedBand = model.Bands[0];
        var bands = Descendants(editor).OfType<BandControls>().Single();
        ((Expander)bands.FindName("ControlsExpander")).IsExpanded = true;
        Render(content, 1640, 900, "studio-editor");
        var bandSnapshot = (model.Bands[0].FrequencyHz, model.Bands[0].GainDb, model.Bands[0].Q);
        EditorFieldViewChecks.Verify(editor, graph, model.Bands[0]);
        (model.Bands[0].FrequencyHz, model.Bands[0].GainDb, model.Bands[0].Q) = bandSnapshot;
        Layout(content, 1640, 900);
        Render(content, 1586, 992, "studio-reference-size");
        var eqLayer = (DrawingVisual)VisualTreeHelper.GetChild(graph, 1);
        var retainedEqDrawing = System.Windows.Markup.XamlWriter.Save(eqLayer.Drawing);
        // Synthetic spectrum exercises the visual layer without capturing audio in UI tests.
        graph.SetSpectrumFrame(Enumerable.Range(0, WolfEQ.Services.PlaybackSpectrumAnalyzer.BinCount)
            .Select(i => (float)(0.13 + 0.65 * Math.Exp(-Math.Pow((i - 24) / 12.0, 2)) + 0.36 * Math.Exp(-Math.Pow((i - 72) / 18.0, 2)) + 0.06 * Math.Sin(i * 0.7))).ToArray());
        Render(content, 1586, 992, "studio-spectrum");
        Assert.Equal(retainedEqDrawing, System.Windows.Markup.XamlWriter.Save(eqLayer.Drawing));
        var spectrumLayer = (DrawingVisual)VisualTreeHelper.GetChild(graph, 0);
        Assert.NotEmpty(spectrumLayer.Drawing.Children);
        graph.SetSpectrumFrame(null);
        Assert.Null(spectrumLayer.Drawing);
        Assert.Equal(retainedEqDrawing, System.Windows.Markup.XamlWriter.Save(eqLayer.Drawing));
        graph.SetSpectrumFrame(null);
        var inspector = (FrameworkElement)editor.FindName("BandEditorCapsule");
        Assert.True(inspector.TranslatePoint(new Point(), editor).Y >= graph.TranslatePoint(new Point(0, graph.ActualHeight), editor).Y);
        Layout(content, 1280, 720);
        Render(content, 1280, 720, "studio-editor-compact");
        Assert.True(graph.ActualHeight >= 140, $"Curve must remain usable at minimum window size: {graph.ActualHeight}.");
        var tableChoice = Descendants(bands).OfType<RadioButton>().Single(b => Equals(b.Content, "Table"));
        tableChoice.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Layout(content, 1280, 720);
        Assert.Equal(1, ((TabControl)bands.FindName("ViewTabs")).SelectedIndex);
        Assert.True(graph.ActualHeight >= 120);
        Render(content, 1280, 720, "studio-table-compact");
        ((Expander)bands.FindName("ControlsExpander")).IsExpanded = false;
        Descendants(bands).OfType<RadioButton>().Single(b => Equals(b.Content, "Faders")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(((Expander)bands.FindName("ControlsExpander")).IsExpanded);

        var dock = Descendants(content).OfType<DeviceDock>().Single();
        var deviceImage = (Image)dock.FindName("DeviceProductImage");
        var deviceGallery = new StackPanel { Background = (Brush)Application.Current.FindResource("WolfPanelBrush") };
        foreach (var profile in model.DeviceProfiles)
        {
            // Exercise the binding without changing saved device preferences or connecting hardware.
            dock.DataContext = new { SelectedDeviceProfile = profile };
            Layout(dock, 1586, 80);
            var bitmap = Assert.IsAssignableFrom<BitmapSource>(deviceImage.Source);
            Assert.Equal(profile.ProductImagePath, bitmap.ToString());
            Assert.True(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 4, 12, 4) };
            row.Children.Add(new Image { Source = bitmap, Width = 72, Height = 52, Stretch = Stretch.Uniform });
            row.Children.Add(new TextBlock { Text = profile.DisplayName, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) });
            deviceGallery.Children.Add(row);
        }
        dock.ClearValue(FrameworkElement.DataContextProperty);
        Layout(dock, 1586, 80);
        Assert.Equal(model.SelectedDeviceProfile.ProductImagePath, deviceImage.Source.ToString());
        Render(deviceGallery, 420, 620, "studio-device-images");
        var savePopup = (Popup)dock.FindName("SaveLibraryPopup");
        ((TextBox)dock.FindName("LibraryPresetName")).Text = model.SuggestedLibraryName;
        Render((FrameworkElement)savePopup.Child, 340, 265, "studio-save-library");
        var slots = (Popup)dock.FindName("SlotPopup");
        var slotContent = (FrameworkElement)slots.Child;
        slotContent.DataContext = model;
        var unnamedSlot = model.DeviceUserPresets[0];
        model.DeviceUserPresets[0] = unnamedSlot with { LocalEqName = "Evening music" };
        Render(slotContent, 296, 560, "studio-slot-menu");
        Assert.Contains(Descendants(slotContent).OfType<TextBlock>(), text => text.Text == "Evening music");
        Assert.Contains(Descendants(slotContent).OfType<TextBlock>(), text => text.Text == "No local name yet");
        var slotButtons = Descendants((ItemsControl)dock.FindName("SlotItems")).OfType<Button>().ToArray();
        Assert.Equal(model.DeviceUserPresets.Count, slotButtons.Length);
        Assert.All(slotButtons, button => Assert.Same(model.SwitchSlotCommand, button.Command));
        Assert.All(slotButtons, button => Assert.Contains(button.CommandParameter, model.DeviceUserPresets));
        // Footer opens Settings to name the slot; it is the only other button in the menu.
        var nameSlot = (Button)dock.FindName("NameSlotButton");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)nameSlot.Parent).Visibility);
        Assert.Equal(slotButtons.Length + 1, Descendants(slotContent).OfType<Button>().Count());
        model.DeviceUserPresets[0] = unnamedSlot;

        var tools = (Popup)editor.FindName("ToolsPopup");
        var toolsContent = (FrameworkElement)tools.Child;
        toolsContent.DataContext = model;
        Render(toolsContent, 400, 540, "studio-tools");
        var toolsTabs = (TabControl)editor.FindName("ToolsTabs");
        var toolCommands = new HashSet<System.Windows.Input.ICommand>();
        foreach (TabItem tab in toolsTabs.Items)
        {
            toolsTabs.SelectedItem = tab;
            Layout(toolsContent, 400, 540);
            foreach (var expander in Descendants(toolsContent).OfType<Expander>()) expander.IsExpanded = true;
            Layout(toolsContent, 400, 540);
            toolsContent.Measure(new Size(400, double.PositiveInfinity));
            Assert.Equal(540, toolsContent.DesiredSize.Height);
            foreach (var button in Descendants(toolsContent).OfType<Button>().Where(button => button.Command is not null))
                toolCommands.Add(button.Command);
            Render(toolsContent, 400, 540, "studio-tools-" + tab.Header.ToString()!.ToLowerInvariant());
        }
        Assert.Equal(16, toolCommands.Count);
        Assert.Contains(model.CaptureCompareCommand, toolCommands);
        Assert.Contains(model.ApplyAutoHeadroomCommand, toolCommands);
        toolsTabs.SelectedIndex = 0;

        foreach (var section in new[] { "Device", "Profile Lights", "Windows Audio", "Integrations", "Appearance", "About" })
        {
            var settings = new SettingsPopover { DataContext = model };
            Layout(settings, 460, 1400);
            var expanders = Descendants(settings).OfType<Expander>().ToArray();
            // Render menu contents without invoking the Windows Audio refresh-on-open action.
            settings.DataContext = null;
            foreach (var expander in expanders) expander.IsExpanded = Equals(expander.Header, section);
            settings.DataContext = model;
            Layout(settings, 460, 1400);
            if (section is "Device" or "Integrations")
            {
                foreach (var expander in Descendants(settings).OfType<Expander>().Where(e => Equals(e.Header, "Model & compatibility") || Equals(e.Header, "Setup & troubleshooting")))
                    expander.IsExpanded = true;
            }
            Render(settings, 460, 1400, "studio-settings-" + section.Replace(' ', '-').ToLowerInvariant());
            if (section == "Integrations")
            {
                var setupPopup = (Popup)settings.FindName("StreamDeckSetupPopup");
                var setupContent = (FrameworkElement)setupPopup.Child;
                setupContent.DataContext = model;
                Render(setupContent, 420, 580, "studio-streamdeck-setup");
            }
        }

        var tray = new TrayFlyout(model, () => { }, () => { }, () => { });
        var trayContent = (FrameworkElement)tray.Content;
        trayContent.DataContext = tray.DataContext;
        tray.Content = null;
        Render(trayContent, 368, 720, "studio-tray");
        tray.DisposeFlyout();

        var library = new LibraryPanel { DataContext = model };
        Layout(library, 460, 1000);
        Descendants(library).OfType<Expander>().Single().IsExpanded = true;
        Render(library, 460, 1000, "studio-library");
        var savedSearch = model.ProfileSearchText;
        model.ProfileSearchText = "__no_matching_preset_ui_review__";
        Render(library, 460, 1000, "studio-library-empty");
        Assert.Equal(Visibility.Collapsed, ((Border)library.FindName("LibrarySelectionActions")).Visibility);
        model.ProfileSearchText = savedSearch;
        var presetActions = (Popup)library.FindName("PresetActionsPopup");
        ((FrameworkElement)presetActions.Child).DataContext = model;
        Render((FrameworkElement)presetActions.Child, 230, 150, "studio-library-actions");
        ((RadioButton)library.FindName("OnlineSourceChip")).IsChecked = true;
        var previewProperty = typeof(MainViewModel).GetProperty(nameof(MainViewModel.GitHubPreviewPreset))!;
        var originalPreview = model.GitHubPreviewPreset;
        var preview = WolfEQ.Models.EqPreset.AnandaMusic();
        previewProperty.SetValue(model, preview);
        Render(library, 460, 1000, "studio-library-online");
        var previewTabs = (TabControl)library.FindName("OnlinePreviewTabs");
        var curveHeight = previewTabs.ActualHeight;
        ((RadioButton)library.FindName("PreviewTableChoice")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Layout(library, 460, 1000);
        var previewTable = (DataGrid)library.FindName("OnlinePreviewTable");
        Assert.Equal(1, previewTabs.SelectedIndex);
        Assert.True(previewTable.IsReadOnly);
        Assert.Same(preview.Bands, previewTable.ItemsSource);
        Assert.Equal(6, previewTable.Columns.Count);
        Assert.Equal(curveHeight, previewTabs.ActualHeight);
        Render(library, 460, 1000, "studio-library-online-table");
        var replacement = new WolfEQ.Models.EqPreset { Name = "Second preview", Bands = new() { new() { Number = 1, FrequencyHz = 120, GainDb = -2, Q = 0.7 } } };
        previewProperty.SetValue(model, replacement);
        Layout(library, 460, 1000);
        Assert.Same(replacement.Bands, previewTable.ItemsSource);
        previewProperty.SetValue(model, null);
        Layout(library, 460, 1000);
        Assert.Empty(previewTable.Items);
        ((RadioButton)library.FindName("PreviewCurveChoice")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(0, previewTabs.SelectedIndex);
        previewProperty.SetValue(model, originalPreview);
        VerifyThemeChanges(content, model);
        VerifySettingsIconThemes(model);
    }

    private static void VerifySettingsIconThemes(MainViewModel model)
    {
        var original = model.SelectedAccentColorOption;
        var settings = new SettingsPopover();
        var host = new Window { Content = settings };
        try
        {
            Layout(settings, 460, 1400);
            Descendants(settings).OfType<Expander>().Single(e => Equals(e.Header, "Device")).IsExpanded = true;
            settings.DataContext = model;
            Layout(settings, 460, 1400);
            var accent = (Brush)Application.Current.FindResource("WolfGreenBrush");
            var icons = Descendants(settings).OfType<PhosphorIcon>().Where(icon => ReferenceEquals(icon.IconBrush, accent)).ToArray();
            Assert.True(icons.Length >= 8, "Cover section headers and device action icons.");
            foreach (var name in new[] { "AmpUp Green", "Violet", "Wolf Cyan", "AmpUp Green" })
            {
                model.SelectedAccentColorOption = model.AccentColorOptions.Single(option => option.Name == name);
                Layout(settings, 460, 1400);
                var expected = (Color)ColorConverter.ConvertFromString(model.SelectedAccentColorOption.Hex);
                foreach (var icon in icons)
                {
                    var layers = icon.Children.OfType<System.Windows.Shapes.Path>().ToArray();
                    Assert.All(layers, layer => Assert.Equal(expected, Assert.IsType<SolidColorBrush>(layer.Fill).Color));
                    Assert.Equal(icon.BaseOpacity, layers[0].Opacity);
                    Assert.Equal(1, layers[1].Opacity);
                }
            }
            Render(settings, 460, 1400, "studio-settings-green-icons");
        }
        finally
        {
            model.SelectedAccentColorOption = original;
            host.Content = null;
            host.Close();
        }
    }

    private static void VerifyThemeChanges(FrameworkElement content, MainViewModel model)
    {
        // Resource invalidation is delivered through application windows, as in the running app.
        var host = new Window { Content = content };
        var original = model.SelectedAccentColorOption;
        var accent = ((SolidColorBrush)Application.Current.FindResource("WolfGreenBrush")).Color;
        // Capture already-created accent surfaces so a new view cannot hide stale brush references.
        var properties = new[] { Control.ForegroundProperty, Control.BackgroundProperty,
            Border.BackgroundProperty, Border.BorderBrushProperty, TextBlock.ForegroundProperty,
            System.Windows.Shapes.Shape.FillProperty, System.Windows.Shapes.Shape.StrokeProperty };
        var surfaces = Descendants(content).SelectMany(element => properties.Select(property => (element, property)))
            .Where(pair => pair.element.GetValue(pair.property) is SolidColorBrush brush && brush.Color == accent).ToArray();
        Assert.True(surfaces.Length > 25, "Must exercise header, faders, buttons, and selected-band accents.");
        try
        {
            foreach (var name in new[] { "Wolf Cyan", "Violet", "FiiO Red" })
            {
                model.SelectedAccentColorOption = model.AccentColorOptions.Single(option => option.Name == name);
                Layout(content, 1586, 992);
                var expected = (Color)ColorConverter.ConvertFromString(model.SelectedAccentColorOption.Hex);
                foreach (var (element, property) in surfaces)
                    Assert.Equal(expected, Assert.IsType<SolidColorBrush>(element.GetValue(property)).Color);
                Assert.All(Descendants(content).OfType<StyledSlider>(), slider => Assert.Equal(expected, slider.AccentColor));
                if (name == "Wolf Cyan") Render(content, 1586, 992, "studio-theme-blue");
            }
            Assert.Equal(Colors.White, ((SolidColorBrush)Application.Current.FindResource("WolfGreenContrastBrush")).Color);
            var savedMode = WolfEQ.Services.BackgroundThemeService.Load();
            try
            {
                model.SelectedBackgroundMode = "Light";
                model.SelectedAccentColorOption = model.AccentColorOptions[0];
                Layout(content, 1586, 992);
                Assert.Equal("Light", WolfEQ.Services.BackgroundThemeService.CurrentMode);
                Assert.Equal(Colors.White, ((SolidColorBrush)Application.Current.FindResource("WolfPanelBrush")).Color);
                Assert.Equal("Light", WolfEQ.Services.BackgroundThemeService.Load());
                Assert.Equal("AmpUp Green", model.SelectedAccentColorOption.Name);
                var lightAccent = (Color)Application.Current.FindResource("WolfGreen");
                Assert.InRange(lightAccent.G, (byte)90, (byte)140);
                foreach (var (element, property) in surfaces)
                    Assert.Equal(lightAccent, Assert.IsType<SolidColorBrush>(element.GetValue(property)).Color);
                Assert.All(Descendants(content).OfType<TextBlock>().Where(text => text.Text == "EQ Curve"),
                    text => Assert.Equal((Color)Application.Current.FindResource("WolfText"), Assert.IsType<SolidColorBrush>(text.Foreground).Color));
                Render(content, 1586, 992, "studio-theme-light");
                var settings = new SettingsPopover { DataContext = model };
                Layout(settings, 460, 850);
                foreach (var section in Descendants(settings).OfType<Expander>()) section.IsExpanded = Equals(section.Header, "Appearance");
                Render(settings, 460, 850, "studio-settings-light");
                var library = new LibraryPanel { DataContext = model };
                Render(library, 460, 900, "studio-library-light");
                model.SelectedAccentColorOption = model.AccentColorOptions.Single(option => option.Name == "Aurora");
                Render(content, 1586, 992, "studio-theme-light-aurora");
                model.SelectedBackgroundMode = "Dark";
                Assert.Equal((Color)ColorConverter.ConvertFromString("#111C22"), ((SolidColorBrush)Application.Current.FindResource("WolfPanelBrush")).Color);
                Render(content, 1586, 992, "studio-theme-dark-restored");
            }
            finally
            {
                model.SelectedBackgroundMode = "Dark";
                WolfEQ.Services.BackgroundThemeService.Save(savedMode);
            }

            foreach (var palette in model.AccentColorOptions.Where(option => option.IsMulticolor))
            {
                model.SelectedAccentColorOption = palette;
                Layout(content, 1586, 992);
                var expected = Assert.IsType<LinearGradientBrush>(palette.Brush);
                foreach (var (element, property) in surfaces)
                {
                    var actual = Assert.IsType<LinearGradientBrush>(element.GetValue(property));
                    Assert.Equal(expected.GradientStops.Select(stop => stop.Color), actual.GradientStops.Select(stop => stop.Color));
                }
                Assert.Equal(Colors.Black, ((SolidColorBrush)Application.Current.FindResource("WolfGreenContrastBrush")).Color);
                if (palette.Name == "Aurora") Render(content, 1586, 992, "studio-theme-aurora");
            }
        }
        finally
        {
            model.SelectedAccentColorOption = original;
            host.Content = null;
            host.Close();
        }
    }

    internal static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    internal static void Render(FrameworkElement element, int width, int height, string name)
    {
        Layout(element, width, height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, name + ".png"));
        encoder.Save(output);
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
