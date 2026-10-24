using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KikiMeter.controllers;
using AuraTracker.util;
using ExileCenter.Localization;
using ExileCenter.RemoteEnums;
using ExileCenter.Utils;
using ImGuiNET;

namespace KikiMeter.render;

internal sealed class DamageMeterRenderer
{
    private Vector2? damageMeterPosition;
    private bool damageMeterMoved;
    private Vector2? damageMeterSize;
    private bool comparisonMulti;
    private int mapMode;
    private string referenceMapId = "", comparedMapId = "";

    public bool RenderDamageMeter(DamageTracker meter, KikiMeterSettings settings, PluginLocalization text, Vector2 overlaySize, bool detached = false)
    {
        bool save = false;
        // MeterBody owns scrolling; outer scrollbars feed back into the frame's
        // padding and make its visible width and height alternate every frame.
        var flags = ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        if (detached)
        {
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(overlaySize);
            flags |= ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove;
        }
        else
        {
            var size = settings.DamageMeterSize;
            if (!float.IsFinite(size.X) || !float.IsFinite(size.Y)) size = new Vector2(460f, 420f);
            var minimum = Vector2.Min(new Vector2(320f, 220f), overlaySize);
            size = Vector2.Clamp(size, minimum, overlaySize);
            if (damageMeterSize != size) ImGui.SetNextWindowSize(size, ImGuiCond.Always);
            ImGui.SetNextWindowSizeConstraints(minimum, overlaySize);
            var position = settings.DamageMeterPosition;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) position = new Vector2(40f, 140f);
            var occupied = settings.DamageMeterCollapsed ? new Vector2(size.X, ImGui.GetFrameHeight()) : size;
            position = Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, overlaySize - occupied));
            if (damageMeterPosition != position) ImGui.SetNextWindowPos(position, ImGuiCond.Always);
            ImGui.SetNextWindowCollapsed(settings.DamageMeterCollapsed, ImGuiCond.Always);
            if (settings.DamageMeterLocked) flags |= ImGuiWindowFlags.NoMove;
        }
        ImGui.SetNextWindowBgAlpha(0.96f);
        var style = ImGui.GetStyle();
        var previousButtonPosition = style.WindowMenuButtonPosition;
        bool expanded;
        try
        {
            style.WindowMenuButtonPosition = ImGuiDir.Right;
            expanded = detached
                ? ExileCenter.Ui.ImGuiTheme.BeginFramedWindow("##AuraTrackerDesktop", "Kikimeter", flags)
                : ExileCenter.Ui.ImGuiTheme.BeginWindow(text.Title("meter.title", "Damage meter (estimated)", "AuraTrackerDamageMeter"), flags);
        }
        finally { style.WindowMenuButtonPosition = previousButtonPosition; }
        if (!detached)
        {
            bool collapsed = ImGui.IsWindowCollapsed();
            save = settings.DamageMeterCollapsed != collapsed;
            settings.DamageMeterCollapsed = collapsed;
            var occupied = collapsed ? new Vector2(ImGui.GetWindowWidth(), ImGui.GetFrameHeight()) : ImGui.GetWindowSize();
            var moved = Vector2.Clamp(ImGui.GetWindowPos(), Vector2.Zero, Vector2.Max(Vector2.Zero, overlaySize - occupied));
            if (moved != ImGui.GetWindowPos()) ImGui.SetWindowPos(moved);
            if (moved != settings.DamageMeterPosition || (!collapsed && ImGui.GetWindowSize() != settings.DamageMeterSize)) damageMeterMoved = true;
            settings.DamageMeterPosition = moved;
            if (!collapsed) settings.DamageMeterSize = ImGui.GetWindowSize();
            damageMeterPosition = moved;
            damageMeterSize = settings.DamageMeterSize;
            if (damageMeterMoved && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) { save = true; damageMeterMoved = false; }
        }
        try
        {
            if (expanded)
            {
                if (detached) ImGui.TextColored(ExileCenter.Ui.ImGuiTheme.Accent, text.T("meter.heading", "DAMAGE METER"));
                float rowRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
                ImGui.TextColored(meter.InCombat ? new Vector4(0.45f, 0.85f, 0.65f, 1f) : new Vector4(0.65f, 0.69f, 0.76f, 1f),
                    text.T(meter.InCombat ? "meter.active" : "meter.idle", meter.InCombat ? "In combat" : "Waiting"));
                string mode = text.T(detached ? "meter.game_mode" : "meter.desktop_mode", detached ? "Game screen" : "Another screen");
                if (ImGui.GetItemRectMax().X + ImGui.CalcTextSize(mode).X + style.FramePadding.X * 2 + style.ItemSpacing.X <= rowRight) ImGui.SameLine();
                if (ImGui.SmallButton(mode + "##MeterDisplayMode"))
                {
                    settings.DamageMeterSeparateWindow = !detached;
                    settings.DamageMeterWindowVisible = true;
                    save = true;
                }
                if (!detached)
                {
                    string lockLabel = text.T(settings.DamageMeterLocked ? "meter.unlock_short" : "meter.lock_short", settings.DamageMeterLocked ? "Move" : "Lock");
                    if (ImGui.GetItemRectMax().X + ImGui.CalcTextSize(lockLabel).X + style.FramePadding.X * 2 + style.ItemSpacing.X <= rowRight) ImGui.SameLine();
                    if (ImGui.SmallButton(lockLabel + "##LockDamageMeterPanel")) { settings.DamageMeterLocked = !settings.DamageMeterLocked; save = true; }
                    if (ImGui.IsItemHovered()) DrawDamageTooltip(text.T("meter.lock", "Lock panel position"));
                }
                if (ImGui.SmallButton(text.Label("meter.trial_button", "10-second test", "MeterTrial"))) meter.ArmTrial();
                ImGui.SameLine();
                if (ImGui.SmallButton(text.Label("meter.new_test", "New test", "MeterNewTest"))) meter.RestartMeasurement();
                if (meter.SkillChangeDetected)
                {
                    ImGui.TextWrapped(text.T("meter.skills_changed", "Available skills changed: a new measurement has started."));
                    if (ImGui.SmallButton(text.Label("meter.dismiss", "Dismiss", "MeterDismissChange"))) meter.DismissSkillChange();
                }                ImGui.Separator();
                if (ImGui.BeginChild("MeterBody", Vector2.Zero, ImGuiChildFlags.None))
                {
                    if (ImGui.BeginTabBar("DamageMeterTabs"))
                    {
                        if (ImGui.BeginTabItem(text.T("meter.live", "Live")))
                        {
                            save |= DrawDamageLive(meter, settings, text);
                            ImGui.EndTabItem();
                        }
                        if (ImGui.BeginTabItem(text.T("meter.compare", "Compare")))
                        {
                            save |= DrawDamageComparison(meter, settings, text);
                            ImGui.EndTabItem();
                        }
                        if (ImGui.BeginTabItem(text.T("meter.history_tab", "History")))
                        {
                            DrawDamageHistory(meter, text);
                            ImGui.EndTabItem();
                        }
                        if (ImGui.BeginTabItem(text.T("meter.maps", "Maps")))
                        {
                            save |= DrawDamageMaps(meter, settings, text);
                            ImGui.EndTabItem();
                        }
                        ImGui.EndTabBar();
                    }
                    ImGui.Separator();
                    ImGui.TextWrapped(text.T("meter.all_sources", "Observed damage · all sources. Individual party-member DPS is unavailable: damage attribution is not read."));
                }
                ImGui.EndChild();
            }
        }
        finally { ImGui.End(); }
        return save;
    }
    private static void DrawDamageMetric(string label, string value, Vector4 color)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(value);
        ImGui.PopStyleColor();
        ImGui.PopTextWrapPos();
    }

    private static bool DrawDamageLive(DamageTracker meter, KikiMeterSettings settings, PluginLocalization text)
    {
        bool save = false;
        ImGui.Spacing();
        ImGui.SetWindowFontScale(1.25f);
        DrawDamageMetric(text.T("meter.current", "Observed DPS · last second"), NumberFormatter.Format((long)meter.CurrentDps), new Vector4(0.45f, 0.85f, 0.65f, 1f));
        ImGui.SetWindowFontScale(1f);
        ImGui.TextWrapped(meter.CurrentTargets == 0 ? text.T("meter.no_target", "No targets hit (1 s)") :
            text.T(meter.CurrentTargets > 1 ? "meter.multi" : "meter.single", meter.CurrentTargets > 1 ? "Multi-target" : "Single-target")
            + " · " + meter.CurrentTargets + " " + text.T("meter.targets_short", "targets"));
        ImGui.Separator();
        if (meter.TrialArmed) ImGui.TextWrapped(text.T("meter.trial_armed", "Test ready: starts with the first observed damage."));
        else if (meter.TrialRunning)
        {
            ImGui.TextWrapped(text.T("meter.trial_running", "Fixed-duration test") + $" · {meter.TrialSeconds:0.0} / 10 s");
            ImGui.ProgressBar((float)(meter.TrialSeconds / 10), new Vector2(-1, 0));
        }
        else if (meter.TrialInterrupted) ImGui.TextWrapped(text.T("meter.trial_interrupted", "Test cancelled: target died, area changed, or reading was interrupted. Start again on an enemy that can survive 10 seconds."));
        else if (meter.TrialResult is { } trial)
        {
            DrawDamageMetric(text.T("meter.trial_result", "10-second test · average DPS"), NumberFormatter.Format((long)trial.Average), settings.DpsTextColor);
            ImGui.TextWrapped($"{trial.Targets} " + text.T("meter.targets_short", "targets"));
            if (ImGui.Button(text.Label("meter.save_reference", "Save as reference", "LiveTrialReference")))
                save |= StoreDamageReference(settings, trial, settings.DamageMeterTestName);
        }
        int columns = ImGui.GetContentRegionAvail().X >= 390 ? 2 : 1;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10, 8));
        if (ImGui.BeginTable("MeterLiveMetrics", columns, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn();
            DrawDamageMetric(text.T("meter.main_target", "Most-damaged target · DPS"), NumberFormatter.Format((long)meter.MainTargetDps), settings.DpsTextColor);
            if (ImGui.IsItemHovered()) DrawDamageTooltip(text.T("meter.main_target_hint", "Damage in the last second on the most-damaged target. An isolated single-target test requires only one enemy."));
            ImGui.TableNextColumn();
            DrawDamageMetric(text.T("meter.multi_dps", "All targets · DPS"), meter.CurrentTargets > 1 ? NumberFormatter.Format((long)meter.MultiTargetDps) : "—", new Vector4(0.48f, 0.69f, 1f, 1f));
            if (ImGui.IsItemHovered()) DrawDamageTooltip(text.T("meter.multi_hint", "Total damage in the last second across all targets; available when at least two enemies take damage."));
            ImGui.TableNextColumn();
            DrawDamageMetric(text.T("meter.average", "Combat average"), NumberFormatter.Format((long)meter.AverageDps), settings.DpsTextColor);
            ImGui.TableNextColumn();
            DrawDamageMetric(text.T("meter.peak", "Peak DPS (1 s)"), NumberFormatter.Format((long)meter.PeakDps), settings.DpsTextColor);
            ImGui.TableNextColumn();
            DrawDamageMetric(text.T("meter.total", "Observed damage"), NumberFormatter.Format(meter.TotalDamage), new Vector4(0.82f, 0.85f, 0.91f, 1f));
            ImGui.TableNextColumn();
            DrawDamageMetric(text.T("meter.duration", "Combat duration"), $"{meter.CombatSeconds:0.0} s", new Vector4(0.82f, 0.85f, 0.91f, 1f));
            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
        DrawMonsterCounts(meter.CombatEnemies, text);
        if (ImGui.CollapsingHeader(text.T("meter.live_guide", "Understand the measurements")))
        {
            ImGui.TextWrapped(text.T("meter.live_help", "Single-target: one enemy hit. Multi-target: two or more within one second. The average and peak cover the whole combat; after 3 seconds without damage, the combat ends."));
            ImGui.TextWrapped(text.T("meter.trial_help", "For a useful comparison: play solo, use the same durable enemy, the same buffs and one skill. Run a 10-second test, save the reference, change one setting and repeat. Idle time counts. Regeneration and unreadable damage can reduce the estimate; it is not a skill's theoretical DPS."));
        }
        return save;
    }
    internal static bool StoreDamageReference(KikiMeterSettings settings, DamageTracker.CombatSummary combat, string name)
    {
        if (combat.Seconds < 1 || combat.Targets < 1 || !double.IsFinite(combat.Average) || combat.Average <= 0) return false;
        var reference = new KikiMeterSettings.DamageMeterReference
        {
            Name = name, Average = combat.Average, Peak = combat.Peak, Seconds = combat.Seconds, Targets = combat.Targets, Controlled = combat.Controlled
        };
        if (combat.Targets > 1) settings.DamageMeterMultiReference = reference;
        else settings.DamageMeterSingleReference = reference;
        return true;
    }

    private bool DrawDamageComparison(DamageTracker meter, KikiMeterSettings settings, PluginLocalization text)
    {
        string nameLabel = text.Label("meter.test_name", "Test name", "MeterTestName");
        ImGui.SetNextItemWidth(Math.Max(80f, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text.T("meter.test_name", "Test name")).X - 8f));
        bool save = ImGui.InputText(nameLabel, ref settings.DamageMeterTestName, 80);
        ImGui.BeginDisabled(meter.TrialArmed || meter.TrialRunning || (!meter.TrialResult.HasValue && meter.InCombat) || meter.ComparisonSnapshot is not { Seconds: >= 1 });
        if (ImGui.Button(text.Label("meter.save_reference", "Save as reference", "MeterSaveReference")) && meter.ComparisonSnapshot is { } snapshot)
        {
            comparisonMulti = snapshot.Targets > 1;
            save |= StoreDamageReference(settings, snapshot, settings.DamageMeterTestName);
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) DrawDamageTooltip(text.T("meter.reference_help", "Finish a test, save it as a reference, change your setup, then start a new test. Available-skill changes can save the previous test automatically."));
        ImGui.Separator();
        for (int mode = 0; mode < 2; mode++)
        {
            if (mode > 0) ImGui.SameLine();
            bool selected = comparisonMulti == (mode == 1);
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ExileCenter.Ui.ImGuiTheme.AccentMuted);
            if (ImGui.SmallButton(text.T(mode == 0 ? "meter.single" : "meter.multi", mode == 0 ? "Single-target" : "Multi-target") + "##compare" + mode))
                comparisonMulti = mode == 1;
            if (selected) ImGui.PopStyleColor();
        }
        var reference = comparisonMulti ? settings.DamageMeterMultiReference : settings.DamageMeterSingleReference;
        DamageTracker.CombatSummary? latest = null;
        if (reference?.Controlled == true)
        {
            if (meter.TrialResult is { } trial && (trial.Targets > 1) == comparisonMulti) latest = trial;
        }
        else foreach (var combat in meter.History)
            if ((combat.Targets > 1) == comparisonMulti) { latest = combat; break; }
        ImGui.TextWrapped(text.T("meter.compare_steps", "1. Run a 10-second test and save the reference.\n2. Change one setting.\n3. Repeat on the same enemy with the same buffs."));
        bool valid = reference != null && double.IsFinite(reference.Average) && reference.Average > 0;
        if (!valid) ImGui.TextWrapped(text.T("meter.no_reference", "No reference saved for this combat type."));
        else
        {
            if (latest is { } result && result.Seconds >= 1)
            {
                double delta = (result.Average / reference!.Average - 1) * 100;
                bool matching = result.Targets == reference.Targets && result.Controlled == reference.Controlled;
                Vector4 color = !matching ? new Vector4(0.75f, 0.78f, 0.85f, 1f) : delta >= 0 ? new Vector4(0.45f, 0.85f, 0.65f, 1f) : new Vector4(0.95f, 0.5f, 0.5f, 1f);
                DrawDamageMetric(text.T("meter.observed_change", "Observed average DPS change"), matching ? $"{delta:+0.0;-0.0;0.0} %" : "—", color);
                if (!matching) ImGui.TextWrapped(text.T("meter.different_targets", "Different target counts: these tests cannot isolate a setup improvement."));
            }
            if (ImGui.BeginTable("MeterComparisonValues", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableNextColumn();
                DrawDamageMetric(text.T("meter.reference_short", "Reference") + (string.IsNullOrWhiteSpace(reference!.Name) ? "" : " · " + reference.Name),
                    NumberFormatter.Format((long)reference.Average), settings.DpsTextColor);
                ImGui.TextWrapped($"{reference.Seconds:0.0} s · {reference.Targets} " + text.T("meter.targets_short", "targets"));
                ImGui.TableNextColumn();
                if (latest is { } completed && completed.Seconds >= 1)
                {
                    DrawDamageMetric(text.T("meter.last_test_short", "Last test"), NumberFormatter.Format((long)completed.Average), settings.DpsTextColor);
                    ImGui.TextWrapped($"{completed.Seconds:0.0} s · {completed.Targets} " + text.T("meter.targets_short", "targets"));
                }
                else ImGui.TextWrapped(text.T("meter.no_history", "No completed combat yet."));
                ImGui.EndTable();
            }
            if (ImGui.SmallButton(text.Label("meter.clear_reference", "Clear this reference", "MeterClearReference")))
            {
                if (comparisonMulti) settings.DamageMeterMultiReference = null;
                else settings.DamageMeterSingleReference = null;
                save = true;
            }
        }
        if (ImGui.CollapsingHeader(text.T("meter.compare_guide", "How to compare tests")))
            ImGui.TextWrapped(text.T("meter.compare_help", "Compare similar durations on the same enemies, with the same buffs and preferably solo. Damage is not attributed to an individual skill; support and passive changes are not detected automatically."));
        return save;
    }

    private static KikiMeterSettings.DamageMeterMapStats MapStats(KikiMeterSettings.DamageMeterMap map, int mode)
        => mode == 1 ? map.Single : mode == 2 ? map.Multi : map.Overall;

    private static string MapCaption(KikiMeterSettings.DamageMeterMap map)
        => map.Name + (string.IsNullOrWhiteSpace(map.Label) ? "" : " · " + map.Label)
            + " · " + map.StartedUtc.ToLocalTime().ToString("dd/MM HH:mm") + " · " + map.AreaLevel;

    private static string MapTime(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");

    private static string MapDelta(double before, double after)
        => before > 0 && double.IsFinite(before) && double.IsFinite(after)
            ? $"{(after / before - 1) * 100:+0.0;-0.0;0.0} %" : "—";

    private static void DrawMapMetrics(KikiMeterSettings.DamageMeterMapStats stats, KikiMeterSettings settings, PluginLocalization text, bool countsAvailable)
    {
        int columns = ImGui.GetContentRegionAvail().X >= 480 ? 3 : 1;
        if (ImGui.BeginTable("MapMetrics", columns, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextColumn(); DrawDamageMetric(text.T("meter.map_average", "Map average DPS"), NumberFormatter.Format((long)stats.Average), settings.DpsTextColor);
            ImGui.TableNextColumn(); DrawDamageMetric(text.T("meter.peak_short", "Peak / 1 s"), NumberFormatter.Format((long)stats.Peak), settings.DpsTextColor);
            ImGui.TableNextColumn(); DrawDamageMetric(text.T("meter.map_burst", "Largest sample · damage"), NumberFormatter.Format(stats.LargestSample), settings.DpsTextColor);
            ImGui.TableNextColumn(); DrawDamageMetric(text.T("meter.map_impact", "Maximum observed impact"), NumberFormatter.Format(stats.LargestImpact), settings.DpsTextColor);
            ImGui.TableNextColumn(); DrawDamageMetric(text.T("meter.map_damage", "Total damage"), NumberFormatter.Format(stats.Damage), settings.DpsTextColor);
            ImGui.TableNextColumn(); DrawDamageMetric(text.T("meter.map_enemies", "Distinct enemies hit"), (stats.Enemies?.Count ?? 0).ToString(), settings.DpsTextColor);
            ImGui.EndTable();
        }
        if (countsAvailable) DrawMonsterCounts(stats.Enemies ?? new(), text);
        else ImGui.TextWrapped(text.T("meter.monsters.unavailable", "Monster counts are unavailable for this older report."));
    }

    private static string MonsterCategory(int category, PluginLocalization text) => category switch
    {
        0 => text.T("meter.monsters.normal", "Normal"),
        1 => text.T("meter.monsters.magic", "Magic"),
        2 => text.T("meter.monsters.rare", "Rare"),
        3 => text.T("meter.monsters.unique", "Unique bosses"),
        _ => text.T("meter.monsters.unknown", "Unclassified")
    };

    private static string MonsterCount(IEnumerable<KikiMeterSettings.DamageMeterEnemy> enemies, int category)
    {
        int hit = 0, dead = 0;
        foreach (var enemy in enemies)
            if ((enemy.Rarity.HasValue ? (int)enemy.Rarity.Value : 4) == category)
            { hit++; if (enemy.DeathObserved == true) dead++; }
        return hit + " / " + dead;
    }

    private static void DrawMonsterCounts(IEnumerable<KikiMeterSettings.DamageMeterEnemy> enemies, PluginLocalization text)
    {
        var observed = enemies.ToArray();
        if (!ImGui.BeginTable("MonsterCounts", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp)) return;
        ImGui.TableSetupColumn(text.T("meter.monsters.category", "Monster category"), ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn(text.T("meter.monsters.hit", "Hit"));
        ImGui.TableSetupColumn(text.T("meter.monsters.dead", "Observed deaths"));
        ImGui.TableHeadersRow();
        for (int category = 0; category < (observed.Any(e => !e.Rarity.HasValue) ? 5 : 4); category++)
        {
            ImGui.TableNextColumn(); ImGui.TextUnformatted(MonsterCategory(category, text));
            var group = observed.Where(e => (e.Rarity.HasValue ? (int)e.Rarity.Value : 4) == category).ToArray();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(group.Length.ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(group.Count(e => e.DeathObserved == true).ToString());
        }
        ImGui.EndTable();
        if (ImGui.IsItemHovered()) DrawDamageTooltip(text.T("meter.monsters.help", "Distinct enemies hit / confirmed deaths after a live observation. Unique includes map bosses, rogue exiles and other unique monsters. All damage sources are included. Disappearance alone is not a death; enemies outside reading range are not counted."));
    }

    private bool DrawDamageMaps(DamageTracker meter, KikiMeterSettings settings, PluginLocalization text)
    {
        bool save = false;
        var maps = settings.DamageMeterMaps;
        float modeRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        for (int mode = 0; mode < 3; mode++)
        {
            string key = mode == 0 ? "meter.map_all" : mode == 1 ? "meter.single" : "meter.multi";
            string label = text.T(key, mode == 0 ? "Whole map" : mode == 1 ? "Single-target" : "Multi-target");
            if (ImGui.GetContentRegionAvail().X < 400)
                label = text.T(mode == 0 ? "meter.map_all_short" : mode == 1 ? "meter.map_single_short" : "meter.map_multi_short",
                    mode == 0 ? "Map" : mode == 1 ? "Single" : "Multi");
            if (mode > 0 && ImGui.GetItemRectMax().X + ImGui.CalcTextSize(label).X +
                ImGui.GetStyle().FramePadding.X * 2 + ImGui.GetStyle().ItemSpacing.X <= modeRight) ImGui.SameLine();
            if (mapMode == mode) ImGui.PushStyleColor(ImGuiCol.Button, ExileCenter.Ui.ImGuiTheme.AccentMuted);
            bool selected = mapMode == mode;
            if (ImGui.SmallButton(label + "##MapMode" + mode)) mapMode = mode;
            if (selected) ImGui.PopStyleColor();
        }
        var reference = maps.Find(m => m.Id == referenceMapId) ?? maps.FirstOrDefault();
        var compared = maps.Find(m => m.Id == comparedMapId) ?? maps.LastOrDefault(m => m.Id != reference?.Id) ?? reference;
        save |= DrawMapColumnOptions(settings, text);
        ImGui.SameLine();
        if (ImGui.Button(text.Label("meter.map_details", "Details / compare...", "MapDetails"))) ImGui.OpenPopup("MapReportDetails");
        if (ImGui.IsPopupOpen("MapReportDetails"))
            ImGui.SetNextWindowSizeConstraints(new Vector2(200, 0),
                new Vector2(Math.Max(200, ImGui.GetIO().DisplaySize.X - 20), Math.Max(180, ImGui.GetIO().DisplaySize.Y - 20)));
        if (ImGui.BeginPopup("MapReportDetails"))
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(360, ImGui.GetIO().DisplaySize.X - 40));
            if (meter.CurrentMap is { } active && ImGui.CollapsingHeader(text.T("meter.map_current", "Current / last map")))
            {
                ImGui.TextWrapped(active.Name + " · " + text.T(active.Finished ? "meter.map_finished" : meter.MapActive ? "meter.map_running" : "meter.map_paused",
                    active.Finished ? "Finished" : meter.MapActive ? "Recording" : "Paused"));
                var stats = MapStats(active, mapMode);
                DrawMapMetrics(stats, settings, text, active.MonsterCountsVersion > 0);
                ImGui.TextWrapped(text.T("meter.map_time", "Time in map") + " : " + MapTime(active.Overall.Seconds));
                ImGui.TextUnformatted(text.T("meter.map_label", "Setup / note"));
                ImGui.SetNextItemWidth(-1);
                save |= ImGui.InputText("##MapSetupLabel", ref active.Label, 120);
                if (ImGui.Button(text.Label("meter.map_save", "Save now", "MapSaveNow"))) save = true;
                ImGui.BeginDisabled(active.Overall.Seconds <= 0);
                if (ImGui.Button(text.Label(active.Finished ? "meter.map_resume" : "meter.map_finish", active.Finished ? "Resume this map" : "Finish and save", "MapFinish")))
                {
                    if (active.Finished) meter.ResumeMap(); else meter.FinishMap();
                    save = true;
                }
                ImGui.EndDisabled();
            }
            ImGui.TextWrapped(text.T("meter.map_autosave_short", "Auto-saved · pauses outside the map"));
            if (ImGui.CollapsingHeader(text.T("meter.map_guide", "Average, single-target and impacts")))
            {
                ImGui.TextWrapped(text.T("meter.map_help", "The map includes its subareas. The hideout pauses tracking. Observed portal replacement, or entering another map, finishes the report. Auto-saved every 20 seconds and on area exits."));
                ImGui.TextWrapped(text.T("meter.map_measure_help", "Whole-map average includes movement and idle time inside the map. Single/multi averages use periods with one/two or more damaged targets in the last second. Samples sum observed damage on the first N distinct enemies, including every later observed loss on those same enemies. Maximum impact is the largest observed loss on one enemy between readings, not a guaranteed single hit; overkill damage is unavailable. All sources are included."));
            }
            if (reference != null && compared != null && ImGui.CollapsingHeader(text.T("meter.map_compare", "Compare saved maps")))
            {
                DrawMapChoice("MapReference", text.T("meter.map_reference", "Reference map"), maps, ref reference, ref referenceMapId);
                DrawMapChoice("MapCompared", text.T("meter.map_compared", "Compared map"), maps, ref compared, ref comparedMapId);
                if (reference.Id != compared.Id)
                {
                    var before = MapStats(reference, mapMode);
                    var after = MapStats(compared, mapMode);
                    bool compact = ImGui.GetContentRegionAvail().X < 540;
                    if (ImGui.BeginTable("MapComparison", compact ? 3 : 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn(text.T("meter.map_metric", "Metric"), ImGuiTableColumnFlags.WidthStretch, 2);
                        ImGui.TableSetupColumn("A", ImGuiTableColumnFlags.WidthStretch, 1);
                        ImGui.TableSetupColumn("B", ImGuiTableColumnFlags.WidthStretch, 1);
                        if (!compact) ImGui.TableSetupColumn(text.T("meter.map_change", "Change"), ImGuiTableColumnFlags.WidthStretch, 1);
                        ImGui.TableHeadersRow();
                        DrawMapComparisonRow(text.T("meter.map_average", "Average DPS"), before.Average, after.Average, compact);
                        DrawMapComparisonRow(text.T("meter.peak_short", "Peak / 1 s"), before.Peak, after.Peak, compact);
                        DrawMapComparisonRow(text.T("meter.map_burst", "Largest sample · damage"), before.LargestSample, after.LargestSample, compact);
                        DrawMapComparisonRow(text.T("meter.map_damage", "Total damage"), before.Damage, after.Damage, compact);
                        if (reference.EnemySampleVersion > 0 && compared.EnemySampleVersion > 0)
                        {
                            DrawMapComparisonRow(text.T("meter.map_impact", "Maximum observed impact"), before.LargestImpact, after.LargestImpact, compact);
                            foreach (int sample in KikiMeterSettings.DamageMeterEnemySampleSizes)
                                if (settings.DamageMeterMapSampleColumns?.Contains(sample) == true &&
                                    SampleDamage(before, sample) is { } beforeDamage && SampleDamage(after, sample) is { } afterDamage)
                                    DrawMapComparisonRow(SampleLabel(sample, text), beforeDamage, afterDamage, compact);
                        }
                        ImGui.EndTable();
                    }
                    if (!reference.Finished || !compared.Finished)
                        ImGui.TextWrapped(text.T("meter.map_unfinished", "At least one report is still open; the comparison can change."));
                    if (reference.SkillsChanged || compared.SkillsChanged)
                        ImGui.TextWrapped(text.T("meter.map_skills_changed", "Skills changed during at least one map: it contains several setups."));
                    if (reference.ReadingInterrupted || compared.ReadingInterrupted)
                        ImGui.TextWrapped(text.T("meter.map_gaps", "Reading was interrupted in at least one report. Its damage may be incomplete."));
                    ImGui.TextWrapped(text.T("meter.map_compare_help", "Map monsters, modifiers and density affect these values. An increase is an observed map result, not proof that a specific skill gained DPS."));
                }
                else ImGui.TextWrapped(text.T("meter.map_need_two", "Save two different maps to compare them."));
            }
            ImGui.PopTextWrapPos();
            ImGui.EndPopup();
        }
        if (maps.Count == 0) { ImGui.TextWrapped(text.T("meter.map_none", "Enter a map to start a report.")); return save; }
        if (maps.Any(map => map.MonsterCountsVersion == 0 || map.MonsterCountsPartial || map.SubAreas.Any(a => a.MonsterCountsPartial)))
            ImGui.TextWrapped(text.T("meter.monsters.partial", "Older reports retain their totals. Monster categories and confirmed deaths cover only observations made after this feature was enabled; missing historical counts are not reconstructed."));
        var columns = new List<int>();
        if (settings.DamageMeterMapShowTime) columns.Add(0);
        if (settings.DamageMeterMapShowAverage) columns.Add(1);
        if (settings.DamageMeterMapShowPeak) columns.Add(2);
        if (settings.DamageMeterMapShowImpact) columns.Add(3);
        if (settings.DamageMeterMapShowDamage) columns.Add(4);
        if (settings.DamageMeterMapShowEnemies) columns.Add(5);
        if (settings.DamageMeterMapShowMonsterTypes)
        {
            columns.AddRange(new[] { -1, -2, -3, -4 });
            if (maps.SelectMany(m => new[] { m }.Concat(m.SubAreas)).Any(m => MapStats(m, mapMode).Enemies?.Any(e => !e.Rarity.HasValue) == true)) columns.Add(-5);
        }
        if (settings.DamageMeterMapShowStatus) columns.Add(6);
        columns.AddRange(KikiMeterSettings.DamageMeterEnemySampleSizes.Where(n => settings.DamageMeterMapSampleColumns?.Contains(n) == true));
        float height = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 2, ImGui.GetContentRegionAvail().Y - 4);
        float nameWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * .3f, 95, 240);
        if (ImGui.BeginTable("SavedDamageMaps", columns.Count + 1, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new Vector2(0, height), nameWidth + columns.Count * 120))
        {
            ImGui.TableSetupColumn(text.T("meter.map_name", "Map / setup / date"), ImGuiTableColumnFlags.WidthFixed, nameWidth);
            foreach (int column in columns) ImGui.TableSetupColumn(MapColumnLabel(column, text), ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupScrollFreeze(1, 1);
            ImGui.TableHeadersRow();
            foreach (var row in maps.AsEnumerable().Reverse().SelectMany(parent =>
                new[] { (Map: parent, Parent: parent) }.Concat(parent.SubAreas.Select(child => (Map: child, Parent: parent)))))
            {
                var map = row.Map;
                var stats = MapStats(map, mapMode);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable((map == row.Parent ? "" : "    > ") + MapCaption(map) + "##SavedMap" + map.Id, compared?.Id == row.Parent.Id)) comparedMapId = row.Parent.Id;
                if (ImGui.IsItemHovered()) DrawDamageTooltip(MapCaption(map));
                foreach (int column in columns)
                {
                    ImGui.TableNextColumn();
                    string value = column switch
                    {
                        0 => MapTime(map.Overall.Seconds),
                        1 => NumberFormatter.Format((long)stats.Average),
                        2 => NumberFormatter.Format((long)stats.Peak),
                        3 => map.EnemySampleVersion > 0 ? NumberFormatter.Format(stats.LargestImpact) : "—",
                        4 => NumberFormatter.Format(stats.Damage),
                        5 => map.EnemySampleVersion > 0 ? (stats.Enemies?.Count ?? 0).ToString() : "—",
                        6 => text.T(row.Parent.Finished ? "meter.map_finished" : meter.MapActive && row.Parent == meter.CurrentMap &&
                            (map == row.Parent || map.Hash == meter.CurrentSubAreaHash) ? "meter.map_running" : "meter.map_paused", row.Parent.Finished ? "Finished" : "Paused"),
                        < 0 => map.MonsterCountsVersion == 0 ? "—" : MonsterCount(stats.Enemies ?? new(), -column - 1),
                        _ => map.EnemySampleVersion == 0 ? "—" : SampleDamage(stats, column) is { } damage
                            ? NumberFormatter.Format(damage) : $"{Math.Min(stats.Enemies?.Count ?? 0, column)} / {column}"
                    };
                    ImGui.TextUnformatted(value);
                    if (ImGui.IsItemHovered()) DrawDamageTooltip(column < 0 ? text.T("meter.monsters.help", "Distinct enemies hit / confirmed deaths after a live observation. Unique includes map bosses, rogue exiles and other unique monsters. All damage sources are included. Disappearance alone is not a death; enemies outside reading range are not counted.") : column == 3 ? text.T("meter.map_impact_help", "Largest observed loss on a single enemy between readings. Always retains the map maximum; several hits may be combined and overkill damage is not visible.") :
                        column >= 10 ? text.T("meter.map_samples_help", "Total observed damage on the first N distinct enemies in the selected mode. Repeated hits add damage without counting the enemy twice. A value appears once N enemies have been reached; otherwise progress is shown. These are damage totals, not DPS.") : MapCaption(map));
                }
            }
            ImGui.EndTable();
        }
        if (maps.Any(map => map.EnemySamplesPartial))
            ImGui.TextWrapped(text.T("meter.map_samples_partial", "Older reports retain their totals; enemy samples cover only damage observed after this feature was enabled."));
        return save;
    }

    private static long? SampleDamage(KikiMeterSettings.DamageMeterMapStats stats, int count)
    {
        int index = Array.IndexOf(KikiMeterSettings.DamageMeterEnemySampleSizes, count);
        return index >= 0 && stats.Enemies?.Count >= count && stats.EnemySampleDamage?.Count > index
            ? stats.EnemySampleDamage[index] : null;
    }

    private static string SampleLabel(int count, PluginLocalization text) => count + " " + text.T("meter.map_sample_enemies", "enemies · damage");

    private static string MapColumnLabel(int column, PluginLocalization text) => column switch
    {
        0 => text.T("meter.duration_short", "Time"),
        1 => text.T("meter.map_average", "Average DPS"),
        2 => text.T("meter.peak_short", "Peak / 1 s"),
        3 => text.T("meter.map_impact_short", "Max impact\nobserved"),
        4 => text.T("meter.map_damage", "Total damage"),
        5 => text.T("meter.map_enemies", "Distinct enemies hit"),
        6 => text.T("meter.map_state", "Status"),
        < 0 => MonsterCategory(-column - 1, text) + "\n" + text.T("meter.monsters.hit_dead", "Hit / deaths"),
        _ => SampleLabel(column, text)
    };

    private static bool DrawMapColumnOptions(KikiMeterSettings settings, PluginLocalization text)
    {
        bool save = false;
        if (ImGui.Button(text.Label("meter.map_columns", "Columns...", "MapColumns"))) ImGui.OpenPopup("MapColumnOptions");
        if (ImGui.IsPopupOpen("MapColumnOptions"))
            ImGui.SetNextWindowSizeConstraints(new Vector2(200, 0),
                new Vector2(Math.Max(200, ImGui.GetIO().DisplaySize.X - 20), Math.Max(180, ImGui.GetIO().DisplaySize.Y - 20)));
        if (!ImGui.BeginPopup("MapColumnOptions")) return false;
        save |= ImGui.Checkbox(MapColumnLabel(0, text), ref settings.DamageMeterMapShowTime);
        save |= ImGui.Checkbox(MapColumnLabel(1, text), ref settings.DamageMeterMapShowAverage);
        save |= ImGui.Checkbox(MapColumnLabel(2, text), ref settings.DamageMeterMapShowPeak);
        save |= ImGui.Checkbox(MapColumnLabel(3, text), ref settings.DamageMeterMapShowImpact);
        save |= ImGui.Checkbox(MapColumnLabel(4, text), ref settings.DamageMeterMapShowDamage);
        save |= ImGui.Checkbox(MapColumnLabel(5, text), ref settings.DamageMeterMapShowEnemies);
        save |= ImGui.Checkbox(text.T("meter.monsters.columns", "Monster categories · hit / deaths"), ref settings.DamageMeterMapShowMonsterTypes);
        save |= ImGui.Checkbox(MapColumnLabel(6, text), ref settings.DamageMeterMapShowStatus);
        ImGui.SeparatorText(text.T("meter.map_samples", "Enemy samples"));
        settings.DamageMeterMapSampleColumns ??= new();
        float buttonsRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.SmallButton(text.T("meter.map_samples_all", "All samples")))
        { settings.DamageMeterMapSampleColumns = KikiMeterSettings.DamageMeterEnemySampleSizes.ToList(); save = true; }
        string noSamples = text.T("meter.map_samples_none", "No samples");
        if (ImGui.GetItemRectMax().X + ImGui.CalcTextSize(noSamples).X + ImGui.GetStyle().FramePadding.X * 2 +
            ImGui.GetStyle().ItemSpacing.X <= buttonsRight) ImGui.SameLine();
        if (ImGui.SmallButton(noSamples))
        { settings.DamageMeterMapSampleColumns.Clear(); save = true; }
        if (ImGui.BeginTable("SampleChoices", ImGui.GetContentRegionAvail().X < 330 ? 1 : 2, ImGuiTableFlags.SizingFixedFit))
        {
            foreach (int count in KikiMeterSettings.DamageMeterEnemySampleSizes)
            {
                ImGui.TableNextColumn();
                bool enabled = settings.DamageMeterMapSampleColumns.Contains(count);
                if (ImGui.Checkbox(count + " " + text.T("meter.map_sample_count", "enemies"), ref enabled))
                {
                    if (enabled) settings.DamageMeterMapSampleColumns.Add(count);
                    else settings.DamageMeterMapSampleColumns.RemoveAll(n => n == count);
                    save = true;
                }
            }
            ImGui.EndTable();
        }
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Min(360, ImGui.GetIO().DisplaySize.X - 40));
        ImGui.TextWrapped(text.T("meter.map_samples_help", "First N distinct enemies; all observed damage on those enemies is added. Unreached samples show their progress."));
        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
        return save;
    }

    private static void DrawMapChoice(string id, string label, List<KikiMeterSettings.DamageMeterMap> maps,
        ref KikiMeterSettings.DamageMeterMap selected, ref string selectedId)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##" + id, MapCaption(selected)))
        {
            foreach (var map in maps.AsEnumerable().Reverse())
                if (ImGui.Selectable(MapCaption(map) + "##" + map.Id, selected.Id == map.Id))
                { selected = map; selectedId = map.Id; }
            ImGui.EndCombo();
        }
    }

    private static void DrawMapComparisonRow(string label, double before, double after, bool compact)
    {
        var color = after >= before ? new Vector4(0.45f, 0.85f, 0.65f, 1f) : new Vector4(0.95f, 0.5f, 0.5f, 1f);
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.TextWrapped(label);
        if (compact) ImGui.TextColored(color, MapDelta(before, after));
        ImGui.TableNextColumn(); ImGui.TextUnformatted(NumberFormatter.Format((long)before));
        ImGui.TableNextColumn(); ImGui.TextUnformatted(NumberFormatter.Format((long)after));
        if (!compact) { ImGui.TableNextColumn(); ImGui.TextColored(color, MapDelta(before, after)); }
    }

    private static void DrawDamageTooltip(string message)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        ImGui.TextUnformatted(message);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }
    private static void DrawDamageHistory(DamageTracker meter, PluginLocalization text)
    {
        if (meter.History.Count == 0) ImGui.TextWrapped(text.T("meter.no_history", "No completed combat yet."));
        else if (ImGui.BeginTable("DamageMeterCombats", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new Vector2(0f, Math.Max(ImGui.GetTextLineHeightWithSpacing() * 3, ImGui.GetContentRegionAvail().Y - 36f))))
        {
            ImGui.TableSetupColumn(text.T("meter.duration_short", "Time"));
            ImGui.TableSetupColumn(text.T("meter.target_count", "Targets"));
            ImGui.TableSetupColumn(text.T("meter.average", "Average DPS"));
            ImGui.TableSetupColumn(text.T("meter.peak_short", "Peak / 1 s"));
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            foreach (var combat in meter.History)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted($"{combat.Seconds:0.0} s");
                ImGui.TableNextColumn(); ImGui.TextWrapped(text.T(combat.Targets > 1 ? "meter.multi" : "meter.single", combat.Targets > 1 ? "Multi-target" : "Single-target") + $" · {combat.Targets}");
                ImGui.TableNextColumn(); ImGui.TextUnformatted(NumberFormatter.Format((long)combat.Average));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(NumberFormatter.Format((long)combat.Peak));
            }
            ImGui.EndTable();
        }
    }

}
