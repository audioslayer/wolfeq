# Changelog

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
