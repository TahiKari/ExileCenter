# Crédits — ExileCenter

**ExileCenter** est maintenu par [TahiKari](https://github.com/TahiKari) dans un dépôt GitHub autonome. Son code dérive de la lignée [GameHelper / GameHelper2](https://github.com/MordWraith/Gamehelper) : cette origine et les contributions antérieures restent créditées ci-dessous.

| What | Where |
|------|--------|
| **Sources ExileCenter** | https://github.com/TahiKari/ExileCenter |
| **Releases ExileCenter** | https://github.com/TahiKari/ExileCenter/releases |
| **Trust / auto-update** | [SECURITY.md](SECURITY.md) |

**Mainteneur ExileCenter :** [TahiKari](https://github.com/TahiKari). **Base antérieure :** [MordWraith](https://github.com/MordWraith).

If someone is missing from this list, please open an issue or contact the maintainer.

## In the application

Open **Plugins** in the ExileCenter menu. The plugin table preserves each plugin name and its **Author** (original / upstream credit).

## Core & launcher

| Component | Credit | Notes |
|-----------|--------|-------|
| Fork basis | Lafko / Gordin | Starting point for this project (GameHelper2 lineage) |
| GameHelper2 base | [Gordin](https://github.com/MordWraith/Gamehelper) and community | Overlay architecture, offsets, plugin host |
| GameHelper (original) | GameHelper (OwnedCore) | Early PoE helper lineage |
| Community maintainers | KronosDesign, arsenic2k, abevol, mm3141, others | GameHelper2 contributions |
| Upstream fork | MordWraith | PoE2 updates, launcher, signed auto-update, plugins, publish pipeline |
| ExileCenter / previous GameHelper P-V1 builds | TahiKari | Independent distribution, interface and plugin themes, local and GitHub updates, loot and damage tracking adaptations, Sekhema attempt timers and integrated plugin changes |

## Bundled plugins

| Plugin | Author / upstream | Notes |
|--------|-------------------|-------|
| Atlas | Nekkoy / [yokkenUA](https://github.com/MordWraith/Gamehelper) (synced **v0.1.4**) | Chevron routes, hide available maps, expedition targets, Grand Mirror badge, content icons |
| Atlas zoom-out research | [syrairc/ExileMaps — AtlasHacks.cs](https://github.com/syrairc/ExileMaps/blob/main/AtlasHacks.cs) | Atlas-only clamp signatures; independently implemented validation, limited zoom range and restoration |
| Radar | Gordin | GameHelper2 |
| RitualHelper | caio | Based on AutoRitualPricer ([Queuete/GameHelper](https://github.com/MordWraith/Gamehelper) lineage) |
| RuneforgeHelper | Nekkoy / [yokkenUA](https://github.com/MordWraith/Gamehelper) | Runeshape rewards overlay |
| SekhemaHelper | Nekkoy / [yokkenUA](https://github.com/MordWraith/Gamehelper) (synced **v0.0.3**) | Sekhema Trial path helper; room read from FloorData content vector |
| AutoPot | MordWraith | Written for this fork |
| AutoHotKeyTrigger | GameHelper2 upstream | Bundled with GameHelper2 |
| HealthBars | GameHelper2 upstream | Bundled with GameHelper2 |
| SimpleBars | [Reynbow](https://github.com/MordWraith/Gamehelper) | Simpler HealthBars fork (gradient bars, circle-dot mode) |
| PreloadAlert | GameHelper2 upstream | Concept from [TehCheat/PreloadAlert](https://github.com/MordWraith/Gamehelper) |
| AuraTracker | Skrip / [derekShaheen](https://github.com/MordWraith/Gamehelper) | Nearby enemy list with HP/ES, buffs, DPS |
| MapKillCounter | MordWraith | Per-map monster kill counts |
| AmanamuVoidAlert | [1k4ru5g3](https://github.com/MordWraith/Gamehelper) | Abyss / Amanamu void cloud tracker |
| PlayerBuffBar | MordWraith | Player buff watchlist, charges, rage |
| Hiveblood | MordWraith | Genesis Tree Hiveblood tracker (PoE2) |
| FarmTracker | Senbry / [MordWraith](https://github.com/MordWraith/Gamehelper) | Farm session tracker (loot, maps, kills, div/h) |

## Third-party libraries

Includes (non-exhaustive): ImGui.NET, ClickableTransparentOverlay, Newtonsoft.Json, Coroutine, GameOffsets, AsmResolver, Vortice, SixLabors.ImageSharp, and NuGet dependencies listed in each project's `.csproj`.

CorpseCleaner uses [LibGGPK3 / LibBundle3 / LibBundledGGPK3](https://github.com/aianlinb/LibGGPK3), version 2.7.5, by aianlinb, under AGPLv3. The plugin's source is distributed under AGPLv3; its license is included in `Plugins/CorpseCleaner/LICENSE`. The native Oodle library is supplied locally by the user and is not distributed in GameHelper packages.

## License

Upstream GameHelper2 is GPLv3. ExileCenter preserves that license — see [LICENSE](LICENSE). Component-specific notices, including CorpseCleaner's AGPLv3 license, remain applicable.
