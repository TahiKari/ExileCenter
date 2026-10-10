# GameHelper

GameHelper is a Windows x64 .NET overlay for **Path of Exile 2** with a plugin architecture. The launcher (`GameHelper.exe`) checks for updates, starts the overlay (`GameHelper.App.exe`), reads data from the running game process, and loads plugins from the `Plugins` folder.

**Open-source fork maintained by** [MordWraith](https://github.com/MordWraith) — basis **Lafko / Gordin** ([GameHelper2](https://github.com/MordWraith/Gamehelper)).

## Source + binaries (same project)

| Channel | For whom | Link |
|---------|----------|------|
| **`main` branch** | Developers, auditors, no-auto-update users | https://github.com/MordWraith/Gamehelper |
| **Releases** | Players (installer / ZIP) | https://github.com/MordWraith/Gamehelper/releases |
| **Auto-update** | Optional convenience (signed ZIP from Releases) | Built into `GameHelper.exe` |

- **Do not want auto-update?** Use the [full ZIP](https://github.com/MordWraith/Gamehelper/releases/latest) or [build from source](#build-from-source).
- **Trust / security:** [SECURITY.md](SECURITY.md) — signed manifests, what gets updated, what stays local.
- **Windows Defender blocked GameHelper?** See [SECURITY.md → false positives](SECURITY.md#windows-defender-and-antivirus-false-positives) — common with auto-update and unsigned DLLs; usually not a real trojan.
- **Attribution:** [CREDITS.md](CREDITS.md) and in-app **Plugins → Author** column.

## Download (players)

| What | Link |
|------|------|
| **Installer (recommended)** | https://github.com/MordWraith/Gamehelper/releases/latest/download/GameHelperDownloader.exe |
| Full ZIP (manual install) | https://github.com/MordWraith/Gamehelper/releases/latest/download/GameHelper-*-full.zip |
| All releases | https://github.com/MordWraith/Gamehelper/releases/latest |

Run `GameHelperDownloader.exe` in an **empty folder**. Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### Release assets

| Asset | Purpose |
|-------|---------|
| `GameHelperDownloader.exe` | One-file fresh install |
| `GameHelper-*-full.zip` | Full install / auto-update package |
| `manifest.json` + `manifest.sig` | Signed update metadata (SHA256 per file) |
| `changelog-history.json` | In-app changelog history |

User settings (`configs/`, `Plugins/*/config/`) are **not** included in update packages and are **not** overwritten by auto-update.

## Build from source

### Required tools

- [Visual Studio](https://visualstudio.microsoft.com/downloads/) with **.NET desktop development**
- [.NET 10 SDK for Windows x64](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
git clone https://github.com/MordWraith/Gamehelper
cd Gamehelper
powershell -ExecutionPolicy Bypass -File scripts\build.ps1
```

Output: `publish\` (run `GameHelper.exe` from there).

Open [`GameOverlay.sln`](GameOverlay.sln) for IDE development — not a single `.csproj` only.

### Maintainer: publish

```powershell
# Release-Binaries hochladen (Quellcode wird NICHT automatisch committed):
powershell -ExecutionPolicy Bypass -File rebuild-and-publish.ps1

# Optional: alten Auto-Push-Quellcode-Flow (nicht empfohlen):
powershell -ExecutionPolicy Bypass -File rebuild-and-publish.ps1 -PushSource

# Quellcode manuell committen (bevorzugt): normales git add/commit/push mit Messages wie [Core] ...
```

Source commits should be descriptive (`[Core] …`, `[Radar] …`), not bulk `Release vX source` snapshots.

## Project layout

| Path | Role |
|------|------|
| `GameHelper/` | Overlay (`GameHelper.App.exe`) |
| `Launcher/` | Launcher & updater (`GameHelper.exe`) |
| `Downloader/` | Standalone installer EXE |
| `GameOffsets/` | Game structure offsets |
| `Plugins/` | Atlas, Radar, AutoPot, … |
| `scripts/` | Build & publish automation |
| `CREDITS.md` | Attribution |
| `SECURITY.md` | Auto-update & trust |

Target: `net10.0-windows`, `win-x64`. License: [GPLv3](LICENSE).

## Solution projects

- `GameHelper` — main overlay
- `Launcher` — updater entry point
- `Downloader` — public installer
- `GameOffsets` — offsets
- Plugins: `Atlas`, `AuraTracker`, `AutoPot`, `AutoHotKeyTrigger`, `HealthBars`, `SimpleBars`, `MapKillCounter`, `PreloadAlert`, `Radar`, `RitualHelper`, `RuneforgeHelper`, `SekhemaHelper`, `PlayerBuffBar`, `AmanamuVoidAlert`, …

## Run

1. Open `publish\` or `GameHelper\bin\Release\net10.0-windows\win-x64\`
2. Start **`GameHelper.exe`** (not `GameHelper.App.exe` directly)
3. Match admin elevation with the game if needed

## Runtime data (not in git)

```
configs\core_settings.json
configs\plugins.json
Plugins\<Name>\config\
```

## Troubleshooting

**`net10.0-windows` not supported** — Install .NET 10 SDK and update Visual Studio.

**Plugins missing after build** — Use **Rebuild Solution**, not Build Project.

**Update pulled wrong version** — Install into an **empty** folder; check `VERSION.txt`.

**Overlay does not attach** — Match admin elevation with the game.

**Windows Defender / helper won't start** — Open Protection history, allow blocked `GameHelper` / `GameHelperUpdate` entries, or install from the [full ZIP](https://github.com/MordWraith/Gamehelper/releases/latest) into a new folder. Details: [SECURITY.md](SECURITY.md#windows-defender-and-antivirus-false-positives).

## Personal local updates

The personal build checks `Desktop\Gamehelpers\Maj GameHelper\GameHelper-Maj.json`
at startup and every five seconds without pausing the game. **Updates** in the
settings menu installs a verified package and restarts GameHelper. The official
launcher is bypassed when this local folder or an installed local-update state
exists. Personal packages also replace `GameHelper.App.exe` with an identical
copy of the core apphost (`GameHelper.exe`), which loads `GameHelper.dll` directly.
Existing shortcuts to either executable therefore open the personal build rather
than the official updater. Keep this alias in every local update and bootstrap
archive; omitting it leaves old official launchers active in existing folders.
Configuration folders and plugin `settings.json` files are never packaged.
Failed file replacement restores the previous files before restarting.

After a requested change, build only the affected projects, check the result, then
publish it to this folder. Do not publish intermediate or unverified builds:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1 -LocalUpdate -SkipBuild -LocalLabel "Description de la modification"
```

`-LocalSource` can point to a prepared complete runtime directory; the default is
the Release output of GameHelper. `-LocalDestination` supports an isolated folder
for checking publication. The ZIP is copied and verified before its manifest is
atomically published. Updating never overwrites inventory protections, stash
destinations or other saved settings. The first installation of this updater is
manual; subsequent packages use the in-app button.

## Links

- [Upstream GameHelper2](https://github.com/MordWraith/Gamehelper)
- [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0)
