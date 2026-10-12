namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;
    using GameHelper;
    using GameHelper.RemoteObjects.UiElement;
    using ImGuiNET;

    public sealed partial class LootTrackerCore
    {
        private SettingsDisplay? trackerDisplay;
        private string trackerDisplayError = string.Empty;
        private bool applyTrackerPosition;
        private float detachedUiScale = 1f;
        private bool trackerPositionDirty;
        private bool trackerInteractionActive;
        private System.Numerics.Vector2 trackerModeButtonMin, trackerModeButtonMax;
        private System.Numerics.Vector2 trackerSize = new(200f, 80f);

        private void DrawDisplaySettings()
        {
            ImGui.SeparatorText(this.L("lt.placement", "Screen placement"));
            if (ImGui.Checkbox(this.L("lt.separate_window", "Independent window (another screen)"), ref this.Settings.SeparateWindow))
            {
                this.Settings.SeparateWindowVisible = true;
                this.applyTrackerPosition = true;
                this.trackerDisplayError = string.Empty;
                this.SaveSettings();
            }
            if (this.Settings.SeparateWindow)
            {
                ImGui.TextUnformatted(this.L("lt.separate_hint", "Drag the window title bar to any monitor. Its position and size are saved."));
                if (ImGui.Button(this.L("lt.show_window", "Show window"))) this.Settings.SeparateWindowVisible = true;
                if (ImGui.Button(this.L("lt.reset_window", "Reset window position")))
                {
                    this.CloseTrackerDisplay();
                    this.Settings.WindowPositionSaved = false;
                    this.Settings.SeparateWindowVisible = true;
                    this.SaveSettings();
                }
            }
            else
            {
                if (ImGui.Checkbox(this.L("lt.free_position", "Free position on the game screen"), ref this.Settings.FreePosition))
                    this.applyTrackerPosition = true;
                if (ImGui.Checkbox(this.L("lt.lock_position", "Lock position"), ref this.Settings.LockPosition))
                {
                    if (!this.Settings.LockPosition) this.Settings.FreePosition = true;
                    this.applyTrackerPosition = true;
                }
                ImGui.TextUnformatted(this.L("lt.move_hint", "Unlock, close the settings menu, then drag the LootTracker title bar. Lock it again when done."));
                if (this.Settings.FreePosition && ImGui.DragFloat2(this.L("lt.position_xy", "Position X / Y (px)"), ref this.Settings.OverlayPosition, 1f, 0f, 16000f, "%.0f"))
                {
                    this.Settings.OverlayPositionSaved = true;
                    this.applyTrackerPosition = true;
                }
                if (ImGui.Button(this.L("lt.reset_overlay", "Restore automatic position")))
                {
                    this.Settings.FreePosition = false;
                    this.Settings.LockPosition = true;
                    this.Settings.OverlayPositionSaved = false;
                    this.applyTrackerPosition = true;
                    this.SaveSettings();
                }
            }
            if (this.trackerDisplayError.Length > 0)
            {
                ImGui.TextUnformatted(this.L("lt.window_error", "The independent window could not open. The display has returned to the game screen."));
                ImGui.TextUnformatted(this.trackerDisplayError);
            }
            ImGui.Spacing();
        }

        private static System.Numerics.Vector2 ClampTrackerPosition(System.Numerics.Vector2 position,
            System.Numerics.Vector2 size, System.Numerics.Vector2 screen)
        {
            float x = float.IsFinite(position.X) ? position.X : 16f;
            float y = float.IsFinite(position.Y) ? position.Y : 16f;
            return new(Math.Clamp(x, 0f, Math.Max(0f, screen.X - Math.Min(screen.X, Math.Max(64f, size.X)))),
                Math.Clamp(y, 0f, Math.Max(0f, screen.Y - Math.Min(screen.Y, Math.Max(32f, size.Y)))));
        }

        private void PrepareTrackerPosition(System.Numerics.Vector2 anchor, System.Numerics.Vector2 pivot)
        {
            if (this.Settings.FreePosition && this.Settings.OverlayPositionSaved)
            {
                var position = ClampTrackerPosition(this.Settings.OverlayPosition, this.trackerSize, ImGui.GetIO().DisplaySize);
                ImGui.SetNextWindowPos(position, this.Settings.LockPosition || this.applyTrackerPosition ? ImGuiCond.Always : ImGuiCond.Appearing);
            }
            else ImGui.SetNextWindowPos(anchor, this.Settings.FreePosition ? ImGuiCond.Appearing : ImGuiCond.Always, pivot);
            this.applyTrackerPosition = false;
        }

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern IntPtr TrackerForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint TrackerWindowProcess(IntPtr window, out uint processId);

        private static bool IsTrackerAppForeground()
        {
            var window = TrackerForegroundWindow();
            return window != IntPtr.Zero && TrackerWindowProcess(window, out var processId) != 0 &&
                processId == (uint)Environment.ProcessId;
        }

        private bool ShouldDrawGameTracker(bool gameForeground)
        {
            bool held = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            if (!held) this.trackerInteractionActive = false;
            return gameForeground || IsTrackerAppForeground() ||
                (this.Settings.FreePosition && !this.Settings.LockPosition && held &&
                    (this.trackerInteractionActive || ImGui.GetIO().WantCaptureMouse));
        }

        private ImGuiWindowFlags TrackerWindowFlags(ImGuiWindowFlags flags)
        {
            if (this.Settings.FreePosition && !this.Settings.LockPosition)
                return flags & ~(ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs);
            // Only the mode button captures input on an otherwise locked game overlay.
            if (this.trackerModeButtonMax.X > this.trackerModeButtonMin.X &&
                this.trackerModeButtonMax.Y > this.trackerModeButtonMin.Y &&
                ImGui.GetCurrentContext() != IntPtr.Zero &&
                ImGui.IsMouseHoveringRect(this.trackerModeButtonMin, this.trackerModeButtonMax, false))
                return flags & ~ImGuiWindowFlags.NoInputs;
            return flags;
        }

        private void DrawTrackerModeButton()
        {
            bool detached = this.Settings.SeparateWindow;
            string label = this.L(detached ? "lt.game_mode" : "lt.desktop_mode", detached ? "Game screen" : "Another screen");
            ContinueTrackerRow(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2);
            if (ImGui.SmallButton(label + "##TrackerDisplayMode"))
            {
                this.Settings.SeparateWindow = !detached;
                this.Settings.SeparateWindowVisible = true;
                this.applyTrackerPosition = true;
                this.trackerDisplayError = string.Empty;
                this.SaveSettings();
            }
            if (!detached)
            {
                this.trackerModeButtonMin = ImGui.GetItemRectMin();
                this.trackerModeButtonMax = ImGui.GetItemRectMax();
            }
        }

        private void RecordTrackerPosition()
        {
            this.trackerSize = ImGui.GetWindowSize();
            this.trackerInteractionActive = this.Settings.FreePosition && !this.Settings.LockPosition &&
                ImGui.IsMouseDown(ImGuiMouseButton.Left) &&
                (this.trackerInteractionActive || ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows));
            if (!this.Settings.FreePosition) return;
            var position = ClampTrackerPosition(ImGui.GetWindowPos(), this.trackerSize, ImGui.GetIO().DisplaySize);
            if (position != ImGui.GetWindowPos()) ImGui.SetWindowPos(position);
            if (!this.Settings.OverlayPositionSaved || this.Settings.OverlayPosition != position)
            {
                this.Settings.OverlayPosition = position;
                this.Settings.OverlayPositionSaved = true;
                this.trackerPositionDirty = true;
            }
            if (this.trackerPositionDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                this.trackerPositionDirty = false;
                this.SaveSettings();
            }
        }

        private void CloseTrackerDisplay()
        {
            var display = this.trackerDisplay;
            this.trackerDisplay = null;
            display?.Dispose();
        }

        private void RenderTrackerDisplay()
        {
            try
            {
                if (this.trackerDisplay == null && this.Settings.SeparateWindowVisible)
                {
                    var placement = new GameHelper.Settings.State
                    {
                        SettingsWindowPositionSaved = this.Settings.WindowPositionSaved,
                        SettingsWindowX = this.Settings.WindowX, SettingsWindowY = this.Settings.WindowY,
                        SettingsWindowWidth = this.Settings.WindowWidth, SettingsWindowHeight = this.Settings.WindowHeight,
                    };
                    this.trackerDisplay = new SettingsDisplay(Core.Overlay, this.DrawDetachedTracker,
                        () => { this.Settings.SeparateWindowVisible = false; this.SaveSettings(); }, placement,
                        () => this.L("lt.window_title", "GameHelper · LootTracker"), bounds =>
                        {
                            this.Settings.WindowX = bounds.X; this.Settings.WindowY = bounds.Y;
                            this.Settings.WindowWidth = bounds.Width; this.Settings.WindowHeight = bounds.Height;
                            this.Settings.WindowPositionSaved = true;
                            this.SaveSettings();
                        }, new System.Drawing.Size(360, 240), new System.Drawing.Size(820, 400));
                }
                this.detachedUiScale = this.UiScaleFactor();
                this.trackerDisplay?.Render(this.Settings.SeparateWindowVisible);
            }
            catch (Exception ex)
            {
                this.trackerDisplayError = ex.Message;
                try { this.CloseTrackerDisplay(); } catch { }
                this.Settings.SeparateWindow = false;
                this.SaveSettings();
                Console.WriteLine($"[LootTracker.Display] {ex}");
            }
        }

        private void DrawDetachedTracker()
        {
            var size = ImGui.GetIO().DisplaySize;
            ImGui.SetNextWindowPos(System.Numerics.Vector2.Zero);
            ImGui.SetNextWindowSize(size);
            bool visible = GameHelper.Ui.ImGuiTheme.BeginWindow("##LootTrackerDesktop", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                ImGuiWindowFlags.NoSavedSettings);
            try
            {
                if (visible) this.DrawCompactContents(size.Y, this.detachedUiScale);
            }
            finally { ImGui.End(); }
            this.DrawSessionHistoryWindow();
            this.DrawSessionDetailWindow();
            this.DrawMapLootWindow();
        }

        // Game-UI scale factor applied to the bars' font and fixed metrics: the auto factor
        // (DisplaySize.Y / 1600 — the game's own UI height-scale base) times the manual UiScale knob,
        // so the overlay shrinks/grows with the HUD across resolutions instead of clipping its content.
        private float UiScaleFactor()
        {
            float auto = ImGui.GetIO().DisplaySize.Y / 1600f;
            return Math.Clamp(auto * this.Settings.UiScale, 0.5f, 3f);
        }

        // True when overlay values should be shown in Divine only: the user opted in AND a live
        // Divine→Exalted rate exists to convert by. While the rate is unknown we keep Exalted so the
        // numbers never blank out.
        private bool DivineOnly =>
            this.Settings.ShowPricesInDivineOnly && this.priceCache.DivineToExaltedRate > 0;

        // ── Map strip ────────────────────────────────────────────────────
        // A slim, click-through line pinned to the bottom of the game window (right side by default),
        // shown while on a map so it doesn't block the run: "<map> · <timer> · <+X ex>". Sits just
        // above the experience bar via a manual offset (BarBottomOffset) — GameHelper doesn't expose
        // the XP element's scaled screen rect to plugins, so we anchor to the viewport bottom edge.
        private void DrawMapBar()
        {
            // Anchor the strip's bottom edge to the experience-bar element (its real scaled screen rect,
            // so it tracks resolution / UI scale), on the chosen side, then lift it by BarBottomOffset so
            // it clears the bar. Falls back to the viewport bottom if the element can't be resolved.
            float anchorX, anchorY;
            if (TryGetExperienceBarRect(out var xpPos, out var xpSize))
            {
                anchorY = xpPos.Y - this.Settings.BarBottomOffset;
                anchorX = this.Settings.BarOnRight ? xpPos.X + xpSize.X : xpPos.X;
            }
            else
            {
                var vp = ImGui.GetMainViewport();
                const float margin = 8f;
                anchorX = this.Settings.BarOnRight ? vp.Pos.X + vp.Size.X - margin : vp.Pos.X + margin;
                anchorY = vp.Pos.Y + vp.Size.Y - this.Settings.BarBottomOffset;
            }

            var pivot = new System.Numerics.Vector2(this.Settings.BarOnRight ? 1f : 0f, 1f);
            this.PrepareTrackerPosition(new System.Numerics.Vector2(anchorX, anchorY), pivot);
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(Math.Max(200f, Math.Min(this.Settings.CompactWidth, ImGui.GetIO().DisplaySize.X - 16f)), 0f));
            ImGui.SetNextWindowBgAlpha(Math.Clamp(this.Settings.BarOpacity, 0f, 1f));
            var flags = ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoMove;
            flags = this.TrackerWindowFlags(flags);
            if (!GameHelper.Ui.ImGuiTheme.BeginWindow(this.L("lt.move_window", "LootTracker · drag to move") + "###loottracker_bar", flags))
            {
                ImGui.End();
                return;
            }

            ImGui.SetWindowFontScale(this.UiScaleFactor());

            var root = this.current == null ? null : this.RootRun(this.current);
            var gained = root == null ? this.CurrentGainedLive() : this.RunGained(root);
            double ex = this.ValueOf(gained, out _, out _);
            string name = this.current?.Name ?? "—";
            const float pad = 5f;    // space between an icon and its value

            // [map] name
            if (this.DrawInlineIcon("Map")) ImGui.SameLine(0f, pad);
            ImGui.TextWrapped(name);
            this.DrawTrackerModeButton();

            // [time] mm:ss
            ContinueTrackerRow(90f);
            if (this.DrawInlineIcon("Time")) ImGui.SameLine(0f, pad);
            ImGui.TextUnformatted(FormatDuration(root == null ? TimeSpan.Zero : this.RunDisplayTime(root)));

            this.DrawProfitCurrencyControls();
            for (int unit = 0, value = 0; unit < ProfitUnits.Length; unit++)
            {
                if ((this.SelectedProfitUnits & (1 << unit)) == 0) continue;
                this.DrawProfitBadge(ex, unit, this.UiScaleFactor(), value > 0);
                value++;
            }

            // <n>[normal] <n>[magic] <n>[rare] <n>[unique]  — monsters slain this run, by rarity.
            if (this.Settings.ShowKills && this.current != null)
            {
                var k = Enumerable.Range(0, 4).Select(i => this.RunFamily(root!).Sum(r => r.Kills[i])).ToArray();
                ContinueTrackerRow(90f);
                this.DrawKillStat("NormalMob", k[0]);
                ContinueTrackerRow(90f);
                this.DrawKillStat("MagicMob", k[1]);
                ContinueTrackerRow(90f);
                this.DrawKillStat("RareMob", k[2]);
                ContinueTrackerRow(90f);
                this.DrawKillStat("UniqueMob", k[3]);
            }

            this.RecordTrackerPosition();
            ImGui.End();
        }

        // (Map-strip unpriced "+N?" hint intentionally omitted — too noisy while running.)

        // Renders "<count><icon>" inline (count then the rarity icon), the layout used for the
        // kill tallies on the strip. Falls back to count-only text when the icon isn't loaded.
        private void DrawKillStat(string iconKey, int count)
        {
            ImGui.TextUnformatted(count.ToString());
            if (this.iconHandles.TryGetValue(iconKey, out var handle) && handle != IntPtr.Zero)
            {
                ImGui.SameLine(0f, 4f);
                var s = ImGui.GetTextLineHeight();
                ImGui.Image(handle, new System.Numerics.Vector2(s, s));
            }
        }

        // ── Compact hideout bar ──────────────────────────────────────────
        // A session readout pinned into the empty band above the bottom HUD; the sole hideout/town UI.
        // Its bottom edge sits at the same viewport line as the map strip (BarBottomOffset up from the
        // window bottom) so it clears the HUD. Its width is the user-set CompactWidth but is always
        // clamped to the experience-bar width so it can't spill past it; once narrower than the bar it's
        // pinned to the chosen side of the XP bar via the same BarOnRight knob as the map strip. A pure
        // read-out (the New-session button lives in the plugin settings now).
        private void DrawCompactBar()
        {
            float h = Math.Clamp(this.Settings.CompactHeight, 190f, 600f);
            float reqW = Math.Max(this.Settings.CompactWidth, 320f);
            float x, y, w, pivotX;
            if (TryGetExperienceBarRect(out var xpPos, out var xpSize))
            {
                w = Math.Min(xpSize.X, reqW);
                y = xpPos.Y - this.Settings.BarBottomOffset; // bottom edge lifted off the XP bar
                // Capped narrower than the XP bar — pin it to the chosen side of the bar.
                x = this.Settings.BarOnRight ? xpPos.X + xpSize.X : xpPos.X;
                pivotX = this.Settings.BarOnRight ? 1f : 0f;
            }
            else
            {
                var vp = ImGui.GetMainViewport();
                const float margin = 8f;
                w = Math.Max(200f, Math.Min(vp.Size.X - 16f, reqW));
                x = this.Settings.BarOnRight ? vp.Pos.X + vp.Size.X - margin : vp.Pos.X + margin;
                y = vp.Pos.Y + vp.Size.Y - this.Settings.BarBottomOffset;
                pivotX = this.Settings.BarOnRight ? 1f : 0f;
            }

            bool resize = this.Settings.FreePosition && !this.Settings.LockPosition;
            bool applySize = this.applyTrackerPosition;
            float scale = this.UiScaleFactor();
            h = Math.Min(ImGui.GetIO().DisplaySize.Y - 16f, Math.Max(h, (w < 480 * scale ? 240f : 190f) * scale));
            this.PrepareTrackerPosition(new System.Numerics.Vector2(x, y), new System.Numerics.Vector2(pivotX, 1f));
            ImGui.SetNextWindowSize(new System.Numerics.Vector2(w, h), resize && !applySize ? ImGuiCond.Appearing : ImGuiCond.Always);
            ImGui.SetNextWindowBgAlpha(Math.Clamp(this.Settings.BarOpacity, 0f, 1f));
            var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoNav |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;
            flags = this.TrackerWindowFlags(flags);
            if (resize) flags &= ~ImGuiWindowFlags.NoResize;
            ImGui.SetNextWindowSizeConstraints(new System.Numerics.Vector2(Math.Min(320f, ImGui.GetIO().DisplaySize.X), 160 * scale), ImGui.GetIO().DisplaySize);
            if (!GameHelper.Ui.ImGuiTheme.BeginWindow(this.L("lt.move_window", "LootTracker · drag to move") + "###loottracker_compact", flags))
            {
                ImGui.End();
                return;
            }

            this.DrawCompactContents(h, this.UiScaleFactor());

            if (resize)
            {
                var size = ImGui.GetWindowSize();
                if (this.Settings.CompactWidth != size.X || this.Settings.CompactHeight != size.Y)
                {
                    this.Settings.CompactWidth = size.X;
                    this.Settings.CompactHeight = size.Y;
                    this.trackerPositionDirty = true;
                }
            }
            this.RecordTrackerPosition();
            ImGui.End();
        }

        private static readonly (string Key, string Short, string Name, string Art)[] ProfitUnits =
        {
            ("exalted", "Ex", "Exalted Orb", "CurrencyAddModToRare"),
            ("chaos", "Ch", "Chaos Orb", "CurrencyRerollRare"),
            ("greater_exalted", "Ex M", "Greater Exalted Orb", "CurrencyAddModToRare2"),
            ("perfect_exalted", "Ex P", "Perfect Exalted Orb", "CurrencyAddModToRare3"),
            ("divine", "Div", "Divine Orb", "CurrencyModValues"),
            ("greater_chaos", "Ch M", "Greater Chaos Orb", "CurrencyRerollRare2"),
            ("perfect_chaos", "Ch P", "Perfect Chaos Orb", "CurrencyRerollRare3"),
        };

        private int SelectedProfitUnits => (this.Settings.ProfitCurrencies & 127) != 0 ? this.Settings.ProfitCurrencies & 127 : 1;

        private bool TryGetProfitRate(int unit, out double rate)
        {
            rate = 0;
            if (unit == 0) { rate = 1; return true; }
            if (unit == 4) rate = this.priceCache.DivineToExaltedRate;
            if ((!double.IsFinite(rate) || rate <= 0) &&
                !this.priceCache.TryGetExaltedPrice(ProfitUnits[unit].Name, out rate))
                this.priceCache.TryGetPriceByArtId(ProfitUnits[unit].Art, out rate);
            return double.IsFinite(rate) && rate > 0;
        }

        private string ProfitUnitLabel(int unit) => this.L("lt.currency." + ProfitUnits[unit].Key, ProfitUnits[unit].Name);

        private string ProfitUnitShort(int unit) => this.L("lt.currency_short." + ProfitUnits[unit].Key, ProfitUnits[unit].Short);

        private string FormatProfitCurrency(double exalted, int unit)
        {
            if (!this.TryGetProfitRate(unit, out var rate) || !double.IsFinite(exalted))
                return "— " + this.ProfitUnitShort(unit);
            double value = exalted / rate;
            if (!double.IsFinite(value)) return "— " + this.ProfitUnitShort(unit);
            return value.ToString(Math.Abs(value) < 1 && value != 0 ? "+0.####;-0.####;0" : "+0.##;-0.##;0") + " " + this.ProfitUnitShort(unit);
        }

        private void ProfitTooltip(double exalted)
        {
            if (!ImGui.IsItemHovered()) return;
            ImGui.BeginTooltip();
            for (int unit = 0; unit < ProfitUnits.Length; unit++)
                if ((this.SelectedProfitUnits & (1 << unit)) != 0)
                    ImGui.TextUnformatted(this.ProfitUnitLabel(unit) + " : " + this.FormatProfitCurrency(exalted, unit));
            ImGui.TextDisabled(this.L("lt.conversion_hint", "Equivalent values using cached market prices; amounts are not added together."));
            ImGui.EndTooltip();
        }

        private static void ContinueTrackerRow(float nextWidth)
        {
            float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
            if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + nextWidth <= right)
                ImGui.SameLine();
        }

        private void DrawProfitCurrencyControls()
        {
            for (int unit = 0; unit < ProfitUnits.Length; unit++)
            {
                string label = this.ProfitUnitShort(unit);
                if (unit > 0) ContinueTrackerRow(ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2);
                bool selected = (this.SelectedProfitUnits & (1 << unit)) != 0;
                if (selected) ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.20f, 0.37f, 0.61f, 1f));
                if (ImGui.SmallButton(label + "##unit"))
                {
                    this.Settings.ProfitCurrencies = 1 << unit;
                    this.SaveSettings();
                }
                if (selected) ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted(this.ProfitUnitLabel(unit));
                    ImGui.TextUnformatted(this.TryGetProfitRate(unit, out var rate)
                        ? this.LF("lt.exchange_rate", "1 unit = {0:0.####} Ex", rate)
                        : this.L("lt.rate_missing", "Market rate unavailable. Refresh prices in LootTracker settings."));
                    ImGui.EndTooltip();
                }
            }
            string multiple = this.L("lt.currencies", "Currencies...");
            ContinueTrackerRow(ImGui.CalcTextSize(multiple).X + ImGui.GetStyle().FramePadding.X * 2);
            if (ImGui.SmallButton(multiple)) ImGui.OpenPopup("profit_currencies");
            if (ImGui.BeginPopup("profit_currencies"))
            {
                ImGui.TextUnformatted(this.L("lt.currencies_hint", "Select the profit values to display together."));
                ImGui.Separator();
                for (int unit = 0; unit < ProfitUnits.Length; unit++)
                {
                    bool selected = (this.SelectedProfitUnits & (1 << unit)) != 0;
                    if (ImGui.Checkbox(this.ProfitUnitLabel(unit), ref selected))
                    {
                        int next = selected ? this.SelectedProfitUnits | (1 << unit) : this.SelectedProfitUnits & ~(1 << unit);
                        if (next != 0) { this.Settings.ProfitCurrencies = next; this.SaveSettings(); }
                    }
                }
                if (ImGui.Button(this.L("lt.all_currencies", "Show all")))
                {
                    this.Settings.ProfitCurrencies = 127;
                    this.SaveSettings();
                }
                ImGui.TextDisabled(this.L("lt.rate_missing_hint", "— means the conversion rate is unavailable."));
                ImGui.EndPopup();
            }
        }

        private void DrawTrackerMetric(string label, string value = "", double? profit = null)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextDisabled(label);
            if (profit.HasValue)
            {
                for (int unit = 0; unit < ProfitUnits.Length; unit++)
                {
                    if ((this.SelectedProfitUnits & (1 << unit)) == 0) continue;
                    string converted = this.FormatProfitCurrency(profit.Value, unit);
                    ImGui.TextColored(converted.StartsWith("—", StringComparison.Ordinal)
                        ? new System.Numerics.Vector4(0.65f, 0.68f, 0.74f, 1f)
                        : profit.Value >= 0 ? GreenCol : RedCol, converted);
                    this.ProfitTooltip(profit.Value);
                }
            }
            else ImGui.TextWrapped(value);
            ImGui.PopTextWrapPos();
        }

        private static readonly System.Numerics.Vector4[] ProfitUnitColors =
        {
            new(0.89f, 0.73f, 0.39f, 1f), new(0.91f, 0.56f, 0.59f, 1f),
            new(0.98f, 0.64f, 0.38f, 1f), new(0.98f, 0.84f, 0.57f, 1f),
            new(0.76f, 0.65f, 0.98f, 1f), new(0.46f, 0.80f, 0.78f, 1f),
            new(0.52f, 0.71f, 0.97f, 1f),
        };

        private void DrawProfitBadge(double exalted, int unit, float scale, bool flow)
        {
            string label = this.ProfitUnitShort(unit);
            string formatted = this.FormatProfitCurrency(exalted, unit);
            string amount = formatted[..^(label.Length + 1)];
            float pad = 6f * scale;
            float labelWidth = ImGui.CalcTextSize(label).X;
            float width = labelWidth + ImGui.CalcTextSize(amount).X + 5 * pad;
            if (flow) ContinueTrackerRow(width);
            var position = ImGui.GetCursorScreenPos();
            float available = Math.Max(1f, ImGui.GetContentRegionAvail().X);
            bool stacked = width > available;
            width = Math.Min(width, available);
            var size = new System.Numerics.Vector2(width, ImGui.GetTextLineHeight() * (stacked ? 2 : 1) + (stacked ? 3 : 2) * pad);
            var draw = ImGui.GetWindowDrawList();
            var accent = ProfitUnitColors[unit];
            draw.AddRectFilled(position, position + size,
                ImGui.ColorConvertFloat4ToU32(new(accent.X * 0.25f, accent.Y * 0.25f, accent.Z * 0.25f, 1f)), 4 * scale);
            draw.AddRect(position, position + size,
                ImGui.ColorConvertFloat4ToU32(new(accent.X, accent.Y, accent.Z, 0.65f)), 4 * scale);
            draw.AddText(position + new System.Numerics.Vector2(pad, pad), ImGui.ColorConvertFloat4ToU32(accent), label);
            float divider = position.X + labelWidth + 2 * pad;
            var amountPosition = new System.Numerics.Vector2(divider + pad, position.Y + pad);
            float amountSpace = width - labelWidth - 4 * pad;
            if (stacked)
            {
                float line = position.Y + ImGui.GetTextLineHeight() + 1.5f * pad;
                draw.AddLine(new(position.X + pad, line), new(position.X + width - pad, line),
                    ImGui.ColorConvertFloat4ToU32(new(accent.X, accent.Y, accent.Z, 0.5f)));
                amountPosition = new(position.X + pad, line + 0.5f * pad);
                amountSpace = width - 2 * pad;
            }
            else draw.AddLine(new(divider, position.Y + pad), new(divider, position.Y + size.Y - pad),
                ImGui.ColorConvertFloat4ToU32(new(accent.X, accent.Y, accent.Z, 0.5f)));
            float fontSize = ImGui.GetFontSize() * Math.Min(1f, Math.Max(1f, amountSpace) / Math.Max(1f, ImGui.CalcTextSize(amount).X));
            draw.AddText(ImGui.GetFont(), fontSize, amountPosition,
                ImGui.ColorConvertFloat4ToU32(formatted.StartsWith("—", StringComparison.Ordinal)
                    ? new System.Numerics.Vector4(0.65f, 0.68f, 0.74f, 1f) : exalted >= 0 ? GreenCol : RedCol), amount);
            ImGui.Dummy(size);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(this.ProfitUnitLabel(unit) + " : " + formatted);
        }

        private void DrawCompactContents(float h, float s) => this.DrawTrackerContents(h, s, false);

        private void DrawTrackerContents(float h, float s, bool showLoot)
        {
            ImGui.SetWindowFontScale(s);
            float width = ImGui.GetContentRegionAvail().X;
            this.SessionTotals(out var totalActive, out var totalEx);
            int maps = this.completed.Count(r => r.ParentHash.Length == 0);
            double perHour = totalActive.TotalHours > 0 ? totalEx / totalActive.TotalHours : 0;
            TimeSpan avgTime = maps > 0 ? totalActive / maps : TimeSpan.Zero;
            double avgProfit = maps > 0 ? totalEx / maps : 0;

            // Bound the header independently so the history always retains most of the panel.
            float headerLimit = Math.Max(1f, ImGui.GetContentRegionAvail().Y * 0.45f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(4 * s, 2 * s));
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new System.Numerics.Vector2(4 * s, 1 * s));
            ImGui.SetNextWindowSizeConstraints(System.Numerics.Vector2.Zero, new System.Numerics.Vector2(float.MaxValue, headerLimit));
            bool headerVisible = ImGui.BeginChild("tracker_header", System.Numerics.Vector2.Zero,
                ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysAutoResize);
            try
            {
                if (headerVisible)
                {
                    width = ImGui.GetContentRegionAvail().X;
                    ImGui.TextColored(GameHelper.Ui.ImGuiTheme.Accent, this.L("lt.dashboard", "LOOT TRACKER"));
                    this.DrawTrackerModeButton();
                    string timer = this.LF("lt.session_timer", "Session: {0}", FormatDuration(DateTime.UtcNow - this.sessionStartUtc));
                    if (ImGui.GetItemRectMax().X + ImGui.CalcTextSize(timer).X + 20 * s < ImGui.GetCursorScreenPos().X + width)
                    {
                        ImGui.SameLine(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(timer).X);
                        ImGui.TextDisabled(timer);
                    }
                    this.DrawProfitCurrencyControls();
                    string summary = this.L(this.Settings.ShowProfitSummary ? "lt.hide_summary" : "lt.show_summary",
                        this.Settings.ShowProfitSummary ? "Hide summary" : "Show summary");
                    ContinueTrackerRow(ImGui.CalcTextSize(summary).X + ImGui.GetStyle().FramePadding.X * 2);
                    if (ImGui.SmallButton(summary))
                    {
                        this.Settings.ShowProfitSummary = !this.Settings.ShowProfitSummary;
                        this.SaveSettings();
                    }
                    ImGui.Separator();

                    if (this.Settings.ShowProfitSummary)
                    {
                        int columns = width >= 650 * s ? 4 : width >= 280 * s ? 2 : 1;
                        if (ImGui.BeginTable("profit_summary", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
                        {
                            ImGui.TableNextColumn();
                            this.DrawTrackerMetric(this.L("lt.total", "Total"), profit: totalEx);
                            ImGui.TableNextColumn();
                            this.DrawTrackerMetric(this.L("lt.hourly_profit", "Profit / hour"), profit: perHour);
                            ImGui.TableNextColumn();
                            this.DrawTrackerMetric(this.L("lt.average_profit", "Average profit"), profit: avgProfit);
                            ImGui.TableNextColumn();
                            this.DrawTrackerMetric(this.LF("lt.maps", "Maps: {0}", maps), this.LF("lt.avg_time", "AVG Time: {0}", FormatDuration(avgTime)));
                            ImGui.EndTable();
                        }
                        ImGui.Separator();
                    }

                }
            }
            finally
            {
                ImGui.EndChild();
                ImGui.PopStyleVar(3);
            }
            width = ImGui.GetContentRegionAvail().X;

            int units = 0;
            float profitWidth = 120 * s;
            for (int unit = 0; unit < ProfitUnits.Length; unit++)
            {
                if ((this.SelectedProfitUnits & (1 << unit)) == 0) continue;
                units++;
                profitWidth = Math.Max(profitWidth, ImGui.CalcTextSize(this.FormatProfitCurrency(totalEx, unit)).X + 36 * s);
            }
            bool separateColumns = width >= 160 * s + 60 * s + units * profitWidth + (showLoot ? 64 * s : 0);
            float tableHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 2, ImGui.GetContentRegionAvail().Y);
            MapRun? toOpen = null;
            if (ImGui.BeginTable($"compact_runs_{this.SelectedProfitUnits}_{separateColumns}_{showLoot}", (separateColumns ? 2 + units : 3) + (showLoot ? 1 : 0),
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                    ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable | ImGuiTableFlags.NoSavedSettings,
                    new System.Numerics.Vector2(0, tableHeight)))
            {
                ImGui.TableSetupColumn(this.L("lt.col_map", "Map"), ImGuiTableColumnFlags.WidthStretch, separateColumns ? 2f : 1.4f);
                ImGui.TableSetupColumn(this.L("lt.col_time", "Time"), ImGuiTableColumnFlags.WidthFixed, 56 * s);
                if (separateColumns)
                {
                    for (int unit = 0; unit < ProfitUnits.Length; unit++)
                        if ((this.SelectedProfitUnits & (1 << unit)) != 0)
                            ImGui.TableSetupColumn(this.L("lt.col_profit", "Profit") + " · " + this.ProfitUnitShort(unit),
                                ImGuiTableColumnFlags.WidthFixed, profitWidth);
                }
                else ImGui.TableSetupColumn(this.L("lt.col_profit", "Profit"), ImGuiTableColumnFlags.WidthStretch, 3f);
                if (showLoot) ImGui.TableSetupColumn(this.L("lt.loot", "Loot"), ImGuiTableColumnFlags.WidthFixed, 64 * s);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                if (this.completed.Count == 0)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped(this.L("lt.no_maps", "No completed maps in this session yet."));
                }
                foreach (var r in this.completed.Where(r => r.ParentHash.Length == 0).Reverse().SelectMany(this.RunFamily))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextWrapped((r.ParentHash.Length == 0 ? "" : "    > ") + r.Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(FormatDuration(this.RunDisplayTime(r)));
                    double ex = this.ValueOf(this.RunGained(r), out _, out _);
                    for (int unit = 0, value = 0; unit < ProfitUnits.Length; unit++)
                    {
                        if ((this.SelectedProfitUnits & (1 << unit)) == 0) continue;
                        if (separateColumns || value == 0) ImGui.TableNextColumn();
                        this.DrawProfitBadge(ex, unit, s, !separateColumns && value > 0);
                        value++;
                    }
                    if (showLoot)
                    {
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"{this.L("lt.loot", "Loot")}##a{r.ProcessId}_{r.Hash}")) toOpen = r;
                    }
                }
                ImGui.EndTable();
            }
            if (toOpen != null)
            {
                this.lootMap = null;
                this.lootRun = toOpen;
            }
        }

        // Draws a loaded icon (icons\<key>.png) inline at the current text-line height so it lines up
        // with adjacent text. Returns false (drawing nothing) if the icon isn't loaded, so callers can
        // skip the trailing spacing.
        private bool DrawInlineIcon(string key)
        {
            if (!this.iconHandles.TryGetValue(key, out var handle) || handle == IntPtr.Zero)
            {
                return false;
            }

            var s = ImGui.GetTextLineHeight();
            ImGui.Image(handle, new System.Numerics.Vector2(s, s));
            return true;
        }

        // ── Soft binding to fork-only core members ───────────────────────
        // LootTracker may be loaded against a stock GameHelper whose ImportantUiElements lacks the
        // fork's additions (ExperienceBar, IsAnyLargePanelOpen). Binding to them by name via reflection
        // — rather than a direct member access — means a missing member degrades gracefully (the bar
        // anchors to the viewport instead of the XP bar; it no longer auto-hides on the Atlas) instead
        // of throwing MissingMethodException when the overlay method is JIT-compiled, which would blank
        // the whole plugin. The PropertyInfos are resolved once and cached.
        private static bool gameUiReflectionReady;
        private static PropertyInfo? expBarProp;
        private static PropertyInfo? largePanelProp;
        private static PropertyInfo? atlasProp;
        private static PropertyInfo? worldMapProp;

        private static void EnsureGameUiReflection(object gameUi)
        {
            if (gameUiReflectionReady)
            {
                return;
            }

            var t = gameUi.GetType();
            expBarProp = t.GetProperty("ExperienceBar");
            largePanelProp = t.GetProperty("IsAnyLargePanelOpen");
            // The endgame Atlas is a separate panel from the world-travel map and isn't always folded
            // into IsAnyLargePanelOpen by every fork, so we also probe these element members directly.
            atlasProp = t.GetProperty("Atlas");
            worldMapProp = t.GetProperty("WorldMapPanel");
            gameUiReflectionReady = true;
        }

        // True when a large blocking panel covers the screen (inventory / passive tree / world map /
        // endgame Atlas). We OR the core's own IsAnyLargePanelOpen (when present) with a direct check of
        // the Atlas / WorldMapPanel elements, because not every fork folds the endgame Atlas into that
        // flag. Members absent on the running core are simply skipped (compact bar just stays up).
        private static bool IsLargePanelOpen()
        {
            var gameUi = Core.States.InGameStateObject?.GameUi;
            if (gameUi == null)
            {
                return false;
            }

            EnsureGameUiReflection(gameUi);
            if (largePanelProp?.GetValue(gameUi) is bool open && open)
            {
                return true;
            }

            return IsElementVisible(atlasProp, gameUi) || IsElementVisible(worldMapProp, gameUi);
        }

        // Reads a UiElementBase-typed GameUi property by reflection and returns its IsVisible, false if
        // the member is absent / null / not a UiElementBase on the running core.
        private static bool IsElementVisible(PropertyInfo? prop, object gameUi)
        {
            return prop?.GetValue(gameUi) is UiElementBase el && el.Address != IntPtr.Zero && el.IsVisible;
        }

        // Scaled screen rect of the experience bar. Primary path resolves it by Flags fingerprint by
        // walking GameUi's UI tree ourselves (fork-independent — see LootTrackerCore.UiTree.cs); the
        // reflection path is a fallback for a fork that exposes GameUi.ExperienceBar directly.
        private bool TryGetExperienceBarRect(out System.Numerics.Vector2 pos, out System.Numerics.Vector2 size)
        {
            if (this.TryGetExperienceBarRectByFp(out pos, out size))
            {
                return true;
            }

            return TryGetExperienceBarRectByReflection(out pos, out size);
        }

        // Fallback: a fork whose ImportantUiElements exposes ExperienceBar (its UiElementBase already
        // carries the scaled rect). Absent on most forks, in which case this returns false.
        private static bool TryGetExperienceBarRectByReflection(out System.Numerics.Vector2 pos, out System.Numerics.Vector2 size)
        {
            pos = default;
            size = default;
            var gameUi = Core.States.InGameStateObject?.GameUi;
            if (gameUi == null)
            {
                return false;
            }

            EnsureGameUiReflection(gameUi);
            if (expBarProp?.GetValue(gameUi) is not UiElementBase xp ||
                xp.Address == IntPtr.Zero || !xp.IsVisible)
            {
                return false;
            }

            pos = xp.Position;
            size = xp.Size;
            return size.X > 1f && size.Y > 1f;
        }

        private static readonly System.Numerics.Vector4 GreenCol = new(0.5f, 0.9f, 0.5f, 1f);
        private static readonly System.Numerics.Vector4 RedCol = new(0.9f, 0.5f, 0.5f, 1f);

        // ── Snapshot / delta ─────────────────────────────────────────────
        // Aggregated MainInventory1 contents: itemPath -> total count (stack-aware). False if unreadable.
        private bool TrySnapshotInventory(out Dictionary<string, long> snap)
        {
            snap = new Dictionary<string, long>(StringComparer.Ordinal);
            if (!this.TryReadMainInventory(out _, out _, out _, out var items))
            {
                return false;
            }

            foreach (var (path, count) in items)
            {
                snap.TryGetValue(path, out var c);
                snap[path] = c + count;
            }

            return true;
        }

        // now - baseline, per item path; only non-zero deltas are kept.
        private static Dictionary<string, long> Diff(Dictionary<string, long> now, Dictionary<string, long> baseline)
        {
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in now)
            {
                baseline.TryGetValue(kv.Key, out var b);
                long delta = kv.Value - b;
                if (delta != 0) d[kv.Key] = delta;
            }

            foreach (var kv in baseline)
            {
                if (!now.ContainsKey(kv.Key)) d[kv.Key] = -kv.Value;
            }

            return d;
        }

        private static void MergeInto(Dictionary<string, long> acc, Dictionary<string, long> delta)
        {
            foreach (var kv in delta)
            {
                acc.TryGetValue(kv.Key, out var c);
                long v = c + kv.Value;
                if (v == 0) acc.Remove(kv.Key);
                else acc[kv.Key] = v;
            }
        }

        private static string LastSegment(string path)
        {
            int i = path.LastIndexOf('/');
            return i >= 0 && i < path.Length - 1 ? path[(i + 1)..] : path;
        }

        // ── Inventory raw read (self-contained; offsets documented in LootTrackerCore.cs) ──
        // Resolves MainInventory1 and returns the metadata path + stack count of every distinct item.
        private bool TryReadMainInventory(out IntPtr inventoryAddr, out int boxesX, out int boxesY,
            out List<(string Path, int Count)> items)
        {
            inventoryAddr = IntPtr.Zero;
            boxesX = 0;
            boxesY = 0;
            items = new List<(string, int)>();

            if (!this.EnsureProcess())
            {
                return false;
            }

            var serverData = Core.States.InGameStateObject.CurrentAreaInstance.ServerDataObject;
            if (serverData == null || serverData.Address == IntPtr.Zero)
            {
                return false;
            }

            // ServerData +0x48 -> vector<IntPtr>; element [0] is the player data block.
            if (!this.TryReadStdVector(serverData.Address + ServerDataPlayerVectorOffset, out var pdFirst, out _))
            {
                return false;
            }

            IntPtr playerData = this.ReadPtr(pdFirst);
            if (playerData == IntPtr.Zero)
            {
                return false;
            }

            // playerData +0x320 -> vector<InventoryArrayStruct> (stride 0x18).
            if (!this.TryReadStdVector(playerData + PlayerInventoriesVectorOffset, out var invFirst, out var invLast))
            {
                return false;
            }

            long count = ((long)invLast - (long)invFirst) / InventoryArrayStride;
            if (count <= 0 || count > 4096)
            {
                return false;
            }

            for (long i = 0; i < count; i++)
            {
                IntPtr entry = invFirst + (int)(i * InventoryArrayStride);
                int id = this.ReadInt(entry + InventoryArrayIdOffset);
                if (id == MainInventory1Id)
                {
                    inventoryAddr = this.ReadPtr(entry + InventoryArrayPtr0Offset);
                    break;
                }
            }

            if (inventoryAddr == IntPtr.Zero)
            {
                return false;
            }

            boxesX = this.ReadInt(inventoryAddr + InventoryTotalBoxesOffset);
            boxesY = this.ReadInt(inventoryAddr + InventoryTotalBoxesOffset + 4);

            // ItemList: vector<IntPtr> slot->invItemPtr mapping (length X*Y; Zero = empty; dups for big items).
            if (!this.TryReadStdVector(inventoryAddr + InventoryItemListOffset, out var itFirst, out var itLast))
            {
                return true; // empty inventory is still a valid read
            }

            long slots = ((long)itLast - (long)itFirst) / 8;
            if (slots <= 0 || slots > 4096)
            {
                return true;
            }

            var seen = new HashSet<long>();
            for (long i = 0; i < slots; i++)
            {
                IntPtr invItemPtr = this.ReadPtr(itFirst + (int)(i * 8));
                if (invItemPtr == IntPtr.Zero || !seen.Add(invItemPtr.ToInt64()))
                {
                    continue;
                }

                IntPtr entity = this.ReadPtr(invItemPtr + InventoryItemItemOffset);
                if (entity == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr detailsPtr = this.ReadPtr(entity + EntityDetailsPtrOffset);
                if (detailsPtr == IntPtr.Zero)
                {
                    continue;
                }

                string path = this.ReadStdWString(detailsPtr + EntityDetailsNameOffset);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                this.ReadItemFacts(entity, detailsPtr, out int stack, out int rarity, out var renderArt);
                items.Add((BuildItemKey(rarity, path, renderArt), stack));
            }

            return true;
        }

        // Reads an item's Stack.Count, Mods.Rarity and (for uniques) its rendered-icon art id in a SINGLE
        // walk of the entity's component name→index map (cheaper than one walk per component, run for every
        // inventory item at up to ~2 Hz). Defaults: stack 1 (non-stackable), rarity 0 (Normal — also when
        // the item lacks the Mods component, true for stackable currency), renderArt "" (read only for
        // uniques, where the shared base metapath can't tell one unique from another — see RenderItemArtOffset).
        private void ReadItemFacts(IntPtr entity, IntPtr detailsPtr, out int stack, out int rarity, out string renderArt)
        {
            stack = 1;
            rarity = 0;
            renderArt = string.Empty;

            // entity +0x10 -> vector<IntPtr> of component addresses (indexed by the name->index map).
            if (!this.TryReadStdVector(entity + EntityComponentListOffset, out var compFirst, out var compLast))
            {
                return;
            }

            long compCount = ((long)compLast - (long)compFirst) / 8;
            if (compCount <= 0 || compCount > 256)
            {
                return;
            }

            // EntityDetails +0x28 -> ComponentLookUpStruct; +0x28 -> ComponentsNameAndIndex (StdBucket;
            // its Data vector holds {NamePtr@+0, Index@+8} records, stride 0x10).
            IntPtr lookup = this.ReadPtr(detailsPtr + EntityComponentLookupOffset);
            if (lookup == IntPtr.Zero)
            {
                return;
            }

            if (!this.TryReadStdVector(lookup + ComponentLookupBucketOffset, out var niFirst, out var niLast))
            {
                return;
            }

            long niCount = ((long)niLast - (long)niFirst) / ComponentNameIndexStride;
            if (niCount <= 0 || niCount > 256)
            {
                return;
            }

            int stackIndex = -1, modsIndex = -1, renderIndex = -1;
            for (long i = 0; i < niCount && (stackIndex < 0 || modsIndex < 0 || renderIndex < 0); i++)
            {
                IntPtr rec = niFirst + (int)(i * ComponentNameIndexStride);
                IntPtr namePtr = this.ReadPtr(rec);
                if (namePtr == IntPtr.Zero)
                {
                    continue;
                }

                var name = this.ReadCString(namePtr, 40);
                if (stackIndex < 0 && name == "Stack")
                {
                    stackIndex = this.ReadInt(rec + 8);
                }
                else if (modsIndex < 0 && name == "Mods")
                {
                    modsIndex = this.ReadInt(rec + 8);
                }
                else if (renderIndex < 0 && name == "RenderItem")
                {
                    renderIndex = this.ReadInt(rec + 8);
                }
            }

            if (stackIndex >= 0 && stackIndex < compCount)
            {
                IntPtr c = this.ReadPtr(compFirst + (int)(stackIndex * 8));
                if (c != IntPtr.Zero)
                {
                    int n = this.ReadInt(c + StackCountOffset);
                    if (n > 0) stack = n;
                }
            }

            if (modsIndex >= 0 && modsIndex < compCount)
            {
                IntPtr c = this.ReadPtr(compFirst + (int)(modsIndex * 8));
                if (c != IntPtr.Zero)
                {
                    int r = this.ReadInt(c + ModsRarityOffset);
                    if (r >= 0 && r <= 3) rarity = r;
                }
            }

            // Only uniques need the rendered icon (to tell apart uniques sharing one base metapath); skip
            // the extra wstring read for everything else.
            if (rarity == 3 && renderIndex >= 0 && renderIndex < compCount)
            {
                IntPtr c = this.ReadPtr(compFirst + (int)(renderIndex * 8));
                if (c != IntPtr.Zero)
                {
                    var ddsPath = this.ReadStdWString(c + RenderItemArtOffset);
                    renderArt = ArtIdFromDdsPath(ddsPath);
                }
            }
        }

        // "Art/2DItems/.../PrecursorTabletDeliriumUnique1.dds" → "PrecursorTabletDeliriumUnique1" (the
        // language-independent art id poe.ninja keys by). Returns "" for an empty/odd path.
        private static string ArtIdFromDdsPath(string ddsPath)
        {
            if (string.IsNullOrEmpty(ddsPath)) return string.Empty;
            var seg = LastSegment(ddsPath);
            int dot = seg.LastIndexOf('.');
            return dot > 0 ? seg[..dot] : seg;
        }

        private static string FormatDuration(TimeSpan t)
        {
            if (t.TotalHours >= 1)
            {
                return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
            }

            return $"{t.Minutes:D2}:{t.Seconds:D2}";
        }

        // ── Raw process-memory helpers (mirrors RunecraftHelperCore; self-contained) ──
        private bool EnsureProcess()
        {
            int pid = (int)Core.Process.Pid;
            if (pid == 0)
            {
                if (this.handlePid != 0) this.ResetHandle();
                return false;
            }

            if (pid == this.handlePid && this.processHandle != IntPtr.Zero) return true;

            this.ResetHandle();
            this.processHandle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, pid);
            if (this.processHandle == IntPtr.Zero) return false;
            this.handlePid = pid;
            return true;
        }

        private void ResetHandle()
        {
            if (this.processHandle != IntPtr.Zero)
            {
                CloseHandle(this.processHandle);
                this.processHandle = IntPtr.Zero;
            }

            this.handlePid = 0;
        }

        private IntPtr ReadPtr(IntPtr addr)
        {
            if (addr == IntPtr.Zero) return IntPtr.Zero;
            var buf = new byte[8];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return IntPtr.Zero;
            long v = BitConverter.ToInt64(buf, 0);
            if (v < 0x10000 || v > 0x7FFFFFFFFFFF) return IntPtr.Zero;
            return (IntPtr)v;
        }

        private int ReadInt(IntPtr addr)
        {
            if (addr == IntPtr.Zero) return 0;
            var buf = new byte[4];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return 0;
            return BitConverter.ToInt32(buf, 0);
        }

        private bool TryReadStdVector(IntPtr addr, out IntPtr first, out IntPtr last)
        {
            first = IntPtr.Zero;
            last = IntPtr.Zero;
            var buf = new byte[16];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return false;
            first = (IntPtr)BitConverter.ToInt64(buf, 0);
            last = (IntPtr)BitConverter.ToInt64(buf, 8);
            if (first == IntPtr.Zero) return false;
            ulong f = (ulong)(long)first;
            if (f < 0x10000 || f > 0x7FFFFFFFFFFFul) return false;
            if ((long)last < (long)first) return false;
            return true;
        }

        // Null-terminated ASCII string (used for component names).
        private string ReadCString(IntPtr addr, int max)
        {
            if (addr == IntPtr.Zero) return string.Empty;
            var buf = new byte[max];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return string.Empty;
            int n = Array.IndexOf(buf, (byte)0);
            if (n < 0) n = max;
            return Encoding.ASCII.GetString(buf, 0, n);
        }

        // MSVC std::wstring: buffer ptr at +0x00 (or 8 chars inline if cap < 8), length at +0x10, capacity at +0x18.
        private string ReadStdWString(IntPtr addr)
        {
            var buf = new byte[0x20];
            if (!ReadProcessMemory(this.processHandle, addr, buf, (uint)buf.Length, out _)) return string.Empty;

            int len = BitConverter.ToInt32(buf, 0x10);
            if (len <= 0 || len > 256) return string.Empty;
            int cap = BitConverter.ToInt32(buf, 0x18);
            if (cap < len) return string.Empty;

            if (cap < 8)
            {
                int byteLen = Math.Min(len * 2, 16);
                return Encoding.Unicode.GetString(buf, 0, byteLen);
            }

            long ptr = BitConverter.ToInt64(buf, 0);
            if (ptr < 0x10000 || ptr > 0x7FFFFFFFFFFF) return string.Empty;
            var outBuf = new byte[len * 2];
            if (!ReadProcessMemory(this.processHandle, (IntPtr)ptr, outBuf, (uint)outBuf.Length, out _)) return string.Empty;
            return Encoding.Unicode.GetString(outBuf);
        }

        // ── P/Invoke ─────────────────────────────────────────────────────
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint dwSize, out int lpNumberOfBytesRead);
    }
}
