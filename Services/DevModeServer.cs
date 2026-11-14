using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WolfEQ.Services;

/// <summary>
/// Developer-only UI control endpoint so Claude (or any local tool) can see and drive the running UI.
/// Started ONLY when WolfEQ is launched with <c>--dev</c> (deploy.bat does this); installed builds,
/// Stream Deck commands and Start-with-Windows launches never open the port.
///
/// Listens on http://127.0.0.1:47824 (loopback only). Every request must send the header
/// <c>X-WolfEQ-Dev: 1</c> and a loopback Host, so web pages in a browser cannot drive the app.
///   GET /health                         → "ok"
///   GET /status                         → JSON: status line, version, open windows/popups
///   GET /screenshot[?scale=1&amp;delay=300]   → PNG of the main window with open popups/dialogs drawn on top
///   GET /elements[?q=text&amp;all=1]           → JSON list of interactive elements with bounds (DIPs)
///   GET /click?name=|text=|x=&amp;y=[&amp;nth=0]  → click a button/item by x:Name, visible text, or point
///   GET /type?name=|text=&amp;value=[&amp;enter=1] → set a text box's text (and optionally press Enter)
///   GET /set?name=|text=&amp;value=             → set a slider value, combo box item, or toggle state
///   GET /scroll?name=|text=                 → bring an element into view
///   GET /key?key=Escape                     → send a key to the focused element
/// Coordinates are DIPs relative to the main window's top-left, matching a scale=1 screenshot.
/// </summary>
public sealed class DevModeServer : IDisposable
{
    public const int Port = 47824;
    public const string Flag = "--dev";
    public const string RequiredHeader = "X-WolfEQ-Dev";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly MethodInfo? OnClickMethod =
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly Window _window;
    private readonly Func<string> _status;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    private DevModeServer(Window window, Func<string> status)
    {
        _window = window;
        _status = status;
        _listener = new TcpListener(IPAddress.Loopback, Port);
    }

    /// <summary>Removes <see cref="Flag"/> from the arguments so the rest of startup never sees it.</summary>
    public static string[] ExtractFlag(string[] args, out bool enabled)
    {
        enabled = args.Any(IsFlag);
        return enabled ? args.Where(a => !IsFlag(a)).ToArray() : args;
    }

    private static bool IsFlag(string arg) => string.Equals(arg, Flag, StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts the server; returns null (and logs to the debugger) if the port is unavailable.</summary>
    public static DevModeServer? Start(Window window, Func<string> status)
    {
        try
        {
            var server = new DevModeServer(window, status);
            server._listener.Start();
            _ = Task.Run(server.AcceptLoopAsync);
            System.Diagnostics.Debug.WriteLine($"WolfEQ dev mode listening on http://127.0.0.1:{Port}");
            return server;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WolfEQ dev mode failed to start: {ex.Message}");
            return null;
        }
    }

    // ── HTTP ─────────────────────────────────────────────────────────

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync() ?? "";
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }

                var parts = requestLine.Split(' ');
                if (parts.Length < 2 || parts[0] != "GET") { await WriteAsync(stream, 405, "text/plain", "GET only"); return; }
                if (!IsTrustedRequest(headers))
                {
                    await WriteAsync(stream, 403, "text/plain", $"Send the header '{RequiredHeader}: 1' from a local tool.");
                    return;
                }

                var target = parts[1];
                var path = target.Split('?')[0].TrimEnd('/').ToLowerInvariant();
                var q = ParseQuery(target);

                switch (path)
                {
                    case "/health":
                        await WriteAsync(stream, 200, "text/plain", "ok");
                        break;
                    case "/status":
                        await WriteJsonAsync(stream, await OnUi(Status));
                        break;
                    case "/screenshot":
                        await OnUi(() => { BringUp(); return 0; });
                        await SettleAsync(q, 300);
                        await WriteAsync(stream, 200, "image/png", await OnUi(() => Render(q)));
                        break;
                    case "/elements":
                        await WriteJsonAsync(stream, await OnUi(() => ListElements(q)));
                        break;
                    case "/click":
                    case "/type":
                    case "/set":
                    case "/scroll":
                    case "/key":
                        var result = await OnUi(() => Act(path, q));
                        await SettleAsync(q, 250);
                        await WriteJsonAsync(stream, result);
                        break;
                    default:
                        await WriteAsync(stream, 404, "text/plain",
                            "Try /health, /status, /screenshot, /elements, /click, /type, /set, /scroll or /key.");
                        break;
                }
            }
            catch (Exception ex)
            {
                var message = ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message;
                try { await WriteAsync(stream, ex is ArgumentException ? 400 : 500, "text/plain", message); } catch { }
            }
        }
    }

    private static bool IsTrustedRequest(Dictionary<string, string> headers)
    {
        // A browser cannot add a custom header to a cross-origin request without a CORS preflight,
        // which this server never approves; the Host check blocks DNS-rebinding pages.
        if (!headers.TryGetValue(RequiredHeader, out var token) || token != "1") return false;
        if (!headers.TryGetValue("Host", out var host)) return false;
        var hostName = host.Split(':')[0];
        return hostName is "127.0.0.1" or "localhost";
    }

    private Task<T> OnUi<T>(Func<T> func) => _window.Dispatcher.InvokeAsync(func).Task;

    private static Task SettleAsync(Dictionary<string, string> q, int defaultMs)
        => Task.Delay(q.TryGetValue("delay", out var ds) && int.TryParse(ds, out var d) ? Math.Clamp(d, 0, 5000) : defaultMs);

    private void BringUp()
    {
        if (!_window.IsVisible && Application.Current is App app) app.RestoreEditor();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
    }

    // ── Status ───────────────────────────────────────────────────────

    private object Status() => new
    {
        status = _status(),
        version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
        window = new { visible = _window.IsVisible, state = _window.WindowState.ToString(), width = _window.ActualWidth, height = _window.ActualHeight },
        surfaces = Surfaces().Select(s => s.Label).ToArray(),
        focused = Describe(Keyboard.FocusedElement as DependencyObject)
    };

    // ── Surfaces: the main window plus any open popup or dialog ─────────

    private sealed record Surface(Visual Root, Point Offset, Size Size, string Label, Brush? Background = null);

    private Visual MainRoot => (Visual)(_window.Content ?? _window);

    private IEnumerable<Surface> Surfaces()
    {
        var main = MainRoot;
        var mainSize = main is FrameworkElement mf ? new Size(mf.ActualWidth, mf.ActualHeight) : new Size(_window.ActualWidth, _window.ActualHeight);
        yield return new Surface(main, new Point(0, 0), mainSize, "main");

        foreach (var source in PresentationSource.CurrentSources.OfType<HwndSource>())
        {
            if (source.IsDisposed || source.RootVisual is not Visual rootVisual || !IsWindowVisible(source.Handle)) continue;
            if (ReferenceEquals(rootVisual, _window)) continue;

            var label = rootVisual is Window w ? $"dialog: {w.Title}" : "popup";
            // Render dialogs as the whole Window (template + content): drawing only the content
            // mis-sized frames that use a margin and drop shadow inside a transparent window.
            var root = rootVisual;
            if (root is not FrameworkElement fe || fe.ActualWidth < 1 || fe.ActualHeight < 1) continue;

            Point offset;
            try { offset = main.PointFromScreen(fe.PointToScreen(new Point(0, 0))); }
            catch (InvalidOperationException) { continue; }
            // A dialog's own background lives on the Window, not its content: paint it under the content.
            yield return new Surface(fe, offset, new Size(fe.ActualWidth, fe.ActualHeight), label);
        }
    }

    // ── Screenshot ──────────────────────────────────────────────────

    private byte[] Render(Dictionary<string, string> q)
    {
        var scale = q.TryGetValue("scale", out var ss) && double.TryParse(ss, System.Globalization.CultureInfo.InvariantCulture, out var s)
            ? Math.Clamp(s, 0.25, 3)
            : 1.0;
        var surfaces = Surfaces().ToList();
        var canvas = surfaces[0].Size;
        if (canvas.Width < 1 || canvas.Height < 1) throw new InvalidOperationException("Main window has no rendered content yet.");

        var background = Application.Current.TryFindResource("WolfBgBrush") as Brush ?? Brushes.Black;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawRectangle(background, null, new Rect(canvas));
            foreach (var surface in surfaces)
            {
                if (surface.Background is { } surfaceBackground)
                    dc.DrawRectangle(surfaceBackground, null, new Rect(surface.Offset, surface.Size));
                // Pin the brush to the surface's own bounds: effects such as drop shadows widen a
                // visual's drawn bounds and would otherwise shift and clip the image.
                dc.DrawRectangle(
                    new VisualBrush(surface.Root)
                    {
                        Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                        Viewbox = new Rect(surface.Size), ViewboxUnits = BrushMappingMode.Absolute
                    },
                    null,
                    new Rect(surface.Offset, surface.Size));
            }
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(canvas.Width * scale), (int)Math.Ceiling(canvas.Height * scale), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    // ── Element discovery ───────────────────────────────────────────

    private sealed record Found(FrameworkElement Element, Surface Surface, Rect Bounds);

    private IEnumerable<Found> Interactive(bool includeHidden)
    {
        // Popups and dialogs sit on top, so search them first.
        foreach (var surface in Surfaces().Reverse())
        {
            foreach (var element in Descendants(surface.Root).OfType<FrameworkElement>())
            {
                if (!IsInteractive(element) || !element.IsVisible || element.ActualWidth < 1 || element.ActualHeight < 1) continue;
                Rect bounds;
                try { bounds = element.TransformToAncestor(surface.Root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)); }
                catch (InvalidOperationException) { continue; }
                if (!includeHidden && (!bounds.IntersectsWith(new Rect(surface.Size)) || IsTransparent(element))) continue;
                bounds.Offset(surface.Offset.X, surface.Offset.Y);
                yield return new Found(element, surface, bounds);
            }
        }
    }

    private static bool IsInteractive(FrameworkElement element)
        => element switch
        {
            ScrollBar => false,
            ButtonBase b => !HasAncestor<ScrollBar>(b) && !(b is ToggleButton && b.TemplatedParent is ComboBox),
            TextBoxBase or PasswordBox or ComboBox or ListBoxItem or TabItem => true,
            RangeBase r => r is not ProgressBar && !HasAncestor<ScrollBar>(r),
            _ => false
        };

    private static bool HasAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (var p = VisualTreeHelper.GetParent(node); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is T) return true;
        return false;
    }

    private static bool IsTransparent(UIElement element)
    {
        for (DependencyObject? p = element; p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is UIElement u && u.Opacity <= 0.01) return true;
        return false;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            var count = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(node) : 0;
            for (var i = count - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
    }

    private object ListElements(Dictionary<string, string> q)
    {
        q.TryGetValue("q", out var filter);
        var includeHidden = q.TryGetValue("all", out var all) && all is "1" or "true";
        return Interactive(includeHidden)
            .Select(f => new { f, info = Info(f) })
            .Where(x => string.IsNullOrWhiteSpace(filter)
                || Contains(x.info.Label, filter) || Contains(x.f.Element.Name, filter) || Contains(x.info.Tip, filter))
            .Select((x, i) => new
            {
                id = i,
                type = x.f.Element.GetType().Name,
                name = NullIfEmpty(x.f.Element.Name),
                label = NullIfEmpty(x.info.Label),
                tip = NullIfEmpty(x.info.Tip),
                value = x.info.Value,
                @checked = (x.f.Element as ToggleButton)?.IsChecked,
                enabled = x.f.Element.IsEnabled,
                covered = IsCovered(x.f),
                surface = x.f.Surface.Label,
                x = Math.Round(x.f.Bounds.X), y = Math.Round(x.f.Bounds.Y),
                w = Math.Round(x.f.Bounds.Width), h = Math.Round(x.f.Bounds.Height)
            })
            .ToArray();
    }

    private sealed record ElementInfo(string Label, string Tip, string? Value);

    private static ElementInfo Info(Found found)
    {
        var e = found.Element;
        var label = AutomationProperties.GetName(e);
        if (string.IsNullOrWhiteSpace(label))
        {
            label = e switch
            {
                HeaderedContentControl { Header: string h } => h,
                ContentControl { Content: string s } => s,
                TextBox => "",
                _ => string.Join(" ", Descendants(e).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text.Trim()).Where(t => t.Length > 0).Distinct().Take(4))
            };
        }

        var tip = e.ToolTip switch { string s => s, ToolTip { Content: string s } => s, _ => "" };
        string? value = e switch
        {
            TextBox t => t.Text,
            ComboBox c => c.SelectedItem?.ToString() ?? c.Text,
            RangeBase r => r.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
        // Item buttons (swatches, list rows) often have no text: fall back to their data item.
        if (string.IsNullOrWhiteSpace(label) && e.DataContext is { } data && data is not ViewModels.MainViewModel)
            label = data.ToString() ?? "";
        return new ElementInfo(label.Trim(), tip.Trim(), value);
    }

    private bool IsCovered(Found found)
    {
        var center = new Point(found.Bounds.X + found.Bounds.Width / 2 - found.Surface.Offset.X,
                               found.Bounds.Y + found.Bounds.Height / 2 - found.Surface.Offset.Y);
        if (found.Surface.Root is not UIElement root) return false;
        var hit = root.InputHitTest(center) as DependencyObject;
        if (hit is null) return true;
        for (var p = hit; p is not null; p = VisualTreeHelper.GetParent(p))
            if (ReferenceEquals(p, found.Element)) return false;
        return true;
    }

    private Found Resolve(Dictionary<string, string> q, Func<FrameworkElement, bool>? kind = null, bool includeOffscreen = false)
    {
        q.TryGetValue("name", out var name);
        q.TryGetValue("text", out var text);
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Pass name= (x:Name) or text= (visible label, tooltip or value).");

        var candidates = Interactive(includeOffscreen).Where(f => kind?.Invoke(f.Element) ?? true).ToList();
        List<Found> matches;
        if (!string.IsNullOrWhiteSpace(name))
        {
            matches = candidates.Where(f => string.Equals(f.Element.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            var infos = candidates.Select(f => (f, info: Info(f))).ToList();
            matches = infos.Where(x => string.Equals(x.info.Label, text, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(x.info.Tip, text, StringComparison.OrdinalIgnoreCase)).Select(x => x.f).ToList();
            if (matches.Count == 0)
                matches = infos.Where(x => Contains(x.info.Label, text!) || Contains(x.info.Tip, text!)).Select(x => x.f).ToList();
        }

        // Prefer elements the user could actually click right now.
        matches = matches.OrderBy(f => IsCovered(f) ? 1 : 0).ToList();
        var nth = q.TryGetValue("nth", out var ns) && int.TryParse(ns, out var n) ? n : 0;
        if (matches.Count == 0) throw new ArgumentException($"No visible element matched {(name is null ? $"text '{text}'" : $"name '{name}'")}. Try /elements?q=...");
        if (nth < 0 || nth >= matches.Count) throw new ArgumentException($"nth={nth} is out of range; {matches.Count} element(s) matched.");
        return matches[nth];
    }

    // ── Actions ────────────────────────────────────────────────────

    private object Act(string path, Dictionary<string, string> q)
    {
        switch (path)
        {
            case "/click":
                if (q.ContainsKey("x") || q.ContainsKey("y")) return ClickPoint(q);
                var target = Resolve(q);
                Click(target.Element);
                return Result("clicked", target);

            case "/type":
            {
                var found = Resolve(q, e => e is TextBox);
                var box = (TextBox)found.Element;
                if (!box.IsEnabled || box.IsReadOnly) throw new ArgumentException("That text box is disabled or read-only.");
                box.Focus();
                box.Text = q.TryGetValue("value", out var value) ? value : "";
                box.CaretIndex = box.Text.Length;
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (q.TryGetValue("enter", out var enter) && enter is "1" or "true") SendKey(box, Key.Enter);
                return Result("typed", found);
            }

            case "/set":
            {
                var found = Resolve(q, e => e is RangeBase or ComboBox or ToggleButton);
                if (!found.Element.IsEnabled) throw new ArgumentException("That control is disabled.");
                q.TryGetValue("value", out var value);
                switch (found.Element)
                {
                    case RangeBase range:
                        if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
                            throw new ArgumentException("value= must be a number for a slider.");
                        range.Value = Math.Clamp(number, range.Minimum, range.Maximum);
                        break;
                    case ComboBox combo:
                        var item = combo.Items.Cast<object>().FirstOrDefault(i => string.Equals(ItemText(combo, i), value, StringComparison.OrdinalIgnoreCase))
                            ?? combo.Items.Cast<object>().FirstOrDefault(i => Contains(ItemText(combo, i), value ?? ""))
                            ?? throw new ArgumentException($"No item matched '{value}'. Items: {string.Join(", ", combo.Items.Cast<object>().Select(i => ItemText(combo, i)))}");
                        combo.SelectedItem = item;
                        break;
                    case ToggleButton toggle:
                        var wanted = value is "1" or "true" or "on";
                        if ((toggle.IsChecked == true) != wanted) Click(toggle);
                        break;
                }
                return Result("set", found);
            }

            case "/scroll":
            {
                // Scrolling exists to reach controls that are currently off screen.
                var found = Resolve(q, includeOffscreen: true);
                found.Element.BringIntoView();
                return Result("scrolled", found);
            }

            case "/key":
            {
                if (!q.TryGetValue("key", out var keyName) || !Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
                    throw new ArgumentException("key= must be a WPF Key name such as Escape, Enter, Tab, Delete, Up or Z.");
                // Escape over an open dropdown/menu closes it, as a real outside click or Esc would,
                // without also reaching the window's own Escape handler.
                if (key == Key.Escape)
                {
                    var open = OpenPopups().ToList();
                    if (open.Count > 0)
                    {
                        foreach (var popup in open) popup.IsOpen = false;
                        return new { ok = true, action = "key", key = "Escape", target = $"closed {open.Count} popup(s)" };
                    }
                }

                var focused = Keyboard.FocusedElement as UIElement ?? (UIElement?)_window.Content ?? _window;
                SendKey(focused, key);
                return new { ok = true, action = "key", key = key.ToString(), target = Describe(focused) };
            }
        }

        throw new ArgumentException($"Unknown action {path}.");
    }

    private object ClickPoint(Dictionary<string, string> q)
    {
        if (!double.TryParse(q.GetValueOrDefault("x"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(q.GetValueOrDefault("y"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
            throw new ArgumentException("x= and y= must both be numbers (DIPs from the main window's top-left).");

        foreach (var surface in Surfaces().Reverse())
        {
            if (surface.Root is not UIElement root) continue;
            var local = new Point(x - surface.Offset.X, y - surface.Offset.Y);
            if (local.X < 0 || local.Y < 0 || local.X > surface.Size.Width || local.Y > surface.Size.Height) continue;
            if (root.InputHitTest(local) is not DependencyObject hit) continue;

            // Use the nearest clickable ancestor so buttons behave exactly like a user click.
            for (var p = hit; p is not null; p = VisualTreeHelper.GetParent(p))
            {
                if (p is FrameworkElement fe && IsInteractive(fe))
                {
                    Click(fe);
                    return new { ok = true, action = "clicked", target = Describe(fe), surface = surface.Label };
                }
            }

            // No button there: raise a plain left click on whatever was hit (e.g. a scrim or graph).
            if (hit is UIElement element) RaiseMouseClick(element);
            return new { ok = true, action = "clicked", target = Describe(hit), surface = surface.Label };
        }

        throw new ArgumentException($"Nothing at ({x}, {y}).");
    }

    private void Click(FrameworkElement element)
    {
        if (!element.IsEnabled) throw new ArgumentException($"{Describe(element)} is disabled.");
        // Defer so a click that opens a modal dialog doesn't block this request; later requests
        // still run inside the dialog's message loop and can see and click it.
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            switch (element)
            {
                case ButtonBase button when OnClickMethod is not null:
                    button.Focus();
                    OnClickMethod.Invoke(button, null);
                    break;
                case ComboBoxItem comboItem:
                    comboItem.IsSelected = true;
                    if (ItemsControl.ItemsControlFromItemContainer(comboItem) is ComboBox owner) owner.IsDropDownOpen = false;
                    break;
                case ListBoxItem listItem:
                    listItem.IsSelected = true;
                    listItem.Focus();
                    break;
                case TabItem tab:
                    tab.IsSelected = true;
                    break;
                case ComboBox combo:
                    combo.IsDropDownOpen = !combo.IsDropDownOpen;
                    break;
                case UIElement other:
                    other.Focus();
                    RaiseMouseClick(other);
                    break;
            }
        });
    }

    /// <summary>Open Popups (dropdowns, menus): each popup's root visual's logical parent is the Popup.</summary>
    private static IEnumerable<System.Windows.Controls.Primitives.Popup> OpenPopups()
        => PresentationSource.CurrentSources.OfType<HwndSource>()
            .Where(source => !source.IsDisposed && IsWindowVisible(source.Handle))
            .Select(source => (source.RootVisual as FrameworkElement)?.Parent as System.Windows.Controls.Primitives.Popup)
            .Where(popup => popup is { IsOpen: true })
            .Cast<System.Windows.Controls.Primitives.Popup>();

    private static void RaiseMouseClick(UIElement element)
    {
        foreach (var routed in new[] { UIElement.PreviewMouseLeftButtonDownEvent, UIElement.MouseLeftButtonDownEvent,
                                       UIElement.PreviewMouseLeftButtonUpEvent, UIElement.MouseLeftButtonUpEvent })
        {
            element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routed });
        }
    }

    private static void SendKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? throw new InvalidOperationException("The target is not on screen.");
        foreach (var routed in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.KeyDownEvent, Keyboard.PreviewKeyUpEvent, Keyboard.KeyUpEvent })
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = routed });
    }

    private static string ItemText(ItemsControl owner, object item)
    {
        if (item is ComboBoxItem { Content: { } content }) return content.ToString() ?? "";
        if (!string.IsNullOrEmpty(owner.DisplayMemberPath))
        {
            var probe = new TextBlock { DataContext = item };
            probe.SetBinding(TextBlock.TextProperty, new Binding(owner.DisplayMemberPath));
            return probe.Text;
        }
        return item.ToString() ?? "";
    }

    private static object Result(string action, Found found)
        => new { ok = true, action, target = Describe(found.Element), surface = found.Surface.Label };

    private static string? Describe(DependencyObject? node)
    {
        if (node is null) return null;
        var type = node.GetType().Name;
        if (node is FrameworkElement fe)
        {
            var label = fe is HeaderedContentControl { Header: string h } ? h
                : fe is ContentControl { Content: string s } ? s
                : string.Join(" ", Descendants(fe).OfType<TextBlock>().Select(t => t.Text.Trim()).Where(t => t.Length > 0).Take(3));
            return $"{type}{(string.IsNullOrEmpty(fe.Name) ? "" : $" #{fe.Name}")}{(string.IsNullOrWhiteSpace(label) ? "" : $" \"{label}\"")}";
        }
        return type;
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // ── HTTP helpers ───────────────────────────────────────────────

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var qi = target.IndexOf('?');
        if (qi < 0) return dict;
        foreach (var pair in target[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            dict[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : "";
        }
        return dict;
    }

    private static Task WriteJsonAsync(Stream s, object value)
        => WriteAsync(s, 200, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json)));

    private static Task WriteAsync(Stream s, int status, string type, string body)
        => WriteAsync(s, status, type, Encoding.UTF8.GetBytes(body));

    private static async Task WriteAsync(Stream s, int status, string type, byte[] body)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Error" };
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(header));
        await s.WriteAsync(body);
        await s.FlushAsync();
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
