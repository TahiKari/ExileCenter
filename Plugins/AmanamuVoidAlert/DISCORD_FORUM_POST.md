# Discord forum — first post (copy into `#plugins`)

**Title:** `AmanamuVoidAlert`

**Tags:** `community`

---

## AmanamuVoidAlert

**Author:** 1k4ru5g3 (GameHelper2 port: MordWraith)  
**Source:** https://github.com/MordWraith/AmanamuVoidAlert  
**Compatible with:** [Gordin/GameHelper2](https://github.com/Gordin/GameHelper2) `main`, **.NET 10** (`net10.0-windows`, x64). Works on maintained community forks on the same API/target.

### What it does (PoE2)

Abyss / **Amanamu void** helper for GameHelper:

- Tracks **Lightless** abyss monsters (void cloud mechanics)
- **On-screen labels** and **off-screen arrows** (inside vs outside cloud colors)
- Optional circles, rare/unique filter, distance / forget timers
- Debug window for troubleshooting detections

Read-only overlay — no input automation.

### Install (source only — no prebuilt DLL)

1. Clone [GameHelper2](https://github.com/Gordin/GameHelper2)
2. Clone this plugin into `Plugins/AmanamuVoidAlert`:
   ```
   git clone https://github.com/MordWraith/AmanamuVoidAlert.git Plugins/AmanamuVoidAlert
   ```
3. Build:
   ```
   dotnet build ExileCenter/ExileCenter.csproj -c Release
   dotnet build Plugins/AmanamuVoidAlert/AmanamuVoidAlert.csproj -c Release
   ```
4. Enable **AmanamuVoidAlert** in GameHelper → Plugins

Full README: https://github.com/MordWraith/AmanamuVoidAlert

### Config folder

- `config/settings.txt` — plugin settings (JSON)

### v1.0.0

- Initial GameHelper2 community release (port from PoeFixer plugin lineage)

### Support

- Feedback / feature requests: **this thread**
- Crashes / GameHelper install issues: `#help` with tag **`plugin`**

### Disclaimer

Community third-party plugin. Use at your own risk. Not affiliated with GGG or PoeFixer. Only install from trusted sources.

---

## Update checklist (for you)

When releasing a new version, edit this first post:

1. Bump version in `AmanamuVoidAlert.csproj` and README version table
2. Add bullet under **vX.Y.Z** with changes
