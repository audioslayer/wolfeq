# Changelog

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
