# Equalizer APO (bundled installer)

WolfEQ's **Windows System EQ** mode uses [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) as its audio engine. The WolfEQ installer and portable ZIP ship the official, unmodified Equalizer APO x64 installer in this folder so a user can install it from WolfEQ's setup card.

The executable is not committed to git (see `.gitignore`). `build-installer.bat` downloads it when it is missing and refuses to build if its SHA-256 does not match the pinned value below.

| Field | Value |
| --- | --- |
| Program | Equalizer APO |
| Version | 1.4.2 |
| File | `EqualizerAPO-x64-1.4.2.exe` |
| Size | 11,980,366 bytes |
| Official download | https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-x64-1.4.2.exe/download |
| SHA-256 | `7403be7427bbe1936a40dded082829b6e217fc4f5990fee5cba501f0ae055afa` |
| License | GNU General Public License v2.0 (GPL-2.0) |
| Copyright | Jonas Thedering |
| Corresponding source | https://sourceforge.net/projects/equalizerapo/files/1.4.2/EqualizerAPO-src-1.4.2.zip/download (listed at https://sourceforge.net/projects/equalizerapo/files/1.4.2/) |

The pinned hash matches the `checksum64` in the Chocolatey [`equalizerapo` 1.4.2 package](https://community.chocolatey.org/packages/equalizerapo/1.4.2) and was confirmed against a fresh SourceForge download on 2026-10-10.

## Updating the pinned version

1. Download the new x64 installer from the official SourceForge project.
2. Compare its SHA-256 with an independent source (for example the Chocolatey package checksum).
3. Update `APO_VERSION` and `APO_SHA256` in `build-installer.bat`, this README, `THIRD_PARTY_NOTICES.md`, and the installer file name that WolfEQ launches.

Equalizer APO is a separate program. WolfEQ launches its installer and writes to its configuration folder; it does not link to or modify Equalizer APO.
