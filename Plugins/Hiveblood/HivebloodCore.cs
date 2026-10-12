namespace Hiveblood
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.UiElement;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class HivebloodCore : PCore<HivebloodSettings>
    {
        private const int GainDedupSeconds = 4;

        private readonly HivebloodUiScanner scanner = new();
        private readonly List<long> scanGainsScratch = new();
        private readonly Dictionary<string, DateTime> recentGainKeys = new(StringComparer.Ordinal);
        private readonly Stopwatch gainSaveDebounce = Stopwatch.StartNew();
        private readonly Stopwatch uiScanTimer = Stopwatch.StartNew();
        private bool pendingTrackerSave;

        private string lastStatus = string.Empty;
        private Vector2 lastInventoryOverlayPosition;
        private bool hasLastInventoryOverlayPosition;
        private bool positionDummySeeded;

        private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingsPath))
            {
                try
                {
                    var content = File.ReadAllText(this.SettingsPath);
                    this.Settings = JsonConvert.DeserializeObject<HivebloodSettings>(content) ?? new HivebloodSettings();
                }
                catch
                {
                    this.Settings = new HivebloodSettings();
                }
            }
        }

        public override void OnDisable()
        {
            this.FlushPendingTrackerSave(force: true);
            this.scanner.ResetProcess();
            this.SaveSettings();
        }

        public override void SaveSettings()
        {
            var dir = Path.GetDirectoryName(this.SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            
            this.DrawSettingsTabs("HivebloodSettings",
                ("tracking", "Tracking", () =>
                {
                    ImGui.TextWrapped(
                        "Tracks Hiveblood: syncs from Genesis Tree (+ Breach popups). Visit the tree once to calibrate.");
                    ImGui.Checkbox("Only when inventory is open", ref this.Settings.ShowOnlyWithInventory);
                    ImGui.Checkbox("Also show when inventory is closed", ref this.Settings.ShowAlways);
                    ImGui.Checkbox("Warn near cap (100,000)", ref this.Settings.WarnNearCap);
                    ImGui.SliderInt("Warn from amount", ref this.Settings.WarnThreshold, 80_000, 100_000);
                    ImGuiHelper.ToolTip(
                        "Above the threshold the overlay text turns orange-red and blinks. It is also shown while the inventory is closed (at the last in-inventory position).");
                    ImGui.Checkbox("Show gains since last tree sync", ref this.Settings.ShowSessionGains);
                    this.DrawStatusBlock();
                }),
                ("appearance", "Appearance", () =>
                {
                    ImGui.SliderFloat("Font scale", ref this.Settings.OverlayFontScale, 0.75f, 1.6f, "%.2f");
                    
                    var anchorIndex = (int)this.Settings.OverlayAnchor;
                    if (ImGui.Combo(
                            "Position anchor",
                            ref anchorIndex,
                            "Inventory top-left\0" +
                            "Inventory bottom (near gold)\0" +
                            "Custom screen position\0"))
                    {
                        this.Settings.OverlayAnchor = (HivebloodOverlayAnchor)anchorIndex;
                    }
                    
                    ImGui.DragFloat2(
                        "Offset from anchor (px)",
                        ref this.Settings.OverlayOffset,
                        1f,
                        -400f,
                        400f,
                        "%.0f");
                    ImGuiHelper.ToolTip(
                        "Bottom anchor: left edge of the inventory panel, just above the gold line. Drag X/Y to fine-tune.");
                    
                    ImGui.Checkbox(
                        "Show position dummy (drag, then disable)",
                        ref this.Settings.ShowPositionDummy);
                    ImGuiHelper.ToolTip(
                        "Shows a draggable preview in-game and saves a fixed screen position. Switches to custom screen position.");
                    
                    if (this.Settings.ShowPositionDummy ||
                        this.Settings.OverlayAnchor == HivebloodOverlayAnchor.CustomScreen)
                    {
                        ImGui.DragFloat2(
                            "Screen position (px)",
                            ref this.Settings.OverlayScreenPosition,
                            1f,
                            0f,
                            4000f,
                            "%.0f");
                    }
                    ImGui.ColorEdit4("Text color", ref this.Settings.TextColor);
                }),
                ("diagnostics", "Diagnostics", () =>
                {
                    ImGui.Checkbox("Debug status line", ref this.Settings.DebugStatusLine);
                    ImGui.SliderInt("UI scan interval (ms)", ref this.Settings.ScanIntervalMs, 150, 1000);
                    ImGuiHelper.ToolTip(
                        "How often the plugin reads the game UI tree. Lower values react faster but use more CPU (default 300).");
                    
                    if (ImGui.Button("Reset tracker"))
                    {
                        this.Settings.EstimatedAmount = 0;
                        this.Settings.HasSyncedOnce = false;
                        this.Settings.LastTreeSyncUtc = DateTime.MinValue;
                        this.Settings.SessionGainSinceSync = 0;
                        this.recentGainKeys.Clear();
                        this.QueueTrackerSave(immediate: true);
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button("Save now"))
                    {
                        this.FlushPendingTrackerSave(force: true);
                        this.SaveSettings();
                    }
                }));
            
        }

        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            if (!Core.Process.Foreground &&
                Process.GetCurrentProcess().MainWindowHandle != GetForegroundWindow())
            {
                return;
            }

            var gameUi = Core.States.InGameStateObject.GameUi;
            if (gameUi.Address == IntPtr.Zero)
            {
                return;
            }

            if (!this.Settings.ShowPositionDummy)
            {
                this.positionDummySeeded = false;
            }

            var inventoryOpen = gameUi.RightPanel.IsVisible;
            var nearCap = this.IsCapWarningActive();
            var positioningDummy = this.Settings.ShowPositionDummy;

            if (this.ShouldScanUi(inventoryOpen, nearCap, positioningDummy))
            {
                this.UpdateTracker(gameUi.Address);
            }

            this.FlushPendingTrackerSave();
            if (this.Settings.ShowOnlyWithInventory && !inventoryOpen && !this.Settings.ShowAlways && !nearCap && !positioningDummy)
            {
                return;
            }

            if (!this.ShouldDrawOverlay(inventoryOpen, positioningDummy))
            {
                return;
            }

            if (positioningDummy)
            {
                this.EnsurePositionDummySeeded(gameUi.RightPanel, inventoryOpen);
                this.DrawPositionDummy();
                return;
            }

            this.DrawOverlay(gameUi.RightPanel, inventoryOpen);
        }

        private bool ShouldScanUi(bool inventoryOpen, bool nearCap, bool positioningDummy)
        {
            var intervalMs = Math.Clamp(this.Settings.ScanIntervalMs, 150, 2000);
            if (inventoryOpen || positioningDummy || this.Settings.ShowAlways || nearCap)
            {
                intervalMs = Math.Min(intervalMs, 250);
            }
            else
            {
                intervalMs = Math.Max(intervalMs, 400);
            }

            if (this.uiScanTimer.ElapsedMilliseconds < intervalMs)
            {
                return false;
            }

            this.uiScanTimer.Restart();
            return true;
        }

        private void UpdateTracker(IntPtr gameUiRoot)
        {
            if (!this.scanner.TryScan(gameUiRoot, out var treeTotal, this.scanGainsScratch))
            {
                return;
            }

            var changed = false;
            var treeSyncedThisFrame = false;
            if (treeTotal.HasValue)
            {
                var total = Math.Clamp(treeTotal.Value, 0, HivebloodSettings.HivebloodCap);
                if (this.Settings.EstimatedAmount != total || !this.Settings.HasSyncedOnce)
                {
                    this.Settings.EstimatedAmount = total;
                    this.Settings.HasSyncedOnce = true;
                    this.Settings.LastTreeSyncUtc = DateTime.UtcNow;
                    this.Settings.SessionGainSinceSync = 0;
                    this.recentGainKeys.Clear();
                    changed = true;
                    treeSyncedThisFrame = true;
                    this.lastStatus = $"Synced from Genesis Tree: {total:N0}";
                }
            }

            foreach (var gain in this.scanGainsScratch)
            {
                var key = $"+{gain}";
                var now = DateTime.UtcNow;
                if (this.recentGainKeys.TryGetValue(key, out var seen) &&
                    (now - seen).TotalSeconds < GainDedupSeconds)
                {
                    continue;
                }

                this.recentGainKeys[key] = now;
                this.Settings.EstimatedAmount = Math.Clamp(
                    this.Settings.EstimatedAmount + gain,
                    0,
                    HivebloodSettings.HivebloodCap);
                this.Settings.SessionGainSinceSync += gain;
                if (!this.Settings.HasSyncedOnce)
                {
                    this.Settings.HasSyncedOnce = true;
                }

                changed = true;
                this.lastStatus = $"+{gain:N0} Hiveblood";
            }

            if (changed)
            {
                this.QueueTrackerSave(immediate: treeSyncedThisFrame);
            }
        }

        private void QueueTrackerSave(bool immediate)
        {
            this.pendingTrackerSave = true;
            if (immediate)
            {
                this.FlushPendingTrackerSave(force: true);
            }
        }

        private void FlushPendingTrackerSave(bool force = false)
        {
            if (!this.pendingTrackerSave)
            {
                return;
            }

            if (!force && this.gainSaveDebounce.Elapsed.TotalSeconds < 1.5)
            {
                return;
            }

            this.SaveSettings();
            this.pendingTrackerSave = false;
            this.gainSaveDebounce.Restart();
        }

        private bool ShouldDrawOverlay(bool inventoryOpen, bool positioningDummy)
        {
            if (positioningDummy)
            {
                return true;
            }

            if (!this.Settings.HasSyncedOnce && this.Settings.EstimatedAmount <= 0)
            {
                return this.Settings.DebugStatusLine;
            }

            if (this.Settings.ShowOnlyWithInventory && !inventoryOpen && !this.Settings.ShowAlways)
            {
                if (!this.IsCapWarningActive())
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsCapWarningActive() =>
            this.Settings.WarnNearCap &&
            this.Settings.HasSyncedOnce &&
            this.Settings.EstimatedAmount >= this.Settings.WarnThreshold;

        private static float CapWarningBlinkAlpha()
        {
            // ~500 ms on / 500 ms dimmer
            return (Environment.TickCount / 500) % 2 == 0 ? 1f : 0.4f;
        }

        private void DrawOverlay(UiElementBase rightPanel, bool inventoryOpen)
        {
            var fg = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            var scale = Math.Clamp(this.Settings.OverlayFontScale, 0.75f, 1.6f);
            var fontSize = ImGui.GetFontSize() * scale;

            this.BuildOverlayLines(out var line1, out var line2);
            var pos = this.ResolveOverlayPosition(rightPanel, inventoryOpen);
            var color = this.ResolveOverlayColor();

            this.DrawOverlayLines(fg, font, fontSize, scale, pos, color, line1, line2);

            if (this.Settings.DebugStatusLine && !string.IsNullOrEmpty(this.lastStatus))
            {
                var shadow = ImGui.ColorConvertFloat4ToU32(this.Settings.ShadowColor);
                var debugPos = pos + new Vector2(0f, 36f);
                this.DrawShadowedText(
                    fg,
                    font,
                    fontSize * 0.8f,
                    debugPos,
                    shadow,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 1f)),
                    this.lastStatus);
            }
        }

        private void DrawPositionDummy()
        {
            var fg = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            var scale = Math.Clamp(this.Settings.OverlayFontScale, 0.75f, 1.6f);
            var fontSize = ImGui.GetFontSize() * scale;

            this.BuildOverlayLines(out var line1, out var line2);
            var pos = this.Settings.OverlayScreenPosition;
            var color = this.ResolveOverlayColor();

            var size1 = ImGui.CalcTextSize(line1);
            var blockWidth = size1.X;
            var blockHeight = size1.Y * (scale / Math.Max(ImGui.GetFontSize(), 1f));
            if (!string.IsNullOrEmpty(line2))
            {
                var size2 = ImGui.CalcTextSize(line2);
                blockWidth = MathF.Max(blockWidth, size2.X);
                blockHeight += size2.Y * (scale / Math.Max(ImGui.GetFontSize(), 1f)) * 0.9f + 2f;
            }

            this.DrawOverlayLines(fg, font, fontSize, scale, pos, color, line1, line2);

            var outlineMin = pos - new Vector2(4f, 4f);
            var outlineMax = pos + new Vector2(blockWidth, blockHeight) + new Vector2(4f, 4f);
            fg.AddRect(
                outlineMin,
                outlineMax,
                ImGuiHelper.Color(new Vector4(0.4f, 0.85f, 1f, 0.85f)),
                4f,
                ImDrawFlags.None,
                1.5f);

            var hint = "Drag to move. Disable dummy in settings when done.";
            var hintSize = ImGui.CalcTextSize(hint);
            var hintPos = new Vector2(pos.X, pos.Y - hintSize.Y - 6f);
            fg.AddText(hintPos + Vector2.One, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.9f)), hint);
            fg.AddText(hintPos, ImGuiHelper.Color(new Vector4(0.75f, 0.9f, 1f, 0.95f)), hint);

            ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(blockWidth + 8f, blockHeight + 8f), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);

            if (!GameHelper.Ui.ImGuiTheme.BeginWindow(
                    "###HivebloodPositionDummy",
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground))
            {
                ImGui.PopStyleColor();
                ImGui.PopStyleVar(2);
                ImGui.End();
                return;
            }

            ImGui.InvisibleButton("##drag", new Vector2(blockWidth + 8f, blockHeight + 8f));
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                this.Settings.OverlayScreenPosition += ImGui.GetIO().MouseDelta;
            }

            var saveSettings = ImGui.IsItemDeactivated();
            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);

            if (saveSettings)
            {
                this.Settings.OverlayAnchor = HivebloodOverlayAnchor.CustomScreen;
                this.Settings.OverlayOffset = Vector2.Zero;
                this.lastInventoryOverlayPosition = this.Settings.OverlayScreenPosition;
                this.hasLastInventoryOverlayPosition = true;
                this.SaveSettings();
            }
        }

        private void BuildOverlayLines(out string line1, out string? line2)
        {
            line2 = null;
            if (!this.Settings.HasSyncedOnce)
            {
                line1 = "Hiveblood: visit Genesis Tree";
                return;
            }

            line1 = $"Hiveblood: {this.Settings.EstimatedAmount:N0}";
            if (this.Settings.ShowSessionGains && this.Settings.SessionGainSinceSync > 0)
            {
                line2 = $"(+{this.Settings.SessionGainSinceSync:N0} since tree)";
            }
        }

        private Vector4 ResolveOverlayColor()
        {
            var color = this.Settings.TextColor;
            if (this.IsCapWarningActive())
            {
                color = new Vector4(1f, 0.45f, 0.35f, 1f);
                color.W *= CapWarningBlinkAlpha();
            }

            return color;
        }

        private void DrawOverlayLines(
            ImDrawListPtr fg,
            ImFontPtr font,
            float fontSize,
            float scale,
            Vector2 pos,
            Vector4 color,
            string line1,
            string? line2)
        {
            var textColor = ImGui.ColorConvertFloat4ToU32(color);
            var shadow = ImGui.ColorConvertFloat4ToU32(this.Settings.ShadowColor);
            this.DrawShadowedText(fg, font, fontSize, pos, shadow, textColor, line1);
            if (string.IsNullOrEmpty(line2))
            {
                return;
            }

            var size1 = ImGui.CalcTextSize(line1);
            var pos2 = pos + new Vector2(0f, size1.Y * (scale / Math.Max(ImGui.GetFontSize(), 1f)) + 2f);
            this.DrawShadowedText(fg, font, fontSize * 0.9f, pos2, shadow, textColor, line2);
        }

        private void EnsurePositionDummySeeded(UiElementBase rightPanel, bool inventoryOpen)
        {
            if (this.positionDummySeeded)
            {
                return;
            }

            if (this.Settings.OverlayAnchor != HivebloodOverlayAnchor.CustomScreen)
            {
                if (inventoryOpen && rightPanel.Size.X > 0f)
                {
                    this.Settings.OverlayScreenPosition = this.ResolveInventoryAnchorPosition(rightPanel);
                }
                else if (this.hasLastInventoryOverlayPosition)
                {
                    this.Settings.OverlayScreenPosition = this.lastInventoryOverlayPosition;
                }

                this.Settings.OverlayAnchor = HivebloodOverlayAnchor.CustomScreen;
                this.Settings.OverlayOffset = Vector2.Zero;
            }

            this.positionDummySeeded = true;
        }

        private void DrawShadowedText(
            ImDrawListPtr draw,
            ImFontPtr font,
            float fontSize,
            Vector2 pos,
            uint shadowColor,
            uint textColor,
            string text)
        {
            draw.AddText(font, fontSize, pos + new Vector2(1f, 1f), shadowColor, text);
            draw.AddText(font, fontSize, pos, textColor, text);
        }

        private void DrawStatusBlock()
        {
            ImGui.TextDisabled("Tracker");
            ImGui.BulletText(this.Settings.HasSyncedOnce
                ? $"Estimate: {this.Settings.EstimatedAmount:N0}"
                : "Not calibrated yet");
            if (this.Settings.LastTreeSyncUtc != DateTime.MinValue)
            {
                ImGui.BulletText(
                    $"Last tree sync: {this.Settings.LastTreeSyncUtc.ToLocalTime():g}");
            }

            if (this.Settings.SessionGainSinceSync > 0)
            {
                ImGui.BulletText(
                    $"Gains since sync: +{this.Settings.SessionGainSinceSync:N0}");
            }

            if (!string.IsNullOrEmpty(this.lastStatus))
            {
                ImGui.TextDisabled(this.lastStatus);
            }
        }

        private Vector2 ResolveOverlayPosition(UiElementBase rightPanel, bool inventoryOpen)
        {
            if (this.Settings.OverlayAnchor == HivebloodOverlayAnchor.CustomScreen)
            {
                return this.Settings.OverlayScreenPosition + this.Settings.OverlayOffset;
            }

            if (!inventoryOpen)
            {
                if (this.hasLastInventoryOverlayPosition)
                {
                    return this.lastInventoryOverlayPosition;
                }

                return this.Settings.OverlayScreenPosition + this.Settings.OverlayOffset;
            }

            var pos = this.ResolveInventoryAnchorPosition(rightPanel);
            this.lastInventoryOverlayPosition = pos;
            this.hasLastInventoryOverlayPosition = true;
            return pos;
        }

        private Vector2 ResolveInventoryAnchorPosition(UiElementBase rightPanel)
        {
            var panelPos = rightPanel.Position;
            var panelSize = rightPanel.Size;
            var basePos = this.Settings.OverlayAnchor switch
            {
                HivebloodOverlayAnchor.InventoryBottomNearGold => panelPos + new Vector2(6f, panelSize.Y - 32f),
                _ => panelPos,
            };

            return basePos + this.Settings.OverlayOffset;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
    }
}
