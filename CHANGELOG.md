# Changelog

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
