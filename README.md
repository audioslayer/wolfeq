<p align="center">
  <img src="Assets/icon/wolfeq.png" width="120" alt="WolfEQ logo" />
</p>

<h1 align="center">WolfEQ</h1>

<p align="center">
  <strong>WolfEQ 0.2.0 beta is here.</strong><br/>
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

The **FiiO K13 R2R** is the main tested device. WolfEQ 0.2.0 beta also adds early experimental profiles for KA15, KA17, JA11, Snowsky Melody, and Snowsky Retro Nano.

---

## 🚀 Latest Beta

Download the latest installer from [GitHub Releases](https://github.com/audioslayer/wolfeq/releases/latest):

```text
WolfEQ-Setup-<version>.exe
```

WolfEQ 0.2.0 beta adds:

- 🎧 Experimental device profiles for FiiO KA15, FiiO KA17, FiiO JA11, Snowsky Melody, and Snowsky Retro Nano.
- 📈 A cleaner PEQ graph with hover readouts, better band handles, and a more standard response feel.
- 🎛️ A redesigned tuning workspace with compact band rows and a right-side preset/profile panel.
- 📚 A better library tab with online profile search, import/export tools, and cleaner device-slot names.
- 💾 Improved K13 read-on-boot, slot switching, save confirmation, and post-save reload behavior.

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
| Snowsky Melody | ⚠️ Experimental | Device profile, slot map, 10-band PEQ layout, USB EQ path. Needs real-device testing. |
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
