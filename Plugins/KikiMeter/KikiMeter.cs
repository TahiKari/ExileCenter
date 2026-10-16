using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using KikiMeter.controllers;
using KikiMeter.render;
using Coroutine;
using GameHelper;
using GameHelper.CoroutineEvents;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States;
using GameHelper.RemoteObjects.States.InGameStateObjects;
using Newtonsoft.Json;
using ImGuiNET;

namespace KikiMeter
{
    public sealed class KikiMeter : PCore<KikiMeterSettings>
    {

        private readonly DamageTracker damageMeter = new();
        private double nextDamageSample;
        private double nextMapSave;
        private long mapPortalGeneration;
        private readonly DamageMeterRenderer panelRenderer = new();
        private ActiveCoroutine? onAreaChange;
        private SettingsDisplay? damageMeterDisplay;
        private string damageMeterDisplayError = "";

        private string SettingsPath => Path.Join(this.DllDirectory, "config", "KikiMeter.settings.json");

        public override void OnEnable(bool isGameOpened)
        {
            try
            {
                this.Settings = KikiMeterSettings.Load(this.DllDirectory);
            }
            catch
            {
                this.Settings = new KikiMeterSettings();
            }

            this.onAreaChange = CoroutineHandler.Start(OnAreaChange(), string.Empty, 0);
            this.damageMeter.Reset();
            this.Settings.DamageMeterMaps ??= new();
            this.damageMeter.BindMaps(this.Settings.DamageMeterMaps);
            this.nextDamageSample = 0;
            this.nextMapSave = 0;
            this.mapPortalGeneration = Core.States.InGameStateObject.CurrentAreaInstance.MapPortalGeneration;
            if (!File.Exists(SettingsPath)) this.SaveSettings();
        }

        public override void OnDisable()
        {
            this.damageMeter.PauseMap();
            if (this.damageMeter.MapDirty) this.SaveSettings();
            this.CloseDamageMeterDisplay();
            this.onAreaChange?.Cancel();
            this.onAreaChange = null;
            this.damageMeter.Reset();
        }

        public override void SaveSettings()
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
            this.damageMeter.MarkMapSaved();
        }

        public override void DrawSettings()
        {
            
            this.DrawSettingsTabs("KikiMeterSettings",
                ("meter", "Damage meter", () =>
                {
                    bool changed = ImGui.Checkbox(this.PluginText.Label("meter.show", "Show damage meter", "ShowDamageMeter"), ref this.Settings.ShowDamageMeter);
                    if (changed) { this.damageMeter.EndArea(); this.damageMeter.PauseMap(); }
                    changed |= ImGui.Checkbox(this.PluginText.Label("meter.detect_changes", "Detect available-skill changes", "DetectMeterSkills"), ref this.Settings.DamageMeterDetectSkillChanges);
                    ImGui.TextWrapped(this.PluginText.T("meter.detect_help", "Detects additions/removals of available skills after one second of stability. Saves a previous test of at least one second as a reference and starts a new measurement. Support gems, passives, and individual damage sources are not detected."));
                    if (ImGui.Checkbox(this.PluginText.Label("meter.separate", "Independent window (another screen)", "SeparateDamageMeter"), ref this.Settings.DamageMeterSeparateWindow))
                    {
                        this.Settings.DamageMeterWindowVisible = true;
                        this.damageMeterDisplayError = "";
                        changed = true;
                    }
                    if (this.Settings.DamageMeterSeparateWindow)
                    {
                        ImGui.TextWrapped(this.PluginText.T("meter.window_help", "Move the title bar to another monitor. Position and size are saved."));
                        if (ImGui.Button(this.PluginText.Label("meter.show_window", "Show window", "ShowMeterWindow")))
                        {
                            this.Settings.DamageMeterWindowVisible = true;
                            changed = true;
                        }
                        ImGui.SameLine();
                        if (ImGui.Button(this.PluginText.Label("meter.reset_window", "Reset position", "ResetMeterWindow")))
                        {
                            this.CloseDamageMeterDisplay();
                            this.Settings.DamageMeterWindowPositionSaved = false;
                            this.Settings.DamageMeterWindowVisible = true;
                            changed = true;
                        }
                    }
                    if (this.damageMeterDisplayError.Length > 0) ImGui.TextWrapped(this.PluginText.T("meter.window_error", "The independent window could not open. The meter has returned to the game screen.") + " " + this.damageMeterDisplayError);
                    if (!this.Settings.DamageMeterSeparateWindow)
                    {
                        changed |= ImGui.Checkbox(this.PluginText.Label("meter.lock", "Lock panel position", "LockDamageMeter"), ref this.Settings.DamageMeterLocked);
                        changed |= ImGui.DragFloat2(this.PluginText.Label("meter.position", "Position (X / Y)", "DamageMeterPosition"), ref this.Settings.DamageMeterPosition, 1f, 0f, 10000f);
                    }
                    changed |= ImGui.Checkbox(this.PluginText.Label("settings.draw_when_backgrounded", "Draw when game is in background", "DrawWhenGameInBackground"), ref this.Settings.DrawWhenGameInBackground);
                    changed |= ImGui.ColorEdit4(this.PluginText.Label("settings.dps_text_color", "DPS text color", "DpsTextColor"), ref this.Settings.DpsTextColor);
                    ImGui.TextWrapped(this.PluginText.T("meter.help", "Estimates damage from monsters' health and shield loss. Includes all nearby readable enemies and all damage sources, including other players. Unlock the panel to move its title bar. Combats end after three seconds without observed damage."));
                    if (ImGui.Button(this.PluginText.Label("meter.reset", "Reset", "ResetDamageMeterSettings"))) this.damageMeter.Reset();
                    if (changed)
                    {
                        this.SaveSettings();
                    }
                }));
        }

        public override void DrawUI()
        {
            if (!this.Settings.ShowDamageMeter) this.CloseDamageMeterDisplay();
            else if (!this.Settings.DamageMeterSeparateWindow) this.damageMeterDisplay?.Render(false);
            if (Core.States.GameCurrentState != GameStateTypes.InGameState && Core.States.GameCurrentState != GameStateTypes.EscapeState)
            {
                this.damageMeter.EndArea();
                this.damageMeter.PauseMap();
                if (this.damageMeter.MapDirty) this.SaveSettings();
                if (this.Settings.ShowDamageMeter && this.Settings.DamageMeterSeparateWindow) this.RenderDamageMeterDisplay();
                return;
            }

            InGameState inGame = Core.States.InGameStateObject;

            var overlaySize = new Vector2(Core.Overlay.Size.Width, Core.Overlay.Size.Height);

            if (this.Settings.ShowDamageMeter)
            {
                // The independent window shares this sampler, even while it has focus.
                if (this.Settings.DamageMeterSeparateWindow) this.RenderDamageMeterDisplay();
                else if (Core.Process.Foreground || this.Settings.DrawWhenGameInBackground ||
                    (!this.Settings.DamageMeterLocked && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
                    if (this.panelRenderer.RenderDamageMeter(this.damageMeter, this.Settings, this.PluginText, overlaySize)) this.SaveSettings();
                double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                if (now >= this.nextDamageSample)
                {
                    // Short-lived monsters need a live baseline before their final hit.
                    this.nextDamageSample = now + 1.0 / 30;
                    var area = inGame.CurrentAreaInstance;
                    var details = inGame.CurrentWorldInstance.AreaDetails;
                    bool wasMapActive = this.damageMeter.MapActive;
                    string previousMap = this.damageMeter.CurrentMap?.Id ?? "";
                    if (this.mapPortalGeneration != area.MapPortalGeneration)
                    {
                        if (this.damageMeter.CurrentMap?.Hash.StartsWith("sekhema:", StringComparison.Ordinal) != true)
                            this.damageMeter.FinishMap();
                        this.mapPortalGeneration = area.MapPortalGeneration;
                    }
                    if (SekhemaRunTracker.FloorNumber(details.Id) > 0 || details.Id == "G2_13")
                        this.damageMeter.VisitSekhema(area.SekhemaRun, area.AreaHash, details.Name, area.CurrentAreaLevel, Core.Process.Pid, now, this.Settings.DamageMeterTestName);
                    else if (!details.IsTown && !details.IsHideout && details.Id != "Abyss_Hub")
                        this.damageMeter.VisitMap(area.AreaHash, details.Name, area.CurrentAreaLevel, Core.Process.Pid, now, this.Settings.DamageMeterTestName);
                    else this.damageMeter.VisitHub();
                    if (this.Settings.DamageMeterDetectSkillChanges &&
                        inGame.CurrentAreaInstance.Player.TryGetComponent<Actor>(out var actor) &&
                        this.damageMeter.ObserveSkills(actor.ActiveSkills.Keys, now))
                    {
                        this.damageMeter.MarkMapSkillChange();
                        if (this.damageMeter.ComparisonSnapshot is { } before && DamageMeterRenderer.StoreDamageReference(this.Settings, before,
                            this.PluginText.T("meter.before_change", "Before skill change"))) this.SaveSettings();
                        this.damageMeter.RestartMeasurement();
                    }
                    this.damageMeter.BeginFrame(now);
                    foreach (var entity in inGame.CurrentAreaInstance.AwakeEntities.Values)
                    {
                        if (!entity.IsValid || entity.EntityType != EntityTypes.Monster ||
                            entity.EntityState == EntityStates.MonsterFriendly || entity.EntityState == EntityStates.PinnacleBossHidden) continue;
                        bool finalSample = entity.EntityState == EntityStates.Useless;
                        if (finalSample && !this.damageMeter.NeedsFinalSample(entity.Id, entity.Address)) continue;
                        if (!entity.TryGetComponent<Life>(out var life, false) || !life.IsParentValid(entity.Address)) continue;
                        // Take one last life reading only for a previously tracked enemy.
                        // The core stops refreshing dormant components before their final hit.
                        if (finalSample) life.Address = life.Address;
                        if (!finalSample || !life.IsAlive)
                            this.damageMeter.Observe(entity.Id, entity.Address, life.Health.Current, life.EnergyShield.Current);
                    }
                    this.damageMeter.EndFrame();
                    if (area.SekhemaRun.Completed && this.damageMeter.CurrentMap?.Hash == area.SekhemaRun.RunKey)
                        this.damageMeter.FinishMap();
                    if (this.damageMeter.MapDirty && (now >= this.nextMapSave ||
                        (wasMapActive && !this.damageMeter.MapActive) || previousMap != this.damageMeter.CurrentMap?.Id))
                    {
                        this.SaveSettings();
                        this.nextMapSave = now + 20;
                    }
                }
            }

        }

        private void CloseDamageMeterDisplay()
        {
            var display = this.damageMeterDisplay;
            this.damageMeterDisplay = null;
            display?.Dispose();
        }

        private void RenderDamageMeterDisplay()
        {
            try
            {
                if (this.damageMeterDisplay == null && this.Settings.DamageMeterWindowVisible)
                {
                    var placement = new GameHelper.Settings.State
                    {
                        SettingsWindowPositionSaved = this.Settings.DamageMeterWindowPositionSaved,
                        SettingsWindowX = this.Settings.DamageMeterWindowX, SettingsWindowY = this.Settings.DamageMeterWindowY,
                        SettingsWindowWidth = this.Settings.DamageMeterWindowWidth, SettingsWindowHeight = this.Settings.DamageMeterWindowHeight
                    };
                    this.damageMeterDisplay = new SettingsDisplay(Core.Overlay,
                        () => { if (this.panelRenderer.RenderDamageMeter(this.damageMeter, this.Settings, this.PluginText, ImGui.GetIO().DisplaySize, true)) this.SaveSettings(); },
                        () => { this.Settings.DamageMeterWindowVisible = false; this.SaveSettings(); }, placement,
                        () => this.PluginText.T("meter.window_title", "GameHelper · Damage meter"), bounds =>
                        {
                            this.Settings.DamageMeterWindowX = bounds.X; this.Settings.DamageMeterWindowY = bounds.Y;
                            this.Settings.DamageMeterWindowWidth = bounds.Width; this.Settings.DamageMeterWindowHeight = bounds.Height;
                            this.Settings.DamageMeterWindowPositionSaved = true;
                            this.SaveSettings();
                        }, new System.Drawing.Size(320, 240), new System.Drawing.Size(540, 580));
                }
                this.damageMeterDisplay?.Render(this.Settings.DamageMeterWindowVisible);
            }
            catch (Exception ex)
            {
                this.damageMeterDisplayError = ex.Message;
                try { this.CloseDamageMeterDisplay(); } catch { }
                this.Settings.DamageMeterSeparateWindow = false;
                this.SaveSettings();
                Console.WriteLine($"[KikiMeter.Display] {ex}");
            }
        }

        private IEnumerator<Wait> OnAreaChange()
        {
            for (; ; )
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.damageMeter.EndArea();
                this.damageMeter.PauseMap();
                if (this.damageMeter.MapDirty) this.SaveSettings();
                this.nextDamageSample = 0;
            }
        }

    }
}
