# Changelog

## v1.0.0

WolfEQ reaches 1.0. Every band has its own color, your cursor becomes part of the knob you turn, and 19 devices are supported, including the first from outside FiiO.

### ✨ New

- 🌈 **A color for every band** — Graph points, knobs, faders and the band editor share one color per band, warm lows to cool highs.
- 🎚️ **Knobs** — The new default Band controls view. Your cursor flies into the knob and the light follows it around the dial. Type exact values any time.
- 🪽 **Width wings** — The selected band's wings follow the curve. Drag a tip, or scroll even while dragging a point, to make it wider or narrower.
- 🔢 **Live readout** — Frequency, gain and Q appear beside a point as you drag it.
- 🎧 **More devices** — TRN Black Pearl, CrinEar Protocol Max, Tanchjim SPACE II, JadeAudio JIEZI and Snowsky Tiny A. On the first three, every save is read back and checked.
- 🖼️ **Real pictures** — Every device has its own picture.
- 🎛️ **Tidier settings** — Settings show only what your device supports. Volume limit, balance and high gain are gone: the K13 did not respond to them from a PC.

### 🐞 Fixed

- 🔊 **KA15, JA11 and Melody level** — Saved EQs played 12 dB too quiet. The preamp now matches what you set, from -24 to 0 dB.
- 🛡️ **Safer saving on BTR17, K17 and K19** — These now save with FiiO's standard command. The old one is a factory reset on other FiiO models, so please update before saving to these.
- 📶 **Bluetooth stays saved** — If Windows gives your device a new USB path, picking it again keeps its Bluetooth, slot names and lights.
- ⏱️ **KA15 and JA11** — Loading from a slot no longer waits on a command these devices never answer.
- 📏 **Retro Nano** — Corrected width limits and filter list.

### ⚠️ Good to know

- **KA15, JA11 and Melody owners:** your next save will sound about 12 dB louder than before. **Turn the volume down first.**
- WolfEQ is developed on the K13 R2R. The other devices build on published work by Contour, DevicePEQ and fiioctl. Using one? Start with a low-gain EQ and tell us how it goes, especially on the BTR17, K17 and K19: check that your EQ is still there after a power cycle.
- The TRN Black Pearl only appears in Windows with headphones plugged in.
- **Coming from 0.9?** If WolfEQ says your saved device is unavailable, pick it again from the device menu.

## v0.9.0-beta

Settings rebuilt around **your device**, a real lights editor, one-click Bluetooth, a Stream Deck guide, four more FiiO devices, and a faster, calmer app everywhere.

### ✨ Highlights

- 🎛️ **Settings at a glance** — Opens on a live drawing of your K13 in its real light colours, with **USB connected** and **Bluetooth ready** chips. Everything else lives in colour-coded sections: Your device, Sound, Lights, Windows output, Shortcuts, Look and About.
- ❔ **Help where you need it** — Hover any **?** for a short, plain explanation. The walls of fine print are gone.
- 🌈 **A real lights editor** — Preview the top bar and knob, edit **Both**, **Top** or **Knob**, and pick any built-in effect: Solid, Breathing, Rainbow, Rainbow breathing or Follow audio. Two-tone looks (Neon Split, Sunset, Ice, Twilight) set both at once.
- 📶 **Bluetooth in one click** — **Find my K13** flashes your unit green and saves it when you say yes. No Windows pairing, no separate save step.
- ⌨️ **Stream Deck made easy** — Pick a slot from glowing Stream Deck-style keys, then follow a four-step guide that saves the shortcut and shows exactly where it goes.
- 🎧 **More FiiO devices** — Added the **K17**, **K19**, **FP3** and **Oak Nano**. The **QX13** and **BTR17** are now recognised by their USB IDs. Every listed device is now labelled working.

### 🛠️ Also new

- **Clipping risk** sits right next to its **Avoid clipping** fix in the preamp strip.
- **Sound** has its own section: input, volume, volume limit, balance and high gain, each with a **?** tip.
- Matching headers, buttons and spacing across the editor, Library, Settings and dialogs. Small text is bigger and easier to read.
- Library filter chips, empty states and the **Fits 10 bands** tip are clearer. "1 saved presets" is now grammatical.
- The **Library** and **Settings** buttons have green icons and a soft highlight for the open panel.

### 🐞 Fixed

- ⚡ **Fast on every PC** — WolfEQ no longer opens every USB device on your computer before each action. On some PCs that added over a minute to every detection, slot switch and readback. ([#5](https://github.com/audioslayer/wolfeq/issues/5))
- 💡 **Lights after a restart** — If Windows hasn't seen your K13 over Bluetooth yet, WolfEQ listens for it for a few seconds and retries, so slot lighting works without pressing Scan. Thanks to **derailius** for the patch. ([#4](https://github.com/audioslayer/wolfeq/issues/4))
- 🔊 **Volume** — The Sound volume slider now finds your K13's Windows output. It used to fail with an `IMMDeviceCollection` or "RETRO NANO" error.
- 💾 **Saving Bluetooth** — Saving your Bluetooth device is no longer blocked by unsaved EQ edits.
- 🎨 **Lights stay honest** — If the K13 doesn't accept a light change, the editor goes back to what's really on the device.

### ⚠️ Good to know

- The K13 R2R is the device tested on hardware by WolfEQ. Devices marked **save-only** in the README write EQ to USER slots but keep live sync and automatic readback off until their USB behaviour is confirmed. Using one? Please tell us how it goes.
- If a K17 or K19 turns out to use USB serial instead of HID, it won't be detected yet.
- Sound › Volume sets the Windows playback volume for your device.

### 🚀 Downloads

| Asset | Use it for |
|

## v0.8.0-beta

Headphone EQ on **any** Windows output, plus the biggest look-and-feel refresh WolfEQ has had.

### ✨ Highlights

- 💻 **Software EQ** — Run your headphone EQ on any Windows output: laptop jacks, USB dongles, any brand of DAC, even Bluetooth. Pick your outputs, edit live, bypass anytime. Powered by Equalizer APO.
- 📦 **One-click setup** — Equalizer APO ships inside WolfEQ. If it's missing, WolfEQ downloads the official installer, verifies it, and shows progress the whole way.
- 📚 **A brand-new library** — Your list and a big curve preview side by side. Favorites tab, ⭐ stars, ✎ rename, a mini curve on every preset, and one-click saving from AutoEQ and OPRA results.
- 🔀 **Quicker switching** — A device menu right in the toolbar, a clearer USER slot menu with your slot names, and color-coded connection status.
- 🎛️ **Sharper controls** — Frequency, Gain, and Q get − / + steppers with unit tabs. Arrow keys and the scroll wheel work too (Shift for bigger steps). Filter types read as plain names.
- 📌 **A new tray** — Now playing, an EQ on/off switch, and quick presets with your favorites first.
- 💬 **Dialogs that fit** — Delete, rename, quit, and update prompts now match WolfEQ instead of plain Windows pop-ups.

### 🛠️ Also new

- Plays nicely with **Peace**: WolfEQ keeps to its own file and warns you about double EQ.
- **Stream Deck & scripts:** `--system-eq-bypass on|off|toggle` and `--preset "<name>"`.
- Collapsed **Settings** sections show a one-line summary of where things stand.
- Import and Export moved into tidy menus at the top of the library.
- Uninstalling WolfEQ removes its EQ from Equalizer APO.

### 🐞 Fixed

- The online preview no longer shows blank until you click Table and back.
- Favorites and renames save instantly.
- USER slot names are easy to read, even with the device unplugged.

### ⚠️ Good to know

- Apps using exclusive mode or ASIO bypass Software EQ.
- Windows and driver updates can remove Equalizer APO from an output. Use **Add outputs** to put it back.
- FiiO and Snowsky hardware support is unchanged; the K13 R2R remains the verified device.

### 🚀 Downloads

| Asset | Use it for |
|---|---|
| **WolfEQ-Setup-0.8.0-beta.exe** | Install on Windows 10/11 x64. |
| **WolfEQ-0.8.0-beta-win-x64.zip** | Portable: extract everything and run WolfEQ.exe. |
| **SHA256SUMS.txt** | Verify your download. |

Both include the .NET runtime and the official Equalizer APO 1.4.2 installer. Equalizer APO is a separate GPL-2.0 program by Jonas Thedering, bundled unmodified; see THIRD_PARTY_NOTICES.md. Your presets and settings carry over.

---

💚 Tell us how it sounds in the [WolfEQ Discord channel](https://discord.com/channels/1467241109657751737/1503967391581995069) or [report an issue](https://github.com/audioslayer/wolfeq/issues).


## v0.7.0-beta

A redesigned interface, colorful live spectrum bars, more ways to edit your bands, and less friction when saving or switching presets. This update brings together the details you've been asking for.

### 🎚️ More room for your EQ

- A wider graph with a dedicated selected-band inspector beneath it.
- **Faders and Table** views with frequency, gain, Q, filter shape, and bypass controls.
- Band controls start collapsed. Expand them and **drag the divider to resize live**, giving either the graph or controls more space.
- A compact USER-slot picker, device product images, familiar window controls, and a larger transparent green waveform icon.
- Clearer selected-band highlights, soft hover/focus glows, and fewer redundant borders and instructions.

### 🎶 See your music move

- **Colorful spectrum bars** behind the EQ curve, with luminous tips and smooth decay.
- Spectrum defaults on and remembers your toggle choice.
- Monitors the default Windows multimedia output and follows output-device changes.
- Stops capture and animation when disabled, hidden, minimized, inactive, or scrolled out of view.
- Separate spectrum/EQ drawing layers, cached bar brushes, precomputed FFT data, and background device checks reduce repeated work and UI interruptions.

*The spectrum is a playback visualization, not an additional EQ or a measurement of the DAC's analog output. Its relative level scale is separate from the EQ gain axis.*

### 🎨 Light, dark, and a little color

- Saved **Light / Dark** background mode in Settings → Appearance.
- Green is the default accent, alongside solid colors and **Aurora, Sunset, Neon, and Ocean** multicolor palettes.
- Icons, controls, menus, graph accents, and previews follow the theme. Light mode adjusts accent contrast for readability.

### 📚 A library that flows

- Slim search with **+ New**, and duplicate/delete/write actions grouped beside the selected preset.
- A small naming popup before **Save to Library**.
- Browsing presets no longer replaces your current editor content.
- **Open in editor** closes the sidebar after a successful load, including Discover Online.
- **Curve / Table** tabs in online previews.
- Locally saved profile names beneath USER slots in the picker, toolbar, and tray quick profiles.
- A useful empty-search state, with actions hidden when no presets match.

### ⌨️ Stream Deck, without the guesswork

- A **Windows ScriptDeck setup** flyout under Settings → Integrations.
- Direct Marketplace link, USER-slot selector, and one-click PowerShell copying.
- Scripts include your app location and saved USB device, with spaces and special characters quoted correctly.
- Instructions for **On Press** and optional saved lighting cues. Shortcut export remains available.

### 🛠️ Finishing touches

- **Edit / Compare / Device / Share** tools tabs keep a consistent panel height and use clearer icons and labels.
- Live sync has a compact question-mark explanation; reconnect uses a refresh icon.
- Updated app, taskbar, installer, and tray artwork.
- Direct **Discord app-channel** access from Settings → About.

### 🚀 Downloads

| Asset | Use it for |
|---|---|
| **WolfEQ-Setup-0.7.0-beta.exe** | Install on Windows 10/11 x64. |
| **WolfEQ-0.7.0-beta-win-x64.zip** | Portable use: extract everything and launch WolfEQ.exe. |
| **SHA256SUMS.txt** | Verify the downloaded installer and ZIP. |

Both builds include the .NET runtime. Existing presets and settings remain in your WolfEQ user-data folder. A saved Spectrum-off preference stays off. Slot names are stored locally on your PC, not in device firmware. Re-copy Stream Deck scripts if the executable location or saved USB connection changes.

### ✅ Validation and beta scope

**94 automated tests pass**, including EQ behavior, preset guards, PowerShell argument handling, spectrum analysis, and rendered UI checks. Live Windows playback capture and repeated capture start/stop were checked during development.

K13 R2R remains the primary verified device. Other listed FiiO/Snowsky profiles remain experimental; this release does not expand hardware-verification claims. Physical DAC switching, Stream Deck hardware, and every monitor/DPI combination have not all been re-tested for this release.

---

💚 **Thanks for the feedback that shaped this update.** Share your setup in the [WolfEQ Discord channel](https://discord.com/channels/1467241109657751737/1503967391581995069) or [report an issue](https://github.com/audioslayer/wolfeq/issues).


## v0.6.0-beta

- Added a Windows system tray companion with a modern quick-switch panel for saved device USER slots.
- Added configurable close-to-tray and minimize-to-tray behavior, with editor restore and explicit exit actions.
- Kept tray slot switching on the guarded device-switch path, including unsaved-edit protection and optional per-slot lighting.

## v0.5.0-beta

- Added explicit USB and Bluetooth device selection, a reversible identify-light action, and a saved connection in Settings > Devices.
- Removed automatic Bluetooth target selection and fallback; legacy cached addresses require explicit selection.
- Scoped lighting colors to the USB device and added optional lighting application after confirmed USER-slot switches.
- Added --slot, --device, --list-devices, and --help automation with single-instance command forwarding and unsaved-edit protection.
- Added Settings > Integrations with a USER-slot command generator, clipboard copy, Windows shortcut export, and Stream Deck setup instructions.
- Added automation results and exit codes, including a distinct result when EQ succeeds but lighting fails.
- Redesigned Settings with a unified Device section, Connections / Controls tabs, compact themed action buttons, and section icons.
- Moved automatic slot lighting into Profile Lights and tucked model compatibility and Stream Deck setup details into expandable sections.

## v0.4.0-beta

- Replaced the persistent band-chip grid with a graph-native EQ editing workflow.
- Added numbered EQ nodes, distinct selected/enabled/bypassed states, and draggable Q handles.
- Anchored enabled band nodes to the rendered combined-response curve and made vertical dragging solve for the underlying band gain, eliminating detached handles where filters overlap.
- Numbered graph bands by left-to-right frequency order while preserving their original hardware filter IDs for safe Live Sync writes.
- Added a contextual precision editor that follows the selected graph point.
- Reduced the lower workspace to a compact preamp, headroom, safety, and tools footer.
- Added grouped EQ undo/redo with Ctrl+Z and Ctrl+Y shortcuts.
- Made all ten K13 R2R USER slots discoverable with explicit slot navigation and active-slot tracking.
- Made confirmed USER-slot switches read the newly active PEQ into the editor so the graph follows the device, with an unsaved-edit confirmation before replacement.
- Added K13 PEQ compatibility guidance for the 192 kHz/24-bit and MQA limitations documented by FiiO.
- Hardened online profile and update downloads with trusted-host checks and strict response-size limits.
- Added bounded preset/library imports, DTD-safe FiiO XML parsing, atomic local saves, and rotating sanitized logs.
- Added finite-number and bounded-drain guards to FiiO device writes and HID reads.
- Reduced wide-graph rendering work and cached the combined AutoEQ/OPRA search index.
- Made mapped-drive and UNC deployments use a clean machine-local WPF intermediate cache.
- Moved Undo/Redo into the title bar, softened the WolfEQ wordmark, and removed duplicate history actions from Tools.
- Fixed the title-bar Undo/Redo controls with explicit click routing and observable availability state shared with their keyboard shortcuts.
- Replaced the boxed PEQ badge with a quiet inline capability label and unified headroom optimization with its live safety status.
- Replaced mode-specific Windows Audio guidance with compact advanced quick controls for supported default-output formats and spatial sound settings.
- Replaced the unexplained readiness score with an explicit headroom margin and upgraded Auto Headroom into an Optimize Headroom action that shows the strongest boost and matching preamp before applying it.
- Fixed Live Sync scheduling so manual and optimized preamp changes, plus PEQ band edits, are debounced and written with device readback instead of only showing an unsaved status.
- Added guarded DevicePEQ-derived profiles for FiiO BR15 R2R, QX13, BTR17, and BTR13. The profiles include model-specific slot/filter limits and BTR17's `0x21` save command, start without automatic readback or live writes, and require a matching USB product name when no verified PID is available.

## v0.3.1-beta

- Kept Snowsky Melody connected and writable by skipping unsupported automatic and manual EQ readback.
- Added an explicit per-device EQ-readback capability while preserving normal readback behavior for other profiles.
- Added regression coverage for Melody USER-slot writes, readback capability gating, and unsaved-edit protection.

## v0.2.0-beta

- Added experimental device profiles for FiiO KA15, FiiO KA17, FiiO JA11, Snowsky Melody, and Snowsky Retro Nano.
- Redesigned the EQ workspace with a wider graph, compact band rows, cleaner library flow, and a right-side preset/profile panel.
- Added auto device detection, selectable device profiles, cleaner slot naming, and refreshed readback behavior.
- Improved K13 slot switching, boot readback, save confirmation, and post-save reload flow.
- Added graph hover readouts, PEQ-style response behavior, local preset import/export tools, online profile search, and clipping/headroom guidance.

## v0.1.0-beta

- First public beta preparation for WolfEQ.
- Added AmpUp-style Settings About panel with GitHub update check and Buy Me a Coffee link.
- Added Inno Setup installer workflow and local `build-installer.bat`.
- Added public README, license, release notes, and disabled GitHub Actions installer template.
- Polished the EQ workspace, slot switcher, profile library, online AutoEq preview, and profile lighting controls.
- Enabled verified K13 USB EQ readback, slot switching, global preamp writes, and band writes behind guarded device paths.
