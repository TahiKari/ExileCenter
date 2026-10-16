# SimpleBars

Community plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) (Path of Exile 2).

Lightweight on-screen **health bars** (gradient bars, circle-dot mode, POI monsters). Fork of **Reynbow/simplebars**, integrated for GameHelper2 by MordWraith. Disable the built-in **HealthBars** plugin when using this. Read-only overlay — **build from source**.

## Features

- Custom textured HP bars over monsters (and self)
- Town/hideout/background toggles, position interpolation
- POI monster configs, graduation lines, circle-dot mode
- **Textures** in `Textures/` (`full_bar.png`, `hollow_bar.png`, …)

## Requirements

- A working [GameHelper2](https://github.com/Gordin/GameHelper2) source tree
- **.NET 10 SDK** (`net10.0-windows`, x64)
- **Texture files** — if `Textures/` is empty, copy from [Reynbow/simplebars](https://github.com/Reynbow/simplebars)

## Build & install

```bash
git clone https://github.com/Gordin/GameHelper2.git
cd GameHelper2
git clone https://github.com/MordWraith/SimpleBars.git Plugins/SimpleBars
# If Textures/ is missing:
#   copy from https://github.com/Reynbow/simplebars/tree/main/Textures
dotnet build ExileCenter/ExileCenter.csproj -c Release
dotnet build Plugins/SimpleBars/SimpleBars.csproj -c Release
```

Enable **SimpleBars** in GameHelper → Plugins (disable **HealthBars**).

### Config folder

| Path | Purpose |
|------|---------|
| `Plugins/SimpleBars/config/settings.txt` | Bar colors, POI, display options (JSON) |
| `Plugins/SimpleBars/Textures/` | Bar PNG textures (required) |

## Credits

- Original: **Reynbow** — [simplebars](https://github.com/Reynbow/simplebars)
- GameHelper2 port: **MordWraith**

## Disclaimer

Third-party plugin — use at your own risk. Read-only overlay; no automation.

## Version history

| Version | Notes |
|---------|--------|
| **1.0.0** | Initial community GameHelper2 release (fork port) |
