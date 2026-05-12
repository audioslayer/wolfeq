<p align="center">
  <img src="Assets/icon/wolfeq.png" width="120" alt="WolfEQ logo" />
</p>

<h1 align="center">WolfEQ</h1>

<p align="center">
  <strong>Introducing WolfEQ 0.2.0 beta.</strong><br/>
  A focused Windows EQ workspace for tuning, previewing, and syncing FiiO / Snowsky PEQ presets.
</p>

<p align="center">
  <a href="https://github.com/audioslayer/wolfeq/releases/latest"><img src="https://img.shields.io/github/v/release/audioslayer/wolfeq?include_prereleases&label=beta" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?logo=windows&logoColor=white" alt="Windows 10 / 11" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/FiiO-K13%20R2R-C8102E" alt="FiiO K13 R2R" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License" />
</p>

---

## 🐺 Overview

WolfEQ is a new desktop app for people who want a cleaner way to manage EQ on the **FiiO K13 R2R**. Instead of jumping between device menus, loose preset files, and separate graph tools, WolfEQ brings the core tuning workflow into one Windows app.

This beta focuses on the essentials: edit PEQ profiles, preview the response curve, read the active device USER slot, save presets locally, and push verified changes back to supported devices over USB.

---

## 🚀 First Beta Release

WolfEQ 0.2.0 beta expands the app beyond the K13-only first release with experimental device profiles, a redesigned tuning workspace, cleaner slot handling, and improved read/save confirmation.

Download the latest installer from [GitHub Releases](https://github.com/audioslayer/wolfeq/releases/latest):

```text
WolfEQ-Setup-<version>.exe
```

Connect the K13 over USB, open WolfEQ, then choose the USER slot you want to tune.

---

## ✨ What WolfEQ Does

- 🎛️ Gives the K13 a wider, cleaner EQ workspace built for desktop tuning.
- 🎚️ Edits 10-band PEQ values for gain, frequency, and Q.
- 📈 Shows a live response preview while you shape a preset.
- 💾 Reads and refreshes K13 USER slot presets without duplicate readbacks.
- 📚 Saves, imports, exports, duplicates, favorites, and deletes local presets.
- 🔎 Searches AutoEq so you can start from known headphone correction curves.
- ⚙️ Keeps settings, updates, and device options in a simplified slide-out panel.

---

## 🎧 Device Support

WolfEQ is built around the **FiiO K13 R2R** over USB, with additional experimental profiles for nearby FiiO / Snowsky devices.

| Device | Status | Supported in 0.2.0 beta |
|-|-|-|
| FiiO KA15 | Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path |
| FiiO KA17 | Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path |
| FiiO JA11 | Experimental | Device profile, 5-band PEQ layout, core filter support |
| Snowsky Melody | Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path |
| Snowsky Retro Nano | Experimental | Product-name matched profile, slot map, 10-band PEQ layout |
| FiiO K13 R2R | 🧪 Beta | USB detection, EQ readback, USER slot switching, PEQ writes, global preamp writes, preset storage, LED color cues, USB/COAX input switching |

For full Windows audio format support, set the K13 to **UAC2.0** mode.

---

## 🧪 Beta Notes

This is an early release, so the safest path is USB-first EQ editing, preset management, and K13 USER slot sync.

Still being explored:

- front-panel LCD text
- volume control
- EQ on/off switching
- NOS / OS / SAM mode switching
- optical and Bluetooth input switching

Please open an issue if something breaks or feels strange. Include your Windows version, K13 mode, and what you were doing when it happened.

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
