namespace StashUtility
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using ImGuiNET;
    using Models;

    public sealed partial class StashUtilityCore
    {
        private string tabletProfileJson = "", tabletProfileMessage = "";
        private TabletObservation lastMetaObservation;
        private string lastMetaItemName = "";
        private DateTime lastMetaObservedUtc;
        private string MT(string key, string fallback) => PluginText.T("stashutility.meta." + key, fallback);
        private static void MetaText(string text) => ImGuiHelper.WrappedText(text);
        private static TabletMetaCatalog MetaCatalog => Data.ModDatabase.TabletMeta;
        private static string MetaDisplayText(TabletMetaModifier modifier) =>
            GameHelper.Localization.OverlayLocalization.CurrentLanguage == GameHelper.Localization.OverlayLanguage.French
                ? modifier.FrenchText : modifier.EnglishText;
        private static TabletFilterProfile ActiveMetaProfile(TabletProfile settings)
            => settings.MetaProfiles?.FirstOrDefault(p => p != null && p.Id == settings.ActiveMetaProfileId);

        private void TabletHelp(string text)
        {
            if (!Settings.TabletHelpTooltips || !ImGuiHelper.HelpTooltipsEnabled) return;
            if (ImGui.GetItemRectMax().X + 30 < ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X) ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            ImGuiHelper.ToolTip(text);
        }

        private void DrawTabletMetaFilters(string type, TabletProfile settings)
        {
            settings.MetaProfiles ??= new(); settings.ManualMetaModifiers ??= new(); settings.MetaSearch ??= "";
            ImGui.TextDisabled(TabletTypeLabel(type));
            bool changedMode = ImGui.Checkbox(MT("enable", "Use workbook AND / OR filters for this type"), ref settings.UseMetaFilters);
            if (changedMode) SaveSettings();
            TabletHelp(MT("rank_and_rules", "The tablet keeps its rank. AND / OR rules choose the combinations to highlight."));
            if (settings.UseMetaFilters)
                ImGui.TextDisabled(MT("active_profile", "Active profile") + " : " + (ActiveMetaProfile(settings)?.Name ?? MT("none", "None")));
            if (changedMode && settings.UseMetaFilters) ImGui.SetNextItemOpen(true);
            if (ImGui.CollapsingHeader(MT("rules_editor", "AND / OR rules and analysis") + "###TabletRulesEditor") && ImGui.BeginTabBar("TabletMetaTabs"))
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
                if (ImGui.BeginTabItem(MT("analysis", "Analysis"))) { DrawMetaAnalysis(type, settings); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(MT("exchange", "Import / export"))) { DrawMetaExchange(type, settings); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
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
                DrawMetaResult(TabletFilterEngine.Evaluate(active, MetaCatalog, lastMetaObservation));
                if (ImGui.SmallButton(MT("copy_manual", "Copy known modifiers to manual analysis")))
                { settings.ManualMetaModifiers = lastMetaObservation.Mods.Where(m => m.Modifier != null).Select(m => m.Modifier.Id).Distinct().Take(4).ToList(); SaveSettings(); }
            }
            else ImGui.TextDisabled(MT("no_hover", "No observed tablet for this type yet."));
            if (ImGui.CollapsingHeader(MT("manual", "Manual analysis / correction (does not alter the game)")))
            {
                DrawMetaCatalog(type, settings, true);
                var observation = new TabletObservation { TabletType = type };
                foreach (string id in settings.ManualMetaModifiers) observation.Mods.Add(new() { RawId = id, Modifier = MetaCatalog.Find(type, id) });
                DrawMetaResult(TabletFilterEngine.Evaluate(active, MetaCatalog, observation));
            }
        }

        private void DrawMetaResult(TabletFilterResult result)
        {
            MetaText(MT("combination", "Detected combination") + " : " + string.Join(" + ", result.Observation?.Mods.Select(m => m.Modifier?.RankLabel ?? "?") ?? Array.Empty<string>()));
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
            var observation = MetaCatalog.Recognize(type, mods?.ExplicitMods.Select(m => m.name),
                mods != null && mods.Rarity != GameHelper.RemoteEnums.Rarity.Unique);
            for (int i = 0; mods != null && i < observation.Mods.Count; i++)
            {
                var raw = mods.ExplicitMods[i];
                var definition = Data.ModDatabase.AllTabletMods.FirstOrDefault(m =>
                    m.TabletTypes.Contains(type) && MatchesModId(raw.name, m.Id));
                float value = definition?.RollValueIndex == 1 ? raw.values.value1 : raw.values.value0;
                value /= definition?.RollDivisor ?? 1f;
                observation.Mods[i].Roll = float.IsFinite(value) ? value : null;
            }
            return TabletFilterEngine.Evaluate(settings == null ? null : ActiveMetaProfile(settings), MetaCatalog,
                observation);
        }

        private void DrawMetaTabletHighlight(string name, string type, Vector2 pos, Vector2 size, Mods mods)
        {
            var result = AnalyzeMetaTablet(type, mods); var mouse = ImGui.GetMousePos();
            bool hovered = !ImGui.GetIO().WantCaptureMouse && mouse.X >= pos.X && mouse.Y >= pos.Y && mouse.X < pos.X + size.X && mouse.Y < pos.Y + size.Y;
            bool occluded = IsTabletOccluded(pos, size);
            if (hovered && !occluded) { lastMetaObservation = result.Observation; lastMetaItemName = name; lastMetaObservedUtc = DateTime.UtcNow; }
            if (TabletHighlightsSuppressed() || occluded || (Settings.HideTabletHighlightsOnItemHover && hovered)) return;
            if (result.Action == TabletFilterAction.Ignore || (result.State == TabletFilterState.Rejected && Settings.DisableBadTabletHighlight)) return;
            var color = result.Action == TabletFilterAction.Signal ? new Vector4(1, .73f, .3f, 1) : MetaResultColor(result); float scale = size.X / 52f;
            if (Settings.ShowTabletModBorder) ImGui.GetBackgroundDrawList().AddRect(pos + Vector2.One, pos + size - Vector2.One, ImGuiHelper.Color(color), 3, ImDrawFlags.RoundCornersAll, Math.Clamp(scale, 1, 2));
            if (Settings.ShowRarityBorder && mods != null) DrawItemRarityMarker(pos, size, scale, Settings.ShowTabletModBorder ? Math.Clamp(scale, 1, 2) : 0, mods.Rarity);
            Settings.TabletProfiles.TryGetValue(type, out var settings);
            var classification = ClassifyTabletMods(mods, settings, type);
            string status = result.State is TabletFilterState.Uncertain or TabletFilterState.Invalid ? "?" :
                result.Action == TabletFilterAction.Signal ? "!" : result.State == TabletFilterState.Accepted ? "OK" : "NON";
            string badge = (classification.IsRanked ? classification.RankLabel : PluginText.T("stashutility.filters.tablet_unclassified", "N/C")) + " · " + status;
            DrawTabletRankBadge(pos, size, badge, scale, Math.Max(2, Settings.BorderMargin * scale));
        }

        private void DrawItemRarityMarker(Vector2 pos, Vector2 size, float margin, float borderThickness, GameHelper.RemoteEnums.Rarity rarity)
        {
            Vector4 color = rarity switch
            {
                GameHelper.RemoteEnums.Rarity.Normal => Settings.NormalRarityColor,
                GameHelper.RemoteEnums.Rarity.Magic => Settings.MagicRarityColor,
                GameHelper.RemoteEnums.Rarity.Rare => Settings.RareRarityColor,
                GameHelper.RemoteEnums.Rarity.Unique => new(1, .5f, 0, 1),
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
