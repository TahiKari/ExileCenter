# Discord forum — first post (copy into `#plugins`)

**Title:** `AuraTracker`

**Tags:** `community`

---

## AuraTracker

**Author:** Skrip (GameHelper2 port: MordWraith)  
**Source:** https://github.com/MordWraith/AuraTracker  
**Upstream:** https://github.com/derekShaheen/AuraTracker  
**Compatible with:** [Gordin/GameHelper2](https://github.com/Gordin/GameHelper2) `main`, **.NET 10** (`net10.0-windows`, x64). Works on maintained community forks on the same API/target.

### What it does (PoE2)

**Nearby enemy tracker** panel for GameHelper:

- HP / ES bars for monsters near you
- Buff / debuff icons per target (configurable limit)
- **DPS** per enemy and optional **total DPS** header
- Draggable panel, fonts, filters — German/English settings UI in this fork

Read-only overlay — no input automation.

### Install (source only — no prebuilt DLL)

1. Clone [GameHelper2](https://github.com/Gordin/GameHelper2)
2. Clone this plugin into `Plugins/AuraTracker`:
   ```
   git clone https://github.com/MordWraith/AuraTracker.git Plugins/AuraTracker
   ```
3. Build:
   ```
   dotnet build ExileCenter/ExileCenter.csproj -c Release
   dotnet build Plugins/AuraTracker/AuraTracker.csproj -c Release
   ```
4. Enable **AuraTracker** in GameHelper → Plugins

Full README: https://github.com/MordWraith/AuraTracker

### Config folder

- `config/AuraTracker.settings.json` — panel layout, colors, DPS options

### v1.0.0

- Initial GameHelper2 community release (fork port, DE/EN settings)

### Support

- Feedback / feature requests: **this thread**
- Crashes / GameHelper install issues: `#help` with tag **`plugin`**

### Disclaimer

Community third-party plugin. Use at your own risk. Not affiliated with GGG. Only install from trusted sources.

---

## Update checklist (for you)

When releasing a new version, edit this first post:

1. Bump version in `AuraTracker.csproj` and README version table
2. Add bullet under **vX.Y.Z** with changes
