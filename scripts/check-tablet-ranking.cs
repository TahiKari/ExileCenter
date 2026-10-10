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
            CheckTooltipGeometry(root);
            Console.WriteLine(checks + " tablet classification checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
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
        string[] grades = { "S", "A", "B", "C", "D" };
        var inputs = (from a in grades from b in grades from c in grades from d in grades select new[] { a, b, c, d }).ToArray();
        string Canonical(string[] values) => string.Concat(values.Select(v => Array.IndexOf(grades, v)).OrderBy(v => v).Select(v => (char)('0' + v)));
        var compositions = inputs.Select(Canonical).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray();
        Check(compositions.Length == 70, "Exactly 70 order-independent compositions");
        var numbers = new int[5];
        var expected = new Dictionary<string, string>();
        foreach (var composition in compositions)
        {
            int rank = composition[0] - '0';
            expected.Add(composition, grades[rank] + ++numbers[rank]);
        }
        Check(numbers.SequenceEqual(new[] { 35, 20, 10, 4, 1 }), "35 / 20 / 10 / 4 / 1 categories");
        var results = new Dictionary<string, TabletClassification>();
        var colors = new[] { new Vector4(1f, .8f, .3f, 1f), new Vector4(.78f, .6f, 1f, 1f),
            new Vector4(.4f, .7f, 1f, 1f), new Vector4(.35f, .85f, .65f, 1f), Vector4.One };
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
        Check(results.Values.Select(v => v.RankLabel).Distinct().Count() == 70, "Every code unique");
        Check(results.Values.OrderBy(v => v.SortScore).Select(v => v.RankLabel).SequenceEqual(compositions.Select(v => expected[v])), "Global numeric quality order");
        Check(results.Values.Select(v => v.SortScore).OrderBy(v => v).SequenceEqual(Enumerable.Range(0, 70)), "Unique contiguous sort scores");
        foreach (string grade in grades) Check(StashUtilityCore.ClassifyTabletRanks(Enumerable.Repeat(grade, 4).ToArray()).RankLabel == grade + "1", "Best combination " + grade + "1");
        string[] bTable = { "BBBB", "BBBC", "BBBD", "BBCC", "BBCD", "BBDD", "BCCC", "BCCD", "BCDD", "BDDD" };
        for (int i = 0; i < bTable.Length; i++) Check(StashUtilityCore.ClassifyTabletRanks(bTable[i].Select(c => c.ToString()).ToArray()).RankLabel == "B" + (i + 1), "Mandatory B table " + bTable[i]);
        string[] cTable = { "CCCC", "CCCD", "CCDD", "CDDD" };
        for (int i = 0; i < cTable.Length; i++) Check(StashUtilityCore.ClassifyTabletRanks(cTable[i].Select(c => c.ToString()).ToArray()).RankLabel == "C" + (i + 1), "Mandatory C table " + cTable[i]);
        foreach (var invalid in new string[][] { null, Array.Empty<string>(), new[] { "S" }, new[] { "S", "A", "B" }, new[] { "S", "A", "B", "C", "D" },
            new[] { "S", "A", "B", (string)null }, new[] { "S", "A", "B", "" }, new[] { "S", "A", "B", "E" },
            new[] { "S", "A", "B", "S+++" }, new[] { "S", "A", "B", "d" }, new[] { "S", "A", "B", " D " } })
        {
            var result = StashUtilityCore.ClassifyTabletRanks(invalid);
            Check(!result.IsRanked && result.RankIndex == 0 && result.RankLabel == "" && result.SortScore == int.MaxValue, "Invalid ranks never become D");
        }
        Check(results["0444"].SortScore < results["1111"].SortScore, "S35 outranks A1");
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
            string expectedCode = expected[Canonical(grades.Skip(1).ToArray())];
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
            Check(!Analyze(mods, profile, tabletType).IsRanked, "Missing stored rank not inferred " + tabletType);
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
            var targets = (Dictionary<IntPtr, (int x, int y)>)planner.Invoke(null, new object[] { items, 12, 6, new HashSet<int>(), byType });
            Check(targets != null && targets.Count == 70, "Planner keeps all tablets");
            for (int i = 0; i < reverse.Length; i++)
            {
                var target = targets[new IntPtr(i + 1)];
                Check(target.y * 12 + target.x == reverse[i].SortScore, "Real numeric grid order " + reverse[i].RankLabel);
            }
            var protectedTargets = (Dictionary<IntPtr, (int x, int y)>)planner.Invoke(null, new object[] { items, 12, 6, new HashSet<int> { 0 }, byType });
            Check(protectedTargets[new IntPtr(1)] == (0, 0), "Protected tablet stays in place");
            Check(protectedTargets[new IntPtr(70)] == (1, 0), "Best movable tablet follows protected cell");
        }
        var moved = type.GetMethod("At").Invoke(items[0], new object[] { 4, 5 });
        Check((bool)type.GetField("IsTablet").GetValue(moved) && (int)type.GetField("TabletSortScore").GetValue(moved) == reverse[0].SortScore, "Rank retained across planned moves");
    }
}
