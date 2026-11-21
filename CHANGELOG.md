# Changelog

All notable changes to this project are documented here.

## [Unreleased]

## [1.1.0] - 2026-11-21

### Added

- [Test] "Profiles & Setup Wizard" panel (the old "Mods & Weights" panel is unchanged and edits the same weights):
  - Named profiles stored as one JSON file each in the ExileCore config folder (`Profiles`); manual save with a blinking Save button (auto-save optional), switch, duplicate, rename, delete, import/export via clipboard. Existing settings become the "Default" profile on first run.
  - A profile can inherit from a base profile and store only its overrides.
  - Setup wizard: answer questions about the character (damage, defences, resistances, flasks, charges, rewards, risk) and get a new profile with downside weights and thresholds filled in, with a preview before creating it.
- Current chaos prices, in parentheses and a separate colour, next to reward mods (orbs, threads, Ichors/Embers) in the weights list.

- Profiles: a blinking SAVE button appears at the top of the panels (and the near editor) while there are unsaved changes; changed mods show a red "(not saved)". Auto-save is an option (off by default); switching profiles is blocked until you save or discard.
- [Test] Near editor (hotkey Numpad 7 or the "Near editor window" toggle): an in-game window with sliders, danger-level buttons and Alert checkboxes for exactly the mods of the altar you stand at, with chaos prices; edits go to the active profile.
- Near editor: -5..+5 danger slider (BRICK / Lethal / Severe / Moderate / Minor / Ignore / Harmless +1..+5) coloured red to green, plus Ignore, Harmless + and BRICK buttons.
- Near editor: "Recent altars (last 10)" window; "Edit mods" reopens an earlier altar in the editor so its weights can be changed after the fact.
- Fixed false "unknown mod" for boss drops with a count of 1 ("Final Boss drops 1 additional Ultimatum Thread", "an additional Divination Card ..."): all wordings now resolve to one canonical id, also for minion "N% chance to drop an additional X" and game markup. New setting "Scale boss drops by count": the weight is for 3 drops and is scaled to the real count; the near editor shows "Sum: N x price = X c" per drop and the total chaos value per option.
- Added "reduced Quantity / Rarity" downsides (checked against the game stat descriptions). Profiles now live in the ExileCore config folder (existing ones are copied over once).
- Rarity default raised to 1500 (Quantity 1000); button in the profile panel applies the unpriced reward defaults to an existing profile.
- New setting "Signed downside weights" (default off = old behaviour). When on, a positive weight on a downside is a small bonus (only counted if the option has a reward); wizard profiles turn it on and give harmless downsides a small plus.

### Changed

- Reward weight model reworked: chaos price is the unit, Divine Orb stays the top (10000), other rewards use 10000 x (price/divine)^0.65 so a pricier item always outweighs a cheaper one (Grand Ichor/Ember > Chaos Orb) and a Chaos Orb still counts; Thread prices are multiplied x10 because they drop in big stacks. Duplication/quantity weights raised, extra Divination Card rewards added.
- Wizard penalties are milder (Minor 60 / Moderate 250 / Severe 800 / Lethal 3000) with lower pick thresholds.
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
