namespace StashUtility
{
    using ExileCenter;
    using ExileCenter.Plugin;
    using ExileCenter.RemoteEnums;
    using ExileCenter.RemoteObjects.Components;
    using ExileCenter.RemoteObjects.States.InGameStateObjects;
    using ExileCenter.RemoteObjects.UiElement;
    using ExileCenter.Utils;
    using GameOffsets.Objects.UiElement;
    using GameOffsets.Natives;
    using ImGuiNET;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Reflection;
    using System.Runtime.InteropServices;

    public sealed partial class StashUtilityCore : PCore<StashUtilitySettings>
    {
        private readonly Dictionary<string, IntPtr> tabletIconHandles = new();
        private readonly List<(Item Item, Vector2 Position, Vector2 Size)> pendingItemHighlights = new();
        private readonly List<(Vector2 Position, Vector2 Size)> tabletOcclusions = new();
        private bool tabletHighlightsHiddenManually;
        private bool tabletItemHovered;
        private bool tabletTooltipVisible;
        private readonly List<IntPtr> tooltipContainers = new();
        private IntPtr tooltipUiRoot;
        private double nextTooltipContainerScan;
        private object handleObj;
        private MethodInfo readStdWStringMethod;
        private object uiParentsObj;   // Parent cache for P1 / Single-Player slots
        private object uiParentsObjP2; // Separate parent cache for P2 slots (avoids cross-panel position corruption)
        private object currentUiParentsObj; // Points to either uiParentsObj or uiParentsObjP2 during processing
        private MethodInfo updateCacheMethod;

        private readonly Dictionary<Type, MethodInfo> readMemoryMethods = new();
        private readonly Dictionary<Type, MethodInfo> readStdVectorMethods = new();
        private readonly Dictionary<Type, MethodInfo> tryReadMemoryMethods = new();

        private struct CoopDebugInfo
        {
            public bool IsCoopMode;
            public IntPtr P1LeftPanel;
            public IntPtr P1StashTabsContainer;
            public IntPtr P1ActiveTab;
            public string P1Status;
            public List<string> P1TabsInfo;
            public IntPtr P2RightPanel;
            public IntPtr P2StashTabsContainer;
            public IntPtr P2ActiveTab;
            public string P2Status;
            public List<string> P2TabsInfo;
        }
        private CoopDebugInfo coopDebug = new() { P1TabsInfo = new(), P2TabsInfo = new() };

        private struct SinglePlayerDebugInfo
        {
            public IntPtr LeftPanel;
            public string StashTabsPath;
            public IntPtr StashTabsContainer;
            public IntPtr ActiveTab;
            public string Status;
        }
        private SinglePlayerDebugInfo spDebug = new();

        private readonly List<string> probeLog = new();
        private string waystoneSearchTerm = string.Empty;
        private string tabletSearchTerm = string.Empty;
        private string jewelSearchTerm = string.Empty;
        private bool waystoneConfiguredOnly, tabletConfiguredOnly, jewelConfiguredOnly;
        private int waystoneAffixFilter, tabletAffixFilter, jewelAffixFilter, tabletFilterType, jewelFilterType, jewelFilterGroup;
        private string transferExportStatus = string.Empty;
        private readonly List<string> transferDestinationChecks = new();
        private sealed class DetectedStashTab
        {
            public string Name;
            public List<string> Folders;
            public IntPtr Page;
            public readonly Dictionary<string, string> ItemKinds = new();
            public readonly HashSet<IntPtr> ItemAddresses = new();
            public readonly HashSet<IntPtr> UiAddresses = new();
            public readonly List<(string name, Rarity rarity)> Items = new();
            public string DisplayName => string.Join(" / ", Folders.Append(Name));
        }
        private readonly List<DetectedStashTab> detectedStashTabs = new();
        private readonly Queue<(DetectedStashTab tab, IntPtr address, int depth)> stashScanQueue = new();
        private IntPtr stashScanRoot;
        private int stashScanNodeCount;
        private string stashScanStatus = string.Empty;
        private List<string> stashBrowserFolders = new();
        private IntPtr stashBrowserTab;
        private int stashDestinationCategory;
        private bool affinityTransferRunning;
        private bool affinityTransferWaiting;
        private bool affinityStartPrepared;
        private IntPtr affinityGameWindow;
        private IntPtr affinityInventoryRoot;
        private IntPtr affinityStashRoot;
        private IntPtr affinitySlot;
        private IntPtr affinityItem;
        private bool affinityClicked;
        private bool affinityCtrlHeld;
        private bool affinityShiftHeld;
        private bool affinityMousePositioned;
        private NativePoint affinityCursor;
        private System.Drawing.Rectangle affinityWindow;
        private DateTime affinityNextAction;
        private DateTime affinityDeadline;
        private DateTime affinityFeedbackUntil;
        private readonly HashSet<IntPtr> affinityProcessed = new();
        private readonly Dictionary<IntPtr, int> affinityItemOffsets = new();
        private Vector2? inventoryToolbarAnchor;
        private IntPtr inventoryToolbarPanel;
        private System.Drawing.Rectangle inventoryToolbarWindow;
        private int affinityTransferred;
        private int affinityFailed;
        private int affinityProtectedSkipped;
        private int affinityActionDelay;
        private int affinityInventoryReadRetries;
        private int affinityUnrouted;
        private string affinityTabletCategory;
        private bool affinityTabletUsesSpecializedPage;
        private bool affinityTabletSectionSelected;
        private TabletPlacement affinityTabletPlacement;
        private DateTime affinityTabletReadDeadline;
        private DateTime affinityTabletNavigationReadDeadline;
        private sealed class TabletPlacement
        {
            public IntPtr Page, Grid, Inventory;
            public Vector2 Origin, Cell;
            public SortItem Source, Target;
            public List<SortItem> Before;
            public int Phase;
        }
        private IntPtr affinityOpenedPage;
        private string affinityRouteIssue = string.Empty;
        private string affinityPendingKind;
        private StashDestination affinityKalguuranSortDestination;
        private bool affinityFinishingGemSort;
        private int affinityGemSortAttempts;
        private bool sortFromGemTransfer;
        private StashDestination affinityEquipmentDestination;
        private StashDestination affinityUniqueFallbackDestination;
        private int affinityEquipmentTab;
        private bool affinityEquipmentRouteResolved;
        private DateTime affinityNavigationDeadline;
        private IntPtr affinityNavigationElement;
        private int affinityNavigationWheel;
        private const int InventoryColumns = 12;
        private const int InventoryRows = 5;
        private readonly HashSet<int> affinityProtectedCells = new();
        private string affinityTransferStatus = string.Empty;
        private string affinityCompletionSummary = string.Empty;
        private sealed class SortItem
        {
            public IntPtr Address;
            public int X, Y, Width, Height, Tier;
            public bool IsTablet;
            public int TabletSortScore = int.MaxValue;
            public string Kind, Path, Identity;
            public Rarity Rarity;
            public SortItem At(int x, int y) => new() { Address = Address, X = x, Y = y, Width = Width,
                Height = Height, Tier = Tier, Kind = Kind, Path = Path, Identity = Identity, Rarity = Rarity, IsTablet = IsTablet, TabletSortScore = TabletSortScore };
        }
        private sealed class SortMove
        {
            public SortItem Item, Swap;
            public int X, Y;
        }
        private bool sortRunning, sortStash, sortPrepared, sortMousePositioned;
        private IntPtr sortInventory, sortGrid;
        private int sortColumns, sortRows, sortPhase, sortMoves;
        private Vector2 sortOrigin, sortCellSize;
        private System.Drawing.Rectangle sortWindow;
        private NativePoint sortCursor;
        private DateTime sortNextAction, sortDeadline;
        private List<SortItem> sortItems;
        private Dictionary<IntPtr, (int x, int y)> sortTargets;
        private readonly Dictionary<IntPtr, IntPtr> sortAddresses = new();
        private readonly HashSet<int> sortProtectedCells = new();
        private readonly HashSet<string> sortLayouts = new();
        private SortMove sortMove;
        private string sortStatus = string.Empty;

        // Jewel UI State
        private readonly Dictionary<string, string> jewelCategorySearchTerms = new();
        private readonly Dictionary<string, int> jewelCategorySelectedIndices = new();

        // Debug UI Path Explorer State
        private readonly List<int> currentDebugPath = new();
        private string explorerRootAddressStr = string.Empty;
        private bool debugHoveredCurrentElement = false;
        private bool debugHoveredAllChildren = false;
        private int debugHoveredChildIndex = -1;
        private IntPtr debugCurrentAddress = IntPtr.Zero;
        private readonly List<IntPtr> debugChildrenAddresses = new();

        // Debug Hovered Waystone Inspector State
        private Item lastHoveredWaystone = null;
        private bool freezeHoveredWaystone = false;

        private string SettingPathname => Path.Combine(DllDirectory, "config", "settings.txt");

        public override void OnDisable()
        {
            UnloadTabletIcons();
            StopGridSort();
            stashScanQueue.Clear();
            affinityTransferRunning = false;
            ReleaseAffinityControl();
        }

        public override void OnEnable(bool isGameOpened)
        {
            LoadTabletIcons();
            inventoryToolbarAnchor = null;
            affinityItemOffsets.Clear();
            hasClearedDumpFile = false;
            if (File.Exists(SettingPathname))
            {
                try
                {
                    var content = File.ReadAllText(SettingPathname);
                    Settings = JsonConvert.DeserializeObject<StashUtilitySettings>(content,
                        new JsonSerializerSettings { MaxDepth = Models.TabletFilterEngine.MaximumDepth * 3 + 32 }) ?? new StashUtilitySettings();
                }
                catch
                {
                    // Fallback to defaults
                }
            }

            if (Settings.ScanEndOffset < 0x500)
            {
                Settings.ScanEndOffset = 0x600;
            }

            if (Settings.TabletProfiles == null)
            {
                Settings.TabletProfiles = new Dictionary<string, TabletProfile>();
            }
            foreach (var type in StashUtilitySettings.TabletTypes)
            {
                if (!Settings.TabletProfiles.ContainsKey(type) || Settings.TabletProfiles[type] == null)
                {
                    Settings.TabletProfiles[type] = new TabletProfile();
                }
            }

            // Migrate old global tablet settings to all profiles
            if (Settings.TabletGoodModPatterns != null && Settings.TabletGoodModPatterns.Count > 0)
            {
                foreach (var type in StashUtilitySettings.TabletTypes)
                {
                    var profile = Settings.TabletProfiles[type];
                    foreach (var pat in Settings.TabletGoodModPatterns)
                    {
                        if (!profile.GoodModPatterns.Contains(pat))
                            profile.GoodModPatterns.Add(pat);
                    }
                }
                Settings.TabletGoodModPatterns.Clear();
                SaveSettings();
            }

            if (Settings.TabletBadModPatterns != null && Settings.TabletBadModPatterns.Count > 0)
            {
                foreach (var type in StashUtilitySettings.TabletTypes)
                {
                    var profile = Settings.TabletProfiles[type];
                    foreach (var pat in Settings.TabletBadModPatterns)
                    {
                        if (!profile.BadModPatterns.Contains(pat))
                            profile.BadModPatterns.Add(pat);
                    }
                }
                Settings.TabletBadModPatterns.Clear();
                SaveSettings();
            }

            if (Settings.TabletGodModPatterns != null && Settings.TabletGodModPatterns.Count > 0)
            {
                foreach (var type in StashUtilitySettings.TabletTypes)
                {
                    var profile = Settings.TabletProfiles[type];
                    foreach (var pat in Settings.TabletGodModPatterns)
                    {
                        if (!profile.GodModPatterns.Contains(pat))
                            profile.GodModPatterns.Add(pat);
                    }
                }
                Settings.TabletGodModPatterns.Clear();
                SaveSettings();
            }

            if (Settings.TabletModRequiredMinRolls != null && Settings.TabletModRequiredMinRolls.Count > 0)
            {
                foreach (var type in StashUtilitySettings.TabletTypes)
                {
                    var profile = Settings.TabletProfiles[type];
                    foreach (var kvp in Settings.TabletModRequiredMinRolls)
                    {
                        profile.ModRequiredMinRolls[kvp.Key] = kvp.Value;
                    }
                }
                Settings.TabletModRequiredMinRolls.Clear();
                SaveSettings();
            }

            bool tabletRanksMigrated = false;
            foreach (var profile in Settings.TabletProfiles.Values)
                if (profile != null) tabletRanksMigrated |= MigrateTabletModRanks(profile);
            tabletRanksMigrated |= ApplyTabletFarmPreset();
            if (tabletRanksMigrated) SaveSettings();

            // Migration code: Convert old text patterns to new mod database IDs
            if (Settings.BadModPatterns.Count > 0)
            {
                for (int i = 0; i < Settings.BadModPatterns.Count; i++)
                {
                    var pattern = Settings.BadModPatterns[i];
                    var normalizedPat = NormalizeForMatching(pattern);
                    var match = Data.ModDatabase.AllWaystoneMods.FirstOrDefault(dbMod => 
                        NormalizeForMatching(dbMod.Name) == normalizedPat ||
                        dbMod.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        Settings.BadModPatterns[i] = match.Id;
                    }
                }
            }
            if (Settings.GoodModPatterns.Count > 0)
            {
                for (int i = 0; i < Settings.GoodModPatterns.Count; i++)
                {
                    var pattern = Settings.GoodModPatterns[i];
                    var normalizedPat = NormalizeForMatching(pattern);
                    var match = Data.ModDatabase.AllWaystoneMods.FirstOrDefault(dbMod => 
                        NormalizeForMatching(dbMod.Name) == normalizedPat ||
                        dbMod.Id.Equals(pattern, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        Settings.GoodModPatterns[i] = match.Id;
                    }
                }
            }

            InitReflection();
        }

        public override void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingPathname);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var settingsData = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(SettingPathname, settingsData);
            }
            catch
            {
                // Ignored
            }
        }

        private void DrawMinMaxFilterTableRow(string label, string id, int maxSliderVal, ref bool filterMin, ref int minVal, ref bool filterMax, ref int maxVal)
        {
            ImGui.PushID(id);
            ImGui.TableNextRow();
            
            // Column 1: Label
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(PluginText.T($"stashutility.criteria.{id}", label));
            
            // Column 2: Min
            ImGui.TableNextColumn();
            ImGui.Checkbox(PluginText.T("stashutility.min", "Min"), ref filterMin);
            if (filterMin)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(Math.Max(45, ImGui.GetContentRegionAvail().X));
                if (ImGui.SliderInt("##minval", ref minVal, 0, maxSliderVal)) SaveSettings();
            }
            
            // Column 3: Max
            ImGui.TableNextColumn();
            ImGui.Checkbox(PluginText.T("stashutility.max", "Max"), ref filterMax);
            if (filterMax)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(Math.Max(45, ImGui.GetContentRegionAvail().X));
                if (ImGui.SliderInt("##maxval", ref maxVal, 0, maxSliderVal)) SaveSettings();
            }
            
            ImGui.PopID();
        }

        public override void DrawSettings()
        {
            if (affinityTransferRunning && ImGui.Button(PluginText.T("stashutility.transfer.affinity_stop", "Stop Transfer"))) StopAffinityTransfer();
            if (sortRunning && ImGui.Button(PluginText.T("stashutility.sort.stop", "Stop Sorting"))) StopGridSort();
            if (!string.IsNullOrEmpty(affinityTransferStatus)) ImGui.TextWrapped(affinityTransferStatus);
            var pages = new[] { "stash", "inventory", "filters", "appearance", "diagnostics" };
            var labels = new[] { "Stash", "Inventory", "Filters", "Appearance", "Diagnostics" };
            if (!ImGui.BeginTabBar("##stashSettingsPages", ImGuiTabBarFlags.FittingPolicyScroll)) return;
            for (int i = 0; i < pages.Length; i++)
            {
                using var theme = ExileCenter.Ui.ImGuiTheme.UsePluginTheme("StashUtility/" + pages[i]);
                if (!ImGui.BeginTabItem(PluginText.T("stashutility.ui." + pages[i], labels[i]) + "###stashSettings_" + pages[i])) continue;
                if (ImGui.BeginChild("##stashSettingsBody", Vector2.Zero))
                {
                    ImGui.PushItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X * 0.4f, 140f, 280f));
                    switch (i)
                    {
                        case 0: DrawTransferDestinationSettings(); break;
                        case 1: DrawInventoryTransferOptions(); break;
                        case 2: DrawFilterSettings(); break;
                        case 3: DrawAppearanceSettings(); break;
                        case 4: DrawStashDiagnostics(); DrawDebugSettings(); break;
                    }
                    ImGui.PopItemWidth();
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        private void DrawFilterSettings()
        {
            var groups = new[] { "waystones", "tablets", "jewels" };
            var labels = new[] { "Waystones", "Tablets", "Jewels" };
            if (!ImGui.BeginTabBar("##stashFilterPages", ImGuiTabBarFlags.FittingPolicyScroll)) return;
            for (int i = 0; i < groups.Length; i++)
            {
                using var theme = ExileCenter.Ui.ImGuiTheme.UsePluginTheme("StashUtility/" + groups[i]);
                if (!ImGui.BeginTabItem(PluginText.T("stashutility.ui." + groups[i], labels[i]) + "###stashFilter_" + groups[i])) continue;
                switch (i)
                {
                    case 0: DrawWaystoneFilterSettings(); break;
                    case 1: DrawTabletFilterSettings(); break;
                    case 2: DrawJewelFilterSettings(); break;
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        private void DrawDebugSettings()
        {
            ImGui.Checkbox(PluginText.T("stashutility.enable_debug", "Enable Debug Settings"), ref Settings.EnableDebugProbe);
            ImGuiHelper.ToolTip(PluginText.T("stashutility.enable_debug_tooltip", "Enables advanced debugging options, interactive explorer, and hovered item inspector."));

            if (Settings.EnableDebugProbe)
            {
                var settingsGameUi = Core.States.InGameStateObject.GameUi;
                if (settingsGameUi != null && settingsGameUi.Address != IntPtr.Zero)
                {
                    ImGui.SeparatorText(PluginText.T("stashutility.debug.processing", "Active Stash Processing Debug Info"));
                    bool isCoopMode = Core.GHSettings.EnableControllerMode && settingsGameUi.LeftPanel.Address != IntPtr.Zero;
                    if (isCoopMode)
                    {
                        ImGui.Text(PluginText.T("stashutility.debug.coop_on", "Coop Mode: TRUE"));
                        ImGui.Text(PluginText.F("stashutility.debug.panel", "{0}LeftPanel: 0x{1:X}", "P1 ", coopDebug.P1LeftPanel.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.tabs_container", "{0}StashTabsContainer: 0x{1:X}", "P1 ", coopDebug.P1StashTabsContainer.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.active_tab", "{0}ActiveTab: 0x{1:X}", "P1 ", coopDebug.P1ActiveTab.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.status", "{0}Status: {1}", "P1 ", coopDebug.P1Status));
                        
                        if (coopDebug.P1TabsInfo != null && coopDebug.P1TabsInfo.Count > 0 && ImGui.TreeNode(PluginText.F("stashutility.debug.tab_details", "P{0} Tabs List Details", 1)))
                        {
                            foreach (var info in coopDebug.P1TabsInfo)
                            {
                                ImGui.Text(info);
                            }
                            ImGui.TreePop();
                        }

                        ImGui.Separator();
                        ImGui.Text(PluginText.F("stashutility.debug.right_panel", "{0}RightPanel: 0x{1:X}", "P2 ", coopDebug.P2RightPanel.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.tabs_container", "{0}StashTabsContainer: 0x{1:X}", "P2 ", coopDebug.P2StashTabsContainer.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.active_tab", "{0}ActiveTab: 0x{1:X}", "P2 ", coopDebug.P2ActiveTab.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.status", "{0}Status: {1}", "P2 ", coopDebug.P2Status));

                        if (coopDebug.P2TabsInfo != null && coopDebug.P2TabsInfo.Count > 0 && ImGui.TreeNode(PluginText.F("stashutility.debug.tab_details", "P{0} Tabs List Details", 2)))
                        {
                            foreach (var info in coopDebug.P2TabsInfo)
                            {
                                ImGui.Text(info);
                            }
                            ImGui.TreePop();
                        }
                    }
                    else
                    {
                        ImGui.Text(PluginText.T("stashutility.debug.coop_off", "Coop Mode: FALSE (Single Player)"));
                        ImGui.Text(PluginText.F("stashutility.debug.panel", "{0}LeftPanel: 0x{1:X}", "", spDebug.LeftPanel.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.tabs_path", "StashTabsPath: {0}", spDebug.StashTabsPath));
                        ImGui.Text(PluginText.F("stashutility.debug.tabs_container", "{0}StashTabsContainer: 0x{1:X}", "", spDebug.StashTabsContainer.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.active_tab", "{0}ActiveTab: 0x{1:X}", "", spDebug.ActiveTab.ToInt64()));
                        ImGui.Text(PluginText.F("stashutility.debug.status", "{0}Status: {1}", "", spDebug.Status));
                    }
                    ImGui.Spacing();
                }

                debugHoveredCurrentElement = false;
                debugHoveredAllChildren = false;
                debugHoveredChildIndex = -1;

                if (ImGui.CollapsingHeader(PluginText.T("stashutility.debug.explorer_header", "UI Path Offsets (Debug Explorer)")))
                {
                    ImGui.SeparatorText(PluginText.T("stashutility.debug.path_nav", "Path Navigation"));

                    // Sync currentDebugPath with Settings.PathString
                    string expectedPathStr = string.Join(",", currentDebugPath);
                    if (Settings.PathString != expectedPathStr)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(Settings.PathString))
                            {
                                currentDebugPath.Clear();
                            }
                            else
                            {
                                currentDebugPath.Clear();
                                currentDebugPath.AddRange(Settings.PathString
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                    .Select(int.Parse));
                            }
                        }
                        catch
                        {
                            // Invalid input, don't update list
                        }
                    }

                    ImGui.InputText(PluginText.T("stashutility.debug.path_indices", "Path Indices"), ref Settings.PathString, 128);
                    ImGuiHelper.ToolTip(PluginText.T("stashutility.debug.path_indices_tooltip", "Comma-separated indices starting from the LeftPanel root. Edit manually or click through the explorer."));

                    if (ImGui.Button(PluginText.T("stashutility.debug.reset_path", "Reset Path to Default")))
                    {
                        Settings.PathString = "2,0,0,0,1,1,45,0,1";
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(PluginText.T("stashutility.debug.dump_tree", "Dump UI Tree to File")))
                    {
                        var gameUi = Core.States.InGameStateObject.GameUi;
                        if (gameUi != null && gameUi.Address != IntPtr.Zero)
                        {
                            if (this.handleObj == null)
                            {
                                InitReflection();
                            }
                            if (this.handleObj != null)
                            {
                                try
                                {
                                    var pathIndices = Settings.PathString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                        .Select(int.Parse)
                                        .ToArray();
                                    var resolved = ResolvePath(gameUi.LeftPanel.Address, pathIndices);
                                    if (resolved != IntPtr.Zero)
                                    {
                                        DumpUiTreeToFile(resolved);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[StashUtility] Dump error: {ex}");
                                }
                            }
                        }
                    }

                    ImGui.SeparatorText(PluginText.T("stashutility.debug.interactive_explorer", "Interactive Explorer"));

                    ImGui.InputText(PluginText.T("stashutility.debug.custom_root", "Custom Root Address (Hex)"), ref explorerRootAddressStr, 64);
                    ImGuiHelper.ToolTip(PluginText.T("stashutility.debug.custom_root_tooltip", "Leave empty to use gameUi.LeftPanel.Address as root."));

                    ImGui.Text(PluginText.T("stashutility.debug.breadcrumbs", "Breadcrumbs:"));
                    ImGui.SameLine();
                    if (ImGui.Button(PluginText.T("stashutility.debug.root", "Root")))
                    {
                        currentDebugPath.Clear();
                        Settings.PathString = string.Join(",", currentDebugPath);
                    }
                    for (int i = 0; i < currentDebugPath.Count; i++)
                    {
                        ImGui.SameLine();
                        ImGui.Text("->");
                        ImGui.SameLine();
                        if (ImGui.Button($"[{currentDebugPath[i]}]##path_{i}"))
                        {
                            var truncated = currentDebugPath.Take(i + 1).ToList();
                            currentDebugPath.Clear();
                            currentDebugPath.AddRange(truncated);
                            Settings.PathString = string.Join(",", currentDebugPath);
                        }
                    }

                    var root = GetExplorerRootAddress();
                    var current = root;
                    bool pathValid = true;
                    int failedIndex = -1;

                    for (int i = 0; i < currentDebugPath.Count; i++)
                    {
                        if (current == IntPtr.Zero)
                        {
                            pathValid = false;
                            failedIndex = i;
                            break;
                        }
                        var off = ReadMemory<UiElementBaseOffset>(current);
                        var kids = ReadStdVector<IntPtr>(off.ChildrensPtr);
                        var idx = currentDebugPath[i];
                        if (idx < 0 || idx >= kids.Length)
                        {
                            pathValid = false;
                            failedIndex = i;
                            break;
                        }
                        current = kids[idx];
                    }

                    this.debugCurrentAddress = current;

                    if (root == IntPtr.Zero)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), PluginText.T("stashutility.debug.root_null", "Root address is null. (Game UI not loaded?)"));
                    }
                    else if (!pathValid)
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), PluginText.F("stashutility.debug.path_failed", "Path resolution failed at index step {0}.", failedIndex));
                    }
                    else if (current != IntPtr.Zero)
                    {
                        var off = ReadMemory<UiElementBaseOffset>(current);
                        var stringId = ReadStdWString(off.StringIdPtr);
                        var isVis = UiElementBaseFuncs.IsVisibleChecker(off.Flags);

                        ImGui.TextColored(new Vector4(0, 1, 0, 1), PluginText.T("stashutility.debug.path_success", "Path resolved successfully!"));
                        ImGui.SameLine();
                        ImGui.SmallButton(PluginText.T("stashutility.debug.hover_active", "Hover me to highlight active node in game"));
                        if (ImGui.IsItemHovered())
                        {
                            debugHoveredCurrentElement = true;
                        }

                        ImGuiHelper.IntPtrToImGui(PluginText.T("stashutility.debug.active_node_addr", "Active Node Addr"), current);
                        ImGui.Text(PluginText.F("stashutility.debug.string_id_visible", "String ID: {0}  |  Visible: {1}", stringId, isVis));

                        var kids = ReadStdVector<IntPtr>(off.ChildrensPtr);
                        ImGui.SeparatorText(PluginText.F("stashutility.debug.children_count", "Children ({0})", kids.Length));

                        ImGui.SmallButton(PluginText.T("stashutility.debug.hover_all_children", "Hover me to highlight all children bounds"));
                        if (ImGui.IsItemHovered())
                        {
                            debugHoveredAllChildren = true;
                        }

                        if (ImGui.BeginChild("ExplorerChildrenList", new Vector2(0, 200), ImGuiChildFlags.Borders))
                        {
                            debugChildrenAddresses.Clear();
                            for (int j = 0; j < kids.Length; j++)
                            {
                                var childAddr = kids[j];
                                debugChildrenAddresses.Add(childAddr);
                                if (childAddr == IntPtr.Zero)
                                {
                                    ImGui.Text(PluginText.F("stashutility.debug.null_pointer", "[{0}] Null Pointer", j));
                                    continue;
                                }

                                var childOff = ReadMemory<UiElementBaseOffset>(childAddr);
                                var childId = ReadStdWString(childOff.StringIdPtr);
                                var childVis = UiElementBaseFuncs.IsVisibleChecker(childOff.Flags);

                                if (childVis)
                                {
                                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 1, 0, 1));
                                }
                                else
                                {
                                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.7f, 0.7f, 0.7f, 1));
                                }

                                if (ImGui.Selectable($"[{j}] Addr: 0x{childAddr.ToInt64():X} | ID: {childId}##child_{j}"))
                                {
                                    currentDebugPath.Add(j);
                                    Settings.PathString = string.Join(",", currentDebugPath);
                                }
                                if (ImGui.IsItemHovered())
                                {
                                    debugHoveredChildIndex = j;
                                }

                                ImGui.PopStyleColor();
                            }
                        }
                        ImGui.EndChild();
                    }
                }

                if (ImGui.CollapsingHeader(PluginText.T("stashutility.debug.waystone_inspector_header", "Hovered Waystone Inspector (Debug)")))
                {
                    ImGui.Checkbox(PluginText.T("stashutility.debug.freeze_waystone", "Freeze Hovered Waystone"), ref freezeHoveredWaystone);
                    ImGui.SameLine();
                    if (ImGui.Button(PluginText.T("stashutility.debug.clear", "Clear")))
                    {
                        lastHoveredWaystone = null;
                    }

                    if (lastHoveredWaystone != null)
                    {
                        ImGui.SeparatorText(PluginText.T("stashutility.debug.item_details", "Item Details"));
                        ImGuiHelper.IntPtrToImGui(PluginText.T("stashutility.debug.entity_address", "Entity Address"), lastHoveredWaystone.Address);
                        ImGuiHelper.DisplayTextAndCopyOnClick(PluginText.F("stashutility.debug.item_path", "Path: {0}", lastHoveredWaystone.Path), lastHoveredWaystone.Path);

                        if (lastHoveredWaystone.TryGetComponent<Base>(out var baseComp))
                        {
                            ImGuiHelper.DisplayTextAndCopyOnClick(PluginText.F("stashutility.debug.base_name", "Base Name: {0}", baseComp.BaseItemName), baseComp.BaseItemName);
                            ImGuiHelper.DisplayTextAndCopyOnClick(PluginText.F("stashutility.debug.internal_name", "Internal Name: {0}", baseComp.InternalName), baseComp.InternalName);
                        }

                        if (lastHoveredWaystone.TryGetComponent<Mods>(out var modsComp))
                        {
                            ImGui.Text(PluginText.F("stashutility.debug.rarity", "Rarity: {0}", modsComp.Rarity));

                            if (modsComp.ImplicitMods.Count > 0 && ImGui.TreeNode(PluginText.T("stashutility.debug.implicit_mods", "Implicit Mods")))
                            {
                                foreach (var mod in modsComp.ImplicitMods)
                                {
                                    ImGui.Text($"{mod.name}: ({mod.values.value0}, {mod.values.value1})");
                                }
                                ImGui.TreePop();
                            }
                            if (modsComp.ExplicitMods.Count > 0 && ImGui.TreeNode(PluginText.T("stashutility.debug.explicit_mods", "Explicit Mods")))
                            {
                                foreach (var mod in modsComp.ExplicitMods)
                                {
                                    ImGui.Text($"{mod.name}: ({mod.values.value0}, {mod.values.value1})");
                                }
                                ImGui.TreePop();
                            }
                            if (modsComp.EnchantMods.Count > 0 && ImGui.TreeNode(PluginText.T("stashutility.debug.enchant_mods", "Enchant Mods")))
                            {
                                foreach (var mod in modsComp.EnchantMods)
                                {
                                    ImGui.Text($"{mod.name}: ({mod.values.value0}, {mod.values.value1})");
                                }
                                ImGui.TreePop();
                            }
                            var statsFromMods = GetStatsFromMods(modsComp);
                            if (statsFromMods.Count > 0)
                            {
                                ImGuiHelper.StatsWidget(statsFromMods, PluginText.T("stashutility.debug.mod_stats", "Stats From Mods"));
                            }
                        }

                        if (lastHoveredWaystone.TryGetComponent<ObjectMagicProperties>(out var omp))
                        {
                            if (omp.ModStats.Count > 0)
                            {
                                ImGuiHelper.StatsWidget(omp.ModStats, PluginText.T("stashutility.debug.magic_stats", "Stats From Magic Properties"));
                            }
                        }

                        if (lastHoveredWaystone.TryGetComponent<Mods>(out var modsCompForDebug))
                        {
                            var allRawMods = modsCompForDebug.ImplicitMods.Concat(modsCompForDebug.ExplicitMods).Concat(modsCompForDebug.EnchantMods).ToList();
                            if (allRawMods.Count > 0 && ImGui.TreeNode(PluginText.T("stashutility.debug.raw_mods", "Raw Game Memory Mods (For Matching)")))
                            {
                                foreach (var mod in allRawMods)
                                {
                                    ImGui.Text(PluginText.F("stashutility.debug.raw_id", "Raw ID: {0}", mod.name));
                                }
                                ImGui.TreePop();
                            }
                        }

                        ImGui.SeparatorText(PluginText.T("stashutility.debug.advanced", "Advanced"));
                        if (ImGui.Button(PluginText.T("stashutility.debug.dump_full_memory", "Dump Full Memory To File")))
                        {
                            DumpAllWaystonesMemory(lastHoveredWaystone);
                        }

                        var field = typeof(Entity).GetField("componentAddresses", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (field != null)
                        {
                            if (field.GetValue(lastHoveredWaystone) is System.Collections.Concurrent.ConcurrentDictionary<string, IntPtr> dict)
                            {
                                if (ImGui.TreeNode(PluginText.T("stashutility.debug.components", "All Components (Raw Addresses)")))
                                {
                                    foreach (var kv in dict)
                                    {
                                        ImGuiHelper.IntPtrToImGui(kv.Key, kv.Value);
                                    }
                                    ImGui.TreePop();
                                }
                            }
                        }
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1, 1, 0, 1), PluginText.T("stashutility.debug.hover_hint", "Hover over a waystone in your Stash Tab to inspect it."));
                    }
                }
            }
        }

        private void DrawWaystoneFilterSettings()
        {
            if (ImGui.Checkbox(PluginText.T("stashutility.enable_waystone_manager", "Enable Waystone Manager"), ref Settings.EnableWaystoneManager)) SaveSettings();
            if (!Settings.EnableWaystoneManager) return;
            ImGui.TextWrapped(PluginText.T("stashutility.filters.waystone_help", "Choose the modifiers you want or want to avoid. Numerical limits are optional."));
            if (ImGui.CollapsingHeader(PluginText.T("stashutility.waystone_criteria_header", "Numerical limits")))
            {
                ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X));
                if (ImGui.SliderInt(PluginText.T("stashutility.min_tier", "Minimum tier"), ref Settings.MinTier, 1, 16)) SaveSettings();
                DrawFilterThreshold("stashutility.filter_revives", "Limit revives", "revives", ref Settings.FilterMaxRevives, ref Settings.MaxRevivesAvailable, 5);
                if (ImGui.BeginTable("FiltersTable", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn(PluginText.T("stashutility.table.criteria", "Criteria"), ImGuiTableColumnFlags.WidthStretch, 1.3f);
                    ImGui.TableSetupColumn(PluginText.T("stashutility.table.min_limit", "Minimum"));
                    ImGui.TableSetupColumn(PluginText.T("stashutility.table.max_limit", "Maximum"));
                    ImGui.TableHeadersRow();
                    DrawMinMaxFilterTableRow("Explicit Mods", "ExplicitMods", 10, ref Settings.FilterMinExplicitMods, ref Settings.MinExplicitMods, ref Settings.FilterMaxExplicitMods, ref Settings.MaxExplicitMods);
                    DrawMinMaxFilterTableRow("Item Rarity", "ItemRarity", 200, ref Settings.FilterMinItemRarity, ref Settings.MinItemRarity, ref Settings.FilterMaxItemRarity, ref Settings.MaxItemRarity);
                    DrawMinMaxFilterTableRow("Pack Size", "PackSize", 100, ref Settings.FilterMinPackSize, ref Settings.MinPackSize, ref Settings.FilterMaxPackSize, ref Settings.MaxPackSize);
                    DrawMinMaxFilterTableRow("Monster Rarity", "MonsterRarity", 100, ref Settings.FilterMinMonsterRarity, ref Settings.MinMonsterRarity, ref Settings.FilterMaxMonsterRarity, ref Settings.MaxMonsterRarity);
                    DrawMinMaxFilterTableRow("Monster Effectiveness", "MonsterEffectiveness", 100, ref Settings.FilterMinMonsterEffectiveness, ref Settings.MinMonsterEffectiveness, ref Settings.FilterMaxMonsterEffectiveness, ref Settings.MaxMonsterEffectiveness);
                    DrawMinMaxFilterTableRow("Waystone Drop Chance", "DropChance", 300, ref Settings.FilterMinWaystoneDropChance, ref Settings.MinWaystoneDropChance, ref Settings.FilterMaxWaystoneDropChance, ref Settings.MaxWaystoneDropChance);
                    ImGui.EndTable();
                }
            }
            if (ImGui.CollapsingHeader(PluginText.T("stashutility.waystone_great_conditions_header", "Excellent items")))
            {
                DrawFilterThreshold("stashutility.filter_great_explicit_mods", "Minimum explicit modifiers", "greatExplicit", ref Settings.FilterGreatExplicitMods, ref Settings.MinGreatExplicitMods, 10);
                DrawFilterThreshold("stashutility.filter_great_max_explicit_mods", "Maximum explicit modifiers", "greatMaxExplicit", ref Settings.FilterGreatMaxExplicitMods, ref Settings.MaxGreatExplicitMods, 10);
                DrawFilterThreshold("stashutility.filter_great_rarity", "Item rarity", "greatRarity", ref Settings.FilterGreatRarity, ref Settings.MinGreatRarity, 200);
                DrawFilterThreshold("stashutility.filter_great_pack_size", "Pack size", "greatPack", ref Settings.FilterGreatPackSize, ref Settings.MinGreatPackSize, 100);
                DrawFilterThreshold("stashutility.filter_great_monster_rarity", "Monster rarity", "greatMonster", ref Settings.FilterGreatMonstRarity, ref Settings.MinGreatMonstRarity, 100);
                DrawFilterThreshold("stashutility.filter_great_monster_effectiveness", "Monster effectiveness", "greatEffect", ref Settings.FilterGreatEffect, ref Settings.MinGreatEffect, 100);
                DrawFilterThreshold("stashutility.filter_great_waystone_drop_chance", "Waystone drops", "greatDrop", ref Settings.FilterGreatDropChance, ref Settings.MinGreatDropChance, 300);
            }
            if (ImGui.Checkbox(PluginText.T("stashutility.filters.all_waystone", "Require all wanted modifiers"), ref Settings.RequireAllGoodMods)) SaveSettings();
            ImGuiHelper.ToolTip(PluginText.T("stashutility.filters.all_waystone_help", "Off: one wanted modifier is enough. On: all your wanted modifiers must be present."));
            if (ImGui.Checkbox(PluginText.T("stashutility.filter_bad_mods_only_on_highlighted", "Only check unwanted modifiers after numerical limits"), ref Settings.FilterBadModsOnlyOnHighlighted)) SaveSettings();
            DrawModCatalog("waystone", Data.ModDatabase.AllWaystoneMods.Select(m => new Models.TabletMod(m.Id, m.Name, m.Affix)).ToList(), Settings.GoodModPatterns, Settings.BadModPatterns, null, null, ref waystoneSearchTerm, ref waystoneConfiguredOnly, ref waystoneAffixFilter);
        }

        private void DrawTabletFilterSettings()
        {
            if (ImGui.Checkbox(PluginText.T("stashutility.enable_tablet_manager", "Enable Tablet Manager"), ref Settings.EnableTabletManager)) SaveSettings();
            string helpLabel = MT(Settings.TabletHelpTooltips ? "help_on" : "help_off", Settings.TabletHelpTooltips ? "Tooltips: ON" : "Tooltips: OFF");
            if (ImGui.GetItemRectMax().X + ImGui.CalcTextSize(helpLabel).X + ImGui.GetStyle().FramePadding.X * 2 + 12 <
                ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X) ImGui.SameLine();
            if (ImGui.SmallButton(helpLabel + "##TabletHelp"))
            { Settings.TabletHelpTooltips = !Settings.TabletHelpTooltips; SaveSettings(); }
            using var help = ImGuiHelper.UseHelpTooltips(Settings.TabletHelpTooltips);
            if (!Settings.EnableTabletManager) return;
            DrawTabletVisibilityButton(false);
            TabletProfile SelectedProfile(string selectedType)
            {
                if (!Settings.TabletProfiles.TryGetValue(selectedType, out var selected) || selected == null)
                    Settings.TabletProfiles[selectedType] = selected = new TabletProfile();
                return selected;
            }
            if (ImGui.CollapsingHeader(MT("display_options", "Display options")))
            {
                if (ImGui.Checkbox(PluginText.T("stashutility.tablet.auto_hide", "Hide tablet indications when hovering an item"), ref Settings.HideTabletHighlightsOnItemHover)) SaveSettings();
                TabletHelp(PluginText.T("stashutility.tablet.auto_hide_help", "Temporarily hide indications while hovering an item or viewing its game tooltip."));
            }
            if (ImGui.CollapsingHeader(PluginText.T("stashutility.filters.farm_preset", "Farming ranks - PoE 2 0.5.5")))
            {
                ImGui.BeginDisabled(Settings.TabletMode != "custom");
                if (ImGui.Button(PluginText.T("stashutility.filters.farm_preset_apply", "Reapply farming ranks to all 8 tablets")))
                {
                    if (ApplyTabletFarmPreset(true)) SaveSettings();
                }
                ImGui.EndDisabled();
                TabletHelp(PluginText.T("stashutility.filters.farm_preset_apply_help", "Apply workbook ranks without changing minimum rolls or AND / OR profiles."));
            }
            if (ImGui.CollapsingHeader(PluginText.T("stashutility.filters.highlight_rules", "Highlight rules")))
            {
                DrawFilterThreshold("stashutility.filter_tablet_great", "Excellent highlight", "tabletGreat", ref Settings.FilterTabletGreat, ref Settings.MinTabletGoodMods, 4);
                TabletHelp(PluginText.T("stashutility.filters.tablet_rank_conditions", "The best qualifying modifier sets the color. Index 1 is the best attainable grade combination for this tablet type and your selected ranks (up to two prefixes and two suffixes). Minimum rolls apply; rolls above the minimum do not change the index."));
            }
            ImGui.Separator();
            ImGui.TextColored(ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark], MT("rank_settings", "Modifier ranks S to F"));
            TabletHelp(PluginText.T("stashutility.filters.rank_help", "Choose S to F for each modifier. Unchecked modifiers and rolls below their minimum do not contribute to the classification."));
            DrawTabletTypeSelector();
            DrawTabletModes();
            DrawTabletPresets();
            string type = StashUtilitySettings.TabletTypes[tabletFilterType];
            var profile = SelectedProfile(type);
            var catalog = Data.ModDatabase.AllTabletMods.Where(m => m.TabletTypes.Contains(type)).ToList();
            profile.ModRanks ??= new Dictionary<string, string>();
            if (MigrateTabletModRanks(profile)) SaveSettings();
            ImGui.BeginDisabled(Settings.TabletMode != "custom");
            DrawTabletMatchMode(type, profile);
            ImGui.EndDisabled();
            DrawTabletMetaFilters(type, profile);
            DrawModCatalog("tablet", catalog, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns, profile.ModRequiredMinRolls, ref tabletSearchTerm, ref tabletConfiguredOnly, ref tabletAffixFilter, profile.ModRanks);
        }

        // Original game art from https://poe2db.tw/us/Tablet, bundled for offline use.
        private void LoadTabletIcons()
        {
            tabletIconHandles.Clear();
            string folder = Path.Combine(DllDirectory, "images");
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "Tablet*.webp"))
            {
                try
                {
                    Core.Overlay.AddOrGetImagePointer(file, false, out var handle, out _, out _);
                    if (handle != IntPtr.Zero) tabletIconHandles[Path.GetFileNameWithoutExtension(file)] = handle;
                }
                catch { /* A missing/invalid icon must not disable tablet settings. */ }
            }
        }

        private void UnloadTabletIcons()
        {
            foreach (string name in tabletIconHandles.Keys)
            {
                try { Core.Overlay.RemoveImage(Path.Combine(DllDirectory, "images", name + ".webp")); }
                catch { /* The overlay may already be shutting down. */ }
            }
            tabletIconHandles.Clear();
        }

        private void DrawTabletTypeSelector()
        {
            int[] order = { 4, 3, 1, 2, 0, 6, 7, 5 };
            string[] names = { "Breach", "Expedition", "Delirium", "Ritual", "Irradiated", "Boss", "Abyss", "Temple" };
            Vector4[] colors = { new(.8f,.35f,.9f,1), new(.9f,.7f,.3f,1), new(.65f,.68f,.74f,1), new(.62f,.43f,1,1),
                new(.45f,.83f,.48f,1), new(1,.45f,.3f,1), new(.72f,.82f,.26f,1), new(.35f,.77f,.8f,1) };
            float gap = ImGui.GetStyle().ItemSpacing.X;
            int columns = Math.Clamp((int)((ImGui.GetContentRegionAvail().X + gap) / (100 + gap)), 1, 8);
            float width = Math.Max(40, (ImGui.GetContentRegionAvail().X - gap * (columns - 1)) / columns);
            float height = 44 + ImGui.GetTextLineHeight();
            int rows = (8 + columns - 1) / columns;
            ImGui.BeginChild("tabletTypes", new Vector2(0, height * rows + gap * (rows - 1) + 8), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            for (int n = 0; n < order.Length; n++)
            {
                int i = order[n];
                if (n % columns > 0) ImGui.SameLine();
                ImGui.PushID(i);
                var pos = ImGui.GetCursorScreenPos();
                if (ImGui.InvisibleButton("type", new Vector2(width, height)))
                {
                    tabletFilterType = i;
                    tabletSearchTerm = string.Empty;
                    tabletConfiguredOnly = false;
                    tabletAffixFilter = 0;
                }
                bool active = tabletFilterType == i;
                var dl = ImGui.GetWindowDrawList();
                dl.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.GetColorU32(active ? ImGuiCol.HeaderActive : ImGui.IsItemHovered() ? ImGuiCol.HeaderHovered : ImGuiCol.FrameBg), 5);
                if (active) dl.AddRect(pos, pos + new Vector2(width, height), ImGuiHelper.Color(colors[i]), 5, ImDrawFlags.None, 1.5f);
                var center = pos + new Vector2(width / 2, 20);
                if (tabletIconHandles.TryGetValue("Tablet" + names[i], out var icon))
                    dl.AddImage(icon, center - new Vector2(16), center + new Vector2(16));
                string label = PluginText.T("stashutility.tablet.selector." + names[i], names[i]);
                var textSize = ImGui.CalcTextSize(label);
                dl.AddText(pos + new Vector2((width - textSize.X) / 2, 38), ImGui.GetColorU32(active ? ImGuiCol.Text : ImGuiCol.TextDisabled), label);
                ImGuiHelper.ToolTip(TabletTypeLabel(StashUtilitySettings.TabletTypes[i]));
                ImGui.PopID();
            }
            ImGui.EndChild();
        }

        private string TabletTypeLabel(string type) => PluginText.T("stashutility.tablet.category." + type.Replace(" ", ""), type);

        private void DrawJewelFilterSettings()
        {
            if (ImGui.Checkbox(PluginText.T("stashutility.enable_jewel_manager", "Enable Jewel Manager"), ref Settings.EnableJewelManager)) SaveSettings();
            if (!Settings.EnableJewelManager) return;
            if (ImGui.GetContentRegionAvail().X > 600) ImGui.TextWrapped(PluginText.T("stashutility.filters.jewel_help", "Build a profile for each jewel type. A jewel is highlighted when it meets at least one profile, including its minimum rolls."));
            string[] categories = { "Sapphire", "Ruby", "Emerald", "Diamond", "Time-Lost Sapphire", "Time-Lost Ruby", "Time-Lost Emerald", "Time-Lost Diamond" };
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##jewelType", PluginText.T("stashutility.jewel.category." + categories[jewelFilterType], categories[jewelFilterType])))
            {
                for (int i = 0; i < categories.Length; i++)
                    if (ImGui.Selectable(PluginText.T("stashutility.jewel.category." + categories[i], categories[i]), i == jewelFilterType)) { jewelFilterType = i; jewelFilterGroup = 0; }
                ImGui.EndCombo();
            }
            string category = categories[jewelFilterType];
            var catalog = Data.ModDatabase.AllJewelMods.Where(m => m.Category == category).ToList();
            if (!Settings.JewelCategoryGroups.TryGetValue(category, out var groups) || groups == null)
                Settings.JewelCategoryGroups[category] = groups = new List<Models.JewelGroup>();
            if (groups.Count == 0)
            {
                var initial = new Models.JewelGroup { MinMatchCount = Settings.MinJewelGoodModsToHighlight };
                initial.ModPatternIds = Settings.JewelGoodModPatterns.Where(id => catalog.Any(m => m.Id == id)).ToList();
                foreach (string id in initial.ModPatternIds)
                    if (Settings.JewelModRequiredMinRolls.TryGetValue(id, out float roll)) initial.ModRequiredMinRolls[id] = roll;
                groups.Add(initial);
                SaveSettings();
            }
            jewelFilterGroup = Math.Clamp(jewelFilterGroup, 0, groups.Count - 1);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##jewelProfile", JewelProfileLabel(groups[jewelFilterGroup], jewelFilterGroup)))
            {
                for (int i = 0; i < groups.Count; i++)
                    if (ImGui.Selectable(JewelProfileLabel(groups[i], i) + "##" + groups[i].Id, i == jewelFilterGroup)) jewelFilterGroup = i;
                ImGui.EndCombo();
            }
            var group = groups[jewelFilterGroup];
            if (ImGui.CollapsingHeader(PluginText.T("stashutility.filters.profile_options", "Profile settings")))
            {
                if (ImGui.Button(PluginText.T("stashutility.jewel.add_group", "Add profile")))
                {
                    groups.Add(new Models.JewelGroup { Name = PluginText.F("stashutility.jewel.group", "Profile {0}", groups.Count + 1), MinMatchCount = 2 });
                    jewelFilterGroup = groups.Count - 1;
                    SaveSettings();
                    group = groups[jewelFilterGroup];
                }
                if (groups.Count > 1)
                {
                    ImGui.SameLine();
                    if (ImGui.Button(PluginText.T("stashutility.jewel.delete_group", "Delete profile")))
                    {
                        groups.RemoveAt(jewelFilterGroup);
                        jewelFilterGroup = Math.Min(jewelFilterGroup, groups.Count - 1);
                        SaveSettings();
                        group = groups[jewelFilterGroup];
                    }
                }
                    string name = group.Name ?? "";
                ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X * .5f));
                if (ImGui.InputText(PluginText.T("stashutility.jewel.group_name", "Profile name"), ref name, 80)) { group.Name = name; SaveSettings(); }
                ImGui.SetNextItemWidth(120);
                int minMatch = group.MinMatchCount;
                if (ImGui.SliderInt(PluginText.T("stashutility.jewel.min_match", "Wanted modifiers needed"), ref minMatch, 1, 6)) { group.MinMatchCount = minMatch; SaveSettings(); }
                DrawFilterThreshold("stashutility.filter_jewel_great", "Excellent highlight", "jewelGreat", ref Settings.FilterJewelGreat, ref Settings.MinJewelGoodMods, 6);
            }
            ImGui.TextWrapped(PluginText.F("stashutility.filters.profile_summary", "This profile needs {0} matching modifiers; {1} configured.", group.MinMatchCount, group.ModPatternIds.Count));
            if (group.MinMatchCount > group.ModPatternIds.Count)
                ImGui.TextWrapped(PluginText.T("stashutility.filters.incomplete_profile", "Add more wanted modifiers or reduce the required count: this profile cannot match yet."));
            var rows = catalog.Select(m => new Models.TabletMod(m.Id, m.Name, m.Type.ToLowerInvariant()) { MinRoll = m.MinRoll, MaxRoll = m.MaxRoll, RollValueIndex = m.RollValueIndex, RollDivisor = m.RollDivisor }).ToList();
            DrawModCatalog("jewel", rows, group.ModPatternIds, null, null, group.ModRequiredMinRolls, ref jewelSearchTerm, ref jewelConfiguredOnly, ref jewelAffixFilter);
        }

        private string JewelProfileLabel(Models.JewelGroup group, int index) => group.Name == "Default Profile"
            ? PluginText.T("stashutility.filters.default_profile", "Default profile")
            : string.IsNullOrWhiteSpace(group.Name) ? PluginText.F("stashutility.jewel.group", "Profile {0}", index + 1) : group.Name;

        private void DrawFilterThreshold(string key, string fallback, string id, ref bool enabled, ref int value, int maximum)
        {
            ImGui.PushID(id);
            if (ImGui.Checkbox(PluginText.T(key, fallback), ref enabled)) SaveSettings();
            if (enabled)
            {
                ImGui.SetNextItemWidth(Math.Min(220, ImGui.GetContentRegionAvail().X));
                if (ImGui.SliderInt("##value", ref value, 0, maximum)) SaveSettings();
            }
            ImGui.PopID();
        }

        private string ModDisplayName(string id, string name) => PluginText.T("stashutility.mod." + id, name);

        private static string NormalizeModSearch(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            return new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).ToLowerInvariant();
        }

        private bool MatchesModSearch(string search, string id, string name)
        {
            string haystack = NormalizeModSearch(id + " " + name + " " + ModDisplayName(id, name));
            return NormalizeModSearch(search).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(haystack.Contains);
        }

        private void DrawModCatalog(string scope, List<Models.TabletMod> mods, List<string> good, List<string> bad, List<string> priority, Dictionary<string, float> rolls, ref string search, ref bool configuredOnly, ref int affixFilter, Dictionary<string, string> ranks = null)
        {
            ImGui.PushID(scope);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##modSearch", PluginText.T("stashutility.filters.search", "Search a modifier in French or English..."), ref search, 160);
            ImGui.Checkbox(PluginText.T("stashutility.filters.configured_only", "Configured rules only"), ref configuredOnly);
            string[] affixes = { "all", "prefix", "suffix", "corruption", "desecrated" };
            if (scope == "tablet")
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i > 0) ImGui.SameLine();
                    bool selectedAffix = i == affixFilter;
                    if (selectedAffix) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive]);
                    if (ImGui.Button(PluginText.T("stashutility.filters.affix." + affixes[i], affixes[i]) + "##affix" + i)) affixFilter = i;
                    if (selectedAffix) ImGui.PopStyleColor();
                }
            }
            else
            {
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##affix", PluginText.T("stashutility.filters.affix." + affixes[affixFilter], affixes[affixFilter])))
            {
                for (int i = 0; i < (scope == "jewel" ? affixes.Length : 3); i++)
                    if (ImGui.Selectable(PluginText.T("stashutility.filters.affix." + affixes[i], affixes[i]), i == affixFilter)) affixFilter = i;
                ImGui.EndCombo();
            }
            }
            var selected = good.Concat(bad ?? new List<string>()).Concat(priority ?? new List<string>())
                .Where(id => scope != "tablet" || mods.Any(m => m.Id == id)).Distinct().ToList();
            // Historical/unavailable rules stay editable, including after game patches.
            foreach (string id in selected)
                if (!mods.Any(m => m.Id == id)) mods.Add(new Models.TabletMod(id, id, "legacy"));
            bool onlySelected = configuredOnly;
            int affix = affixFilter;
            string query = search;
            var filtered = mods.Where(m => (!onlySelected || selected.Contains(m.Id)) && (affix == 0 || m.Affix == affixes[affix]) && MatchesModSearch(query, m.Id, m.Name))
                .OrderBy(m => scope == "tablet" && m.Affix == "suffix" ? 1 : 0).ThenBy(m => ModDisplayName(m.Id, m.Name), StringComparer.CurrentCultureIgnoreCase).ToList();
            ImGui.TextDisabled(PluginText.F("stashutility.filters.catalog_summary", "{0} modifiers shown · {1} configured rules", filtered.Count, selected.Count));
            float height = Math.Clamp(ImGui.GetContentRegionAvail().Y - 8, 160, 460);
            bool rankGrid = ranks != null && ImGui.GetContentRegionAvail().X >= 620;
            int columns = rankGrid ? TabletModRanks.Length + 2 : 3;
            bool scrollInPanel = scope == "tablet";
            if (ImGui.BeginTable("mods_" + rankGrid, columns, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp |
                (scrollInPanel ? ImGuiTableFlags.None : ImGuiTableFlags.ScrollY), scrollInPanel ? Vector2.Zero : new Vector2(0, height)))
            {
                ImGui.TableSetupColumn(PluginText.T("stashutility.filters.modifier", "Modifier"), ImGuiTableColumnFlags.WidthStretch);
                float ruleWidth = Math.Max(120, new[] { "ignore", "wanted", "avoid", "priority" }
                    .Max(state => ImGui.CalcTextSize(PluginText.T("stashutility.filters.state." + state, state)).X) + ImGui.GetFrameHeight() + 12);
                if (rankGrid)
                    foreach (string rank in TabletModRanks)
                        ImGui.TableSetupColumn(rank, ImGuiTableColumnFlags.WidthFixed,
                            Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(rank).X) + 6);
                else ImGui.TableSetupColumn(ranks != null ? PluginText.T("stashutility.filters.rank", "Rank") : PluginText.T("stashutility.filters.rule", "Rule"),
                    ImGuiTableColumnFlags.WidthFixed, ranks != null ? 74 : ruleWidth);
                ImGui.TableSetupColumn(PluginText.T("stashutility.filters.minimum", "Minimum"), ImGuiTableColumnFlags.WidthFixed, 82);
                ImGui.TableSetupScrollFreeze(0, 1);
                if (rankGrid)
                {
                    ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TableHeader(PluginText.T("stashutility.filters.modifier", "Modifier"));
                    for (int i = 0; i < TabletModRanks.Length; i++)
                    {
                        ImGui.TableSetColumnIndex(i + 1);
                        ImGui.PushStyleColor(ImGuiCol.Text, TabletModRankColors[i]);
                        ImGui.TableHeader(TabletModRanks[i]);
                        ImGui.PopStyleColor();
                    }
                    ImGui.TableSetColumnIndex(columns - 1);
                    ImGui.TableHeader(PluginText.T("stashutility.filters.minimum", "Minimum"));
                }
                else ImGui.TableHeadersRow();
                string lastAffix = null;
                foreach (var mod in filtered)
                {
                    if (scope == "tablet" && mod.Affix != lastAffix)
                    {
                        lastAffix = mod.Affix;
                        ImGui.TableNextRow();
                        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.Header));
                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted(PluginText.T("stashutility.filters.affix." + mod.Affix, mod.Affix) + " (" + filtered.Count(m => m.Affix == mod.Affix) + ")");
                    }
                    ImGui.BeginDisabled(scope == "tablet" && Settings.TabletMode != "custom");
                    DrawModConfigRow(mod, good, bad, priority, rolls, ranks, rankGrid);
                    ImGui.EndDisabled();
                }
                ImGui.EndTable();
            }
            if (filtered.Count == 0) ImGui.TextWrapped(PluginText.T("stashutility.filters.no_results", "No matching modifiers. Clear the search or show all rules."));
            ImGui.PopID();
        }

        private void DrawAppearanceSettings()
        {
            ImGui.Checkbox(PluginText.T("stashutility.show_in_bg", "Show Overlay When Game in Background"), ref Settings.ShowOverlayInBackground);
            ImGuiHelper.ToolTip(PluginText.T("stashutility.show_in_bg_tooltip", "If checked, the waystone highlights will remain visible even when the game window is in the background."));

            ImGui.Checkbox(PluginText.T("stashutility.enable_merchant_purchase", "Enable Merchant Purchase Panel Overlay"), ref Settings.EnableMerchantPurchasePanel);
            ImGuiHelper.ToolTip(PluginText.T("stashutility.enable_merchant_purchase_tooltip", "When checked, waystones and tablets in the active merchant purchase panel tab will be highlighted based on your criteria."));

            if (ImGui.CollapsingHeader(PluginText.T("stashutility.visual_settings_header", "Overlay Visual Settings"), ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Checkbox(PluginText.T("stashutility.show_waystone_mod_border", "Show Waystone Mod Highlight Border"), ref Settings.ShowModBorder);
                ImGui.Checkbox(PluginText.T("stashutility.show_tablet_mod_border", "Show Tablet Mod Highlight Border"), ref Settings.ShowTabletModBorder);
                ImGui.Checkbox(PluginText.T("stashutility.show_rarity_border", "Show Rarity Corner Indicator"), ref Settings.ShowRarityBorder);
                if (Settings.ShowRarityBorder)
                {
                    ImGui.SetNextItemWidth(150f);
                    ImGui.SliderFloat(PluginText.T("stashutility.rarity_indicator_size", "Indicator Size##size"), ref Settings.RarityIndicatorSize, 5f, 30f);
                }
                ImGui.Checkbox(PluginText.T("stashutility.hide_normal_waystones", "Hide Normal (White) Waystones"), ref Settings.HideNormalWaystones);
                ImGui.SliderFloat(PluginText.T("stashutility.border_thickness", "Border Thickness"), ref Settings.BorderThickness, 1f, 10f);
                ImGui.SliderFloat(PluginText.T("stashutility.border_margin", "Border Margin"), ref Settings.BorderMargin, 0f, 10f);

                string[] borderStyles = {
                    PluginText.T("stashutility.style_solid", "Solid"),
                    PluginText.T("stashutility.style_dashed", "Dashed"),
                    PluginText.T("stashutility.style_dotted", "Dotted")
                };
                ImGui.Combo(PluginText.T("stashutility.bad_border_style", "Bad Highlight Border Style"), ref Settings.FrameStyleBad, borderStyles, borderStyles.Length);
                ImGui.Combo(PluginText.T("stashutility.good_border_style", "Good Highlight Border Style"), ref Settings.FrameStyleGood, borderStyles, borderStyles.Length);

                string[] arrowPositions = {
                    PluginText.T("stashutility.pos_top_left", "Top-Left"),
                    PluginText.T("stashutility.pos_top_right", "Top-Right"),
                    PluginText.T("stashutility.pos_bottom_left", "Bottom-Left"),
                    PluginText.T("stashutility.pos_bottom_right", "Bottom-Right")
                };
                ImGui.Combo(PluginText.T("stashutility.great_arrow_position", "GREAT Arrow Position"), ref Settings.GreatIndicatorPosition, arrowPositions, arrowPositions.Length);
                ImGui.SliderFloat(PluginText.T("stashutility.great_arrow_size", "GREAT Arrow Size"), ref Settings.GreatIndicatorSize, 5f, 40f);

                ImGui.SeparatorText(PluginText.T("stashutility.colors_section", "Colors"));
                if (ImGui.ColorEdit4(PluginText.T("stashutility.good_color", "Waystone Good Highlight Color"), ref Settings.GoodColor)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.bad_color", "Waystone Bad Highlight Color"), ref Settings.BadColor)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.tablet_good_color", "Tablet Good Highlight Color"), ref Settings.TabletGoodColor)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.tablet_bad_color", "Tablet Bad Highlight Color"), ref Settings.TabletBadColor)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.jewel_good_color", "Jewel Good Highlight Color"), ref Settings.JewelGoodColor)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.jewel_bad_color", "Jewel Bad Highlight Color"), ref Settings.JewelBadColor)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.waystone_great_color", "Waystone GREAT Arrow Color"), ref Settings.ColorGreat)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.tablet_great_color", "Tablet GREAT Arrow Color"), ref Settings.TabletColorGreat)) SaveSettings();
                if (ImGui.ColorEdit4(PluginText.T("stashutility.jewel_great_color", "Jewel GREAT Arrow Color"), ref Settings.JewelColorGreat)) SaveSettings();
                ImGui.ColorEdit4(PluginText.T("stashutility.rare_color", "Rare Rarity Color"), ref Settings.RareRarityColor);
                ImGui.ColorEdit4(PluginText.T("stashutility.magic_color", "Magic Rarity Color"), ref Settings.MagicRarityColor);
                ImGui.ColorEdit4(PluginText.T("stashutility.normal_color", "Normal Rarity Color"), ref Settings.NormalRarityColor);
            }
        }

        private void DrawTransferDestinationSettings()
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.70f, 0.79f, 1f));
            ImGui.TextWrapped(PluginText.T("stashutility.transfer.scan_hint", "Open your stash, scan it, then select a folder and a tab below."));
            ImGui.PopStyleColor();

            ImGui.BeginDisabled(sortRunning || affinityTransferRunning);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.39f, 0.65f, 1f));
            if (stashScanQueue.Count == 0)
            {
                if (ImGui.Button(PluginText.T("stashutility.transfer.scan", "Scan My Stash"))) BeginStashScan();
            }
            else if (ImGui.Button(PluginText.T("stashutility.transfer.scan_stop", "Stop Scan")))
            {
                stashScanQueue.Clear();
                detectedStashTabs.Clear();
                stashScanStatus = PluginText.T("stashutility.transfer.scan_cancelled", "Scan stopped. Destinations were not changed.");
            }
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(sortRunning || affinityTransferRunning);
            if (ImGui.Button(PluginText.T("stashutility.transfer.scan_reset", "Reset and Scan My Stash")))
            {
                ResetStashScanConfiguration();
                BeginStashScan();
            }
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(PluginText.T("stashutility.transfer.scan_reset_hint", "Clear detected tabs and saved destinations, then scan again. Inventory protections and other settings are kept."));
            if (!string.IsNullOrEmpty(stashScanStatus)) ImGui.TextWrapped(stashScanStatus);
            Settings.TransferDestinations ??= new Dictionary<string, StashDestination>();
            var destinationChoices = GetDestinationChoices();
            bool save = false;
            if (!ImGui.BeginTabBar("##stashBrowserViews")) return;
            if (ImGui.BeginTabItem(PluginText.T("stashutility.ui.stash_view", "My stash") + "###stashView"))
            {
                DrawStashBrowser();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(PluginText.T("stashutility.ui.destinations", "Destinations") + "###stashDestinations"))
            {
                ImGui.TextWrapped(PluginText.T("stashutility.transfer.type_hint", "Rows are item types, not required stash names. Several types can share one destination: shields, foci and quivers can all use Secondary Weapons."));
                if (ImGui.CollapsingHeader(PluginText.T("stashutility.ui.routing_help", "How destinations work")))
                {
                ImGui.TextWrapped(PluginText.T("stashutility.transfer.equipment_hint", "Equipment uses the destinations below. The correct folder and tab are opened before Ctrl+Click. Numbered tabs in the same folder are tried in order when an item cannot be deposited."));
                ImGui.TextWrapped(PluginText.T("stashutility.transfer.affinity_hint", "Specialized items use the game's stash affinities, including newly looted items. No stash scan is needed. Configure the affinities in the game first."));
                ImGui.TextWrapped(PluginText.T("stashutility.transfer.unique_hint", "Unique equipment uses its own numbered tabs, then the overflow tab. Each unique tablet type can use its own selected stash tab. Unset types use the default unique-tablet destination. Fragment destinations also select the matching category and preserve the existing Boss/Map model blocks."));
                ImGui.TextWrapped(PluginText.T("stashutility.transfer.timelost_hint", "Time-Lost jewels require their own destination. Without one, they stay in the inventory."));
                ImGui.TextWrapped(PluginText.T("stashutility.transfer.gem_hint", "Gems use three separate destinations at every level. A scan recognizes uniquely named Skills, Supports and Kalguuran tabs. You can change these destinations or select None to keep these items in your inventory."));
                }
                var categories = new[] { PluginText.T("stashutility.ui.all", "All items"), PluginText.T("stashutility.ui.equipment", "Equipment"),
                    PluginText.T("stashutility.ui.jewels", "Jewels"), PluginText.T("stashutility.ui.other", "Other items") };
                ImGui.Combo(PluginText.T("stashutility.ui.category", "Show"), ref stashDestinationCategory, categories, categories.Length);
                if (destinationChoices.Count == 0)
                    ImGui.TextWrapped(PluginText.T("stashutility.transfer.scan_choices_hint", "Open your stash and click Scan My Stash once to list all tabs. Existing destinations are kept."));
                ImGui.BeginDisabled(affinityTransferRunning || sortRunning);
                if (ImGui.BeginTable("##stashTransferDestinations", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                    new Vector2(0, Math.Max(160f, ImGui.GetContentRegionAvail().Y - 8f))))
                {
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableSetupColumn(PluginText.T("stashutility.transfer.group", "Item Group"), ImGuiTableColumnFlags.WidthStretch, 2f);
                    ImGui.TableSetupColumn(PluginText.T("stashutility.transfer.destination", "Folder / Tab"), ImGuiTableColumnFlags.WidthStretch, 3f);
                    ImGui.TableHeadersRow();

                    DrawDestinationRow("UniqueEquipment", PluginText.T("stashutility.transfer.unique_equipment", "Unique Equipment"));
                    DrawDestinationRow("UniqueEquipmentOverflow", PluginText.T("stashutility.transfer.unique_overflow", "Unique Equipment Overflow Tab"), false);

                    var equipmentGroups = new Dictionary<string, string> { ["Weapons"] = "Weapons",
                        ["Weapons/OneHandWeapons/OneHandSwords"] = "One-Handed Swords", ["Weapons/TwoHandWeapons/TwoHandSwords"] = "Two-Handed Swords",
                        ["Weapons/OneHandWeapons/OneHandAxes"] = "One-Handed Axes", ["Weapons/TwoHandWeapons/TwoHandAxes"] = "Two-Handed Axes",
                        ["Weapons/OneHandWeapons/OneHandMaces"] = "One-Handed Maces", ["Weapons/TwoHandWeapons/TwoHandMaces"] = "Two-Handed Maces",
                        ["Weapons/OneHandWeapons/OneHandSpears"] = "Spears", ["Weapons/OneHandWeapons/Daggers"] = "Daggers",
                        ["Weapons/OneHandWeapons/Claws"] = "Claws", ["Weapons/OneHandWeapons/Flails"] = "Flails",
                        ["Weapons/OneHandWeapons/Wands"] = "Wands", ["Weapons/OneHandWeapons/Sceptres"] = "Sceptres",
                        ["Weapons/TwoHandWeapons/Bows"] = "Bows", ["Weapons/TwoHandWeapons/Crossbows"] = "Crossbows",
                        ["Weapons/TwoHandWeapons/Staves/CasterStaves"] = "Staves", ["Weapons/TwoHandWeapons/Staves/Quarterstaves"] = "Quarterstaves",
                        ["Weapons/TwoHandWeapons/Talismans"] = "Talismans", ["Weapons/TwoHandWeapons/Traps"] = "Trap Tools",
                        ["Weapons/TwoHandWeapon/FishingRods"] = "Fishing Rods",
                        ["Armours/Helmets"] = "Helmets", ["Armours/BodyArmours"] = "Body Armour", ["Armours/Gloves"] = "Gloves", ["Armours/Boots"] = "Boots",
                        ["Armours/Shields"] = "Shields", ["Armours/Focii"] = "Foci", ["Quivers"] = "Quivers",
                        ["Rings"] = "Rings", ["Amulets"] = "Amulets", ["Belts"] = "Belts", ["Charms"] = "Charms",
                        ["Jewels"] = "Jewels (Default)", ["Jewels/Sapphire"] = "Sapphire Jewels",
                        ["Jewels/Emerald"] = "Emerald Jewels", ["Jewels/Ruby"] = "Ruby Jewels",
                        ["Jewels/Diamond"] = "Diamond Jewels",
                        ["Jewels/Sapphire/TimeLost"] = "Time-Lost Sapphire Jewels",
                        ["Jewels/Emerald/TimeLost"] = "Time-Lost Emerald Jewels",
                        ["Jewels/Ruby/TimeLost"] = "Time-Lost Ruby Jewels",
                        ["Jewels/Diamond/TimeLost"] = "Time-Lost Diamond Jewels", ["Flasks"] = "Flasks" };
                    if (Settings.TransferDestinations.ContainsKey("Weapons/TwoHandWeapons/Staves"))
                        equipmentGroups["Weapons/TwoHandWeapons/Staves"] = "Staves (Default)";
                    foreach (var group in equipmentGroups)
                        DrawDestinationRow(group.Key, PluginText.T($"stashutility.transfer.kind.{group.Key}", group.Value));
                    DrawDestinationRow("Gem/Skills", PluginText.T("stashutility.transfer.gem_skills", "Skill and Spirit Gems (cut and uncut)"), false);
                    DrawDestinationRow("Gem/Supports", PluginText.T("stashutility.transfer.gem_supports", "Support Gems (cut and uncut)"), false);
                    DrawDestinationRow("Gem/Kalguuran", PluginText.T("stashutility.transfer.gem_kalguuran", "Kalguuran Gems and Alloys"), false);
                    DrawDestinationRow("UniqueTablets", PluginText.T("stashutility.transfer.unique_tablets", "Unique Tablets (default destination)"), false);
                    foreach (var category in UniqueTabletCategories)
                        DrawDestinationRow("UniqueTablets/" + category,
                            PluginText.T("stashutility.transfer.unique_tablets." + category, "Unique Tablets: " + category), false);
                    var itemGroups = Settings.TransferDestinations.Where(pair =>
                        !UsesGameAffinity(pair.Key) && pair.Key != "UniqueEquipment" && pair.Key != "UniqueEquipmentOverflow" &&
                        !equipmentGroups.ContainsKey(pair.Key) && !IsRoutedGemKind(pair.Key) && pair.Key != "UniqueTablets" && !IsUniqueTabletDestinationKey(pair.Key) && !string.IsNullOrEmpty(pair.Value?.ExampleItemName)).ToList();
                    foreach (var group in itemGroups)
                    {
                        DrawDestinationRow(group.Key, PluginText.F("stashutility.transfer.items_like", "Items like {0}", group.Value.ExampleItemName));
                    }
                    DrawDestinationRow("Equipment", PluginText.T("stashutility.transfer.equipment", "Weapons and Armour"));
                    DrawDestinationRow("Accessories", PluginText.T("stashutility.transfer.accessories", "Jewellery"));
                    if (itemGroups.Count == 0 || !string.IsNullOrEmpty(Settings.TransferDestinations.GetValueOrDefault("Other")?.TabName))
                        DrawDestinationRow("Other", PluginText.T("stashutility.transfer.other", "Other Items"));
                    ImGui.EndTable();
                }
                ImGui.EndDisabled();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();

            if (save)
            {
                transferDestinationChecks.Clear();
                SaveSettings();
            }

            void DrawDestinationRow(string group, string label, bool numbered = true)
            {
                int category = group.StartsWith("Jewels", StringComparison.Ordinal) ? 2 :
                    IsEquipmentKind(group) || group == "Equipment" || group == "Accessories" || group.StartsWith("UniqueEquipment", StringComparison.Ordinal) ? 1 : 3;
                if (stashDestinationCategory != 0 && stashDestinationCategory != category) return;
                if (!Settings.TransferDestinations.TryGetValue(group, out var destination) || destination == null)
                {
                    destination = new StashDestination();
                    Settings.TransferDestinations[group] = destination;
                }
                destination.TabName ??= string.Empty;
                destination.FolderName ??= string.Empty;

                ImGui.PushID(group);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextWrapped(label);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                if (ImGui.BeginCombo("##tabName", destination.Disabled ? PluginText.T("stashutility.transfer.none", "None") : string.IsNullOrEmpty(destination.TabName)
                    ? (group.StartsWith("Weapons/", StringComparison.Ordinal) || IsUniqueTabletDestinationKey(group) ? PluginText.T("stashutility.transfer.inherit", "Use default destination") : PluginText.T("stashutility.transfer.choose_tab", "Choose a detected tab")) :
                    (string.IsNullOrEmpty(destination.FolderName) ? destination.TabName : $"{destination.FolderName} / {destination.TabName}")))
                {
                    if (group == "Weapons" || group.StartsWith("Weapons/", StringComparison.Ordinal) || IsUniqueTabletDestinationKey(group))
                    {
                        if (ImGui.Selectable(PluginText.T("stashutility.transfer.inherit", "Use default destination"), !destination.Disabled && string.IsNullOrWhiteSpace(destination.TabName)))
                        {
                            destination.Disabled = false;
                            destination.IsAutomatic = false;
                            destination.TabName = string.Empty;
                            destination.SetFolders(new List<string>());
                            destination.OverflowTabNames?.Clear();
                            save = true;
                        }
                    }
                    if (ImGui.Selectable(PluginText.T("stashutility.transfer.none", "None"), destination.Disabled))
                    {
                        destination.Disabled = true;
                        destination.IsAutomatic = false;
                        destination.TabName = string.Empty;
                        destination.SetFolders(new List<string>());
                        destination.OverflowTabNames?.Clear();
                        save = true;
                    }
                    int choiceId = 0;
                    foreach (var tab in destinationChoices.OrderByDescending(tab => tab.ItemKinds.ContainsKey(group)))
                    {
                        ImGui.PushID(choiceId++);
                        if (ImGui.Selectable(tab.DisplayName))
                        {
                            SetDetectedDestination(destination, tab, numbered, destinationChoices);
                            destination.IsAutomatic = false;
                            save = true;
                        }
                        ImGui.PopID();
                    }
                    if (destinationChoices.Count == 0)
                        ImGui.TextDisabled(PluginText.T("stashutility.transfer.scan_choices_empty", "No scanned tabs yet."));
                    ImGui.EndCombo();
                }
                if (destination.OverflowTabNames?.Count > 0)
                    ImGui.TextWrapped(PluginText.F("stashutility.transfer.priority_order", "Order: {0}",
                        string.Join(" → ", new[] { destination.TabName }.Concat(destination.OverflowTabNames))));
                if (ImGui.TreeNode(PluginText.T("stashutility.transfer.manual_destination", "Manual entry")))
                {
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputTextWithHint("##manualTab", PluginText.T("stashutility.transfer.tab_name", "Tab name"), ref destination.TabName, 512))
                    {
                        if (!string.IsNullOrWhiteSpace(destination.TabName)) destination.Disabled = false;
                        destination.IsAutomatic = false;
                        destination.OverflowTabNames?.Clear();
                    }
                    save |= ImGui.IsItemDeactivatedAfterEdit();
                    ImGui.TextWrapped(PluginText.T("stashutility.transfer.folder", "Folder Name (Optional)"));
                    ImGui.SetNextItemWidth(-1f);
                    if (ImGui.InputText("##folderName", ref destination.FolderName, 512))
                    {
                        destination.FolderPath?.Clear();
                        destination.OverflowTabNames?.Clear();
                        destination.IsAutomatic = false;
                    }
                    save |= ImGui.IsItemDeactivatedAfterEdit();
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }
        }

        private List<DetectedStashTab> GetDestinationChoices()
        {
            if (detectedStashTabs.Count > 0 && stashScanQueue.Count == 0) return detectedStashTabs;
            var choices = new List<DetectedStashTab>();
            foreach (var saved in Settings.ScannedStashTabs ?? new List<StashDestination>())
                Add(saved, false);
            if (choices.Count == 0)
                foreach (var saved in (Settings.TransferDestinations ?? new Dictionary<string, StashDestination>()).Values)
                    Add(saved, true);
            return choices.GroupBy(tab => JsonConvert.SerializeObject(new { tab.Folders, tab.Name }))
                .Select(group => group.First()).Take(512).ToList();

            void Add(StashDestination saved, bool includeOverflow)
            {
                if (saved == null || saved.Disabled || string.IsNullOrWhiteSpace(saved.TabName)) return;
                var folders = saved.FolderPath?.Count > 0 ? saved.FolderPath :
                    string.IsNullOrEmpty(saved.FolderName) ? new List<string>() : new List<string> { saved.FolderName };
                choices.Add(new DetectedStashTab { Name = saved.TabName, Folders = new List<string>(folders) });
                if (includeOverflow && saved.OverflowTabNames != null)
                    foreach (var name in saved.OverflowTabNames.Where(name => !string.IsNullOrWhiteSpace(name)))
                        choices.Add(new DetectedStashTab { Name = name, Folders = new List<string>(folders) });
            }
        }

        private void DrawStashDiagnostics()
        {
            if (ImGui.Button(PluginText.T("stashutility.transfer.check", "Check Destinations (No Item Movement)")))
            {
                CheckTransferDestinations();
            }
            foreach (var result in transferDestinationChecks.Distinct()) ImGui.TextWrapped(result);

            if (detectedStashTabs.Count > 0 && stashScanQueue.Count == 0 &&
                ImGui.TreeNode(PluginText.T("stashutility.transfer.scan_results", "Detected Tabs and Contents")))
            {
                foreach (var tab in detectedStashTabs)
                {
                    ImGui.TextUnformatted(tab.DisplayName);
                    ImGui.TextWrapped(tab.ItemAddresses.Count == 0
                        ? PluginText.T("stashutility.transfer.scan_no_items", "No readable items: this tab may be empty or not loaded. Open it, then scan again.")
                        : PluginText.F("stashutility.transfer.scan_items", "{0} readable item(s). Examples: {1}", tab.ItemAddresses.Count, string.Join(", ", tab.ItemKinds.Values.Take(4))));
                    ImGui.Separator();
                }
                ImGui.TreePop();
            }

            ImGui.TextWrapped(PluginText.T("stashutility.transfer.dump_hint", "To validate name detection, open your stash and export its UI, first at the root and then inside a folder. The export is saved to config/ui_tree_dump.txt."));
            if (ImGui.Button(PluginText.T("stashutility.transfer.dump", "Export Stash UI Names (Debug)")))
            {
                var gameUi = Core.States.InGameStateObject.GameUi;
                transferExportStatus = PluginText.T("stashutility.transfer.dump_unavailable", "Open the stash in keyboard/mouse mode before exporting.");
                if (gameUi != null && gameUi.LeftPanel.Address != IntPtr.Zero &&
                    !Core.GHSettings.EnableControllerMode &&
                    (this.handleObj != null || InitReflection()))
                {
                    try
                    {
                        if (IsElementVisible(gameUi.LeftPanel.Address))
                        {
                            var stashPanel = ResolvePath(gameUi.LeftPanel.Address, new int[] { 2, 0, 0, 0 });
                            if (stashPanel != IntPtr.Zero && IsElementVisible(stashPanel))
                            {
                                transferExportStatus = DumpUiTreeToFile(stashPanel, namesOnly: true)
                                    ? PluginText.T("stashutility.transfer.dump_saved", "Export saved to config/ui_tree_dump.txt. Copy it before the next export replaces it.")
                                    : PluginText.T("stashutility.transfer.dump_failed", "Export failed. See the GameHelper log.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        transferExportStatus = PluginText.T("stashutility.transfer.dump_failed", "Export failed. See the GameHelper log.");
                        Console.WriteLine($"[StashUtility] Stash name export failed: {ex.Message}");
                    }
                }
            }
            if (!string.IsNullOrEmpty(transferExportStatus)) ImGui.TextWrapped(transferExportStatus);
        }

        private void DrawStashBrowser()
        {
            if (detectedStashTabs.Count == 0 || stashScanQueue.Count > 0)
            {
                ImGui.Spacing();
                ImGui.SeparatorText(PluginText.T("stashutility.ui.empty_title", "Your stash, your layout"));
                ImGui.TextWrapped(PluginText.T("stashutility.ui.empty_hint", "Scan your stash to display its folders and tabs here. Then choose where each item type belongs in Destinations."));
                return;
            }
            var gold = new Vector4(0.56f, 0.70f, 0.91f, 1f);
            var browserHeight = Math.Max(220f, ImGui.GetContentRegionAvail().Y - 10f);
            if (!ImGui.BeginTable("##stashBrowser", 2, ImGuiTableFlags.SizingStretchProp)) return;
            ImGui.TableSetupColumn("##folders", ImGuiTableColumnFlags.WidthFixed, Math.Clamp(ImGui.GetContentRegionAvail().X * 0.28f, 150f, 230f));
            ImGui.TableSetupColumn("##contents", ImGuiTableColumnFlags.WidthStretch, 5f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.BeginChild("##stashFolders", new Vector2(0, browserHeight), ImGuiChildFlags.Borders))
            {
                ImGui.TextColored(gold, PluginText.T("stashutility.transfer.folders", "Folders"));
                ImGui.Separator();
                if (ImGui.Selectable(PluginText.T("stashutility.transfer.root", "Stash Root"), stashBrowserFolders.Count == 0))
                {
                    stashBrowserFolders = new List<string>();
                    stashBrowserTab = IntPtr.Zero;
                }
                DrawFolders(new List<string>());
            }
            ImGui.EndChild();
            ImGui.TableNextColumn();
            if (ImGui.BeginChild("##stashContents", new Vector2(0, browserHeight), ImGuiChildFlags.Borders))
            {
                ImGui.TextWrapped(stashBrowserFolders.Count == 0
                    ? PluginText.T("stashutility.transfer.root", "Stash Root") : string.Join(" / ", stashBrowserFolders));
                var tabs = detectedStashTabs.Where(tab => tab.Folders.SequenceEqual(stashBrowserFolders)).ToList();
                if (!tabs.Any(tab => tab.Page == stashBrowserTab)) stashBrowserTab = tabs.FirstOrDefault()?.Page ?? IntPtr.Zero;
                if (ImGui.BeginChild("##stashTabStrip", new Vector2(0, ImGui.GetFrameHeightWithSpacing() + 18),
                    ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
                {
                    foreach (var tab in tabs)
                    {
                        ImGui.PushID(tab.Page.ToString());
                        ImGui.PushStyleColor(ImGuiCol.Button, tab.Page == stashBrowserTab
                            ? new Vector4(0.48f, 0.32f, 0.10f, 1f) : new Vector4(0.22f, 0.18f, 0.12f, 1f));
                        if (ImGui.Button(tab.Name)) stashBrowserTab = tab.Page;
                        ImGui.PopStyleColor();
                        ImGui.PopID();
                        ImGui.SameLine();
                    }
                }
                ImGui.EndChild();
                var selected = tabs.FirstOrDefault(tab => tab.Page == stashBrowserTab);
                if (selected == null)
                    ImGui.TextWrapped(PluginText.T("stashutility.transfer.select_subfolder", "Select a subfolder to see its tabs."));
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, gold);
                    ImGui.TextWrapped(PluginText.F("stashutility.transfer.tab_count", "{0} — {1} readable item(s)", selected.Name, selected.Items.Count));
                    ImGui.PopStyleColor();
                    var chain = GetDestinationTabs(selected);
                    if (chain.Count > 1)
                        ImGui.TextWrapped(PluginText.F("stashutility.transfer.priority_order", "Order: {0}", string.Join(" → ", chain.Select(tab => tab.Name))));
                    ImGui.TextWrapped(PluginText.T("stashutility.transfer.preview_hint", "Simplified preview of the last scan: names and rarity, without game icons or exact slot positions."));
                    ImGui.Separator();
                    if (selected.Items.Count == 0)
                        ImGui.TextWrapped(PluginText.T("stashutility.transfer.scan_no_items", "No readable items: this tab may be empty or not loaded. Open it, then scan again."));
                    else
                    {
                        int columns = Math.Clamp((int)(ImGui.GetContentRegionAvail().X / 155f), 1, 8);
                        if (ImGui.BeginTable("##stashItems", columns, ImGuiTableFlags.SizingStretchSame))
                        {
                            for (int i = 0; i < selected.Items.Count; i++)
                            {
                                var item = selected.Items[i];
                                var color = item.rarity switch
                                {
                                    Rarity.Unique => new Vector4(0.9f, 0.5f, 0.2f, 1f),
                                    Rarity.Rare => new Vector4(0.9f, 0.8f, 0.3f, 1f),
                                    Rarity.Magic => new Vector4(0.45f, 0.55f, 1f, 1f),
                                    _ => new Vector4(0.75f, 0.75f, 0.75f, 1f)
                                };
                                ImGui.TableNextColumn();
                                ImGui.PushStyleColor(ImGuiCol.Border, color);
                                if (ImGui.BeginChild($"##stashItem{i}", new Vector2(0, 112), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar))
                                {
                                    ImGui.TextWrapped(item.name);
                                    if (ImGui.IsWindowHovered()) ImGui.SetTooltip(item.name);
                                }
                                ImGui.EndChild();
                                ImGui.PopStyleColor();
                            }
                            ImGui.EndTable();
                        }
                    }
                }
            }
            ImGui.EndChild();
            ImGui.EndTable();

            void DrawFolders(List<string> parent)
            {
                var names = detectedStashTabs.Where(tab => tab.Folders.Count > parent.Count &&
                    tab.Folders.Take(parent.Count).SequenceEqual(parent))
                    .Select(tab => tab.Folders[parent.Count]).Distinct();
                foreach (var name in names)
                {
                    var path = new List<string>(parent) { name };
                    bool hasChildren = detectedStashTabs.Any(tab => tab.Folders.Count > path.Count &&
                        tab.Folders.Take(path.Count).SequenceEqual(path));
                    var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
                    if (path.SequenceEqual(stashBrowserFolders)) flags |= ImGuiTreeNodeFlags.Selected;
                    if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
                    ImGui.PushID(JsonConvert.SerializeObject(path));
                    bool open = ImGui.TreeNodeEx(name, flags);
                    if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                    {
                        stashBrowserFolders = path;
                        stashBrowserTab = IntPtr.Zero;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
                    if (open && hasChildren)
                    {
                        DrawFolders(path);
                        ImGui.TreePop();
                    }
                    ImGui.PopID();
                }
            }
        }

        private void CheckTransferDestinations()
        {
            transferDestinationChecks.Clear();
            try
            {
                var gameUi = Core.States.InGameStateObject.GameUi;
                if (gameUi == null || gameUi.LeftPanel.Address == IntPtr.Zero || Core.GHSettings.EnableControllerMode ||
                    (this.handleObj == null && !InitReflection()) || !IsElementVisible(gameUi.LeftPanel.Address))
                {
                    transferDestinationChecks.Add(PluginText.T("stashutility.transfer.dump_unavailable", "Open the stash in keyboard/mouse mode before exporting."));
                    return;
                }

                var rootHeader = ResolvePath(gameUi.LeftPanel.Address, new int[] { 2, 0, 0, 0, 1 });
                if (rootHeader == IntPtr.Zero || !IsElementVisible(rootHeader) ||
                    !TryReadUiString(rootHeader, 0x98, out var rootId) || rootId != "top_level_tabs")
                {
                    transferDestinationChecks.Add(PluginText.T("stashutility.transfer.layout_unavailable", "Stash tab names are unavailable. Open the stash and try again."));
                    return;
                }

                bool hasConfiguredDestination = false;
                foreach (var pair in Settings.TransferDestinations)
                {
                    if (UsesGameAffinity(pair.Key)) continue;
                    var destination = pair.Value;
                    if (destination == null || string.IsNullOrWhiteSpace(destination.TabName)) continue;
                    hasConfiguredDestination = true;
                    var folder = destination.FolderName ?? string.Empty;
                    var displayName = string.IsNullOrEmpty(folder) ? destination.TabName : $"{folder} / {destination.TabName}";
                    IntPtr header = rootHeader;

                    var folders = destination.FolderPath?.Count > 0 ? destination.FolderPath :
                        (string.IsNullOrEmpty(folder) ? new List<string>() : new List<string> { folder });
                    bool folderFound = true;
                    foreach (var folderPart in folders)
                    {
                        if (!TryResolveNamedStashPage(header, folderPart, out var folderPage))
                        {
                            transferDestinationChecks.Add(PluginText.F("stashutility.transfer.folder_not_found", "Folder name not found or ambiguous: {0}", folderPart));
                            folderFound = false;
                            break;
                        }
                        header = ResolvePath(folderPage, new int[] { 0, 1 });
                    }
                    if (!folderFound) continue;

                    foreach (var tabName in new[] { destination.TabName }.Concat(destination.OverflowTabNames ?? new List<string>()))
                    {
                        displayName = string.IsNullOrEmpty(folder) ? tabName : $"{folder} / {tabName}";
                        bool found = TryResolveNamedStashPage(header, tabName, out _);
                        transferDestinationChecks.Add(found
                            ? PluginText.F("stashutility.transfer.destination_found", "Destination found: {0}", displayName)
                            : PluginText.F("stashutility.transfer.destination_not_found", "Destination name not found or ambiguous: {0}", displayName));
                    }
                }

                if (!hasConfiguredDestination)
                {
                    transferDestinationChecks.Add(PluginText.T("stashutility.transfer.enter_destination", "Enter a tab name in the table before checking."));
                }
            }
            catch (Exception ex)
            {
                transferDestinationChecks.Clear();
                transferDestinationChecks.Add(PluginText.T("stashutility.transfer.layout_unavailable", "Stash tab names are unavailable. Open the stash and try again."));
                Console.WriteLine($"[StashUtility] Destination check failed: {ex.Message}");
            }
        }

        private void DrawInventoryTransferOptions()
        {
            ImGui.BeginDisabled(affinityTransferRunning || sortRunning);
            if (ImGui.Checkbox(PluginText.T("stashutility.transfer.sort_gem_levels", "Sort gems by level (highest first)"), ref Settings.SortGemsByLevel)) SaveSettings();
            if (ImGui.TreeNodeEx(PluginText.T("stashutility.transfer.protected_cells", "Protected Inventory Cells"), ImGuiTreeNodeFlags.DefaultOpen))
            {
                Settings.ProtectedInventoryCells ??= new HashSet<int>();
                bool changed = Settings.ProtectedInventoryCells.RemoveWhere(cell => cell < 0 || cell >= InventoryColumns * InventoryRows) > 0;
                ImGui.TextWrapped(PluginText.T("stashutility.ui.protected_hint", "Items touching a cell marked X stay in your inventory."));
                ImGuiHelper.ToolTip(PluginText.T("stashutility.transfer.protected_hint", "Click cells to protect them. Red X = keep. The grid has the same orientation as your character inventory. Any item touching a protected cell stays in the inventory."));
                if (ImGui.Button(PluginText.T("stashutility.transfer.protect_all", "Protect All")))
                {
                    Settings.ProtectedInventoryCells.UnionWith(Enumerable.Range(0, InventoryColumns * InventoryRows));
                    changed = true;
                }
                ImGui.SameLine();
                if (ImGui.Button(PluginText.T("stashutility.transfer.clear_protection", "Clear Protection")))
                {
                    Settings.ProtectedInventoryCells.Clear();
                    changed = true;
                }
                float cellSize = Math.Clamp((ImGui.GetContentRegionAvail().X - (InventoryColumns - 1) * 3) / InventoryColumns, 16, 36);
                for (int row = 0; row < InventoryRows; row++)
                {
                    for (int col = 0; col < InventoryColumns; col++)
                    {
                        int cell = row * InventoryColumns + col;
                        bool kept = Settings.ProtectedInventoryCells.Contains(cell);
                        if (col > 0) ImGui.SameLine(0, 3);
                        ImGui.PushID(cell);
                        ImGui.PushStyleColor(ImGuiCol.Button, kept ? new Vector4(0.65f, 0.18f, 0.18f, 1) : new Vector4(0.2f, 0.22f, 0.27f, 1));
                        if (ImGui.Button(kept ? "X" : "##cell", new Vector2(cellSize, cellSize)))
                        {
                            if (kept) Settings.ProtectedInventoryCells.Remove(cell); else Settings.ProtectedInventoryCells.Add(cell);
                            changed = true;
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(PluginText.F("stashutility.transfer.cell_position", "Row {0}, column {1}", row + 1, col + 1));
                        ImGui.PopStyleColor();
                        ImGui.PopID();
                    }
                }
                if (changed) SaveSettings();
                ImGui.TreePop();
            }
            ImGui.SeparatorText(PluginText.T("stashutility.ui.sorting", "Sorting and speed"));
            var sortModes = new[] { PluginText.T("stashutility.sort.by_type", "Type, rarity and tier"), PluginText.T("stashutility.sort.compact", "Free up space") };
            if (ImGui.BeginCombo(PluginText.T("stashutility.sort.mode", "Sorting Mode"), sortModes[Settings.SortByType ? 0 : 1]))
            {
                for (int i = 0; i < sortModes.Length; i++)
                    if (ImGui.Selectable(sortModes[i], Settings.SortByType == (i == 0))) { Settings.SortByType = i == 0; SaveSettings(); }
                ImGui.EndCombo();
            }
            var speeds = new[] { PluginText.T("stashutility.transfer.speed_very_fast", "Very Fast"), PluginText.T("stashutility.transfer.speed_fast", "Fast"),
                PluginText.T("stashutility.transfer.speed_normal", "Standard"), PluginText.T("stashutility.transfer.speed_slow", "Slow") };
            int speed = Settings.AffinityTransferDelayMs <= 20 ? 0 : Settings.AffinityTransferDelayMs <= 60 ? 1 : Settings.AffinityTransferDelayMs <= 100 ? 2 : 3;
            if (ImGui.BeginCombo(PluginText.T("stashutility.transfer.speed", "Transfer Speed"), speeds[speed]))
            {
                for (int i = 0; i < speeds.Length; i++)
                    if (ImGui.Selectable(speeds[i], i == speed))
                    {
                        Settings.AffinityTransferDelayMs = new[] { 20, 60, 100, 150 }[i];
                        SaveSettings();
                    }
                ImGui.EndCombo();
            }

            if (ImGui.CollapsingHeader(PluginText.T("stashutility.ui.inventory_buttons", "Inventory toolbar")))
            {
            ImGui.TextWrapped(PluginText.T("stashutility.transfer.inventory_button_hint", "Use the Transfer to Stash button below your character inventory. Keep the stash open."));
            if (ImGui.Checkbox(PluginText.T("stashutility.transfer.show_inventory_button", "Show Transfer Button in Inventory"), ref Settings.ShowInventoryTransferButton)) SaveSettings();

            if (ImGui.TreeNode(PluginText.T("stashutility.ui.button_position", "Position and lock")))
            {
            if (ImGui.Checkbox(PluginText.T("stashutility.transfer.button_locked", "Lock Transfer Button Position"), ref Settings.InventoryTransferButtonLocked)) SaveSettings();
            if (ImGui.InputFloat2(PluginText.T("stashutility.transfer.button_offset", "Button Position Offset (X / Y)"), ref Settings.InventoryTransferButtonOffset)) SaveSettings();
            if (ImGui.Button(PluginText.T("stashutility.transfer.button_reset", "Reset Button Position")))
            {
                Settings.InventoryTransferButtonOffset = Vector2.Zero;
                Settings.InventoryTransferButtonLocked = true;
                SaveSettings();
            }
            ImGui.TextWrapped(PluginText.T("stashutility.transfer.button_move_hint", "Unlock the position, then drag the move icon next to the transfer icon. Click the padlock to lock it again. X/Y offsets are in pixels from its default position."));
            ImGui.TreePop();
            }


            if (ImGui.Checkbox(PluginText.T("stashutility.sort.show_inventory", "Show Inventory Sort Button"), ref Settings.ShowInventorySortButton)) SaveSettings();
            if (ImGui.Checkbox(PluginText.T("stashutility.sort.show_stash", "Show Stash Sort Button"), ref Settings.ShowStashSortButton)) SaveSettings();
            if (ImGui.Button(PluginText.T("stashutility.sort.reset_position", "Reset stash button position")))
            {
                Settings.StashToolbarOffset = Vector2.Zero;
                SaveSettings();
            }
            }
            ImGui.EndDisabled();
        }

        private IntPtr FindPlayerInventoryAddress(InventoryName name)
        {
            var inventories = ReadPlayerInventoryList();
            for (int i = 0; i < inventories.TotalElements(0x18); i++)
            {
                var entry = inventories.First + i * 0x18;
                if (ReadMemory<int>(entry) == (int)name) return ReadMemory<IntPtr>(entry + 0x08);
            }
            return IntPtr.Zero;
        }

        private StdVector ReadPlayerInventoryList()
        {
            var serverData = Core.States.InGameStateObject.CurrentAreaInstance.ServerDataObject;
            if (serverData == null || serverData.Address == IntPtr.Zero)
                throw new InvalidOperationException("Protected inventory cells could not be read.");
            // Read the current inventory directly, as LootTracker does, rather than relying on
            // ServerData's cached dictionary or inventory layouts from the installed GameOffsets DLL.
            var players = ReadMemory<StdVector>(serverData.Address + 0x48);
            if (players.First == IntPtr.Zero || players.TotalElements(IntPtr.Size) < 1 || players.TotalElements(IntPtr.Size) > 16)
                throw new InvalidOperationException("Player inventory data is unavailable.");
            var player = ReadMemory<IntPtr>(players.First);
            if (player == IntPtr.Zero) throw new InvalidOperationException("Player inventory data is unavailable.");
            var inventories = ReadMemory<StdVector>(player + 0x320);
            long count = inventories.TotalElements(0x18);
            if (inventories.First == IntPtr.Zero || count < 1 || count > 4096)
                throw new InvalidOperationException("Player inventory list is unavailable.");
            return inventories;
        }

        private (HashSet<IntPtr> present, HashSet<IntPtr> protectedItems) ReadProtectedInventoryItems()
        {
            var address = FindPlayerInventoryAddress(InventoryName.MainInventory1);
            if (address == IntPtr.Zero) throw new InvalidOperationException("Character inventory is unavailable.");
            var itemList = ReadMemory<StdVector>(address + 0x170);
            if (ReadMemory<int>(address + 0x150) != InventoryColumns || ReadMemory<int>(address + 0x154) != InventoryRows ||
                itemList.First == IntPtr.Zero || itemList.TotalElements(IntPtr.Size) != InventoryColumns * InventoryRows)
                throw new InvalidOperationException("Protected inventory grid does not match the character inventory.");

            var mapping = ReadStdVector<IntPtr>(itemList);
            if (mapping.Length != InventoryColumns * InventoryRows)
                throw new InvalidOperationException("Protected inventory grid could not be read completely.");
            var seen = new Dictionary<IntPtr, IntPtr>();
            var present = new HashSet<IntPtr>();
            var protectedItems = new HashSet<IntPtr>();
            for (int cell = 0; cell < mapping.Length; cell++)
            {
                var inventoryItem = mapping[cell];
                if (inventoryItem == IntPtr.Zero) continue;
                if (!seen.TryGetValue(inventoryItem, out var item))
                {
                    if (!TryReadMemory<IntPtr>(inventoryItem, out item) || item == IntPtr.Zero)
                        throw new InvalidOperationException("Inventory changed while reading protected cells.");
                    seen.Add(inventoryItem, item);
                    present.Add(item);
                }
                // Large items repeat the same pointer in every occupied cell.
                if (affinityProtectedCells.Contains(cell)) protectedItems.Add(item);
            }
            return (present, protectedItems);
        }

        private List<SortItem> ReadSortItems(IntPtr inventory, int columns, int rows)
        {
            if (inventory == IntPtr.Zero || ReadMemory<int>(inventory + 0x150) != columns || ReadMemory<int>(inventory + 0x154) != rows)
                throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
            var vector = ReadMemory<StdVector>(inventory + 0x170);
            if (vector.First == IntPtr.Zero || vector.TotalElements(IntPtr.Size) != columns * rows)
                throw new InvalidOperationException(PluginText.T("stashutility.sort.unavailable", "The grid could not be read safely."));
            var mapping = ReadStdVector<IntPtr>(vector);
            var pointers = new Dictionary<IntPtr, IntPtr>();
            var cells = new Dictionary<IntPtr, List<int>>();
            for (int i = 0; i < mapping.Length; i++)
            {
                if (mapping[i] == IntPtr.Zero) continue;
                if (!pointers.TryGetValue(mapping[i], out var item))
                {
                    item = ReadMemory<IntPtr>(mapping[i]);
                    if (item == IntPtr.Zero || !PluginUiElementReflection.TryValidateItemAddress(item, out _, out _))
                        throw new InvalidOperationException(PluginText.T("stashutility.sort.unavailable", "The grid could not be read safely."));
                    pointers.Add(mapping[i], item);
                }
                if (!cells.TryGetValue(item, out var occupied)) cells[item] = occupied = new List<int>();
                occupied.Add(i);
            }
            var result = new List<SortItem>();
            foreach (var entry in cells)
            {
                int x = entry.Value.Min(cell => cell % columns), y = entry.Value.Min(cell => cell / columns);
                int width = entry.Value.Max(cell => cell % columns) - x + 1, height = entry.Value.Max(cell => cell / columns) - y + 1;
                if (entry.Value.Count != width * height)
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.unavailable", "The grid could not be read safely."));
                result.Add(new SortItem { Address = entry.Key, X = x, Y = y, Width = width, Height = height });
            }
            return result;
        }

        private IntPtr ReadSortCursorItem()
        {
            var inventory = FindPlayerInventoryAddress(InventoryName.Cursor1);
            if (inventory == IntPtr.Zero)
                throw new InvalidOperationException(PluginText.T("stashutility.sort.cursor_unavailable", "The item held by the cursor could not be checked."));
            var vector = ReadMemory<StdVector>(inventory + 0x170);
            long count = vector.TotalElements(IntPtr.Size);
            if (count < 0 || count > 60 || (count > 0 && vector.First == IntPtr.Zero))
                throw new InvalidOperationException(PluginText.T("stashutility.sort.cursor_unavailable", "The item held by the cursor could not be checked."));
            if (count == 0) return IntPtr.Zero;
            var result = IntPtr.Zero;
            foreach (var pointer in ReadStdVector<IntPtr>(vector).Where(pointer => pointer != IntPtr.Zero).Distinct())
            {
                var item = ReadMemory<IntPtr>(pointer);
                if (item == IntPtr.Zero || (result != IntPtr.Zero && result != item))
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.cursor_unavailable", "The item held by the cursor could not be checked."));
                result = item;
            }
            return result;
        }

        private static IEnumerable<int> SortCells(SortItem item, int x, int y, int columns)
        {
            for (int row = y; row < y + item.Height; row++)
                for (int col = x; col < x + item.Width; col++) yield return row * columns + col;
        }

        private static Dictionary<IntPtr, (int x, int y)> BuildSortTargets(List<SortItem> items, int columns, int rows,
            HashSet<int> protectedCells, bool byType)
        {
            var occupied = new HashSet<int>(protectedCells);
            var targets = new Dictionary<IntPtr, (int x, int y)>();
            foreach (var item in items.Where(item => SortCells(item, item.X, item.Y, columns).Any(protectedCells.Contains)))
            {
                targets.Add(item.Address, (item.X, item.Y));
                occupied.UnionWith(SortCells(item, item.X, item.Y, columns));
            }
            var movable = items.Where(item => !targets.ContainsKey(item.Address));
            var ordered = byType
                ? movable.OrderBy(item => item.IsTablet ? "TowerAugment" : item.Kind, StringComparer.Ordinal).ThenBy(item => item.TabletSortScore).ThenByDescending(item => item.Rarity)
                    .ThenByDescending(item => item.Tier).ThenBy(item => item.Path, StringComparer.Ordinal)
                    .ThenByDescending(item => item.Width * item.Height).ThenBy(item => item.Y).ThenBy(item => item.X)
                : movable.OrderByDescending(item => item.Width * item.Height).ThenByDescending(item => item.Height)
                    .ThenBy(item => item.IsTablet ? "TowerAugment" : item.Kind, StringComparer.Ordinal).ThenBy(item => item.TabletSortScore).ThenBy(item => item.Y).ThenBy(item => item.X);
            foreach (var item in ordered)
            {
                bool placed = false;
                for (int y = 0; y <= rows - item.Height && !placed; y++)
                    for (int x = 0; x <= columns - item.Width && !placed; x++)
                    {
                        var cells = SortCells(item, x, y, columns).ToArray();
                        if (cells.Any(occupied.Contains)) continue;
                        targets.Add(item.Address, (x, y));
                        occupied.UnionWith(cells);
                        placed = true;
                    }
                if (!placed) return null;
            }
            return targets;
        }

        private static SortMove NextSortMove(List<SortItem> items, Dictionary<IntPtr, (int x, int y)> targets,
            int columns, int rows, HashSet<int> protectedCells)
        {
            var misplaced = items.Where(item => targets[item.Address] != (item.X, item.Y)).ToList();
            bool Free(SortItem item, int x, int y) => !SortCells(item, x, y, columns).Any(protectedCells.Contains) &&
                !items.Where(other => other.Address != item.Address).Any(other =>
                    x < other.X + other.Width && x + item.Width > other.X && y < other.Y + other.Height && y + item.Height > other.Y);
            foreach (var item in misplaced)
            {
                var target = targets[item.Address];
                if (Free(item, target.x, target.y)) return new SortMove { Item = item, X = target.x, Y = target.y };
                var other = IsEquipmentKind(item.Kind) ? misplaced.FirstOrDefault(other => other.Address != item.Address &&
                    IsEquipmentKind(other.Kind) && other.X == target.x && other.Y == target.y &&
                    other.Width == item.Width && other.Height == item.Height && other.Path != item.Path) : null;
                // Only swap equipment. A currency dropped onto an occupied equipment slot could
                // craft that item, and equal base types could merge into a stack.
                if (other != null) return new SortMove { Item = item, Swap = other, X = target.x, Y = target.y };
            }
            // Break a blocked cycle by parking a blocking item in a genuinely empty rectangle.
            foreach (var wanted in misplaced)
            {
                var target = targets[wanted.Address];
                foreach (var blocker in misplaced.Where(other => other.Address != wanted.Address &&
                    target.x < other.X + other.Width && target.x + wanted.Width > other.X &&
                    target.y < other.Y + other.Height && target.y + wanted.Height > other.Y))
                    for (int y = rows - blocker.Height; y >= 0; y--)
                        for (int x = columns - blocker.Width; x >= 0; x--)
                        {
                            if ((x, y) == (blocker.X, blocker.Y) || !Free(blocker, x, y)) continue;
                            if (x < target.x + wanted.Width && x + blocker.Width > target.x &&
                                y < target.y + wanted.Height && y + blocker.Height > target.y) continue;
                            return new SortMove { Item = blocker, X = x, Y = y };
                        }
            }
            return null;
        }

        private IntPtr FindSortSurface(bool stash)
        {
            var ui = Core.States.InGameStateObject.GameUi;
            if (ui == null || Core.GHSettings.EnableControllerMode) return IntPtr.Zero;
            if (!stash)
            {
                if (!IsElementVisible(ui.RightPanel.Address)) return IntPtr.Zero;
                var grid = ResolvePath(ui.RightPanel.Address, new int[] { 5, 36 });
                return IsElementVisible(grid) ? grid : IntPtr.Zero;
            }
            if (!IsElementVisible(ui.LeftPanel.Address)) return IntPtr.Zero;
            var header = ResolvePath(ui.LeftPanel.Address, new int[] { 2, 0, 0, 0, 1 });
            return FindSelectedStashPage(header);
        }

        private IntPtr FindSelectedStashPage(IntPtr header)
        {
            for (int depth = 0; depth <= 8; depth++)
            {
                if (!TryReadStashEntries(header, out var entries) || IsElementVisible(ResolvePath(header, new int[] { 4 }))) return IntPtr.Zero;
                var active = entries.Where(entry => IsTabVisible(entry.page) && IsElementVisible(entry.page)).ToList();
                if (active.Count != 1) return IntPtr.Zero;
                var nested = ResolvePath(active[0].page, new int[] { 0, 1 });
                if (TryReadStashEntries(nested, out _)) { header = nested; continue; }
                return active[0].page;
            }
            return IntPtr.Zero;
        }

        private bool TrySortGeometry(IntPtr surface, List<SortItem> items, bool stash, int columns, int rows,
            out IntPtr grid, out Vector2 origin, out Vector2 cellSize)
        {
            grid = IntPtr.Zero; origin = cellSize = Vector2.Zero;
            if (surface == IntPtr.Zero) return false;
            var candidates = stash ? new[] { ResolvePath(surface, new int[] { 0, 0 }), ResolvePath(surface, new int[] { 0 }), surface } : new[] { surface };
            foreach (var candidate in candidates.Where(candidate => candidate != IntPtr.Zero && IsElementVisible(candidate)).Distinct())
            {
                if (items.Count == 0 && sortRunning && candidate == sortGrid)
                { grid = candidate; origin = sortOrigin; cellSize = sortCellSize; return true; }
                var vector = ReadMemory<UiElementBaseOffset>(candidate).ChildrensPtr;
                if (vector.TotalElements(IntPtr.Size) < 1 || vector.TotalElements(IntPtr.Size) > 700) continue;
                var matched = new HashSet<IntPtr>();
                var itemLookup = items.ToDictionary(item => item.Address);
                Vector2 referenceOrigin = Vector2.Zero, referenceCell = Vector2.Zero;
                bool valid = true;
                foreach (var slot in ReadStdVector<IntPtr>(vector))
                {
                    if (slot == IntPtr.Zero || !IsElementVisible(slot)) continue;
                    var address = GetItemAddressFromElement(slot);
                    if (sortRunning && sortAddresses.TryGetValue(address, out var plannedAddress)) address = plannedAddress;
                    if (!itemLookup.TryGetValue(address, out var item) || !matched.Add(address)) continue;
                    if (!PluginUiElementReflection.TryGetAbsoluteRect(slot, out var position, out var size)) { valid = false; break; }
                    var cell = new Vector2(size.X / item.Width, size.Y / item.Height);
                    var start = position - new Vector2(item.X * cell.X, item.Y * cell.Y);
                    if (cell.X < 10 || cell.Y < 10 || cell.X > 100 || cell.Y > 100 || Math.Abs(cell.X - cell.Y) > 3)
                    { valid = false; break; }
                    if (matched.Count == 1) { referenceOrigin = start; referenceCell = cell; }
                    else if (Vector2.Distance(start, referenceOrigin) > 4 || Vector2.Distance(cell, referenceCell) > 3)
                    { valid = false; break; }
                }
                var window = Core.Process.WindowArea;
                if (!valid || matched.Count != items.Count || matched.Count == 0 || referenceOrigin.X < 0 || referenceOrigin.Y < 0 ||
                    referenceOrigin.X + columns * referenceCell.X > window.Width + 3 || referenceOrigin.Y + rows * referenceCell.Y > window.Height + 3) continue;
                grid = candidate; origin = referenceOrigin; cellSize = referenceCell; return true;
            }
            return false;
        }

        private void BeginGridSort(bool stash, bool gemLevelsOnly = false)
        {
            if (sortRunning || affinityTransferRunning || stashScanQueue.Count > 0) return;
            sortFromGemTransfer = gemLevelsOnly;
            try
            {
                if (Core.States.GameCurrentState != GameStateTypes.InGameState || Core.GHSettings.EnableControllerMode ||
                    (handleObj == null && !InitReflection())) return;
                ReleaseAffinityControl();
                if (affinityCtrlHeld || affinityShiftHeld) throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_release_failed", "Ctrl could not be released. Press and release Ctrl before trying again."));
                sortInventory = FindPlayerInventoryAddress(stash ? InventoryName.StashInventoryId : InventoryName.MainInventory1);
                if (sortInventory == IntPtr.Zero) throw new InvalidOperationException(PluginText.T("stashutility.sort.unavailable", "The grid could not be read safely."));
                sortColumns = ReadMemory<int>(sortInventory + 0x150); sortRows = ReadMemory<int>(sortInventory + 0x154);
                if (stash ? !((sortColumns, sortRows) == (12, 12) || (sortColumns, sortRows) == (24, 24)) : (sortColumns, sortRows) != (12, 5))
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.specialized", "Sorting supports normal and quad stash tabs. Specialized slots are arranged by the game."));
                if (ReadSortCursorItem() != IntPtr.Zero)
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.cursor_busy", "Put down the item held by the cursor before sorting."));
                sortItems = ReadSortItems(sortInventory, sortColumns, sortRows);
                if (sortItems.Count == 0) { SetSortFeedback(PluginText.T("stashutility.sort.empty", "The grid is empty.")); return; }
                var surface = FindSortSurface(stash);
                if (stash && (!IsActiveTab(surface, out var tabType) || !tabType.StartsWith("Normal/Quad", StringComparison.Ordinal)))
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.specialized", "Sorting supports normal and quad stash tabs. Specialized slots are arranged by the game."));
                if (!TrySortGeometry(surface, sortItems, stash, sortColumns, sortRows, out sortGrid, out sortOrigin, out sortCellSize))
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.unavailable", "The grid could not be read safely."));
                foreach (var item in sortItems)
                {
                    var fresh = ReadFreshItem(item.Address);
                    if (fresh == null || !fresh.IsValid || string.IsNullOrEmpty(fresh.Path))
                        throw new InvalidOperationException(PluginText.T("stashutility.sort.unavailable", "The grid could not be read safely."));
                    item.Path = fresh.Path; item.Kind = GetItemKind(fresh.Path);
                    item.Rarity = fresh.TryGetComponent<Mods>(out var mods) ? mods.Rarity : Rarity.Normal;
                    item.Tier = GetTabPriority(fresh.Path) ?? 0;
                    item.IsTablet = IsTabletItem(fresh.Path);
                    if (item.IsTablet)
                    {
                        fresh.TryGetComponent<Base>(out var tabletBase);
                        var type = GetTabletProfileType(tabletBase?.BaseItemName, fresh.Path);
                        var profile = type != null && Settings.TabletProfiles?.TryGetValue(type, out var foundProfile) == true ? foundProfile : null;
                        item.TabletSortScore = ClassifyTabletMods(mods, profile, type).SortScore;
                    }
                    item.Identity = SortItemIdentity(fresh);
                }
                if (gemLevelsOnly)
                {
                    // Flux levels are explicit metadata IDs. Never infer learned gem levels from an unrelated number.
                    if (sortItems.Any(item => item.Kind != "Gem/Kalguuran" || GetGemLevelFromPath(item.Path) == 0))
                        throw new InvalidOperationException(PluginText.T("stashutility.sort.gem_levels_unavailable", "Automatic level sorting requires a readable level for every item in the Kalguuran tab."));
                    foreach (var item in sortItems) item.Tier = GetGemLevelFromPath(item.Path);
                }
                sortAddresses.Clear();
                foreach (var item in sortItems) sortAddresses.Add(item.Address, item.Address);
                sortProtectedCells.Clear();
                if (!stash) sortProtectedCells.UnionWith((Settings.ProtectedInventoryCells ?? new HashSet<int>()).Where(cell => cell >= 0 && cell < 60));
                sortTargets = BuildSortTargets(sortItems, sortColumns, sortRows, sortProtectedCells, gemLevelsOnly || Settings.SortByType);
                if (sortTargets == null) throw new InvalidOperationException(PluginText.T("stashutility.sort.needs_space", "No safe arrangement could be found. Free some space or choose the compact mode."));
                sortStash = stash; sortWindow = Core.Process.WindowArea; sortMoves = sortPhase = 0;
                sortMove = null; sortLayouts.Clear(); sortPrepared = sortMousePositioned = false;
                sortDeadline = DateTime.UtcNow.AddSeconds(30); sortNextAction = DateTime.UtcNow.AddMilliseconds(200);
                sortRunning = true;
                SetSortFeedback(PluginText.T("stashutility.sort.preparing", "Preparing sorting. Keep the grid open and leave the mouse still. Escape stops sorting."));
            }
            catch (Exception ex) { SetSortFeedback(ex.InnerException?.Message ?? ex.Message); }
        }

        private void SetSortFeedback(string message)
        {
            if (sortFromGemTransfer && !string.IsNullOrEmpty(affinityRouteIssue)) message += "\n" + affinityRouteIssue;
            affinityCompletionSummary = string.Empty;
            sortStatus = affinityTransferStatus = message;
            affinityFeedbackUntil = DateTime.UtcNow.AddSeconds(10);
        }

        private void StopGridSort(string reason = null)
        {
            if (!sortRunning) return;
            sortRunning = false;
            reason ??= PluginText.T("stashutility.sort.stopped", "Sorting stopped.");
            try
            {
                if (ReadSortCursorItem() != IntPtr.Zero)
                    reason += "\n" + PluginText.T("stashutility.sort.held_item", "An item is held by the cursor. Put it back in the grid.");
            }
            catch (Exception) { /* No input is sent when the cursor cannot be verified. */ }
            SetSortFeedback(reason);
        }

        private static bool SortGridEquals(List<SortItem> actual, IEnumerable<SortItem> expected)
        {
            var check = expected.ToList();
            return actual.Count == check.Count && check.All(item => actual.Any(other => other.Address == item.Address &&
                (other.X, other.Y, other.Width, other.Height) == (item.X, item.Y, item.Width, item.Height)));
        }

        private static string SortItemIdentity(Item item)
        {
            if (item == null || !item.IsValid || string.IsNullOrEmpty(item.Path)) return null;
            item.TryGetComponent<Mods>(out var mods);
            item.TryGetComponent<Base>(out var itemBase);
            item.TryGetComponent<Stack>(out var stack);
            return JsonConvert.SerializeObject(new { item.Path, Base = itemBase?.InternalName,
                Count = stack?.Count, Rarity = mods?.Rarity, mods?.ImplicitMods, mods?.ExplicitMods,
                mods?.EnchantMods, mods?.HellscapeMods });
        }

        private static IntPtr NormalizeSortAddresses(List<SortItem> actual, SortMove move, int phase,
            IntPtr cursor, Dictionary<IntPtr, IntPtr> addresses, Func<IntPtr, string> readIdentity)
        {
            // An item can be recreated when it enters/leaves the cursor inventory. Keep
            // the plan's identity, but bind a new native address only to the expected held
            // item or to its verified destination, with the same base, stack and modifiers.
            bool Bind(IntPtr address, SortItem expected)
            {
                if (address == IntPtr.Zero || expected == null) return false;
                if (addresses.TryGetValue(address, out var known)) return known == expected.Address;
                if (string.IsNullOrEmpty(expected.Identity) || readIdentity(address) != expected.Identity) return false;
                addresses.Add(address, expected.Address);
                return true;
            }
            if (move != null)
            {
                var held = phase == 2 || phase == 3 ? move.Item : phase == 4 || phase == 5 ? move.Swap : null;
                Bind(cursor, held);
                foreach (var item in actual.Where(item => !addresses.ContainsKey(item.Address)))
                {
                    if (phase >= 4 && phase <= 6 &&
                        (item.X, item.Y, item.Width, item.Height) == (move.X, move.Y, move.Item.Width, move.Item.Height))
                        Bind(item.Address, move.Item);
                    if (phase == 6 && move.Swap != null &&
                        (item.X, item.Y, item.Width, item.Height) == (move.Item.X, move.Item.Y, move.Swap.Width, move.Swap.Height))
                        Bind(item.Address, move.Swap);
                }
            }
            foreach (var item in actual)
                if (addresses.TryGetValue(item.Address, out var planned)) item.Address = planned;
            return addresses.TryGetValue(cursor, out var plannedCursor) ? plannedCursor : cursor;
        }

        private static List<SortItem> SortVisibleGrid(List<SortItem> actual, List<SortItem> before,
            SortMove move, int phase, IntPtr cursor)
        {
            // Picking up an item can leave the server's grid unchanged until the cursor is
            // emptied. Only account for that pending transaction with the exact held item
            // and an otherwise unchanged grid. Final placement still needs a native acknowledgement.
            if (move == null || cursor == IntPtr.Zero || !SortGridEquals(actual, before)) return actual;
            if ((phase == 2 || phase == 3) && cursor == move.Item.Address)
                return before.Where(item => item.Address != cursor).ToList();
            if ((phase == 4 || phase == 5) && move.Swap != null && cursor == move.Swap.Address)
                return before.Where(item => item.Address != cursor)
                    .Select(item => item.Address == move.Item.Address ? item.At(move.X, move.Y) : item).ToList();
            return actual;
        }

        private void MoveSortCursor(int x, int y, SortItem item)
        {
            sortCursor = new NativePoint { X = sortWindow.X + (int)(sortOrigin.X + (x + item.Width / 2f) * sortCellSize.X),
                Y = sortWindow.Y + (int)(sortOrigin.Y + (y + item.Height / 2f) * sortCellSize.Y) };
            if (!sortWindow.Contains(sortCursor.X, sortCursor.Y) || !AffinitySetCursorPos(sortCursor.X, sortCursor.Y))
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_cursor_failed", "Windows could not move the cursor to the item."));
            sortMousePositioned = true;
            sortDeadline = DateTime.UtcNow.AddMilliseconds(1500);
            sortNextAction = DateTime.UtcNow.AddMilliseconds(Math.Clamp(Settings.AffinityTransferDelayMs / 2, 10, 75));
        }

        private void SortClick()
        {
            if (!AffinityGameIsForeground() || new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x01, 0x02 }.Any(key => (AffinityGetAsyncKeyState(key) & 0x8000) != 0))
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_target_changed", "The item moved, its position could not be confirmed, or a mouse button/modifier key is being held."));
            var inputs = new[] { AffinityMouseInput(0x0002), AffinityMouseInput(0x0004) };
            if (AffinitySendInput(2, inputs, Marshal.SizeOf<AffinityInput>()) != 2)
            {
                AffinitySendInput(1, new[] { AffinityMouseInput(0x0004) }, Marshal.SizeOf<AffinityInput>());
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_click_failed", "Windows did not accept the mouse click."));
            }
            sortDeadline = DateTime.UtcNow.AddMilliseconds(1500);
            sortNextAction = DateTime.UtcNow.AddMilliseconds(20);
        }

        private void UpdateGridSort()
        {
            if (!sortRunning) return;
            try
            {
                var now = DateTime.UtcNow;
                if ((AffinityGetAsyncKeyState(0x1B) & 0x8000) != 0) { StopGridSort(); return; }
                if (now < sortNextAction) return;
                if (!sortPrepared)
                {
                    if (Core.IsSettingsMenuOpen)
                    {
                        var hide = typeof(Core).Assembly.GetType("ExileCenter.Settings.SettingsWindow")?.GetMethod("HideSettingsWindow", BindingFlags.Static | BindingFlags.NonPublic);
                        if (hide == null) throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_menu_failed", "Could not close the GameHelper menu. Close it before starting."));
                        hide.Invoke(null, null);
                    }
                    using var game = System.Diagnostics.Process.GetProcessById((int)Core.Process.Pid);
                    AffinitySetForegroundWindow(game.MainWindowHandle);
                    sortPrepared = true; sortNextAction = now.AddMilliseconds(300); return;
                }
                if (Core.IsSettingsMenuOpen || !AffinityGameIsForeground() || Core.Process.WindowArea != sortWindow ||
                    Core.States.GameCurrentState != GameStateTypes.InGameState || affinityTransferRunning || stashScanQueue.Count > 0)
                { StopGridSort(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped.")); return; }
                if (sortMousePositioned && (!AffinityGetCursorPos(out var current) || Math.Abs(current.X - sortCursor.X) > 2 || Math.Abs(current.Y - sortCursor.Y) > 2))
                { StopGridSort(PluginText.T("stashutility.transfer.affinity_mouse_moved", "The mouse moved. Leave it still during the transfer.")); return; }
                if (FindPlayerInventoryAddress(sortStash ? InventoryName.StashInventoryId : InventoryName.MainInventory1) != sortInventory)
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
                var actual = ReadSortItems(sortInventory, sortColumns, sortRows);
                var cursor = ReadSortCursorItem();
                cursor = NormalizeSortAddresses(actual, sortMove, sortPhase, cursor, sortAddresses,
                    address => SortItemIdentity(ReadFreshItem(address)));
                actual = SortVisibleGrid(actual, sortItems, sortMove, sortPhase, cursor);
                var surface = FindSortSurface(sortStash);
                var candidates = sortStash ? new[] { surface, ResolvePath(surface, new int[] { 0 }), ResolvePath(surface, new int[] { 0, 0 }) } : new[] { surface };
                if (surface == IntPtr.Zero || !candidates.Contains(sortGrid) || !IsElementVisible(sortGrid))
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
                // Server inventory acknowledgements can precede the corresponding UI frame.
                // Wait for geometry to catch up before sending another click.
                if (sortPhase == 0 || sortPhase == 1 || sortPhase == 3 || sortPhase == 5)
                {
                    if (!TrySortGeometry(surface, actual, sortStash, sortColumns, sortRows, out var grid, out var origin, out var cell) ||
                        grid != sortGrid || Vector2.Distance(origin, sortOrigin) > 4 || Vector2.Distance(cell, sortCellSize) > 3)
                    {
                        if (now >= sortDeadline) throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
                        sortNextAction = now.AddMilliseconds(20); return;
                    }
                }
                if (sortPhase == 0)
                {
                    if (cursor != IntPtr.Zero || !SortGridEquals(actual, sortItems))
                        throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
                    if (sortItems.All(item => sortTargets[item.Address] == (item.X, item.Y)))
                    { StopGridSort(PluginText.F("stashutility.sort.complete", "Sorting complete: {0} move(s).", sortMoves)); return; }
                    string layout = string.Join(";", sortItems.OrderBy(item => item.Address.ToInt64()).Select(item => $"{item.Address}:{item.X},{item.Y}"));
                    if (sortMoves >= 2000 || !sortLayouts.Add(layout) || (sortMove = NextSortMove(sortItems, sortTargets, sortColumns, sortRows, sortProtectedCells)) == null)
                    { StopGridSort(PluginText.T("stashutility.sort.needs_space", "No safe arrangement could be found. Free some space or choose the compact mode.")); return; }
                    MoveSortCursor(sortMove.Item.X, sortMove.Item.Y, sortMove.Item); sortPhase = 1;
                    SetSortFeedback(PluginText.F("stashutility.sort.progress", "Sorting: {0} move(s). Leave the mouse still; Escape stops sorting.", sortMoves)); return;
                }
                var first = sortMove.Item;
                var afterPickup = sortItems.Where(item => item.Address != first.Address).ToList();
                var afterDrop = sortItems.Where(item => item.Address != sortMove.Swap?.Address)
                    .Select(item => item.Address == first.Address ? item.At(sortMove.X, sortMove.Y) : item).ToList();
                var afterSwap = afterDrop.Concat(sortMove.Swap == null ? Enumerable.Empty<SortItem>() : new[] { sortMove.Swap.At(first.X, first.Y) }).ToList();
                if (sortPhase == 1 || sortPhase == 3 || sortPhase == 5)
                {
                    var expected = sortPhase == 1 ? sortItems : sortPhase == 3 ? afterPickup : afterDrop;
                    var expectedCursor = sortPhase == 1 ? IntPtr.Zero : sortPhase == 3 ? first.Address : sortMove.Swap.Address;
                    if (cursor != expectedCursor || !SortGridEquals(actual, expected))
                        throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
                    SortClick(); sortPhase++; return;
                }
                var awaited = sortPhase == 2 ? afterPickup : sortPhase == 4 ? afterDrop : afterSwap;
                var held = sortPhase == 2 ? first.Address : sortPhase == 4 && sortMove.Swap != null ? sortMove.Swap.Address : IntPtr.Zero;
                if (cursor != held || !SortGridEquals(actual, awaited))
                {
                    if (now >= sortDeadline)
                    {
                        Console.WriteLine($"[StashUtility] Sort acknowledgement failed: phase={sortPhase}, cursor={cursor:X}, expected={held:X}; " +
                            $"grid={string.Join(";", actual.Select(item => $"{item.Address:X}@{item.X},{item.Y}:{item.Width}x{item.Height}"))}; " +
                            $"expectedGrid={string.Join(";", awaited.Select(item => $"{item.Address:X}@{item.X},{item.Y}:{item.Width}x{item.Height}"))}");
                        throw new InvalidOperationException(PluginText.T("stashutility.sort.not_acknowledged", "The game did not confirm the move. Sorting stopped."));
                    }
                    sortNextAction = now.AddMilliseconds(20); return;
                }
                if (sortPhase == 2) { MoveSortCursor(sortMove.X, sortMove.Y, first); sortPhase = 3; return; }
                if (sortPhase == 4 && sortMove.Swap != null) { MoveSortCursor(first.X, first.Y, sortMove.Swap); sortPhase = 5; return; }
                sortItems = afterSwap; sortMoves += sortMove.Swap == null ? 1 : 2; sortPhase = 0;
                sortDeadline = now.AddMilliseconds(1500);
                sortNextAction = now;
            }
            catch (Exception ex) { StopGridSort(ex.InnerException?.Message ?? ex.Message); }
        }

        private static bool UsesGameAffinity(string kind)
        {
            // Match item families, not items observed in a previous stash scan.
            string[] families = { "Currency", "Maps", "MapFragments", "TowerAugment", "Pinnacle",
                "Gem", "Gems", "SoulCores", "Ultimatum", "Sanctum", "Brequel", "Expedition" };
            return !string.IsNullOrEmpty(kind) && families.Any(family =>
                kind == family || kind.StartsWith(family + "/", StringComparison.Ordinal));
        }

        private static bool IsEquipmentKind(string kind)
        {
            string[] families = { "Weapons", "Armours", "Quivers", "Rings", "Amulets", "Belts", "Charms", "Jewels", "Flasks" };
            return !string.IsNullOrEmpty(kind) && families.Any(family => kind == family || kind.StartsWith(family + "/", StringComparison.Ordinal));
        }

        private static bool IsRoutedGemKind(string kind) =>
            kind == "Gem/Skills" || kind == "Gem/Supports" || kind == "Gem/Kalguuran";

        private StashDestination GetGemDestination(string kind)
        {
            if (!IsRoutedGemKind(kind)) return null;
            var destination = Settings.TransferDestinations?.GetValueOrDefault(kind);
            return destination?.Disabled == true || string.IsNullOrWhiteSpace(destination?.TabName) ? null : destination;
        }

        private static readonly string[] UniqueTabletCategories = { "Map", "Boss", "Abyss", "Breach", "Ritual", "Delirium" };

        private static bool IsUniqueTabletDestinationKey(string key) =>
            key?.StartsWith("UniqueTablets/", StringComparison.Ordinal) == true;

        private StashDestination GetUniqueTabletDestination(string category)
        {
            if (category == null || Settings.TransferDestinations == null) return null;
            var destination = Settings.TransferDestinations.GetValueOrDefault("UniqueTablets/" + category);
            if (destination == null || !destination.Disabled && string.IsNullOrWhiteSpace(destination.TabName))
                destination = Settings.TransferDestinations.GetValueOrDefault("UniqueTablets");
            return destination?.Disabled == true || string.IsNullOrWhiteSpace(destination?.TabName) ? null : destination;
        }

        private static string GetUniqueTabletCategory(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Metadata/Items/TowerAugment/", StringComparison.Ordinal)) return null;
            return path.Substring(path.LastIndexOf('/') + 1) switch
            {
                "DeliriumAugment" => "Delirium",
                "RitualAugment" => "Ritual",
                "BreachAugment" => "Breach",
                "AbyssAugment" => "Abyss",
                "MapBossAugment" => "Boss",
                "GenericAugment" => "Map",
                _ => null
            };
        }

        private StashDestination GetEquipmentDestination(string kind)
        {
            if (!IsEquipmentKind(kind) || Settings.TransferDestinations == null) return null;
            // PDT jewels must not fall back to an ordinary jewel tab or the general Sapphire tab.
            if (kind.StartsWith("Jewels/", StringComparison.Ordinal) && kind.EndsWith("/TimeLost", StringComparison.Ordinal))
            {
                var timeLostDestination = Settings.TransferDestinations.GetValueOrDefault(kind);
                return timeLostDestination?.Disabled == true || string.IsNullOrWhiteSpace(timeLostDestination?.TabName) ? null : timeLostDestination;
            }
            // Prefer the most specific configured metadata family, independent of the game's language.
            var destination = Settings.TransferDestinations
                .Where(pair => (pair.Value?.Disabled == true || !string.IsNullOrWhiteSpace(pair.Value?.TabName)) &&
                    (pair.Key == kind || kind.StartsWith(pair.Key + "/", StringComparison.Ordinal)))
                .OrderByDescending(pair => pair.Key.Length).Select(pair => pair.Value).FirstOrDefault();
            if (destination != null) return destination.Disabled ? null : destination;
            string group = new[] { "Rings", "Amulets", "Belts", "Charms", "Jewels" }.Any(family =>
                kind == family || kind.StartsWith(family + "/", StringComparison.Ordinal)) ? "Accessories" : "Equipment";
            return Settings.TransferDestinations.TryGetValue(group, out destination) &&
                destination?.Disabled != true && !string.IsNullOrWhiteSpace(destination?.TabName) ? destination : null;
        }

        private StashDestination GetEquipmentDestination(string kind, Rarity rarity)
        {
            if (!IsEquipmentKind(kind)) return null;
            if (rarity != Rarity.Unique) return GetEquipmentDestination(kind);
            var destination = Settings.TransferDestinations?.GetValueOrDefault("UniqueEquipment");
            return destination?.Disabled == true || string.IsNullOrWhiteSpace(destination?.TabName) ? null : destination;
        }

        private static StashDestination SnapshotDestination(StashDestination destination, bool numbered = true)
        {
            if (destination?.Disabled == true || string.IsNullOrWhiteSpace(destination?.TabName)) return null;
            return new StashDestination { TabName = destination.TabName, FolderName = destination.FolderName,
                FolderPath = destination.FolderPath?.ToList() ?? new List<string>(),
                OverflowTabNames = numbered ? (destination.OverflowTabNames ?? new List<string>()).ToList() : new List<string>() };
        }

        private bool AdvanceEquipmentDestination(DateTime now)
        {
            if (affinityEquipmentDestination == null) return false;
            if (affinityEquipmentTab < affinityEquipmentDestination.OverflowTabNames.Count)
                affinityEquipmentTab++;
            else if (affinityUniqueFallbackDestination != null)
            {
                affinityEquipmentDestination = affinityUniqueFallbackDestination;
                affinityUniqueFallbackDestination = null;
                affinityEquipmentTab = 0;
                // The explicitly selected overflow tab is the last destination, even if numbered.
                affinityEquipmentRouteResolved = true;
            }
            else return false;
            affinityNavigationElement = IntPtr.Zero;
            affinityNavigationDeadline = now.AddSeconds(20);
            affinityClicked = false;
            affinityNextAction = now.AddMilliseconds(affinityActionDelay);
            return true;
        }

        private static string GetItemKind(string path)
        {
            const string prefix = "Metadata/Items/";
            if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.Ordinal)) return string.Empty;
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash <= prefix.Length) return string.Empty;
            string kind = path.Substring(prefix.Length, lastSlash - prefix.Length);
            string baseId = path.Substring(lastSlash + 1);
            // Spell staves and quarterstaves share the native Staves folder. Keep its old
            // parent destination as a fallback, while exposing both types independently.
            if (kind == "Weapons/TwoHandWeapons/Staves")
            {
                if (baseId.StartsWith("FourQuarterstaff", StringComparison.Ordinal) || baseId.StartsWith("Quarterstaff", StringComparison.Ordinal) || baseId.StartsWith("Warstaff", StringComparison.Ordinal))
                    return kind + "/Quarterstaves";
                if (baseId.StartsWith("FourStaff", StringComparison.Ordinal) || baseId.StartsWith("Staff", StringComparison.Ordinal))
                    return kind + "/CasterStaves";
            }
            if (kind == "Currency" && baseId.StartsWith("CurrencySetKalguuranSkillGemLevel", StringComparison.Ordinal) &&
                GetGemLevelFromPath(path) > 0)
                return "Gem/Kalguuran";
            if (kind == "Gem" || kind == "Gems")
            {
                if (Data.ModDatabase.KalguuranGemIds.Contains(baseId)) return "Gem/Kalguuran";
                return baseId.StartsWith("Support", StringComparison.Ordinal) ? "Gem/Supports" : "Gem/Skills";
            }
            if (kind == "Jewels" || kind.StartsWith("Jewels/", StringComparison.Ordinal))
            {
                // Base metadata is stable across languages and does not depend on a prior stash scan.
                bool timeLost = baseId.StartsWith("JewelRadius", StringComparison.Ordinal) ||
                    baseId.Contains("TimeLost", StringComparison.Ordinal) || baseId.Contains("Time-Lost", StringComparison.Ordinal);
                string jewelKind = null;
                if (baseId.StartsWith("JewelRadiusInt", StringComparison.Ordinal) || baseId.StartsWith("JewelInt", StringComparison.Ordinal) ||
                    baseId.StartsWith("Sapphire", StringComparison.Ordinal)) jewelKind = "Jewels/Sapphire";
                else if (baseId.StartsWith("JewelRadiusDex", StringComparison.Ordinal) || baseId.StartsWith("JewelDex", StringComparison.Ordinal) ||
                    baseId.StartsWith("Emerald", StringComparison.Ordinal)) jewelKind = "Jewels/Emerald";
                else if (baseId.StartsWith("JewelRadiusStr", StringComparison.Ordinal) || baseId.StartsWith("JewelStr", StringComparison.Ordinal) ||
                    baseId.StartsWith("Ruby", StringComparison.Ordinal)) jewelKind = "Jewels/Ruby";
                else if (baseId.StartsWith("JewelRadiusDiamond", StringComparison.Ordinal) || baseId.StartsWith("JewelDiamond", StringComparison.Ordinal) ||
                    baseId.StartsWith("JewelAll", StringComparison.Ordinal) || baseId.StartsWith("JewelStat", StringComparison.Ordinal) ||
                    baseId.StartsWith("Diamond", StringComparison.Ordinal)) jewelKind = "Jewels/Diamond";
                if (jewelKind != null) return jewelKind + (timeLost ? "/TimeLost" : string.Empty);
            }
            return kind;
        }

        private static int GetGemLevelFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            string id = path.Substring(path.LastIndexOf('/') + 1);
            foreach (var prefix in new[] { "CurrencySetKalguuranSkillGemLevel", "SkillGemUncut", "ReservationGemUncut", "SupportGemUncut" })
                if (id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.Substring(prefix.Length), out var level) && level >= 1 && level <= 20)
                    return level;
            return 0;
        }

        private bool TryGetAffinityInventory(out IntPtr inventory, out IntPtr stash)
        {
            inventory = stash = IntPtr.Zero;
            if (Core.States.GameCurrentState != GameStateTypes.InGameState || Core.GHSettings.EnableControllerMode ||
                (handleObj == null && !InitReflection())) return false;
            var gameUi = Core.States.InGameStateObject.GameUi;
            if (gameUi == null || !IsElementVisible(gameUi.LeftPanel.Address) || !IsElementVisible(gameUi.RightPanel.Address)) return false;
            stash = ResolvePath(gameUi.LeftPanel.Address, new int[] { 2, 0, 0, 0, 1 });
            var panel = ResolvePath(gameUi.RightPanel.Address, new int[] { 5 });
            if (!IsElementVisible(panel)) return false;
            inventory = ResolvePath(panel, new int[] { 36 });
            // This grid is a layout container; its own visibility bit can be unset
            // while the inventory panel and its actual item slots remain visible.
            return IsElementVisible(stash) && inventory != IntPtr.Zero &&
                TryReadUiString(stash, 0x98, out var id) && id == "top_level_tabs";
        }

        private IntPtr[] ReadAffinityInventorySlots(IntPtr inventory)
        {
            var vector = ReadMemory<UiElementBaseOffset>(inventory).ChildrensPtr;
            long count = vector.TotalElements(IntPtr.Size);
            if (count < 0 || count > 144) throw new InvalidOperationException("Unexpected inventory layout.");
            return ReadStdVector<IntPtr>(vector);
        }

        private void BeginAffinityTransfer()
        {
            if (sortRunning || affinityTransferRunning) return;
            affinityCompletionSummary = string.Empty;
            affinityFeedbackUntil = DateTime.UtcNow.AddSeconds(10);
            try
            {
                ReleaseAffinityControl();
                if (affinityCtrlHeld || affinityShiftHeld)
                {
                    affinityTransferStatus = PluginText.T("stashutility.transfer.affinity_release_failed", "Ctrl could not be released. Press and release Ctrl before trying again.");
                    return;
                }
                if (stashScanQueue.Count > 0 || !TryGetAffinityInventory(out affinityInventoryRoot, out affinityStashRoot))
                {
                    affinityTransferStatus = PluginText.T("stashutility.transfer.affinity_unavailable", "Finish the scan, then open your stash and character inventory in keyboard/mouse mode.");
                    return;
                }
                DiscoverSpecializedDestinations(affinityStashRoot);
                affinityProcessed.Clear();
                affinityItemOffsets.Clear();
                affinityTransferred = affinityFailed = affinityProtectedSkipped = 0;
                affinityUnrouted = 0;
                affinityTabletCategory = null;
                affinityTabletUsesSpecializedPage = false;
                affinityTabletSectionSelected = false;
                affinityTabletPlacement = null;
                affinityOpenedPage = IntPtr.Zero;
                affinityRouteIssue = string.Empty;
                affinityPendingKind = null;
                affinityKalguuranSortDestination = null;
                affinityFinishingGemSort = false;
                affinityEquipmentDestination = null;
                affinityUniqueFallbackDestination = null;
                affinityEquipmentTab = 0;
                affinityEquipmentRouteResolved = false;
                affinityNavigationElement = IntPtr.Zero;
                affinityInventoryReadRetries = 0;
                affinityProtectedCells.Clear();
                affinityProtectedCells.UnionWith((Settings.ProtectedInventoryCells ?? new HashSet<int>())
                    .Where(cell => cell >= 0 && cell < InventoryColumns * InventoryRows));
                affinityActionDelay = Math.Clamp(Settings.AffinityTransferDelayMs, 20, 150);
                affinitySlot = affinityItem = IntPtr.Zero;
                affinityClicked = affinityMousePositioned = false;
                affinityWindow = Core.Process.WindowArea;
                affinityTransferWaiting = affinityTransferRunning = true;
                affinityStartPrepared = false;
                affinityGameWindow = IntPtr.Zero;
                affinityDeadline = DateTime.UtcNow.AddSeconds(5);
                affinityNextAction = DateTime.UtcNow.AddMilliseconds(50);
                affinityTransferStatus = PluginText.T("stashutility.transfer.affinity_preparing", "Preparing transfer: the GameHelper menu will close automatically. Keep the stash and inventory open and leave the mouse still.");
            }
            catch (Exception ex)
            {
                StopAffinityTransfer(PluginText.F("stashutility.transfer.affinity_prepare_failed", "Could not prepare the transfer: {0}", ex.InnerException?.Message ?? ex.Message));
                Console.WriteLine($"[StashUtility] Affinity transfer could not start: {ex.Message}");
            }
        }

        private void StopAffinityTransfer(string reason = null)
        {
            affinityCompletionSummary = string.Empty;
            affinityTransferRunning = false;
            ReleaseAffinityControl();
            affinityFeedbackUntil = DateTime.UtcNow.AddSeconds(10);
            reason ??= PluginText.T("stashutility.transfer.affinity_manual_stop", "Stopped by the user.");
            if (affinityTabletPlacement != null)
            {
                try
                {
                    if (ReadSortCursorItem() != IntPtr.Zero)
                        reason += "\n" + PluginText.T("stashutility.sort.held_item", "An item is held by the cursor. Put it back in the grid.");
                }
                catch (Exception) { /* Never send input after a failed cursor check. */ }
            }
            affinityTransferStatus = PluginText.F("stashutility.transfer.affinity_stopped_reason", "Stopped: {0}\nTransferred: {1} stack(s). Unsuccessful Ctrl+Clicks: {2}.", reason, affinityTransferred, affinityFailed);
        }

        private void UpdateAffinityTransfer()
        {
            if (!affinityTransferRunning)
            {
                ReleaseAffinityControl();
                return;
            }
            try
            {
                var now = DateTime.UtcNow;
                if ((AffinityGetAsyncKeyState(0x1B) & 0x8000) != 0)
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_escape", "Escape was pressed."));
                    return;
                }
                if (!affinityTransferWaiting && Core.Process.WindowArea != affinityWindow)
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_window_changed", "The game window moved or resized. Start the transfer again."));
                    return;
                }
                if (affinityTransferWaiting)
                {
                    if (now < affinityNextAction) return;
                    if (!affinityStartPrepared)
                    {
                        if (Core.IsSettingsMenuOpen)
                        {
                            var hideMenu = typeof(Core).Assembly.GetType("ExileCenter.Settings.SettingsWindow")?
                                .GetMethod("HideSettingsWindow", BindingFlags.Static | BindingFlags.NonPublic);
                            if (hideMenu == null)
                            {
                                StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_menu_failed", "Could not close the GameHelper menu. Close it before starting."));
                                return;
                            }
                            hideMenu.Invoke(null, null);
                        }
                        using var gameProcess = System.Diagnostics.Process.GetProcessById((int)Core.Process.Pid);
                        affinityGameWindow = gameProcess.MainWindowHandle;
                        AffinitySetForegroundWindow(affinityGameWindow);
                        affinityStartPrepared = true;
                        affinityNextAction = now.AddMilliseconds(100);
                        affinityTransferStatus = PluginText.T("stashutility.transfer.affinity_starting", "Starting transfer. Keep the stash and inventory open; leave the mouse still. Escape stops the transfer.");
                        return;
                    }
                    if (Core.IsSettingsMenuOpen)
                    {
                        StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_menu_open", "The GameHelper menu was reopened."));
                        return;
                    }
                    if (now >= affinityDeadline)
                    {
                        StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_focus_wait", "The game could not be activated. Click in the game and start again."));
                        return;
                    }
                    if (!AffinityGameIsForeground())
                    {
                        // Clicking an overlay button can leave the overlay in the
                        // foreground. Retry activation while it still owns focus.
                        if (InventoryToolbarHasFocus()) AffinitySetForegroundWindow(affinityGameWindow);
                        affinityNextAction = now.AddMilliseconds(100);
                        return;
                    }
                    affinityWindow = Core.Process.WindowArea;
                    affinityTransferWaiting = false;
                }
                if (Core.IsSettingsMenuOpen)
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_menu_open", "The GameHelper menu was reopened."));
                    return;
                }
                if (!AffinityGameIsForeground())
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_focus_lost", "The game lost focus."));
                    return;
                }
                if (stashScanQueue.Count > 0)
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_scan_active", "A stash scan is running. Finish it before transferring."));
                    return;
                }
                if (!TryGetAffinityInventory(out var inventory, out var stash))
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_panels_closed", "The stash or character inventory is no longer visible."));
                    return;
                }
                if (inventory != affinityInventoryRoot || stash != affinityStashRoot)
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_panels_changed", "The stash or inventory changed. Start the transfer again."));
                    return;
                }
                if (affinityMousePositioned && (!AffinityGetCursorPos(out var cursor) ||
                    Math.Abs(cursor.X - affinityCursor.X) > 2 || Math.Abs(cursor.Y - affinityCursor.Y) > 2))
                {
                    StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_mouse_moved", "The mouse moved. Leave it still during the transfer."));
                    return;
                }
                if (now < affinityNextAction) return;
                var slots = ReadAffinityInventorySlots(inventory);
                var (presentItems, protectedItems) = ReadProtectedInventoryItems();
                if (affinityTabletPlacement != null)
                {
                    UpdateTabletPlacement(presentItems);
                    return;
                }
                if (affinitySlot != IntPtr.Zero)
                {
                    // Confirm completed transfers from the server inventory, since
                    // UI slots can retain a transferred item's pointer for a while.
                    bool stillPresent = presentItems.Contains(affinityItem) && (affinityClicked ||
                        (slots.Contains(affinitySlot) && GetItemAddressFromElement(affinitySlot, true, presentItems) == affinityItem));
                    if (affinityClicked)
                    {
                        ReleaseAffinityControl();
                        if (affinityCtrlHeld || affinityShiftHeld)
                        {
                            StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_release_failed", "Ctrl could not be released. Press and release Ctrl before trying again."));
                            return;
                        }
                        if (stillPresent && now < affinityDeadline)
                        {
                            affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                            return;
                        }
                        if (stillPresent && AdvanceEquipmentDestination(now)) return;
                        if (!stillPresent && affinityPendingKind == "Gem/Kalguuran" && Settings.SortGemsByLevel)
                            affinityKalguuranSortDestination = SnapshotDestination(affinityEquipmentDestination, false);
                        if (stillPresent) affinityFailed++; else affinityTransferred++;
                        affinitySlot = affinityItem = IntPtr.Zero;
                        affinityEquipmentDestination = null;
                        affinityUniqueFallbackDestination = null;
                        // The game has already acknowledged the transfer. Select the next item
                        // on the next frame without adding another artificial pause.
                        affinityNextAction = now;
                        return;
                    }
                    if (protectedItems?.Contains(affinityItem) == true)
                    {
                        ReleaseAffinityControl();
                        if (affinityCtrlHeld || affinityShiftHeld)
                        {
                            StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_release_failed", "Ctrl could not be released. Press and release Ctrl before trying again."));
                            return;
                        }
                        affinityProtectedSkipped++;
                        affinitySlot = affinityItem = IntPtr.Zero;
                        affinityEquipmentDestination = null;
                        affinityUniqueFallbackDestination = null;
                        affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                        return;
                    }
                    // Revalidate the hovered inventory slot immediately before sending input.
                    if (!stillPresent || (presentItems != null && !presentItems.Contains(affinityItem)) ||
                        !PluginUiElementReflection.TryValidateItemAddress(affinityItem, out var pendingPath, out _) ||
                        !(UsesGameAffinity(GetItemKind(pendingPath)) ||
                            (affinityEquipmentDestination != null && IsEquipmentKind(GetItemKind(pendingPath)))) ||
                        !PluginUiElementReflection.TryGetAbsoluteRect(affinitySlot, out var pos, out var size) ||
                        new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x01, 0x02 }.Any(key =>
                            !(key == 0x11 && affinityCtrlHeld) && !(key == 0x10 && affinityShiftHeld) && (AffinityGetAsyncKeyState(key) & 0x8000) != 0))
                    {
                        StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_target_changed", "The item moved, its position could not be confirmed, or a mouse button/modifier key is being held."));
                        return;
                    }
                    if (affinityEquipmentDestination != null)
                    {
                        if (!EnsureEquipmentDestination()) return;
                        if (affinityTabletCategory != null && !EnsureUniqueTabletCategory()) return;
                        if (Settings.SortGemsByLevel && IsRoutedGemKind(GetItemKind(pendingPath)) &&
                            GetItemKind(pendingPath) != "Gem/Kalguuran" && !EnsureGemLevelSort()) return;
                        var itemCursor = new NativePoint { X = (int)(pos.X + size.X / 2) + affinityWindow.X,
                            Y = (int)(pos.Y + size.Y / 2) + affinityWindow.Y };
                        if (Math.Abs(itemCursor.X - affinityCursor.X) > 2 || Math.Abs(itemCursor.Y - affinityCursor.Y) > 2)
                        {
                            if (affinityCtrlHeld || affinityShiftHeld) throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_target_changed", "The destination or inventory position changed before the click."));
                            MoveTransferCursor(itemCursor);
                            affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                            return;
                        }
                    }
                    if (Math.Abs(pos.X + size.X / 2 + affinityWindow.X - affinityCursor.X) > 2 ||
                        Math.Abs(pos.Y + size.Y / 2 + affinityWindow.Y - affinityCursor.Y) > 2)
                    {
                        StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_target_changed", "The item moved, its position could not be confirmed, or a mouse button/modifier key is being held."));
                        return;
                    }
                    if (!AffinityGameIsForeground())
                    {
                        StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_focus_lost", "The game lost focus."));
                        return;
                    }
                    if (affinityTabletUsesSpecializedPage && affinityTabletCategory is ("Boss" or "Map"))
                    {
                        BeginTabletPlacement(presentItems);
                        return;
                    }
                    if (!affinityCtrlHeld)
                    {
                        var keyDown = new[] { AffinityKeyInput(0) };
                        if (AffinitySendInput(1, keyDown, Marshal.SizeOf<AffinityInput>()) != 1)
                        {
                            StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_key_failed", "Windows did not accept the Ctrl key press."));
                            return;
                        }
                        affinityCtrlHeld = true;
                        affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                        return;
                    }
                    // Keep Ctrl down across a game frame before and after the click.
                    // Explicit gem destinations override the game's single gem affinity.
                    if ((IsRoutedGemKind(GetItemKind(pendingPath)) || affinityTabletCategory != null) && !affinityShiftHeld)
                    {
                        if (AffinitySendInput(1, new[] { AffinityKeyInput(0, 0x10) }, Marshal.SizeOf<AffinityInput>()) != 1)
                            throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_key_failed", "Windows did not accept the modifier key press."));
                        affinityShiftHeld = true;
                        affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                        return;
                    }
                    var inputs = new[] { AffinityMouseInput(0x0002), AffinityMouseInput(0x0004) };
                    uint sent = AffinitySendInput((uint)inputs.Length, inputs, Marshal.SizeOf<AffinityInput>());
                    if (sent != inputs.Length)
                    {
                        var releases = new[] { AffinityMouseInput(0x0004) };
                        AffinitySendInput((uint)releases.Length, releases, Marshal.SizeOf<AffinityInput>());
                        StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_click_failed", "Windows did not accept the mouse click."));
                        return;
                    }
                    affinityClicked = true;
                    affinityDeadline = now.AddMilliseconds(1500);
                    affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                    return;
                }

                foreach (var slot in slots)
                {
                    if (slot == IntPtr.Zero || !IsElementVisible(slot)) continue;
                    var itemAddress = GetItemAddressFromElement(slot, true, presentItems);
                    if (itemAddress == IntPtr.Zero || affinityProcessed.Contains(itemAddress) ||
                        !PluginUiElementReflection.TryValidateItemAddress(itemAddress, out var path, out _) ||
                        !(UsesGameAffinity(GetItemKind(path)) || IsEquipmentKind(GetItemKind(path)))) continue;
                    if (presentItems != null && !presentItems.Contains(itemAddress))
                    {
                        // The UI can lag behind the server inventory after a transfer. Wait for it
                        // to catch up without sending input or bypassing the protected cells.
                        if (++affinityInventoryReadRetries > 3)
                            throw new InvalidOperationException("Inventory item could not be matched to the protected grid.");
                        affinityNextAction = now.AddMilliseconds(100);
                        return;
                    }
                    affinityInventoryReadRetries = 0;
                    if (protectedItems?.Contains(itemAddress) == true)
                    {
                        affinityProcessed.Add(itemAddress);
                        affinityProtectedSkipped++;
                        continue;
                    }
                    affinityEquipmentDestination = null;
                    affinityUniqueFallbackDestination = null;
                    affinityTabletCategory = null;
                    affinityTabletUsesSpecializedPage = false;
                    affinityTabletSectionSelected = false;
                    affinityTabletReadDeadline = default;
                    affinityTabletNavigationReadDeadline = default;
                    string itemKind = GetItemKind(path);
                    affinityFinishingGemSort = false;
                    affinityPendingKind = itemKind;
                    affinityGemSortAttempts = 0;
                    if (IsRoutedGemKind(itemKind))
                    {
                        var destination = GetGemDestination(itemKind);
                        if (destination == null)
                        {
                            affinityProcessed.Add(itemAddress);
                            affinityUnrouted++;
                            continue;
                        }
                        affinityEquipmentDestination = SnapshotDestination(destination, false);
                        affinityEquipmentTab = 0;
                        affinityEquipmentRouteResolved = true;
                        affinityNavigationElement = IntPtr.Zero;
                        affinityNavigationDeadline = now.AddSeconds(20);
                    }
                    else if (itemKind == "TowerAugment" || itemKind.StartsWith("TowerAugment/", StringComparison.Ordinal))
                    {
                        var item = ReadFreshItem(itemAddress);
                        if (item == null || !item.IsValid || !item.TryGetComponent<Mods>(out var mods))
                            throw new InvalidOperationException(PluginText.T("stashutility.transfer.rarity_unavailable", "The item rarity could not be read. No click was sent for this item."));
                        if (mods.Rarity == Rarity.Unique)
                        {
                            affinityTabletCategory = GetUniqueTabletCategory(path);
                            var destination = GetUniqueTabletDestination(affinityTabletCategory);
                            if (affinityTabletCategory == null || destination?.Disabled == true || string.IsNullOrWhiteSpace(destination?.TabName))
                            {
                                affinityProcessed.Add(itemAddress);
                                affinityUnrouted++;
                                affinityRouteIssue = PluginText.T("stashutility.transfer.tablet_route_missing", "Unique tablets stayed in your inventory. Choose a destination for their type, or a default unique-tablet destination.");
                                affinityTabletCategory = null;
                                continue;
                            }
                            affinityEquipmentDestination = SnapshotDestination(destination, false);
                            affinityEquipmentTab = 0;
                            affinityEquipmentRouteResolved = true;
                            affinityNavigationElement = IntPtr.Zero;
                            affinityNavigationDeadline = now.AddSeconds(20);
                        }
                    }
                    else if (IsEquipmentKind(itemKind))
                    {
                        var item = ReadFreshItem(itemAddress);
                        if (item == null || !item.IsValid || !item.TryGetComponent<Mods>(out var mods))
                            throw new InvalidOperationException(PluginText.T("stashutility.transfer.rarity_unavailable", "The equipment rarity could not be read. No click was sent for this item."));
                        var destination = GetEquipmentDestination(GetItemKind(path), mods.Rarity);
                        if (destination == null)
                        {
                            affinityProcessed.Add(itemAddress);
                            affinityUnrouted++;
                            continue;
                        }
                        affinityEquipmentDestination = SnapshotDestination(destination);
                        if (mods.Rarity == Rarity.Unique)
                            affinityUniqueFallbackDestination = SnapshotDestination(Settings.TransferDestinations.GetValueOrDefault("UniqueEquipmentOverflow"), false);
                        affinityEquipmentTab = 0;
                        affinityEquipmentRouteResolved = false;
                        affinityNavigationElement = IntPtr.Zero;
                        affinityNavigationDeadline = now.AddSeconds(20);
                    }
                    if (!PluginUiElementReflection.TryGetAbsoluteRect(slot, out var position, out var size) ||
                        position.X < 0 || position.Y < 0 || position.X + size.X > affinityWindow.Width || position.Y + size.Y > affinityWindow.Height)
                        throw new InvalidOperationException("Inventory item rectangle unavailable.");
                    affinityCursor = new NativePoint { X = (int)(position.X + size.X / 2) + affinityWindow.X,
                        Y = (int)(position.Y + size.Y / 2) + affinityWindow.Y };
                    if (!AffinitySetCursorPos(affinityCursor.X, affinityCursor.Y))
                    {
                        StopAffinityTransfer(PluginText.T("stashutility.transfer.affinity_cursor_failed", "Windows could not move the cursor to the inventory item."));
                        return;
                    }
                    affinityMousePositioned = true;
                    affinityProcessed.Add(itemAddress);
                    affinitySlot = slot;
                    affinityItem = itemAddress;
                    affinityClicked = false;
                    affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                    return;
                }
                if (affinityKalguuranSortDestination != null)
                {
                    if (!affinityFinishingGemSort)
                    {
                        affinityEquipmentDestination = affinityKalguuranSortDestination;
                        affinityEquipmentTab = 0;
                        affinityEquipmentRouteResolved = true;
                        affinityTabletCategory = null;
                        affinityNavigationElement = IntPtr.Zero;
                        affinityNavigationDeadline = now.AddSeconds(20);
                        affinityFinishingGemSort = true;
                    }
                    if (!EnsureEquipmentDestination()) return;
                }
                affinityTransferRunning = false;
                affinityFeedbackUntil = now.AddSeconds(3);
                affinityTransferStatus = PluginText.F("stashutility.transfer.complete_equipment", "Finished: {0} stack(s) transferred, {1} protected, {2} could not be deposited, {3} equipment item(s) without a destination. Unrecognized item families stayed in the inventory.", affinityTransferred, affinityProtectedSkipped, affinityFailed, affinityUnrouted);
                if (!string.IsNullOrEmpty(affinityRouteIssue)) affinityTransferStatus += "\n" + affinityRouteIssue;
                affinityCompletionSummary = PluginText.F("stashutility.transfer.complete_short", "{0} stack(s) transferred · {1} protected", affinityTransferred, affinityProtectedSkipped);
                if (affinityFailed > 0 || affinityUnrouted > 0 || !string.IsNullOrEmpty(affinityRouteIssue))
                    affinityCompletionSummary += "\n" + PluginText.T("stashutility.transfer.complete_short_details", "Some items stayed in the inventory. Details in StashUtility.");
                if (affinityFinishingGemSort) BeginGridSort(true, true);
            }
            catch (Exception ex)
            {
                StopAffinityTransfer(PluginText.F("stashutility.transfer.affinity_read_failed", "Could not read the inventory: {0}", ex.InnerException?.Message ?? ex.Message));
                Console.WriteLine($"[StashUtility] Affinity transfer interrupted: {ex.Message}");
                if (affinityTabletCategory != null)
                    try
                    {
                        if (DumpUiTreeToFile(affinityOpenedPage != IntPtr.Zero ? affinityOpenedPage : affinityStashRoot, true))
                            File.AppendAllText(Path.Combine(DllDirectory, "config", "ui_tree_dump.txt"), "\nTablet transfer: " +
                                JsonConvert.SerializeObject(new { Category = affinityTabletCategory, Reason = ex.InnerException?.Message ?? ex.Message,
                                    Page = affinityOpenedPage.ToInt64(), Destination = affinityEquipmentDestination }) + "\n");
                    }
                    catch (Exception diagnosticError) { Console.WriteLine($"[StashUtility] Tablet transfer diagnostic failed: {diagnosticError.Message}"); }
            }
        }

        private bool EnsureEquipmentDestination()
        {
            if (DateTime.UtcNow >= affinityNavigationDeadline)
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_navigation_timeout", "The destination tab could not be opened in time."));
            var destination = affinityEquipmentDestination;
            var folders = destination.FolderPath.Count > 0 ? destination.FolderPath :
                (string.IsNullOrEmpty(destination.FolderName) ? new List<string>() : new List<string> { destination.FolderName });
            if (folders.Count > 8 || destination.OverflowTabNames.Count > 256)
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_invalid_route", "The equipment destination contains too many folders or tabs."));
            var header = affinityStashRoot;
            for (int depth = 0; depth <= folders.Count; depth++)
            {
                if (!TryReadStashEntries(header, out var entries, out _, out var list))
                {
                    // Folder contents can lag behind selection. Keep the pending tablet
                    // without sending input; the existing navigation deadline bounds retries.
                    if (affinityTabletCategory != null)
                    {
                        affinityNextAction = DateTime.UtcNow.AddMilliseconds(100);
                        return false;
                    }
                    throw new InvalidOperationException(PluginText.T("stashutility.transfer.layout_unavailable", "Stash tab names are unavailable. Open the stash and try again."));
                }
                if (depth == folders.Count && !affinityEquipmentRouteResolved)
                {
                    if (folders.Count > 0 && GetTabPriority(destination.TabName) != null &&
                        entries.Any(entry => entry.name == destination.TabName))
                    {
                        var numbered = entries.Where(entry => GetTabPriority(entry.name) != null)
                            .OrderBy(entry => GetTabPriority(entry.name)).ToList();
                        if (!numbered.GroupBy(entry => GetTabPriority(entry.name)).Any(group => group.Count() > 1))
                        {
                            destination.TabName = numbered[0].name;
                            destination.OverflowTabNames = numbered.Skip(1).Select(entry => entry.name).ToList();
                        }
                    }
                    affinityEquipmentRouteResolved = true;
                }
                string name = depth < folders.Count ? folders[depth] :
                    (affinityEquipmentTab == 0 ? destination.TabName : destination.OverflowTabNames[affinityEquipmentTab - 1]);
                var matches = entries.Where(entry => string.Equals(entry.name, name, StringComparison.Ordinal)).ToList();
                if (affinityTabletCategory != null && !string.IsNullOrWhiteSpace(name) && matches.Count == 0)
                {
                    affinityNextAction = DateTime.UtcNow.AddMilliseconds(100);
                    return false;
                }
                if (string.IsNullOrWhiteSpace(name) || matches.Count != 1)
                    throw new InvalidOperationException(PluginText.F("stashutility.transfer.destination_not_found", "Destination name not found or ambiguous: {0}", name));
                var page = matches[0].page;
                int visiblePages = entries.Count(entry => IsTabVisible(entry.page));
                if (affinityTabletCategory != null && visiblePages != 1)
                {
                    affinityNextAction = DateTime.UtcNow.AddMilliseconds(100);
                    return false;
                }
                bool selected = IsElementVisible(page) && visiblePages == 1;
                var dropdown = ResolvePath(header, new int[] { 4 });
                bool dropdownOpen = IsElementVisible(dropdown);
                if (!selected || dropdownOpen)
                {
                    if (affinityCtrlHeld || affinityShiftHeld)
                        throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_target_changed", "The destination or inventory position changed before the click."));
                    if (!dropdownOpen || selected)
                    {
                        var toggle = ResolvePath(header, new int[] { 2 });
                        if (affinityTabletCategory != null && !selected && !dropdownOpen && !IsElementVisible(toggle))
                        {
                            // Small folders hide the dropdown toggle when their tabs fit
                            // on screen. Their visible buttons can be reordered: match names.
                            var button = FindVisibleStashTabButton(header, name);
                            if (button != IntPtr.Zero)
                            {
                                NavigateStashControl(button);
                                return false;
                            }
                        }
                        if (!TryReadUiString(toggle, 0x98, out var id) || id != "tab_list_button")
                            throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_control_unavailable", "The stash navigation control could not be identified."));
                        NavigateStashControl(toggle);
                        return false;
                    }
                    var rows = ReadStdVector<IntPtr>(ReadMemory<UiElementBaseOffset>(list).ChildrensPtr);
                    if (rows.Length != entries.Count || !PluginUiElementReflection.TryGetAbsoluteRect(list, out var listPos, out var listSize))
                        throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_control_unavailable", "The stash navigation control could not be identified."));
                    int index = entries.FindIndex(entry => entry.page == page);
                    var row = rows[index];
                    var label = ResolvePath(row, new int[] { 0, 1 });
                    if (!TryReadUiString(label, 0x360, out var currentName) || currentName != name ||
                        !PluginUiElementReflection.TryGetAbsoluteRect(row, out var rowPos, out var rowSize))
                        throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_control_unavailable", "The stash navigation control could not be identified."));
                    if (rowPos.Y < listPos.Y || rowPos.Y + rowSize.Y > listPos.Y + listSize.Y)
                        NavigateStashControl(list, rowPos.Y < listPos.Y ? 120 : -120);
                    else if (rowPos.X >= listPos.X && rowPos.X + rowSize.X <= listPos.X + listSize.X)
                        NavigateStashControl(row);
                    else
                        throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_control_unavailable", "The stash navigation control could not be identified."));
                    return false;
                }
                header = ResolvePath(page, new int[] { 0, 1 });
                if (depth == folders.Count) affinityOpenedPage = page;
            }
            return true;
        }

        private IntPtr FindVisibleStashTabButton(IntPtr header, string name)
        {
            var buttons = ResolvePath(header, new int[] { 0 });
            if (buttons == IntPtr.Zero || string.IsNullOrWhiteSpace(name) ||
                !TryReadUiString(buttons, 0x98, out var id) || id != "buttons_container") return IntPtr.Zero;
            var vector = ReadMemory<UiElementBaseOffset>(buttons).ChildrensPtr;
            long count = vector.TotalElements(IntPtr.Size);
            if (count < 0 || count > 257) return IntPtr.Zero;
            var matches = ReadStdVector<IntPtr>(vector).Where(button =>
            {
                if (!IsElementVisible(button)) return false;
                var label = ResolvePath(button, new int[] { 0, 1 });
                return TryReadUiString(label, 0x98, out var labelId) && labelId == "label" &&
                    TryReadUiString(label, 0x360, out var text) && string.Equals(text, name, StringComparison.Ordinal);
            }).ToList();
            return matches.Count == 1 ? matches[0] : IntPtr.Zero;
        }

        private sealed class StashUiNode
        {
            public IntPtr Address;
            public string Id = string.Empty, Text = string.Empty;
            public StashUiNode Parent;
            public readonly List<StashUiNode> Children = new();
        }

        private StashUiNode ReadStashUiBranch(IntPtr root)
        {
            var visited = new HashSet<IntPtr>();
            return Read(root, null, 0);
            StashUiNode Read(IntPtr address, StashUiNode parent, int depth)
            {
                if (address == IntPtr.Zero || depth > 18 || visited.Count >= 4096 || !visited.Add(address) ||
                    !TryReadMemory<UiElementBaseOffset>(address, out var data)) return null;
                var node = new StashUiNode { Address = address, Parent = parent };
                if (TryReadUiString(address, 0x98, out var id)) node.Id = id;
                if ((node.Id == "label" || node.Id.EndsWith("_label", StringComparison.Ordinal)) &&
                    TryReadUiString(address, 0x360, out var text)) node.Text = text;
                long count = data.ChildrensPtr.TotalElements(IntPtr.Size);
                if (count >= 0 && count <= 512)
                    foreach (var child in ReadStdVector<IntPtr>(data.ChildrensPtr))
                    {
                        var next = Read(child, node, depth + 1);
                        node.Children.Add(next ?? new StashUiNode { Parent = node });
                    }
                return node;
            }
        }

        private static IEnumerable<StashUiNode> StashUiNodes(StashUiNode root)
        {
            if (root == null) yield break;
            yield return root;
            foreach (var child in root.Children)
                foreach (var node in StashUiNodes(child)) yield return node;
        }

        private static string TabletCategoryLabel(string text) => NormalizeModSearch(text).Trim() switch
        {
            "delirium" or "delire" => "Delirium",
            "ritual" or "rituel" => "Ritual",
            "breach" or "breche" => "Breach",
            "abyss" or "abysse" or "abime" => "Abyss",
            "boss" or "bosses" or "overseer" or "superviseur" or "superviseurs" or
                "overseer tablet" or "overseer tablets" or "tablette de superviseur" or "tablettes de superviseur" => "Boss",
            "map" or "maps" or "carte" or "cartes" or "irradiated" or "irradie" or "irradiee" or "irradiees" or
                "irradiated tablet" or "irradiated tablets" or "tablette irradiee" or "tablettes irradiees" => "Map",
            _ => null
        };

        private static string TabletCategoryUiId(string id)
        {
            var tokens = (id ?? string.Empty).ToLowerInvariant().Split(new[] { '_', '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var categories = tokens.Select(TabletCategoryLabel).Where(category => category != null).Distinct().ToList();
            if (categories.Contains("Boss") && categories.Contains("Map")) categories.Remove("Map");
            return categories.Count == 1 ? categories[0] : null;
        }

        private static List<(StashUiNode button, StashUiNode page, StashUiNode pages)> UniqueTabletNavigation(StashUiNode root, string category)
        {
            // Native content pages keep this canonical order. The visible buttons are
            // reordered when selected and contain a fade_bar, so their indices differ.
            string[] categories = { "Delirium", "Ritual", "Breach", "Abyss", "Boss", "Map" };
            string ButtonCategory(StashUiNode button) => StashUiNodes(button)
                .Select(node => TabletCategoryLabel(node.Text) ?? TabletCategoryUiId(node.Id))
                .FirstOrDefault(label => label != null);
            var groups = StashUiNodes(root).Select(node => (node,
                buttons: node.Children.FirstOrDefault(child => child.Id == "buttons_container"),
                pages: node.Children.FirstOrDefault(child => child.Id == "pages_container")))
                .Where(group => group.buttons != null && group.pages != null && group.pages.Children.Count >= 2 &&
                    group.pages.Children.Count <= 12 && group.buttons.Children.Count >= group.pages.Children.Count &&
                    group.pages.Children.All(page => page.Address != IntPtr.Zero) &&
                    group.buttons.Children.Take(group.pages.Children.Count).All(button => button.Address != IntPtr.Zero)).ToList();
            var matches = groups.Where(group => group.pages.Children.Count == 6 &&
                categories.All(name => group.buttons.Children.Count(button => ButtonCategory(button) == name) == 1)).ToList();
            if (matches.Count != 1) return null;
            var target = matches[0];
            int index = Array.IndexOf(categories, category);
            if (index < 0) return null;
            var plan = new List<(StashUiNode, StashUiNode, StashUiNode)>();
            foreach (var ancestor in groups.Where(group => group.node != target.node))
            {
                int pageIndex = ancestor.pages.Children.FindIndex(page => StashUiNodes(page).Contains(target.node));
                if (pageIndex >= 0) plan.Add((ancestor.buttons.Children[pageIndex], ancestor.pages.Children[pageIndex], ancestor.pages));
            }
            // Discovery is pre-order: select outer tabs first, then the unique-tablet category.
            plan.Add((target.buttons.Children.Single(button => ButtonCategory(button) == category), target.pages.Children[index], target.pages));
            return plan;
        }

        private IntPtr FindNativeTabletSectionButton(IntPtr page)
        {
            // Native Fragment/Tablet/Trial selectors have no UI id. Their content is
            // loaded on selection, so the six unique categories may not exist yet.
            var selectors = ResolvePath(page, new[] { 0, 1 });
            if (!IsElementVisible(selectors)) return IntPtr.Zero;
            var vector = ReadMemory<UiElementBaseOffset>(selectors).ChildrensPtr;
            if (vector.TotalElements(IntPtr.Size) != 3) return IntPtr.Zero;
            var buttons = ReadStdVector<IntPtr>(vector);
            var sections = new HashSet<string>();
            var target = IntPtr.Zero;
            foreach (var button in buttons)
            {
                if (!IsElementVisible(button) ||
                    !TryReadUiString(ResolvePath(button, new[] { 0 }), 0x360, out var text)) return IntPtr.Zero;
                string section = NormalizeModSearch(text).Trim() switch
                {
                    "fragments" => "Fragments",
                    "tablets" or "tablettes" => "Tablets",
                    "trials" or "epreuves" => "Trials",
                    _ => null
                };
                if (section == null || !sections.Add(section)) return IntPtr.Zero;
                if (section == "Tablets") target = button;
            }
            return target;
        }

        private bool EnsureUniqueTabletCategory()
        {
            var plan = UniqueTabletNavigation(ReadStashUiBranch(affinityOpenedPage), affinityTabletCategory);
            bool expectsFragment = plan == null && (affinityTabletUsesSpecializedPage ||
                IsGemDestinationName("UniqueTablets", affinityEquipmentDestination?.TabName ?? string.Empty) ||
                (IsActiveTab(affinityOpenedPage, out var tabType) && tabType == "Fragment"));
            affinityTabletUsesSpecializedPage = plan != null || expectsFragment;
            // Ctrl+Shift+Click deposits into the selected ordinary tab, including an empty
            // tab. Fragment tabs additionally require their matching category page.
            if (plan == null && !expectsFragment) return true;
            if (plan == null)
            {
                if (!affinityTabletSectionSelected)
                {
                    var button = FindNativeTabletSectionButton(affinityOpenedPage);
                    if (button != IntPtr.Zero)
                    {
                        if (affinityCtrlHeld || affinityShiftHeld)
                            throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_target_changed", "The destination changed before the click."));
                        NavigateStashControl(button);
                        // Navigation first positions the cursor, then clicks next frame.
                        if (affinityNavigationElement == IntPtr.Zero)
                        {
                            affinityTabletSectionSelected = true;
                            affinityTabletNavigationReadDeadline = DateTime.UtcNow.AddSeconds(3);
                        }
                        return false;
                    }
                }
                // A newly selected tab can still expose the previous tab's controls.
                // Retry discovery before abandoning any of the six tablet categories.
                var now = DateTime.UtcNow;
                if (affinityTabletNavigationReadDeadline == default)
                    affinityTabletNavigationReadDeadline = now.AddSeconds(3);
                if (now < affinityTabletNavigationReadDeadline)
                {
                    affinityNextAction = now.AddMilliseconds(100);
                    return false;
                }
                ReleaseAffinityControl();
                if (affinityCtrlHeld || affinityShiftHeld)
                    throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_release_failed", "Release Ctrl and Shift before trying again."));
                affinityRouteIssue = PluginText.T("stashutility.transfer.tablet_view_missing", "Unique tablets stayed in the inventory: their controls could not be identified. Open Fragment > Tablets > Unique Tablets and try again.");
                try
                {
                    if (DumpUiTreeToFile(affinityOpenedPage, true))
                        File.AppendAllText(Path.Combine(DllDirectory, "config", "ui_tree_dump.txt"), "\nTablet navigation: " +
                            JsonConvert.SerializeObject(new { Category = affinityTabletCategory, Page = affinityOpenedPage.ToInt64() }) + "\n");
                }
                catch (Exception ex) { Console.WriteLine($"[StashUtility] Tablet navigation diagnostic failed: {ex.Message}"); }
                affinityUnrouted++;
                affinitySlot = affinityItem = IntPtr.Zero;
                affinityEquipmentDestination = null;
                affinityTabletCategory = null;
                affinityNextAction = DateTime.UtcNow;
                return false;
            }
            foreach (var step in plan)
            {
                if (IsElementVisible(step.page.Address) && step.pages.Children.Count(page => IsElementVisible(page.Address)) == 1) continue;
                if (affinityCtrlHeld || affinityShiftHeld)
                    throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_target_changed", "The destination changed before the click."));
                NavigateStashControl(step.button.Address);
                return false;
            }
            return true;
        }

        private static SortItem ReadTabletPlacementItem(Item item)
        {
            if (item == null || !item.IsValid || GetUniqueTabletCategory(item.Path) is not ("Boss" or "Map") ||
                !item.TryGetComponent<Mods>(out var mods) || mods.Rarity != Rarity.Unique || mods.ExplicitMods.Count == 0)
                return null;
            item.TryGetComponent<Stack>(out var stack);
            item.TryGetComponent<Base>(out var itemBase);
            if (stack != null && stack.Count < 1) return null;
            // Mastered Domain's biome changes its mod IDs, but not its unique model.
            bool masteredDomain = GetUniqueTabletCategory(item.Path) == "Map" &&
                item.TryGetComponent<RenderItem>(out var renderItem) &&
                Path.GetFileNameWithoutExtension(renderItem.ResourcePath.Replace('\\', '/')) == "PrecursorTabletMasteredDomain";
            return new SortItem { Address = item.Address, Width = 1, Height = 1, Path = item.Path, Tier = stack?.Count ?? 1,
                // Rolls and remaining uses can vary within one unique model. Only merge identical rolls.
                Kind = masteredDomain ? JsonConvert.SerializeObject(new { item.Path, Model = "MasteredDomain" }) :
                    JsonConvert.SerializeObject(new { item.Path,
                    Explicit = mods.ExplicitMods.Select(mod => mod.name).OrderBy(name => name, StringComparer.Ordinal),
                    Implicit = mods.ImplicitMods.Select(mod => mod.name).OrderBy(name => name, StringComparer.Ordinal) }),
                Identity = JsonConvert.SerializeObject(new { item.Path, Base = itemBase?.InternalName, mods.Rarity,
                    mods.ExplicitMods, mods.ImplicitMods, mods.EnchantMods, mods.HellscapeMods }) };
        }

        private List<SortItem> ReadTabletPlacementGrid(IntPtr page, IntPtr inventory, out IntPtr matchedInventory,
            out IntPtr grid, out Vector2 origin, out Vector2 cell, out string failure)
        {
            matchedInventory = inventory;
            grid = ResolvePath(page, new[] { 0, 0 });
            origin = cell = Vector2.Zero;
            failure = "Page/grid hidden";
            if (!IsElementVisible(page) || !IsElementVisible(grid)) return null;
            var children = ReadMemory<UiElementBaseOffset>(grid).ChildrensPtr;
            failure = "UI child count: " + children.TotalElements(IntPtr.Size);
            if (children.TotalElements(IntPtr.Size) < 1 || children.TotalElements(IntPtr.Size) > 96) return null;
            var visibleItems = new HashSet<IntPtr>();
            foreach (var slot in ReadStdVector<IntPtr>(children))
            {
                if (!IsElementVisible(slot)) continue;
                var address = GetItemAddressFromElement(slot, true);
                // Empty cells and duplicate UI copies are not inventory items. The complete
                // server inventory must still match below before any placement is allowed.
                if (address != IntPtr.Zero) visibleItems.Add(address);
            }
            if (inventory == IntPtr.Zero)
            {
                // Specialized pages have their own server inventories. Bind by all visible
                // item addresses, rather than guessing an inventory id from the category.
                var inventories = ReadPlayerInventoryList();
                var matches = new HashSet<IntPtr>();
                for (int i = 0; i < inventories.TotalElements(0x18); i++)
                {
                    var address = ReadMemory<IntPtr>(inventories.First + i * 0x18 + 0x08);
                    if (address == IntPtr.Zero || ReadMemory<int>(address + 0x150) != 12 || ReadMemory<int>(address + 0x154) != 8) continue;
                    if (visibleItems.SetEquals(ReadSortItems(address, 12, 8).Select(item => item.Address))) matches.Add(address);
                }
                failure = "Matching inventories: " + matches.Count + "; UI items: " + visibleItems.Count;
                if (matches.Count != 1) return null;
                inventory = matchedInventory = matches.Single();
            }
            var actual = ReadSortItems(inventory, 12, 8);
            failure = "Inventory/UI mismatch or geometry unavailable; UI items: " + visibleItems.Count + "; inventory items: " + actual.Count;
            if (!visibleItems.SetEquals(actual.Select(item => item.Address)) || actual.Any(item => item.Width != 1 || item.Height != 1) ||
                !TrySortGeometry(page, actual, true, 12, 8, out grid, out origin, out cell)) return null;
            var result = new List<SortItem>();
            foreach (var position in actual)
            {
                var item = ReadTabletPlacementItem(ReadFreshItem(position.Address));
                failure = "Unrecognized destination item: " + position.Address.ToInt64().ToString("X");
                if (item == null) return null;
                item.X = position.X; item.Y = position.Y;
                result.Add(item);
            }
            failure = string.Empty;
            return result;
        }

        private static SortItem TabletBlockTarget(List<SortItem> items, SortItem source, string category, int stackLimit, out bool blockFull)
        {
            blockFull = false;
            if (items == null || source == null || string.IsNullOrEmpty(source.Kind) || source.Tier < 1 ||
                items.Any(item => item.X < 0 || item.X >= 12 || item.Y < 0 || item.Y >= 8 || string.IsNullOrEmpty(item.Kind)) ||
                items.Select(item => (item.X, item.Y)).Distinct().Count() != items.Count) return null;
            int modelCount = category == "Boss" ? 2 : category == "Map" ? 3 : 0;
            if (modelCount == 0) return null;
            var blocks = new List<(string model, int left, int right)>();
            foreach (var model in items.GroupBy(item => item.Kind))
            {
                var clusters = new List<List<int>>();
                foreach (int column in model.Select(item => item.X).Distinct().Order())
                {
                    if (clusters.Count == 0 || column > clusters.Last().Last() + 1) clusters.Add(new List<int>());
                    clusters.Last().Add(column);
                }
                var ranked = clusters.OrderByDescending(cluster => model.Count(item => cluster.Contains(item.X))).ToList();
                if (ranked.Count > 1 && model.Count(item => ranked[0].Contains(item.X)) == model.Count(item => ranked[1].Contains(item.X))) return null;
                blocks.Add((model.Key, ranked[0].First(), ranked[0].Last()));
            }
            blocks = blocks.OrderBy(block => block.left).ToList();
            if (blocks.Count > modelCount || blocks.Zip(blocks.Skip(1), (left, right) => left.right >= right.left).Any(overlap => overlap)) return null;
            var matching = blocks.Where(block => block.model == source.Kind).ToList();
            if (matching.Count != 1) return null;
            var block = matching[0];
            var merge = items.Where(item => item.X >= block.left && item.X <= block.right &&
                item.Kind == source.Kind && item.Identity == source.Identity && item.Tier > 0 &&
                stackLimit > 0 && item.Tier <= stackLimit - source.Tier).OrderBy(item => item.Y).ThenBy(item => item.X).FirstOrDefault();
            if (merge != null) return merge;
            var occupied = items.Select(item => item.Y * 12 + item.X).ToHashSet();
            for (int y = 0; y < 8; y++)
                for (int x = block.left; x <= block.right; x++)
                    if (!occupied.Contains(y * 12 + x))
                        return new SortItem { X = x, Y = y, Width = 1, Height = 1, Kind = source.Kind, Identity = source.Identity };
            // A native tablet page has one shared grid, not three equal-width regions.
            // When all models are present, extend into adjacent free columns while
            // keeping at least one separator column before the neighbouring model.
            if (blocks.Count == modelCount)
            {
                int index = blocks.IndexOf(block);
                int leftLimit = index == 0 ? 0 : blocks[index - 1].right + 2;
                int rightLimit = index == blocks.Count - 1 ? 11 : blocks[index + 1].left - 2;
                for (int distance = 1; distance < 12; distance++)
                    foreach (int x in new[] { block.left - distance, block.right + distance })
                        if (x >= leftLimit && x <= rightLimit && !items.Any(item => item.X == x))
                            for (int y = 0; y < 8; y++)
                                if (!occupied.Contains(y * 12 + x))
                                    return new SortItem { X = x, Y = y, Width = 1, Height = 1, Kind = source.Kind, Identity = source.Identity };
            }
            blockFull = true;
            return null;
        }

        private static bool TabletPlacementGridEquals(List<SortItem> actual, List<SortItem> expected) =>
            actual != null && expected != null && actual.Count == expected.Count && expected.All(item =>
                actual.Any(other => other.X == item.X && other.Y == item.Y && other.Identity == item.Identity && other.Tier == item.Tier));

        private void BeginTabletPlacement(HashSet<IntPtr> presentItems)
        {
            var now = DateTime.UtcNow;
            if (affinityTabletReadDeadline == default) affinityTabletReadDeadline = now.AddMilliseconds(1500);
            if (ReadSortCursorItem() != IntPtr.Zero)
                throw new InvalidOperationException(PluginText.T("stashutility.sort.cursor_busy", "Put down the item held by the cursor before sorting."));
            var plan = UniqueTabletNavigation(ReadStashUiBranch(affinityOpenedPage), affinityTabletCategory);
            var source = ReadTabletPlacementItem(ReadFreshItem(affinityItem));
            var page = plan?.Last().page.Address ?? IntPtr.Zero;
            var items = ReadTabletPlacementGrid(page, IntPtr.Zero, out var inventory, out var grid, out var origin, out var cell, out var gridFailure);
            if ((items == null || source == null) && now < affinityTabletReadDeadline)
            {
                affinityNextAction = now.AddMilliseconds(100);
                return;
            }
            var fresh = ReadFreshItem(affinityItem);
            int limit = fresh != null && fresh.TryGetComponent<Stack>(out var stack) ? stack.MaxStack : 1;
            var target = TabletBlockTarget(items, source, affinityTabletCategory, limit, out var blockFull);
            if (target == null)
            {
                affinityRouteIssue = source == null
                    ? PluginText.T("stashutility.transfer.tablet_source_unavailable", "The unique tablet could not be read. It stayed in the inventory.")
                    : items == null
                        ? PluginText.T("stashutility.transfer.tablet_grid_unavailable", "The unique-tablet grid could not be read or matched to its inventory. The tablet stayed in the inventory.")
                        : blockFull
                            ? PluginText.T("stashutility.transfer.tablet_block_full", "The block reserved for this unique-tablet model is full. Free a space or choose another destination for this tablet type.")
                            : PluginText.T("stashutility.transfer.tablet_block_unavailable", "A Boss/Map tablet stayed in the inventory: its existing block is full or could not be identified safely. Keep one model in each left/centre/right block.");
                try
                {
                    var sourceMods = fresh != null && fresh.TryGetComponent<Mods>(out var diagnosticMods) ? diagnosticMods : null;
                    var sourceArt = fresh != null && fresh.TryGetComponent<RenderItem>(out var diagnosticArt) ? diagnosticArt.ResourcePath : null;
                    if (DumpUiTreeToFile(page, true))
                        File.AppendAllText(Path.Combine(DllDirectory, "config", "ui_tree_dump.txt"), "\nTablet placement: " +
                            JsonConvert.SerializeObject(new { Category = affinityTabletCategory, Source = source, Items = items,
                                SourcePath = fresh?.Path, SourceArt = sourceArt, Rarity = sourceMods?.Rarity,
                                Explicit = sourceMods?.ExplicitMods, Implicit = sourceMods?.ImplicitMods,
                                Failure = gridFailure, Inventory = inventory.ToInt64(), Grid = grid.ToInt64(), Origin = origin, Cell = cell }) + "\n");
                }
                catch (Exception ex) { Console.WriteLine($"[StashUtility] Tablet placement diagnostic failed: {ex.Message}"); }
                affinityUnrouted++;
                affinitySlot = affinityItem = IntPtr.Zero;
                affinityEquipmentDestination = null;
                affinityTabletCategory = null;
                affinityNextAction = DateTime.UtcNow;
                return;
            }
            // Stash scans can change the cached slot offset; exclude retained, non-inventory pointers here too.
            var checkedSource = ReadTabletPlacementItem(ReadFreshItem(affinityItem));
            if (checkedSource == null || checkedSource.Identity != source.Identity || checkedSource.Tier != source.Tier ||
                GetItemAddressFromElement(affinitySlot, true, presentItems) != affinityItem ||
                !PluginUiElementReflection.TryGetAbsoluteRect(affinitySlot, out var position, out var size) ||
                !AffinityGetCursorPos(out var cursor) || Math.Abs(cursor.X - affinityCursor.X) > 2 || Math.Abs(cursor.Y - affinityCursor.Y) > 2 ||
                Math.Abs(position.X + size.X / 2 + affinityWindow.X - affinityCursor.X) > 2 ||
                Math.Abs(position.Y + size.Y / 2 + affinityWindow.Y - affinityCursor.Y) > 2)
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_target_changed", "The item moved, its position could not be confirmed, or a mouse button/modifier key is being held."));
            affinityTabletPlacement = new TabletPlacement { Page = page, Grid = grid, Inventory = inventory, Origin = origin, Cell = cell,
                Source = source, Target = target, Before = items, Phase = 1 };
            TabletPlacementClick();
        }

        private void TabletPlacementClick()
        {
            if (!AffinityGameIsForeground() || new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x01, 0x02 }.Any(key => (AffinityGetAsyncKeyState(key) & 0x8000) != 0))
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_target_changed", "The item moved, its position could not be confirmed, or a mouse button/modifier key is being held."));
            if (AffinitySendInput(2, new[] { AffinityMouseInput(0x0002), AffinityMouseInput(0x0004) }, Marshal.SizeOf<AffinityInput>()) != 2)
            {
                AffinitySendInput(1, new[] { AffinityMouseInput(0x0004) }, Marshal.SizeOf<AffinityInput>());
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_click_failed", "Windows did not accept the mouse click."));
            }
            affinityDeadline = DateTime.UtcNow.AddMilliseconds(1500);
            affinityNextAction = DateTime.UtcNow.AddMilliseconds(affinityActionDelay);
        }

        private void UpdateTabletPlacement(HashSet<IntPtr> presentItems)
        {
            var placement = affinityTabletPlacement;
            var now = DateTime.UtcNow;
            var plan = UniqueTabletNavigation(ReadStashUiBranch(affinityOpenedPage), affinityTabletCategory);
            if (plan == null || plan.Last().page.Address != placement.Page ||
                plan.Last().pages.Children.Count(page => IsElementVisible(page.Address)) != 1)
                throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
            var items = ReadTabletPlacementGrid(placement.Page, placement.Inventory, out _, out var grid, out var origin, out var cell, out _);
            if (items == null)
            {
                if (now >= affinityDeadline)
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
                affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                return;
            }
            if (grid != placement.Grid || Vector2.Distance(origin, placement.Origin) > 3 || Vector2.Distance(cell, placement.Cell) > 3)
                throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
            var cursor = ReadSortCursorItem();
            var held = cursor == IntPtr.Zero ? null : ReadTabletPlacementItem(ReadFreshItem(cursor));
            bool exactHeld = held != null && held.Identity == placement.Source.Identity && held.Tier == placement.Source.Tier;
            if (placement.Phase == 1)
            {
                if (exactHeld && TabletPlacementGridEquals(items, placement.Before))
                {
                    MoveTransferCursor(new NativePoint { X = affinityWindow.X + (int)(origin.X + (placement.Target.X + 0.5f) * cell.X),
                        Y = affinityWindow.Y + (int)(origin.Y + (placement.Target.Y + 0.5f) * cell.Y) });
                    placement.Phase = 2;
                    affinityNextAction = now.AddMilliseconds(affinityActionDelay);
                    return;
                }
            }
            else if (placement.Phase == 2)
            {
                if (!exactHeld || !TabletPlacementGridEquals(items, placement.Before))
                    throw new InvalidOperationException(PluginText.T("stashutility.sort.grid_changed", "The grid changed. Sorting stopped."));
                TabletPlacementClick();
                placement.Phase = 3;
                return;
            }
            else
            {
                var expected = placement.Before.Where(item => item.X != placement.Target.X || item.Y != placement.Target.Y).ToList();
                var deposited = placement.Source.At(placement.Target.X, placement.Target.Y);
                deposited.Tier += placement.Target.Tier;
                expected.Add(deposited);
                if (cursor == IntPtr.Zero && !presentItems.Contains(affinityItem) && TabletPlacementGridEquals(items, expected))
                {
                    affinityTransferred++;
                    affinityTabletPlacement = null;
                    affinitySlot = affinityItem = IntPtr.Zero;
                    affinityEquipmentDestination = null;
                    affinityTabletCategory = null;
                    affinityNextAction = now;
                    return;
                }
            }
            if (now >= affinityDeadline)
                throw new InvalidOperationException(PluginText.T("stashutility.sort.not_acknowledged", "The game did not confirm the move. Sorting stopped."));
            affinityNextAction = now.AddMilliseconds(affinityActionDelay);
        }

        private static bool IsGemLevelSortLabel(string text) =>
            new[] { "niveau de gemme", "niveau gemme", "gem level" }.Contains(NormalizeModSearch(text));

        private bool EnsureGemLevelSort()
        {
            if (affinityGemSortAttempts >= 3)
            {
                affinityRouteIssue = PluginText.T("stashutility.transfer.gem_sort_manual", "Gems were routed, but automatic level sorting could not be confirmed. Select Gem Level in the gem stash's Sort By control.");
                return true;
            }
            var root = ReadStashUiBranch(affinityOpenedPage);
            bool Visible(StashUiNode node)
            {
                for (var parent = node; parent != null; parent = parent.Parent)
                    if (!IsElementVisible(parent.Address)) return false;
                return true;
            }
            var nodes = StashUiNodes(root).Where(Visible).ToList();
            var combos = nodes.Where(node => (node.Id.Contains("dropdown", StringComparison.OrdinalIgnoreCase) ||
                node.Id.Contains("drop_down", StringComparison.OrdinalIgnoreCase) || node.Id.Contains("combo", StringComparison.OrdinalIgnoreCase)) &&
                !node.Id.Contains("entry", StringComparison.OrdinalIgnoreCase) && !node.Id.Contains("option", StringComparison.OrdinalIgnoreCase) &&
                (node.Id.Contains("sort", StringComparison.OrdinalIgnoreCase) || node.Parent != null &&
                    node.Parent.Children.Any(child => StashUiNodes(child).Any(label =>
                        new[] { "trier par", "sort by" }.Contains(NormalizeModSearch(label.Text)))))).ToList();
            // Nested dropdown decoration nodes are not independent controls.
            combos = combos.Where(node => !combos.Any(other => other != node && StashUiNodes(other).Contains(node))).ToList();
            if (combos.Count == 1)
            {
                var combo = combos[0];
                var labels = StashUiNodes(combo).Where(node => Visible(node) && !string.IsNullOrEmpty(node.Text)).ToList();
                if (labels.Count > 0 && IsGemLevelSortLabel(labels[0].Text)) return true;
                var options = nodes.Where(node => IsGemLevelSortLabel(node.Text) && node.Parent != null &&
                    (node.Parent.Id.Contains("entry", StringComparison.OrdinalIgnoreCase) || node.Parent.Id.Contains("option", StringComparison.OrdinalIgnoreCase))).ToList();
                if (affinityCtrlHeld || affinityShiftHeld)
                    throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_target_changed", "The destination changed before the click."));
                IntPtr control = options.Count == 1 ? options[0].Parent.Address : combo.Address;
                if (affinityNavigationElement == control) affinityGemSortAttempts++;
                if (options.Count <= 1) NavigateStashControl(control);
                else return SortUnavailable();
                return false;
            }
            // An already selected level label needs no action, even when the game's dropdown ID differs.
            if (nodes.Count(node => IsGemLevelSortLabel(node.Text)) == 1) return true;
            return SortUnavailable();
            bool SortUnavailable()
            {
                affinityRouteIssue = PluginText.T("stashutility.transfer.gem_sort_manual", "Gems were routed, but automatic level sorting could not be confirmed. Select Gem Level in the gem stash's Sort By control.");
                return true;
            }
        }

        private void MoveTransferCursor(NativePoint point)
        {
            if (point.X < affinityWindow.Left || point.X >= affinityWindow.Right ||
                point.Y < affinityWindow.Top || point.Y >= affinityWindow.Bottom || !AffinitySetCursorPos(point.X, point.Y))
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_cursor_failed", "Windows could not move the cursor to the inventory item."));
            affinityCursor = point;
            affinityMousePositioned = true;
        }

        private void NavigateStashControl(IntPtr element, int wheel = 0)
        {
            if (!IsElementVisible(element) || !PluginUiElementReflection.TryGetAbsoluteRect(element, out var pos, out var size) ||
                pos.X < 0 || pos.Y < 0 || pos.X + size.X > affinityWindow.Width || pos.Y + size.Y > affinityWindow.Height)
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_control_unavailable", "The stash navigation control could not be identified."));
            var point = new NativePoint { X = (int)(pos.X + size.X / 2) + affinityWindow.X,
                Y = (int)(pos.Y + size.Y / 2) + affinityWindow.Y };
            if (affinityNavigationElement != element || affinityNavigationWheel != wheel ||
                Math.Abs(point.X - affinityCursor.X) > 2 || Math.Abs(point.Y - affinityCursor.Y) > 2)
            {
                MoveTransferCursor(point);
                affinityNavigationElement = element;
                affinityNavigationWheel = wheel;
                affinityNextAction = DateTime.UtcNow.AddMilliseconds(affinityActionDelay);
                return;
            }
            if (affinityCtrlHeld || affinityShiftHeld || !AffinityGameIsForeground() ||
                new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x01, 0x02 }.Any(key => (AffinityGetAsyncKeyState(key) & 0x8000) != 0))
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.equipment_target_changed", "The destination or inventory position changed before the click."));
            var inputs = wheel == 0 ? new[] { AffinityMouseInput(0x0002), AffinityMouseInput(0x0004) } :
                new[] { new AffinityInput { Data = new AffinityInputData { Mouse = new AffinityMouse { Flags = 0x0800, Data = unchecked((uint)wheel) } } } };
            if (AffinitySendInput((uint)inputs.Length, inputs, Marshal.SizeOf<AffinityInput>()) != inputs.Length)
            {
                if (wheel == 0) AffinitySendInput(1, new[] { AffinityMouseInput(0x0004) }, Marshal.SizeOf<AffinityInput>());
                throw new InvalidOperationException(PluginText.T("stashutility.transfer.affinity_click_failed", "Windows did not accept the mouse click."));
            }
            affinityNavigationElement = IntPtr.Zero;
            affinityNextAction = DateTime.UtcNow.AddMilliseconds(Math.Max(80, affinityActionDelay * 2));
        }

        private static bool AffinityGameIsForeground()
        {
            AffinityGetWindowThreadProcessId(AffinityGetForegroundWindow(), out var pid);
            return pid != 0 && pid == Core.Process.Pid;
        }

        private static bool InventoryToolbarHasFocus()
        {
            AffinityGetWindowThreadProcessId(AffinityGetForegroundWindow(), out var pid);
            return pid != 0 && (pid == Core.Process.Pid || pid == (uint)Environment.ProcessId);
        }

        private void DrawInventoryTransferButton()
        {
            if ((!Settings.ShowInventoryTransferButton && !Settings.ShowInventorySortButton && !Settings.EnableTabletManager) ||
                (!Core.IsSettingsMenuOpen && !affinityTransferWaiting && !InventoryToolbarHasFocus()) ||
                Core.States.GameCurrentState != GameStateTypes.InGameState || Core.GHSettings.EnableControllerMode ||
                (handleObj == null && !InitReflection())) return;
            Vector2 position = Vector2.Zero, size = Vector2.Zero;
            bool hasGridBounds;
            bool canTransfer = false;
            IntPtr panel;
            try
            {
                var gameUi = Core.States.InGameStateObject.GameUi;
                if (gameUi == null || !IsElementVisible(gameUi.RightPanel.Address)) return;
                panel = ResolvePath(gameUi.RightPanel.Address, new int[] { 5 });
                if (!IsElementVisible(panel)) return;
                var inventory = ResolvePath(panel, new int[] { 36 });
                // Layout-only grid containers can have no rectangle even while their item slots
                // and inventory panel are visible. Button display must not depend on that rectangle.
                hasGridBounds = inventory != IntPtr.Zero &&
                    PluginUiElementReflection.TryGetAbsoluteRect(inventory, out position, out size) && size.X > 100 && size.Y > 100;
            }
            catch (Exception)
            {
                // Inventory UI may be destroyed during an area change; hide the button for this frame.
                return;
            }
            try
            {
                canTransfer = !affinityTransferRunning && !sortRunning && stashScanQueue.Count == 0 && TryGetAffinityInventory(out _, out _);
            }
            catch (Exception)
            {
                // A merchant or other left-side panel is not a stash. Keep the inventory button
                // visible but disabled when the stash layout cannot be read.
            }

            var viewport = ImGui.GetMainViewport();
            int actions = (Settings.ShowInventoryTransferButton ? 1 : 0) + (Settings.ShowInventorySortButton ? 1 : 0) + (Settings.EnableTabletManager ? 1 : 0) + (Settings.InventoryTransferButtonLocked ? 1 : 2);
            var windowSize = new Vector2(4 + actions * 32, 36);
            if (inventoryToolbarPanel != panel || inventoryToolbarWindow != Core.Process.WindowArea)
            {
                inventoryToolbarAnchor = null;
                inventoryToolbarPanel = panel;
                inventoryToolbarWindow = Core.Process.WindowArea;
            }
            if (hasGridBounds) inventoryToolbarAnchor = position + new Vector2(size.X - 100, size.Y + 4);
            var anchor = viewport.Pos + (inventoryToolbarAnchor ??
                new Vector2(Math.Max(0, viewport.Size.X - 120), viewport.Size.Y * 0.83f));
            var offset = Settings.InventoryTransferButtonOffset;
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) offset = Vector2.Zero;
            var location = Vector2.Clamp(anchor + offset, viewport.Pos,
                viewport.Pos + Vector2.Max(Vector2.Zero, viewport.Size - windowSize -
                    new Vector2(Settings.InventoryTransferButtonLocked ? 32 : 0, 0)));
            ImGui.SetNextWindowPos(location, ImGuiCond.Always);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(0.9f);
            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;
            if (Settings.InventoryTransferButtonLocked) flags |= ImGuiWindowFlags.NoBackground;
            if (affinityTransferRunning || sortRunning) flags |= ImGuiWindowFlags.NoInputs;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4));
            if (ExileCenter.Ui.ImGuiTheme.BeginWindow("##stashInventoryTransfer", flags))
            {
                DrawToolbarPositionControls(anchor, location, windowSize, ref Settings.InventoryTransferButtonOffset,
                    ref Settings.InventoryTransferButtonLocked, lockFirst: true);
                if (Settings.EnableTabletManager)
                {
                    ImGui.SameLine();
                    DrawTabletVisibilityButton(true);
                }
                if (Settings.ShowInventorySortButton)
                {
                    ImGui.SameLine();
                    ImGui.BeginDisabled(affinityTransferRunning || sortRunning || stashScanQueue.Count > 0);
                    if (ImGui.Button("##sortInventory", new Vector2(28))) BeginGridSort(false);
                    DrawActionIcon(3);
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(PluginText.T("stashutility.sort.inventory_tooltip", "Sort this inventory. Protected cells stay untouched."));
                }
                if (Settings.ShowInventoryTransferButton)
                {
                    ImGui.SameLine();
                    ImGui.BeginDisabled(!canTransfer);
                    if (ImGui.Button("##transfer", new Vector2(28))) BeginAffinityTransfer();
                    DrawActionIcon(0);
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(PluginText.T("stashutility.transfer.button_tooltip", "Open your stash to transfer specialized items and equipment with a configured destination. Protected cells stay untouched. Leave the mouse still after clicking; Escape stops the transfer."));
                }
            }
            ImGui.End();
            ImGui.PopStyleVar(2);
        }

        private void DrawActionIcon(int icon)
        {
            var draw = ImGui.GetWindowDrawList();
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            uint color = ImGui.GetColorU32(ImGuiCol.Text);
            if (icon == 0)
            {
                // Arrow entering a tray.
                draw.AddLine(center + new Vector2(0, -8), center + new Vector2(0, 1), color, 2);
                draw.AddTriangleFilled(center + new Vector2(0, 5), center + new Vector2(-5, 0), center + new Vector2(5, 0), color);
                draw.AddLine(center + new Vector2(-7, 4), center + new Vector2(-7, 8), color, 2);
                draw.AddLine(center + new Vector2(-7, 8), center + new Vector2(7, 8), color, 2);
                draw.AddLine(center + new Vector2(7, 8), center + new Vector2(7, 4), color, 2);
            }
            else if (icon == 1)
            {
                // Four directional arrows; drawn without depending on icon fonts.
                draw.AddLine(center + new Vector2(-6, 0), center + new Vector2(6, 0), color, 2);
                draw.AddLine(center + new Vector2(0, -6), center + new Vector2(0, 6), color, 2);
                draw.AddTriangleFilled(center + new Vector2(-9, 0), center + new Vector2(-5, -3), center + new Vector2(-5, 3), color);
                draw.AddTriangleFilled(center + new Vector2(9, 0), center + new Vector2(5, 3), center + new Vector2(5, -3), color);
                draw.AddTriangleFilled(center + new Vector2(0, -9), center + new Vector2(3, -5), center + new Vector2(-3, -5), color);
                draw.AddTriangleFilled(center + new Vector2(0, 9), center + new Vector2(-3, 5), center + new Vector2(3, 5), color);
            }
            else if (icon == 2 || icon == 4)
            {
                draw.AddRect(center + new Vector2(icon == 4 ? 1 : -4, -8), center + new Vector2(icon == 4 ? 9 : 4, 2), color, 4, ImDrawFlags.None, 2);
                draw.AddRectFilled(center + new Vector2(-7, -1), center + new Vector2(7, 9), color, 2);
                draw.AddCircleFilled(center + new Vector2(0, 3), 1.5f, ImGui.GetColorU32(ImGuiCol.Button));
            }
            else
            {
                DrawSortIcon();
            }
        }

        private bool TabletHighlightsSuppressed() => tabletHighlightsHiddenManually ||
            (Settings.HideTabletHighlightsOnItemHover && (tabletItemHovered || tabletTooltipVisible));

        private void ToggleTabletHighlights() => tabletHighlightsHiddenManually = !tabletHighlightsHiddenManually;

        private void DrawTabletVisibilityButton(bool compact)
        {
            string label = tabletHighlightsHiddenManually
                ? PluginText.T("stashutility.tablet.show_indications", "Show tablet indications")
                : PluginText.T("stashutility.tablet.hide_indications", "Hide tablet indications");
            bool clicked = compact ? ImGui.Button("##tabletVisibility", new Vector2(28))
                : ImGui.Button(label + "##tabletVisibility");
            if (clicked) ToggleTabletHighlights();
            if (compact)
            {
                var draw = ImGui.GetWindowDrawList();
                var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                uint color = ImGui.GetColorU32(ImGuiCol.Text);
                if (tabletIconHandles.TryGetValue("TabletIrradiated", out var icon))
                    draw.AddImage(icon, center - new Vector2(12), center + new Vector2(12), Vector2.Zero, Vector2.One,
                        ImGuiHelper.Color(new Vector4(1, 1, 1, tabletHighlightsHiddenManually ? .45f : 1f)));
                else draw.AddRect(center + new Vector2(-7, -10), center + new Vector2(7, 10), color, 2, ImDrawFlags.None, 2);
                if (tabletHighlightsHiddenManually) draw.AddLine(center + new Vector2(-10, -10), center + new Vector2(10, 10), color, 2);
            }
            ImGuiHelper.ToolTip(label + "\n" +
                PluginText.T("stashutility.tablet.manual_hide_help", "Manual hiding takes priority. Tablet colors and ranks stay hidden until you click Show or restart GameHelper, even after moving the mouse."), 28f);
        }

        private static void DrawSortIcon()
        {
            var draw = ImGui.GetWindowDrawList();
            var center = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            uint color = ImGui.GetColorU32(ImGuiCol.Text);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    draw.AddRectFilled(center + new Vector2(-8 + col * 6, -8 + row * 6), center + new Vector2(-4 + col * 6, -4 + row * 6), color);
        }

        private void DrawToolbarMoveButton(Vector2 anchor, Vector2 location, Vector2 windowSize, ref Vector2 offset)
        {
            ImGui.Button("##move", new Vector2(28));
            DrawActionIcon(1);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                var viewport = ImGui.GetMainViewport();
                var moved = Vector2.Clamp(location + ImGui.GetIO().MouseDelta, viewport.Pos,
                    viewport.Pos + Vector2.Max(Vector2.Zero, viewport.Size - windowSize));
                offset = moved - anchor;
            }
            if (ImGui.IsItemDeactivated()) SaveSettings();
            ImGuiHelper.ToolTip(PluginText.T("stashutility.transfer.button_drag", "Drag to move"));
        }

        private void DrawToolbarPositionControls(Vector2 anchor, Vector2 location, Vector2 windowSize, ref Vector2 offset, ref bool locked, bool lockFirst = false)
        {
            if (!locked && !lockFirst)
            {
                DrawToolbarMoveButton(anchor, location, windowSize, ref offset);
                ImGui.SameLine();
            }
            if (ImGui.Button("##lock", new Vector2(28)))
            {
                locked = !locked;
                SaveSettings();
            }
            DrawActionIcon(locked ? 2 : 4);
            ImGuiHelper.ToolTip(locked
                ? PluginText.T("stashutility.transfer.button_unlock", "Unlock position")
                : PluginText.T("stashutility.transfer.button_lock", "Lock Position"));
            if (!locked && lockFirst)
            {
                ImGui.SameLine();
                DrawToolbarMoveButton(anchor, location, windowSize, ref offset);
            }
        }

        private void DrawStashSortButton()
        {
            if ((!Settings.ShowStashSortButton && !Settings.EnableTabletManager) || (!Core.IsSettingsMenuOpen && !InventoryToolbarHasFocus()) ||
                Core.States.GameCurrentState != GameStateTypes.InGameState || Core.GHSettings.EnableControllerMode ||
                (handleObj == null && !InitReflection())) return;
            bool canSort = false;
            bool merchant = false;
            Vector2 position = Vector2.Zero, size = Vector2.Zero;
            try
            {
                var ui = Core.States.InGameStateObject.GameUi;
                if (ui == null || !IsElementVisible(ui.LeftPanel.Address)) return;
                var header = ResolvePath(ui.LeftPanel.Address, new int[] { 2, 0, 0, 0, 1 });
                if (!IsElementVisible(header) || !TryReadUiString(header, 0x98, out var id) || id != "top_level_tabs") return;
                // Ange reuses the stash control; the exported panel title identifies her shop.
                var title = ResolvePath(ui.LeftPanel.Address, new int[] { 1 });
                merchant = TryReadUiString(title, 0x98, out var titleId) && titleId == "label_title" &&
                    TryReadUiString(title, 0x360, out var titleText) &&
                    (titleText == "Marchand" || titleText == "Merchant");
                var inventory = FindPlayerInventoryAddress(InventoryName.StashInventoryId);
                var dimensions = inventory == IntPtr.Zero ? (0, 0) : (ReadMemory<int>(inventory + 0x150), ReadMemory<int>(inventory + 0x154));
                var surface = FindSortSurface(true);
                canSort = !sortRunning && !affinityTransferRunning && stashScanQueue.Count == 0 &&
                    (dimensions == (12, 12) || dimensions == (24, 24)) && surface != IntPtr.Zero;
                canSort = canSort && IsActiveTab(surface, out var tabType) && tabType.StartsWith("Normal/Quad", StringComparison.Ordinal);
                if (surface != IntPtr.Zero)
                    foreach (var candidate in new[] { ResolvePath(surface, new int[] { 0, 0 }), ResolvePath(surface, new int[] { 0 }), surface })
                        if (candidate != IntPtr.Zero && IsElementVisible(candidate) &&
                            PluginUiElementReflection.TryGetAbsoluteRect(candidate, out position, out size) && size.X > 100 && size.Y > 100) break;
            }
            catch (Exception) { return; }
            ref var toolbarOffset = ref (merchant ? ref Settings.MerchantToolbarOffset : ref Settings.StashToolbarOffset);
            ref var toolbarLocked = ref (merchant ? ref Settings.MerchantToolbarLocked : ref Settings.StashToolbarLocked);
            var viewport = ImGui.GetMainViewport();
            var windowSize = new Vector2(4 + ((Settings.ShowStashSortButton ? 1 : 0) + (Settings.EnableTabletManager ? 1 : 0) + (toolbarLocked ? 1 : 2)) * 32, 36);
            bool hasGridBounds = size.X > 100 && size.Y > 100;
            var anchor = viewport.Pos + (hasGridBounds ? position + new Vector2(size.X - windowSize.X, size.Y + 4) : new Vector2(16, viewport.Size.Y * 0.83f));
            var offset = toolbarOffset;
            if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y)) offset = Vector2.Zero;
            var location = Vector2.Clamp(anchor + offset, viewport.Pos, viewport.Pos + Vector2.Max(Vector2.Zero, viewport.Size - windowSize));
            ImGui.SetNextWindowPos(location, ImGuiCond.Always);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing;
            if (toolbarLocked) flags |= ImGuiWindowFlags.NoBackground;
            if (sortRunning || affinityTransferRunning) flags |= ImGuiWindowFlags.NoInputs;
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4));
            if (ExileCenter.Ui.ImGuiTheme.BeginWindow(merchant ? "##sortMerchantStash" : "##sortCurrentStash", flags))
            {
                if (Settings.ShowStashSortButton)
                {
                    ImGui.BeginDisabled(!canSort);
                    if (ImGui.Button("##sort", new Vector2(28))) BeginGridSort(true);
                    DrawSortIcon();
                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && ImGui.BeginTooltip())
                    {
                        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
                        ImGui.TextUnformatted(PluginText.T("stashutility.sort.stash_tooltip", "Sort the open stash tab (normal or quad). Specialized slots are arranged automatically by the game."));
                        ImGui.PopTextWrapPos();
                        ImGui.EndTooltip();
                    }
                }
                if (Settings.EnableTabletManager)
                {
                    if (Settings.ShowStashSortButton) ImGui.SameLine();
                    DrawTabletVisibilityButton(true);
                }
                ImGui.SameLine();
                bool wasLocked = toolbarLocked;
                DrawToolbarPositionControls(anchor, location, windowSize, ref toolbarOffset, ref toolbarLocked);
                if (!hasGridBounds && wasLocked != toolbarLocked)
                {
                    // Specialized tabs use a fixed left anchor. Keep the right edge in place
                    // when the 32-pixel move button is hidden or shown, just like normal tabs.
                    toolbarOffset.X = location.X - anchor.X + (toolbarLocked ? 32 : -32);
                    toolbarOffset.Y = location.Y - anchor.Y;
                    SaveSettings();
                }
            }
            ImGui.End(); ImGui.PopStyleVar(2);
        }

        private void DrawAffinityTransferFeedback()
        {
            if (Core.IsSettingsMenuOpen || (!affinityTransferRunning && !sortRunning && DateTime.UtcNow > affinityFeedbackUntil) ||
                !Core.Process.Foreground || string.IsNullOrEmpty(affinityTransferStatus)) return;
            bool compact = !affinityTransferRunning && !sortRunning && !string.IsNullOrEmpty(affinityCompletionSummary) &&
                affinityFailed == 0 && affinityUnrouted == 0 && string.IsNullOrEmpty(affinityRouteIssue);
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos + new Vector2(viewport.Size.X / 2f, 25), ImGuiCond.Always, new Vector2(0.5f, 0));
            ImGui.SetNextWindowSize(new Vector2(Math.Min(compact ? 360f : 650f, Math.Max(250f, viewport.Size.X - 40f)), 0), ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(compact ? 0.72f : 0.85f);
            if (ExileCenter.Ui.ImGuiTheme.BeginWindow("##stashAffinityFeedback", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.SetWindowFontScale(compact ? 0.9f : 1f);
                ImGui.TextWrapped(sortRunning ? sortStatus : affinityTransferRunning && !affinityTransferWaiting
                    ? PluginText.F("stashutility.transfer.affinity_progress", "Transfer in progress: {0} stack(s) moved. Escape stops the transfer.", affinityTransferred)
                    : compact ? affinityCompletionSummary : affinityTransferStatus);
            }
            ImGui.End();
        }

        private void ReleaseAffinityControl()
        {
            if (affinityShiftHeld && AffinitySendInput(1, new[] { AffinityKeyInput(0x0002, 0x10) }, Marshal.SizeOf<AffinityInput>()) == 1)
                affinityShiftHeld = false;
            if (!affinityCtrlHeld) return;
            var keyUp = new[] { AffinityKeyInput(0x0002) };
            if (AffinitySendInput(1, keyUp, Marshal.SizeOf<AffinityInput>()) == 1) affinityCtrlHeld = false;
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct AffinityMouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct AffinityKey { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] private struct AffinityInputData
        {
            [FieldOffset(0)] public AffinityMouse Mouse;
            [FieldOffset(0)] public AffinityKey Key;
        }
        [StructLayout(LayoutKind.Sequential)] private struct AffinityInput { public uint Type; public AffinityInputData Data; }
        private static AffinityInput AffinityKeyInput(uint flags, ushort key = 0x11) => new() { Type = 1,
            Data = new AffinityInputData { Key = new AffinityKey { Key = key, Flags = flags } } };
        private static AffinityInput AffinityMouseInput(uint flags) => new() {
            Data = new AffinityInputData { Mouse = new AffinityMouse { Flags = flags } } };
        [DllImport("user32.dll", EntryPoint = "SendInput")] private static extern uint AffinitySendInput(uint count, AffinityInput[] inputs, int size);
        [DllImport("user32.dll", EntryPoint = "SetCursorPos")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AffinitySetCursorPos(int x, int y);
        [DllImport("user32.dll", EntryPoint = "GetCursorPos")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AffinityGetCursorPos(out NativePoint point);
        [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")] private static extern short AffinityGetAsyncKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern IntPtr AffinityGetForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AffinitySetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")] private static extern uint AffinityGetWindowThreadProcessId(IntPtr window, out uint pid);

        private List<DetectedStashTab> GetDestinationTabs(DetectedStashTab selected, List<DetectedStashTab> available = null)
        {
            // The folder defines the destination group; keep original names in every language.
            // Never combine root tabs or tabs from a different folder with the same name.
            if (selected.Folders.Count == 0 || GetTabPriority(selected.Name) == null)
                return new List<DetectedStashTab> { selected };

            var numberedTabs = (available ?? detectedStashTabs)
                .Where(tab => tab.Folders.SequenceEqual(selected.Folders) && GetTabPriority(tab.Name) != null)
                .OrderBy(tab => GetTabPriority(tab.Name)).ToList();
            // Two tabs with the same number do not define an unambiguous overflow order.
            if (numberedTabs.GroupBy(tab => GetTabPriority(tab.Name)).Any(group => group.Count() > 1))
                return new List<DetectedStashTab> { selected };
            return numberedTabs;
        }

        private static int? GetTabPriority(string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name ?? string.Empty, @"([0-9]+)\s*$");
            return match.Success && int.TryParse(match.Groups[1].Value, out var number) && number > 0
                ? number : null;
        }

        private void SetDetectedDestination(StashDestination destination, DetectedStashTab selected, bool numbered = true, List<DetectedStashTab> available = null)
        {
            destination.Disabled = false;
            var tabs = numbered ? GetDestinationTabs(selected, available) : new List<DetectedStashTab> { selected };
            destination.TabName = tabs[0].Name;
            destination.SetFolders(selected.Folders);
            destination.OverflowTabNames = tabs.Skip(1).Select(tab => tab.Name).ToList();
        }

        private void BeginStashScan()
        {
            if (sortRunning || affinityTransferRunning) return;
            stashScanQueue.Clear();
            detectedStashTabs.Clear();
            transferDestinationChecks.Clear();
            stashScanNodeCount = 0;
            stashScanStatus = PluginText.T("stashutility.transfer.scan_unavailable", "Open your stash in keyboard/mouse mode before scanning.");
            try
            {
                var gameUi = Core.States.InGameStateObject.GameUi;
                if (gameUi == null || Core.GHSettings.EnableControllerMode ||
                    (handleObj == null && !InitReflection()) || !IsElementVisible(gameUi.LeftPanel.Address)) return;

                stashScanRoot = ResolvePath(gameUi.LeftPanel.Address, new int[] { 2, 0, 0, 0, 1 });
                if (!IsElementVisible(stashScanRoot) ||
                    !TryReadUiString(stashScanRoot, 0x98, out var id) || id != "top_level_tabs") return;
                DiscoverTabs(stashScanRoot, new List<string>(), new HashSet<IntPtr>());
                stashScanStatus = PluginText.T("stashutility.transfer.scan_progress", "Reading loaded stash contents. Keep the stash open.");
                if (stashScanQueue.Count == 0) CompleteStashScan();
            }
            catch (Exception ex)
            {
                CancelStashScan(ex.GetBaseException().Message);
                Console.WriteLine($"[StashUtility] Stash scan failed: {ex.Message}");
            }

            void DiscoverTabs(IntPtr header, List<string> folders, HashSet<IntPtr> visited)
            {
                if (folders.Count > 8 || !visited.Add(header) || visited.Count > 256)
                    throw new InvalidOperationException(PluginText.T("stashutility.transfer.scan_folder_limit", "Too many nested folders or a repeated folder."));
                if (!TryReadStashEntries(header, out var entries, out var failure))
                    throw new InvalidOperationException(PluginText.F("stashutility.transfer.scan_folder_error", "Folder: {0}. {1}",
                        folders.Count == 0 ? PluginText.T("stashutility.transfer.root", "Stash Root") : string.Join(" / ", folders), failure));
                foreach (var entry in GetStashEntriesInDisplayOrder(header, entries))
                {
                    var nestedHeader = ResolvePath(entry.page, new int[] { 0, 1 });
                    var nestedButtons = ResolvePath(nestedHeader, new int[] { 0 });
                    if (TryReadUiString(nestedButtons, 0x98, out var buttonsId) && buttonsId == "buttons_container")
                    {
                        DiscoverTabs(nestedHeader, new List<string>(folders) { entry.name }, visited);
                        continue;
                    }
                    if (detectedStashTabs.Count >= 512) throw new InvalidOperationException("Too many stash tabs.");
                    var tab = new DetectedStashTab { Name = entry.name, Page = entry.page, Folders = new List<string>(folders) };
                    detectedStashTabs.Add(tab);
                    tab.UiAddresses.Add(tab.Page);
                    stashScanQueue.Enqueue((tab, tab.Page, 0));
                }
            }
        }

        private void ResetStashScanConfiguration()
        {
            stashScanQueue.Clear();
            detectedStashTabs.Clear();
            transferDestinationChecks.Clear();
            stashBrowserFolders.Clear();
            stashBrowserTab = IntPtr.Zero;
            stashScanRoot = IntPtr.Zero;
            stashScanNodeCount = 0;
            stashScanStatus = string.Empty;
            Settings.TransferDestinations = new Dictionary<string, StashDestination>();
            Settings.ScannedStashTabs = new List<StashDestination>();
            SaveSettings();
        }

        private void CancelStashScan(string reason = null)
        {
            stashScanQueue.Clear();
            detectedStashTabs.Clear();
            stashScanStatus = PluginText.T("stashutility.transfer.scan_failed", "Scan interrupted: the stash changed, closed, or could not be read. Destinations were not changed.");
            if (!string.IsNullOrEmpty(reason)) stashScanStatus += "\n" + reason;
        }

        private void UpdateStashScan()
        {
            if (stashScanQueue.Count == 0) return;
            try
            {
                var gameUi = Core.States.InGameStateObject.GameUi;
                if (gameUi == null || Core.GHSettings.EnableControllerMode || !IsElementVisible(gameUi.LeftPanel.Address) ||
                    ResolvePath(gameUi.LeftPanel.Address, new int[] { 2, 0, 0, 0, 1 }) != stashScanRoot || !IsElementVisible(stashScanRoot))
                {
                    CancelStashScan();
                    return;
                }

                // Spread memory reads across frames; inactive pages may contain cached items.
                for (int i = 0; i < 4 && stashScanQueue.Count > 0; i++)
                {
                    var node = stashScanQueue.Dequeue();
                    if (++stashScanNodeCount > 20000 || !TryReadMemory<UiElementBaseOffset>(node.address, out var offset) || offset.Self != node.address)
                        throw new InvalidOperationException("Stash UI changed during the scan.");
                    long childCount = offset.ChildrensPtr.TotalElements(IntPtr.Size);
                    if (childCount < 0 || childCount > 4096) throw new InvalidOperationException("Invalid stash UI children.");

                    if (offset.UnscaledSize.X > 0 && offset.UnscaledSize.Y > 0 &&
                        offset.UnscaledSize.X <= 512 && offset.UnscaledSize.Y <= 512)
                    {
                        var itemAddress = GetItemAddressFromElement(node.address);
                        if (itemAddress != IntPtr.Zero && !node.tab.ItemAddresses.Contains(itemAddress))
                        {
                            var item = ReadFreshItem(itemAddress);
                            if (item != null && item.IsValid && item.TryGetComponent<Base>(out var itemBase))
                            {
                                var kind = GetItemKind(item.Path);
                                if (!string.IsNullOrEmpty(kind))
                                {
                                    var rarity = item.TryGetComponent<Mods>(out var mods) ? mods.Rarity : Rarity.Normal;
                                    if (rarity == Rarity.Unique && IsEquipmentKind(kind)) kind = "UniqueEquipment";
                                    else if (rarity == Rarity.Unique && GetUniqueTabletCategory(item.Path) is string tabletCategory)
                                        kind = "UniqueTablets/" + tabletCategory;
                                    node.tab.ItemAddresses.Add(itemAddress);
                                    var itemName = string.IsNullOrEmpty(itemBase.BaseItemName) ? itemBase.InternalName : itemBase.BaseItemName;
                                    node.tab.ItemKinds.TryAdd(kind, itemName);
                                    node.tab.Items.Add((itemName, rarity));
                                }
                            }
                        }
                    }

                    if (childCount == 0) continue;
                    if (node.depth >= 16) throw new InvalidOperationException("Stash UI scan depth exceeded.");
                    foreach (var child in ReadStdVector<IntPtr>(offset.ChildrensPtr))
                        if (child != IntPtr.Zero && node.tab.UiAddresses.Add(child))
                            stashScanQueue.Enqueue((node.tab, child, node.depth + 1));
                }
                if (stashScanQueue.Count > 0) return;

                CompleteStashScan();
            }
            catch (Exception ex)
            {
                CancelStashScan(ex.GetBaseException().Message);
                Console.WriteLine($"[StashUtility] Stash scan failed: {ex.Message}");
            }
        }

        private void CompleteStashScan()
        {
            Settings.TransferDestinations ??= new Dictionary<string, StashDestination>();
            // Keep valid routes even when their contents were not loaded during this scan.
            foreach (var pair in Settings.TransferDestinations)
            {
                var destination = pair.Value;
                if (destination == null || destination.Disabled) continue;
                if (!string.IsNullOrWhiteSpace(destination.TabName))
                {
                    var folders = destination.FolderPath?.Count > 0 ? destination.FolderPath :
                        (string.IsNullOrEmpty(destination.FolderName) ? new List<string>() : new List<string> { destination.FolderName });
                    var matches = detectedStashTabs.Where(tab => tab.Name == destination.TabName &&
                        tab.Folders.SequenceEqual(folders)).ToList();
                    if (matches.Count == 1) SetDetectedDestination(destination, matches[0],
                        pair.Key != "UniqueEquipmentOverflow" && pair.Key != "UniqueTablets" && !IsUniqueTabletDestinationKey(pair.Key) && !IsRoutedGemKind(pair.Key));
                    else destination.OverflowTabNames?.Clear();
                    // Copied settings may refer to another player's stash. Rebuild missing destinations only after a successful scan.
                    if (matches.Count != 0 || detectedStashTabs.Count == 0) continue;
                }
                destination.TabName = string.Empty;
                destination.OverflowTabNames?.Clear();
                destination.SetFolders(new List<string>());
                destination.IsAutomatic = false;
            }
            AutoConfigureSpecializedDestinations(detectedStashTabs);
            var ambiguousKinds = AutoConfigureEquipmentDestinations(detectedStashTabs);
            foreach (var kind in detectedStashTabs.SelectMany(tab => tab.ItemKinds.Keys).Distinct())
            {
                if (UsesGameAffinity(kind)) continue;
                var candidates = detectedStashTabs.Where(tab => tab.ItemKinds.ContainsKey(kind)).ToList();
                var orderedTabs = GetDestinationTabs(candidates[0]);
                bool singleDestination = candidates.All(tab => orderedTabs.Contains(tab));
                if (!Settings.TransferDestinations.TryGetValue(kind, out var destination) || destination == null)
                    Settings.TransferDestinations[kind] = destination = new StashDestination();
                destination.ExampleItemName = candidates[0].ItemKinds[kind];
                if (destination.Disabled) continue;
                if (singleDestination && string.IsNullOrEmpty(destination.TabName))
                {
                    SetDetectedDestination(destination, candidates[0], !IsUniqueTabletDestinationKey(kind));
                    destination.IsAutomatic = true;
                }
                if (!singleDestination && string.IsNullOrEmpty(destination.TabName)) ambiguousKinds.Add(kind);
            }
            stashScanStatus = PluginText.F("stashutility.transfer.scan_complete", "Scan complete: {0} tab(s), {1} readable item(s), {2} item group(s) requiring a destination choice.",
                detectedStashTabs.Count, detectedStashTabs.Sum(tab => tab.ItemAddresses.Count), ambiguousKinds.Count);
            Settings.ScannedStashTabs = detectedStashTabs.Select(tab =>
            {
                var saved = new StashDestination { TabName = tab.Name };
                saved.SetFolders(tab.Folders);
                return saved;
            }).ToList();
            SaveSettings();
        }

        private void AutoConfigureSpecializedDestinations(List<DetectedStashTab> tabs)
        {
            Settings.TransferDestinations ??= new Dictionary<string, StashDestination>();
            foreach (string kind in new[] { "Gem/Skills", "Gem/Supports", "Gem/Kalguuran", "UniqueTablets" })
            {
                if (!Settings.TransferDestinations.TryGetValue(kind, out var gemDestination) || gemDestination == null)
                    Settings.TransferDestinations[kind] = gemDestination = new StashDestination();
                if (gemDestination.Disabled || !string.IsNullOrWhiteSpace(gemDestination.TabName)) continue;
                var candidates = tabs.Where(tab => IsGemDestinationName(kind, tab.Name)).ToList();
                if (candidates.Count != 1) continue;
                SetDetectedDestination(gemDestination, candidates[0], false);
                gemDestination.IsAutomatic = true;
            }
        }

        private static readonly Dictionary<string, string[]> EquipmentRouteNames = new()
        {
            ["Weapons"] = new[] { "armes principales", "armes principale", "primary weapons", "weapons" },
            ["Weapons/OneHandWeapons/OneHandSwords"] = new[] { "epees a une main", "epees 1 main", "one handed swords", "one hand swords" },
            ["Weapons/TwoHandWeapons/TwoHandSwords"] = new[] { "epees a deux mains", "epees 2 mains", "two handed swords", "two hand swords" },
            ["Weapons/OneHandWeapons/OneHandAxes"] = new[] { "haches a une main", "haches 1 main", "one handed axes", "one hand axes" },
            ["Weapons/TwoHandWeapons/TwoHandAxes"] = new[] { "haches a deux mains", "haches 2 mains", "two handed axes", "two hand axes" },
            ["Weapons/OneHandWeapons/OneHandMaces"] = new[] { "masses a une main", "masses 1 main", "one handed maces", "one hand maces" },
            ["Weapons/TwoHandWeapons/TwoHandMaces"] = new[] { "masses a deux mains", "masses 2 mains", "two handed maces", "two hand maces" },
            ["Weapons/OneHandWeapons/OneHandSpears"] = new[] { "lances", "lance", "spears", "spear" },
            ["Weapons/OneHandWeapons/Daggers"] = new[] { "dagues", "dague", "daggers", "dagger" },
            ["Weapons/OneHandWeapons/Claws"] = new[] { "griffes", "griffe", "claws", "claw" },
            ["Weapons/OneHandWeapons/Flails"] = new[] { "fleaux", "fleau", "flails", "flail" },
            ["Weapons/OneHandWeapons/Wands"] = new[] { "baguettes", "baguette", "wands", "wand" },
            ["Weapons/OneHandWeapons/Sceptres"] = new[] { "sceptres", "sceptre", "scepters", "scepter" },
            ["Weapons/TwoHandWeapons/Bows"] = new[] { "arcs", "arc", "bows", "bow" },
            ["Weapons/TwoHandWeapons/Crossbows"] = new[] { "arbaletes", "arbalete", "crossbows", "crossbow" },
            ["Weapons/TwoHandWeapons/Staves/CasterStaves"] = new[] { "batons", "baton", "batons magiques", "staves", "staff", "caster staves" },
            ["Weapons/TwoHandWeapons/Staves/Quarterstaves"] = new[] { "batons de combat", "baton de combat", "quarterstaves", "quarterstaff", "warstaves", "warstaff" },
            ["Weapons/TwoHandWeapons/Talismans"] = new[] { "talismans", "talisman" },
            ["Weapons/TwoHandWeapons/Traps"] = new[] { "outils de pieges", "outil de pieges", "trap tools", "trap tool", "traps" },
            ["Weapons/TwoHandWeapon/FishingRods"] = new[] { "cannes a peche", "canne a peche", "fishing rods", "fishing rod" },
            ["Armours/Helmets"] = new[] { "casques", "casque", "helmets", "helmet" },
            ["Armours/BodyArmours"] = new[] { "torses", "torse", "armures de torse", "body armour", "body armours", "body armor", "body armors" },
            ["Armours/Gloves"] = new[] { "mains", "gants", "gloves" },
            ["Armours/Boots"] = new[] { "pieds", "bottes", "boots", "feet" },
            ["Armours/Shields"] = new[] { "boucliers", "bouclier", "shields", "shield", "armes secondaires", "armes secondaire", "secondary weapons", "offhand", "off hand" },
            ["Armours/Focii"] = new[] { "focus", "foci", "armes secondaires", "armes secondaire", "secondary weapons", "offhand", "off hand" },
            ["Quivers"] = new[] { "carquois", "quivers", "quiver", "armes secondaires", "armes secondaire", "secondary weapons", "offhand", "off hand" },
            ["Rings"] = new[] { "bagues", "bague", "anneaux", "rings", "ring" },
            ["Amulets"] = new[] { "amulettes", "amulette", "amulets", "amulet" },
            ["Belts"] = new[] { "ceintures", "ceinture", "belts", "belt" },
            ["Charms"] = new[] { "charmes", "charme", "charms", "charm" },
            ["Flasks"] = new[] { "flacons", "flacon", "flasks", "flask" },
            ["Jewels/Sapphire"] = new[] { "sapphir", "saphir", "sapphire", "sapphires" },
            ["Jewels/Emerald"] = new[] { "emerald", "emeraude", "emeralds", "emeraudes" },
            ["Jewels/Ruby"] = new[] { "ruby", "rubis", "rubies" },
            ["Jewels/Diamond"] = new[] { "diamond", "diamant", "diamonds", "diamants" },
            ["Jewels/Sapphire/TimeLost"] = new[] { "sapphir pdt", "saphir pdt", "sapphire pdt", "time lost sapphire", "sapphire time lost" },
            ["Jewels/Emerald/TimeLost"] = new[] { "emerald pdt", "emeraude pdt", "time lost emerald", "emerald time lost" },
            ["Jewels/Ruby/TimeLost"] = new[] { "ruby pdt", "rubis pdt", "time lost ruby", "ruby time lost" },
            ["Jewels/Diamond/TimeLost"] = new[] { "diamond pdt", "diamant pdt", "time lost diamond", "diamond time lost" },
            ["UniqueEquipment"] = new[] { "legendaire", "legendaires", "collections legendaire", "collections legendaires", "uniques", "unique equipment", "legendary" },
            ["UniqueEquipmentOverflow"] = new[] { "poubelle legendaire", "poubelle legendaires", "unique overflow", "legendary overflow", "unique trash" }
        };

        private static string NormalizeRouteName(string name)
        {
            var normalized = System.Text.RegularExpressions.Regex.Replace(NormalizeModSearch(name), @"\s+", " ").Trim();
            return System.Text.RegularExpressions.Regex.Replace(normalized, @"^\d+\s+|\s+\d+$", "").Trim();
        }

        private HashSet<string> AutoConfigureEquipmentDestinations(List<DetectedStashTab> tabs)
        {
            var ambiguous = new HashSet<string>();
            Settings.TransferDestinations ??= new Dictionary<string, StashDestination>();
            foreach (var group in EquipmentRouteNames)
            {
                if (!Settings.TransferDestinations.TryGetValue(group.Key, out var destination) || destination == null)
                    Settings.TransferDestinations[group.Key] = destination = new StashDestination();
                if (destination.Disabled || !string.IsNullOrWhiteSpace(destination.TabName)) continue;
                var candidates = tabs.Where(tab => group.Value.Contains(NormalizeRouteName(tab.Name))).ToList();
                if (candidates.Count == 0 && group.Key != "UniqueEquipmentOverflow")
                    candidates = tabs.Where(tab => GetTabPriority(tab.Name) != null &&
                        tab.Folders.Any(folder => group.Value.Contains(NormalizeRouteName(folder)))).ToList();
                if (candidates.Count == 0) continue;
                bool numbered = group.Key != "UniqueEquipmentOverflow";
                var chain = numbered ? GetDestinationTabs(candidates[0], tabs) : new List<DetectedStashTab> { candidates[0] };
                if (!candidates.All(chain.Contains)) { ambiguous.Add(group.Key); continue; }
                SetDetectedDestination(destination, candidates[0], numbered, tabs);
                destination.IsAutomatic = true;
            }
            return ambiguous;
        }

        private void DiscoverSpecializedDestinations(IntPtr header)
        {
            // Resolve names only. Keep existing routes and None choices; do not rescan item contents.
            var tabs = new List<DetectedStashTab>();
            var visited = new HashSet<IntPtr>();
            if (Read(header, new List<string>()))
            {
                int configured = Settings.TransferDestinations?.Count(pair => !string.IsNullOrWhiteSpace(pair.Value?.TabName)) ?? 0;
                AutoConfigureSpecializedDestinations(tabs);
                AutoConfigureEquipmentDestinations(tabs);
                if (Settings.TransferDestinations.Count(pair => !string.IsNullOrWhiteSpace(pair.Value?.TabName)) > configured) SaveSettings();
            }
            bool Read(IntPtr current, List<string> folders)
            {
                if (folders.Count > 8 || !visited.Add(current) || visited.Count > 256 ||
                    !TryReadStashEntries(current, out var entries)) return false;
                foreach (var entry in entries)
                {
                    var nested = ResolvePath(entry.page, new[] { 0, 1 });
                    if (TryReadUiString(ResolvePath(nested, new[] { 0 }), 0x98, out var id) && id == "buttons_container")
                    {
                        if (!Read(nested, new List<string>(folders) { entry.name })) return false;
                    }
                    else tabs.Add(new DetectedStashTab { Name = entry.name, Folders = new List<string>(folders), Page = entry.page });
                    if (tabs.Count > 512) return false;
                }
                return true;
            }
        }

        private static bool IsGemDestinationName(string kind, string name)
        {
            string normalized = NormalizeModSearch(name);
            return kind switch
            {
                "Gem/Skills" => new[] { "aptitudes", "aptitude", "skills", "skill gems", "skill and spirit gems" }.Contains(normalized),
                "Gem/Supports" => new[] { "soutiens", "soutien", "supports", "support", "support gems" }.Contains(normalized),
                "Gem/Kalguuran" => new[] { "kalguuran", "kalguur", "kalguuran gems" }.Contains(normalized),
                "UniqueTablets" => new[] { "fragment", "fragments" }.Contains(normalized),
                _ => false
            };
        }

        private bool TryResolveNamedStashPage(IntPtr header, string name, out IntPtr page)
        {
            page = IntPtr.Zero;
            if (string.IsNullOrEmpty(name) || !TryReadStashEntries(header, out var entries)) return false;
            var matches = entries.Where(entry => string.Equals(entry.name, name, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1) return false;
            page = matches[0].page;
            return true;
        }

        private bool TryReadStashEntries(IntPtr header, out List<(string name, IntPtr page)> entries)
            => TryReadStashEntries(header, out entries, out _);

        private List<(string name, IntPtr page)> GetStashEntriesInDisplayOrder(IntPtr header,
            List<(string name, IntPtr page)> entries)
        {
            // Keep canonical name/page pairs intact; only the scan/browser display order changes.
            var container = ResolvePath(header, new[] { 0 });
            if (container == IntPtr.Zero || !TryReadMemory<UiElementBaseOffset>(container, out var offset) ||
                offset.Self != container) return entries;
            long count = offset.ChildrensPtr.TotalElements(IntPtr.Size);
            if (count < 0 || count > 257) return entries;
            var buttons = new List<(string name, Vector2 position)>();
            bool positionsReadable = true;
            foreach (var button in ReadStdVector<IntPtr>(offset.ChildrensPtr))
            {
                var label = ResolvePath(button, new[] { 0, 1 });
                if (!TryReadUiString(label, 0x98, out var id) || id != "label" ||
                    !TryReadUiString(label, 0x360, out var name) || !entries.Any(entry => entry.name == name)) continue;
                // Sibling buttons share a container, so their local positions give the visible order.
                bool readable = TryReadMemory<UiElementBaseOffset>(button, out var buttonOffset) && buttonOffset.Self == button;
                var position = new Vector2(buttonOffset.RelativePosition.X, buttonOffset.RelativePosition.Y);
                readable &= float.IsFinite(position.X) && float.IsFinite(position.Y);
                positionsReadable &= readable;
                buttons.Add((name, position));
            }
            // Unloaded or ambiguous button lists must not invent a destination or an order.
            if (buttons.Count != entries.Count || buttons.Select(button => button.name).Distinct().Count() != entries.Count)
                return entries;
            var ordered = positionsReadable ? buttons.OrderBy(button => button.position.Y).ThenBy(button => button.position.X).ToList() : buttons;
            var ranks = ordered.Select((button, index) => (button.name, index)).ToDictionary(button => button.name, button => button.index);
            return entries.OrderBy(entry => ranks[entry.name]).ToList();
        }

        private bool TryReadStashEntries(IntPtr header, out List<(string name, IntPtr page)> entries, out string failure)
            => TryReadStashEntries(header, out entries, out failure, out _);

        private bool TryReadStashEntries(IntPtr header, out List<(string name, IntPtr page)> entries, out string failure, out IntPtr list)
        {
            entries = new();
            list = IntPtr.Zero;
            failure = PluginText.T("stashutility.transfer.scan_list_unavailable", "The tab list or its content pages could not be read.");
            if (header == IntPtr.Zero) return false;

            // The exported UI has canonical-order dropdown entries and matching content pages.
            // Header buttons can be visually reordered, so their indices must not be used.
            var defaultList = ResolvePath(header, new int[] { 4, 2 });
            var pagesContainer = ResolvePath(header, new int[] { 1 });
            var buttonsContainer = ResolvePath(header, new int[] { 0 });
            if (pagesContainer == IntPtr.Zero || buttonsContainer == IntPtr.Zero ||
                !TryReadUiString(buttonsContainer, 0x98, out var buttonsId) || buttonsId != "buttons_container" ||
                !TryReadUiString(pagesContainer, 0x98, out var pagesId) || pagesId != "pages_container") return false;

            var pageVector = ReadMemory<UiElementBaseOffset>(pagesContainer).ChildrensPtr;
            long pageCount = pageVector.TotalElements(IntPtr.Size);
            long count = defaultList != IntPtr.Zero && TryReadMemory<UiElementBaseOffset>(defaultList, out var defaultOffset)
                ? defaultOffset.ChildrensPtr.TotalElements(IntPtr.Size) : -1;
            failure = PluginText.F("stashutility.transfer.scan_list_mismatch", "The tab list has {0} entries and {1} content pages.", count, pageCount);
            if (pageCount < 0 || pageCount > 256) return false;
            if (!TryFindStashRowList(header, defaultList, (int)pageCount, out list, out var rows, out var names))
            {
                if (count == pageCount) failure = PluginText.T("stashutility.transfer.scan_list_unavailable", "The tab list or its content pages could not be read.");
                return false;
            }
            var pages = ReadStdVector<IntPtr>(pageVector);
            if (pages.Length != pageCount) return false;

            for (int i = 0; i < rows.Length; i++)
            {
                failure = PluginText.F("stashutility.transfer.scan_tab_unavailable", "The name or content page of tab {0} could not be read.", i + 1);
                if (pages[i] == IntPtr.Zero) return false;
                entries.Add((names[i], pages[i]));
            }
            failure = string.Empty;
            return true;
        }

        private bool TryFindStashRowList(IntPtr header, IntPtr defaultList, int expectedCount, out IntPtr list, out IntPtr[] rows, out string[] names)
        {
            list = IntPtr.Zero;
            rows = Array.Empty<IntPtr>();
            names = Array.Empty<string>();
            if (IsRowList(defaultList, out var defaultRows, out var defaultNames))
            {
                list = defaultList;
                rows = defaultRows;
                names = defaultNames;
                return true;
            }
            if (expectedCount == 0) return false;

            // Scrollbars and wrappers can change the dropdown's child indices between stash layouts.
            // Search only this dropdown, and require one complete, readable list in canonical order.
            var pending = new Queue<(IntPtr address, int depth)>();
            var visited = new HashSet<IntPtr>();
            pending.Enqueue((ResolvePath(header, new int[] { 4 }), 0));
            while (pending.Count > 0)
            {
                var node = pending.Dequeue();
                if (node.address == IntPtr.Zero || !visited.Add(node.address)) continue;
                if (visited.Count > 128) return false;
                if (IsRowList(node.address, out var candidateRows, out var candidateNames))
                {
                    if (list != IntPtr.Zero) return false;
                    list = node.address;
                    rows = candidateRows;
                    names = candidateNames;
                    continue;
                }
                if (node.depth >= 4 || !TryReadMemory<UiElementBaseOffset>(node.address, out var offset) || offset.Self != node.address) continue;
                long count = offset.ChildrensPtr.TotalElements(IntPtr.Size);
                if (count < 0 || count > 256) return false;
                foreach (var child in ReadStdVector<IntPtr>(offset.ChildrensPtr)) pending.Enqueue((child, node.depth + 1));
            }
            return list != IntPtr.Zero;

            bool IsRowList(IntPtr address, out IntPtr[] candidates, out string[] candidateNames)
            {
                candidates = Array.Empty<IntPtr>();
                candidateNames = Array.Empty<string>();
                if (address == IntPtr.Zero || !TryReadMemory<UiElementBaseOffset>(address, out var offset) || offset.Self != address ||
                    offset.ChildrensPtr.TotalElements(IntPtr.Size) != expectedCount) return false;
                if (expectedCount == 0) return true;
                try
                {
                    candidates = ReadStdVector<IntPtr>(offset.ChildrensPtr);
                    if (candidates.Length != expectedCount) return false;
                    candidateNames = new string[expectedCount];
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        var row = candidates[i];
                        if (row == IntPtr.Zero || !TryReadUiString(ResolvePath(row, new int[] { 0, 1 }), 0x360, out var name) ||
                            string.IsNullOrWhiteSpace(name)) return false;
                        candidateNames[i] = name;
                    }
                    return true;
                }
                catch { return false; }
            }
        }

        private IntPtr GetExplorerRootAddress()
        {
            if (string.IsNullOrWhiteSpace(explorerRootAddressStr))
            {
                var gameUi = Core.States.InGameStateObject.GameUi;
                return gameUi != null ? gameUi.LeftPanel.Address : IntPtr.Zero;
            }

            string cleanAddr = explorerRootAddressStr.Trim();
            if (cleanAddr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                cleanAddr = cleanAddr.Substring(2);
            }

            if (IntPtr.TryParse(cleanAddr, System.Globalization.NumberStyles.HexNumber, null, out var addr))
            {
                return addr;
            }

            return IntPtr.Zero;
        }

        private bool IsElementVisible(IntPtr address)
        {
            if (address == IntPtr.Zero) return false;
            var current = address;
            int depth = 0;
            while (current != IntPtr.Zero && depth < 20)
            {
                var off = ReadMemory<UiElementBaseOffset>(current);
                if (off.Self != IntPtr.Zero && off.Self != current)
                {
                    return false;
                }
                if (!UiElementBaseFuncs.IsVisibleChecker(off.Flags))
                {
                    return false;
                }
                current = off.ParentPtr;
                depth++;
            }
            return true;
        }

        private bool IsTabVisible(IntPtr address)
        {
            if (address == IntPtr.Zero) return false;
            var off = ReadMemory<UiElementBaseOffset>(address);
            if (off.Self != IntPtr.Zero && off.Self != address)
            {
                return false;
            }
            return UiElementBaseFuncs.IsVisibleChecker(off.Flags);
        }

        private bool IsActiveTab(IntPtr tab, out string tabType)
        {
            // IMPORTANT: Do NOT call CreateUiElement anywhere in this method.
            // CreateUiElement calls parents.AddIfNotExists() on its parent pointer, which contaminates
            // the shared uiParentsObj parent cache. When item slots are later created with CreateUiElement
            // they walk the poisoned cache and compute wrong absolute positions => overlay offset.
            // We check visibility and size exclusively through raw memory (UiElementBaseOffset).

            tabType = "Unknown";
            if (tab == IntPtr.Zero) return false;

            // First check if the tab itself is visible
            if (!IsElementVisible(tab)) return false;

            // Helper: check if a resolved address is visible with non-zero UnscaledSize.X
            bool HasNonZeroSize(IntPtr addr)
            {
                if (addr == IntPtr.Zero) return false;
                var off = ReadMemory<UiElementBaseOffset>(addr);
                return UiElementBaseFuncs.IsVisibleChecker(off.Flags) && off.UnscaledSize.X > 0f;
            }

            // 1. Check Waystone tab: tab -> 0 -> 1 has 16 children (tiers)
            var child01 = ResolvePath(tab, new int[] { 0, 1 });
            if (child01 != IntPtr.Zero)
            {
                var off01 = ReadMemory<UiElementBaseOffset>(child01);
                var kids01 = ReadStdVector<IntPtr>(off01.ChildrensPtr);
                if (kids01.Length == 16 && HasNonZeroSize(child01))
                {
                    tabType = "Waystone";
                    return true;
                }
            }

            // 2. Check Fragment tab: tab -> 0 -> 0 -> 0 -> 1 has 6 children (pages)
            var childFrag = ResolvePath(tab, new int[] { 0, 0, 0, 1 });
            if (childFrag != IntPtr.Zero)
            {
                var offFrag = ReadMemory<UiElementBaseOffset>(childFrag);
                var kidsFrag = ReadStdVector<IntPtr>(offFrag.ChildrensPtr);
                if (kidsFrag.Length == 6 && HasNonZeroSize(childFrag))
                {
                    tabType = "Fragment";
                    return true;
                }
            }

            // 4. Check normal grid: tab -> 0 -> 0 visible, non-zero size, and has item slots
            var child00 = ResolvePath(tab, new int[] { 0, 0 });
            if (child00 != IntPtr.Zero && HasNonZeroSize(child00))
            {
                var off00 = ReadMemory<UiElementBaseOffset>(child00);
                var slots00 = ReadStdVector<IntPtr>(off00.ChildrensPtr);
                if (slots00.Length > 0 && slots00.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
                {
                    tabType = "Normal/Quad (0->0)";
                    return true;
                }
            }

            // 5. Normal grid flat: tab -> 0 directly
            var child0 = ResolvePath(tab, new int[] { 0 });
            if (child0 != IntPtr.Zero && HasNonZeroSize(child0))
            {
                var off0 = ReadMemory<UiElementBaseOffset>(child0);
                var slots = ReadStdVector<IntPtr>(off0.ChildrensPtr);
                if (slots.Length > 0 && slots.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
                {
                    tabType = "Normal/Quad (Flat 0)";
                    return true;
                }
            }

            // 6. Normal grid self: tab directly
            var offSelf = ReadMemory<UiElementBaseOffset>(tab);
            var slotsSelf = ReadStdVector<IntPtr>(offSelf.ChildrensPtr);
            if (slotsSelf.Length > 0 && slotsSelf.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
            {
                tabType = "Normal/Quad (Self)";
                return true;
            }

            return false;
        }

        public override void DrawUI()
        {
            pendingItemHighlights.Clear();
            tabletItemHovered = false;
            tabletTooltipVisible = false;
            UpdateGridSort();
            UpdateAffinityTransfer();
            DrawInventoryTransferButton();
            DrawStashSortButton();
            DrawAffinityTransferFeedback();
            UpdateStashScan();
            // Transfers validate live item positions directly; decorative scans need not
            // compete with those checks. Resume highlighting as soon as the transfer ends.
            if (affinityTransferRunning) return;
            if (!Settings.EnableWaystoneManager && !Settings.EnableTabletManager && !Settings.EnableJewelManager && !Settings.EnableDebugProbe && !Settings.EnableMerchantPurchasePanel) return;

            if (!Settings.ShowOverlayInBackground && !Core.Process.Foreground)
            {
                return;
            }

            if (this.handleObj == null)
            {
                InitReflection();
                if (this.handleObj == null) return;
            }

            var gameUi = Core.States.InGameStateObject.GameUi;
            if (gameUi == null || gameUi.Address == IntPtr.Zero) return;

            // Update the parent caches every frame to refresh stale parent positions/coordinates.
            // This is extremely fast (parallel memory reads) and avoids GC allocations from clearing the cache.
            if (this.updateCacheMethod != null)
            {
                if (this.uiParentsObj != null) this.updateCacheMethod.Invoke(this.uiParentsObj, null);
                if (this.uiParentsObjP2 != null) this.updateCacheMethod.Invoke(this.uiParentsObjP2, null);
            }

            if (Settings.EnableDebugProbe)
            {
                DrawDebugOverlay();
            }

            if (Settings.EnableWaystoneManager || Settings.EnableTabletManager || Settings.EnableJewelManager)
            {
                bool isCoopMode = Core.GHSettings.EnableControllerMode && gameUi.LeftPanel.Address != IntPtr.Zero;

                if (isCoopMode)
                {
                    coopDebug.IsCoopMode = true;
                    coopDebug.P1LeftPanel = gameUi.LeftPanel.Address;
                    coopDebug.P2RightPanel = gameUi.RightPanel.Address;

                    // Player 1 (LeftPanel)
                    if (gameUi.LeftPanel.Address != IntPtr.Zero && IsElementVisible(gameUi.LeftPanel.Address))
                    {
                        this.currentUiParentsObj = this.uiParentsObj;

                        // 1. Player 1 inventory: LeftPanel -> 3 -> 0 -> 0 -> 1 -> 0 -> 2
                        // Check inventory panel container (3 -> 0 -> 0 -> 1) is visible first
                        var invPanelContainerP1 = ResolvePath(gameUi.LeftPanel.Address, new int[] { 3, 0, 0, 1 });
                        var inventoryGridRoot = (invPanelContainerP1 != IntPtr.Zero && IsElementVisible(invPanelContainerP1))
                            ? ResolvePath(invPanelContainerP1, new int[] { 0, 2 })
                            : IntPtr.Zero;
                        if (inventoryGridRoot != IntPtr.Zero)
                        {
                            ProcessNormalTab(inventoryGridRoot);
                        }

                        // 2. Player 1 stash tabs: LeftPanel -> 3 -> 0 -> 0 -> 0 -> 2
                        // Check the stash panel container (3 -> 0 -> 0 -> 0) is visible before processing stash tabs
                        var stashPanelContainerP1 = ResolvePath(gameUi.LeftPanel.Address, new int[] { 3, 0, 0, 0 });
                        var stashTabsContainer = (stashPanelContainerP1 != IntPtr.Zero && IsElementVisible(stashPanelContainerP1))
                            ? ResolvePath(stashPanelContainerP1, new int[] { 2 })
                            : IntPtr.Zero;
                        if (stashTabsContainer != IntPtr.Zero)
                        {
                            ProcessCoopStashTabs(stashTabsContainer, 1);
                        }
                        else
                        {
                            coopDebug.P1StashTabsContainer = IntPtr.Zero;
                            coopDebug.P1Status = stashPanelContainerP1 == IntPtr.Zero
                                ? "Stash panel container path resolution failed (LeftPanel -> 3 -> 0 -> 0 -> 0)"
                                : "Stash panel is not visible (closed)";
                        }
                    }
                    else
                    {
                        coopDebug.P1Status = gameUi.LeftPanel.Address == IntPtr.Zero ? "LeftPanel address is null" : "LeftPanel is not visible";
                    }

                    // Player 2 (RightPanel)
                    if (gameUi.RightPanel.Address != IntPtr.Zero && IsElementVisible(gameUi.RightPanel.Address))
                    {
                        this.currentUiParentsObj = this.uiParentsObjP2;

                        // 1. Player 2 inventory: RightPanel -> 3 -> 0 -> 0 -> 1 -> 0 -> 2
                        // Check inventory panel container (3 -> 0 -> 0 -> 1) is visible first
                        var invPanelContainerP2 = ResolvePath(gameUi.RightPanel.Address, new int[] { 3, 0, 0, 1 });
                        var inventoryGridRoot = (invPanelContainerP2 != IntPtr.Zero && IsElementVisible(invPanelContainerP2))
                            ? ResolvePath(invPanelContainerP2, new int[] { 0, 2 })
                            : IntPtr.Zero;
                        if (inventoryGridRoot != IntPtr.Zero)
                        {
                            ProcessNormalTab(inventoryGridRoot);
                        }

                        // 2. Player 2 stash tabs: RightPanel -> 3 -> 0 -> 0 -> 0 -> 2
                        // Check the stash panel container (3 -> 0 -> 0 -> 0) is visible before processing stash tabs
                        var stashPanelContainerP2 = ResolvePath(gameUi.RightPanel.Address, new int[] { 3, 0, 0, 0 });
                        var stashTabsContainerP2 = (stashPanelContainerP2 != IntPtr.Zero && IsElementVisible(stashPanelContainerP2))
                            ? ResolvePath(stashPanelContainerP2, new int[] { 2 })
                            : IntPtr.Zero;
                        if (stashTabsContainerP2 != IntPtr.Zero)
                        {
                            ProcessCoopStashTabs(stashTabsContainerP2, 2);
                        }
                        else
                        {
                            coopDebug.P2StashTabsContainer = IntPtr.Zero;
                            coopDebug.P2Status = stashPanelContainerP2 == IntPtr.Zero
                                ? "Stash panel container path resolution failed (RightPanel -> 3 -> 0 -> 0 -> 0)"
                                : "Stash panel is not visible (closed)";
                        }
                    }
                    else
                    {
                        coopDebug.P2Status = gameUi.RightPanel.Address == IntPtr.Zero ? "RightPanel address is null" : "RightPanel is not visible";
                    }
                }
                else
                {
                    coopDebug.IsCoopMode = false;
                    spDebug.LeftPanel = gameUi.LeftPanel.Address;
                    this.currentUiParentsObj = this.uiParentsObj;

                    // Resolve Path
                    int[] pathIndices;
                    try
                    {
                        pathIndices = Settings.PathString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(int.Parse)
                            .ToArray();
                    }
                    catch
                    {
                        lock (probeLog)
                        {
                            probeLog.Clear();
                            probeLog.Add("Error: Path String is not in a valid format (must be comma-separated integers)");
                        }
                        return;
                    }

                    int[] stashTabsContainerPath;
                    if (pathIndices.Length >= 12 && pathIndices[pathIndices.Length - 4] == 0 && pathIndices[pathIndices.Length - 3] == 0 && pathIndices[pathIndices.Length - 2] == 0 && pathIndices[pathIndices.Length - 1] == 1)
                    {
                        // Fragment stash tab layout (e.g. 2,0,0,0,0,1,1,40,0,0,0,1)
                        stashTabsContainerPath = pathIndices.Take(pathIndices.Length - 5).ToArray();
                    }
                    else if (pathIndices.Length >= 9 && pathIndices[pathIndices.Length - 2] == 0 && pathIndices[pathIndices.Length - 1] == 1)
                    {
                        // Waystone stash tab layout (e.g. 2,0,0,0,1,1,45,0,1)
                        stashTabsContainerPath = pathIndices.Take(pathIndices.Length - 3).ToArray();
                    }
                    else
                    {
                        // Fallback / default behavior
                        stashTabsContainerPath = pathIndices.Length >= 6 
                            ? pathIndices.Take(6).ToArray() 
                            : new int[] { 2, 0, 0, 0, 1, 1 };
                    }

                    spDebug.StashTabsPath = string.Join(",", stashTabsContainerPath);
                    var stashTabsContainer = ResolvePath(gameUi.LeftPanel.Address, stashTabsContainerPath);
                    if (stashTabsContainer == IntPtr.Zero)
                    {
                        // Fallback 1: Try default waystone/normal stash tabs container path (2,0,0,0,1,1)
                        var fbPath1 = new int[] { 2, 0, 0, 0, 1, 1 };
                        stashTabsContainer = ResolvePath(gameUi.LeftPanel.Address, fbPath1);
                        if (stashTabsContainer != IntPtr.Zero)
                        {
                            stashTabsContainerPath = fbPath1;
                            spDebug.StashTabsPath = string.Join(",", fbPath1) + " (Fallback Waystone/Normal)";
                        }
                        else
                        {
                            // Fallback 2: Try default fragment stash tabs container path (2,0,0,0,0,1,1)
                            var fbPath2 = new int[] { 2, 0, 0, 0, 0, 1, 1 };
                            stashTabsContainer = ResolvePath(gameUi.LeftPanel.Address, fbPath2);
                            if (stashTabsContainer != IntPtr.Zero)
                            {
                                stashTabsContainerPath = fbPath2;
                                spDebug.StashTabsPath = string.Join(",", fbPath2) + " (Fallback Fragment)";
                            }
                        }
                    }

                    if (stashTabsContainer != IntPtr.Zero)
                    {
                        ProcessStashTabs(stashTabsContainer);
                    }
                    else
                    {
                        spDebug.StashTabsContainer = IntPtr.Zero;
                        spDebug.Status = $"StashTabsContainer path resolution failed for path: {spDebug.StashTabsPath}";
                    }

                    // 3. Process Character Inventory Panel if open: RightPanel -> 5 -> 36
                    if (gameUi.RightPanel.Address != IntPtr.Zero && IsElementVisible(gameUi.RightPanel.Address))
                    {
                        var inventoryPanelSP = ResolvePath(gameUi.RightPanel.Address, new int[] { 5 });
                        var inventoryGridRoot = (inventoryPanelSP != IntPtr.Zero && IsElementVisible(inventoryPanelSP))
                            ? ResolvePath(inventoryPanelSP, new int[] { 36 })
                            : IntPtr.Zero;
                        if (inventoryGridRoot != IntPtr.Zero)
                        {
                            ProcessNormalTab(inventoryGridRoot);
                        }
                    }
                }
            }

            // 4. Process Merchant Purchase Panel if open
            if (Settings.EnableMerchantPurchasePanel)
            {
                var merchantPanelParent = ResolvePath(gameUi.Address, new int[] { 80, 8, 1 });
                if (merchantPanelParent != IntPtr.Zero)
                {
                    var parentOff = ReadMemory<UiElementBaseOffset>(merchantPanelParent);
                    var tabs = ReadStdVector<IntPtr>(parentOff.ChildrensPtr);

                    IntPtr activeTabAddr = IntPtr.Zero;
                    foreach (var tab in tabs)
                    {
                        if (tab == IntPtr.Zero) continue;
                        if (IsElementVisible(tab))
                        {
                            activeTabAddr = tab;
                            break;
                        }
                    }

                    if (activeTabAddr != IntPtr.Zero)
                    {
                        var itemsContainer = ResolvePath(activeTabAddr, new int[] { 0 });
                        if (itemsContainer != IntPtr.Zero)
                        {
                            ProcessNormalTab(itemsContainer);
                        }
                    }
                }
            }
            UpdateTabletOcclusions(gameUi.Address);
            FlushItemHighlights();
        }

        private void ProcessWaystoneTab(IntPtr[] tierKids)
        {
            foreach (var tierKid in tierKids)
            {
                if (tierKid == IntPtr.Zero) continue;

                var tierOff = ReadMemory<UiElementBaseOffset>(tierKid);
                if (!UiElementBaseFuncs.IsVisibleChecker(tierOff.Flags)) continue;

                // Go down: tierKid -> 0 -> 1
                var tierKidKids = ReadStdVector<IntPtr>(tierOff.ChildrensPtr);
                if (tierKidKids.Length <= 0 || tierKidKids[0] == IntPtr.Zero) continue;

                var child0Off = ReadMemory<UiElementBaseOffset>(tierKidKids[0]);
                var child0Kids = ReadStdVector<IntPtr>(child0Off.ChildrensPtr);
                if (child0Kids.Length <= 1 || child0Kids[1] == IntPtr.Zero) continue;

                var pagesContainer = child0Kids[1];
                var pagesContainerOff = ReadMemory<UiElementBaseOffset>(pagesContainer);
                var pages = ReadStdVector<IntPtr>(pagesContainerOff.ChildrensPtr);

                foreach (var page in pages)
                {
                    if (page == IntPtr.Zero) continue;

                    var pageOff = ReadMemory<UiElementBaseOffset>(page);
                    if (!UiElementBaseFuncs.IsVisibleChecker(pageOff.Flags)) continue;

                    var pageKids = ReadStdVector<IntPtr>(pageOff.ChildrensPtr);
                    if (pageKids.Length == 0) continue;

                    var slotContainer = pageKids[0];
                    if (slotContainer == IntPtr.Zero) continue;

                    var containerOff = ReadMemory<UiElementBaseOffset>(slotContainer);
                    var slots = ReadStdVector<IntPtr>(containerOff.ChildrensPtr);

                    foreach (var slot in slots)
                    {
                        if (slot == IntPtr.Zero) continue;

                        var slotOff = ReadMemory<UiElementBaseOffset>(slot);
                        if (!UiElementBaseFuncs.IsVisibleChecker(slotOff.Flags)) continue;

                        // Retrieve screen bounds for slot
                        var el = PluginUiElementReflection.CreateUiElement(slot, this.currentUiParentsObj);
                        if (el == null) continue;

                        var pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(el)!;
                        var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(el)!;
                        if (size.X <= 0f || pos == Vector2.Zero) continue;

                        // Scan slot for item entity
                        var itemAddr = GetHighlightItemAddress(slot);
                        if (itemAddr != IntPtr.Zero)
                        {
                            var item = ReadFreshItem(itemAddr);
                            if (item != null)
                            {
                                QueueItemHighlight(item, pos, size);
                            }
                        }
                    }
                }
            }
        }

        private void ProcessFragmentTabletsTab(IntPtr[] pages)
        {
            foreach (var page in pages)
            {
                if (page == IntPtr.Zero) continue;

                var pageOff = ReadMemory<UiElementBaseOffset>(page);
                if (!UiElementBaseFuncs.IsVisibleChecker(pageOff.Flags)) continue;

                // From page, go -> 0 -> 0 to get the slots container
                var slotContainer = ResolvePath(page, new int[] { 0, 0 });
                if (slotContainer == IntPtr.Zero) continue;

                var containerOff = ReadMemory<UiElementBaseOffset>(slotContainer);
                var slots = ReadStdVector<IntPtr>(containerOff.ChildrensPtr);

                foreach (var slot in slots)
                {
                    if (slot == IntPtr.Zero) continue;

                    var slotOff = ReadMemory<UiElementBaseOffset>(slot);
                    if (!UiElementBaseFuncs.IsVisibleChecker(slotOff.Flags)) continue;

                    // Retrieve screen bounds for slot
                    var el = PluginUiElementReflection.CreateUiElement(slot, this.currentUiParentsObj);
                    if (el == null) continue;

                    var pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(el)!;
                    var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(el)!;
                    if (size.X <= 0f || pos == Vector2.Zero) continue;

                    // Scan slot for item entity
                    var itemAddr = GetHighlightItemAddress(slot);
                    if (itemAddr != IntPtr.Zero)
                    {
                        var item = ReadFreshItem(itemAddr);
                        if (item != null)
                        {
                            QueueItemHighlight(item, pos, size);
                        }
                    }
                }
            }
        }

        private void ProcessNormalTab(IntPtr gridRoot)
        {
            var gridOffsets = ReadMemory<UiElementBaseOffset>(gridRoot);
            var slots = ReadStdVector<IntPtr>(gridOffsets.ChildrensPtr);

            foreach (var slot in slots)
            {
                if (slot == IntPtr.Zero) continue;

                var slotOff = ReadMemory<UiElementBaseOffset>(slot);
                if (!UiElementBaseFuncs.IsVisibleChecker(slotOff.Flags)) continue;

                // Retrieve screen bounds for slot
                var el = PluginUiElementReflection.CreateUiElement(slot, this.currentUiParentsObj);
                if (el == null) continue;

                var pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(el)!;
                var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(el)!;
                if (size.X <= 0f || pos == Vector2.Zero) continue;

                // Scan slot for item entity
                var itemAddr = GetHighlightItemAddress(slot);
                if (itemAddr != IntPtr.Zero)
                {
                    var item = ReadFreshItem(itemAddr);
                    if (item != null)
                    {
                        QueueItemHighlight(item, pos, size);
                    }
                }
            }
        }

        private void ProcessVisibleStashItems(IntPtr page)
        {
            if (!IsElementVisible(page)) return;
            var visited = new HashSet<IntPtr>();
            var items = new HashSet<IntPtr>();
            Visit(page, 0);

            void Visit(IntPtr address, int depth)
            {
                if (address == IntPtr.Zero || depth > 18 || visited.Count >= 4096 || !visited.Add(address) ||
                    !TryReadMemory<UiElementBaseOffset>(address, out var data) ||
                    (data.Self != IntPtr.Zero && data.Self != address) || !UiElementBaseFuncs.IsVisibleChecker(data.Flags)) return;
                var itemAddress = GetHighlightItemAddress(address);
                if (itemAddress != IntPtr.Zero)
                {
                    var element = PluginUiElementReflection.CreateUiElement(address, this.currentUiParentsObj);
                    if (element == null) return;
                    var pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(element)!;
                    var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(element)!;
                    if (size.X > 0 && size.Y > 0 && items.Add(itemAddress))
                    {
                        var item = ReadFreshItem(itemAddress);
                        if (item != null) QueueItemHighlight(item, pos, size);
                    }
                    return;
                }
                long count = data.ChildrensPtr.TotalElements(IntPtr.Size);
                if (count > 0 && count <= 1024)
                    foreach (var child in ReadStdVector<IntPtr>(data.ChildrensPtr)) Visit(child, depth + 1);
            }
        }

        private void ProcessStashTabs(IntPtr stashTabsContainer)
        {
            spDebug.StashTabsContainer = stashTabsContainer;
            spDebug.ActiveTab = IntPtr.Zero;
            spDebug.Status = "No active tab found";

            if (stashTabsContainer == IntPtr.Zero) return;

            // Highlighting only needs visible content, not the dropdown names/order
            // required for sorting. Follow visible folder pages directly from the root.
            if (IsElementVisible(stashTabsContainer))
            {
                spDebug.ActiveTab = stashTabsContainer;
                spDebug.Status = "Visible stash content (including folder pages)";
                ProcessVisibleStashItems(stashTabsContainer);
                return;
            }

            var tabsOffsets = ReadMemory<UiElementBaseOffset>(stashTabsContainer);
            var tabs = ReadStdVector<IntPtr>(tabsOffsets.ChildrensPtr);

            IntPtr activeTabAddr = IntPtr.Zero;
            int activeTabIdx = -1;
            string activeTabType = "None";

            // 1. Try to find the tab that is visible and has active/non-zero size children
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                if (tab == IntPtr.Zero) continue;
                if (IsActiveTab(tab, out string detectedType))
                {
                    activeTabAddr = tab;
                    activeTabIdx = i;
                    activeTabType = detectedType;
                    break;
                }
            }

            // 2. Fallback to old behavior if no active tab found by size/structure
            if (activeTabAddr == IntPtr.Zero)
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    var tab = tabs[i];
                    if (tab == IntPtr.Zero) continue;
                    if (IsElementVisible(tab))
                    {
                        activeTabAddr = tab;
                        activeTabIdx = i;
                        activeTabType = "Fallback (Visible)";
                        break;
                    }
                }
            }

            spDebug.ActiveTab = activeTabAddr;
            if (activeTabAddr != IntPtr.Zero)
            {
                spDebug.Status = $"Active tab index {activeTabIdx} ({activeTabType}) at 0x{activeTabAddr.ToInt64():X}";
            }

            if (activeTabAddr != IntPtr.Zero)
            {
                // 1. Check if it's the Waystone stash tab: activeTabAddr -> 0 -> 1 has 16 children (tiers)
                bool processedAsWaystone = false;
                var waystonesTabRoot = ResolvePath(activeTabAddr, new int[] { 0, 1 });
                if (waystonesTabRoot != IntPtr.Zero)
                {
                    var waystoneOffsets = ReadMemory<UiElementBaseOffset>(waystonesTabRoot);
                    var waystoneKids = ReadStdVector<IntPtr>(waystoneOffsets.ChildrensPtr);
                    if (waystoneKids.Length == 16)
                    {
                        ProcessWaystoneTab(waystoneKids);
                        processedAsWaystone = true;
                        spDebug.Status += " | Processed: Waystone Tab";
                    }
                }

                if (!processedAsWaystone)
                {
                    // 2. Check if it's a Fragment stash tab with tablets: activeTabAddr -> 0 -> 0 -> 0 -> 1 has 6 children (pages)
                    bool processedAsFragmentTablets = false;
                    var fragmentTabletsRoot = ResolvePath(activeTabAddr, new int[] { 0, 0, 0, 1 });
                    if (fragmentTabletsRoot != IntPtr.Zero)
                    {
                        var fragmentOffsets = ReadMemory<UiElementBaseOffset>(fragmentTabletsRoot);
                        var fragmentKids = ReadStdVector<IntPtr>(fragmentOffsets.ChildrensPtr);
                        if (fragmentKids.Length == 6)
                        {
                            ProcessFragmentTabletsTab(fragmentKids);
                            processedAsFragmentTablets = true;
                            spDebug.Status += " | Processed: Fragment Tab";
                        }
                    }

                    if (!processedAsFragmentTablets)
                    {
                        bool processedAsGrid = false;

                        // Check activeTabAddr itself first
                        var selfOffsets = ReadMemory<UiElementBaseOffset>(activeTabAddr);
                        var selfKids = ReadStdVector<IntPtr>(selfOffsets.ChildrensPtr);
                        if (selfKids.Length > 0 && selfKids.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
                        {
                            ProcessNormalTab(activeTabAddr);
                            processedAsGrid = true;
                            spDebug.Status += " | Processed: Flat Grid (Self)";
                        }

                        // Next check activeTabAddr -> 0
                        if (!processedAsGrid)
                        {
                            var gridRoot0 = ResolvePath(activeTabAddr, new int[] { 0 });
                            if (gridRoot0 != IntPtr.Zero)
                            {
                                var gridOffsets = ReadMemory<UiElementBaseOffset>(gridRoot0);
                                var slots = ReadStdVector<IntPtr>(gridOffsets.ChildrensPtr);
                                if (slots.Length > 0 && slots.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
                                {
                                    ProcessNormalTab(gridRoot0);
                                    processedAsGrid = true;
                                    spDebug.Status += " | Processed: Flat Grid (0)";
                                }
                            }
                        }

                        // Next check activeTabAddr -> 0 -> 0
                        if (!processedAsGrid)
                        {
                            var normalGridRoot = ResolvePath(activeTabAddr, new int[] { 0, 0 });
                            if (normalGridRoot != IntPtr.Zero)
                            {
                                ProcessNormalTab(normalGridRoot);
                                spDebug.Status += " | Processed: Normal/Quad Tab (0->0)";
                            }
                            else
                            {
                                spDebug.Status += " | Failed: Not Waystone/Fragment, and grid resolved to null";
                            }
                        }
                    }
                }
            }
        }


        private void ProcessCoopStashTabs(IntPtr stashTabsContainer, int playerNumber)
        {
            if (playerNumber == 1)
            {
                coopDebug.P1StashTabsContainer = stashTabsContainer;
                coopDebug.P1ActiveTab = IntPtr.Zero;
                coopDebug.P1Status = "No active tab found";
            }
            else
            {
                coopDebug.P2StashTabsContainer = stashTabsContainer;
                coopDebug.P2ActiveTab = IntPtr.Zero;
                coopDebug.P2Status = "No active tab found";
            }

            if (stashTabsContainer == IntPtr.Zero) return;

            var tabsOffsets = ReadMemory<UiElementBaseOffset>(stashTabsContainer);
            var tabs = ReadStdVector<IntPtr>(tabsOffsets.ChildrensPtr);

            var debugTabsList = new List<string>();
            IntPtr activeTabAddr = IntPtr.Zero;
            int activeTabIdx = -1;

            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                if (tab == IntPtr.Zero)
                {
                    debugTabsList.Add($"[{i}] Null");
                    continue;
                }

                var off = ReadMemory<UiElementBaseOffset>(tab);
                bool localVis = UiElementBaseFuncs.IsVisibleChecker(off.Flags);
                bool fullVis = IsElementVisible(tab);

                var el = PluginUiElementReflection.CreateUiElement(tab, this.currentUiParentsObj);
                Vector2 size = Vector2.Zero;
                Vector2 pos = Vector2.Zero;
                if (el != null)
                {
                    size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(el)!;
                    pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(el)!;
                }

                debugTabsList.Add($"[{i}] 0x{tab.ToInt64():X} | LVis:{localVis} | FVis:{fullVis} | Sz:{(int)size.X}x{(int)size.Y} | Pos:{(int)pos.X},{(int)pos.Y}");

                // Try to find the active tab using IsActiveTab helper
            }

            string activeTabType = "None";

            // 1. Try to find the tab that is visible and has active/non-zero size children
            for (int i = 0; i < tabs.Length; i++)
            {
                var tab = tabs[i];
                if (tab == IntPtr.Zero) continue;
                if (IsActiveTab(tab, out string detectedType))
                {
                    activeTabAddr = tab;
                    activeTabIdx = i;
                    activeTabType = detectedType;
                    break;
                }
            }

            // 2. Fallback to old behavior if no active tab found by size/structure
            if (activeTabAddr == IntPtr.Zero)
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    var tab = tabs[i];
                    if (tab == IntPtr.Zero) continue;
                    var off = ReadMemory<UiElementBaseOffset>(tab);
                    if (UiElementBaseFuncs.IsVisibleChecker(off.Flags))
                    {
                        activeTabAddr = tab;
                        activeTabIdx = i;
                        activeTabType = "Fallback (Visible)";
                        break;
                    }
                }
            }

            if (playerNumber == 1)
            {
                coopDebug.P1TabsInfo = debugTabsList;
                coopDebug.P1ActiveTab = activeTabAddr;
                if (activeTabAddr != IntPtr.Zero)
                {
                    coopDebug.P1Status = $"Active tab index {activeTabIdx} ({activeTabType}) at 0x{activeTabAddr.ToInt64():X}";
                }
            }
            else
            {
                coopDebug.P2TabsInfo = debugTabsList;
                coopDebug.P2ActiveTab = activeTabAddr;
                if (activeTabAddr != IntPtr.Zero)
                {
                    coopDebug.P2Status = $"Active tab index {activeTabIdx} ({activeTabType}) at 0x{activeTabAddr.ToInt64():X}";
                }
            }

            if (activeTabAddr != IntPtr.Zero)
            {
                // 1. Check if the active tab itself, activeTabAddr -> 0, or activeTabAddr -> 0 -> 0 contains item slots directly.
                // In coop/controller mode, POE2 often flattens the UI tree layout, rendering item slots directly under the active tab node.
                bool processedAsGrid = false;

                // Let's check activeTabAddr itself first
                var selfOffsets = ReadMemory<UiElementBaseOffset>(activeTabAddr);
                var selfKids = ReadStdVector<IntPtr>(selfOffsets.ChildrensPtr);
                if (selfKids.Length > 0 && selfKids.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
                {
                    ProcessNormalTab(activeTabAddr);
                    processedAsGrid = true;
                    if (playerNumber == 1) coopDebug.P1Status += " | Processed: Flat Grid (Self)";
                    else coopDebug.P2Status += " | Processed: Flat Grid (Self)";
                }

                // Next check activeTabAddr -> 0
                if (!processedAsGrid)
                {
                    var coopGridRoot = ResolvePath(activeTabAddr, new int[] { 0 });
                    if (coopGridRoot != IntPtr.Zero)
                    {
                        var gridOffsets = ReadMemory<UiElementBaseOffset>(coopGridRoot);
                        var slots = ReadStdVector<IntPtr>(gridOffsets.ChildrensPtr);
                        if (slots.Length > 0 && slots.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
                        {
                            ProcessNormalTab(coopGridRoot);
                            processedAsGrid = true;
                            if (playerNumber == 1) coopDebug.P1Status += " | Processed: Flat Grid (Self->0)";
                            else coopDebug.P2Status += " | Processed: Flat Grid (Self->0)";
                        }
                    }
                }

                // Next check activeTabAddr -> 0 -> 0
                if (!processedAsGrid)
                {
                    var normalGridRoot = ResolvePath(activeTabAddr, new int[] { 0, 0 });
                    if (normalGridRoot != IntPtr.Zero)
                    {
                        var gridOffsets = ReadMemory<UiElementBaseOffset>(normalGridRoot);
                        var slots = ReadStdVector<IntPtr>(gridOffsets.ChildrensPtr);
                        if (slots.Length > 0 && slots.Any(k => k != IntPtr.Zero && GetItemAddressFromElement(k) != IntPtr.Zero))
                        {
                            ProcessNormalTab(normalGridRoot);
                            processedAsGrid = true;
                            if (playerNumber == 1) coopDebug.P1Status += " | Processed: Flat Grid (Self->0->0)";
                            else coopDebug.P2Status += " | Processed: Flat Grid (Self->0->0)";
                        }
                    }
                }

                if (!processedAsGrid)
                {
                    // 2. If not a flattened grid, check nested/special structures
                    // Check if it's the Waystone stash tab: activeTabAddr -> 0 -> 1 has 16 children (tiers)
                    bool processedAsWaystone = false;
                    var waystonesTabRoot = ResolvePath(activeTabAddr, new int[] { 0, 1 });
                    if (waystonesTabRoot != IntPtr.Zero)
                    {
                        var waystoneOffsets = ReadMemory<UiElementBaseOffset>(waystonesTabRoot);
                        var waystoneKids = ReadStdVector<IntPtr>(waystoneOffsets.ChildrensPtr);
                        if (waystoneKids.Length == 16)
                        {
                            ProcessWaystoneTab(waystoneKids);
                            processedAsWaystone = true;
                            if (playerNumber == 1) coopDebug.P1Status += " | Processed: Waystone Tab (16 tiers)";
                            else coopDebug.P2Status += " | Processed: Waystone Tab (16 tiers)";
                        }
                    }

                    if (!processedAsWaystone)
                    {
                        // Check if it's a Fragment stash tab with tablets in coop mode: activeTabAddr -> 0 -> 0 -> 0 -> 1 has 6 children (pages)
                        var fragmentTabletsRoot = ResolvePath(activeTabAddr, new int[] { 0, 0, 0, 1 });
                        if (fragmentTabletsRoot != IntPtr.Zero)
                        {
                            var fragmentOffsets = ReadMemory<UiElementBaseOffset>(fragmentTabletsRoot);
                            var fragmentKids = ReadStdVector<IntPtr>(fragmentOffsets.ChildrensPtr);
                            if (fragmentKids.Length == 6)
                            {
                                ProcessFragmentTabletsTab(fragmentKids);
                                if (playerNumber == 1) coopDebug.P1Status += " | Processed: Fragment Tab (6 pages)";
                                else coopDebug.P2Status += " | Processed: Fragment Tab (6 pages)";
                            }
                            else
                            {
                                if (playerNumber == 1) coopDebug.P1Status += $" | Failed: Path 0->0->0->1 resolved but has {fragmentKids.Length} children instead of 6";
                                else coopDebug.P2Status += $" | Failed: Path 0->0->0->1 resolved but has {fragmentKids.Length} children instead of 6";
                            }
                        }
                        else
                        {
                            if (playerNumber == 1) coopDebug.P1Status += " | Failed: Not grid, and no nested 0->0->0->1 child found";
                            else coopDebug.P2Status += " | Failed: Not grid, and no nested 0->0->0->1 child found";
                        }
                    }
                }
            }
        }


        private void DrawDebugOverlay()
        {
            if (debugHoveredCurrentElement && debugCurrentAddress != IntPtr.Zero)
            {
                DrawDebugRect(debugCurrentAddress, new Vector4(1f, 1f, 0f, 1f), -1);
            }

            if (debugHoveredAllChildren)
            {
                for (int i = 0; i < debugChildrenAddresses.Count; i++)
                {
                    DrawDebugRect(debugChildrenAddresses[i], new Vector4(1f, 0f, 0f, 1f), i);
                }
            }
            else if (debugHoveredChildIndex >= 0 && debugHoveredChildIndex < debugChildrenAddresses.Count)
            {
                DrawDebugRect(debugChildrenAddresses[debugHoveredChildIndex], new Vector4(0f, 1f, 1f, 1f), debugHoveredChildIndex);
            }
        }

        private void DrawDebugRect(IntPtr address, Vector4 color, int indexLabel)
        {
            if (address == IntPtr.Zero) return;
            try
            {
                var el = PluginUiElementReflection.CreateUiElement(address, this.currentUiParentsObj ?? this.uiParentsObj);
                if (el != null)
                {
                    var pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(el)!;
                    var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(el)!;
                    if (size.X > 0f && pos != Vector2.Zero)
                    {
                        ImGui.GetForegroundDrawList().AddRect(
                            pos,
                            pos + size,
                            ImGuiHelper.Color(color),
                            0f,
                            ImDrawFlags.RoundCornersNone,
                            3.0f
                        );

                        if (indexLabel >= 0)
                        {
                            ImGui.GetForegroundDrawList().AddText(pos, ImGuiHelper.Color(color), $"{indexLabel}");
                        }
                    }
                }
            }
            catch
            {
                // Ignored
            }
        }

        private static bool MatchesModId(string rawId, string id)
        {
            if (string.IsNullOrEmpty(rawId) || string.IsNullOrEmpty(id)) return false;
            if (rawId.Equals(id, StringComparison.OrdinalIgnoreCase)) return true;
            if (char.IsDigit(id[^1])) return false;
            return rawId.StartsWith(id, StringComparison.OrdinalIgnoreCase) && rawId.Length > id.Length &&
                rawId.AsSpan(id.Length).IndexOfAnyExceptInRange('0', '9') < 0;
        }

        private static bool IsJewelModMatch(string gameModName, Models.JewelMod def) => def != null && MatchesModId(gameModName, def.GameModId);

        private static bool PassesModRoll(float value0, float value1, int index, float minimum, float divisor = 1)
        {
            if (index < 0 || index > 1) return false;
            float value = index == 0 ? value0 : value1;
            return float.IsFinite(value) && divisor > 0 && Math.Abs(value) / divisor + .00001f >= minimum;
        }

        private static int CountMatchingJewelMods(string category, List<string> patterns, Dictionary<string, float> rolls, List<(string name, (float v0, float v1) vals)> rawMods)
        {
            if (patterns == null) return 0;
            int count = 0;
            foreach (var def in Data.ModDatabase.AllJewelMods.Where(m => m.Category == category && patterns.Contains(m.Id)))
            {
                float minimum = rolls != null && rolls.TryGetValue(def.Id, out float saved) ? saved : def.MinRoll;
                if (rawMods.Any(raw => IsJewelModMatch(raw.name, def) && (def.MinRoll == def.MaxRoll || PassesModRoll(raw.vals.v0, raw.vals.v1, def.RollValueIndex, minimum, def.RollDivisor)))) count++;
            }
            return count;
        }

        private void UpdateTabletOcclusions(IntPtr uiRoot)
        {
            tabletOcclusions.Clear();
            tabletTooltipVisible = false;
            // No item is being drawn while fighting or while the panels are closed.
            // Do not inspect transient game UI memory or enter a native callback then.
            if (pendingItemHighlights.Count == 0) return;
            var now = ImGui.GetTime();
            if (tooltipUiRoot != uiRoot || now >= nextTooltipContainerScan)
            {
                tooltipUiRoot = uiRoot;
                nextTooltipContainerScan = now + 1;
                tooltipContainers.Clear();
                if (TryReadMemory<UiElementBaseOffset>(uiRoot, out var root) && root.Self == uiRoot &&
                    root.ChildrensPtr.TotalElements(IntPtr.Size) is >= 0 and <= 180)
                {
                    foreach (var child in ReadStdVector<IntPtr>(root.ChildrensPtr))
                        if (child != IntPtr.Zero && TryReadUiString(child, 0x98, out var id) &&
                            (id == "full_description_fixture" || id == "PopupManager" ||
                             id.Contains("tooltip", StringComparison.OrdinalIgnoreCase)))
                            tooltipContainers.Add(child);
                }
            }
            int remaining = 128;
            foreach (var container in tooltipContainers) CollectTooltipOcclusions(container, 0, ref remaining);

            var gameArea = Core.Process.WindowArea;
            var covers = new List<TabletWindowRect>();
            TabletWindowCallback callback = (window, _) =>
            {
                // Managed exceptions must never cross the reverse P/Invoke boundary.
                try
                {
                    if (!TabletIsWindowVisible(window) || TabletIsIconic(window)) return true;
                    // Overlay windows can be owned/no-activate windows. Do not stop at
                    // the game: inspect all visible PoE Overlay windows on this screen.
                    var title = new System.Text.StringBuilder(256);
                    TabletGetWindowText(window, title, title.Capacity);
                    if (title.ToString().Contains("PoE Overlay", StringComparison.OrdinalIgnoreCase) &&
                        TabletGetWindowRect(window, out var rect)) covers.Add(rect);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            };
            TabletEnumWindows(callback, IntPtr.Zero);
            // ImGui and game state are accessed only after Windows has finished its callback.
            foreach (var rect in covers)
                AddTabletOcclusion(new Vector2(rect.Left - gameArea.Left, rect.Top - gameArea.Top),
                    new Vector2(rect.Right - rect.Left, rect.Bottom - rect.Top));
        }

        private void CollectTooltipOcclusions(IntPtr address, int depth, ref int remaining)
        {
            if (address == IntPtr.Zero || depth > 8 || remaining-- <= 0 ||
                !TryReadMemory<UiElementBaseOffset>(address, out var data) ||
                data.Self != address || !UiElementBaseFuncs.IsVisibleChecker(data.Flags)) return;
            if (data.UnscaledSize.X > 8 && data.UnscaledSize.Y > 8)
            {
                tabletTooltipVisible = true;
                if (PluginUiElementReflection.TryGetAbsoluteRect(address, out var position, out var size))
                    AddTabletOcclusion(position - new Vector2(2), size + new Vector2(4));
                // Text and comparison panels can extend outside the fixture rectangle.
                // Include their rectangles rather than stopping at the first sized parent.
            }
            if (data.ChildrensPtr.TotalElements(IntPtr.Size) is < 0 or > 64) return;
            foreach (var child in ReadStdVector<IntPtr>(data.ChildrensPtr))
                CollectTooltipOcclusions(child, depth + 1, ref remaining);
        }

        private void AddTabletOcclusion(Vector2 position, Vector2 size)
        {
            var display = ImGui.GetIO().DisplaySize;
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
                !float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X <= 0 || size.Y <= 0 ||
                position.X >= display.X || position.Y >= display.Y ||
                position.X + size.X <= 0 || position.Y + size.Y <= 0) return;
            tabletOcclusions.Add((position, size));
        }

        private bool IsTabletOccluded(Vector2 position, Vector2 size)
        {
            foreach (var cover in tabletOcclusions)
                if (position.X < cover.Position.X + cover.Size.X && position.Y < cover.Position.Y + cover.Size.Y &&
                    position.X + size.X > cover.Position.X && position.Y + size.Y > cover.Position.Y) return true;
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TabletWindowRect { public int Left, Top, Right, Bottom; }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private delegate bool TabletWindowCallback(IntPtr window, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "EnumWindows")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TabletEnumWindows(TabletWindowCallback callback, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TabletIsWindowVisible(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "IsIconic")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TabletIsIconic(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TabletGetWindowRect(IntPtr window, out TabletWindowRect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
        private static extern int TabletGetWindowText(IntPtr window, System.Text.StringBuilder text, int count);

        private void QueueItemHighlight(Item item, Vector2 pos, Vector2 size)
        {
            if (item == null) return;
            pendingItemHighlights.Add((item, pos, size));
        }

        private void FlushItemHighlights()
        {
            var mouse = ImGui.GetMousePos();
            // Inspect every queued item before drawing, including equipment/currency.
            // A hovered item late in the queue must also hide earlier tablet marks.
            tabletItemHovered = Settings.HideTabletHighlightsOnItemHover && !ImGui.GetIO().WantCaptureMouse &&
                pendingItemHighlights.Any(p => mouse.X >= p.Position.X && mouse.Y >= p.Position.Y &&
                    mouse.X < p.Position.X + p.Size.X && mouse.Y < p.Position.Y + p.Size.Y);
            try
            {
                foreach (var pending in pendingItemHighlights)
                    EvaluateAndHighlightItem(pending.Item, pending.Position, pending.Size);
            }
            finally
            {
                pendingItemHighlights.Clear();
            }
        }

        private void EvaluateAndHighlightItem(Item item, Vector2 pos, Vector2 size)
        {
            if (item == null) return;

            if (!item.TryGetComponent<Base>(out var baseComponent)) return;

            var name = baseComponent.BaseItemName;
            var path = item.Path;
            var internalNameStr = baseComponent.InternalName;

            bool isWaystone = (!string.IsNullOrEmpty(name) && name.Contains("Waystone")) || path.Contains("MapKey", StringComparison.OrdinalIgnoreCase) || path.Contains("Waystone", StringComparison.OrdinalIgnoreCase);
            bool isTablet = IsTabletItem(path);
            bool isJewel = (!string.IsNullOrEmpty(name) && (name.Contains("Jewel") || name == "Sapphire" || name == "Ruby" || name == "Emerald" || name == "Diamond" || name.Contains("Time-Lost"))) || path.Contains("Metadata/Items/Jewels", StringComparison.OrdinalIgnoreCase) || path.Contains("Jewel", StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(internalNameStr) && internalNameStr.Contains("Jewel", StringComparison.OrdinalIgnoreCase));

            if (!isWaystone && !isTablet && !isJewel) return;

            if (isWaystone && !Settings.EnableWaystoneManager) return;
            if (isTablet && !Settings.EnableTabletManager) return;
            if (isJewel && !Settings.EnableJewelManager) return;

            if (Settings.EnableDebugProbe && !freezeHoveredWaystone)
            {
                var mousePos = ImGui.GetMousePos();
                if (mousePos.X >= pos.X && mousePos.X <= pos.X + size.X &&
                    mousePos.Y >= pos.Y && mousePos.Y <= pos.Y + size.Y)
                {
                    lastHoveredWaystone = item;
                }
            }
            if (isTablet)
            {
                string metaType = GetTabletProfileType(name, path);
                if (metaType != null && Settings.TabletProfiles?.TryGetValue(metaType, out var metaSettings) == true &&
                    metaSettings?.UseMetaFilters == true && ActiveMetaProfile(metaSettings) != null)
                {
                    item.TryGetComponent<Mods>(out var metaMods);
                    DrawMetaTabletHighlight(name, metaType, pos, size, metaMods);
                    return;
                }
            }
            if (isTablet && (TabletHighlightsSuppressed() || IsTabletOccluded(pos, size))) return;
            // Only hide the hovered tablet's overlay so its remaining uses stay readable.
            if (isTablet && Settings.HideTabletHighlightsOnItemHover)
            {
                var mouse = ImGui.GetMousePos();
                if (mouse.X >= pos.X && mouse.Y >= pos.Y &&
                    mouse.X < pos.X + size.X && mouse.Y < pos.Y + size.Y) return;
            }

            var rarity = ExileCenter.RemoteEnums.Rarity.Normal;
            if (item.TryGetComponent<Mods>(out var modsComponent))
            {
                rarity = modsComponent.Rarity;
            }

            if (isWaystone && Settings.HideNormalWaystones && rarity == ExileCenter.RemoteEnums.Rarity.Normal) return;

            int tier = 0;
            if (isWaystone)
            {
                // The internal id is locale-independent (e.g. MapKeyTier15).
                var tierMatch = System.Text.RegularExpressions.Regex.Match(internalNameStr ?? string.Empty, @"^MapKeyTier(\d+)$");
                if (!tierMatch.Success)
                {
                    tierMatch = System.Text.RegularExpressions.Regex.Match(name ?? string.Empty, @"Tier\s*(\d+)");
                }
                if (tierMatch.Success && int.TryParse(tierMatch.Groups[1].Value, out var parsedTier))
                {
                    tier = parsedTier;
                }
                else
                {
                    var pathMatch = System.Text.RegularExpressions.Regex.Match(path, @"(?:MapKeyTier|Waystone)(\d+)");
                    if (pathMatch.Success) int.TryParse(pathMatch.Groups[1].Value, out tier);
                }

                if (tier < Settings.MinTier) return;
            }

            bool isBad = false;
            bool isGood = false;
            bool isGreat = false;
            bool isGod = false;

            int sumRarity = 0;
            int sumPackSize = 0;
            int sumMonstRarity = 0;
            int sumEffect = 0;
            int sumDropChance = 0;
            int revives = 5;
            int explicitModsCount = 0;

            int tabletGoodCount = 0;
            int tabletMatchCount = 0;
            var tabletClassification = UnclassifiedTablet;
            string tabletType = isTablet ? GetTabletProfileType(name, path) : null;
            if (isTablet && tabletType != null && Settings.TabletProfiles?.TryGetValue(tabletType, out var tabletProfile) == true)
                tabletClassification = ClassifyTabletMods(modsComponent, tabletProfile, tabletType);
            string tabletLabel = tabletClassification.IsRanked ? tabletClassification.RankLabel :
                PluginText.T("stashutility.filters.tablet_unclassified", "N/C");
            if (isTablet && (tabletType == null || Settings.TabletProfiles?.TryGetValue(tabletType, out var selection) != true ||
                !MatchesTabletSelection(modsComponent, selection, tabletType)))
            {
                if (!Settings.DisableBadTabletHighlight)
                    DrawTabletRankBadge(pos, size, tabletLabel + " · NON", size.X / 52f, Math.Max(2f, Settings.BorderMargin * size.X / 52f));
                return;
            }
            int jewelGoodCount = 0;

            if (modsComponent != null)
            {
                explicitModsCount = modsComponent.ExplicitMods.Count;
                revives = Math.Max(0, Math.Min(5, 6 - explicitModsCount));
                var statsFromMods = GetStatsFromMods(modsComponent);
                bool hasMemoryStats = statsFromMods.Count > 0;
                if (statsFromMods.TryGetValue((GameStats)8210, out var rawDropChance)) sumDropChance = rawDropChance;
                if (statsFromMods.TryGetValue((GameStats)8206, out var rawRarity)) sumRarity = rawRarity;
                if (statsFromMods.TryGetValue((GameStats)8207, out var rawPackSize)) sumPackSize = rawPackSize;
                if (statsFromMods.TryGetValue((GameStats)8208, out var rawMonsterRarity)) sumMonstRarity = rawMonsterRarity;
                if (statsFromMods.TryGetValue((GameStats)8209, out var rawMonsterEffectiveness)) sumEffect = rawMonsterEffectiveness;

                int quality = 0;
                var field = typeof(Entity).GetField("componentAddresses", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    var dict = field.GetValue(item) as System.Collections.Concurrent.ConcurrentDictionary<string, IntPtr>;
                    if (dict != null && dict.TryGetValue("Quality", out var qualityAddr) && qualityAddr != IntPtr.Zero)
                    {
                        TryReadMemory<int>(qualityAddr + 0x18, out quality);
                    }
                }
                sumDropChance += quality;

                var allRawMods = new List<(string name, (float v0, float v1) vals)>();
                allRawMods.AddRange(modsComponent.ImplicitMods);
                allRawMods.AddRange(modsComponent.ExplicitMods);
                allRawMods.AddRange(modsComponent.EnchantMods);
                if (item.TryGetComponent<ObjectMagicProperties>(out var extraMods))
                    foreach (var extra in extraMods.Mods)
                        if (!allRawMods.Any(m => m.name == extra.name)) allRawMods.Add(extra);

                var matchedWaystonePatternIds = new HashSet<string>();
                var matchedTabletPatternIds = new HashSet<string>();
                var rankedTabletPatternIds = new HashSet<string>();

                foreach (var mod in allRawMods)
                {
                    if (string.IsNullOrEmpty(mod.name)) continue;

                    string lowerName = mod.name.ToLowerInvariant();
                    float rawVal = float.IsNaN(mod.vals.v0) ? 0f : mod.vals.v0;
                    if (rawVal == 0f) rawVal = float.IsNaN(mod.vals.v1) ? 0f : mod.vals.v1;
                    int intVal = (int)rawVal;

                    if (lowerName.Contains("revive"))
                    {
                        revives = intVal;
                    }
                    else if (sumPackSize == 0 && (lowerName.Contains("packsize") || lowerName.Contains("pack_size") || lowerName.Contains("pack")))
                    {
                        sumPackSize = intVal;
                    }

                    if (isWaystone)
                    {
                        var def = Data.ModDatabase.AllWaystoneMods
                            .OrderByDescending(d => d.Id.Length)
                            .FirstOrDefault(d => MatchesModId(mod.name, d.Id));

                        if (def != null)
                        {
                            if (!hasMemoryStats)
                            {
                                sumRarity += def.ItemRarity;
                                sumPackSize += def.PackSize;
                                sumMonstRarity += def.MonsterRarity;
                                sumEffect += def.MonsterEffectiveness;
                                sumDropChance += def.WaystoneDropChance;
                            }

                            if (Settings.BadModPatterns.Contains(def.Id))
                            {
                                isBad = true;
                            }
                            if (Settings.GoodModPatterns.Contains(def.Id))
                            {
                                matchedWaystonePatternIds.Add(def.Id);
                                isGood = true;
                            }
                        }
                    }
                    else if (isTablet)
                    {
                        if (tabletType != null && Settings.TabletProfiles != null && Settings.TabletProfiles.TryGetValue(tabletType, out var profile) && profile != null)
                        {
                            var def = Data.ModDatabase.AllTabletMods
                                .OrderByDescending(d => d.Id.Length)
                                .FirstOrDefault(d => MatchesModId(mod.name, d.Id));

                            if (def != null)
                            {
                                bool passesRollCheck = true;
                                if (def.MinRoll != def.MaxRoll)
                                {
                                    float reqMin = profile.ModRequiredMinRolls.TryGetValue(def.Id, out float savedRoll) ? savedRoll : def.MinRoll;
                                    passesRollCheck = PassesModRoll(mod.vals.v0, mod.vals.v1, def.RollValueIndex, reqMin, def.RollDivisor);
                                }

                                string rank = GetTabletModRank(def.Id, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns);
                                int rankIndex = Array.IndexOf(TabletModRanks, rank);
                                if (passesRollCheck && rankIndex >= 0 && rankedTabletPatternIds.Add(def.Id))
                                {
                                    tabletMatchCount++;

                                }

                                if (profile.GodModPatterns.Contains(def.Id) && passesRollCheck)
                                {
                                    isGod = true;
                                }
                                if (profile.BadModPatterns.Contains(def.Id))
                                {
                                    isBad = true;
                                }
                                if (profile.GoodModPatterns.Contains(def.Id) && passesRollCheck && matchedTabletPatternIds.Add(def.Id))
                                {
                                    isGood = true;
                                    tabletGoodCount++;
                                }
                            }
                        }
                    }
                    else if (isJewel)
                    {
                        // Profile Groups evaluation is handled after raw mods scan
                    }
                }

                if (isWaystone)
                {
                    isGood = Settings.GoodModPatterns.Count > 0 && (Settings.RequireAllGoodMods
                        ? Settings.GoodModPatterns.Distinct().All(matchedWaystonePatternIds.Contains)
                        : matchedWaystonePatternIds.Count > 0);
                    bool candidate = true;
                    int activeFilters = 0;

                    if (Settings.FilterGreatRarity) { activeFilters++; if (sumRarity < Settings.MinGreatRarity) candidate = false; }
                    if (Settings.FilterGreatPackSize) { activeFilters++; if (sumPackSize < Settings.MinGreatPackSize) candidate = false; }
                    if (Settings.FilterGreatMonstRarity) { activeFilters++; if (sumMonstRarity < Settings.MinGreatMonstRarity) candidate = false; }
                    if (Settings.FilterGreatEffect) { activeFilters++; if (sumEffect < Settings.MinGreatEffect) candidate = false; }
                    if (Settings.FilterGreatDropChance) { activeFilters++; if (sumDropChance < Settings.MinGreatDropChance) candidate = false; }
                    if (Settings.FilterGreatExplicitMods) { activeFilters++; if (explicitModsCount < Settings.MinGreatExplicitMods) candidate = false; }
                    if (Settings.FilterGreatMaxExplicitMods) { activeFilters++; if (explicitModsCount > Settings.MaxGreatExplicitMods) candidate = false; }

                    isGreat = activeFilters > 0 && candidate;
                }
                else if (isTablet)
                {
                    if (isGod || tabletGoodCount >= Settings.MinGoodModsToIgnoreBad)
                    {
                        isBad = false;
                        isGood = true;
                    }

                    if (!isGod && tabletGoodCount < Settings.MinTabletGoodModsToHighlight)
                    {
                        isGood = false;
                    }

                    // The ET / OU selection has already passed, independently of highlight thresholds.
                    if (tabletMatchCount > 0)
                    {
                        isGood = true;
                        isBad = false;
                    }

                    if (Settings.FilterTabletGreat)
                    {
                        isGreat = isGod || tabletGoodCount >= Settings.MinTabletGoodMods;
                    }
                }
                else if (isJewel)
                {
                    string kind = GetItemKind(path);
                    string categoryKey = kind.StartsWith("Jewels/", StringComparison.Ordinal) ? kind.Split('/')[1] : "";
                    if (kind.EndsWith("/TimeLost", StringComparison.Ordinal)) categoryKey = "Time-Lost " + categoryKey;

                    bool satisfiesAnyGroup = false;

                    if (Settings.JewelCategoryGroups.TryGetValue(categoryKey, out var groupList) && groupList != null && groupList.Count > 0)
                    {
                        foreach (var group in groupList)
                        {
                            if (group.ModPatternIds == null || group.ModPatternIds.Count == 0) continue;

                            int groupMatchCount = CountMatchingJewelMods(categoryKey, group.ModPatternIds, group.ModRequiredMinRolls, allRawMods);

                            if (groupMatchCount >= group.MinMatchCount)
                            {
                                satisfiesAnyGroup = true;
                                jewelGoodCount = Math.Max(jewelGoodCount, groupMatchCount);
                            }
                        }
                    }
                    else
                    {
                        jewelGoodCount = CountMatchingJewelMods(categoryKey, Settings.JewelGoodModPatterns, Settings.JewelModRequiredMinRolls, allRawMods);
                        if (jewelGoodCount >= Settings.MinJewelGoodModsToHighlight)
                        {
                            satisfiesAnyGroup = true;
                        }
                    }

                    isGood = satisfiesAnyGroup;

                    if (Settings.FilterJewelGreat)
                    {
                        isGreat = jewelGoodCount >= Settings.MinJewelGoodMods;
                    }
                }
            }

            bool passesNumericalFilters = true;
            if (isWaystone)
            {
                if (Settings.FilterMaxRevives && revives > Settings.MaxRevivesAvailable) passesNumericalFilters = false;
                if (Settings.FilterMinItemRarity && sumRarity < Settings.MinItemRarity) passesNumericalFilters = false;
                if (Settings.FilterMaxItemRarity && sumRarity > Settings.MaxItemRarity) passesNumericalFilters = false;
                if (Settings.FilterMinPackSize && sumPackSize < Settings.MinPackSize) passesNumericalFilters = false;
                if (Settings.FilterMaxPackSize && sumPackSize > Settings.MaxPackSize) passesNumericalFilters = false;
                if (Settings.FilterMinMonsterRarity && sumMonstRarity < Settings.MinMonsterRarity) passesNumericalFilters = false;
                if (Settings.FilterMaxMonsterRarity && sumMonstRarity > Settings.MaxMonsterRarity) passesNumericalFilters = false;
                if (Settings.FilterMinMonsterEffectiveness && sumEffect < Settings.MinMonsterEffectiveness) passesNumericalFilters = false;
                if (Settings.FilterMaxMonsterEffectiveness && sumEffect > Settings.MaxMonsterEffectiveness) passesNumericalFilters = false;
                if (Settings.FilterMinWaystoneDropChance && sumDropChance < Settings.MinWaystoneDropChance) passesNumericalFilters = false;
                if (Settings.FilterMaxWaystoneDropChance && sumDropChance > Settings.MaxWaystoneDropChance) passesNumericalFilters = false;
                if (Settings.FilterMinExplicitMods && explicitModsCount < Settings.MinExplicitMods) passesNumericalFilters = false;
                if (Settings.FilterMaxExplicitMods && explicitModsCount > Settings.MaxExplicitMods) passesNumericalFilters = false;
            }

            if (isWaystone && Settings.GoodModPatterns.Count > 0 && !isGood) passesNumericalFilters = false;
            if ((isTablet || isJewel) && !isBad && !isGood)
            {
                if (isTablet) DrawTabletRankBadge(pos, size, tabletLabel, size.X / 52f, Math.Max(2f, Settings.BorderMargin * size.X / 52f));
                return;
            }

            if (isWaystone && Settings.FilterBadModsOnlyOnHighlighted && !passesNumericalFilters) return;

            if (!isBad && !passesNumericalFilters) return;

            float scale = size.X / 52.0f;
            float margin = isTablet ? scale : Settings.BorderMargin * scale;
            float activeBorderThickness = 0f;

            bool showModBorder = (isWaystone && Settings.ShowModBorder) || (isTablet && Settings.ShowTabletModBorder) || (isJewel && Settings.EnableJewelManager);

            if (isTablet && isBad && Settings.DisableBadTabletHighlight)
            {
                return;
            }

            if (showModBorder && (isBad || isGood || passesNumericalFilters))
            {
                Vector4 borderCol = isJewel
                    ? (isBad ? Settings.JewelBadColor : Settings.JewelGoodColor)
                    : (isTablet 
                        ? (isBad ? Settings.TabletBadColor : Settings.TabletGoodColor)
                        : (isBad ? Settings.BadColor : Settings.GoodColor));
                float thickness = isBad ? Settings.BorderThickness : Math.Max(1.5f, Settings.BorderThickness - 0.5f);
                if (isTablet) thickness = Math.Clamp(Settings.BorderThickness * .5f, 1f, 2f) * scale;
                int style = isBad ? Settings.FrameStyleBad : Settings.FrameStyleGood;

                if (isTablet && tabletMatchCount > 0)
                {
                    int strength = Math.Clamp(tabletMatchCount, 1, 4);
                    borderCol = tabletClassification.RankColor;
                    borderCol.W = Math.Min(1f, .7f + .1f * strength);
                    var fill = borderCol;
                    fill.W = .07f + .05f * (strength - 1);
                    ImGui.GetBackgroundDrawList().AddRectFilled(pos + new Vector2(margin), pos + size - new Vector2(margin), ImGuiHelper.Color(fill), 3f);
                }

                activeBorderThickness = thickness;
                float halfThickness = thickness / 2.0f;
                float inset = margin + halfThickness;

                AddStyledRect(ImGui.GetBackgroundDrawList(), pos + new Vector2(inset, inset), pos + size - new Vector2(inset, inset), ImGuiHelper.Color(borderCol), thickness, style);
            }

            if ((isWaystone || isTablet || isJewel) && Settings.ShowRarityBorder)
            {
                DrawItemRarityMarker(pos, size, margin, activeBorderThickness, rarity);
            }

            if (isGreat)
            {
                float arrowSize = Settings.GreatIndicatorSize * scale;
                float padding = 4.0f * scale;
                float borderPad = activeBorderThickness > 0 ? activeBorderThickness : 0f;
                float totalPadX = margin + borderPad + padding;
                float totalPadY = margin + borderPad + padding;

                Vector2 topTip = Settings.GreatIndicatorPosition switch
                {
                    0 => pos + new Vector2(totalPadX + (arrowSize / 2), totalPadY), // Top-Left
                    1 => pos + new Vector2(size.X - totalPadX - (arrowSize / 2), totalPadY + (Settings.ShowRarityBorder ? Settings.RarityIndicatorSize : 0f)), // Top-Right
                    2 => pos + new Vector2(totalPadX + (arrowSize / 2), size.Y - totalPadY - arrowSize), // Bottom-Left
                    3 => pos + new Vector2(size.X - totalPadX - (arrowSize / 2), size.Y - totalPadY - arrowSize), // Bottom-Right
                    _ => pos + new Vector2(totalPadX + (arrowSize / 2), totalPadY)
                };

                var dl = ImGui.GetBackgroundDrawList();
                Vector4 arrowCol = isJewel ? Settings.JewelColorGreat : (isTablet ? Settings.TabletColorGreat : Settings.ColorGreat);
                if (isTablet && tabletMatchCount > 0) arrowCol = tabletClassification.RankColor;
                dl.AddTriangleFilled(topTip, topTip + new Vector2(-arrowSize / 2, arrowSize), topTip + new Vector2(arrowSize / 2, arrowSize), ImGuiHelper.Color(arrowCol));
                dl.AddTriangle(topTip, topTip + new Vector2(-arrowSize / 2, arrowSize), topTip + new Vector2(arrowSize / 2, arrowSize), 0xFF000000, Math.Max(1.0f, 1.5f * scale));
            }
            if (isTablet)
                DrawTabletRankBadge(pos, size, tabletLabel, scale,
                    Math.Max(2f * scale, margin + activeBorderThickness + scale));
        }

        private static void DrawTabletRankBadge(Vector2 pos, Vector2 size, string rank, float scale, float inset)
        {
            if (string.IsNullOrEmpty(rank) || size.X < 20 || size.Y < 20) return;
            var dl = ImGui.GetBackgroundDrawList();
            float padding = Math.Max(2f, 2f * scale);
            var measured = ImGui.CalcTextSize(rank);
            float fontSize = Math.Min(Math.Clamp(14f * scale, 10f, 28f),
                Math.Max(0, size.X - inset * 2 - padding * 2) / Math.Max(1, measured.X) * ImGui.GetFontSize());
            if (fontSize < 8f) return;
            var textSize = measured * (fontSize / ImGui.GetFontSize());
            var badgeSize = textSize + new Vector2(padding * 2, padding);
            var p0 = pos + new Vector2(inset, inset);
            var p1 = p0 + badgeSize;
            var color = TabletItemRankColor(rank);
            dl.AddRectFilled(p0, p1, ImGuiHelper.Color(new Vector4(.04f, .05f, .07f, .94f)), 3f);
            dl.AddRect(p0, p1, ImGuiHelper.Color(color), 3f, ImDrawFlags.RoundCornersAll, Math.Max(1f, scale));
            dl.AddText(ImGui.GetFont(), fontSize, p0 + new Vector2(padding, padding / 2), ImGuiHelper.Color(color), rank);
        }

        private void AddStyledRect(ImDrawListPtr dl, Vector2 min, Vector2 max, uint color, float thickness, int style)
        {
            if (style == 0)
            {
                dl.AddRect(min, max, color, 3.0f, ImDrawFlags.RoundCornersAll, thickness);
                return;
            }

            float step = (style == 1) ? 10.0f : 4.0f;
            float space = (style == 1) ? 5.0f : 4.0f;

            DrawDashedLine(dl, new Vector2(min.X, min.Y), new Vector2(max.X, min.Y), color, thickness, step, space);
            DrawDashedLine(dl, new Vector2(max.X, min.Y), new Vector2(max.X, max.Y), color, thickness, step, space);
            DrawDashedLine(dl, new Vector2(max.X, max.Y), new Vector2(min.X, max.Y), color, thickness, step, space);
            DrawDashedLine(dl, new Vector2(min.X, max.Y), new Vector2(min.X, min.Y), color, thickness, step, space);
        }

        private void DrawDashedLine(ImDrawListPtr dl, Vector2 start, Vector2 end, uint color, float thickness, float step, float space)
        {
            Vector2 diff = end - start;
            float fullLen = diff.Length();
            if (fullLen <= 0) return;
            Vector2 dir = diff / fullLen;

            float currentLen = 0;
            while (currentLen < fullLen)
            {
                float lineLen = Math.Min(step, fullLen - currentLen);
                dl.AddLine(start + dir * currentLen, start + dir * (currentLen + lineLen), color, thickness);
                currentLen += lineLen + space;
            }
        }

        private List<string> GetWaystoneModLines(Item item)
        {
            var lines = new List<string>();
            if (item == null) return lines;

            if (item.TryGetComponent<Mods>(out var modsComponent))
            {
                AddModGroup(lines, modsComponent.ImplicitMods);
                AddModGroup(lines, modsComponent.ExplicitMods);
                AddModGroup(lines, modsComponent.EnchantMods);
            }

            if (item.TryGetComponent<ObjectMagicProperties>(out var magicProps))
            {
                AddModGroup(lines, magicProps.Mods);
            }

            return lines;
        }

        private void AddModGroup(List<string> lines, List<(string name, (float value0, float value1) values)> mods)
        {
            foreach (var (name, values) in mods)
            {
                var formatted = FormatModLine(name, values);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    lines.Add(formatted);
                }
            }
        }

        private string FormatModLine(string template, (float value0, float value1) values)
        {
            if (string.IsNullOrWhiteSpace(template)) return string.Empty;

            var translatedTemplate = TranslateModName(template);
            var line = translatedTemplate;

            if (!float.IsNaN(values.value0))
            {
                float val0 = values.value0;
                if (val0 < 0 && translatedTemplate != template && (translatedTemplate.Contains("less", StringComparison.OrdinalIgnoreCase) || translatedTemplate.Contains("reduced", StringComparison.OrdinalIgnoreCase)))
                {
                    val0 = Math.Abs(val0);
                }

                line = line.Replace("{0}", FormatNumber(val0), StringComparison.Ordinal);
                if (!float.IsNaN(values.value1))
                {
                    float val1 = values.value1;
                    if (val1 < 0 && translatedTemplate != template && (translatedTemplate.Contains("less", StringComparison.OrdinalIgnoreCase) || translatedTemplate.Contains("reduced", StringComparison.OrdinalIgnoreCase)))
                    {
                        val1 = Math.Abs(val1);
                    }
                    line = line.Replace("{1}", FormatNumber(val1), StringComparison.Ordinal);
                }
            }

            return line.Trim();
        }

        private string FormatNumber(float value)
        {
            if (Math.Abs(value - MathF.Round(value)) < 0.001f)
            {
                return ((int)MathF.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private Dictionary<GameStats, int> GetStatsFromMods(Mods modsComponent)
        {
            var stats = new Dictionary<GameStats, int>();
            if (modsComponent == null || modsComponent.Address == IntPtr.Zero)
            {
                return stats;
            }

            try
            {
                var data = ReadMemory<GameOffsets.Objects.Components.ModsOffsets>(modsComponent.Address);
                var mystats = ReadStdVector<GameOffsets.Objects.Components.StatArrayStruct>(data.Details0.StatsFromMods);
                foreach (var newStat in mystats)
                {
                    stats[(GameStats)newStat.key] = newStat.value;
                }
            }
            catch
            {
                // Ignored
            }

            return stats;
        }

        private static int ExtractFirstNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var match = System.Text.RegularExpressions.Regex.Match(text, @"\d+");
            if (match.Success && int.TryParse(match.Value, out var val))
            {
                return val;
            }
            return 0;
        }

        private IntPtr GetHighlightItemAddress(IntPtr elementAddr)
        {
            // Use the displayed-item field, as LootValue does. Scanning the entire UI
            // object can pick a retained tablet reference on a currency slot or container.
            return elementAddr != IntPtr.Zero && TryReadMemory<IntPtr>(elementAddr + 0x4E0, out var item) &&
                IsValidItemEntity(item) ? item : IntPtr.Zero;
        }

        private IntPtr GetItemAddressFromElement(IntPtr elementAddr, bool transfer = false, HashSet<IntPtr> presentItems = null)
        {
            if (elementAddr == IntPtr.Zero) return IntPtr.Zero;

            // Remember only a validated field offset for this slot layout, never an
            // item pointer. Revalidate the current pointer before every use.
            IntPtr layout = IntPtr.Zero;
            if (transfer && TryReadMemory<IntPtr>(elementAddr, out layout) && layout != IntPtr.Zero &&
                affinityItemOffsets.TryGetValue(layout, out var cachedOffset) &&
                cachedOffset >= Settings.ScanStartOffset && cachedOffset + 8 <= Settings.ScanEndOffset &&
                TryReadMemory<IntPtr>(elementAddr + cachedOffset, out var cachedItem) &&
                (presentItems == null || presentItems.Contains(cachedItem)) && IsValidItemEntity(cachedItem))
                return cachedItem;

            for (int offset = Settings.ScanStartOffset; offset + 8 <= Settings.ScanEndOffset; offset += 8)
            {
                if (TryReadMemory<IntPtr>(elementAddr + offset, out var cand))
                {
                    // The live inventory already identifies its entities. Do not probe
                    // unrelated pointers (or transferred items still retained by the UI).
                    if ((presentItems == null || presentItems.Contains(cand)) && IsValidItemEntity(cand))
                    {
                        if (transfer && layout != IntPtr.Zero) affinityItemOffsets[layout] = offset;
                        return cand;
                    }
                }
            }
            return IntPtr.Zero;
        }

        private bool IsValidItemEntity(IntPtr address)
        {
            if (address == IntPtr.Zero || (ulong)address.ToInt64() < 0x10000 || (ulong)address.ToInt64() > 0x7FFFFFFFFFFF)
            {
                return false;
            }

            try
            {
                if (!TryReadMemory<IntPtr>(address + 0x08, out var detailsPtr))
                {
                    return false;
                }
                if (detailsPtr == IntPtr.Zero || (ulong)detailsPtr.ToInt64() < 0x10000 || (ulong)detailsPtr.ToInt64() > 0x7FFFFFFFFFFF)
                {
                    return false;
                }

                if (!TryReadMemory<StdWString>(detailsPtr + 0x08, out var nativeContainer))
                {
                    return false;
                }
                var path = ReadStdWString(nativeContainer);
                if (path.StartsWith("Metadata/Items", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Ignored
            }
            return false;
        }

        private Item ReadFreshItem(IntPtr itemAddress)
        {
            if (itemAddress == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(
                    typeof(Item),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { itemAddress },
                    null) as Item;
            }
            catch
            {
                return null;
            }
        }

        private IntPtr ResolvePath(IntPtr root, int[] path)
        {
            if (root == IntPtr.Zero)
            {
                if (Settings.EnableDebugProbe)
                {
                    lock (probeLog)
                    {
                        probeLog.Clear();
                        probeLog.Add("Root address is Zero");
                    }
                }
                return IntPtr.Zero;
            }

            var current = root;

            if (Settings.EnableDebugProbe)
            {
                lock (probeLog)
                {
                    probeLog.Clear();
                    probeLog.Add($"Root: 0x{current.ToInt64():X}");
                }
            }

            for (int i = 0; i < path.Length; i++)
            {
                var idx = path[i];
                var off = ReadMemory<UiElementBaseOffset>(current);
                var kids = ReadStdVector<IntPtr>(off.ChildrensPtr);

                if (Settings.EnableDebugProbe)
                {
                    lock (probeLog)
                    {
                        probeLog.Add($"Step {i} [Index {idx}]: parent 0x{current.ToInt64():X} has {kids.Length} children");
                    }
                }

                if (idx < 0 || idx >= kids.Length)
                {
                    if (Settings.EnableDebugProbe)
                    {
                        lock (probeLog)
                        {
                            probeLog.Add($"Error: Index {idx} out of range (0..{kids.Length - 1})");
                        }
                    }
                    return IntPtr.Zero;
                }

                current = kids[idx];
                if (current == IntPtr.Zero)
                {
                    if (Settings.EnableDebugProbe)
                    {
                        lock (probeLog)
                        {
                            probeLog.Add($"Error: Child at index {idx} is null");
                        }
                    }
                    return IntPtr.Zero;
                }
            }

            if (Settings.EnableDebugProbe)
            {
                lock (probeLog)
                {
                    probeLog.Add($"Success: Resolved to 0x{current.ToInt64():X}");
                }
            }

            return current;
        }

        private bool InitReflection()
        {
            try
            {
                var handleProp = Core.Process.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                this.handleObj = handleProp?.GetValue(Core.Process);
                if (this.handleObj == null) return false;

                var methods = this.handleObj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                this.readStdWStringMethod = methods.First(m => m.Name == "ReadStdWString" && m.GetParameters().Length == 1);
                this.uiParentsObj          = PluginUiElementReflection.CreateParents("p1");
                this.uiParentsObjP2        = PluginUiElementReflection.CreateParents("p2");
                this.currentUiParentsObj   = this.uiParentsObj;
                
                var uiParentsType = PluginUiElementReflection.UiElementParentsType;
                if (uiParentsType != null)
                {
                    this.updateCacheMethod = uiParentsType.GetMethod("UpdateAllParentsParallel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }

        private T ReadMemory<T>(IntPtr address) where T : unmanaged
        {
            if (!readMemoryMethods.TryGetValue(typeof(T), out var method))
            {
                var genericMethod = this.handleObj.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .First(m => m.Name == "ReadMemory" && m.IsGenericMethod && m.GetParameters().Length == 1);
                method = genericMethod.MakeGenericMethod(typeof(T));
                readMemoryMethods[typeof(T)] = method;
            }
            return (T)method.Invoke(this.handleObj, new object[] { address });
        }

        private bool TryReadMemory<T>(IntPtr address, out T result) where T : unmanaged
        {
            try
            {
                if (!tryReadMemoryMethods.TryGetValue(typeof(T), out var method))
                {
                    var genericMethod = this.handleObj.GetType()
                        .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                        .First(m => m.Name == "TryReadMemory" && m.IsGenericMethod && m.GetParameters().Length == 2);
                    method = genericMethod.MakeGenericMethod(typeof(T));
                    tryReadMemoryMethods[typeof(T)] = method;
                }

                var args = new object[] { address, default(T) };
                var success = (bool)method.Invoke(this.handleObj, args);
                result = (T)args[1];
                return success;
            }
            catch
            {
                result = default;
                return false;
            }
        }

        private T[] ReadStdVector<T>(StdVector nativeContainer) where T : unmanaged
        {
            if (!readStdVectorMethods.TryGetValue(typeof(T), out var method))
            {
                var genericMethod = this.handleObj.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .First(m => m.Name == "ReadStdVector" && m.IsGenericMethod);
                method = genericMethod.MakeGenericMethod(typeof(T));
                readStdVectorMethods[typeof(T)] = method;
            }
            return (T[])method.Invoke(this.handleObj, new object[] { nativeContainer });
        }

        private string ReadStdWString(StdWString nativeContainer)
        {
            try
            {
                return this.readStdWStringMethod!.Invoke(this.handleObj, new object[] { nativeContainer }) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }


        private void DrawStringList(List<string> list)
        {
            int toRemove = -1;
            for (int i = 0; i < list.Count; i++)
            {
                var val = list[i];
                ImGui.PushID(i);
                if (ImGui.InputText("##val", ref val, 128))
                {
                    list[i] = val;
                }
                ImGui.SameLine();
                if (ImGui.Button(PluginText.T("stashutility.remove", "Remove")))
                {
                    toRemove = i;
                }
                ImGui.PopID();
            }

            if (toRemove >= 0)
            {
                list.RemoveAt(toRemove);
            }

        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private bool DumpUiTreeToFile(IntPtr address, bool namesOnly = false)
        {
            if (address == IntPtr.Zero) return false;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"UI Tree Dump from root: 0x{address.ToInt64():X}");
            var visited = namesOnly ? new HashSet<IntPtr>() : null;
            DumpUiTreeRecursive(address, "", 0, sb, visited);
            if (visited?.Count >= 10000) sb.AppendLine("Name export stopped at the 10000-element limit.");
            
            try
            {
                var dir = Path.Combine(DllDirectory, "config");
                Directory.CreateDirectory(dir);
                var filepath = Path.Combine(dir, "ui_tree_dump.txt");
                File.WriteAllText(filepath, sb.ToString());
                Console.WriteLine($"[StashUtility] Dumped UI tree to {filepath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StashUtility] Failed to dump UI tree: {ex}");
                return false;
            }
        }

        private void DumpUiTreeRecursive(IntPtr address, string prefix, int depth, System.Text.StringBuilder sb, HashSet<IntPtr> visited = null)
        {
            bool namesOnly = visited != null;
            if (address == IntPtr.Zero || depth > (namesOnly ? 16 : 8)) return;
            if (namesOnly && (visited.Count >= 10000 || !visited.Add(address))) return;

            var off = ReadMemory<UiElementBaseOffset>(address);
            var kids = ReadStdVector<IntPtr>(off.ChildrensPtr);
            
            sb.AppendLine($"{prefix}Addr: 0x{address.ToInt64():X}, Vis: {UiElementBaseFuncs.IsVisibleChecker(off.Flags)}, Kids: {kids.Length}, Size: <{off.UnscaledSize.X},{off.UnscaledSize.Y}>");

            if (namesOnly)
            {
                // Confirmed by the live export: UI id at 0x98, label text at 0x360.
                if (TryReadUiString(address, 0x98, out var uiId))
                {
                    sb.AppendLine($"{prefix}  ==> UI Id: {JsonConvert.ToString(uiId)}");
                    if (uiId == "label" && TryReadUiString(address, 0x360, out var label))
                    {
                        sb.AppendLine($"{prefix}  ==> Label: {JsonConvert.ToString(label)}");
                    }
                }
            }

            // Export bounded native strings to identify stash labels without assuming a language.
            // This is diagnostic output only; candidates are never used to click or select a tab.
            for (int offset = 0; !namesOnly && offset + 8 <= 0x800; offset += 8)
            {
                if (offset >= 0x20 && offset + Marshal.SizeOf<StdWString>() <= 0x800 &&
                    TryReadUiString(address, offset, out var text))
                {
                    if (text.Any(char.IsLetter))
                    {
                        sb.AppendLine($"{prefix}  ==> String at offset 0x{offset:X}: {JsonConvert.ToString(text)}");
                    }
                }

                // Look for item metadata by dereferencing pointers.
                if (TryReadMemory<IntPtr>(address + offset, out var cand))
                {
                    if (cand != IntPtr.Zero && (ulong)cand.ToInt64() >= 0x10000 && (ulong)cand.ToInt64() <= 0x7FFFFFFFFFFF)
                    {
                        if (TryReadMemory<IntPtr>(cand + 0x08, out var detailsPtr))
                        {
                            if (detailsPtr != IntPtr.Zero && (ulong)detailsPtr.ToInt64() >= 0x10000 && (ulong)detailsPtr.ToInt64() <= 0x7FFFFFFFFFFF)
                            {
                                if (TryReadMemory<StdWString>(detailsPtr + 0x08, out var nativeContainer))
                                {
                                    var path = ReadStdWString(nativeContainer);
                                    if (path != null && path.StartsWith("Metadata/", StringComparison.Ordinal))
                                    {
                                        sb.AppendLine($"{prefix}  ==> Found Metadata Pointer at offset 0x{offset:X}: Entity=0x{cand.ToInt64():X}, Details=0x{detailsPtr.ToInt64():X}, Path={path}");
                                    }
                                }
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < kids.Length; i++)
            {
                DumpUiTreeRecursive(kids[i], prefix + $"  [{i}] ", depth + 1, sb, visited);
            }
        }

        private bool TryReadUiString(IntPtr address, int offset, out string text)
        {
            text = string.Empty;
            if (!TryReadMemory<StdWString>(address + offset, out var container) ||
                container.Length <= 0 || container.Length > 128 ||
                container.Capacity < container.Length || container.Capacity > 512 ||
                container.PAD_14 != 0 || container.PAD_1C != 0 ||
                (container.Capacity > 8 && (container.Buffer.ToInt64() < 0x10000 ||
                    container.Buffer.ToInt64() > 0x7FFFFFFFFFFF))) return false;

            text = ReadStdWString(container);
            return text.Length == container.Length && !text.Any(char.IsControl) && !text.Contains('\uFFFD');
        }

        private static bool hasClearedDumpFile = false;
        private void DumpAllWaystonesMemory(Item item)
        {
            if (item == null) return;
            try
            {
                var dir = Path.Combine(DllDirectory, "config");
                Directory.CreateDirectory(dir);
                var dumpPath = Path.Combine(dir, "waystone_memory_dump.txt");
                if (!hasClearedDumpFile)
                {
                    if (File.Exists(dumpPath)) File.Delete(dumpPath);
                    hasClearedDumpFile = true;
                }

                var lines = new List<string>();
                lines.Add($"=== WAYSTONE MEMORY DUMP: {DateTime.Now} ===");

                if (item.TryGetComponent<Base>(out var baseComponent))
                {
                    lines.Add($"Item Name: {baseComponent.BaseItemName}");
                }
                lines.Add($"Item Path: {item.Path}");

                // Reflect componentAddresses to see what components are actually there
                var field = typeof(Entity).GetField("componentAddresses", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    var dict = field.GetValue(item) as System.Collections.Concurrent.ConcurrentDictionary<string, IntPtr>;
                    if (dict != null)
                    {
                        lines.Add("Available Components:");
                        foreach (var kv in dict)
                        {
                            lines.Add($"  - {kv.Key} at 0x{kv.Value.ToInt64():X}");
                        }

                        if (dict.TryGetValue("Map", out var mapAddr) && mapAddr != IntPtr.Zero)
                        {
                            lines.Add("Map Component Hex Dump (0x80 bytes):");
                            for (int offset = 0; offset < 0x80; offset += 8)
                            {
                                TryReadMemory<long>(mapAddr + offset, out var val64);
                                TryReadMemory<int>(mapAddr + offset, out var val32_1);
                                TryReadMemory<int>(mapAddr + offset + 4, out var val32_2);
                                lines.Add($"  +0x{offset:X2}: {val64:X16} | int32: {val32_1}, {val32_2}");
                            }

                            TryReadMemory<IntPtr>(mapAddr + 0x10, out var detailsAddr);
                            lines.Add($"Map Component Details Pointer: 0x{detailsAddr.ToInt64():X}");
                            if (detailsAddr != IntPtr.Zero)
                            {
                                lines.Add("Map Component Details Hex Dump (0x80 bytes):");
                                for (int offset = 0; offset < 0x80; offset += 8)
                                {
                                    TryReadMemory<long>(detailsAddr + offset, out var val64);
                                    TryReadMemory<int>(detailsAddr + offset, out var val32_1);
                                    TryReadMemory<int>(detailsAddr + offset + 4, out var val32_2);
                                    lines.Add($"  Details +0x{offset:X2}: {val64:X16} | int32: {val32_1}, {val32_2}");
                                }
                            }
                        }

                        if (dict.TryGetValue("LocalStats", out var localStatsAddr) && localStatsAddr != IntPtr.Zero)
                        {
                            lines.Add("LocalStats Component Hex Dump (0x80 bytes):");
                            for (int offset = 0; offset < 0x80; offset += 8)
                            {
                                TryReadMemory<long>(localStatsAddr + offset, out var val64);
                                TryReadMemory<int>(localStatsAddr + offset, out var val32_1);
                                TryReadMemory<int>(localStatsAddr + offset + 4, out var val32_2);
                                lines.Add($"  +0x{offset:X2}: {val64:X16} | int32: {val32_1}, {val32_2}");
                            }
                        }

                        if (dict.TryGetValue("Quality", out var qualityAddr) && qualityAddr != IntPtr.Zero)
                        {
                            lines.Add("Quality Component Hex Dump (0x40 bytes):");
                            for (int offset = 0; offset < 0x40; offset += 8)
                            {
                                TryReadMemory<long>(qualityAddr + offset, out var val64);
                                TryReadMemory<int>(qualityAddr + offset, out var val32_1);
                                TryReadMemory<int>(qualityAddr + offset + 4, out var val32_2);
                                lines.Add($"  +0x{offset:X2}: {val64:X16} | int32: {val32_1}, {val32_2}");
                            }
                        }
                    }
                }

                if (item.TryGetComponent<Mods>(out var modsComp))
                {
                    lines.Add($"Rarity: {modsComp.Rarity}");
                    lines.Add("Implicit Mods:");
                    foreach (var m in modsComp.ImplicitMods)
                    {
                        lines.Add($"  - {m.name}: values = ({m.values.value0}, {m.values.value1})");
                    }
                    lines.Add("Explicit Mods:");
                    foreach (var m in modsComp.ExplicitMods)
                    {
                        lines.Add($"  - {m.name}: values = ({m.values.value0}, {m.values.value1})");
                    }
                    lines.Add("Mods StatsFromMods:");
                    var statsFromMods = GetStatsFromMods(modsComp);
                    foreach (var stat in statsFromMods)
                    {
                        lines.Add($"  - {stat.Key} ({(int)stat.Key}) = {stat.Value}");
                    }
                }

                if (item.TryGetComponent<ObjectMagicProperties>(out var omp))
                {
                    lines.Add("ObjectMagicProperties ModStats:");
                    foreach (var stat in omp.ModStats)
                    {
                        lines.Add($"  - {stat.Key} ({(int)stat.Key}) = {stat.Value}");
                    }
                }

                lines.Add("=========================================\n");

                File.AppendAllLines(dumpPath, lines);
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> MapModTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private class ModsData
        {
            [JsonProperty("DatabaseMods")]
            public List<string> DatabaseMods { get; set; } = new();

            [JsonProperty("Translations")]
            public Dictionary<string, string> Translations { get; set; } = new();
        }

        private static string TranslateModName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return string.Empty;

            // Strip trailing numbers/digits (e.g. MapPlayerRecoveryRate3 -> MapPlayerRecoveryRate)
            string cleanKey = rawName;
            while (cleanKey.Length > 0 && char.IsDigit(cleanKey[cleanKey.Length - 1]))
            {
                cleanKey = cleanKey.Substring(0, cleanKey.Length - 1);
            }

            if (MapModTranslations.TryGetValue(cleanKey, out var translated))
            {
                return translated;
            }

            // Fallback: just return the rawName
            return rawName;
        }

        private static readonly System.Text.RegularExpressions.Regex RangeRegex = 
            new System.Text.RegularExpressions.Regex(@"\([^)]*\)", System.Text.RegularExpressions.RegexOptions.Compiled);
        
        private static readonly System.Text.RegularExpressions.Regex DigitsRegex = 
            new System.Text.RegularExpressions.Regex(@"\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
        
        private static readonly System.Text.RegularExpressions.Regex CleanRegex = 
            new System.Text.RegularExpressions.Regex(@"[^a-zA-Z%\s]", System.Text.RegularExpressions.RegexOptions.Compiled);
        
        private static readonly System.Text.RegularExpressions.Regex SpacesRegex = 
            new System.Text.RegularExpressions.Regex(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

        private string NormalizeForMatching(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            
            // Remove ranges in parentheses like (36-40) or (-8--6)
            var result = RangeRegex.Replace(input, "");
            
            // Remove digits
            result = DigitsRegex.Replace(result, "");
            
            // Remove everything except letters, % and whitespace
            result = CleanRegex.Replace(result, "");
            
            // Normalize spaces to single spaces and lowercase
            result = SpacesRegex.Replace(result, " ").Trim().ToLowerInvariant();
            
            return result;
        }

        private static int GetModRule(string id, List<string> good, List<string> bad, List<string> priority)
        {
            if (priority?.Contains(id) == true) return 3;
            if (bad?.Contains(id) == true) return 2;
            return good.Contains(id) ? 1 : 0;
        }

        private static void SetModRule(string id, int rule, List<string> good, List<string> bad, List<string> priority)
        {
            good.RemoveAll(value => value == id);
            bad?.RemoveAll(value => value == id);
            priority?.RemoveAll(value => value == id);
            if (rule == 1) good.Add(id);
            else if (rule == 2) bad?.Add(id);
            else if (rule == 3) priority?.Add(id);
        }

        private static readonly string[] TabletModRanks = { "S", "A", "B", "C", "D", "E", "F" };

        private bool ApplyTabletFarmPreset(bool force = false)
        {
            if ((!force && Settings.TabletFarmPresetVersion >= Data.ModDatabase.TabletFarmPresetVersion) ||
                Data.ModDatabase.AllTabletMods.Count == 0) return false;

            Settings.TabletProfiles ??= new();
            foreach (string type in StashUtilitySettings.TabletTypes)
            {
                if (!Settings.TabletProfiles.TryGetValue(type, out var profile) || profile == null)
                    Settings.TabletProfiles[type] = profile = new TabletProfile();
                MigrateTabletModRanks(profile);
                var definitions = Data.ModDatabase.AllTabletMods.Where(mod => mod.TabletTypes.Contains(type)).ToArray();
                // Upgrade only untouched presets; manual ranks and unchecked modifiers stay unchanged.
                bool empty = profile.ModRanks.Count == 0 && profile.GoodModPatterns.Count == 0 &&
                    profile.BadModPatterns.Count == 0 && profile.GodModPatterns.Count == 0;
                bool oldPreset = Settings.TabletFarmPresetVersion == 1 && definitions.All(mod =>
                    GetTabletModRank(mod.Id, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns) ==
                    (Data.ModDatabase.GetTabletFarmRank(type, mod.Id) ?? ""));
                if (!force && !empty && !oldPreset) continue;
                foreach (var mod in definitions)
                {
                    string rank = Data.ModDatabase.TabletMeta.Recognize(type, new[] { mod.Id }).Mods[0].Modifier?.Rank ?? "";
                    SetTabletModRank(mod.Id, rank, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns);
                }
            }
            Settings.TabletFarmPresetVersion = Data.ModDatabase.TabletFarmPresetVersion;
            return true;
        }

        private static readonly Vector4[] TabletModRankColors =
        {
            new(.92f, .76f, .32f, 1f), new(.84f, .25f, .28f, 1f),
            new(.66f, .43f, .84f, 1f), new(.27f, .58f, .86f, 1f),
            new(.27f, .69f, .43f, 1f), new(.93f, .47f, .19f, 1f),
            new(.95f, .95f, .95f, 1f)
        };

        private static string NormalizeTabletModRank(string rank) => rank switch
        {
            "S+++" or "S++" or "S+" => "S",
            "S" or "A" or "B" or "C" or "D" or "E" or "F" => rank,
            _ => ""
        };

        private static readonly Models.TabletClassification UnclassifiedTablet =
            new("", 0, "", new Vector4(.65f, .69f, .75f, 1f), int.MaxValue);
        private static readonly Dictionary<int, Models.TabletClassification> TabletRankLookup = BuildTabletRankLookup();
        private static readonly Models.TabletMod[] TabletRankingDefinitions =
            Data.ModDatabase.AllTabletMods.OrderByDescending(mod => mod.Id.Length).ToArray();

        private static Dictionary<int, Models.TabletClassification> BuildTabletRankLookup()
        {
            var result = new Dictionary<int, Models.TabletClassification>();
            var numbers = new int[TabletModRanks.Length];
            int score = 0;
            void Generate(int rank, int remaining, int key, int best)
            {
                for (int count = remaining; count >= 0; count--)
                {
                    if (rank == TabletModRanks.Length - 1 && count != remaining) continue;
                    int first = best < 0 && count > 0 ? rank : best;
                    int nextKey = key * 5 + count;
                    if (rank < TabletModRanks.Length - 1) Generate(rank + 1, remaining - count, nextKey, first);
                    else
                    {
                        int number = ++numbers[first];
                        string letter = TabletModRanks[first];
                        result.Add(nextKey, new Models.TabletClassification(letter, number,
                            letter + number, TabletModRankColors[first], score++));
                    }
                }
            }
            Generate(0, 4, 0, -1);
            return result;
        }

        public static Models.TabletClassification ClassifyTabletRanks(IReadOnlyList<string> ranks)
        {
            if (ranks == null || ranks.Count is < 1 or > 4) return UnclassifiedTablet;
            Span<int> counts = stackalloc int[TabletModRanks.Length];
            counts.Clear();
            int selected = 0;
            foreach (var rank in ranks)
            {
                if (string.IsNullOrEmpty(rank)) continue;
                int index = Array.IndexOf(TabletModRanks, rank);
                if (index < 0) return UnclassifiedTablet;
                counts[index]++;
                selected++;
            }
            if (selected == 0) return UnclassifiedTablet;
            // Missing/unselected ranks occupy the lowest sorting bucket. Preserve
            // all existing four-modifier subrank numbers and the best selected color.
            counts[^1] += 4 - selected;
            int key = 0;
            foreach (int count in counts) key = key * 5 + count;
            return TabletRankLookup.TryGetValue(key, out var result) ? result : UnclassifiedTablet;
        }

        private static readonly Dictionary<string, (string Signature, Dictionary<int, Models.TabletClassification> Ranks)>
            TabletTypeRankLookups = new();

        private static Dictionary<int, Models.TabletClassification> GetTabletTypeRankLookup(TabletProfile profile, string type)
        {
            // A modifier can occur only once. Enumerate up to two prefixes and two
            // suffixes from this type's selected pool, rather than four arbitrary S-F grades.
            var definitions = SelectedTabletMods(profile, type).GroupBy(mod => mod.Id).Select(group => group.First());
            int[] Ranks(string affix) => definitions.Where(mod => mod.Affix == affix &&
                    (mod.MinRoll == mod.MaxRoll || profile.ModRequiredMinRolls == null ||
                     !profile.ModRequiredMinRolls.TryGetValue(mod.Id, out float minimum) || minimum <= mod.MaxRoll))
                .Select(mod => Array.IndexOf(TabletModRanks, GetTabletModRank(mod.Id, profile.ModRanks,
                    profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns)))
                .OrderBy(rank => rank).ToArray();
            var prefixes = Ranks("prefix");
            var suffixes = Ranks("suffix");
            string signature = string.Join(",", prefixes) + "|" + string.Join(",", suffixes);
            lock (TabletTypeRankLookups)
            {
                if (TabletTypeRankLookups.TryGetValue(type, out var cached) && cached.Signature == signature)
                    return cached.Ranks;

                int Weight(int rank)
                {
                    int weight = 1;
                    for (int i = rank + 1; i < TabletModRanks.Length; i++) weight *= 5;
                    return weight;
                }
                HashSet<(int Key, int Count)> Options(int[] ranks)
                {
                    var options = new HashSet<(int Key, int Count)> { (0, 0) };
                    for (int i = 0; i < ranks.Length; i++)
                    {
                        options.Add((Weight(ranks[i]), 1));
                        for (int j = i + 1; j < ranks.Length; j++)
                            options.Add((Weight(ranks[i]) + Weight(ranks[j]), 2));
                    }
                    return options;
                }
                var reachable = new HashSet<int>();
                var prefixOptions = Options(prefixes);
                var suffixOptions = Options(suffixes);
                foreach (var prefix in prefixOptions)
                    foreach (var suffix in suffixOptions)
                        if (prefix.Count + suffix.Count > 0)
                            reachable.Add(prefix.Key + suffix.Key + 4 - prefix.Count - suffix.Count);

                var result = new Dictionary<int, Models.TabletClassification>();
                var numbers = new int[TabletModRanks.Length];
                foreach (var entry in TabletRankLookup.Where(entry => reachable.Contains(entry.Key)).OrderBy(entry => entry.Value.SortScore))
                {
                    var classification = entry.Value;
                    int number = ++numbers[Array.IndexOf(TabletModRanks, classification.Rank)];
                    result.Add(classification.SortScore, classification with { RankIndex = number, RankLabel = classification.Rank + number });
                }
                // Keep at most one immutable lookup per tablet type; edits and preset
                // loads replace it without writing derived data into saved settings.
                TabletTypeRankLookups[type] = (signature, result);
                return result;
            }
        }

        private static Models.TabletClassification ClassifyTabletMods(Mods mods, TabletProfile profile, string type)
        {
            var classification = ClassifyTabletRanks(MatchingTabletRanks(mods, profile, type).Values.ToArray());
            if (!classification.IsRanked) return classification;
            return GetTabletTypeRankLookup(profile, type).TryGetValue(classification.SortScore, out var result)
                ? result : UnclassifiedTablet;
        }

        private static IEnumerable<Models.TabletMod> SelectedTabletMods(TabletProfile profile, string type) =>
            TabletRankingDefinitions.Where(mod => mod.TabletTypes.Contains(type) &&
                GetTabletModRank(mod.Id, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns).Length > 0);

        private static bool MatchesTabletSelection(Mods mods, TabletProfile profile, string type)
        {
            if (profile == null || type == null) return false;
            var matched = MatchingTabletRanks(mods, profile, type);
            return matched.Count > 0 && (!profile.RequireAllRankedMods ||
                SelectedTabletMods(profile, type).All(mod => matched.ContainsKey(mod.Id)));
        }

        private static Dictionary<string, string> MatchingTabletRanks(Mods mods, TabletProfile profile, string type)
        {
            var ranks = new Dictionary<string, string>();
            if (mods?.ExplicitMods?.Count is not (>= 1 and <= 4) || profile == null || type == null) return ranks;
            foreach (var raw in mods.ExplicitMods)
            {
                if (string.IsNullOrEmpty(raw.name)) continue;
                var definition = TabletRankingDefinitions.FirstOrDefault(mod =>
                    mod.TabletTypes.Contains(type) && MatchesModId(raw.name, mod.Id));
                if (definition == null) continue;
                string rank = GetTabletModRank(definition.Id, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns);
                if (rank.Length == 0) continue;
                float minimum = profile.ModRequiredMinRolls?.TryGetValue(definition.Id, out float saved) == true ? saved : definition.MinRoll;
                if (definition.MinRoll != definition.MaxRoll &&
                    !PassesModRoll(raw.values.value0, raw.values.value1, definition.RollValueIndex, minimum, definition.RollDivisor)) continue;
                ranks[definition.Id] = rank;
            }
            return ranks;
        }

        private static bool IsTabletItem(string path) =>
            path?.StartsWith("Metadata/Items/TowerAugment/", StringComparison.OrdinalIgnoreCase) == true;

        private static string GetTabletProfileType(string name, string path)
        {
            bool Has(string term) => name?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
            bool PathHas(string term) => path?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
            if (Has("Breach") || PathHas("BreachAugment")) return "Breach Tablet";
            if (Has("Expedition") || PathHas("ExpeditionAugment")) return "Expedition Tablet";
            if (Has("Delirium") || PathHas("DeliriumAugment")) return "Delirium Tablet";
            if (Has("Ritual") || PathHas("RitualAugment")) return "Ritual Tablet";
            if (Has("Irradiated") || PathHas("GenericAugment")) return "Irradiated Tablet";
            if (Has("Overseer") || PathHas("MapBossAugment")) return "Overseer Tablet";
            if (Has("Abyss") || PathHas("AbyssAugment")) return "Abyss Tablet";
            if (Has("Temple") || Has("Incursion") || PathHas("IncursionAugment")) return "Temple Tablet";
            return null;
        }

        private static Vector4 TabletItemRankColor(string label)
        {
            if (label == "OK") return new Vector4(.4f, .86f, .57f, 1);
            if (label == "NON") return new Vector4(.95f, .4f, .35f, 1);
            if (label is "?" or "!") return new Vector4(1, .73f, .3f, 1);
            int index = label?.Length > 0 ? Array.IndexOf(TabletModRanks, label.Substring(0, 1)) : -1;
            return index >= 0 ? TabletModRankColors[index] : UnclassifiedTablet.RankColor;
        }

        private static bool MigrateTabletModRanks(TabletProfile profile)
        {
            profile.GoodModPatterns ??= new();
            profile.BadModPatterns ??= new();
            profile.GodModPatterns ??= new();
            profile.ModRanks ??= new();
            bool changed = false;
            var selected = profile.GoodModPatterns.Concat(profile.BadModPatterns).Concat(profile.GodModPatterns).Distinct().ToArray();
            foreach (string id in selected)
            {
                string rank = GetTabletModRank(id, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns);
                if (!profile.ModRanks.TryGetValue(id, out var previous) || previous != rank ||
                    GetModRule(id, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns) != TabletRankRule(rank))
                {
                    SetTabletModRank(id, rank, profile.ModRanks, profile.GoodModPatterns, profile.BadModPatterns, profile.GodModPatterns);
                    changed = true;
                }
            }
            foreach (string id in profile.ModRanks.Keys.ToArray())
            {
                string rank = NormalizeTabletModRank(profile.ModRanks[id]);
                if (profile.ModRanks[id] != rank) { profile.ModRanks[id] = rank; changed = true; }
            }
            return changed;
        }

        private static int TabletRankRule(string rank)
        {
            int index = Array.IndexOf(TabletModRanks, rank);
            return index < 0 ? 0 : index == 0 ? 3 : 1;
        }

        private static string GetTabletModRank(string id, Dictionary<string, string> ranks, List<string> good, List<string> bad, List<string> priority)
        {
            // The saved S-to-F choice is authoritative; old three-state lists are a migration fallback.
            if (ranks?.TryGetValue(id, out var rank) == true) return NormalizeTabletModRank(rank);
            int rule = GetModRule(id, good, bad, priority);
            return rule switch { 3 => "S", 1 => "A", 2 => "D", _ => "" };
        }

        private static void SetTabletModRank(string id, string rank, Dictionary<string, string> ranks, List<string> good, List<string> bad, List<string> priority)
        {
            if (Array.IndexOf(TabletModRanks, rank) >= 0) ranks[id] = rank;
            else ranks.Remove(id);
            SetModRule(id, TabletRankRule(rank), good, bad, priority);
        }

        private void DrawTabletRankCheckbox(string id, int index, Dictionary<string, string> ranks, List<string> good, List<string> bad, List<string> priority, bool showLabel)
        {
            string rank = TabletModRanks[index];
            bool selected = GetTabletModRank(id, ranks, good, bad, priority) == rank;
            ImGui.PushStyleColor(ImGuiCol.Text, TabletModRankColors[index]);
            ImGui.PushStyleColor(ImGuiCol.CheckMark, TabletModRankColors[index]);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, selected ? new Vector4(.19f, .24f, .31f, 1f) : new Vector4(.13f, .16f, .20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(.23f, .29f, .36f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(.27f, .34f, .43f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, selected ? TabletModRankColors[index] : new Vector4(.45f, .51f, .61f, 1f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
            if (ImGui.Checkbox((showLabel ? rank : "") + "##rank" + index, ref selected))
            {
                SetTabletModRank(id, selected ? rank : "", ranks, good, bad, priority);
                SaveSettings();
            }
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(6);
            if (ImGui.IsItemHovered()) ImGuiHelper.ToolTip(rank + " · " + PluginText.T("stashutility.filters.state." +
                (index == 0 ? "priority" : "wanted"), index == 0 ? "Priority" : "Wanted"));
        }

        private void DrawModConfigRow(Models.TabletMod mod, List<string> good, List<string> bad, List<string> priority, Dictionary<string, float> rolls, Dictionary<string, string> ranks = null, bool rankGrid = false)
        {
            ImGui.PushID(mod.Id);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            string display = ModDisplayName(mod.Id, mod.Name);
            ImGui.PushTextWrapPos();
            string rank = ranks != null ? GetTabletModRank(mod.Id, ranks, good, bad, priority) : "";
            int rankIndex = Array.IndexOf(TabletModRanks, rank);
            if (rankIndex >= 0) ImGui.PushStyleColor(ImGuiCol.Text, TabletModRankColors[rankIndex]);
            ImGui.TextUnformatted(display + (rankGrid && rankIndex >= 0 ? "  [" + rank + "]" : ""));
            if (rankIndex >= 0) ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
            ImGuiHelper.ToolTip(mod.Name + "\n" + mod.Id);
            ImGui.TableSetColumnIndex(1);
            int rule = GetModRule(mod.Id, good, bad, priority);
            string[] states = { "ignore", "wanted", "avoid", "priority" };
            var color = rule switch { 1 => new Vector4(.4f, .85f, .65f, 1), 2 => new Vector4(1, .5f, .5f, 1), 3 => new Vector4(1, .8f, .35f, 1), _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text] };
            if (rankGrid)
            {
                for (int i = 0; i < TabletModRanks.Length; i++)
                {
                    ImGui.TableSetColumnIndex(i + 1);
                    DrawTabletRankCheckbox(mod.Id, i, ranks, good, bad, priority, false);
                }
            }
            else if (ranks != null)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, rankIndex >= 0 ? TabletModRankColors[rankIndex] : color);
                if (ImGui.Button(rankIndex >= 0 ? rank + "##rankChoice" : "—##rankChoice", new Vector2(-1, 0))) ImGui.OpenPopup("rankChoices");
                ImGui.PopStyleColor();
                if (ImGui.BeginPopup("rankChoices"))
                {
                    if (ImGui.BeginTable("rankGrid", 3, ImGuiTableFlags.SizingFixedFit))
                    {
                        for (int i = 0; i < TabletModRanks.Length; i++)
                        {
                            ImGui.TableNextColumn();
                            DrawTabletRankCheckbox(mod.Id, i, ranks, good, bad, priority, true);
                        }
                        ImGui.EndTable();
                    }
                    ImGui.PushTextWrapPos(260);
                    ImGui.TextUnformatted(PluginText.T("stashutility.filters.rank_one", "Select one rank; uncheck it to ignore this modifier."));
                    ImGui.PopTextWrapPos();
                    ImGui.EndPopup();
                }
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##rule", PluginText.T("stashutility.filters.state." + states[rule], states[rule])))
                {
                    for (int i = 0; i < (priority != null ? 4 : bad != null ? 3 : 2); i++)
                        if (ImGui.Selectable(PluginText.T("stashutility.filters.state." + states[i], states[i]), rule == i))
                        {
                            SetModRule(mod.Id, i, good, bad, priority);
                            rule = i;
                            SaveSettings();
                        }
                    ImGui.EndCombo();
                }
                ImGui.PopStyleColor();
            }
            rule = GetModRule(mod.Id, good, bad, priority);
            ImGui.TableSetColumnIndex(rankGrid ? TabletModRanks.Length + 1 : 2);
            if (rolls != null && (rule == 1 || rule == 3) && mod.MaxRoll > mod.MinRoll && mod.RollValueIndex <= 1)
            {
                float value = rolls.TryGetValue(mod.Id, out float saved) ? saved : mod.MinRoll;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat("##roll", ref value, mod.MaxRoll < 1 ? .001f : .1f, mod.MinRoll, mod.MaxRoll, "%.3g"))
                {
                    rolls[mod.Id] = value;
                    SaveSettings();
                }
                ImGuiHelper.ToolTip(PluginText.F("stashutility.filters.roll_help", "Minimum value to match. Base range: {0} to {1}.", mod.MinRoll, mod.MaxRoll));
            }
            else ImGui.TextDisabled("—");
            ImGui.PopID();
        }
    }
}
