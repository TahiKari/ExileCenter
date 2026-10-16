# AmanamuVoidAlert

Community plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) (Path of Exile 2).

Tracks **Abyss / Amanamu void** mechanics: Lightless monsters, inside/outside void cloud state, on-screen labels, and off-screen arrows. Ported from **1k4ru5g3**'s PoeFixer plugin; maintained by MordWraith for community forks. Read-only overlay — **build from source**.

## Features

- Detects Abyss Lightless monsters (mod + buff heuristics)
- **On-screen labels** and optional **off-screen arrows**
- Circle markers, rare/unique filter, distance / forget timers
- Optional debug window and detection logging

## Requirements

- A working [GameHelper2](https://github.com/Gordin/GameHelper2) source tree
- **.NET 10 SDK** (`net10.0-windows`, x64)

## Build & install

```bash
git clone https://github.com/Gordin/GameHelper2.git
cd GameHelper2
git clone https://github.com/MordWraith/AmanamuVoidAlert.git Plugins/AmanamuVoidAlert
dotnet build ExileCenter/ExileCenter.csproj -c Release
dotnet build Plugins/AmanamuVoidAlert/AmanamuVoidAlert.csproj -c Release
```

Enable **AmanamuVoidAlert** in GameHelper → Plugins.

### Config folder

| Path | Purpose |
|------|---------|
| `Plugins/AmanamuVoidAlert/config/settings.txt` | Plugin settings (JSON) |

## Credits

- Original: **1k4ru5g3** — [AmanamuVoidAlertPlugin](https://github.com/1k4ru5g3/AmanamuVoidAlertPlugin)
- GameHelper2 port: **MordWraith**

## Disclaimer

Third-party plugin — use at your own risk. Read-only overlay; no automation. Comply with GGG terms of service.

## Version history

| Version | Notes |
|---------|--------|
| **1.0.0** | Initial community GameHelper2 release (fork port) |
