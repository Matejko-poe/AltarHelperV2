<p align="center">
  <img src="docs/assets/altar-helper-banner.png" alt="Altar Helper V2 — eldritch altar choice visualization" width="100%">
</p>

<h1 align="center">Altar Helper V2</h1>

<p align="center">
  A visual ExileCore overlay that scores Eldritch Altar options and makes the best choice immediately readable.
</p>

> [!IMPORTANT]
> This is an independent, community-made project. It is not affiliated with or endorsed by Grinding Gear Games.

## What it does

- highlights the recommended altar option with a configurable frame and background;
- marks dangerous choices in red;
- displays a compact score, a `TAKE` indicator, and the most important modifier;
- distinguishes player, minion, and final-boss modifiers with colored dots;
- supports positive and negative veto thresholds;
- provides searchable weights, quick presets, sound alerts, and an unknown-mod inspector.

## Installation

1. Download or clone this repository into `Plugins/Source/AltarHelperV2` inside your ExileCore directory.
2. Start ExileCore and wait for the plugin to compile.
3. Open the ExileCore plugin menu, select `mt_AltarHelperV2`, and enable it.
4. Configure weights under **Mods & Weights (V2)** or apply one of the quick presets.

For a manual build, install the .NET 10 SDK and run:

```powershell
dotnet build -p:exapiPackage="D:\path\to\ExileCore"
```

The specified directory must contain `ExileCore.dll` and `GameOffsets.dll`.

## Visual language

| Signal | Meaning |
|---|---|
| Gold | Positive-veto choice; highest priority |
| Yellow | Recommended choice |
| Orange | Mixed upside and downside |
| Red | Avoid / negative veto |
| Green dot | Eldritch minions |
| Cyan dot | Player |
| Blue dot | Final map boss |

Press `F7` by default to switch between **Any**, **Minions + Player**, and **Boss + Player** modes.

## Configuration notes

Weights are personal priorities, not live prices. A positive value rewards a modifier; a negative value penalizes it. The bundled presets are starting points and may become outdated as the economy changes. Set a positive veto threshold for rewards you never want to miss, and a negative veto threshold for modifiers your build cannot safely run.

Sound file names refer to files from ExileCore's `Sounds` directory. The plugin reads only the visible altar UI and stores settings locally; it does not send telemetry or make network requests.

## Troubleshooting

- **Plugin does not compile:** confirm the folder is under `Plugins/Source` and ExileCore is current.
- **No highlight appears:** enable the plugin and assign non-zero weights or apply a preset.
- **A modifier is not recognized:** expand **Unknown Mods**, then use **Find** to locate and configure it.
- **Sound does not play:** confirm the `.wav` file exists in ExileCore's `Sounds` directory.
- **Manual build cannot find dependencies:** pass `-p:exapiPackage="..."` as shown above.

Please include the ExileCore version, game version, `Errors.txt`, and a screenshot when reporting a bug.

## Development

The project targets `net10.0-windows` and x64. Contributions are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md). Release history is tracked in [CHANGELOG.md](CHANGELOG.md).

## License

Released under the [MIT License](LICENSE).
