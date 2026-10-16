# AuraTracker

Community plugin for [GameHelper2](https://github.com/Gordin/GameHelper2) (Path of Exile 2).

**Nearby enemy panel**: HP/ES bars, buff icons, per-target and total **DPS**, configurable layout. Based on **Skrip**'s AuraTracker; GameHelper2 port with German/English settings UI by MordWraith. Read-only overlay — **build from source**.

## Features

- Lists nearby monsters with life/energy shield bars
- Buff/debuff icons per enemy (configurable max count)
- **DPS tracking** (smoothing, overall DPS header)
- **Filters**: optional tamable-beast-only list + aura/buff name filters (any or all match)

## Requirements

- A working [GameHelper2](https://github.com/Gordin/GameHelper2) source tree
- **.NET 10 SDK** (`net10.0-windows`, x64)

## Build & install

```bash
git clone https://github.com/Gordin/GameHelper2.git
cd GameHelper2
git clone https://github.com/MordWraith/AuraTracker.git Plugins/AuraTracker
dotnet build ExileCenter/ExileCenter.csproj -c Release
dotnet build Plugins/AuraTracker/AuraTracker.csproj -c Release
```

Enable **AuraTracker** in GameHelper → Plugins.

### Config folder

| Path | Purpose |
|------|---------|
| `Plugins/AuraTracker/config/AuraTracker.settings.json` | Panel layout, colors, DPS options |

## Credits

- Original: **Skrip** — [derekShaheen/AuraTracker](https://github.com/derekShaheen/AuraTracker)
- GameHelper2 port: **MordWraith**

## Disclaimer

Third-party plugin — use at your own risk. Read-only overlay; no automation.

## Version history

| Version | Notes |
|---------|--------|
| **1.0.1** | Beast + aura filters (Spirit Walker / Tame Beast farming; DE/EN UI) |
| **1.0.0** | Initial community GameHelper2 release (fork port, DE/EN UI) |
