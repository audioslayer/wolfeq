<p align="center">
  <img src="Assets/icon/wolfeq.png" width="120" alt="WolfEQ logo" />
</p>

<h1 align="center">WolfEQ</h1>

<p align="center">
  <strong>A clean Windows EQ workspace for the FiiO K13 R2R.</strong><br/>
  Tune 10-band PEQ presets, preview response curves, sync K13 USER slots, and keep your device presets organized.
</p>

<p align="center">
  <a href="https://github.com/audioslayer/wolfeq/releases/latest"><img src="https://img.shields.io/github/v/release/audioslayer/wolfeq?include_prereleases&label=beta" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?logo=windows&logoColor=white" alt="Windows 10 / 11" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/FiiO-K13%20R2R-C8102E" alt="FiiO K13 R2R" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License" />
</p>

---

## 🚀 Download

Grab the latest beta installer from [GitHub Releases](https://github.com/audioslayer/wolfeq/releases/latest):

```text
WolfEQ-Setup-<version>.exe
```

Connect the K13 over USB, open WolfEQ, then choose the USER slot you want to tune.

---

## ✨ Highlights

- 🎛️ Wider AmpUp-inspired EQ layout with a cleaner Windows desktop feel.
- 🎚️ 10-band PEQ editing with gain, frequency, Q, and live response preview.
- 💾 Stable per-slot Device EQ presets for K13 USER 1-10 readback and refresh.
- 📚 Local preset library with save, import, export, duplicate, favorite, and delete support.
- 🔎 AutoEq search and preview for quick headphone starting points.
- ⚙️ Simplified Settings with update checks and a focused slide-out panel.

---

## 🎧 Device Support

WolfEQ is currently focused on the **FiiO K13 R2R** over USB.

| Device | Status | Supported today |
|-|-|-|
| FiiO K13 R2R | 🧪 Beta | USB detection, EQ readback, USER slot switching, PEQ writes, global preamp writes, preset storage, LED color cues, USB/COAX input switching |

For full Windows audio format support, set the K13 to **UAC2.0** mode.

---

## 🧪 Beta Notes

WolfEQ 0.1.0 beta is usable, but still early. The safest path is USB-first EQ editing, preset management, and K13 USER slot sync.

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
