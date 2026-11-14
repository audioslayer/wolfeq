---
name: wolfeq-dev
description: See and drive the running WolfEQ app through its dev mode server - screenshots, list buttons, click, type, set sliders/combos, press keys. Use when reviewing or changing any WolfEQ UI/XAML/view code, checking a layout, reproducing a UI bug, comparing before/after, or when the user says "look at the app", "screenshot", or "click around".
---

# WolfEQ dev mode

WolfEQ has a dev-only HTTP server (`Services/DevModeServer.cs`) for seeing and clicking the real UI.
It runs **only** when WolfEQ was started by `deploy.bat`, which passes `--dev`. Installed builds,
Stream Deck commands and Start-with-Windows launches never open the port.

- Address: `http://127.0.0.1:47824` (loopback only)
- **Every request needs the header `X-WolfEQ-Dev: 1`.** Without it the server answers 403. This stops web pages in a browser from driving the app.

```bash
D="http://127.0.0.1:47824"; H="X-WolfEQ-Dev: 1"
curl -s -H "$H" $D/health                      # → ok
```

No answer means WolfEQ wasn't launched via `deploy.bat`, or it's an older build. Ask the user to run `deploy.bat`.

## Don't build or launch it yourself

The user runs `deploy.bat` (pull → kill old → build → launch with `--dev`). Your loop is:

1. Make the code change.
2. Ask the user to run `deploy.bat`.
3. Screenshot, click around, review.

Don't run `deploy.bat`, kill WolfEQ or start the exe. The user owns that step.
For a compile check use `dotnet build WolfEQ.csproj -o <scratchpad>\build`, which doesn't touch the running app.

## Endpoints (all GET)

| Endpoint | What it does |
|-|-|
| `/status` | JSON: status-bar text, version, window state, open popups/dialogs, focused element. Check it after a click to see what happened. |
| `/screenshot` | PNG of the main window with open dropdowns and dialogs drawn on top. `scale=` (default 1, so 1 px = 1 DIP), `delay=` ms to settle (default 300). |
| `/elements` | JSON list of visible interactive elements: `type`, `name` (x:Name), `label` (visible text), `tip`, `value`, `checked`, `enabled`, `covered` (something is on top of it), `surface`, and bounds `x,y,w,h` in DIPs. `q=` filters by text/name/tip; `all=1` includes off-screen/transparent ones. |
| `/click` | `name=` (x:Name) or `text=` (label or tooltip; exact match first, then contains). `nth=` picks among several matches. Or `x=&y=` in DIPs from a scale=1 screenshot. |
| `/type` | Sets a TextBox: `name=` or `text=` plus `value=`; `enter=1` presses Enter afterwards. |
| `/set` | `value=` for a Slider (number), ComboBox (item text) or ToggleButton/CheckBox (`true`/`false`). |
| `/scroll` | `name=` or `text=`: brings that element into view. |
| `/key` | `key=Escape` (any WPF `Key` name) sent to the focused element. |

Actions return JSON like `{"ok":true,"action":"clicked","target":"ToggleButton #SettingsHeaderButton","surface":"main"}`.
Errors are HTTP 400 with a plain-text reason, such as no match or a disabled control. Run curl without `-o` to read it.

## Typical session

```bash
S="<scratchpad>"
curl -s -H "$H" "$D/screenshot" -o "$S/home.png"                       # then Read the PNG
curl -s -H "$H" "$D/click?name=SettingsHeaderButton"                   # open Settings
curl -s -H "$H" "$D/elements?q=bluetooth"                              # find controls
curl -s -H "$H" "$D/click?text=Scan%20Bluetooth"
curl -s -H "$H" "$D/screenshot?delay=600" -o "$S/settings.png"
curl -s -H "$H" "$D/key?key=Escape"                                    # close it
```

Useful names: `SettingsHeaderButton`, `LibraryHeaderButton`, `UndoHeaderButton`, `RedoHeaderButton`.
Settings and Library are slide-over panels inside the main window, so they appear in normal screenshots.
URL-encode spaces in `text=` (`%20`).

## Be careful: this is the user's real app and real hardware

- Clicks do exactly what a user click does. Many controls **write to the connected FiiO device** (USER slot switches, EQ band edits with live sync, saves, lighting, input source) or change Windows/Equalizer APO settings. Only click those when the user asked for it. Otherwise stick to navigation, opening panels and menus, and reading.
- Don't confirm destructive dialogs (delete preset, discard changes, quit) unless asked. Close them with `/key?key=Escape` or click their cancel button.
- A screenshot or click brings WolfEQ out of the tray. Mention it if the user is using the app at the same time.
- Clicks are queued on the UI thread, so a click that opens a modal dialog returns straight away. Follow it with `/screenshot` or `/elements`, which can see and click the dialog.
- Buttons are clicked through WPF itself (Click event and Command), not by moving the real mouse. `x=&y=` clicks on non-button areas raise mouse events but `e.GetPosition` sees the real cursor, so prefer `name=`/`text=`.
