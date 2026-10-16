# Hiveblood

Community plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) (Path of Exile 2).

Tracks **Hiveblood** in Path of Exile 2: syncs your total from the **Genesis Tree** UI and adds **+N** gain popups from **Breach** runs. Configurable overlay (inventory anchor or draggable position dummy), cap warning with blink.

Written by **MordWraith** for community GameHelper2 forks. Read-only overlay — **build from source**.

Also bundled in [MordWraith/Gamehelper](https://github.com/MordWraith/Gamehelper) releases.

## Features

- **Genesis Tree sync** — calibrate once at the tree; estimate stays aligned
- **Breach popups** — reads on-screen `+N Hiveblood` gains between tree visits
- **Overlay** — show near inventory gold line or fixed screen position
- **Position dummy** — drag to place, then disable (like PlayerBuffBar)
- **Cap warning** — above threshold (default 95k): orange blinking text, visible even with inventory closed
- **Session gains** — optional `+N since tree` line
- **DE/EN** — settings via GameHelper localization

## Requirements

- [GameHelper2](https://github.com/Gordin/GameHelper2) source tree
- **.NET 10 SDK** (`net10.0-windows`, x64)
- Path of Exile 2 (Genesis Tree + Hiveblood mechanic)

## Build & install

```bash
git clone https://github.com/Gordin/GameHelper2.git
cd GameHelper2
git clone https://github.com/MordWraith/Hiveblood.git Plugins/Hiveblood
dotnet build ExileCenter/ExileCenter.csproj -c Release
dotnet build Plugins/Hiveblood/Hiveblood.csproj -c Release
```

Enable **Hiveblood** under GameHelper → Plugins. Visit the **Genesis Tree** once to calibrate.

### Config (runtime)

| Path | Purpose |
|------|---------|
| `Plugins/Hiveblood/config/settings.txt` | Overlay, colors, cap threshold, tracker state (JSON) |

## Usage notes

- Overlay defaults to **inventory open only**; cap warning overrides and shows while near 100k
- **Reset tracker** in settings if you want a fresh baseline
- No memory scanning — UI text only (Genesis Tree + Breach popups)

## Credits

- Plugin author: **MordWraith**
- Host: [Gordin/GameHelper2](https://github.com/Gordin/GameHelper2)

## Disclaimer

Third-party plugin — use at your own risk. Read-only overlay; no automation. Not affiliated with Grinding Gear Games.

## Support

- Plugin feedback: Discord `#plugins` forum thread
- Install/crashes: `#help` with tag **`plugin`**

## Version history

| Version | Notes |
|---------|--------|
| **1.0.1** | Throttled UI scans (less CPU); reusable read buffers |
| **1.0.0** | Genesis Tree sync, Breach popups, cap warning, position dummy |
