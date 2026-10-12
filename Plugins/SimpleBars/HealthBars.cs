// <copyright file="HealthBars.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SimpleBars
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    ///     <see cref="SimpleBars" /> plugin.
    /// </summary>
    public sealed class SimpleBars : PCore<SimpleBarsSettings>
    {
        private readonly List<string> textureToValidate = new()
        {
            "full_bar.png",
            "hollow_bar.png"
        };

        private int poiMonsterConfigToDelete = 0;
        private int poiMonsterConfigToAdd = 0;
        private float graduationsThickness = 0f;
        private Vector2 fontSize = Vector2.Zero;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        private string TexturesPath => Path.Join(this.DllDirectory, "Textures");

        private readonly TextureLoader textures = new();

        private readonly Dictionary<uint, Vector2> bPositions = new();

        private ActiveCoroutine? onAreaChange;

        /// <inheritdoc />
        public override void DrawSettings()
        {
            
            this.DrawSettingsTabs("SimpleBarsSettings",
                ("general", "Settings", () =>
                {
                    ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.turn_off_in_game_health_bars_for_best_result", "Turn off in game health bars for best result."));
                    ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.disable_the_built_in_healthbars_plugin_when_using_simplebars", "Disable the built-in HealthBars plugin when using SimpleBars."));
                        if (ImGui.BeginTable("common_config_table", 1))
                        {
                            ImGui.TableNextColumn();
                            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.draw_healthbars_in_town", "Draw healthbars in town", "Draw healthbars in town"), ref this.Settings.DrawInTown);
                            ImGui.TableNextColumn();
                            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.draw_healthbars_in_hideout", "Draw healthbars in hideout", "Draw healthbars in hideout"), ref this.Settings.DrawInHideout);
                            ImGui.TableNextColumn();
                            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.draw_healthbars_when_game_is_in_background", "Draw healthbars when game is in background", "Draw healthbars when game is in background"), ref this.Settings.DrawWhenGameInBackground);
                            ImGui.TableNextColumn();
                            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.interpolate_position", "Interpolate position", "Interpolate position"), ref this.Settings.InterpolatePosition);
                            ImGuiHelper.ToolTip(GameHelper.Localization.OverlayLocalization.T("ui.labels.enable_this_if_your_healthbar_is_stuttering", "Enable this if your healthbar is stuttering."));
                            if (this.Settings.InterpolatePosition)
                            {
                                if (ImGui.DragInt(GameHelper.Localization.OverlayLocalization.Title("ui.labels.interpolation_rate", "Interpolation Rate", "Interpolation Rate"), ref this.Settings.InterpolationRate, 1f, 1, 1000))
                                {
                                    if (this.Settings.InterpolationRate <= 0)
                                    {
                                        this.Settings.InterpolationRate = 1;
                                    }
                                    else if (this.Settings.InterpolationRate >= 1000)
                                    {
                                        this.Settings.InterpolationRate = 1000;
                                    }
                                }
                            }
                    
                            ImGui.TableNextColumn();
                            ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.white_magic_rare_unique", "white       magic      rare         unique"));
                            ImGui.DragInt4(GameHelper.Localization.OverlayLocalization.Title("ui.labels.cull_strike_health", "Cull Strike (%health)", "Cull Strike (%health)"), ref this.Settings.CullingStrikeRangePerRarity[0], 1, 0, 100);
                            ImGui.TableNextColumn();
                            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.use_gradient_textures_globally", "Use gradient textures globally", "Use gradient textures globally"), ref this.Settings.UseGradientBarsGlobal);
                            ImGui.TableNextColumn();
                            // ShowManaRatherThanESOnSelf removed in SimpleBars fork.
                            ImGui.EndTable();
                        }
                }),
                ("monsters", "Monsters", () =>
                {
                    if (ImGui.BeginTabBar("monster_config"))
                    {
                        foreach (var item in this.Settings.Monster)
                        {
                            if (ImGui.BeginTabItem(GameHelper.Localization.OverlayLocalization.T("ui.entity." + item.Key.ToLowerInvariant(), item.Key) + "###" + item.Key))
                            {
                                item.Value.Draw(false, this.textures, this.Settings.UseGradientBarsGlobal);
                                ImGui.EndTabItem();
                            }
                        }
                    
                        ImGui.EndTabBar();
                    }
                }),
                ("player", "Player", () =>
                {
                    if (ImGui.BeginTabBar("player_config"))
                    {
                        foreach (var item in this.Settings.Player)
                        {
                            if (ImGui.BeginTabItem(GameHelper.Localization.OverlayLocalization.T("ui.entity." + item.Key.ToLowerInvariant(), item.Key) + "###" + item.Key))
                            {
                                // Expose self-only controls and show live preview
                                item.Value.Draw(item.Key == "self", this.textures, this.Settings.UseGradientBarsGlobal);
                                ImGui.EndTabItem();
                            }
                        }
                    
                        ImGui.EndTabBar();
                    }
                }),
                ("poi", "Points of interest", () =>
                {
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                    if(ImGui.InputInt(GameHelper.Localization.OverlayLocalization.Title("ui.labels.group_number_poimonsterconfig", "Group Number", "Group Number##poimonsterconfig"), ref this.poiMonsterConfigToAdd) && this.poiMonsterConfigToAdd < 0)
                    {
                        this.poiMonsterConfigToAdd = 0;
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Button(GameHelper.Localization.OverlayLocalization.Title("ui.labels.add", "Add", "Add")))
                    {
                        this.Settings.POIMonster.TryAdd(this.poiMonsterConfigToAdd, new());
                    }
                    
                    if (ImGui.BeginTabBar("poimonster_config", ImGuiTabBarFlags.AutoSelectNewTabs))
                    {
                        foreach (var conf in this.Settings.POIMonster)
                        {
                            var text = conf.Key < 0 ? GameHelper.Localization.OverlayLocalization.T("ui.default", "Default") : GameHelper.Localization.OverlayLocalization.F("ui.group", "Group {0}", conf.Key);
                            var shouldNotDelete = true;
                            if (ImGui.BeginTabItem(text, ref shouldNotDelete, ImGuiTabItemFlags.NoAssumedClosure))
                            {
                                conf.Value.Draw(false, this.textures, this.Settings.UseGradientBarsGlobal);
                                ImGui.EndTabItem();
                            }
                    
                            if (conf.Key >= 0 && !shouldNotDelete)
                            {
                                this.poiMonsterConfigToDelete = conf.Key;
                                ImGui.OpenPopup("POIConfigHealthbarDeleteConfirmation");
                            }
                        }
                    
                        this.DrawConfirmationPopup();
                        ImGui.EndTabBar();
                    }
                }),
                ("diagnostics", "Diagnostics", () =>
                {
                    ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.enable_disable_plugin_to_reload_textures", "Enable/Disable plugin to reload textures."));
                    ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.F("ui.simplebars.textures", "Textures loaded: {0}", this.textures.TotalTexturesLoaded));
                }));
            
        }

        /// <inheritdoc />
        public override void DrawUI()
        {
            var cAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            var cWorldInstance = Core.States.InGameStateObject.CurrentWorldInstance;
            if ((!this.Settings.DrawInTown && cWorldInstance.AreaDetails.IsTown) ||
                (!this.Settings.DrawInHideout && cWorldInstance.AreaDetails.IsHideout))
            {
                return;
            }

            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            if (!this.Settings.DrawWhenGameInBackground && !Core.Process.Foreground)
            {
                return;
            }

            if (Core.States.InGameStateObject.GameUi.SkillTreeNodesUiElements.Count > 0)
            {
                return;
            }

            if (WorldSpaceBarVisibility.ShouldHide(Core.States.InGameStateObject.GameUi))
            {
                return;
            }

            this.UpdateOncePerDraw();
            foreach (var entity in cAreaInstance.AwakeEntities)
            {
                if (!entity.Value.IsValid || entity.Value.EntityState == EntityStates.Useless ||
                    entity.Value.EntityType == EntityTypes.Renderable ||
                    entity.Value.EntityState == EntityStates.PinnacleBossHidden)
                {
                    continue;
                }

                switch (entity.Value.EntityType)
                {
                    case EntityTypes.Player:
                        if (entity.Value.EntitySubtype == EntitySubtypes.PlayerOther)
                        {
                            if (entity.Value.EntityState == EntityStates.PlayerLeader)
                            {
                                this.DrawHealthbar(entity.Value, this.Settings.Player["leader"], (int)Rarity.Rare);
                            }
                            else
                            {
                                this.DrawHealthbar(entity.Value, this.Settings.Player["member"], (int)Rarity.Rare);
                            }
                        }
                        else
                        {
                            this.DrawHealthbar(entity.Value, this.Settings.Player["self"], (int)Rarity.Rare, true);
                        }

                        break;
                    case EntityTypes.Monster:
                        if (entity.Value.EntitySubtype == EntitySubtypes.POIMonster)
                        {
                            if (!this.Settings.POIMonster.TryGetValue(entity.Value.EntityCustomGroup, out var poiConfig))
                            {
                                poiConfig = this.Settings.POIMonster[-1];
                            }

                            this.DrawHealthbar(entity.Value, poiConfig,
                                entity.Value.TryGetComponent<ObjectMagicProperties>(out var oComp) ?
                                (int)oComp.Rarity :
                                (int)Rarity.Rare);
                        }
                        else if (entity.Value.EntityState == EntityStates.MonsterFriendly)
                        {
                            this.DrawHealthbar(entity.Value, this.Settings.Monster["friendly"], (int)Rarity.Rare);
                        }
                        else if (entity.Value.TryGetComponent<ObjectMagicProperties>(out var oComp))
                        {
                            switch (oComp.Rarity)
                            {
                                case Rarity.Normal:
                                    this.DrawHealthbar(entity.Value, this.Settings.Monster["white"], (int)Rarity.Normal);
                                    break;
                                case Rarity.Magic:
                                    this.DrawHealthbar(entity.Value, this.Settings.Monster["magic"], (int)Rarity.Magic);
                                    break;
                                case Rarity.Rare:
                                    this.DrawHealthbar(entity.Value, this.Settings.Monster["rare"], (int)Rarity.Rare);
                                    break;
                                case Rarity.Unique:
                                    this.DrawHealthbar(entity.Value, this.Settings.Monster["unique"], (int)Rarity.Unique);
                                    break;
                            }
                        }

                        break;
                }
            }
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
            this.textures.cleanup(this.TexturesPath);
            this.onAreaChange?.Cancel();
            this.onAreaChange = null;
        }

        /// <inheritdoc />
        public override void OnEnable(bool isGameOpened)
        {
            this.textures.Load(this.TexturesPath);
            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<SimpleBarsSettings>(content) ?? new SimpleBarsSettings();
            }

            for (var i = 0; i < this.textureToValidate.Count; i++)
            {
                if (!this.textures.TextureKeys.Contains(this.textureToValidate[i]))
                {
                    throw new Exception($"Missing texture file {this.textureToValidate[i]} in {this.TexturesPath} folder.");
                }
            }

            this.onAreaChange = CoroutineHandler.Start(this.OnAreaChange());
        }

        /// <inheritdoc />
        public override void SaveSettings()
        {
            var settingsDir = Path.GetDirectoryName(this.SettingPathname);
            if (!string.IsNullOrEmpty(settingsDir))
            {
                Directory.CreateDirectory(settingsDir);
            }
            var settingsData = JsonConvert.SerializeObject(this.Settings, Formatting.Indented);
            File.WriteAllText(this.SettingPathname, settingsData);
        }

        private void DrawHealthbar(Entity entity, Config healthbarConfig, int rarity, bool isSelf = false)
        {
            if (!healthbarConfig.Enable)
            {
                return;
            }

            if (!entity.TryGetComponent<Render>(out var rComp))
            {
                return;
            }

            var curPos = rComp.WorldPosition;
            curPos.Z -= rComp.ModelBounds.Z + healthbarConfig.Shift.Y;
            var location = Core.States.InGameStateObject.CurrentWorldInstance.WorldToScreen(curPos, curPos.Z);
            location.X += healthbarConfig.Shift.X;
            if (!entity.TryGetComponent<Life>(out var hComp))
            {
                return;
            }

            if (this.Settings.InterpolatePosition)
            {
                if (this.bPositions.TryGetValue(entity.Id, out var prevLocation))
                {
                    location = MathHelper.Lerp(prevLocation, location, this.Settings.InterpolationRate / 1000f);
                }

                this.bPositions[entity.Id] = location;
            }

            var ptr = ImGui.GetBackgroundDrawList();
            // Determine per-bar scales (self can override)
            var baseScale = (isSelf && healthbarConfig.UseIndividualBarScale) ? healthbarConfig.HealthScale : healthbarConfig.Scale;
            var baseHalf = baseScale / 2f;

            bool drawHealth = healthbarConfig.ShowHealthBar;
            bool drawES = hComp.EnergyShield.Total > 0 && healthbarConfig.ShowESBar;
            bool drawMana = isSelf && healthbarConfig.ShowManaBar && hComp.Mana.Total > 0;

            var start = location - baseHalf;
            var end = location + baseHalf;
            Vector2 textAnchor = start;

            var (hb_ptr, _, _) = this.textures.GetTexture(this.textureToValidate[0]);
            var hPercent = hComp.Health.CurrentInPercent();
            float hPct = MathF.Max(0f, MathF.Min(100f, (float)hPercent));
            var healthColor = (hPercent > this.Settings.CullingStrikeRangePerRarity[rarity] || !healthbarConfig.ShowCullStrike)
                ? ImGuiHelper.Color(healthbarConfig.HealthbarColor) : 0xFFFFFFFF;
            bool useGradient = this.Settings.UseGradientBarsGlobal || healthbarConfig.UseGradientBars;

            // Circle Dot Rendering Mode
            if (healthbarConfig.UseCircleDot)
            {
                var dlc = ImGui.GetBackgroundDrawList();
                float baseR = healthbarConfig.CircleRadius > 0 ? healthbarConfig.CircleRadius : MathF.Max(6f, baseScale.Y);
                float radius = baseR * healthbarConfig.CircleScale;
                float arcThick = MathF.Max(1f, healthbarConfig.CircleArcThickness * healthbarConfig.CircleScale);
                float borderThicknessCircle = MathF.Max(1f, ImGui.GetFontSize() / 12f) * healthbarConfig.BorderThicknessScale;
                // Background dot (optional, behind everything)
                if (healthbarConfig.CircleBackgroundRadius > 0f)
                {
                    dlc.AddCircleFilled(location, healthbarConfig.CircleBackgroundRadius, ImGuiHelper.Color(healthbarConfig.CircleBackgroundColor));
                }
                // Background circle (always)
                dlc.AddCircleFilled(location, radius, ImGuiHelper.Color(healthbarConfig.BackgroundColor));
                // Subtle border around circle (health container)
                dlc.AddCircle(location, radius, 0xFF000000, 64, borderThicknessCircle);

                if (drawHealth)
                {
                    float angle = (MathF.PI * 2f) * (hPercent / 100f);
                    float startAng = -MathF.PI / 2f;
                    dlc.PathClear();
                    dlc.PathArcTo(location, radius, startAng, startAng + angle, 64);
                    dlc.PathLineTo(location);
                    dlc.PathFillConvex(healthColor);
                }

                // ES top half arc
                if (drawES)
                {
                    float angStart = -MathF.PI; // left
                    float angle = MathF.PI * (hComp.EnergyShield.CurrentInPercent() / 100f);
                    float rArc = radius + MathF.Max(0f, healthbarConfig.CircleArcOffset) + (arcThick / 2f);
                    dlc.PathClear();
                    dlc.PathArcTo(location, rArc, angStart, angStart + angle, 32);
                    dlc.PathStroke(ImGuiHelper.Color(healthbarConfig.ESColor), ImDrawFlags.None, arcThick);
                }

                // Mana bottom half arc
                if (drawMana)
                {
                    float angStart = 0f; // right
                    float angle = MathF.PI * (hComp.Mana.CurrentInPercent() / 100f);
                    float rArc = radius + MathF.Max(0f, healthbarConfig.CircleArcOffset) + (arcThick / 2f);
                    dlc.PathClear();
                    dlc.PathArcTo(location, rArc, angStart, angStart + angle, 32);
                    dlc.PathStroke(ImGuiHelper.Color(healthbarConfig.ManaColor), ImDrawFlags.None, arcThick);
                }

                // Text: anchor to top of ES arc (top of circle)
                textAnchor = location - new Vector2(radius, radius);
                goto AfterBars; // skip rect-based logic
            }
            if (drawHealth)
            {
                ptr.AddRectFilled(start, end, ImGuiHelper.Color(healthbarConfig.BackgroundColor));
                if (useGradient)
                {
                    ptr.AddImage(hb_ptr, start, end - (Vector2.UnitX * baseScale * (100f - hPct) / 100f), Vector2.Zero, Vector2.One, healthColor);
                }
                else
                {
                    var hFillEnd = start + new Vector2(baseScale.X * (hPct / 100f), baseScale.Y);
                    ptr.AddRectFilled(start, hFillEnd, healthColor);
                }
            }

            float pad = 1f;
            float thickness = MathF.Max(1f, ImGui.GetFontSize() / 12f);

            // Precompute ES bar geometry (needed for drawing and graduations)
            var esScale = (isSelf && healthbarConfig.UseIndividualBarScale) ? healthbarConfig.ESScale : healthbarConfig.Scale;
            var esHalf = esScale / 2f;
            var esCenter = drawHealth
                ? location - new Vector2(0f, (baseScale.Y * 0.5f) + healthbarConfig.BarGap + (esScale.Y * 0.5f))
                : (drawMana
                    ? location - new Vector2(0f, (esScale.Y + 0f) * 0.5f)
                    : location);
            Vector2 esStart = esCenter - esHalf;
            Vector2 esEnd = esCenter + esHalf;

            // ===== ES ABOVE (single bar, gradient PNG) =====
            if (drawES)
            {
                var (esTex, _, _) = this.textures.GetTexture(this.textureToValidate[0]); // use FULL bar, not hollow
                float esPct = hComp.EnergyShield.CurrentInPercent();
                float esPctC = MathF.Max(0f, MathF.Min(100f, esPct));
                textAnchor = esStart;

                if (useGradient)
                {
                    ptr.AddImage(
                        esTex,
                        esStart,
                        esEnd - (Vector2.UnitX * esScale * (100f - esPctC) / 100f),
                        Vector2.Zero, Vector2.One,
                        ImGuiHelper.Color(healthbarConfig.ESColor));
                }
                else
                {
                    var esFillEnd = esStart + new Vector2(esScale.X * (esPctC / 100f), esScale.Y);
                    ptr.AddRectFilled(esStart, esFillEnd, ImGuiHelper.Color(healthbarConfig.ESColor));
                }

                // Border around ES
                ptr.AddRect(esStart - new Vector2(pad, pad), esEnd + new Vector2(pad, pad), 0xFF000000, 0f, ImDrawFlags.None, thickness * healthbarConfig.BorderThicknessScale);
            }

            // ===== MANA BELOW (single bar, gradient PNG) =====
            if (drawMana)
            {
                var (manaTex, _, _) = this.textures.GetTexture(this.textureToValidate[0]); // use FULL bar
                float manaPct = hComp.Mana.CurrentInPercent();
                float manaPctC = MathF.Max(0f, MathF.Min(100f, manaPct));

                float gap = healthbarConfig.BarGap;
                var manaScale = (isSelf && healthbarConfig.UseIndividualBarScale) ? healthbarConfig.ManaScale : healthbarConfig.Scale;
                var manaHalf = manaScale / 2f;
                var manaCenter = drawHealth
                    ? location + new Vector2(0f, (baseScale.Y * 0.5f) + gap + (manaScale.Y * 0.5f))
                    : (drawES
                        ? location + new Vector2(0f, (manaScale.Y + 0f) * 0.5f)
                        : location);
                Vector2 mStart = manaCenter - manaHalf;
                Vector2 mEnd = manaCenter + manaHalf;

                if (useGradient)
                {
                    ptr.AddImage(
                        manaTex,
                        mStart,
                        mEnd - (Vector2.UnitX * manaScale * (100f - manaPctC) / 100f),
                        Vector2.Zero, Vector2.One,
                        ImGuiHelper.Color(healthbarConfig.ManaColor));
                }
                else
                {
                    var mFillEnd = mStart + new Vector2(manaScale.X * (manaPctC / 100f), manaScale.Y);
                    ptr.AddRectFilled(mStart, mFillEnd, ImGuiHelper.Color(healthbarConfig.ManaColor));
                }

                // Border around Mana
                ptr.AddRect(mStart - new Vector2(pad, pad), mEnd + new Vector2(pad, pad), 0xFF000000, 0f, ImDrawFlags.None, thickness * healthbarConfig.BorderThicknessScale);
            }

            var tmp = start - Vector2.UnitY;

            // --- BLACK BORDER --- (only around health bar)
            float borderPad = 1f;
            float borderThickness = MathF.Max(1f, ImGui.GetFontSize() / 12f) * healthbarConfig.BorderThicknessScale;
            if (drawHealth)
            {
                ptr.AddRect(
                    start - new Vector2(borderPad, borderPad),
                    end   + new Vector2(borderPad, borderPad),
                    0xFF000000,
                    0f,
                    ImDrawFlags.None,
                    borderThickness
                );
            }

            // HP Graduations
            float gradStep = (isSelf && healthbarConfig.UseIndividualBarScale) ? (baseScale.X / (healthbarConfig.Graduations + 1f)) : healthbarConfig.GraduationsLocationStart;
            Vector2 gradEnd = (isSelf && healthbarConfig.UseIndividualBarScale) ? (Vector2.UnitY * baseScale.Y) : healthbarConfig.GraduationsLocationEnd;
            if (drawHealth && healthbarConfig.ShowHPGraduations)
            {
                for (var i = 0; i < healthbarConfig.Graduations; i++)
                {
                    tmp.X += gradStep;
                    ptr.AddLine(tmp, tmp + gradEnd, 0xFF000000, this.graduationsThickness);
                }
            }

            // ES Graduations (self only)
            if (isSelf && drawES && healthbarConfig.ShowESGraduations && healthbarConfig.ESGraduations > 0)
            {
                float esGradStep = esScale.X / (healthbarConfig.ESGraduations + 1f);
                Vector2 esGradEnd = Vector2.UnitY * esScale.Y;
                var esGradTmp = esStart - Vector2.UnitY;
                for (var i = 0; i < healthbarConfig.ESGraduations; i++)
                {
                    esGradTmp.X += esGradStep;
                    ptr.AddLine(esGradTmp, esGradTmp + esGradEnd, 0xFF000000, this.graduationsThickness);
                }
            }

AfterBars:
            if (healthbarConfig.ShowText)
            {
                var text = this.healthToHumanReadable(hComp.Health.Current + hComp.EnergyShield.Current);
                var size = ImGui.CalcTextSize(text);
                var tPad = new Vector2(2f, 2f);
                float safeGap = 1f;
                // Align left to the bar's left (textAnchor.X), and place fully above by safeGap
                var bgTopLeft = new Vector2(textAnchor.X, textAnchor.Y - (size.Y + (tPad.Y * 2f) + safeGap));
                var bgBottomRight = new Vector2(textAnchor.X + (size.X + (tPad.X * 2f)), textAnchor.Y - safeGap);
                var drawPos = bgTopLeft + tPad;
                if (healthbarConfig.ShowTextBackground)
                {
                    ptr.AddRectFilled(bgTopLeft, bgBottomRight, ImGuiHelper.Color(healthbarConfig.TextBackgroundColor));
                }
                ptr.AddText(drawPos, ImGuiHelper.Color(healthbarConfig.TextColor), text);
            }
        }

        private void UpdateOncePerDraw()
        {
            this.graduationsThickness = ImGui.GetFontSize() / 9f;
            this.fontSize = new(0f, ImGui.GetFontSize());
        }

        private string healthToHumanReadable(int value)
        {
            if (value >= 100000)
            {
                return $"{(value / 1000000f):0.00}M";

            }
            else if (value >= 100)
            {
                return $"{(value / 1000f):0.00}K";
            }
            else
            {
                return $"{value}";
            }
        }

        private void DrawConfirmationPopup()
        {
            ImGui.SetNextWindowPos(new Vector2(Core.Overlay.Size.Width / 3f, Core.Overlay.Size.Height / 3f));
            if (ImGui.BeginPopup("POIConfigHealthbarDeleteConfirmation"))
            {
                GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.F("ui.simplebars.delete", "Delete the health-bar settings for monster group {0}?", this.poiMonsterConfigToDelete));
                ImGui.Separator();
                if (ImGui.Button(GameHelper.Localization.OverlayLocalization.Title("ui.labels.yes", "Yes", "Yes"),
                    new Vector2(ImGui.GetContentRegionAvail().X / 2f, ImGui.GetTextLineHeight() * 2)))
                {
                    _ = this.Settings.POIMonster.Remove(poiMonsterConfigToDelete);
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button(GameHelper.Localization.OverlayLocalization.Title("ui.labels.no", "No", "No"), new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 2)))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private IEnumerator<Wait> OnAreaChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.bPositions.Clear();
            }
        }
    }
}


