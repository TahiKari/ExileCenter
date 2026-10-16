# Historical upstream sync (Gordin GameHelper2)

> Archive provenant de la base GameHelper. ExileCenter est développé dans `TahiKari/ExileCenter` ; ce document décrit une synchronisation antérieure et ne prescrit pas de synchronisation automatique. Pour compiler la version actuelle sans reconstruire GameOffsets, suivre le README ExileCenter.

Stable keeps **Radar**, **AutoHotKeyTrigger**, and **HealthBars** aligned with [Gordin/GameHelper2](https://github.com/Gordin/GameHelper2) `main`. Launcher, updater, Atlas fork, optional plugins, and localization stay MordWraith-specific.

## Reference

| Item | Value |
|------|--------|
| Upstream remote | `gordin` → `https://github.com/Gordin/GameHelper2` |
| Last sync commit | `24ac9c8` (2026-06) |
| Prior stable baseline | pre-sync stable `4077ad3` |

## Pulled in this sync

### Plugins (from `gordin/main`)

- **Radar** — Abyss/Breach paths, reached-path hiding, Runestone socket count fix, co-op settings, default icon updates
- **AutoHotKeyTrigger** — upstream conditions/templates/minion logic
- **HealthBars** — hide when skill tree / other windows open

### Core (minimal, required by Radar / chat)

- `StateMachine.TryGetRuneStationSocketCount` (Runestone socket count)
- `AreaInstance.ScanSleepingEntities` (Abyss scan for Radar)
- `ChatParentUiElement` — chat-active via UI flags (not background alpha)
- `UiElementBase.flags` → `protected` (for chat fix)

## Not merged (intentional)

| Area | Reason |
|------|--------|
| **Atlas** plugin + core Atlas APIs | Large fork-specific rewrite on Stable; merge separately |
| Universal font / WorldArea data in core | Tied to upstream Atlas layout |
| Discord URL in settings | Upstream points to Gordin server |
| Plugin store / launcher / publish scripts | Stable-only |
| `AhkKeySender`, `TemplateUi`, Radar `L()` UI, boss-arena default restore | Removed to match upstream; AHK uses upstream `MiscHelper.KeyUp` (WM_KEYUP) |

## Évolution actuelle

Les adaptations ExileCenter ne sont pas remplacées par une copie globale de fichiers amont. Toute reprise de correction doit faire l'objet d'un patch ciblé et d'une validation isolée. Le binaire validé GameOffsets reste inchangé.

## Test checklist

### Radar

- [ ] Minimap / large map icons
- [ ] Runestone encounter + socket count + “hide when near” toggle
- [ ] Ritual / Breach / Abyss icons and path lines
- [ ] Reached paths hidden when enabled
- [ ] Co-op map centering
- [ ] Settings save/load

### AutoHotKeyTrigger

- [ ] Profiles and rules load
- [ ] Minion command, nearby monsters, status effects, vitals

### HealthBars

- [ ] Hidden in skill tree / when blocking UI open

### Core

- [ ] AHK does not fire keys while chat is focused
