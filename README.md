<p align="center">
  <img src="Assets/icon/wolfeq.png" width="120" alt="WolfEQ logo" />
</p>

<h1 align="center">WolfEQ</h1>

<p align="center">
  <strong>WolfEQ 0.3.1 beta is the latest release.</strong><br/>
  A modern Windows PEQ workspace for FiiO K13 R2R and experimental FiiO / Snowsky device support.
</p>

<p align="center">
  <a href="https://github.com/audioslayer/wolfeq/releases/latest"><img src="https://img.shields.io/github/v/release/audioslayer/wolfeq?include_prereleases&label=beta" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?logo=windows&logoColor=white" alt="Windows 10 / 11" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/FiiO-K13%20R2R-C8102E" alt="FiiO K13 R2R" />
  <img src="https://img.shields.io/badge/devices-experimental-orange" alt="Experimental device profiles" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License" />
</p>

---

## 🐺 Overview

WolfEQ is a desktop app for people who want a cleaner way to tune PEQ on supported FiiO and Snowsky devices. Instead of juggling device menus, preset files, and separate graph tools, WolfEQ brings the core workflow into one Windows app: choose a slot, shape the curve, preview the response, save, and keep your profiles organized.

The **FiiO K13 R2R** is the main tested device. WolfEQ 0.3.1 beta also includes experimental profiles for KA15, KA17, JA11, Snowsky Melody, and Snowsky Retro Nano. Melody supports USER-slot writes without attempting unsupported automatic EQ readback.

---

## 🚀 Latest Beta

Download WolfEQ 0.3.1 beta from the [latest GitHub release](https://github.com/audioslayer/wolfeq/releases/latest):

- [Windows x64 installer](https://github.com/audioslayer/wolfeq/releases/latest/download/WolfEQ-Setup-0.3.1-beta.exe)
- [Portable Windows x64 ZIP](https://github.com/audioslayer/wolfeq/releases/latest/download/WolfEQ-0.3.1-beta-win-x64.zip)

WolfEQ 0.3.1 beta adds:

- A curve-first EQ workspace with a permanent device dock and focused editor.
- Slide-over library and settings panels with clearer save, load, and write actions.
- Guarded connect-time device loading that preserves unsaved editor changes.
- Explicit device readback capabilities so unsupported reads do not disconnect otherwise writable devices.
- Snowsky Melody USER 1-3 writes without unsupported automatic or manual EQ readback.
- Regression tests for editor synchronization, unsaved-edit protection, and device capabilities.

---

## ✨ What WolfEQ Does

- 🎚️ Edits PEQ gain, frequency, Q, filter type, and global preamp.
- 📈 Shows a live response preview while you shape a preset.
- 💾 Reads, writes, and refreshes K13 USER slots over USB.
- 📚 Saves, imports, exports, duplicates, favorites, and deletes local presets.
- 🔎 Searches online AutoEq profiles so you can start from known headphone correction curves.
- 🧰 Imports and exports WolfEQ JSON, Equalizer APO text, and FiiO XML.
- ⚠️ Flags clipping risk and helps apply safer headroom before writing to hardware.

---

## 🎧 Device Support

| Device | Status | Notes |
|-|-|-|
| FiiO K13 R2R | 🧪 Beta | Main tested target. USB detection, EQ readback, USER slot switching, PEQ writes, global preamp writes, preset storage, LED cues, USB/COAX input switching. |
| FiiO KA15 | ⚠️ Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path. Needs real-device testing. |
| FiiO KA17 | ⚠️ Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path. Needs real-device testing. |
| FiiO JA11 | ⚠️ Experimental | Device profile, 5-band PEQ layout, core filter support. Needs real-device testing. |
| Snowsky Melody | ⚠️ Experimental | 10-band PEQ and USER 1-3 writes. Automatic EQ readback and Load from slot are disabled because the device does not answer the required read command. Hardware confirmation for 0.3.1 is pending in [issue #2](https://github.com/audioslayer/wolfeq/issues/2). |
| Snowsky Retro Nano | ⚠️ Experimental | Product-name matched profile, slot map, 10-band PEQ layout. Needs real-device testing. |

For full Windows audio format support, set the K13 to **UAC2.0** mode.

---

## 🧪 Beta Notes

This is beta software, and the newest device profiles are intentionally marked **experimental**. If you try KA15, KA17, JA11, Snowsky Melody, or Snowsky Retro Nano, please treat it like early support and report what works, what fails, and what the device shows when switching or saving.

Still being explored:

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
dotnet build
dotnet run --project WolfEQ.csproj
```

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
