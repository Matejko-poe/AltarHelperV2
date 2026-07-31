# Changelog

All notable changes to this project are documented here.

## [1.0.0] - 2026-07-31

### Added

- Public documentation, visual identity, installation guide, and contribution policy.
- Configurable score overlays, pick arrows, background highlights, target dots, presets, veto thresholds, and unknown-mod inspection.
- Portable manual build through the `exapiPackage` MSBuild property.

### Fixed

- Prevented malformed or empty altar labels from crashing the parser.
- Corrected numeric normalization for decimals and signed values.
- Prevented positive and negative alert sounds from playing simultaneously.
