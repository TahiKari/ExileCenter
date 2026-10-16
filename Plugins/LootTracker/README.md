# LootTracker

A [GH](https://github.com/Gordin/GameHelper2) plugin that tracks a mapping **session**: it times each
map run, reads what you pick up straight from inventory memory, prices it via [poe.ninja](https://poe.ninja)
(in Exalted / Divine Orbs), tallies kills per rarity, and keeps a browsable on-disk history of past
sessions — all on a slim overlay that stays out of the way.

While on a map it shows a thin strip pinned just above the experience bar; back in the hideout it
swaps to a compact session bar that sits in the empty band over the bottom HUD, so nothing floats in
the middle of the screen.

## Features

- **Map strip (on maps).** A slim, click-through line above the experience bar: current map name, run
  timer, profit so far (Exalted + Divine), and per-rarity kill counts (Normal · Magic · Rare · Unique).
- **Compact hideout bar.** A wide bar anchored into the unused band above the bottom HUD, showing the
  session at a glance: New-session button, session time, maps completed, average time per map, average
  profit, total Divine and Divine/hour, plus a table of recent map runs. Auto-hides while the Atlas /
  world-travel panel (or any large panel) is open, and when the game window isn't focused.
- **Inventory-based loot tracking.** Profit is computed from a live read of your main inventory, diffed
  against a per-run baseline — no screen OCR, no manual entry. Unpriceable items are simply skipped.
- **Language-independent pricing.** Items are matched to poe.ninja via the game's own art/metadata
  identifiers rather than localized names, so it works on any client language. Prices are cached on
  disk with a configurable TTL, and totals use the live Divine→Exalted rate.
- **Per-run kill tally.** Monsters are counted on their alive→dead transition using the core entity
  state, throttled and read once per monster — cheap enough to leave on permanently.
- **Map-run resume by hash.** Leaving a map to the hideout banks its progress into the history; on
  return the run is matched by its instance hash and continued, so the common map → subzone → back
  flow doesn't fragment one run into several.
- **On-disk session history.** Each *New session* archives the finished one to JSON in the plugin's
  `config/sessions` folder. A history window lists every session (date, length, map count, total
  Divine, Divine/hour) with **view detailed** (aggregates + per-map table + the loot gathered on each
  map) and **delete**. Older sessions are pruned past a configurable limit.

## Requirements

- A working [GH](https://github.com/Gordin/GameHelper2) checkout (this is a plugin, not a
  standalone app).
- .NET 10 SDK (the project targets `net10.0-windows`, x64).

> **Core compatibility.** Two niceties — anchoring the bars exactly above the experience bar, and
> auto-hiding the compact bar while the Atlas / a large panel is open — use `ExperienceBar` and
> `IsAnyLargePanelOpen` on `ImportantUiElements`. These are resolved by reflection, so on a core that
> lacks them the plugin still runs: the bars fall back to a viewport-anchored position and the compact
> bar simply doesn't auto-hide. No hard dependency.

## Build & install

This plugin is meant to live inside a GH source tree, because it references `GameHelper.csproj` and
copies its build output into GameHelper's `Plugins` folder.

1. Clone this repo into the GameHelper2 `Plugins` directory so the layout is:

   ```
   <GameHelper2>/
     GameHelper/
       GameHelper.csproj
     Plugins/
       LootTracker/          ← contents of this repo
         LootTracker.csproj
         LootTrackerCore.cs
         metaArt.json
         icons/
         ...
   ```

   The `.csproj` expects `..\..\ExileCenter\ExileCenter.csproj` to exist relative to itself.

2. Build:

   ```
   dotnet build Plugins/LootTracker/LootTracker.csproj -c Debug
   ```

   The post-build step copies `LootTracker.dll` (plus `metaArt.json` and `icons/`) into
   `GameHelper/<OutDir>/Plugins/LootTracker/`.

3. Launch GameHelper2 and enable **LootTracker** in the plugin list.

> `metaArt.json` and `icons/` are **source data and must ship with the repo** — without them item
> pricing can't bridge to poe.ninja art ids and the overlay icons are missing.

## Settings

The HUD-layout knobs live in a collapsed **Settings** header at the top of the panel:

| Setting | Default | Notes |
|---|---|---|
| **Compact bar height (px)** | `115` | Height of the hideout compact bar. |
| **History size** | `50` | Completed-map rows kept in the live session (table + memory); oldest dropped past this. |
| **Anchor to right side** | `on` | Side for the map strip's fallback position. |
| **Offset from bottom (px)** | `30` | Fallback only — used when the experience bar can't be located. |
| **Bar opacity** | `0.55` | Background opacity of both bars. |
| **Show kill counts** | `on` | Per-rarity monsters slain this run on the map strip. |

Below the header:

| Setting | Default | Notes |
|---|---|---|
| **View session history** | — | Opens the saved-sessions window (detail / delete per session). |
| **Sessions to keep** | `30` | Older sessions on disk are deleted once this many are stored. |
| **League** | `Runes of Aldur` | poe.ninja PoE2 league slug; update each league launch. |
| **Refresh interval (min)** | `60` | How long cached prices stay valid before a re-fetch (5–60). |

The Pricing section also shows the cache status (last sync, items cached, Divine→Exalted rate) and a
**Refresh now** button.

## Credits

- Built as a plugin for [GameHelper2](https://github.com/Gordin/GameHelper2).
- Prices courtesy of [poe.ninja](https://poe.ninja).

## Disclaimer

This is a read-only overlay tool for personal use. Use at your own risk and in accordance with the game's terms of service.
