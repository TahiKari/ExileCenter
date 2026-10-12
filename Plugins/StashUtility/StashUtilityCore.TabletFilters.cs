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
        private static void MetaText(string text)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
        }
        private static TabletMetaCatalog MetaCatalog => Data.ModDatabase.TabletMeta;
        private static TabletFilterProfile ActiveMetaProfile(TabletProfile settings)
            => settings.MetaProfiles?.FirstOrDefault(p => p != null && p.Id == settings.ActiveMetaProfileId);

        private void DrawTabletMetaFilters(string type, TabletProfile settings)
        {
            settings.MetaProfiles ??= new(); settings.ManualMetaModifiers ??= new(); settings.MetaSearch ??= "";
            ImGui.SeparatorText(MT("title", "Reference workbook · individual modifiers S to F"));
            MetaText(MetaCatalog.SourceFile + " · " + MetaCatalog.Version + " · " + MetaCatalog.ForType(type).Count() + " " + MT("mods", "modifiers"));
            MetaText(MT("editorial", "Individual subranks come from the editorial workbook, not an official GGG or streamer tier list. Legacy combination grades remain separate."));
            if (ImGui.Checkbox(MT("enable", "Use workbook AND / OR filters for this type"), ref settings.UseMetaFilters)) SaveSettings();
            if (ImGui.BeginTabBar("TabletMetaTabs"))
            {
                if (ImGui.BeginTabItem(MT("rules", "Rules")))
                {
                    DrawMetaProfiles(type, settings);
                    var active = ActiveMetaProfile(settings);
                    if (active != null)
                    {
                        bool changed = ImGui.InputText(MT("profile_name", "Profile name"), ref active.Name, 120);
                        changed |= DrawMetaAction(MT("on_match", "When matched"), ref active.OnMatch);
                        changed |= DrawMetaAction(MT("on_miss", "When not matched"), ref active.OnMiss);
                        MetaText(MT("actions_help", "Keep displays the result. Signal adds an amber alert. Ignore hides the indication."));
                        ImGui.Separator();
                        if (ImGui.BeginChild("MetaRuleTree", new Vector2(0, Math.Clamp(ImGui.GetContentRegionAvail().Y - 110, 180, 340)),
                            ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar)) changed |= DrawMetaNode(active.Rule, type, 0);
                        ImGui.EndChild();
                        MetaText(MT("summary", "Rule") + " : " + MetaSummary(active.Rule, type));
                        string error = TabletFilterEngine.Validate(active, MetaCatalog);
                        if (error.Length > 0) ImGui.TextColored(new Vector4(1, .7f, .3f, 1), MT("invalid", "Incomplete or invalid rule") + " : " + MetaError(error));
                        if (changed) SaveSettings();
                    }
                    else MetaText(MT("create_hint", "Create a profile and add conditions or groups. Empty groups never accept a tablet."));
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(MT("catalog", "Catalogue"))) { DrawMetaCatalog(type, settings, false); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(MT("analysis", "Analysis"))) { DrawMetaAnalysis(type, settings); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem(MT("exchange", "Import / export"))) { DrawMetaExchange(type, settings); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            if (!settings.UseMetaFilters) MetaText(MT("legacy", "Legacy settings remain active below. Enable workbook filters to use the profiles above."));
        }

        private void DrawMetaProfiles(string type, TabletProfile settings)
        {
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
                    { node.Kind = (TabletRuleKind)(kind + (int)TabletRuleKind.Modifier); changed = true; }
                    if (node.Kind == TabletRuleKind.Modifier)
                    {
                        var selected = MetaCatalog.Find(type, node.ModifierId); ImGui.SetNextItemWidth(-1);
                        if (ImGui.BeginCombo("##modifier", selected == null ? MT("choose_mod", "Choose a modifier") : selected.RankLabel + " · " + selected.FrenchText))
                        {
                            foreach (var modifier in MetaCatalog.ForType(type).Where(m => m.Availability == "active"))
                            {
                                if (ImGui.Selectable(modifier.RankLabel + " · " + modifier.FrenchText + "##" + modifier.Id, modifier == selected)) { node.ModifierId = modifier.Id; changed = true; }
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
                }
            }
            finally { ImGui.PopID(); }
            return changed;
        }

        private string MetaSummary(TabletRuleNode node, string type, int depth = 0)
        {
            if (node == null || depth > TabletFilterEngine.MaximumDepth) return "?";
            return node.Kind switch
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
        }

        private void MetaTooltip(TabletMetaModifier modifier)
        {
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
            changed |= ImGui.Checkbox(MT("show_english", "Show English text"), ref settings.ShowMetaEnglish);
            var modifiers = MetaCatalog.ForType(type).Where(m => string.IsNullOrWhiteSpace(settings.MetaSearch) ||
                m.FrenchText.Contains(settings.MetaSearch, StringComparison.OrdinalIgnoreCase) || m.EnglishText.Contains(settings.MetaSearch, StringComparison.OrdinalIgnoreCase) ||
                m.Code.Contains(settings.MetaSearch, StringComparison.OrdinalIgnoreCase) || m.RankLabel.Equals(settings.MetaSearch, StringComparison.OrdinalIgnoreCase));
            if (ImGui.BeginTable("MetaCatalogue" + manual, manual ? 4 : 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(0, Math.Clamp(ImGui.GetContentRegionAvail().Y - (manual ? 180 : 16), 160, 390))))
            {
                if (manual) ImGui.TableSetupColumn(MT("present", "Present"), ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableSetupColumn(MT("rank", "Rank"), ImGuiTableColumnFlags.WidthFixed, 55);
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
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(MT(modifier.Affix, modifier.Affix));
                    ImGui.TableNextColumn(); MetaText(modifier.FrenchText);
                    if (ImGui.IsItemHovered()) MetaTooltip(modifier);
                    if (settings.ShowMetaEnglish) MetaText(modifier.EnglishText);
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            if (changed) SaveSettings();
        }

        private void DrawMetaAnalysis(string type, TabletProfile settings)
        {
            var active = ActiveMetaProfile(settings);
            MetaText(MT("hover_hint", "Hover a tablet in the stash, inventory or merchant, then open this tab. The last observed tablet is shown without changing game input."));
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
            foreach (var mod in result.Observation?.Mods ?? new()) MetaText(mod.Modifier == null ? MT("unknown", "Unknown modifier") + " : " + mod.RawId : mod.Modifier.RankLabel + " · " + mod.Modifier.FrenchText);
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
                MetaCatalog.Recognize(type, mods?.ExplicitMods.Select(m => m.name), mods != null && mods.Rarity != GameHelper.RemoteEnums.Rarity.Unique));
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
            string badge = result.State is TabletFilterState.Uncertain or TabletFilterState.Invalid ? "?" :
                result.Action == TabletFilterAction.Signal ? "!" : result.State == TabletFilterState.Accepted ? "OK" : "NON";
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
