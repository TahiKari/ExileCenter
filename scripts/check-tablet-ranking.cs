// Run against a built StashUtility.dll; all dependencies are resolved from the supplied release directory.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using StashUtility;
using StashUtility.Models;
using StashUtility.Data;
using GameHelper.RemoteObjects.Components;
using Newtonsoft.Json;

internal static class TabletRankingCheck
{
    private static int checks;
    private static int nativeWindows;
    private static bool CountWindow(IntPtr window, IntPtr data) { nativeWindows++; return true; }
    private static bool StopWindows(IntPtr window, IntPtr data) { nativeWindows++; return false; }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        checks++;
    }

    public static int Main(string[] args)
    {
        try
        {
            string root = args[0];
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string file = Path.Combine(root, name.Name + ".dll");
                if (!File.Exists(file)) file = Path.Combine(root, "Plugins", "StashUtility", name.Name + ".dll");
                return File.Exists(file) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(file) : null;
            };
            Run();
            RunMetaFilters(args.Length > 1 ? args[1] : null);
            CheckWorkbookPreset();
            CheckTooltipGeometry(root);
            Console.WriteLine(checks + " tablet classification checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunMetaFilters(string workbookPath)
    {
        var catalog = ModDatabase.TabletMeta;
        Check(catalog.Validate().Count == 0 && catalog.Modifiers.Count == 254, "Complete validated reference catalogue");
        Check(catalog.RankOrder.SequenceEqual("SABCDEF".Select(c => c.ToString())), "Exact S to F order, with E and F distinct");
        Check(catalog.ImportIssues.Count == 0 && catalog.Modifiers.All(m => m.GameModIds.Count > 0), "Every workbook row has a game-identifier mapping");
        foreach (string typeName in catalog.Types.Values)
        {
            var entries = catalog.ForType(typeName).ToArray();
            Check(entries.Length > 0 && entries.Select(m => m.Id).Distinct().Count() == entries.Length, "No duplicate IDs: " + typeName);
            foreach (string rank in catalog.RankOrder)
            {
                var numbers = entries.Where(m => m.Rank == rank).Select(m => m.SubRank).ToArray();
                Check(numbers.SequenceEqual(numbers.OrderBy(n => n)) && numbers.Distinct().Count() == numbers.Length, "Numeric subrank order: " + typeName + " " + rank);
            }
            var testProfile = new TabletFilterProfile { TabletType = typeName, Rule = new() { Kind = TabletRuleKind.SubRank, RankLabel = "S1" } };
            var top = entries.Single(m => m.RankLabel == "S1");
            Check(TabletFilterEngine.Evaluate(testProfile, catalog, catalog.Recognize(typeName, new[] { top.GameModIds[0] + "1" })).State == TabletFilterState.Accepted, "Real game identifiers: " + typeName);
            Check(top.SourceFields["Rang"].ToString() == top.RankLabel && top.SourceFields["Mod FR"].ToString() == top.FrenchText && top.SourceFields["Texte du jeu EN"].ToString() == top.EnglishText, "Original fields retained: " + typeName);
        }

        const string type = "Irradiated Tablet";
        TabletRuleNode Sub(string label) => new() { Kind = TabletRuleKind.SubRank, RankLabel = label };
        TabletRuleNode Group(TabletRuleKind kind, params TabletRuleNode[] nodes) => new() { Kind = kind, Children = nodes.ToList() };
        TabletObservation Obs(params string[] labels) => new() { TabletType = type, Mods = labels.Select(label =>
            new TabletDetectedMod { RawId = label, Modifier = catalog.ForType(type).Single(m => m.RankLabel == label) }).ToList() };
        TabletFilterResult Evaluate(TabletRuleNode rule, params string[] labels) => TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = rule }, catalog, Obs(labels));
        TabletRuleNode Mixed() => Group(TabletRuleKind.Any, Group(TabletRuleKind.All, Sub("S1"), Sub("A1")), Group(TabletRuleKind.All, Sub("S1"), Sub("B1")));
        Check(Evaluate(Mixed(), "S1", "B1", "C1").State == TabletFilterState.Accepted, "(S1 AND A1) OR (S1 AND B1): S1+B1+C1 passes");
        Check(Evaluate(Mixed(), "S1", "A2", "C1").State == TabletFilterState.Rejected, "(S1 AND A1) OR (S1 AND B1): S1+A2+C1 fails");
        Check(Evaluate(Mixed(), "S1", "A1", "B1").State == TabletFilterState.Accepted, "(S1 AND A1) OR (S1 AND B1): S1+A1+B1 passes");
        Check(Evaluate(Group(TabletRuleKind.All, Sub("S1"), Sub("A2")), "S1", "A2", "B1", "D3").State == TabletFilterState.Accepted, "Four modifiers: S1 AND A2");
        Check(Evaluate(Group(TabletRuleKind.All, Sub("S1"), Sub("A1")), "S1", "A2", "B1", "D3").State == TabletFilterState.Rejected, "Four modifiers: missing A1");
        Check(Evaluate(Mixed(), "S1", "A2", "B1", "D3").State == TabletFilterState.Accepted, "Four modifiers: mixed groups pass via B1");
        Check(Evaluate(Group(TabletRuleKind.Any, Sub("S2"), Sub("A1")), "S1", "A2", "B1", "D3").State == TabletFilterState.Rejected, "Four modifiers: S2 OR A1 absent");
        Check(Evaluate(Group(TabletRuleKind.All, Group(TabletRuleKind.Any, Sub("S1"), Sub("S2")), Group(TabletRuleKind.Any, Sub("A1"), Sub("A2"))), "S2", "A2").State == TabletFilterState.Accepted, "(S1 OR S2) AND (A1 OR A2)");
        Check(Evaluate(Group(TabletRuleKind.Any, Group(TabletRuleKind.All, Sub("S1"), Sub("A1"), Sub("B1")), Group(TabletRuleKind.All, Sub("S1"), Sub("A2"))), "S1", "A2").State == TabletFilterState.Accepted, "Three-way AND mixed with two-way AND");
        var sequence = new[] { "S1", "A1", "B1", "D3" };
        for (int count = 1; count <= 4; count++) Check(Evaluate(Sub("S1"), sequence.Take(count).ToArray()).State == TabletFilterState.Accepted, "One to four affixes: " + count);
        foreach (string a in sequence) foreach (string b in sequence.Where(s => s != a))
            foreach (string c in sequence.Where(s => s != a && s != b))
            {
                string d = sequence.Single(s => s != a && s != b && s != c);
                Check(Evaluate(Mixed(), a, b, c, d).State == TabletFilterState.Accepted, "All 24 affix permutations");
            }
        TabletRuleNode Count(string rank, int count, bool exact = false) => new() { Kind = TabletRuleKind.MinimumCount, Rank = rank, Minimum = count, ExactCountRank = exact };
        Check(Evaluate(Count("A", 2), "S1", "S2").State == TabletFilterState.Accepted, "At least two A or better counts S");
        Check(Evaluate(Count("A", 2, true), "S1", "S2").State == TabletFilterState.Rejected, "Exact A count excludes S");
        Check(Evaluate(Count("B", 3), "S1", "A1", "B1").State == TabletFilterState.Accepted, "Three B or better");
        Check(Evaluate(Count("B", 3), "S1", "A1", "C1").State == TabletFilterState.Rejected, "Lower rank not counted");
        Check(Evaluate(Group(TabletRuleKind.All, Count("A", 2), Group(TabletRuleKind.Any, Sub("S1"), Sub("S2"))), "S1", "A2").State == TabletFilterState.Accepted, "Count criteria participate in nested logic");
        Check(Evaluate(Group(TabletRuleKind.Any, Group(TabletRuleKind.All, Sub("S1"), Sub("A1")), Count("B", 3)), "A1", "A2", "B1").State == TabletFilterState.Accepted, "OR accepts the quality-count branch");
        Check(Evaluate(new() { Kind = TabletRuleKind.NoRankBelow, Rank = "C" }, "S1", "A1", "C1").State == TabletFilterState.Accepted, "No rank below C accepts C");
        Check(Evaluate(new() { Kind = TabletRuleKind.NoRankBelow, Rank = "C" }, "S1", "D3").State == TabletFilterState.Rejected, "No rank below C rejects D");
        Check(Evaluate(new() { Kind = TabletRuleKind.RankExact, Rank = "A" }, "S1", "S2").State == TabletFilterState.Rejected, "Exact rank remains distinct from minimum rank");
        Check(Evaluate(Sub("S1"), "S1").State == TabletFilterState.Accepted, "Absence of unchosen useful modifiers is not a penalty");
        Check(Evaluate(Sub("F2"), "S1").State == TabletFilterState.Invalid, "Absent individual subrank is not invented");
        Check(Evaluate(new() { Kind = TabletRuleKind.RankExact, Rank = "Q" }, "S1").State == TabletFilterState.Invalid, "Unknown rank rejected");
        foreach (var kind in new[] { TabletRuleKind.All, TabletRuleKind.Any }) Check(Evaluate(Group(kind), "S1").State == TabletFilterState.Invalid, "Empty logical groups never approve");
        Check(Evaluate(Count("A", 0), "S1").State == TabletFilterState.Invalid && Evaluate(Count("A", 5), "S1").State == TabletFilterState.Invalid, "Invalid count bounds");
        var exactMod = catalog.ForType(type).Single(m => m.RankLabel == "A2");
        Check(Evaluate(new() { Kind = TabletRuleKind.Modifier, ModifierId = exactMod.Id }, "A2").State == TabletFilterState.Accepted, "Exact canonical modifier");
        Check(Evaluate(new() { Kind = TabletRuleKind.Modifier, ModifierId = "Breach Tablet:A2" }, "A2").State == TabletFilterState.Invalid, "Other tablet modifier cannot leak into profile");
        var common = catalog.ForType(type).Single(m => m.Code == "G01");
        Check(catalog.Recognize(type, common.GameModIds).Mods.Single().Modifier.RankLabel == "S2" &&
            catalog.Recognize("Overseer Tablet", common.GameModIds).Mods.Single().Modifier.RankLabel == "S1", "Same native modifier has independent grades per tablet");
        Check(catalog.Recognize(type, new[] { common.GameModIds[0] + "12" }).Mods.Single().Modifier?.Id == common.Id, "Numeric tier suffixes recognized precisely");
        Check(catalog.Recognize(type, new[] { common.GameModIds[0] + "Different" }).Uncertain, "Similar native names do not collide");
        Check(catalog.Recognize(type, new[] { common.FrenchText, common.EnglishText }).Uncertain, "Translations and numeric descriptions do not guess native IDs");
        var profile = new TabletFilterProfile { TabletType = type, Rule = Mixed() };
        var unknown = Obs("S1", "B1"); unknown.Mods.Add(new() { RawId = "UnrecognizedGameMod1" });
        Check(TabletFilterEngine.Evaluate(profile, catalog, unknown).State == TabletFilterState.Uncertain, "Unknown modifiers force explicit uncertainty");
        Check(TabletFilterEngine.Evaluate(profile, catalog, unknown).Action == TabletFilterAction.Signal, "Unknown modifiers always signal instead of silently keeping or ignoring");
        var belowUnknown = Obs("D3"); belowUnknown.Mods.Add(new() { RawId = "UnknownExplicit" });
        var belowResult = TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = new() { Kind = TabletRuleKind.NoRankBelow, Rank = "C" } }, catalog, belowUnknown);
        Check(belowResult.State == TabletFilterState.Uncertain && belowResult.Rule.Matches == false, "Known below-threshold modifier remains a failed condition despite unknown data");
        var spoofed = new TabletObservation { TabletType = type, Mods = new() { new() { RawId = exactMod.Id,
            Modifier = new() { Id = exactMod.Id, TabletType = type, Rank = "S", RankLabel = "S1" } } } };
        Check(TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = new() { Kind = TabletRuleKind.RankExact, Rank = "S" } }, catalog, spoofed).State == TabletFilterState.Rejected, "Observed modifiers use canonical catalogue grades, not supplied rank text");
        var actionProfile = new TabletFilterProfile { TabletType = type, Rule = Sub("S1"), OnMatch = TabletFilterAction.Signal, OnMiss = TabletFilterAction.Keep };
        Check(TabletFilterEngine.Evaluate(actionProfile, catalog, Obs("S1")).Action == TabletFilterAction.Signal &&
            TabletFilterEngine.Evaluate(actionProfile, catalog, Obs("A1")).Action == TabletFilterAction.Keep, "Configured match and miss actions remain independent");
        var duplicates = Obs("S1", "S1");
        Check(TabletFilterEngine.Evaluate(new() { TabletType = type, Rule = Count("S", 2) }, catalog, duplicates).State == TabletFilterState.Uncertain, "Duplicate modifiers never inflate counts");
        var unreadable = Obs("S1"); unreadable.Readable = false;
        Check(TabletFilterEngine.Evaluate(profile, catalog, unreadable).State == TabletFilterState.Uncertain, "Unreadable game data remains uncertain");
        Check(Evaluate(Sub("S1"), "S1", "S2", "A1", "A2", "B1").State == TabletFilterState.Uncertain, "More than four modifiers are not silently accepted");
        var cycle = new TabletRuleNode(); cycle.Children.Add(cycle);
        Check(Evaluate(cycle, "S1").State == TabletFilterState.Invalid, "Cyclic input guarded without recursion failure");
        var duplicateNode = Group(TabletRuleKind.All, Sub("S1"), Sub("S2")); duplicateNode.Children[1].Id = duplicateNode.Children[0].Id;
        Check(Evaluate(duplicateNode, "S1").State == TabletFilterState.Invalid, "Duplicate node IDs rejected");
        var deepest = Sub("S1");
        for (int i = 0; i < TabletFilterEngine.MaximumDepth; i++) deepest = Group(TabletRuleKind.All, deepest);
        Check(Evaluate(deepest, "S1").State == TabletFilterState.Accepted, "Supported deep groups evaluate");
        Check(Evaluate(Group(TabletRuleKind.All, deepest), "S1").State == TabletFilterState.Invalid, "Excessive depth rejected");

        var settings = new StashUtilitySettings();
        foreach (string tabletType in catalog.Types.Values)
        {
            var p = new TabletFilterProfile { TabletType = tabletType, Rule = Sub("S1") };
            settings.TabletProfiles[tabletType] = new() { UseMetaFilters = true, ActiveMetaProfileId = p.Id, MetaProfiles = new() { p }, MetaSearch = "test", ShowMetaEnglish = false };
            settings.TabletProfiles[tabletType].ModRanks["LegacyMod"] = "B";
        }
        var reloaded = JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(settings), new JsonSerializerSettings { MaxDepth = 128 });
        foreach (string tabletType in catalog.Types.Values)
        {
            var saved = reloaded.TabletProfiles[tabletType];
            Check(saved.MetaProfiles.Single().TabletType == tabletType && saved.ActiveMetaProfileId == settings.TabletProfiles[tabletType].ActiveMetaProfileId && saved.MetaSearch == "test" && !saved.ShowMetaEnglish && saved.ModRanks["LegacyMod"] == "B", "Persistent independent profiles and legacy settings: " + tabletType);
        }
        settings.TabletProfiles[type].MetaProfiles[0].Rule = deepest;
        var deepReload = JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(settings), new JsonSerializerSettings { MaxDepth = 128 });
        Check(TabletFilterEngine.Validate(deepReload.TabletProfiles[type].MetaProfiles[0], catalog) == "", "Deep tree persists without truncation");
        var archive = new TabletProfileArchive { TabletType = type, CatalogSha256 = catalog.SourceSha256, Enabled = true, Profiles = new() { profile }, ActiveProfileId = profile.Id };
        var imported = TabletFilterEngine.Import(TabletFilterEngine.Export(archive), catalog, type);
        Check(imported.ActiveProfileId == profile.Id && imported.Enabled && TabletFilterEngine.Evaluate(imported.Profiles.Single(), catalog, Obs("S1", "B1")).State == TabletFilterState.Accepted, "Profile export/import round-trip");
        void Refuse(Action action, string label) { bool failed = false; try { action(); } catch (FormatException) { failed = true; } catch (Newtonsoft.Json.JsonException) { failed = true; } Check(failed, label); }
        Refuse(() => TabletFilterEngine.Import(TabletFilterEngine.Export(archive), catalog, "Breach Tablet"), "Cross-type profile import refused");
        Refuse(() => TabletFilterEngine.Import("{bad json}", catalog, type), "Malformed import refused");
        Refuse(() => TabletFilterEngine.Import(new string('x', 1024 * 1024 + 1), catalog, type), "Oversized import refused");
        archive.Profiles[0].Rule = new();
        Refuse(() => TabletFilterEngine.Import(TabletFilterEngine.Export(archive), catalog, type), "Invalid rule import refused");
        var core = new StashUtilityCore { Settings = settings };
        var analyze = typeof(StashUtilityCore).GetMethod("AnalyzeMetaTablet", BindingFlags.Instance | BindingFlags.NonPublic);
        var nativeMods = new Mods(IntPtr.Zero);
        nativeMods.ExplicitMods.Add((catalog.ForType(type).Single(m => m.RankLabel == "S1").GameModIds[0] + "1", (1f, 0f)));
        var actualResult = (TabletFilterResult)analyze.Invoke(core, new object[] { type, nativeMods });
        Check(actualResult.State == TabletFilterState.Accepted, "Existing Mods reader feeds actual filter adapter");
        nativeMods.ImplicitMods.Add(("UnknownImplicit", (1f, 0f)));
        Check(((TabletFilterResult)analyze.Invoke(core, new object[] { type, nativeMods })).State == TabletFilterState.Accepted, "Non-affix implicit modifiers do not inflate explicit counts");
        nativeMods.ExplicitMods.Add(("UnknownExplicit", (1f, 0f)));
        Check(((TabletFilterResult)analyze.Invoke(core, new object[] { type, nativeMods })).State == TabletFilterState.Uncertain, "Actual adapter exposes unknown explicit affixes");
        var factory = typeof(StashUtilityCore).GetMethod("CreateFubgunTabletProfile", BindingFlags.Static | BindingFlags.NonPublic);
        var fubgun = (TabletFilterProfile)factory.Invoke(null, null);
        Check(TabletFilterEngine.Validate(fubgun, catalog) == "", "Fubgun preset has real workbook IDs and valid AND / OR rules");
        TabletObservation FubgunObs(float? roll, string second) => new()
        {
            TabletType = type, Mods = new()
            {
                new() { Modifier = catalog.Recognize(type, new[] { "TowerMapAdditionalModifier" }).Mods[0].Modifier, Roll = roll },
                new() { Modifier = catalog.Recognize(type, new[] { second }).Mods[0].Modifier, Roll = 30 },
            }
        };
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(2, "TowerMonsterEffectiveness")).State == TabletFilterState.Accepted, "Fubgun: 2 map mods AND effectiveness");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(2, "TowerRarePackIncrease")).State == TabletFilterState.Accepted, "Fubgun: 2 map mods AND rare monsters");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(1, "TowerRarePackIncrease")).State == TabletFilterState.Rejected, "Fubgun: only 1 extra map mod rejected");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(null, "TowerRarePackIncrease")).State == TabletFilterState.Uncertain, "Unreadable minimum roll never accepted");
        Check(TabletFilterEngine.Evaluate(fubgun, catalog, FubgunObs(2, "TowerDroppedItemRarityIncrease")).State == TabletFilterState.Rejected, "Fubgun: required companion modifier missing");
        var fubgunReload = JsonConvert.DeserializeObject<TabletFilterProfile>(JsonConvert.SerializeObject(fubgun));
        Check(fubgunReload.Rule.Children[0].MinimumRoll == 2 && TabletFilterEngine.Evaluate(fubgunReload, catalog, FubgunObs(1, "TowerRarePackIncrease")).State == TabletFilterState.Rejected, "Minimum roll survives profile save and reload");
        var fubgunCore = new StashUtilityCore();
        fubgunCore.Settings.TabletProfiles[type] = new() { UseMetaFilters = true, ActiveMetaProfileId = fubgun.Id, MetaProfiles = new() { fubgun } };
        var rawFubgun = new Mods(IntPtr.Zero);
        rawFubgun.ExplicitMods.Add(("TowerMapAdditionalModifier1", (2, 0)));
        rawFubgun.ExplicitMods.Add(("TowerMonsterEffectiveness1", (12, 0)));
        Check(((TabletFilterResult)analyze.Invoke(fubgunCore, new object[] { type, rawFubgun })).State == TabletFilterState.Accepted, "Runtime adapter reads real mod rolls for AND / OR");
        rawFubgun.ExplicitMods[0] = ("TowerMapAdditionalModifier1", (1, 0));
        Check(((TabletFilterResult)analyze.Invoke(fubgunCore, new object[] { type, rawFubgun })).State == TabletFilterState.Rejected, "Runtime adapter rejects low roll");
        Check(Evaluate(new() { Kind = TabletRuleKind.All, Children = new() { Sub("E1"), new() { Kind = TabletRuleKind.Any, Children = new() { Sub("F1"), Sub("A1") } } } }, "E1", "F1").State == TabletFilterState.Accepted, "E AND (F OR A) accepts E+F");
        Check(Evaluate(new() { Kind = TabletRuleKind.All, Children = new() { Sub("E1"), new() { Kind = TabletRuleKind.Any, Children = new() { Sub("F1"), Sub("A1") } } } }, "E1", "B1").State == TabletFilterState.Rejected, "E AND (F OR A) rejects wrong second modifier");
        if (workbookPath != null)
        {
            Check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(workbookPath))).Equals(catalog.SourceSha256, StringComparison.OrdinalIgnoreCase), "Source workbook unchanged and fingerprint matches embedded data");
            using var zip = System.IO.Compression.ZipFile.OpenRead(workbookPath);
            var ns = System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            System.Xml.Linq.XDocument Xml(string name) { using var stream = zip.GetEntry(name).Open(); return System.Xml.Linq.XDocument.Load(stream); }
            var workbook = Xml("xl/workbook.xml");
            var sheet = workbook.Descendants(ns + "sheet").Single(s => (string)s.Attribute("name") == "TOUS LES MODS");
            string relationship = (string)sheet.Attribute(System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id");
            string target = (string)Xml("xl/_rels/workbook.xml.rels").Root.Elements().Single(e => (string)e.Attribute("Id") == relationship).Attribute("Target");
            target = target.TrimStart('/'); if (!target.StartsWith("xl/")) target = "xl/" + target;
            var rows = Xml(target).Descendants(ns + "row").ToArray();
            string Value(System.Xml.Linq.XElement cell) => (string)cell.Attribute("t") == "inlineStr" ? string.Concat(cell.Descendants(ns + "t").Select(e => e.Value)) : cell.Element(ns + "v")?.Value ?? "";
            string[] columns = rows[0].Elements(ns + "c").Select(Value).ToArray();
            Check(rows.Length - 1 == catalog.Modifiers.Count, "Independent XLSX extraction confirms row count");
            for (int i = 1; i < rows.Length; i++)
            {
                string[] values = rows[i].Elements(ns + "c").Select(Value).ToArray();
                var importedMod = catalog.Modifiers.Single(m => m.CanonicalType == values[0] && m.Code == values[1]);
                for (int j = 0; j < columns.Length; j++)
                    Check(Convert.ToString(importedMod.SourceFields[columns[j]], System.Globalization.CultureInfo.InvariantCulture) == values[j], "Exact XLSX field retained: row " + (i + 1) + " " + columns[j]);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckWorkbookPreset()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic;
        var apply = typeof(StashUtilityCore).GetMethod("ApplyTabletFarmPreset", flags);
        var set = typeof(StashUtilityCore).GetMethod("SetTabletModRank", flags);
        void Set(TabletProfile p, string id, string rank) => set.Invoke(null, new object[] { id, rank, p.ModRanks, p.GoodModPatterns, p.BadModPatterns, p.GodModPatterns });
        var presetCore = new StashUtilityCore();
        foreach (string type in StashUtilitySettings.TabletTypes)
        {
            var p = new TabletProfile(); presetCore.Settings.TabletProfiles[type] = p;
            foreach (var mod in ModDatabase.AllTabletMods.Where(m => m.TabletTypes.Contains(type)))
                Set(p, mod.Id, ModDatabase.GetTabletFarmRank(type, mod.Id) ?? "");
            p.ModRequiredMinRolls["TowerMonsterEffectiveness"] = 14;
        }
        presetCore.Settings.TabletFarmPresetVersion = 1;
        apply.Invoke(presetCore, new object[] { false });
        foreach (var mod in ModDatabase.TabletMeta.Modifiers)
        {
            var p = presetCore.Settings.TabletProfiles[mod.TabletType];
            var old = ModDatabase.AllTabletMods.Single(m => m.TabletTypes.Contains(mod.TabletType) && mod.GameModIds.Contains(m.Id));
            Check(p.ModRanks[old.Id] == mod.Rank, "Workbook grade preserved: " + mod.Id);
        }
        foreach (string type in StashUtilitySettings.TabletTypes)
        {
            var p = presetCore.Settings.TabletProfiles[type];
            int count = p.ModRanks.Count(m => m.Value == "S");
            Check(count is >= 1 and <= 2, "Only 1 or 2 S modifiers for " + type);
            Check(p.ModRequiredMinRolls["TowerMonsterEffectiveness"] == 14, "Migration preserves minimum rolls " + type);
        }
        Check(!(bool)apply.Invoke(presetCore, new object[] { false }), "Workbook migration is applied once");
        var custom = new StashUtilityCore();
        custom.Settings.TabletFarmPresetVersion = 1;
        var profile = new TabletProfile { UseMetaFilters = true, MetaProfiles = new() { new() { Name = "Custom", TabletType = "Irradiated Tablet" } } };
        Set(profile, "TowerMonsterEffectiveness", "F");
        profile.ModRequiredMinRolls["TowerMonsterEffectiveness"] = 13;
        custom.Settings.TabletProfiles["Irradiated Tablet"] = profile;
        string before = JsonConvert.SerializeObject(profile);
        apply.Invoke(custom, new object[] { false });
        Check(JsonConvert.SerializeObject(profile) == before, "Manual F, selections, rolls and AND / OR profile stay unchanged");
        apply.Invoke(custom, new object[] { true });
        Check(profile.ModRanks["TowerMonsterEffectiveness"] == "A" && profile.UseMetaFilters && profile.MetaProfiles.Count == 1 && profile.ModRequiredMinRolls["TowerMonsterEffectiveness"] == 13, "Explicit workbook reset keeps AND / OR and roll settings");
        foreach (string rank in new[] { "E", "F" })
        {
            Set(profile, "TowerMonsterEffectiveness", rank);
            var migrate = typeof(StashUtilityCore).GetMethod("MigrateTabletModRanks", flags);
            migrate.Invoke(null, new object[] { profile });
            Check(profile.ModRanks["TowerMonsterEffectiveness"] == rank, "Saved " + rank + " never folded into D");
        }
    }

    private static void CheckTooltipGeometry(string root)
    {
        NativeLibrary.SetDllImportResolver(typeof(ImGuiNET.ImGui).Assembly, (name, assembly, path) =>
            name == "cimgui" ? NativeLibrary.Load(Path.Combine(root, "cimgui.dll")) : IntPtr.Zero);
        var context = ImGuiNET.ImGui.CreateContext();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        string previousDirectory = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(Path.GetDirectoryName(typeof(TabletRankingCheck).Assembly.Location));
        var process = GameHelper.Core.Process;
        var handle = process.GetType().GetProperty("Handle", flags);
        var previousReader = handle.GetValue(process);
        var readerType = typeof(GameHelper.Core).Assembly.GetType("GameHelper.Utils.SafeMemoryHandle");
        var reader = Activator.CreateInstance(readerType, flags, null, new object[] { System.Diagnostics.Process.GetCurrentProcess().Id }, null);
        var parent = Marshal.AllocHGlobal(Marshal.SizeOf<GameOffsets.Objects.UiElement.UiElementBaseOffset>());
        var child = Marshal.AllocHGlobal(Marshal.SizeOf<GameOffsets.Objects.UiElement.UiElementBaseOffset>());
        var children = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            handle.SetValue(process, reader);
            ImGuiNET.ImGui.GetIO().DisplaySize = new Vector2(640, 360);
            ImGuiNET.ImGui.GetIO().DeltaTime = 1f / 60f;
            unsafe { ImGuiNET.ImGui.GetIO().NativePtr->IniFilename = null; }
            ImGuiNET.ImGui.GetIO().Fonts.AddFontDefault();
            ImGuiNET.ImGui.GetIO().Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytes);
            var highlightPlugin = new StashUtilityCore { DllDirectory = root };
            highlightPlugin.Settings.EnableTabletManager = true;
            highlightPlugin.Settings.HideTabletHighlightsOnItemHover = false;
            var itemType = typeof(GameHelper.RemoteObjects.States.InGameStateObjects.Item);
            var entityType = itemType.BaseType;
            var highlight = typeof(StashUtilityCore).GetMethod("EvaluateAndHighlightItem", flags);
            void CheckItemOverlay(string path, string name, bool expected)
            {
                var item = Activator.CreateInstance(itemType, flags, null, new object[] { IntPtr.Zero }, null);
                entityType.GetProperty("Path").SetValue(item, path);
                var baseComponent = new Base(IntPtr.Zero);
                typeof(Base).GetProperty("BaseItemName").SetValue(baseComponent, name);
                var components = (System.Collections.Concurrent.ConcurrentDictionary<string, GameHelper.RemoteObjects.Components.ComponentBase>)entityType.GetField("componentCache", flags).GetValue(item);
                components["Base"] = baseComponent;
                ImGuiNET.ImGui.NewFrame();
                int before = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer.Size;
                highlight.Invoke(highlightPlugin, new object[] { item, new Vector2(200, 40), new Vector2(52) });
                Check((ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer.Size > before) == expected, "Tablet overlay only on real tablets: " + path);
                ImGuiNET.ImGui.EndFrame();
            }
            CheckItemOverlay("Metadata/Items/Currency/CurrencyIncursionCorruptTablet", "Infuseur antique", false);
            CheckItemOverlay("Metadata/Items/Currency/CurrencyIncursionCorruptTablet", "Ancient Infuser", false);
            CheckItemOverlay("Metadata/Items/Currency/Tablet", "Tablet", false);
            CheckItemOverlay("Metadata/Items/TowerAugment/GenericAugment", "Tablette irradiée", true);
            var uiRoot = Path.Combine(Directory.GetCurrentDirectory(), "tablet-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(uiRoot);
            var uiPlugin = new StashUtilityCore { DllDirectory = uiRoot };
            var uiProfile = new TabletProfile();
            uiPlugin.Settings.TabletProfiles["Irradiated Tablet"] = uiProfile;
            var rankCheckbox = typeof(StashUtilityCore).GetMethod("DrawTabletRankCheckbox", flags);
            Vector2 RankFrame(int rank, Vector2 mouse, bool down)
            {
                var io = ImGuiNET.ImGui.GetIO(); io.AddMousePosEvent(mouse.X, mouse.Y); io.AddMouseButtonEvent(0, down);
                ImGuiNET.ImGui.NewFrame();
                ImGuiNET.ImGui.SetNextWindowPos(new Vector2(40, 40)); ImGuiNET.ImGui.SetNextWindowSize(new Vector2(320, 150));
                ImGuiNET.ImGui.Begin("rank fixture");
                rankCheckbox.Invoke(uiPlugin, new object[] { "TowerMonsterEffectiveness", rank, uiProfile.ModRanks, uiProfile.GoodModPatterns, uiProfile.BadModPatterns, uiProfile.GodModPatterns, true });
                var center = (ImGuiNET.ImGui.GetItemRectMin() + ImGuiNET.ImGui.GetItemRectMax()) / 2;
                ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render(); return center;
            }
            foreach (int rank in new[] { 5, 6 })
            {
                var center = RankFrame(rank, new Vector2(-100), false);
                RankFrame(rank, center, false); RankFrame(rank, center, true); RankFrame(rank, center, false);
                Check(uiProfile.ModRanks["TowerMonsterEffectiveness"] == (rank == 5 ? "E" : "F"), "Actual E/F checkbox updates stored rank");
            }
            var persisted = JsonConvert.DeserializeObject<StashUtilitySettings>(File.ReadAllText(Path.Combine(uiRoot, "config", "settings.txt")));
            Check(persisted.TabletProfiles["Irradiated Tablet"].ModRanks["TowerMonsterEffectiveness"] == "F", "UI F selection persists to disk");
            var badgeMods = new Mods(IntPtr.Zero);
            foreach (string label in new[] { "E1", "E2", "E3", "F1" })
                badgeMods.ExplicitMods.Add((ModDatabase.TabletMeta.FindSubRank("Irradiated Tablet", label).GameModIds[0], (10f, 0f)));
            uiProfile.ModRanks.Clear();
            var badgeProfile = new TabletFilterProfile { TabletType = "Irradiated Tablet", Rule = new() { Kind = TabletRuleKind.RankExact, Rank = "E" } };
            uiProfile.UseMetaFilters = true; uiProfile.ActiveMetaProfileId = badgeProfile.Id; uiProfile.MetaProfiles.Add(badgeProfile);
            uiPlugin.Settings.ShowTabletModBorder = false; uiPlugin.Settings.ShowRarityBorder = false; uiPlugin.Settings.HideTabletHighlightsOnItemHover = false;
            ImGuiNET.ImGui.GetIO().AddMousePosEvent(-100, -100);
            ImGuiNET.ImGui.NewFrame();
            typeof(StashUtilityCore).GetMethod("DrawMetaTabletHighlight", flags).Invoke(uiPlugin, new object[] { "Fixture", "Irradiated Tablet", new Vector2(200,40), new Vector2(52), badgeMods });
            var vertices = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
            var actualBadge = Enumerable.Range(0, vertices.Size).Select(i => (vertices[i].pos, vertices[i].uv, vertices[i].col)).ToArray();
            ImGuiNET.ImGui.EndFrame(); ImGuiNET.ImGui.NewFrame();
            typeof(StashUtilityCore).GetMethod("DrawTabletRankBadge", flags | BindingFlags.Static).Invoke(null, new object[] { new Vector2(200,40), new Vector2(52), "E2 · OK", 1f, Math.Max(2f, uiPlugin.Settings.BorderMargin) });
            vertices = ImGuiNET.ImGui.GetBackgroundDrawList().VtxBuffer;
            var expectedBadge = Enumerable.Range(0, vertices.Size).Select(i => (vertices[i].pos, vertices[i].uv, vertices[i].col)).ToArray();
            Check(actualBadge.Length > 0 && actualBadge.SequenceEqual(expectedBadge), "AND / OR overlay actually renders E2 alongside OK");
            ImGuiNET.ImGui.EndFrame();
            var parentData = new GameOffsets.Objects.UiElement.UiElementBaseOffset
            {
                Self = parent, Flags = 1u << 11, LocalScaleMultiplier = 1,
                RelativePosition = new GameOffsets.Natives.StdTuple2D<float>(20, 20),
                UnscaledSize = new GameOffsets.Natives.StdTuple2D<float>(40, 40),
                ChildrensPtr = new GameOffsets.Natives.StdVector { First = children, Last = children + IntPtr.Size, End = children + IntPtr.Size }
            };
            var childData = new GameOffsets.Objects.UiElement.UiElementBaseOffset
            {
                Self = child, ParentPtr = parent, Flags = 1u << 11, LocalScaleMultiplier = 1,
                RelativePosition = new GameOffsets.Natives.StdTuple2D<float>(60, 0),
                UnscaledSize = new GameOffsets.Natives.StdTuple2D<float>(30, 30)
            };
            Marshal.StructureToPtr(parentData, parent, false);
            Marshal.StructureToPtr(childData, child, false);
            Marshal.WriteIntPtr(children, child);
            var plugin = new StashUtilityCore();
            typeof(StashUtilityCore).GetField("handleObj", flags).SetValue(plugin, reader);
            var collect = typeof(StashUtilityCore).GetMethod("CollectTooltipOcclusions", flags);
            var overlaps = typeof(StashUtilityCore).GetMethod("IsTabletOccluded", flags);
            bool Covered(Vector2 position) => (bool)overlaps.Invoke(plugin, new object[] { position, new Vector2(5) });
            collect.Invoke(plugin, new object[] { parent, 0, 128 });
            Check(Covered(new Vector2(25, 25)), "Real tooltip parent rectangle covered");
            Check(Covered(new Vector2(85, 25)), "Tooltip text outside parent bounds also covered");
            Check(!Covered(new Vector2(150, 150)), "Unrelated tablets remain visible");
            ((IList)typeof(StashUtilityCore).GetField("tabletOcclusions", flags).GetValue(plugin)).Clear();
            childData.Flags = 0;
            Marshal.StructureToPtr(childData, child, false);
            collect.Invoke(plugin, new object[] { parent, 0, 128 });
            Check(!Covered(new Vector2(85, 25)), "Hidden text child does not mask tablets");

            var queue = (IList)typeof(StashUtilityCore).GetField("pendingItemHighlights", flags).GetValue(plugin);
            var flush = typeof(StashUtilityCore).GetMethod("FlushItemHighlights", flags);
            var suppressed = typeof(StashUtilityCore).GetMethod("TabletHighlightsSuppressed", flags);
            var toggle = typeof(StashUtilityCore).GetMethod("ToggleTabletHighlights", flags);
            bool Hidden() => (bool)suppressed.Invoke(plugin, null);
            typeof(StashUtilityCore).GetField("tabletTooltipVisible", flags).SetValue(plugin, false);
            void Hover(Vector2 mouse)
            {
                ImGuiNET.ImGui.GetIO().MousePos = mouse;
                ImGuiNET.ImGui.GetIO().WantCaptureMouse = false;
                queue.Add(((GameHelper.RemoteObjects.States.InGameStateObjects.Item)null, new Vector2(200, 40), new Vector2(30)));
                flush.Invoke(plugin, null);
            }
            Check(plugin.Settings.HideTabletHighlightsOnItemHover, "Automatic hiding enabled by default");
            Check(JsonConvert.DeserializeObject<StashUtilitySettings>("{}").HideTabletHighlightsOnItemHover, "Existing settings receive automatic hiding default");
            Hover(new Vector2(205, 45)); Check(Hidden(), "Hover hides all tablet indications");
            Hover(new Vector2(100, 100)); Check(!Hidden(), "Leaving item restores indications immediately");
            toggle.Invoke(plugin, null); Hover(new Vector2(100, 100)); Check(Hidden(), "Manual hiding survives leaving item");
            plugin.Settings.HideTabletHighlightsOnItemHover = false;
            Hover(new Vector2(205, 45)); Check(Hidden(), "Manual hiding takes priority over disabling automatic hiding");
            toggle.Invoke(plugin, null); Hover(new Vector2(205, 45)); Check(!Hidden(), "Show restores indications with automatic hiding disabled");
            Check(!JsonConvert.DeserializeObject<StashUtilitySettings>(JsonConvert.SerializeObject(plugin.Settings)).HideTabletHighlightsOnItemHover, "Automatic preference persists through settings reload");
            plugin.Settings.HideTabletHighlightsOnItemHover = true;
            Hover(new Vector2(230, 45)); Check(!Hidden(), "Pointer on right item edge is outside hover area");
            typeof(StashUtilityCore).GetField("tabletTooltipVisible", flags).SetValue(plugin, true);
            Check(Hidden(), "Detected game tooltip hides all tablet indications");
            plugin.Settings.HideTabletHighlightsOnItemHover = false;
            Check(!Hidden(), "Disabling automatic mode overrides tooltip hiding");
            Check(queue.Count == 0, "Hover checks do not retain stale queued items");
        }
        finally
        {
            handle.SetValue(process, previousReader);
            ((IDisposable)reader).Dispose();
            Marshal.FreeHGlobal(parent); Marshal.FreeHGlobal(child); Marshal.FreeHGlobal(children);
            ImGuiNET.ImGui.DestroyContext(context);
            Directory.SetCurrentDirectory(previousDirectory);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        // Closed panels must never enter ImGui or dereference transient game memory.
        // This fixture deliberately has no ImGui context and no attached game process.
        var core = new StashUtilityCore();
        var tabletIdentity = typeof(StashUtilityCore).GetMethod("IsTabletItem", BindingFlags.Static | BindingFlags.NonPublic);
        bool IsTablet(string path) => (bool)tabletIdentity.Invoke(null, new object[] { path });
        foreach (string id in new[] { "BreachAugment", "DeliriumAugment", "RitualAugment", "GenericAugment", "MapBossAugment", "AbyssAugment", "ExpeditionAugment", "IncursionAugment" })
            Check(IsTablet("Metadata/Items/TowerAugment/" + id), "True tablet remains recognized: " + id);
        foreach (string path in new[] { null, "", "Metadata/Items/Currency/CurrencyIncursionCorruptTablet", "Metadata/Items/Currency/Tablet", "Metadata/Items/Other/TowerAugment/Fake", "Metadata/Items/TowerAugmentFake/Tablet" })
            Check(!IsTablet(path), "Non-tablet identifier rejected: " + path);
        var occlusions = (IList)typeof(StashUtilityCore).GetField("tabletOcclusions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(core);
        var updateOcclusions = typeof(StashUtilityCore).GetMethod("UpdateTabletOcclusions", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 1000; i++)
        {
            occlusions.Add((Vector2.Zero, Vector2.One));
            updateOcclusions.Invoke(core, new object[] { new IntPtr(i == 0 ? 0 : -1) });
        }
        Check(occlusions.Count == 0, "Closed panels clear stale covers without game memory or ImGui calls");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var callbackType = typeof(StashUtilityCore).GetNestedType("TabletWindowCallback", BindingFlags.NonPublic);
            var enumerate = typeof(StashUtilityCore).GetMethod("TabletEnumWindows", BindingFlags.Static | BindingFlags.NonPublic);
            var callback = Delegate.CreateDelegate(callbackType, typeof(TabletRankingCheck).GetMethod("CountWindow", BindingFlags.Static | BindingFlags.NonPublic));
            nativeWindows = 0;
            for (int i = 0; i < 100; i++) enumerate.Invoke(null, new object[] { callback, IntPtr.Zero });
            Check(nativeWindows >= 100, "Repeated real Windows callbacks marshal correctly");
            callback = Delegate.CreateDelegate(callbackType, typeof(TabletRankingCheck).GetMethod("StopWindows", BindingFlags.Static | BindingFlags.NonPublic));
            nativeWindows = 0;
            enumerate.Invoke(null, new object[] { callback, IntPtr.Zero });
            Check(nativeWindows == 1, "Native callback can safely stop enumeration");
        }
        string[] grades = { "S", "A", "B", "C", "D", "E", "F" };
        var inputs = (from a in grades from b in grades from c in grades from d in grades select new[] { a, b, c, d }).ToArray();
        string Canonical(string[] values) => string.Concat(values.Select(v => Array.IndexOf(grades, v)).OrderBy(v => v).Select(v => (char)('0' + v)));
        var compositions = inputs.Select(Canonical).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Check(compositions.Length == 210, "Exactly 210 order-independent compositions");
        var numbers = new int[7];
        var expected = new Dictionary<string, string>();
        foreach (var composition in compositions)
        {
            int rank = composition[0] - '0';
            expected.Add(composition, grades[rank] + ++numbers[rank]);
        }
        Check(numbers.SequenceEqual(new[] { 84, 56, 35, 20, 10, 4, 1 }), "84 / 56 / 35 / 20 / 10 / 4 / 1 categories");
        var results = new Dictionary<string, TabletClassification>();
        var colors = new[] { new Vector4(1f, .8f, .3f, 1f), new Vector4(.78f, .6f, 1f, 1f),
            new Vector4(.4f, .7f, 1f, 1f), new Vector4(.35f, .85f, .65f, 1f), Vector4.One,
            new Vector4(1f, .65f, .3f, 1f), new Vector4(.95f, .4f, .4f, 1f) };
        foreach (var input in inputs)
        {
            string canonical = Canonical(input);
            var result = StashUtilityCore.ClassifyTabletRanks(input);
            Check(result.IsRanked && result.RankLabel == expected[canonical], "Composition and permutation " + string.Join("+", input));
            Check(result.Rank == grades[canonical[0] - '0'] && result.RankColor == colors[canonical[0] - '0'], "Best rank and reused color " + result.RankLabel);
            Check(result.RankLabel == result.Rank + result.RankIndex, "Consistent index " + result.RankLabel);
            if (results.TryGetValue(canonical, out var previous)) Check(result == previous, "All fields independent of mod order");
            results[canonical] = result;
        }
        Check(results.Values.Select(v => v.RankLabel).Distinct().Count() == 210, "Every code unique");
        Check(results.Values.OrderBy(v => v.SortScore).Select(v => v.RankLabel).SequenceEqual(compositions.Select(v => expected[v])), "Global numeric quality order");
        Check(results.Values.Select(v => v.SortScore).OrderBy(v => v).SequenceEqual(Enumerable.Range(0, 210)), "Unique contiguous sort scores");
        foreach (string grade in grades) Check(StashUtilityCore.ClassifyTabletRanks(Enumerable.Repeat(grade, 4).ToArray()).RankLabel == grade + "1", "Best combination " + grade + "1");
        string[] bTable = { "BBBB", "BBBC", "BBBD", "BBCC", "BBCD", "BBDD", "BCCC", "BCCD", "BCDD", "BDDD" };
        for (int i = 0; i < bTable.Length; i++) Check(StashUtilityCore.ClassifyTabletRanks(bTable[i].Select(c => c.ToString()).ToArray()).RankLabel == expected[Canonical(bTable[i].Select(c => c.ToString()).ToArray())], "Extended B table " + bTable[i]);
        string[] cTable = { "CCCC", "CCCD", "CCDD", "CDDD" };
        for (int i = 0; i < cTable.Length; i++) Check(StashUtilityCore.ClassifyTabletRanks(cTable[i].Select(c => c.ToString()).ToArray()).RankLabel == expected[Canonical(cTable[i].Select(c => c.ToString()).ToArray())], "Extended C table " + cTable[i]);
        foreach (var invalid in new string[][] { null, Array.Empty<string>(), new[] { "S" }, new[] { "S", "A", "B" }, new[] { "S", "A", "B", "C", "D" },
            new[] { "S", "A", "B", (string)null }, new[] { "S", "A", "B", "" }, new[] { "S", "A", "B", "Z" },
            new[] { "S", "A", "B", "S+++" }, new[] { "S", "A", "B", "d" }, new[] { "S", "A", "B", " D " } })
        {
            var result = StashUtilityCore.ClassifyTabletRanks(invalid);
            Check(!result.IsRanked && result.RankIndex == 0 && result.RankLabel == "" && result.SortScore == int.MaxValue, "Invalid ranks never become D");
        }
        Check(results["0666"].SortScore < results["1111"].SortScore, "S84 outranks A1");
        Check(results.Values.Single(v => v.RankLabel == "S2").SortScore < results.Values.Single(v => v.RankLabel == "S10").SortScore, "S2 before S10");
        Check(results.Values.Single(v => v.RankLabel == "B2").SortScore < results.Values.Single(v => v.RankLabel == "B10").SortScore, "B2 before B10");

        var analyze = typeof(StashUtilityCore).GetMethod("ClassifyTabletMods", BindingFlags.Static | BindingFlags.NonPublic);
        TabletClassification Analyze(Mods mods, TabletProfile profile, string type) =>
            (TabletClassification)analyze.Invoke(null, new object[] { mods, profile, type });
        foreach (string tabletType in StashUtilitySettings.TabletTypes)
        {
            var definitions = ModDatabase.AllTabletMods.Where(m => m.TabletTypes.Contains(tabletType)).GroupBy(m => m.Id).Select(g => g.First()).Take(4).ToArray();
            Check(definitions.Length == 4, "Catalog coverage " + tabletType);
            var profile = new TabletProfile();
            var mods = new Mods(IntPtr.Zero);
            for (int i = 0; i < 4; i++)
            {
                profile.ModRanks[definitions[i].Id] = grades[i + 1];
                profile.GoodModPatterns.Add(definitions[i].Id);
                profile.ModRequiredMinRolls[definitions[i].Id] = definitions[i].MaxRoll;
                mods.ExplicitMods.Add((definitions[i].Id, (definitions[i].MinRoll, definitions[i].MinRoll)));
            }
            // A+B+C+D = A15, by the independent composition oracle.
            string expectedCode = expected[Canonical(grades.Skip(1).Take(4).ToArray())];
            var result = Analyze(mods, profile, tabletType);
            Check(result.RankLabel == expectedCode, "Actual mod IDs and category " + tabletType);
            string saved = JsonConvert.SerializeObject(profile);
            mods.ExplicitMods.Reverse();
            Check(Analyze(mods, profile, tabletType) == result, "Raw modifier order " + tabletType);
            mods.ImplicitMods.Add(mods.ExplicitMods[0]);
            mods.EnchantMods.Add(mods.ExplicitMods[0]);
            Check(Analyze(mods, profile, tabletType) == result, "Only four explicit affixes determine grade " + tabletType);
            Check(JsonConvert.SerializeObject(profile) == saved, "Ranks and minimum filters untouched " + tabletType);
            var reloaded = JsonConvert.DeserializeObject<TabletProfile>(saved);
            Check(Analyze(mods, reloaded, tabletType) == result, "Save and reload " + tabletType);
            profile.ModRanks[definitions[0].Id] = "unknown";
            Check(!Analyze(mods, profile, tabletType).IsRanked, "Invalid stored rank " + tabletType);
            profile.ModRanks.Remove(definitions[0].Id);
            Check(Analyze(mods, profile, tabletType).IsRanked == (ModDatabase.TabletMeta.Recognize(tabletType, new[] { definitions[0].Id }).Mods[0].Modifier != null), "Missing stored rank uses only a known workbook modifier " + tabletType);
            profile.ModRanks[definitions[0].Id] = "S";
            Check(Analyze(mods, profile, tabletType).Rank == "S", "Rank edit immediately recomputed " + tabletType);
            mods.ExplicitMods.RemoveAt(0);
            Check(!Analyze(mods, profile, tabletType).IsRanked, "Missing explicit affix " + tabletType);
        }

        // Exercise the actual existing grid planner; no input is sent to the game.
        var type = typeof(StashUtilityCore).GetNestedType("SortItem", BindingFlags.NonPublic);
        var items = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
        var reverse = results.Values.OrderByDescending(v => v.SortScore).ToArray();
        for (int i = 0; i < reverse.Length; i++)
        {
            var item = Activator.CreateInstance(type, true);
            void Set(string field, object value) => type.GetField(field).SetValue(item, value);
            Set("Address", new IntPtr(i + 1)); Set("X", i % 12); Set("Y", i / 12); Set("Width", 1); Set("Height", 1);
            Set("Kind", i % 2 == 0 ? "TowerAugment/Breach" : "TowerAugment/Delirium");
            Set("IsTablet", true); Set("TabletSortScore", reverse[i].SortScore); items.Add(item);
        }
        var planner = typeof(StashUtilityCore).GetMethod("BuildSortTargets", BindingFlags.Static | BindingFlags.NonPublic);
        foreach (bool byType in new[] { true, false })
        {
            var targets = (Dictionary<IntPtr, (int x, int y)>)planner.Invoke(null, new object[] { items, 12, 18, new HashSet<int>(), byType });
            Check(targets != null && targets.Count == 210, "Planner keeps all tablets");
            for (int i = 0; i < reverse.Length; i++)
            {
                var target = targets[new IntPtr(i + 1)];
                Check(target.y * 12 + target.x == reverse[i].SortScore, "Real numeric grid order " + reverse[i].RankLabel);
            }
            var protectedTargets = (Dictionary<IntPtr, (int x, int y)>)planner.Invoke(null, new object[] { items, 12, 18, new HashSet<int> { 0 }, byType });
            Check(protectedTargets[new IntPtr(1)] == (0, 0), "Protected tablet stays in place");
            Check(protectedTargets[new IntPtr(210)] == (1, 0), "Best movable tablet follows protected cell");
        }
        var moved = type.GetMethod("At").Invoke(items[0], new object[] { 4, 5 });
        Check((bool)type.GetField("IsTablet").GetValue(moved) && (int)type.GetField("TabletSortScore").GetValue(moved) == reverse[0].SortScore, "Rank retained across planned moves");
    }
}
