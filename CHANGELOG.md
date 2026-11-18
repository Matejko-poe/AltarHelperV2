# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Changed

- Synced the altar modifier list with the current patch (poedb Eldritch Altar): Scarab rewards replaced by Threads, added Ancient Orbs, removed Orbs of Binding/Horizons and Cartographer's Chisels.
- Updated the "Enemies within # metres" player/boss modifiers and Rarity roll ranges.
- Replaced the Scarab Focus preset with Thread Focus.
- Reward weights are now derived from poe.ninja prices (Covens, 2026-11-17): Divine Orb = 10000 (always top), others proportional to price; minion drops are 40% of boss drops.
- Automatic price updates (Test): reward weights are re-priced from the Ninja Price plugin cache (Currency2.json, Threads.json) when it changes; manual slider edits are protected. Toggle and "Refresh prices now" in the weights panel.
- New setting "Min. net weight to recommend an option": below it no option is highlighted (skip the altar).
- Added the "[Test] Champion (Bannerman build)" preset tuned to a specific melee physical armour character, including thresholds.
- Added the "Armour Build" preset with small negative weights for defensive downsides.
- Fixed the stale "-#% to Critical Strike Multiplier per Power Charge" key (sign was missing).

## [1.0.1] - 2026-08-03

### Fixed

- Preserved leading `-` and `+` signs while normalizing altar values, so configured negative player modifiers now contribute to downside weight.

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

- Recreated the GitHub repository as an official fork of `bruno105/AltarHelper`, preserving upstream ancestry and adding explicit attribution.
- New installations now start with the maintainer's tested weights, alert rules, display settings, hotkey, and sound configuration.
- Added prominent in-plugin and README warnings that bundled values are personal defaults and must be reviewed by each user.
- Removed the `mt_` prefix from the plugin name displayed in ExileCore.
- Expanded the modifier table to the full settings-window width and renamed `Snd`/`Clr` to `Alert`/`Clear`.
