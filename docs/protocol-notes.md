# K13 R2R Protocol Notes

These are working notes for WolfEQ. Validate against the actual device before enabling writes.

## USB HID

- Vendor ID: `0x2972`
- Interface: `3`
- OUT endpoint: `0x02`
- IN endpoint: `0x83`
- Report ID: `0x07`

Packet templates:

```text
GET: [0xBB, 0x0B, 0x00, 0x00, CMD, DATA_LEN, ...DATA, 0x00, 0xEE]
SET: [0xAA, 0x0A, 0x00, 0x00, CMD, DATA_LEN, ...DATA, 0x00, 0xEE]
```

Readback command IDs verified with GET packets:

- `0x15` EQ band item. Request data: `[zero_based_band_index]`.
- `0x16` current EQ preset.
- `0x17` global EQ gain / preamp.
- `0x18` EQ band count.
- `0x1A` EQ enabled switch.
- `0x30` preset name. Request data: `[preset_id]`.

Read-only USB probing notes:

- GET scan `0x00` through `0x40` produced known EQ/status responses only; no obvious NOS/OS/SAM command was identified.
- Avoid broad full-range USB GET scans while the app is in normal use. On Tyson's Windows setup, an attempted full-range probe caused the K13 HID interface to disappear until USB/power recovery.
- `0x1A` EQ switch SET was tested with data `[0x00]`; `GET 0x1A` still read back enabled. Treat EQ switch writes as unverified.

Windows HID readback uses report ID `0x07` and the `MI_03` collection with 33-byte input/output reports.

Snowsky Retro Nano USB scan from a user report:

- Device identity: VID `0x0A12`, PID `0x4007`, product `RETRO NANO`, manufacturer `FiiO`.
- HID candidates:
  - `MI_02`, usage `0x000C/0x0001`, reports `3/0/0`: consumer/media control, not usable for EQ traffic.
  - `MI_02`, usage `0xFFA0/0x0001`, reports `65/65/0`: vendor-defined candidate.
  - `MI_03`, usage `0xFF00/0x0001`, reports `66/66/4`: preferred vendor-defined EQ-control candidate.
- Windows device inventory may also contain stale disabled FiiO BTR17 entries with the same CSR/Qualcomm VID/PID. Prefer enabled HID metadata with product string `RETRO NANO`.
- First remote test of the MI_03 build: Retro Nano was recognized and saved EQ applied, but the device disconnected after save. Treat post-save HID loss/re-enumeration as expected and retry detection/readback before declaring failure.

Guarded rename command:

- `0x30` preset name SET. Data: `[preset_id, ...ascii_name_max_8_bytes]`.
- WolfEQ only allows this for USER preset IDs `0xA0` through `0xA9`.
- This has not been treated as an EQ write path; band/preamp/save writes remain disabled.
- Observed on Tyson's K13: USB readback returns the custom name, but the front-panel display still shows the built-in USER slot label.
- No arbitrary LCD text or now-playing display command has been identified. Do not assume `0x30` can print free text on the front-panel LCD; it only appears to affect the stored USER preset name returned by USB readback.
- `tools/K13UsbPresetTest --rename-active TEXT` exists for a guarded closest-possible test. It reads the current preset first, refuses non-USER slots, then sends `SET 0x30` only for the active USER slot.

Guarded preset select command:

- `0x16` current preset SET. Data: `[preset_id]`.
- WolfEQ only allows this for USER preset IDs `0xA0` through `0xA9`.
- No save command is sent. This changes the active USER slot only.
- Verified on Tyson's K13: selecting USER 2 sent `AA 0A 00 00 16 01 A1 00 EE`; `GET 0x16` read back `A1`.

## Bluetooth LE

Lighting controls are BLE-side, not USB HID.

- Service UUID: `00001100-04a5-1000-1000-40ed981a04a5`
- Write characteristic: `00001101-04a5-1000-1000-40ed981a04a5`
- Notify characteristic: `00001102-04a5-1000-1000-40ed981a04a5`
- Packet shape: `[0xF1, 0x10, 0x00, LEN, CMD0, CMD1, CMD2, ...DATA, 0xFF]`
- GET input source: `[0x09, 0x02, 0x01]`
- SET input source: `[0x19, 0x02, 0x01] + [source]`
- Input source values observed/reference: USB `0x01`, COAX `0x04`, OPTICAL `0x08`, BLUETOOTH `0x20`
- GET light switch: `[0x05, 0x01, ZONE]`
- GET light mode: `[0x05, 0x02, ZONE]`
- GET light color: `[0x05, 0x03, ZONE]`
- SET light switch: `[0x15, 0x01, ZONE] + [0x00/0x01]`
- SET light mode: `[0x15, 0x02, ZONE] + [mode]`
- SET light color: `[0x15, 0x03, ZONE] + [color]`
- Zones: top `0x02`, knob `0x03`
- Modes: always on `0x00`, breathe `0x01`
- Colors: follow audio `0x00`, red `0x01`, blue `0x02`, turquoise `0x03`, purple `0x04`, yellow `0x05`, white `0x06`, green `0x07`, cycle `0x08`
- Candidate GET volume from newer reference: `[0x02, 0x01, 0x01]`. Tyson's K13 did not respond to this command in WolfEQ testing.
- Candidate GET volume limit used by WolfEQ's experimental Retro Nano controls: `[0x02, 0x03, 0x01]`, SET `[0x12, 0x03, 0x01] + [0-99]`. Needs real-device confirmation.
- Candidate GET gain mode from newer Linux/Android-reference project: `[0x02, 0x02, 0x01]`, with candidate SET `[0x12, 0x02, 0x01] + [0x00 low / 0x01 high]`. Tyson's K13 did not respond to the direct GET on Windows BLE testing, so do not enable writes.
- Candidate GET channel balance from newer reference: `[0x02, 0x06, 0x01]`, SET `[0x12, 0x06, 0x01] + [direction, magnitude]`.
- DRE command is not mapped yet. The WolfEQ UI keeps the DRE toggle visible for Retro Nano testing, but writes are skipped until a packet is identified.
- Candidate GET SPDIF output from newer reference: `[0x02, 0x08, 0x01]`, SET `[0x12, 0x08, 0x01] + [0/1]`.
- Candidate GET DAC filter from newer reference: `[0x02, 0x09, 0x01]`, SET `[0x12, 0x09, 0x01] + [index]`.
- Candidate GET harmonic/NOS-OS/SAM-style mode from newer reference: `[0x02, 0x0A, 0x01]`, SET `[0x12, 0x0A, 0x01] + [mode]`.
- Candidate GET auto power-off from newer reference: `[0x02, 0x0B, 0x01]`, SET `[0x12, 0x0B, 0x01] + [value]`.
- Candidate GET display mode from newer reference: `[0x05, 0x01, 0x01]`, SET `[0x15, 0x01, 0x01] + [0/1]`.
- Candidate GET screen brightness from newer reference: `[0x05, 0x05, 0x01]`, SET `[0x15, 0x05, 0x01] + [level]`.
- Candidate GET sample-rate info from newer reference: `[0x04, 0x03, 0x01]`; payload is described as `[dsd_flag, bit_depth, sample_rate_hi, sample_rate_lo]`.
- Candidate GET volume from older probe: `[0x00, 0x02, 0x02]`. Tyson's K13 responded with `03 03` while the device front panel volume was `85`, so this is not actual volume.
- Candidate SET volume from older probe shape: `[0x10, 0x02, 0x02] + [level]`. It ACKed, but readback stayed unchanged; do not treat this as working volume control.
- Candidate SET volume from newer reference: `[0x12, 0x01, 0x01] + [level]`. Standalone test sent level `0x54` while the K13 front panel showed `85`; device ACKed with `F1 10 00 09 12 01 01 00 FF`, but the front panel remained `85`.
- Volume level range used by one reference implementation: `0` through `99`.

Verified on Tyson's K13:

- BLE advertisement name: `FIIO K13 R2R`
- BLE input source GET `[0x09, 0x02, 0x01]` read back `0x01` for USB.
- BLE input source SET `[0x19, 0x02, 0x01] + [source]` is partially verified. Re-setting USB (`0x01`) ACKed and read back USB. COAX (`0x04`) ACKed, emitted input-source notification `0x04`, read back COAX, and then returned to USB successfully. OPTICAL (`0x08`) and BLUETOOTH (`0x20`) ACKed but readback stayed USB on Tyson's setup.
- WolfEQ UI exposes input writes only for verified USB and COAX.
- Readback worked for top/knob switch, mode, and color.
- Write test set both lights to green / always on and read back top/knob as on, mode `0x00`, color `0x07`.
- Light power toggle works for both top/knob zones and preserves the selected mode/color.
- Volume controls are not verified. Keep writes guarded/experimental and prefer read-only probing until a response correlates with the K13 front-panel volume.
- `Probe Vol 85` scan found no response containing `0x55` while the K13 front-panel volume was `85`.
- Standalone BLE GET scan over likely legacy/newer device-setting candidates found 38 responses and no `0x55` or BCD `0x85` while the front-panel volume was `85`.
- Passive notification capture is the next read-only diagnostic: subscribe to BLE notifications, turn the K13 volume knob, and inspect any unsolicited packets before attempting more writes.

## Retro Nano Bluetooth LE

Confirmed from Android FiiO Control HCI snoop against `RETRO NANO`:

- BLE advertisement/service data includes 16-bit service `0xfcc0`.
- Command write handle: `0x0017`
- Notification handle: `0x0019`
- Enable notifications by writing `01 00` to CCCD handle `0x001a`.
- There is also a secondary notify path at handle `0x0029` with CCCD `0x002b`; Android enables it and receives short `4c` / `4b` style keepalive/status values. Do not use that as the command channel.
- Command family is raw `00 0A 04 ...`; replies are raw `00 0A 84 ...`.

Read command shape:

```text
GET: 00 0A 04 02 CMD
GET response: 00 0A 84 02 00 CMD ...DATA
```

Write command shape:

```text
SET: 00 0A 04 12 CMD ...DATA
SET ack: 00 0A 84 12 00 CMD
```

Confirmed controls from Android app order:

- Volume:
  - GET: `00 0A 04 02 09`
  - SET: `00 0A 04 12 09 LEVEL`
  - Observed readback changed from `00 0A 84 02 00 09 18` to `00 0A 84 02 00 09 1F` after setting `1F`.
  - Treat level as one byte, likely decimal `0` through `99`.
- Volume limiter:
  - GET: `00 0A 04 02 04`
  - SET: `00 0A 04 12 04 LIMIT`
  - Observed `06` and `08` round-tripped as readbacks.
- Balance:
  - GET: `00 0A 04 02 06`
  - SET: `00 0A 04 12 06 SIDE AMOUNT`
  - Observed readbacks: `00 0A 84 02 00 06 00 04`, `00 0A 84 02 00 06 01 04`, and `00 0A 84 02 00 06 01 00`.
  - Side/direction labels are not confirmed yet. Keep UI/tool output raw until left/right mapping is verified.

Controls not exposed by the Android app:

- Gain
- DRE

Do not expose or write gain/DRE for Retro Nano until a separate verified source identifies those commands.

Tooling:

- `tools/K13BlePacketTest` now has Retro Nano mode:
  - `--nano-scan` scans for and caches the Nano BLE address.
  - `--nano-read` enumerates GATT, subscribes to notifications, and reads volume, volume limiter, and balance.
  - `--nano-listen [seconds]` captures raw notifications.
  - Write switches are explicit only: `--nano-set-volume N`, `--nano-set-limit N`, `--nano-set-balance SIDE AMOUNT`.

## PEQ band encoding

- Frequency: unsigned 16-bit big-endian Hz
- Gain: signed 16-bit big-endian, dB × 10
- Q: unsigned 16-bit big-endian, Q × 100
- Filter type:
  - `0` Peak
  - `1` Low Shelf
  - `2` High Shelf
  - `3` Band Pass
  - `4` Low Pass
  - `5` High Pass
  - `6` All Pass

## Safety

First implementation should be read-only. Enable writes only after:

1. Device detection is reliable.
2. Current preset readback works.
3. Encoded packet bytes are logged and inspected.
4. Save is limited to a chosen USER slot, not factory presets.
