<p align="center">
  <img src="Assets/icon/wolfeq.png" width="120" alt="WolfEQ logo" />
</p>

<h1 align="center">WolfEQ</h1>

<p align="center">
  <strong>WolfEQ 0.6.0 beta is the current release.</strong><br/>
  A modern Windows PEQ workspace for FiiO K13 R2R and guarded experimental FiiO / Snowsky device support.
</p>

<p align="center">
  <a href="https://github.com/audioslayer/wolfeq/releases/latest"><img src="https://img.shields.io/github/v/release/audioslayer/wolfeq?include_prereleases&label=beta" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?logo=windows&logoColor=white" alt="Windows 10 / 11" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/FiiO-K13%20R2R-C8102E" alt="FiiO K13 R2R" />
  <img src="https://img.shields.io/badge/devices-experimental-orange" alt="Experimental device profiles" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License" />
</p>

## System tray (0.6 beta)

WolfEQ now includes a Windows system tray companion for switching the USER profiles already saved on your connected DAC. Click or right-click its tray icon to see device status and choose a USER slot. Double-click the icon or choose **Open editor** to return to the full editor. If Windows hides the icon, find it under the taskbar's hidden-icons arrow.

The tray panel has saved **Close to tray** and **Minimize to tray** preferences. Close to tray is enabled by default, so the window's close button keeps WolfEQ running in the tray. Minimize to tray is initially disabled, so minimizing keeps the normal taskbar button until you enable it. Choose **Quit** in the tray panel to exit WolfEQ completely.

Tray switches use the same device safeguards and optional profile lighting as the editor. Save or discard unsaved edits before switching. Switching a USER slot selects EQ already stored on the DAC; use the editor to write a library preset to a slot.

Download the installer or portable build from the latest release below.

## Stream Deck and device selection (0.5 beta)

1. Open **Settings > Device > Connections**, refresh USB devices, and select your DAC.
2. Scan for nearby K13 Bluetooth devices. Select one and use **Identify** to briefly turn its lights green and restore them. Confirm it is the same unit connected over USB, then **Save device connection**.
3. Under **Settings > Profile Lights**, enable **Apply saved lighting when switching USER slots** if desired and configure each slot's colors. Previous global colors remain in `slot-lighting.json`; the new version stores colors separately for each USB device.
4. Open **Settings > Integrations**, choose a USER slot, and click **Save shortcut**.
5. In Stream Deck, add **System > Open** to a key and choose the saved `.lnk` in **App / File**. Name the key for your headphones and repeat for other slots. Use a current Stream Deck version with support for shortcut arguments ([Elgato release notes](https://edge.elgato.com/egc/sd_content/releasenotes/7.0.0.html)).

The Integrations section also provides a copyable command containing your executable path and selected USB device. Shortcuts switch EQ already stored on the DAC; they do not write library presets. Recreate them if you move the executable or change the saved device connection. A USB port change can change the device path and require selecting the device again.

```text
WolfEQ.exe --slot 2
WolfEQ.exe --device "Desk K13" --slot 3
WolfEQ.exe --list-devices
WolfEQ.exe --help
```

`--device` accepts the saved connection name or exact USB path. It must match the connection configured in Settings; it cannot silently reassign the Bluetooth device. Commands work with the app open or closed. Existing instances receive the request without stealing focus. Cold commands run without displaying the editor and exit when finished. Save or discard unsaved edits before switching with automation.

Exit codes: **0** success, **1** failure/disconnected device, **2** invalid arguments or unsupported slot, **3** busy/unsaved edits, **4** slot switched but lighting failed. The latest result is also written to `%LOCALAPPDATA%\WolfEQ\automation-result.json`. In PowerShell, use `Start-Process -Wait -PassThru` when you need to wait for this Windows GUI executable and inspect its exit code.

EQ uses USB; lights and supported device controls use Bluetooth. Saved connections never fall back to another unit. Previous automatically cached Bluetooth addresses are ignored until you explicitly select and save a target. If Bluetooth is unavailable, the confirmed EQ slot switch is kept and the lighting failure is reported separately.

---

## 🐺 Overview

WolfEQ is a desktop app for people who want a cleaner way to tune PEQ on supported FiiO and Snowsky devices. Instead of juggling device menus, preset files, and separate graph tools, WolfEQ brings the complete workflow into one Windows app: discover or import a profile, load it into the editor, shape and compare the curve, then write it to a compatible USER slot.

The **FiiO K13 R2R** is the main WolfEQ-verified device. WolfEQ 0.6.0 beta also includes experimental profiles for BR15 R2R, QX13, BTR17, BTR13, KA15, KA17, JA11, Snowsky Melody, and Snowsky Retro Nano. Newly added community profiles start in guarded save-only mode without automatic readback or live writes until their Windows HID endpoints are verified on physical hardware.

---

## 🚀 Latest Beta

Download the latest published beta, WolfEQ 0.6.0, from the [latest GitHub release](https://github.com/audioslayer/wolfeq/releases/latest):

- [Windows x64 installer](https://github.com/audioslayer/wolfeq/releases/download/v0.6.0-beta/WolfEQ-Setup-0.6.0-beta.exe)
- [Portable Windows x64 ZIP](https://github.com/audioslayer/wolfeq/releases/download/v0.6.0-beta/WolfEQ-0.6.0-beta-win-x64.zip)

WolfEQ 0.6.0 beta adds:

- A modern system tray panel with quick USER-profile switching, confirmed active-slot indicators, and device status.
- Saved close-to-tray and minimize-to-tray preferences, quick editor/settings access, and guarded Quit.
- Unsaved-edit protection and optional saved lighting when switching profiles from the tray.

Features carried forward from 0.5.0 beta:

- Explicit USB and Bluetooth device selection, an Identify action, and no automatic fallback to another unit.
- Per-device lighting settings that optionally follow confirmed USER-slot changes.
- Stream Deck commands, shortcut export, and setup instructions under Settings > Integrations.
- A compact Settings redesign with Device Connections / Controls tabs, themed icons and buttons, and optional setup details.

Features carried forward from 0.4.0 beta:

- A graph-native EQ workspace with numbered points and a contextual precision editor.
- Distinct selected, enabled, and bypassed band states plus draggable Q handles.
- Grouped EQ undo/redo, A/B curve comparison, and quick tone-shaping tools.
- A compact Tools menu for band organization, flattening, device-ready copies, smoothing, and tuning-card export.
- Explicit navigation across all ten K13 R2R USER slots.
- A dedicated Discover Online library source for AutoEQ and OPRA profiles, with separate load and write actions.
- Slide-over library and settings panels with cleaner connected surfaces, clearer hierarchy, and redesigned device controls.
- Guarded connect-time device loading that preserves unsaved editor changes.
- Explicit device readback capabilities so unsupported reads do not disconnect otherwise writable devices.
- Snowsky Melody USER 1-3 writes without unsupported automatic or manual EQ readback.
- Guarded community profiles for FiiO BR15 R2R, QX13, BTR17, and BTR13, including the BTR17 V2 save command and device-specific slot/filter limits.
- An Optimize Headroom control that shows the strongest enabled boost and matching inverse preamp before applying it.
- Advanced Windows audio quick controls for supported default-output formats, plus direct access to spatial sound settings.
- Hardened imports, online downloads, updater handling, local saves, device packets, and application logs.
- Faster wide-graph drawing and a cached combined AutoEQ/OPRA search index.
- Regression tests for editor history, synchronization, unsaved-edit protection, device capabilities, and security boundaries.

---

## ✨ What WolfEQ Does

- 🎚️ Edits PEQ gain, frequency, Q, filter type, and global preamp.
- 📈 Shows a live response preview while you shape a preset.
- 💾 Reads, writes, and refreshes verified K13 USER slots, with guarded save-only paths for selected community-configured devices.
- 📚 Saves, imports, exports, duplicates, favorites, and deletes local presets.
- 🔎 Searches online AutoEQ and OPRA profiles so you can start from known headphone correction curves.
- 🧰 Imports and exports WolfEQ JSON, Equalizer APO text, and FiiO XML.
- ⚠️ Shows the exact clipping margin and can match preamp automatically to the strongest enabled boost.

---

## Workflow

1. Open **Library** and choose **My Library** or **Discover Online**.
2. Search AutoEQ + OPRA, import a local preset, or select a saved profile.
3. Load the profile into the editor and tune it directly on the graph or in the precision controls.
4. Use **Tools** for undo/redo, sorting, flattening, safer device copies, smoothing, or a shareable tuning card.
5. Select a writable USER slot, review headroom and device compatibility, then write the result to the device.

Online profiles are downloaded into WolfEQ before they are applied. Loading an online result does not silently write to hardware, and writing remains limited to the selected device profile's declared USER slots.

---

## 🎧 Device Support

| Device | Status | Notes |
|-|-|-|
| FiiO K13 R2R | ✅ WolfEQ verified | Main tested target. USB detection, EQ readback, USER slot switching, PEQ writes, global preamp writes, preset storage, LED cues, USB/COAX input switching. |
| FiiO BR15 R2R | ⚠️ Experimental | Community-sourced 10-band layout and USER 1-10 save path. Save-only until WolfEQ readback is physically verified. |
| FiiO QX13 | ⚠️ Experimental | Community-sourced 10-band layout and USER 1-10 save path. Save-only until WolfEQ readback is physically verified. |
| FiiO BTR17 | ⚠️ Experimental | Community-sourced 10-band USER 1-10 layout with V2 `0x21` save command. Save-only until physically verified. |
| FiiO BTR13 | ⚠️ Experimental | Community-sourced 10-band layout with USER 1-3. Save-only until WolfEQ readback is physically verified. |
| FiiO KA15 | ⚠️ Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path. Needs real-device testing. |
| FiiO KA17 | ⚠️ Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path. Needs real-device testing. |
| FiiO JA11 | ⚠️ Experimental | Device profile, 5-band PEQ layout, core filter support. Needs real-device testing. |
| Snowsky Melody | ⚠️ Experimental | 10-band PEQ and USER 1-3 writes. Automatic EQ readback and Load from slot are disabled because the device does not answer the required read command. Readback remains unavailable by design. |
| Snowsky Retro Nano | ⚠️ Experimental | Product-name matched profile, slot map, 10-band PEQ layout. Needs real-device testing. |

For full Windows audio format support, set the K13 to **UAC2.0** mode.
FiiO documents that enabling K13 R2R PEQ limits playback to PCM 192 kHz/24-bit, disables MQA processing, and can briefly reconnect USB when switching between EQ Off and an active EQ mode.

The BR15 R2R, QX13, BTR17, and BTR13 layouts are adapted from the 0BSD-licensed [DevicePEQ](https://github.com/jeromeof/devicePEQ) project and cross-checked against FiiO's published PEQ documentation. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Upstream configuration is evidence for an experimental profile, not a substitute for WolfEQ hardware verification.

K15, K17, and K19 are not listed as supported because their dedicated serial/RS232 control path needs a separate transport implementation. K11 R2R support likewise needs the separate WalkPlay protocol rather than WolfEQ's current FiiO HID handler.

---

## Security and Data Safety

- Preset, library, settings, online-profile, release-metadata, and installer sizes are bounded before parsing or saving.
- FiiO XML import prohibits DTD/entity expansion; imported numeric values are normalized to safe finite ranges.
- Local library, lighting, and BLE cache updates use atomic replacement to reduce corruption after an interrupted save.
- AutoEQ, OPRA, and application-update downloads are restricted to their expected HTTPS hosts and repository paths.
- Update downloads use unique temporary paths, enforce a 250 MB limit, and must have a Windows executable header.
- FiiO writes validate the device profile, writable slot, band number, filter support, numeric ranges, HID report size, and response timeout.
- Application logs escape line breaks, truncate oversized entries, and rotate at 5 MB.

WolfEQ does not upload local presets or device data. Online search and update checks contact the providers named above. Installer code signing or published checksum verification remains recommended for a stable release.

---

## 🧪 Beta Notes

This is beta software, and every profile except K13 R2R is intentionally marked **experimental** in WolfEQ. The BR15 R2R, QX13, BTR17, and BTR13 profiles begin in save-only mode: automatic readback and live writes stay disabled until their Windows HID behavior is confirmed on physical hardware.

If you test an experimental profile:

1. Select the exact device model and click **Detect**.
2. Start with a flat or low-gain profile and an unused USER slot.
3. Confirm the selected slot on the device before choosing **Save Changes**.
4. Report the Windows version, device firmware, USB mode, selected slot, and whether the device stayed connected.
5. Attach `%LOCALAPPDATA%\WolfEQ\logs\wolfeq.log` when reporting a failure.

K13 controls still being explored:

- front-panel LCD text
- volume control
- EQ on/off switching
- NOS / OS / SAM mode switching
- optical and Bluetooth input switching

Please open an issue if something breaks or feels strange. Include your Windows version, device model, USB mode, and the exact preset/slot action you were doing.

---

## 🛠️ Build

```powershell
git clone https://github.com/audioslayer/wolfeq.git
cd wolfeq
dotnet build WolfEQ.csproj
dotnet run --project WolfEQ.csproj
```

Run the regression suite and dependency vulnerability audit:

```powershell
dotnet test tests\WolfEQ.Tests\WolfEQ.Tests.csproj
dotnet list WolfEQ.csproj package --vulnerable --include-transitive
```

For a local build-and-launch from a mapped drive or network share, use `deploy.bat`. It keeps WPF intermediate files under `%LOCALAPPDATA%\WolfEQ\deploy-build` so switching between drive letters and UNC paths cannot leave stale generated-source references.

If an older checkout reports missing `Views\*.g.cs` files, close any active build, remove the generated `obj` folder, and run the current `deploy.bat` again.

Build the installer with Inno Setup 6 installed:

```powershell
.\build-installer.bat
```

---

## 📄 License

MIT. See [LICENSE](LICENSE) for details.

---

<p align="center">
  Built by <a href="https://github.com/audioslayer">Tyson Wolf</a><br/>
  <a href="https://www.buymeacoffee.com/audioslayer">Buy me a coffee</a>
</p>
