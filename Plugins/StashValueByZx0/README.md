# StashValue — GameHelper2 Plugin

**Pricing overlay plugin for Path of Exile 2.** Automatically evaluates and displays the market value of items inside your Stash and Inventory using real-time pricing data.

## What it does

- 💸 **Live Pricing**: Fetches background market prices using **poe2scout.com** or **poe.ninja**.
- 🎨 **Visual Overlay**: Draws clean pill-chip overlays directly on top of item slots.
- 📦 **Stash & Inventory pricing**: Separate configurable toggles to display prices on Stash slots, Inventory slots, or both.
- ⚙️ **Aesthetic Customization**: Easily configure font scale, color, display currency (Divine, Exalted, Chaos), and positional offsets.
- 🛡️ **Zero-Sync Lag**: Locates slots by walking the visible UI panel tree directly, ensuring pricing overlays update immediately without relying on slow or out-of-sync ServerData mappings.
- 🛠️ **Diagnostics**: Includes a debug diagnostic window and bounding box outlines to audit item entity addresses.

## Compatibility

| Attribute | Detail |
|---|---|
| **Game** | Path of Exile 2 (0.5.x+) |
| **GameHelper** | MordWraith Fork (.NET 10.0-windows) |
| **Tag** | `community`, `wip` |

## Install & Build

### Option A: Using Pre-compiled Release (Recommended)
1. Download `StashValue.dll` from the [Releases](https://github.com/zx0CF1/StashValue/releases) section.
2. Create a folder named `StashValue` inside your GameHelper `Plugins/` directory.
3. Place the downloaded `StashValue.dll` inside that folder:
   ```
   <GameHelper>\
     Plugins\
       StashValue\
         StashValue.dll
   ```
4. Launch GameHelper and enable **StashValue** in the plugins tab.

### Option B: Build from Source
If you prefer to compile the plugin yourself:
1. Clone or download this repository directly into the `Plugins/` folder of your GameHelper codebase:
   ```
   <GameHelper>\
     GameHelper\
       GameHelper.csproj
     Plugins\
       StashValue\           ← Clone this repository here
         StashValue.csproj
   ```
2. Open the main GameHelper solution (`ExileCenter.sln`) in Visual Studio, Rider, or compile via .NET CLI:
   ```powershell
   dotnet build -c Release
   ```
3. The `.csproj` is configured to automatically copy the compiled `StashValue.dll` into the local GameHelper build directory.

## Configuration & Saving

Open GameHelper → Plugins → StashValue:
- Settings are saved automatically to `config/settings.txt` inside the plugin directory whenever you adjust a toggle or slider in the UI.

## Credits & Upstream Attribution

This plugin was developed by **zx0CF1** through collaborative AI assistance (vibe-coding / prompt engineering) with acknowledgments to:
- **LootValue** (by **Gordin**, GameHelper2 upstream) for the overlay concept and visual reference.
- **RitualHelper** (originally by **caio** / *AutoRitualPricer*) for the mod parsing and price fetcher architecture.
