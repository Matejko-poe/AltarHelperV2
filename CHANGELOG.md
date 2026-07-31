# Changelog

All notable changes to this project are documented here.

## [1.0.0] - 2026-07-31

### Added

- Public documentation, visual identity, installation guide, and contribution policy.
- Detailed FAQ explaining scoring, weights, vetoes, modes, visual signals, sounds, presets, and saved settings.
- In-plugin `FAQ / How it works` section available directly from the ExileCore settings panel.
- Configurable score overlays, pick arrows, background highlights, target dots, presets, veto thresholds, and unknown-mod inspection.
- Portable manual build through the `exapiPackage` MSBuild property.

### Fixed

- Prevented malformed or empty altar labels from crashing the parser.
- Corrected numeric normalization for decimals and signed values.
- Prevented positive and negative alert sounds from playing simultaneously.
- Replaced ImGui's automatic table headers with a plain row so unsupported menu glyphs cannot appear beside `Snd` and `Clr`.

### Changed

- New installations now start with the maintainer's tested weights, alert rules, display settings, hotkey, and sound configuration.
- Added prominent in-plugin and README warnings that bundled values are personal defaults and must be reviewed by each user.
- Removed the `mt_` prefix from the plugin name displayed in ExileCore.
- Expanded the modifier table to the full settings-window width and renamed `Snd`/`Clr` to `Alert`/`Clear`.
