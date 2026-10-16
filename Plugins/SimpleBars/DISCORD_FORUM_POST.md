# Discord forum — first post (copy into `#plugins`)

**Title:** `SimpleBars`

**Tags:** `community`

---

## SimpleBars

**Author:** Reynbow (GameHelper2 port: MordWraith)  
**Source:** https://github.com/MordWraith/SimpleBars  
**Upstream:** https://github.com/Reynbow/simplebars  
**Compatible with:** [Gordin/GameHelper2](https://github.com/Gordin/GameHelper2) `main`, **.NET 10** (`net10.0-windows`, x64). Works on maintained community forks on the same API/target.

### What it does (PoE2)

Lightweight on-screen **health bars** for GameHelper:

- Textured HP bars over monsters (gradient / circle-dot modes)
- POI monster configs, town/hideout toggles
- **Disable the built-in HealthBars plugin** when using SimpleBars

Read-only overlay — no input automation.

### Install (source only — no prebuilt DLL)

1. Clone [GameHelper2](https://github.com/Gordin/GameHelper2)
2. Clone this plugin into `Plugins/SimpleBars`:
   ```
   git clone https://github.com/MordWraith/SimpleBars.git Plugins/SimpleBars
   ```
3. If `Textures/` is empty, copy bar PNGs from [Reynbow/simplebars](https://github.com/Reynbow/simplebars/tree/main/Textures)
4. Build:
   ```
   dotnet build ExileCenter/ExileCenter.csproj -c Release
   dotnet build Plugins/SimpleBars/SimpleBars.csproj -c Release
   ```
5. Enable **SimpleBars** in GameHelper → Plugins

Full README: https://github.com/MordWraith/SimpleBars

### Config folder

- `config/settings.txt` — bar display options (JSON)
- `Textures/` — required PNG bar assets

### v1.0.0

- Initial GameHelper2 community release (fork port)

### Support

- Feedback / feature requests: **this thread**
- Crashes / GameHelper install issues: `#help` with tag **`plugin`**

### Disclaimer

Community third-party plugin. Use at your own risk. Not affiliated with GGG. Only install from trusted sources.

---

## Update checklist (for you)

When releasing a new version, edit this first post:

1. Bump version in `SimpleBars.csproj` and README version table
2. Add bullet under **vX.Y.Z** with changes
