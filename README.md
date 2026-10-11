# GameHelper P-V1

La version de **TahiKari** pour Path of Exile 2 : **P** pour « personnalisé », **V1** pour « version 1 ».

## ⬇️ [Télécharger GameHelper P-V1 pour Windows x64](https://github.com/TahiKari/Gamehelper2/releases/latest/download/GameHelper-Personnalise-win-x64.zip)

**[Voir la dernière version et ses nouveautés](https://github.com/TahiKari/Gamehelper2/releases/latest)** · [Toutes les versions](https://github.com/TahiKari/Gamehelper2/releases)

1. Télécharger le ZIP avec le lien ci-dessus.
2. Extraire son contenu dans un dossier.
3. Lancer `GameHelper.exe` (runtime **.NET 10 Desktop x64** requis).

Après cette première installation, les nouvelles mises à jour sont proposées au démarrage avec le choix **Oui/Non**, ou signalées dans l'application déjà ouverte.

Le ZIP garde le nom `GameHelper-Personnalise-win-x64.zip` pour que les versions déjà installées puissent continuer à recevoir les mises à jour.
La procédure de publication est décrite dans la section « Releases de GameHelper P-V1 » ci-dessous. Les informations historiques de l'amont sont conservées.

GameHelper is a Windows x64 .NET overlay for **Path of Exile 2** with a plugin architecture. The launcher (`GameHelper.exe`) checks for updates, starts the overlay (`GameHelper.App.exe`), reads data from the running game process, and loads plugins from the `Plugins` folder.

**Open-source fork maintained by** [MordWraith](https://github.com/MordWraith) — basis **Lafko / Gordin** ([GameHelper2](https://github.com/MordWraith/Gamehelper)).

## Source + binaries (original upstream project)

Pour **GameHelper P-V1**, utiliser le bouton de téléchargement en haut de cette page. Les liens ci-dessous concernent le projet d'origine.

| Channel | For whom | Link |
|---------|----------|------|
| **`main` branch** | Developers, auditors, no-auto-update users | https://github.com/MordWraith/Gamehelper |
| **Releases** | Players (installer / ZIP) | https://github.com/MordWraith/Gamehelper/releases |
| **Auto-update** | Optional convenience (signed ZIP from Releases) | Built into `GameHelper.exe` |

- **Do not want auto-update?** Use the [full ZIP](https://github.com/MordWraith/Gamehelper/releases/latest) or [build from source](#build-from-source).
- **Trust / security:** [SECURITY.md](SECURITY.md) — signed manifests, what gets updated, what stays local.
- **Windows Defender blocked GameHelper?** See [SECURITY.md → false positives](SECURITY.md#windows-defender-and-antivirus-false-positives) — common with auto-update and unsigned DLLs; usually not a real trojan.
- **Attribution:** [CREDITS.md](CREDITS.md) and in-app **Plugins → Author** column.

## Download (original upstream project)

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

## Releases de GameHelper P-V1 (GitHub Actions)

Le workflow `.github/workflows/release.yml` compile le cœur et les plugins sur
une machine Windows standard gratuite. Il fonctionne uniquement dans le dépôt
**public** `TahiKari/Gamehelper2` ; il ne démarre pas si le dépôt devient privé.
Il n'utilise ni machine payante, ni cache Actions, ni stockage d'artefacts Actions.
Le ZIP et son SHA256 sont joints directement à une GitHub Release.

Pour l'activer, envoyer les sources personnalisées et les fichiers `.github`
dans ce dépôt, puis créer un tag tel que `v1.5.11-personnalise.1` sur le commit
contenant ces sources. L'envoi du tag déclenche compilation puis préparation d'une
Release en brouillon, à publier après vérification.
Pour une préparation manuelle, utiliser **Actions → GameHelper - compilation et
Release gratuite → Run workflow**, avec un tag existant : l'option brouillon est
activée par défaut. Une Release existante n'est pas remplacée : utiliser un nouveau
tag. Une compilation échouée ne publie rien. Le premier passage sur GitHub doit
être vérifié avant de diffuser le lien aux joueurs.

La copie `.github/ci/GameOffsets.dll` est le binaire validé, conservé à l'identique
(SHA256 `EF67411C6456DE8E92179ED8DB55ADC043D086B072ED37AE52E1109F966F9B93`).
`PreserveOffsets.targets`, importé uniquement par le workflow, remplace les
références au projet GameOffsets par ce binaire et interdit sa compilation.
Les compilations locales habituelles ne sont pas modifiées.

Le ZIP contient les exécutables, plugins, dépendances, ressources, licence et
crédits ; il exclut les réglages personnels et diagnostics. Extraire dans un
dossier et lancer `GameHelper.exe` avec le runtime **.NET 10 Desktop x64** installé.
Les deux exécutables démarrent directement GameHelper P-V1. Au démarrage,
l'application propose les mises à jour avec le choix Oui/Non. Pendant son utilisation,
elle vérifie les Releases publiques toutes les 10 minutes et affiche une notification.
Le bouton « Mettre à jour et redémarrer » télécharge le ZIP depuis ce dépôt,
vérifie son SHA256 et conserve les réglages. Le dossier de mises à jour locales
reste prioritaire. Une connexion indisponible n'empêche pas de démarrer GameHelper.
GameOffsets.dll est vérifiée et ne fait jamais partie des fichiers remplacés.
La version installée est enregistrée dans les métadonnées de compilation à partir
du tag, pour éviter les réinstallations et les retours à une ancienne Release.
Les utilisateurs des anciens ZIP doivent installer une première fois la version
qui ajoute cette fonction ; les mises à jour suivantes sont proposées dans l'application.
Téléchargements : <https://github.com/TahiKari/Gamehelper2/releases>.

## Links

- [Upstream GameHelper2](https://github.com/MordWraith/Gamehelper)
- [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0)
