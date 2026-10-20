namespace StashUtility
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using ExileCenter.RemoteObjects.Components;
    using ExileCenter.Utils;
    using ImGuiNET;
    using Models;

    public sealed partial class StashUtilityCore
    {
        private string tabletProfileJson = "", tabletProfileMessage = "";
        private TabletObservation lastMetaObservation;
        private string lastMetaItemName = "";
        private DateTime lastMetaObservedUtc;
        private readonly List<(Vector2 Position, Vector2 Size, string Text)> tabletTooltipLabels = new();
        private (Vector2 Position, Vector2 Size)? tabletTooltipPanel;
        private string MT(string key, string fallback) => PluginText.T("stashutility.meta." + key, fallback);
        private static void MetaText(string text) => ImGuiHelper.WrappedText(text);
        private static TabletMetaCatalog MetaCatalog => Data.ModDatabase.TabletMeta;
        private static string MetaDisplayText(TabletMetaModifier modifier) =>
            ExileCenter.Localization.OverlayLocalization.CurrentLanguage == ExileCenter.Localization.OverlayLanguage.French
                ? modifier.FrenchText : modifier.EnglishText;
        private static TabletFilterProfile ActiveMetaProfile(TabletProfile settings)
            => settings.MetaProfiles?.FirstOrDefault(p => p != null && p.Id == settings.ActiveMetaProfileId);

        private static Dictionary<string, TabletProfile> CopyTabletProfiles(Dictionary<string, TabletProfile> profiles) =>
            Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, TabletProfile>>(
                Newtonsoft.Json.JsonConvert.SerializeObject(profiles ?? new()),
                new Newtonsoft.Json.JsonSerializerSettings { MaxDepth = TabletFilterEngine.MaximumDepth * 3 + 32 });

        private static readonly string[] TabletModes = { "farm_trade", "farm", "strategy", "custom" };
        private static readonly string[] TabletStrategies = { "fubgun", "ritual", "hiveblood" };

        private bool SetTabletMode(string mode, string strategy = null)
        {
            strategy ??= Settings.TabletStrategy;
            if (!TabletModes.Contains(mode) || !TabletStrategies.Contains(strategy)) return false;
            if (Settings.TabletMode == mode && (mode != "strategy" || Settings.TabletStrategy == strategy)) return false;
            var profiles = mode == "custom" ? CopyTabletProfiles(Settings.CustomTabletProfiles ?? Settings.TabletProfiles) :
                StashUtilitySettings.TabletTypes.ToDictionary(type => type, type =>
                {
                    var profile = new TabletProfile();
                    foreach (var mod in Data.ModDatabase.AllTabletMods.Where(mod => mod.TabletTypes.Contains(type)))
                    {
                        string rank = mode == "farm_trade" ? MetaCatalog.Recognize(type, new[] { mod.Id }).Mods[0].Modifier?.Rank ?? "" :
                            Data.ModDatabase.GetTabletFarmRank(type, mod.Id) ?? "";
                        SetTabletModRank(mod.Id, rank, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns);
                    }
                    return profile;
                });
            if (mode == "strategy")
            {
                var rule = CreateTabletStrategy(strategy);
                if (TabletFilterEngine.Validate(rule, MetaCatalog).Length > 0) return false;
                var profile = profiles[rule.TabletType];
                profile.MetaProfiles.Add(rule); profile.ActiveMetaProfileId = rule.Id; profile.UseMetaFilters = true;
                tabletFilterType = Array.IndexOf(StashUtilitySettings.TabletTypes, rule.TabletType);
            }
            if (Settings.TabletMode == "custom") Settings.CustomTabletProfiles = CopyTabletProfiles(Settings.TabletProfiles);
            Settings.TabletProfiles = profiles;
            Settings.TabletMode = mode; Settings.TabletStrategy = strategy;
            lastMetaObservation = null;
            SaveSettings();
            return true;
        }

        private static TabletFilterProfile CreateTabletStrategy(string strategy)
        {
            if (strategy == "fubgun") return CreateFubgunTabletProfile();
            string type = strategy == "ritual" ? "Ritual Tablet" : "Breach Tablet";
            TabletRuleNode Mod(string id) => new() { Kind = TabletRuleKind.Modifier,
                ModifierId = MetaCatalog.Recognize(type, new[] { id }).Mods[0].Modifier?.Id ?? "" };
            return new TabletFilterProfile
            {
                Id = "strategy-" + strategy, Name = strategy == "ritual" ? "Ritual - Omens" : "Breach - Hiveblood", TabletType = type,
                Rule = strategy == "ritual" ? new() { Kind = TabletRuleKind.All, Children = new()
                    { Mod("TowerRitualOmenChance"), Mod("TowerRitualRerollCostIncrease") } } : Mod("TowerBreachHivebloodQuantity")
            };
        }

        private void DrawTabletModes()
        {
            string ModeName(string mode) => MT("mode_" + mode, mode switch
            { "farm_trade" => "Farming and resale", "farm" => "Farming only", "strategy" => "Specific strategy", _ => "Custom" });
            ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X, 100, 360));
            if (ImGui.BeginCombo("##TabletMode", ModeName(Settings.TabletMode)))
            {
                foreach (string mode in TabletModes)
                    if (ImGui.Selectable(ModeName(mode), Settings.TabletMode == mode)) SetTabletMode(mode);
                ImGui.EndCombo();
            }
            if (Settings.TabletMode == "strategy")
            {
                string StrategyName(string strategy) => MT("strategy_" + strategy, strategy switch
                { "ritual" => "Ritual - Omens", "hiveblood" => "Breach - Hiveblood", _ => "Fubgun - Irradiated" });
                ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X, 100, 360));
                if (ImGui.BeginCombo("##TabletStrategy", StrategyName(Settings.TabletStrategy)))
                {
                    foreach (string strategy in TabletStrategies)
                        if (ImGui.Selectable(StrategyName(strategy), Settings.TabletStrategy == strategy)) SetTabletMode("strategy", strategy);
                    ImGui.EndCombo();
                }
            }
            TabletHelp(MT("mode_help", "Automatic modes apply their own ranks. Custom restores your choices and enables presets 1 to 5. Resale uses the reference ranking, without live prices. A specific strategy adds its conditions to the relevant tablet type; other types keep farming ranks."));
            MetaText(MT("quality_legend", "Badge: selected modifiers meeting their minimum, e.g. S2A = two S and one A. Color follows their strongest grade. No match: neutral N/C. Reference grades remain in the tooltip."));
        }

        private void EnsureTabletPresetSlots()
        {
            if (Settings.TabletPresets == null) Settings.TabletPresets = new TabletPreset[5];
            else if (Settings.TabletPresets.Length != 5) Array.Resize(ref Settings.TabletPresets, 5);
            Settings.ActiveTabletPreset = Math.Clamp(Settings.ActiveTabletPreset, 0, 4);
        }

        private bool SaveTabletPreset(int slot)
        {
            if (Settings.TabletMode != "custom" || slot is < 0 or >= 5) return false;
            EnsureTabletPresetSlots();
            Settings.TabletPresets[slot] = new TabletPreset
            {
                Profiles = CopyTabletProfiles(Settings.TabletProfiles),
                FilterGreat = Settings.FilterTabletGreat,
                MinimumGoodMods = Settings.MinTabletGoodMods,
                MinimumHighlightedMods = Settings.MinTabletGoodModsToHighlight,
                MinimumGoodModsToIgnoreBad = Settings.MinGoodModsToIgnoreBad
            };
            Settings.ActiveTabletPreset = slot;
            SaveSettings();
            return true;
        }

        private bool LoadTabletPreset(int slot)
        {
            if (Settings.TabletMode != "custom" || slot is < 0 or >= 5) return false;
            EnsureTabletPresetSlots();
            var preset = Settings.TabletPresets[slot];
            if (preset == null) return false;
            var profiles = CopyTabletProfiles(preset.Profiles);
            Settings.TabletProfiles = profiles ?? new();
            Settings.FilterTabletGreat = preset.FilterGreat;
            Settings.MinTabletGoodMods = preset.MinimumGoodMods;
            Settings.MinTabletGoodModsToHighlight = preset.MinimumHighlightedMods;
            Settings.MinGoodModsToIgnoreBad = preset.MinimumGoodModsToIgnoreBad;
            Settings.ActiveTabletPreset = slot;
            lastMetaObservation = null;
            SaveSettings();
            return true;
        }

        private void DrawTabletPresets()
        {
            EnsureTabletPresetSlots();
            ImGui.BeginDisabled(Settings.TabletMode != "custom");
            string label = MT("preset", "Preset") + " " + (Settings.ActiveTabletPreset + 1);
            ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X - 76, 100, 240));
            if (ImGui.BeginCombo("##TabletPresetSlot", label))
            {
                for (int i = 0; i < 5; i++)
                {
                    string name = MT("preset", "Preset") + " " + (i + 1) + " · " +
                        MT(Settings.TabletPresets[i] == null ? "preset_empty" : "preset_saved", Settings.TabletPresets[i] == null ? "Empty" : "Saved");
                    if (ImGui.Selectable(name, i == Settings.ActiveTabletPreset)) { Settings.ActiveTabletPreset = i; SaveSettings(); }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (TabletPresetIcon("SaveTabletPreset", true, true))
            {
                SaveTabletPreset(Settings.ActiveTabletPreset);
                tabletProfileMessage = label + " · " + MT("preset_saved", "Saved");
            }
            ImGuiHelper.ToolTip(MT("preset_save", "Save ranks, minimum rolls and AND / OR rules for all eight tablet types in this slot."));
            ImGui.SameLine();
            if (TabletPresetIcon("LoadTabletPreset", false, Settings.TabletPresets[Settings.ActiveTabletPreset] != null))
            {
                LoadTabletPreset(Settings.ActiveTabletPreset);
                tabletProfileMessage = label + " · " + MT("preset_loaded", "Loaded");
            }
            ImGuiHelper.ToolTip(MT("preset_load", "Load this slot and apply its saved tablet choices. Empty slots cannot be loaded."));
            ImGui.EndDisabled();
        }

        private static bool TabletPresetIcon(string id, bool save, bool enabled)
        {
            ImGui.BeginDisabled(!enabled);
            float side = ImGui.GetFrameHeight();
            var pos = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton(id, new Vector2(side));
            var draw = ImGui.GetWindowDrawList();
            uint ink = ImGui.GetColorU32(ImGuiCol.Text);
            draw.AddRectFilled(pos, pos + new Vector2(side), ImGui.GetColorU32(ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered : ImGuiCol.Button), 4);
            Vector2 P(float x, float y) => pos + new Vector2(x, y) * side;
            draw.AddRect(P(.22f, .2f), P(.78f, .8f), ink, 1, ImDrawFlags.None, 1.4f);
            if (save)
            {
                draw.AddRect(P(.34f, .2f), P(.65f, .43f), ink);
                draw.AddRect(P(.34f, .58f), P(.65f, .8f), ink);
            }
            else
            {
                draw.AddLine(P(.5f, .28f), P(.5f, .65f), ink, 1.8f);
                draw.AddLine(P(.34f, .49f), P(.5f, .65f), ink, 1.8f);
                draw.AddLine(P(.66f, .49f), P(.5f, .65f), ink, 1.8f);
            }
            ImGui.EndDisabled();
            return clicked;
        }

        private void TabletHelp(string text)
        {
            if (!Settings.TabletHelpTooltips || !ImGuiHelper.HelpTooltipsEnabled) return;
            if (ImGui.GetItemRectMax().X + 30 < ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X) ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            ImGuiHelper.ToolTip(text);
        }

        private void DrawTabletMatchMode(string type, TabletProfile settings)
        {
            bool all = settings.RequireAllRankedMods, any = !all;
            if (ImGui.Checkbox(MT("match_all", "AND") + "##TabletMatchAll", ref all) && all)
            { settings.RequireAllRankedMods = true; SaveSettings(); }
            ImGui.SameLine();
            if (ImGui.Checkbox(MT("match_any", "OR") + "##TabletMatchAny", ref any) && any)
            { settings.RequireAllRankedMods = false; SaveSettings(); }
            int count = SelectedTabletMods(settings, type).Select(mod => mod.Id).Distinct().Count();
            MetaText(MT(settings.RequireAllRankedMods ? "match_all_help" : "match_any_help",
                settings.RequireAllRankedMods ? "All checked modifiers must be present and meet their minimums." : "At least one checked modifier must be present and meet its minimum.") +
                " " + count + " " + MT("selected_mods", "checked modifiers"));
            if (settings.RequireAllRankedMods && count > 4)
                MetaText(MT("match_too_many", "A tablet has at most 4 modifiers. Uncheck some modifiers to obtain a match with AND."));
        }

        private void DrawTabletMetaFilters(string type, TabletProfile settings)
        {
            settings.MetaProfiles ??= new(); settings.ManualMetaModifiers ??= new(); settings.MetaSearch ??= "";
            // Older settings could enable advanced filtering before a profile existed.
            // There is no additional rule to apply in that case; keep the user's ranks.
            if (settings.UseMetaFilters && ActiveMetaProfile(settings) == null)
            { settings.UseMetaFilters = false; SaveSettings(); }
            if (Settings.TabletMode == "custom" && ImGui.CollapsingHeader(MT("advanced_rules", "Advanced filters (optional)") + "###TabletRulesEditor"))
            {
                bool ready = TabletFilterEngine.Validate(ActiveMetaProfile(settings), MetaCatalog).Length == 0;
                ImGui.BeginDisabled(!settings.UseMetaFilters && !ready);
                if (ImGui.Checkbox(MT("advanced_enable", "Also require the advanced profile"), ref settings.UseMetaFilters)) SaveSettings();
                ImGui.EndDisabled();
                TabletHelp(MT("advanced_help", "Advanced rules add conditions to the AND / OR choice above. They are optional; analysis never enables filtering."));
                if (!ready) MetaText(MT("advanced_requires_profile", "Create or select a profile and complete its rules before enabling the advanced filter. Your modifier ranks work without it."));
                if (ImGui.BeginTabBar("TabletMetaTabs"))
                {
                    if (ImGui.BeginTabItem(MT("rules", "Rules")))
                    {
                        DrawMetaProfiles(type, settings);
                        TabletHelp(MT("rules_help", "Choose a profile and add AND / OR conditions or nested groups. Incomplete groups cannot accept a tablet."));
                        var active = ActiveMetaProfile(settings);
                        if (active != null)
                        {
                            bool changed = ImGui.InputText(MT("profile_name", "Profile name"), ref active.Name, 120);
                            changed |= DrawMetaAction(MT("on_match", "When matched"), ref active.OnMatch);
                            changed |= DrawMetaAction(MT("on_miss", "When not matched"), ref active.OnMiss);
                            TabletHelp(MT("actions_help", "Keep displays the result. Signal adds an amber alert. Ignore hides the indication."));
                            ImGui.Separator();
                            if (ImGui.BeginChild("MetaRuleTree", Vector2.Zero,
                                ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize,
                                ImGuiWindowFlags.HorizontalScrollbar)) changed |= DrawMetaNode(active.Rule, type, 0);
                            ImGui.EndChild();
                            MetaText(MT("summary", "Rule") + " : " + MetaSummary(active.Rule, type));
                            string error = TabletFilterEngine.Validate(active, MetaCatalog);
                            if (error.Length > 0) ImGui.TextColored(new Vector4(1, .7f, .3f, 1), MT("invalid", "Incomplete or invalid rule") + " : " + MetaError(error));
                            if (changed) SaveSettings();
                        }
                        else MetaText(MT("create_hint", "Create a profile and add conditions or groups. Empty groups never accept a tablet."));
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem(MT("exchange", "Import / export"))) { DrawMetaExchange(type, settings); ImGui.EndTabItem(); }
                    ImGui.EndTabBar();
                }
            }
            if (ImGui.CollapsingHeader(MT("analysis", "Analysis") + "###TabletAnalysis")) DrawMetaAnalysis(type, settings);
            if (ImGui.CollapsingHeader(MT("reference_catalog", "Reference catalogue (show / hide)") + "###TabletReferenceCatalogue"))
            {
                ImGui.TextDisabled(MetaCatalog.Version + " · " + MetaCatalog.ForType(type).Count() + " " + MT("mods", "modifiers"));
                DrawMetaCatalog(type, settings, false);
            }
        }

        private void DrawMetaProfiles(string type, TabletProfile settings)
        {
            if (type == "Irradiated Tablet")
            {
                if (ImGui.SmallButton(MT("fubgun_profile", "Use Fubgun's Irradiated combination")))
                {
                    var preset = CreateFubgunTabletProfile();
                    if (!settings.MetaProfiles.Any(p => p?.Id == preset.Id) && settings.MetaProfiles.Count < 100)
                        settings.MetaProfiles.Add(preset);
                    if (settings.MetaProfiles.Any(p => p?.Id == preset.Id))
                    {
                        settings.ActiveMetaProfileId = preset.Id;
                        settings.UseMetaFilters = true;
                        SaveSettings();
                    }
                }
                TabletHelp(MT("fubgun_rule", "At least 2 extra map modifiers AND (monster effectiveness OR more rare monsters).") +
                    "\nhttps://mobalytics.gg/poe-2/atlas-trees/fubgun-atlas-tree-strats");
            }
            var active = ActiveMetaProfile(settings);
            if (ImGui.BeginCombo(MT("active_profile", "Active profile"), active?.Name ?? MT("none", "None")))
            {
                foreach (var profile in settings.MetaProfiles.Where(p => p != null))
                    if (ImGui.Selectable(profile.Name + "##" + profile.Id, profile == active)) { settings.ActiveMetaProfileId = profile.Id; SaveSettings(); }
                ImGui.EndCombo();
            }
            ImGui.BeginDisabled(settings.MetaProfiles.Count >= 100);
            if (ImGui.SmallButton(MT("new", "New")))
            {
                var profile = new TabletFilterProfile { Name = MT("default_name", "My profile"), TabletType = type };
                settings.MetaProfiles.Add(profile); settings.ActiveMetaProfileId = profile.Id; SaveSettings();
            }
            ImGui.EndDisabled(); ImGui.SameLine();
            ImGui.BeginDisabled(active == null || settings.MetaProfiles.Count >= 100);
            if (ImGui.SmallButton(MT("duplicate", "Duplicate")))
            {
                var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<TabletFilterProfile>(Newtonsoft.Json.JsonConvert.SerializeObject(active),
                    new Newtonsoft.Json.JsonSerializerSettings { MaxDepth = TabletFilterEngine.MaximumDepth * 3 + 32 });
                copy.Id = Guid.NewGuid().ToString("N"); copy.Name = (copy.Name.Length > 100 ? copy.Name[..100] : copy.Name) + " " + MT("copy", "copy");
                settings.MetaProfiles.Add(copy); settings.ActiveMetaProfileId = copy.Id; SaveSettings();
            }
            ImGui.EndDisabled(); ImGui.SameLine(); ImGui.BeginDisabled(active == null);
            if (ImGui.SmallButton(MT("delete", "Delete")))
            { settings.MetaProfiles.Remove(active); settings.ActiveMetaProfileId = settings.MetaProfiles.FirstOrDefault()?.Id ?? ""; SaveSettings(); }
            ImGui.EndDisabled();
        }

        private static TabletFilterProfile CreateFubgunTabletProfile()
        {
            const string type = "Irradiated Tablet";
            TabletRuleNode Mod(string rawId) => new()
            {
                Kind = TabletRuleKind.Modifier,
                ModifierId = MetaCatalog.Recognize(type, new[] { rawId }).Mods[0].Modifier?.Id ?? "",
            };
            var additional = Mod("TowerMapAdditionalModifier");
            additional.MinimumRoll = 2;
            return new TabletFilterProfile
            {
                Id = "fubgun-irradiated-combination", Name = "Fubgun - Irradiated", TabletType = type,
                Rule = new() { Kind = TabletRuleKind.All, Children = new()
                {
                    additional,
                    new() { Kind = TabletRuleKind.Any, Children = new()
                    {
                        Mod("TowerMonsterEffectiveness"), Mod("TowerRarePackIncrease"),
                    } },
                } },
            };
        }

        private bool DrawMetaAction(string label, ref TabletFilterAction action)
        {
            int value = (int)action;
            if (!ImGui.Combo(label, ref value, MT("actions", "Keep\0Ignore\0Signal\0"))) return false;
            action = (TabletFilterAction)value; return true;
        }

        private bool DrawMetaNode(TabletRuleNode node, string type, int depth)
        {
            if (node == null || depth > TabletFilterEngine.MaximumDepth) { MetaText(MetaError("tree-limit")); return false; }
            bool changed = false; ImGui.PushID(node.Id ?? "invalid");
            try
            {
                if (node.Kind is TabletRuleKind.All or TabletRuleKind.Any)
                {
                    node.Children ??= new();
                    if (!ImGui.TreeNodeEx("group", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth,
                        MT("group", "Group") + " · " + MT(node.Kind == TabletRuleKind.All ? "and" : "or", node.Kind == TabletRuleKind.All ? "AND" : "OR"))) return false;
                    try
                    {
                        int operation = node.Kind == TabletRuleKind.All ? 0 : 1; ImGui.SetNextItemWidth(90);
                        if (ImGui.Combo("##operator", ref operation, MT("operators", "AND\0OR\0"))) { node.Kind = operation == 0 ? TabletRuleKind.All : TabletRuleKind.Any; changed = true; }
                        ImGui.SameLine();
                        if (ImGui.SmallButton(MT("add_condition", "+ Condition"))) { node.Children.Add(new() { Kind = TabletRuleKind.SubRank }); changed = true; }
                        if (ImGui.GetContentRegionAvail().X > 130) ImGui.SameLine();
                        ImGui.BeginDisabled(depth >= TabletFilterEngine.MaximumDepth);
                        if (ImGui.SmallButton(MT("add_group", "+ Group"))) { node.Children.Add(new()); changed = true; }
                        ImGui.EndDisabled();
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            ImGui.PushID(i);
                            if (ImGui.SmallButton("X##remove")) { node.Children.RemoveAt(i--); changed = true; ImGui.PopID(); continue; }
                            ImGui.SameLine(); changed |= DrawMetaNode(node.Children[i], type, depth + 1); ImGui.PopID();
                        }
                    }
                    finally { ImGui.TreePop(); }
                }
                else
                {
                    int kind = (int)node.Kind - (int)TabletRuleKind.Modifier; ImGui.SetNextItemWidth(Math.Min(240, ImGui.GetContentRegionAvail().X));
                    if (ImGui.Combo("##condition", ref kind, MT("conditions", "Specific modifier\0Exact rank\0Exact subrank\0Minimum count\0No rank below\0")))
                    {
                        node.Kind = (TabletRuleKind)(kind + (int)TabletRuleKind.Modifier);
                        if (node.Kind is not (TabletRuleKind.Modifier or TabletRuleKind.SubRank)) node.MinimumRoll = null;
                        changed = true;
                    }
                    if (node.Kind == TabletRuleKind.Modifier)
                    {
                        var selected = MetaCatalog.Find(type, node.ModifierId); ImGui.SetNextItemWidth(-1);
                        if (ImGui.BeginCombo("##modifier", selected == null ? MT("choose_mod", "Choose a modifier") : selected.RankLabel + " · " + MetaDisplayText(selected)))
                        {
                            foreach (var modifier in MetaCatalog.ForType(type).Where(m => m.Availability == "active"))
                            {
                                if (ImGui.Selectable(modifier.RankLabel + " · " + MetaDisplayText(modifier) + "##" + modifier.Id, modifier == selected)) { node.ModifierId = modifier.Id; changed = true; }
                                if (ImGui.IsItemHovered()) MetaTooltip(modifier);
                            }
                            ImGui.EndCombo();
                        }
                    }
                    else if (node.Kind == TabletRuleKind.SubRank)
                    {
                        ImGui.SetNextItemWidth(110);
                        if (ImGui.BeginCombo("##subrank", node.RankLabel))
                        {
                            foreach (var modifier in MetaCatalog.ForType(type).Where(m => m.Availability == "active"))
                            {
                                if (ImGui.Selectable(modifier.RankLabel, modifier.RankLabel == node.RankLabel)) { node.RankLabel = modifier.RankLabel; changed = true; }
                                if (ImGui.IsItemHovered()) MetaTooltip(modifier);
                            }
                            ImGui.EndCombo();
                        }
                    }
                    else
                    {
                        int rank = Math.Max(0, MetaCatalog.RankOrder.IndexOf(node.Rank)); ImGui.SetNextItemWidth(90);
                        if (ImGui.Combo("##rank", ref rank, string.Join('\0', MetaCatalog.RankOrder) + '\0')) { node.Rank = MetaCatalog.RankOrder[rank]; changed = true; }
                        if (node.Kind == TabletRuleKind.MinimumCount)
                        {
                            ImGui.SetNextItemWidth(120); changed |= ImGui.SliderInt(MT("number", "At least"), ref node.Minimum, 1, 4);
                            changed |= ImGui.Checkbox(MT("exact_count", "Count this exact rank only (otherwise: this rank or better)"), ref node.ExactCountRank);
                        }
                    }
                    if (node.Kind is TabletRuleKind.Modifier or TabletRuleKind.SubRank)
                    {
                        bool requireRoll = node.MinimumRoll != null;
                        if (ImGui.Checkbox(MT("minimum_roll", "Minimum roll"), ref requireRoll))
                        { node.MinimumRoll = requireRoll ? 0f : null; changed = true; }
                        if (node.MinimumRoll is { } minimum)
                        {
                            ImGui.SameLine(); ImGui.SetNextItemWidth(100);
                            if (ImGui.InputFloat("##minimumRoll", ref minimum)) { node.MinimumRoll = minimum; changed = true; }
                        }
                    }
                }
            }
            finally { ImGui.PopID(); }
            return changed;
        }

        private string MetaSummary(TabletRuleNode node, string type, int depth = 0)
        {
            if (node == null || depth > TabletFilterEngine.MaximumDepth) return "?";
            string summary = node.Kind switch
            {
                TabletRuleKind.All or TabletRuleKind.Any => "(" + string.Join(" " + MT(node.Kind == TabletRuleKind.All ? "and" : "or", node.Kind == TabletRuleKind.All ? "AND" : "OR") + " ",
                    (node.Children ?? new()).Select(c => MetaSummary(c, type, depth + 1))) + ")",
                TabletRuleKind.Modifier => (MetaCatalog.Find(type, node.ModifierId)?.RankLabel ?? "?") + " [" + (MetaCatalog.Find(type, node.ModifierId)?.Code ?? "?") + "]",
                TabletRuleKind.SubRank => node.RankLabel,
                TabletRuleKind.RankExact => MT("rank", "Rank") + " " + node.Rank,
                TabletRuleKind.MinimumCount => MT("at_least", "At least") + " " + node.Minimum + " " + MT("mods", "modifiers") + " " + node.Rank + " " + MT(node.ExactCountRank ? "exact" : "or_better", node.ExactCountRank ? "exact" : "or better"),
                TabletRuleKind.NoRankBelow => MT("no_below", "No modifier below") + " " + node.Rank,
                _ => "?"
            };
            return summary + (node.MinimumRoll is { } minimum ? " (" + MT("minimum_roll", "Minimum roll") + " ≥ " + minimum + ")" : "");
        }

        private void MetaTooltip(TabletMetaModifier modifier)
        {
            if (!Settings.TabletHelpTooltips || !ImGuiHelper.HelpTooltipsEnabled) return;
            if (!ImGui.BeginTooltip()) return;
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 38);
            ImGui.TextUnformatted(modifier.CanonicalType + " · " + modifier.RankLabel + " · " + modifier.Code);
            ImGui.TextUnformatted(modifier.FrenchText); ImGui.TextUnformatted(modifier.EnglishText);
            ImGui.TextUnformatted(modifier.Justification); ImGui.TextUnformatted(modifier.RankOrigin);
            ImGui.TextUnformatted(modifier.Source); ImGui.TextUnformatted(modifier.AffixSource); ImGui.TextUnformatted(modifier.Strategy);
            ImGui.PopTextWrapPos(); ImGui.EndTooltip();
        }

        private void DrawMetaCatalog(string type, TabletProfile settings, bool manual)
        {
            bool changed = ImGui.InputText(MT("search", "Search French or English"), ref settings.MetaSearch, 200);
            changed |= ImGui.Checkbox(MT("show_both", "Show both French and English texts"), ref settings.ShowMetaEnglish);
            var modifiers = MetaCatalog.ForType(type).Where(m => string.IsNullOrWhiteSpace(settings.MetaSearch) ||
                m.FrenchText.Contains(settings.MetaSearch, StringComparison.OrdinalIgnoreCase) || m.EnglishText.Contains(settings.MetaSearch, StringComparison.OrdinalIgnoreCase) ||
                m.Code.Contains(settings.MetaSearch, StringComparison.OrdinalIgnoreCase) || m.RankLabel.Equals(settings.MetaSearch, StringComparison.OrdinalIgnoreCase));
            if (!manual && ImGui.CollapsingHeader(MT("sources", "About the rankings and sources")))
            {
                MetaText(MT("editorial", "Individual subranks come from the editorial workbook, not an official GGG or streamer tier list. Legacy combination grades remain separate."));
                foreach (var note in MetaCatalog.MethodAndSources)
                    MetaText(string.Join(" · ", note.Values.Where(v => !string.IsNullOrWhiteSpace(v))));
            }
            var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | (manual ? ImGuiTableFlags.None : ImGuiTableFlags.ScrollY);
            var tableSize = manual ? Vector2.Zero : new Vector2(0, Math.Clamp(ImGui.GetContentRegionAvail().Y * .5f, 180f, 340f));
            if (ImGui.BeginTable("MetaCatalogue" + manual, manual ? 5 : 4, tableFlags, tableSize))
            {
                if (manual) ImGui.TableSetupColumn(MT("present", "Present"), ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableSetupColumn(MT("rank", "Rank"), ImGuiTableColumnFlags.WidthFixed, 55);
                ImGui.TableSetupColumn(MT("code", "Code"), ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableSetupColumn(MT("affix", "Affix"), ImGuiTableColumnFlags.WidthFixed, 75);
                ImGui.TableSetupColumn(MT("modifier", "Modifier")); ImGui.TableSetupScrollFreeze(0, 1); ImGui.TableHeadersRow();
                foreach (var modifier in modifiers)
                {
                    ImGui.PushID(modifier.Id); ImGui.TableNextRow();
                    if (manual)
                    {
                        ImGui.TableNextColumn(); bool selected = settings.ManualMetaModifiers.Contains(modifier.Id);
                        ImGui.BeginDisabled(!selected && settings.ManualMetaModifiers.Count >= 4);
                        if (ImGui.Checkbox("##present", ref selected)) { if (selected) settings.ManualMetaModifiers.Add(modifier.Id); else settings.ManualMetaModifiers.Remove(modifier.Id); changed = true; }
                        ImGui.EndDisabled();
                    }
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(modifier.RankLabel);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(modifier.Code);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(MT(modifier.Affix, modifier.Affix));
                    ImGui.TableNextColumn(); MetaText(MetaDisplayText(modifier));
                    if (ImGui.IsItemHovered()) MetaTooltip(modifier);
                    if (settings.ShowMetaEnglish) MetaText(MetaDisplayText(modifier) == modifier.FrenchText ? modifier.EnglishText : modifier.FrenchText);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            if (changed) SaveSettings();
        }

        private void DrawMetaAnalysis(string type, TabletProfile settings)
        {
            var active = ActiveMetaProfile(settings);
            ImGui.TextDisabled(MT("analysis", "Analysis"));
            TabletHelp(MT("hover_hint", "Hover a tablet in the stash, inventory or merchant, then open this tab."));
            if (lastMetaObservation?.TabletType == type)
            {
                ImGui.TextUnformatted(MT("last_hover", "Last observed tablet") + " : " + lastMetaItemName + " · " + lastMetaObservedUtc.ToLocalTime().ToString("HH:mm:ss"));
                DrawTabletQuality(lastMetaObservation, settings);
                if (active != null) DrawMetaResult(TabletFilterEngine.Evaluate(active, MetaCatalog, lastMetaObservation));
                if (ImGui.SmallButton(MT("copy_manual", "Copy known modifiers to manual analysis")))
                { settings.ManualMetaModifiers = lastMetaObservation.Mods.Where(m => m.Modifier != null).Select(m => m.Modifier.Id).Distinct().Take(4).ToList(); SaveSettings(); }
            }
            else ImGui.TextDisabled(MT("no_hover", "No observed tablet for this type yet."));
            if (ImGui.CollapsingHeader(MT("manual", "Manual analysis / correction (does not alter the game)")))
            {
                DrawMetaCatalog(type, settings, true);
                var observation = new TabletObservation { TabletType = type };
                foreach (string id in settings.ManualMetaModifiers) observation.Mods.Add(new() { RawId = id, Modifier = MetaCatalog.Find(type, id) });
                DrawTabletQuality(observation, settings);
                if (active != null) DrawMetaResult(TabletFilterEngine.Evaluate(active, MetaCatalog, observation));
            }
        }

        private void DrawTabletQuality(TabletObservation observation, TabletProfile settings, bool compact = false)
        {
            var priorities = TabletPriorities(settings, observation.TabletType);
            var quality = TabletQualityEngine.Evaluate(observation, priorities);
            MetaText((compact ? MT("hover_badge", "Badge") : MT("priority_badge", "Badge (matching selected modifiers)")) + " : " + quality.PriorityBadgeLabel);
            bool accepted = quality.PriorityCount > 0 && (!settings.RequireAllRankedMods || quality.PriorityCount == priorities.Count);
            if (compact)
            {
                MetaText((settings.RequireAllRankedMods ? "ET / AND" : "OU / OR") + " : " + (accepted ? "OK" : "NON"));
                if (quality.Uncertain) MetaText(MT("hover_incomplete", "Incomplete observation"));
                return;
            }
            MetaText(MT("composition", "Composition") + " : " + (quality.Composition?.Label ?? "N/C"));
            MetaText(MT("global_grade", "Global grade (your priorities)") + " : " + (quality.Rank.Length > 0 ? quality.Rank : "N/C"));
            MetaText(MT("global_position", "Global position") + " : " + MT("position_unverified", "Unavailable: combination restrictions are not fully verified."));
            MetaText(MT("quality_counts", "Matching priorities / best grade / upper bound for that grade") + " : " +
                quality.PriorityCount + " / " + quality.BestCount + " / <= " + quality.MaximumBestCountUpperBound);
            if (!compact) MetaText(MT("quality_limit", "The upper bound respects known exclusion groups and four affixes. Prefix/suffix budgets, item-level availability and special crafting restrictions are not fully verified: no exact maximum or global ordinal is claimed. Rolls only determine whether a minimum is met; this is not a price estimate."));
            foreach (var detected in observation.Mods)
            {
                if (detected?.Modifier == null) { MetaText(MT("unknown", "Unknown modifier") + " : " + detected?.RawId); continue; }
                var modifier = detected.Modifier;
                var ranked = quality.Modifiers.FirstOrDefault(m => m.Id == modifier.Id);
                string selection = ranked?.IsPriority == true ? MT("quality_selected", "Selected") : MT("quality_secondary", "Secondary reference only; not a matching priority");
                string minimum = priorities.TryGetValue(modifier.Id, out var priority) ? " · " + MT("configured_rank", "Choice") + " " + priority.Rank +
                    (priority.Minimum.HasValue ? " · min " + priority.Minimum + " · " + (detected.Roll.HasValue ? detected.Roll.Value.ToString() : "?") : "") : "";
                MetaText((ranked?.Rank ?? "?") + " · " + selection + " · " + MT("reference_rank", "Reference") + " " + modifier.RankLabel + minimum + " · " + MetaDisplayText(modifier));
            }
            MetaText((settings.RequireAllRankedMods ? "ET / AND" : "OU / OR") + " : " + (accepted ? "OK" : "NON"));
            MetaText(MT("quality_order", "Sort order: global grade, count of matching priorities of that grade, secondary grades, then individual reference subranks. Equal quality stays tied. Composition is not a drop probability or a unique item identifier."));
            if (quality.Composition == null) MetaText(MT("composition_unknown", "N/C: the effective grades of exactly four distinct, readable modifiers are required."));
            if (quality.RankingValidation == TabletRankingValidation.IncompatibleObservation)
                MetaText(MT("composition_conflict", "Known modifier-group conflict: composition describes observations without certifying a feasible combination."));
            if (quality.Uncertain) MetaText(MT("quality_uncertain", "Incomplete or incompatible observation: the letter uses only confirmed matches, and no complete quality comparison is guaranteed."));
        }

        private bool ObserveHoveredTablet(string name, string type, Vector2 pos, Vector2 size, Mods mods,
            TabletProfile settings, TabletFilterResult result = null)
        {
            var mouse = ImGui.GetMousePos();
            if (ImGui.GetIO().WantCaptureMouse || tabletHighlightsHiddenManually || IsTabletOccluded(pos, size) ||
                mouse.X < pos.X || mouse.Y < pos.Y || mouse.X >= pos.X + size.X || mouse.Y >= pos.Y + size.Y) return false;
            lastMetaObservation = result?.Observation ?? ObserveMetaTablet(type, mods);
            lastMetaItemName = name; lastMetaObservedUtc = DateTime.UtcNow;
            DrawNativeTabletTooltipRanks(lastMetaObservation, settings, mods);
            if (Settings.TabletHelpTooltips && Settings.TabletHoverDetailsMode > 0)
            {
                bool compact = Settings.TabletHoverDetailsMode != 2;
                float width = Math.Max(100, Math.Min(compact ? 300 : 460, ImGui.GetIO().DisplaySize.X - 20));
                ImGui.SetNextWindowSize(new Vector2(width, 0), ImGuiCond.Always);
                ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width,
                    Math.Max(200, ImGui.GetIO().DisplaySize.Y - 20)));
                if (ImGui.BeginTooltip())
                {
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width - ImGui.GetStyle().WindowPadding.X * 2);
                    MetaText(name);
                    DrawTabletQuality(lastMetaObservation, settings ?? new TabletProfile(), compact);
                    if (!compact && result != null)
                    {
                        MetaText(MT("advanced", "Advanced conditions (optional)") + " : " +
                            MT(result.State.ToString().ToLowerInvariant(), result.State.ToString().ToUpperInvariant()));
                        MetaText(MT("action", "Action") + " : " + MT(result.Action.ToString().ToLowerInvariant(), result.Action.ToString()));
                        if (result.Error.Length > 0) MetaText(MetaError(result.Error));
                    }
                    ImGui.PopTextWrapPos(); ImGui.EndTooltip();
                }
            }
            return true;
        }

        // Match game labels rather than assuming that UI and native modifier orders agree.
        private static string TabletTooltipTextKey(string text)
        {
            // Native item text contains links such as [Gold|Or] and value/style tags.
            text = System.Text.RegularExpressions.Regex.Replace(text ?? "", @"\[(?:[^\[\]|]*\|)?([^\[\]|]+)\]", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^<>]*>", "");
            // Rolls may be written as "1", "un/une" or "a/an". Keep the full word
            // sequence, but ignore articles and regular plural endings when matching.
            text = System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"\b(un|une|a|an)\b", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\p{L}{3,}", m => m.Value.TrimEnd('s'));
            return new string(text.Normalize(System.Text.NormalizationForm.FormD)
                .Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
        }

        private (int Start, int Count)? FindTabletTooltipModLine(string text, TabletMod definition, TabletMetaModifier modifier)
        {
            string localized = ModDisplayName(definition.Id, definition.Name);
            // Compound catalog entries include shared waystone/pack-size bonuses.
            // Only their distinctive encounter line identifies the modifier itself.
            var primaryLines = localized.Contains('\n') ? localized.Split('\n').Where(line =>
                line.StartsWith("La Carte contient ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("La Carte est habitée par ", StringComparison.OrdinalIgnoreCase)) : Array.Empty<string>();
            var templates = new[] { definition.Name, primaryLines.Any() ? "" : localized, modifier.EnglishText, modifier.FrenchText }.Concat(primaryLines)
                .Select(TabletTooltipTextKey).Where(s => s.Length >= 12).Distinct().ToArray();
            var lines = text.Replace("\r", "").Split('\n');
            (int Start, int Count)? match = null;
            for (int start = 0; start < lines.Length; start++)
            {
                for (int count = 1; count <= Math.Min(3, lines.Length - start); count++)
                    if (templates.Contains(TabletTooltipTextKey(string.Join(" ", lines.Skip(start).Take(count)))))
                    {
                        if (match.HasValue) return null;
                        match = (start, count);
                        break;
                    }
            }
            return match;
        }

        private void DrawNativeTabletTooltipRanks(TabletObservation observation, TabletProfile profile, Mods mods)
        {
            if (tabletTooltipLabels.Count == 0 || observation?.Mods.Count is not (>= 1 and <= 4)) return;
            var quality = TabletQualityEngine.Evaluate(observation, TabletPriorities(profile, observation.TabletType));
            var rows = new List<(Vector2 Position, Vector2 Size, string Rank)>();
            foreach (var detected in observation.Mods)
            {
                if (detected?.Modifier == null) continue;
                var definition = TabletRankingDefinitions.FirstOrDefault(d => d.TabletTypes.Contains(observation.TabletType) &&
                    detected.Modifier.GameModIds.Contains(d.Id));
                var ranked = quality.Modifiers.FirstOrDefault(m => m.Id == detected.Modifier.Id);
                if (definition == null || ranked == null) continue;
                var matches = new List<(Vector2 Position, Vector2 Size)>();
                foreach (var label in tabletTooltipLabels)
                {
                    var line = FindTabletTooltipModLine(label.Text, definition, detected.Modifier);
                    if (!line.HasValue) continue;
                    float height = label.Size.Y / (label.Text.Count(c => c == '\n') + 1);
                    matches.Add((label.Position + new Vector2(0, line.Value.Start * height),
                        new Vector2(label.Size.X, height * line.Value.Count)));
                }
                var unique = matches.Distinct().ToArray();
                if (unique.Length == 1) rows.Add((unique[0].Position, unique[0].Size, ranked.Rank));
            }
            if (rows.Count == 0) return;
            var first = new Vector2(rows.Min(r => r.Position.X), rows.Min(r => r.Position.Y));
            var last = new Vector2(rows.Max(r => r.Position.X + r.Size.X), rows.Max(r => r.Position.Y + r.Size.Y));
            var panels = (tabletTooltipPanel.HasValue ? new[] { tabletTooltipPanel.Value }.AsEnumerable() : tabletOcclusions)
                .Where(p => p.Size.X >= 160 && p.Size.Y >= 60 &&
                p.Position.X <= first.X && p.Position.Y + 24 <= first.Y &&
                p.Position.X + p.Size.X >= last.X && p.Position.Y + p.Size.Y >= last.Y)
                .OrderBy(p => p.Size.X * p.Size.Y).ToArray();
            if (panels.Length == 0) return;
            var panel = panels[0];
            float font = Math.Clamp(rows.Min(r => r.Size.Y) * .8f, 12, 20);
            var classification = ClassifyTabletMods(mods, profile, observation.TabletType);
            string total = string.IsNullOrEmpty(classification.RankLabel) ? "N/C" : classification.RankLabel;
            float totalWidth = ImGui.CalcTextSize(total).X * font / ImGui.GetFontSize() + 8;
            var titles = tabletTooltipLabels.Where(label => label.Position.Y >= panel.Position.Y &&
                label.Position.Y < Math.Min(first.Y, panel.Position.Y + 40) &&
                label.Position.X >= panel.Position.X && label.Position.X + label.Size.X < panel.Position.X + panel.Size.X).ToArray();
            float titleEnd = titles.Length > 0 ? titles.Max(label => label.Position.X + label.Size.X) : panel.Position.X + panel.Size.X / 2;
            float totalHeight = ImGui.CalcTextSize(total).Y * font / ImGui.GetFontSize() + 4;
            float totalY = titles.Length > 0 ?
                (titles.Min(label => label.Position.Y) + titles.Max(label => label.Position.Y + label.Size.Y) - totalHeight) / 2 : panel.Position.Y + 8;
            float rightEdge = panel.Position.X + panel.Size.X - 8;
            if (rightEdge - titleEnd >= totalWidth + 8)
                DrawTabletTooltipRank(new Vector2((titleEnd + rightEdge - totalWidth) / 2, totalY), total, font, classification.RankColor);
            foreach (var row in rows)
            {
                float width = ImGui.CalcTextSize(row.Rank).X * font / ImGui.GetFontSize() + 8;
                float textEnd = row.Position.X + row.Size.X;
                if (rightEdge - textEnd < width + 4) continue;
                float x = (textEnd + rightEdge - width) / 2;
                DrawTabletTooltipRank(new Vector2(x, row.Position.Y + (row.Size.Y - font - 4) / 2),
                    row.Rank, font, TabletItemRankColor(row.Rank));
            }
        }

        private static void DrawTabletTooltipRank(Vector2 position, string rank, float font, Vector4 color)
        {
            var size = ImGui.CalcTextSize(rank) * font / ImGui.GetFontSize() + new Vector2(8, 4);
            var display = ImGui.GetIO().DisplaySize;
            if (position.X < 0 || position.Y < 0 || position.X + size.X > display.X || position.Y + size.Y > display.Y) return;
            var draw = ImGui.GetBackgroundDrawList();
            draw.AddRectFilled(position, position + size, ImGuiHelper.Color(new Vector4(.04f, .05f, .07f, .94f)), 3);
            draw.AddRect(position, position + size, ImGuiHelper.Color(color), 3);
            draw.AddText(ImGui.GetFont(), font, position + new Vector2(4, 2), ImGuiHelper.Color(color), rank);
        }

        private void DrawMetaResult(TabletFilterResult result)
        {
            MetaText(MT("combination", "Detected individual reference subranks") + " : " + string.Join(" + ", result.Observation?.Mods.Select(m => m.Modifier?.RankLabel ?? "?") ?? Array.Empty<string>()));
            foreach (var mod in result.Observation?.Mods ?? new()) MetaText(mod.Modifier == null ? MT("unknown", "Unknown modifier") + " : " + mod.RawId : mod.Modifier.RankLabel + " · " + MetaDisplayText(mod.Modifier));
            ImGui.TextColored(MetaResultColor(result), MT(result.State.ToString().ToLowerInvariant(), result.State.ToString().ToUpperInvariant()));
            if (result.Error.Length > 0) MetaText(MetaError(result.Error));
            if (result.Observation?.Uncertain == true) MetaText(MT("uncertain_help", "Unknown, duplicated, unreadable or excessive modifiers: no automatic keep/reject decision. Manual analysis remains available."));
            void Draw(TabletNodeEvaluation node, int depth)
            {
                if (node == null || depth > TabletFilterEngine.MaximumDepth) return;
                string status = MT(node.Matches == true ? "satisfied" : node.Matches == false ? "missing" : "uncertain", node.Matches == true ? "Satisfied" : node.Matches == false ? "Not satisfied" : "Uncertain");
                MetaText(new string(' ', depth * 2) + status + " : " + MetaSummary(node.Node, result.Observation.TabletType) + (node.Node.Kind == TabletRuleKind.MinimumCount ? " (" + node.Count + "/" + node.Node.Minimum + ")" : ""));
                foreach (var child in node.Children) Draw(child, depth + 1);
            }
            Draw(result.Rule, 0); MetaText(MT("action", "Action") + " : " + MT(result.Action.ToString().ToLowerInvariant(), result.Action.ToString()));
        }

        private string MetaError(string code) => MT("error." + code, code);
        private static Vector4 MetaResultColor(TabletFilterResult result) => result.State switch
        { TabletFilterState.Accepted => new(.4f, .86f, .57f, 1), TabletFilterState.Rejected => new(.95f, .4f, .35f, 1), _ => new(1, .73f, .3f, 1) };

        private void DrawMetaExchange(string type, TabletProfile settings)
        {
            if (ImGui.Button(MT("export", "Export profiles for this type")))
            {
                if (settings.MetaProfiles.Any(p => TabletFilterEngine.Validate(p, MetaCatalog).Length > 0)) tabletProfileMessage = MT("export_invalid", "Complete all profiles before exporting.");
                else
                {
                    try
                    {
                    tabletProfileJson = TabletFilterEngine.Export(new() { TabletType = type, CatalogSha256 = MetaCatalog.SourceSha256, ActiveProfileId = settings.ActiveMetaProfileId, Enabled = settings.UseMetaFilters, Profiles = settings.MetaProfiles });
                    string directory = Path.Join(DllDirectory, "config"); Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Join(directory, "tablet-profiles-" + type.Replace(' ', '-') + ".json"), tabletProfileJson);
                    tabletProfileMessage = MT("exported", "Export saved in the plugin config folder. JSON can also be copied below.");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
                    { tabletProfileMessage = MT("export_failed", "Export failed. Profiles remain saved in settings."); }
                }
            }
            if (ImGui.Button(MT("copy_json", "Copy JSON"))) ImGui.SetClipboardText(tabletProfileJson);
            ImGui.InputTextMultiline("##TabletProfilesJson", ref tabletProfileJson, 1024 * 1024, new Vector2(-1, 180));
            if (ImGui.Button(MT("import", "Import JSON (replace profiles for this type)")))
            {
                try
                {
                    var archive = TabletFilterEngine.Import(tabletProfileJson, MetaCatalog, type);
                    settings.MetaProfiles = archive.Profiles; settings.ActiveMetaProfileId = archive.ActiveProfileId ?? ""; settings.UseMetaFilters = archive.Enabled; SaveSettings();
                    tabletProfileMessage = MT(archive.CatalogSha256 == MetaCatalog.SourceSha256 ? "imported" : "import_changed", archive.CatalogSha256 == MetaCatalog.SourceSha256 ? "Profiles imported." : "Profiles imported from another catalogue revision: review the ranks.");
                }
                catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or FormatException) { tabletProfileMessage = MT("import_failed", "Import refused; current profiles are kept.") + " " + MetaError(ex is FormatException ? ex.Message : "import-schema"); }
            }
            if (tabletProfileMessage.Length > 0) MetaText(tabletProfileMessage);
        }

        private TabletFilterResult AnalyzeMetaTablet(string type, Mods mods)
        {
            Settings.TabletProfiles.TryGetValue(type ?? "", out var settings);
            return TabletFilterEngine.Evaluate(settings == null ? null : ActiveMetaProfile(settings), MetaCatalog,
                ObserveMetaTablet(type, mods));
        }

        private static TabletObservation ObserveMetaTablet(string type, Mods mods)
        {
            var observation = MetaCatalog.Recognize(type, mods?.ExplicitMods.Select(m => m.name),
                mods != null && mods.Rarity != ExileCenter.RemoteEnums.Rarity.Unique);
            for (int i = 0; mods != null && i < observation.Mods.Count; i++)
            {
                var raw = mods.ExplicitMods[i];
                var definition = Data.ModDatabase.AllTabletMods.FirstOrDefault(m =>
                    m.TabletTypes.Contains(type) && MatchesModId(raw.name, m.Id));
                float value = definition?.RollValueIndex == 1 ? raw.values.value1 : raw.values.value0;
                // Cost reductions and slower fog have negative native stats, but
                // user minimums use their positive magnitude, as in PassesModRoll.
                value = Math.Abs(value) / (definition?.RollDivisor ?? 1f);
                observation.Mods[i].Roll = float.IsFinite(value) ? value : null;
            }
            return observation;
        }

        private void DrawMetaTabletHighlight(string name, string type, Vector2 pos, Vector2 size, Mods mods)
        {
            var result = AnalyzeMetaTablet(type, mods);
            Settings.TabletProfiles.TryGetValue(type, out var settings);
            bool hovered = ObserveHoveredTablet(name, type, pos, size, mods, settings, result);
            bool occluded = IsTabletOccluded(pos, size);
            if (TabletHighlightsSuppressed() || occluded || (Settings.HideTabletHighlightsOnItemHover && hovered)) return;
            if (result.Action == TabletFilterAction.Ignore || (result.State == TabletFilterState.Rejected && Settings.DisableBadTabletHighlight)) return;
            var classification = ClassifyTabletMods(mods, settings, type);
            if (!MatchesTabletSelection(mods, settings, type))
            {
                if (!Settings.DisableBadTabletHighlight)
                {
                    DrawTabletCompositionBadge(pos, size, classification, size.X / 52f, Math.Max(2, Settings.BorderMargin * size.X / 52f));
                    if (!classification.Uncertain) DrawTabletFilterMarker(pos, size, "x", size.X / 52f);
                }
                return;
            }
            var color = classification.IsRanked ? classification.RankColor : MetaResultColor(result);
            float scale = size.X / 52f;
            if (Settings.ShowTabletModBorder) ImGui.GetBackgroundDrawList().AddRect(pos + Vector2.One, pos + size - Vector2.One, ImGuiHelper.Color(color), 3, ImDrawFlags.RoundCornersAll, Math.Clamp(scale, 1, 2));
            if (Settings.ShowRarityBorder && mods != null) DrawItemRarityMarker(pos, size, scale, Settings.ShowTabletModBorder ? Math.Clamp(scale, 1, 2) : 0, mods.Rarity);
            DrawTabletCompositionBadge(pos, size, classification, scale, Math.Max(2, Settings.BorderMargin * scale));
            string marker = result.State is TabletFilterState.Uncertain or TabletFilterState.Invalid ? "?" :
                result.Action == TabletFilterAction.Signal ? "!" : result.State == TabletFilterState.Rejected ? "x" : "";
            if (!classification.Uncertain) DrawTabletFilterMarker(pos, size, marker, scale);
        }

        private void DrawItemRarityMarker(Vector2 pos, Vector2 size, float margin, float borderThickness, ExileCenter.RemoteEnums.Rarity rarity)
        {
            Vector4 color = rarity switch
            {
                ExileCenter.RemoteEnums.Rarity.Normal => Settings.NormalRarityColor,
                ExileCenter.RemoteEnums.Rarity.Magic => Settings.MagicRarityColor,
                ExileCenter.RemoteEnums.Rarity.Rare => Settings.RareRarityColor,
                ExileCenter.RemoteEnums.Rarity.Unique => new(1, .5f, 0, 1),
                _ => Settings.NormalRarityColor
            };
            float offset = margin + borderThickness, indicator = Settings.RarityIndicatorSize;
            var p0 = pos + new Vector2(size.X - offset - indicator, offset);
            var p1 = pos + new Vector2(size.X - offset, offset);
            var p2 = pos + new Vector2(size.X - offset, offset + indicator);
            ImGui.GetBackgroundDrawList().AddTriangleFilled(p0, p1, p2, ImGuiHelper.Color(color));
        }
    }
}
