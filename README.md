<p align="center">
  <img src="Assets/icon/wolfeq.png" width="120" alt="WolfEQ logo" />
</p>

<h1 align="center">WolfEQ</h1>

<p align="center">
  <strong>A modern Windows EQ workspace for the FiiO K13 R2R.</strong><br/>
  Edit 10-band PEQ profiles, preview response curves, sync verified USB changes, and keep per-slot lighting cues in one focused desktop app.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.1.0--beta-00E676" alt="Version" />
  <img src="https://img.shields.io/badge/Windows%2010%2F11-0078D6?logo=windows&logoColor=white" alt="Windows" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/FiiO-K13%20R2R-C8102E" alt="FiiO K13 R2R" />
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License" />
</p>

---

## What Is WolfEQ?

WolfEQ is a native Windows controller for FiiO K13 R2R EQ workflows. It is built for fast headphone tuning: import or download a curve, preview it, adjust the 10 PEQ bands directly, and let verified live sync save edits to the active K13 profile.

The app is intentionally practical. It keeps device controls, profile storage, online AutoEq discovery, graph editing, and profile lighting in one place so you do not need to bounce between the FiiO web UI, the mobile app, and loose preset files.

---

## Install

1. Download the latest **`WolfEQ-Setup-<version>.exe`** from [Releases](https://github.com/audioslayer/wolfeq/releases).
2. Run the installer.
3. Connect the FiiO K13 over USB.
4. Open WolfEQ and pick the EQ slot you want to tune.

> Releases are not published yet. The installer workflow is in place for beta testing.

---

## Supported Hardware

| Device | Support | What Works |
|-|-|-|
| **FiiO K13 R2R** | Beta | USB detection, EQ readback, USER slot switching, slot rename readback, global preamp writes, PEQ band writes, USB/COAX input switching, top/knob LED color and mode controls |

Bluetooth LE is only used where the K13 exposes controls that have not been mapped over USB yet. Core EQ editing and live sync are USB-first.

---

## Highlights

### Tune

- Live EQ response graph with draggable band points.
- 10-band PEQ editor with direct value entry for gain, frequency, and Q.
- Always-on live sync for verified USB preamp and band writes.
- Offline-safe profile editing when the device is disconnected.
- A/B capture, swap, and clear for quick listening comparisons.

### Library

- Local preset library with save, duplicate, favorite, import, and export.
- Online AutoEq search and download with non-editable preview curves.
- Equalizer APO / Peace text import and export.
- FiiO DSP XML import and export.
- WolfEQ JSON preset and library import/export.

### Device

- K13 EQ readback into the selected preset.
- USER 1-10 slot switching through the sidebar slot picker.
- Per-slot top LED and knob LED cues.
- Verified USB/COAX input switching.
- Windows playback quality picker for supported device formats.

### Polish

- AmpUp-inspired borderless shell with compact sidebar navigation.
- Runtime accent color selection.
- Custom sliders, dropdowns, scrollbars, and graph styling.
- GitHub Releases update check from Settings.
- Inno Setup installer packaging.

---

## Build From Source

```powershell
git clone https://github.com/audioslayer/wolfeq.git
cd wolfeq
dotnet build
```

Run the app:

```powershell
dotnet run --project WolfEQ.csproj
```

---

## Build The Installer

WolfEQ uses the same local Windows installer flow as Amp Up:

```powershell
.\build-installer.bat
```

The script:

- publishes a self-contained Windows x64 build
- reads `<Version>` from `WolfEQ.csproj`
- generates `installer/version.iss`
- builds `installer/output/WolfEQ-Setup-<version>.exe` with Inno Setup

Install [Inno Setup 6](https://jrsoftware.org/isinfo.php) before running the installer build.

The disabled workflow template at `.github/workflows/build-installer.yml.disabled` mirrors this process for GitHub Actions, but it is intentionally inactive until the release process is ready.

---

## Updates

WolfEQ checks `audioslayer/wolfeq` GitHub Releases from Settings. It compares the running assembly version with release tags, looks for a Windows `.exe` asset, and can download and launch the installer after confirmation.

No update is pushed automatically. Releases stay manual until beta testing is ready.

---

## Safety Notes

The verified path today is focused on EQ and lighting. Broad USB probing can make Windows drop the K13 HID interface until the device is restarted, so the app avoids unsafe scans in normal use.

Not treated as finished yet:

- arbitrary front-panel LCD text
- volume control
- EQ on/off toggle
- NOS/OS/SAM mode switching
- optical and Bluetooth input switching

---

## License

MIT. See [LICENSE](LICENSE) for details.

---

<p align="center">
  Built by <a href="https://github.com/audioslayer">Tyson Wolf</a><br/>
  <a href="https://www.buymeacoffee.com/audioslayer">Buy me a coffee</a>
</p>
