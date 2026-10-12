namespace Autopot
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using ClickableTransparentOverlay.Win32;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using ImGuiNET;
    using Nefarius.ViGEm.Client;
    using Nefarius.ViGEm.Client.Targets;
    using Nefarius.ViGEm.Client.Targets.Xbox360;
    using Newtonsoft.Json;

    public sealed class AutopotCore : PCore<AutopotSettings>
    {
        private readonly Stopwatch key1Cooldown = Stopwatch.StartNew();
        private readonly Stopwatch key2Cooldown = Stopwatch.StartNew();

        private VitalsSnapshot lastVitals;
        private string serviceStatus = "Stopped";
        private string statusDetail = string.Empty;
        private bool safetyLogoutTriggered;
        private readonly Stopwatch safetyLogoutCooldown = new();
        private bool safetyLogoutWaitingRecovery;
        private bool returnSawCharacterSelection;
        private bool capturePlayPosition;
        private bool captureKeyWasDown;
        private int returnInputStep;
        private bool returnControlHeld;
        private readonly Stopwatch returnInputTimer = new();
        private string returnStatus = "";
        private NativePoint returnClickPoint;
        private uint returnGamePid;
        private IntPtr returnGameWindow;

        private ViGEmClient? _vigemClient;
        private IXbox360Controller? _vigemController;
        private bool _vigemReady;
        private bool _vigemInitAttempted;
        private string _vigemStatus = string.Empty;
        private readonly object _vigemLock = new();

        private string SettingPathname => Path.Join(DllDirectory, "config", "settings.txt");

        public override void OnDisable()
        {
            ReleaseReturnControl();
            DisposeViGEm();
        }

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(SettingPathname))
            {
                var content = File.ReadAllText(SettingPathname);
                Settings = JsonConvert.DeserializeObject<AutopotSettings>(content) ?? new AutopotSettings();
            }

            if (Settings.BypassControllerMode)
                InitializeViGEm();
        }

        public override void SaveSettings()
        {
            var dir = Path.GetDirectoryName(SettingPathname);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(SettingPathname, JsonConvert.SerializeObject(Settings, Formatting.Indented));
        }

        private void InitializeViGEm()
        {
            if (_vigemReady || _vigemInitAttempted) return;
            _vigemInitAttempted = true;
            try
            {
                _vigemClient = new ViGEmClient();
                _vigemController = _vigemClient.CreateXbox360Controller();
                _vigemController.Connect();
                _vigemReady = true;
                _vigemStatus = "Virtual controller connected";
            }
            catch (Exception ex)
            {
                DisposeViGEm();
                _vigemStatus = $"Failed: {ex.Message}";
            }
        }

        private void DisposeViGEm()
        {
            try { _vigemController?.Disconnect(); } catch { }
            try { _vigemClient?.Dispose(); } catch { }
            _vigemController = null;
            _vigemClient = null;
            _vigemReady = false;
            _vigemInitAttempted = false;
        }

        private bool PressViGEmButton(Xbox360Button button)
        {
            if (!_vigemReady || _vigemController == null) return false;
            try
            {
                lock (_vigemLock)
                {
                    _vigemController.SetButtonState(button, true);
                    _vigemController.SubmitReport();
                }
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    try
                    {
                        lock (_vigemLock)
                        {
                            _vigemController?.SetButtonState(button, false);
                            _vigemController?.SubmitReport();
                        }
                    }
                    catch { }
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override void DrawSettings()
        {
            RefreshVitals();
            UpdateServiceStatus();
            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.enable_automatic_flasks", "Enable automatic flasks", "Activer les flacons automatiques##EnableAutoPot"), ref Settings.EnableAutoPot);
            ImGui.TextColored(serviceStatus == "Running" ? new Vector4(.35f, .85f, .6f, 1) : new Vector4(.7f, .72f, .77f, 1),
                LocalizeServiceStatus(serviceStatus));
            if (!string.IsNullOrEmpty(statusDetail)) ImGui.TextWrapped(LocalizeReason(statusDetail));
            ImGui.Separator();
            this.DrawSettingsTabs("AutopotSettings",
                ("autopot_flasks", "Flacons", DrawConfigurationColumn),
                ("autopot_safety", "Protection", DrawSafetySettings),
                ("autopot_keys", "Touches", DrawKeySettings),
                ("status", "État", DrawVitalsColumn));
        }
        public override void DrawUI()
        {
            // Keep ViGEm in sync with the bypass checkbox without needing a restart.
            if (Settings.BypassControllerMode && !_vigemReady)
                InitializeViGEm();
            else if (!Settings.BypassControllerMode && _vigemReady)
                DisposeViGEm();

            RefreshVitals();
            if (UpdateHideoutReturn()) return;
            UpdateServiceStatus();

            if (Settings.ShowVitalsOverlay && lastVitals.Valid)
                DrawVitalsOverlay();

            if (TrySafetyLogout())
                return;

            if (!Settings.EnableAutoPot)
                return;

            if (!CanExecute(out _))
                return;

            if (Core.GHSettings.EnableControllerMode && !Settings.BypassControllerMode)
                return;

            if (Core.States.InGameStateObject.GameUi.ChatParent.IsChatActive)
                return;

            if (!lastVitals.Valid)
                return;

            EvaluateTriggers();
        }

        private void DrawVitalsColumn()
        {
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.current_resources", "Current resources", "Ressources actuelles"));
            if (lastVitals.Valid)
            {
                DrawVitalBar("Vie", lastVitals.HpPercent, new Vector4(0.85f, 0.15f, 0.15f, 1f));
                DrawVitalBar("Bouclier d'énergie", lastVitals.EsPercent, new Vector4(0.92f, 0.92f, 0.92f, 1f));
                DrawVitalBar("Mana", lastVitals.MpPercent, new Vector4(0.2f, 0.45f, 0.95f, 1f));
            }
            else GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.resources_unavailable_enter_a_game_area_to_read_them", "Resources unavailable: enter a game area to read them."));

            ImGui.Spacing();
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.in_game_display", "In-game display", "Affichage en jeu"));
            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.show_resources_in_game", "Show resources in game", "Afficher les ressources en jeu"), ref Settings.ShowVitalsOverlay);

            ImGui.Spacing();
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.input_device", "Input device", "Périphérique de contrôle"));
            DrawDeviceStatus();

            ImGui.Spacing();
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.configured_keys", "Configured keys", "Touches configurées"));
            ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.F("ui.flask.key", "Flask {0}: {1}", 1, Settings.Key1));
            ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.F("ui.flask.key", "Flask {0}: {1}", 2, Settings.Key2));
        }

        private void DrawConfigurationColumn()
        {
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.flask_triggers", "Flask triggers", "Déclenchement des flacons"));
            GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.choose_the_resources_to_monitor_each_flask_uses_its_own_key_and_delay", "Choose the resources to monitor. Each flask uses its own key and delay."));
            ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
            DrawLogicModeCombo();
            ImGui.Spacing();
            DrawThresholdSliders();
            ImGui.Spacing();
            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.also_use_flasks_in_the_hideout", "Also use flasks in the hideout", "Utiliser aussi les flacons dans le repaire"), ref Settings.RunInHideout);
            GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.emergency_logout_is_configured_separately_in_protection", "Emergency logout is configured separately in Protection."));
        }

        private void DrawSafetySettings()
        {
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.emergency_logout", "Emergency logout", "Déconnexion d'urgence"));
            GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.disconnects_when_an_enabled_resource_reaches_its_threshold_this_protec", "Disconnects when an enabled resource reaches its threshold. This protection also works when automatic flasks are disabled."));
            if (ImGui.BeginTable("AutoPotSafety", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn(GameHelper.Localization.OverlayLocalization.Title("ui.labels.enabled", "Enabled", "Actif"), ImGuiTableColumnFlags.WidthFixed, 55);
                ImGui.TableSetupColumn(GameHelper.Localization.OverlayLocalization.Title("ui.labels.resource", "Resource", "Ressource"));
                ImGui.TableSetupColumn(GameHelper.Localization.OverlayLocalization.Title("ui.labels.disconnect_at", "Disconnect at", "Déconnexion à"), ImGuiTableColumnFlags.WidthStretch, 1.5f);
                ImGui.TableHeadersRow();
                DrawSafetyLogoutRow(ref Settings.HpDisconnectEnabled, ref Settings.HpDisconnectPercent, "hpdc", "Vie");
                DrawSafetyLogoutRow(ref Settings.EsDisconnectEnabled, ref Settings.EsDisconnectPercent, "esdc", "Bouclier d'énergie");
                DrawSafetyLogoutRow(ref Settings.MpDisconnectEnabled, ref Settings.MpDisconnectPercent, "mpdc", "Mana");
                ImGui.EndTable();
            }
            ImGui.Spacing();
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.safe_return", "Safe return", "Revenir en sécurité"));
            if (ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.safe_return_to_hideout", "Safe return to hideout", "Retour sécurisé au repaire"), ref Settings.ReturnToHideoutAfterLogout))
            {
                if (!Settings.ReturnToHideoutAfterLogout) Settings.HideoutReturnPending = false;
            }
            GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.after_logout_select_your_character_and_use_return_to_hideout_in_the_au", "After logout, select your character and use Return to hideout in the AutoPot panel. The button uses Ctrl + click on Play. If no hideout is available, the game returns you to town."));
            GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.returning_to_the_map_is_interrupted_while_the_hideout_return_is_pendin", "Returning to the map is interrupted while the hideout return is pending."));
            bool configured = HasReturnPosition(Settings.HideoutPlayPosition, Settings.HideoutPlayWindowSize);
            ImGui.TextColored(configured ? new Vector4(.35f, .85f, .6f, 1) : new Vector4(1, .75f, .3f, 1),
                configured ? GameHelper.Localization.OverlayLocalization.T("ui.labels.play_button_saved", "Play button saved") : GameHelper.Localization.OverlayLocalization.T("ui.labels.one_time_setup_required", "One-time setup required"));
            if (ImGui.Button(configured ? GameHelper.Localization.OverlayLocalization.T("ui.labels.move_saved_play_position", "Move saved Play position") : GameHelper.Localization.OverlayLocalization.T("ui.labels.save_play_button_position", "Save Play button position"))) capturePlayPosition = true;
            GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.at_character_selection_place_the_pointer_in_the_centre_of_play_and_pre", "At character selection, place the pointer in the centre of Play and press F8. This setup does not click or connect."));
            if (!string.IsNullOrEmpty(returnStatus)) ImGui.TextWrapped(returnStatus);
            ImGui.Spacing();
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.re_arm_protection", "Re-arm protection", "Réarmement de la protection"));
            ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X));
            ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.delay_after_logout", "Delay after logout"));
            ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X));
            ImGui.SliderInt("##SafetyLogoutCooldown", ref Settings.SafetyLogoutCooldownSeconds, 0, 600,
                Settings.SafetyLogoutCooldownSeconds <= 0 ? GameHelper.Localization.OverlayLocalization.T("ui.labels.disabled", "Disabled") : "%d s");
            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.re_arm_after_resources_recover", "Re-arm after resources recover", "Réarmer après récupération"), ref Settings.SafetyLogoutRequireRecovery);
            GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.prevents_repeated_logouts_when_you_reconnect_with_a_resource_still_bel", "Prevents repeated logouts when you reconnect with a resource still below its threshold."));
        }

        private void DrawKeySettings()
        {
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.flask_keys", "Flask keys", "Touches des flacons"));
            DrawHotkeyRow("key1", "Flacon de vie / hybride", ref Settings.Key1Enabled, ref Settings.Key1);
            ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X));
            ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.flask_1_delay", "Flask 1 delay"));
            ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X));
            ImGui.SliderInt("##Key1Delay", ref Settings.Key1DelayMs, 100, 10000, "%d ms");
            ImGui.Spacing();
            DrawHotkeyRow("key2", "Flacon de mana / utilitaire", ref Settings.Key2Enabled, ref Settings.Key2);
            ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X));
            ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.flask_2_delay", "Flask 2 delay"));
            ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X));
            ImGui.SliderInt("##Key2Delay", ref Settings.Key2DelayMs, 100, 10000, "%d ms");
            ImGui.Spacing();
            ImGui.SeparatorText(GameHelper.Localization.OverlayLocalization.Title("ui.labels.controller", "Controller", "Manette"));
            DrawDeviceStatus();
        }
        private void DrawLogicModeCombo()
        {
            ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.monitored_resources", "Monitored resources"));
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##LogicMode", DisplayLogicMode(Settings.LogicMode)))
            {
                foreach (var mode in LogicModeLabels.All)
                {
                    bool selected = Settings.LogicMode == mode;
                    if (ImGui.Selectable(DisplayLogicMode(mode), selected))
                        Settings.LogicMode = mode;
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        private void DrawThresholdSliders()
        {
            bool showHp = Settings.LogicMode is LogicMode.ManaAndLife or LogicMode.LifeOnly
                or LogicMode.EnergyShield or LogicMode.HybridLifeEs or LogicMode.ManaAndEs
                or LogicMode.LifeEsMana;
            bool showMp = Settings.LogicMode is LogicMode.ManaAndLife or LogicMode.ManaOnly
                or LogicMode.ManaAndEs or LogicMode.LifeEsMana;

            if (showHp)
            {
                string hpLabel = Settings.LogicMode switch
                {
                    LogicMode.EnergyShield => "ES",
                    LogicMode.ManaAndEs => "ES",
                    LogicMode.HybridLifeEs => "Vie + ES",
                    _ => "Vie",
                };
                if (hpLabel == "Vie") hpLabel = GameHelper.Localization.OverlayLocalization.T("ui.labels.life", "Life");
                else if (hpLabel == "Vie + ES") hpLabel = GameHelper.Localization.OverlayLocalization.T("ui.labels.life_energy_shield", "Life + energy shield");
                ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.flask_1_threshold", "Flask 1 threshold"));
                ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
                ImGui.SliderInt("##hpthresh", ref Settings.HpThresholdPercent, 1, 99, $"{hpLabel} %d%%");
            }

            if (showMp)
            {
                ImGui.TextUnformatted(GameHelper.Localization.OverlayLocalization.T("ui.labels.flask_2_threshold", "Flask 2 threshold"));
                ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
                ImGui.SliderInt("##mpthresh", ref Settings.MpThresholdPercent, 1, 99, "Mana %d%%");
            }
        }

        private static void DrawSafetyLogoutRow(ref bool enabled, ref int thresholdPercent, string id, string label)
        {
            label = GameHelper.Localization.OverlayLocalization.T(id == "hpdc" ? "ui.labels.life" : id == "esdc" ? "ui.labels.energy_shield" : "ui.labels.mana", label);
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Checkbox("##enabled" + id, ref enabled);
            ImGui.TableNextColumn(); ImGui.TextWrapped(label);
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(!enabled);
            ImGui.SetNextItemWidth(-1);
            ImGui.SliderInt("##threshold" + id, ref thresholdPercent, 1, 99, "%d %%");
            ImGui.EndDisabled();
        }
        private static void DrawHotkeyRow(string id, string label, ref bool enabled, ref VK key)
        {
            label = GameHelper.Localization.OverlayLocalization.T(id == "key1" ? "ui.labels.life_flask_hybrid_flask" : "ui.labels.mana_flask_utility_flask", label);
            ImGui.Checkbox(label + "##enabled" + id, ref enabled);
            ImGui.BeginDisabled(!enabled);
            ImGui.SetNextItemWidth(Math.Min(250, ImGui.GetContentRegionAvail().X));
            var tmp = key;
            if (ImGuiHelper.NonContinuousEnumComboBox("##" + id, ref tmp)) key = tmp;
            ImGui.EndDisabled();
        }
        private void DrawDeviceStatus()
        {
            bool vigem = InputDeviceStatus.IsViGEmBusInstalled();
            if (vigem)
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), GameHelper.Localization.OverlayLocalization.T("ui.labels.virtual_controller_driver_installed", "Virtual controller driver installed"));
            else
            {
                ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), GameHelper.Localization.OverlayLocalization.T("ui.labels.virtual_controller_driver_missing", "Virtual controller driver missing"));
                ImGui.SameLine();
                if (ImGui.SmallButton(GameHelper.Localization.OverlayLocalization.Title("ui.labels.download_driver", "Download driver", "Télécharger le pilote")))
                    System.Diagnostics.Process.Start(new ProcessStartInfo(InputDeviceStatus.ViGEmDownloadLink) { UseShellExecute = true });
            }

            if (InputDeviceStatus.IsControllerConnected())
                ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), GameHelper.Localization.OverlayLocalization.T("ui.labels.controller_detected", "Controller detected"));
            else
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), GameHelper.Localization.OverlayLocalization.T("ui.labels.no_controller_detected", "No controller detected"));

            ImGui.Spacing();
            if (Core.GHSettings.EnableControllerMode)
                ImGui.TextColored(new Vector4(1f, 0.75f, 0.2f, 1f), GameHelper.Localization.OverlayLocalization.T("ui.labels.game_in_controller_mode", "Game in controller mode"));
            else
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), GameHelper.Localization.OverlayLocalization.T("ui.labels.game_in_keyboard_mouse_mode", "Game in keyboard/mouse mode"));
            ImGuiHelper.ToolTip(GameHelper.Localization.OverlayLocalization.T("ui.labels.in_controller_mode_enable_the_virtual_controller_to_send_flask_buttons", "In controller mode, enable the virtual controller to send flask buttons."));

            ImGui.Spacing();
            ImGui.Checkbox(GameHelper.Localization.OverlayLocalization.Title("ui.labels.send_flasks_through_the_virtual_controller", "Send flasks through the virtual controller", "Envoyer les flacons via la manette virtuelle"), ref Settings.BypassControllerMode);
            ImGuiHelper.ToolTip(
                GameHelper.Localization.OverlayLocalization.T("ui.labels.creates_a_virtual_xbox_360_controller_via_vigembus_and_injects_button_", "Creates a virtual Xbox 360 controller via ViGEmBus and injects button presses\ninstead of keyboard events. Required when PoE2 is in controller mode.\nViGEmBus driver must be installed."));

            if (Settings.BypassControllerMode)
            {
                ImGui.Spacing();
                if (_vigemReady)
                    ImGui.TextColored(new Vector4(0.3f, 1f, 0.3f, 1f), $"ViGEm: {_vigemStatus}");
                else
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f),
                        $"ViGEm: {(_vigemStatus.Length > 0 ? _vigemStatus : "Initializing...")}");

                ImGui.Spacing();
                ImGui.TextDisabled(GameHelper.Localization.OverlayLocalization.T("ui.labels.life_flask_button", "Life flask button:"));
                DrawXboxButtonCombo("##ctrlbtn1", ref Settings.ControllerKey1Button);
                ImGui.TextDisabled(GameHelper.Localization.OverlayLocalization.T("ui.labels.mana_flask_button", "Mana flask button:"));
                DrawXboxButtonCombo("##ctrlbtn2", ref Settings.ControllerKey2Button);
            }
        }

        private static void DrawXboxButtonCombo(string id, ref Xbox360Button current)
        {
            var allButtons = new[]
            {
                Xbox360Button.A, Xbox360Button.B, Xbox360Button.X, Xbox360Button.Y,
                Xbox360Button.LeftShoulder, Xbox360Button.RightShoulder,
                Xbox360Button.Up, Xbox360Button.Down, Xbox360Button.Left, Xbox360Button.Right,
                Xbox360Button.Start, Xbox360Button.Back,
                Xbox360Button.LeftThumb, Xbox360Button.RightThumb,
            };
            if (ImGui.BeginCombo(id, current.ToString()))
            {
                foreach (var btn in allButtons)
                {
                    bool sel = current == btn;
                    if (ImGui.Selectable(btn.ToString(), sel))
                        current = btn;
                    if (sel) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }

        private static void DrawVitalBar(string label, int percent, Vector4 fillColor)
        {
            label = GameHelper.Localization.OverlayLocalization.T(label == "Vie" ? "ui.labels.life" : label is "Bouclier d'énergie" or "ES" ? "ui.labels.energy_shield" : "ui.labels.mana", label);
            ImGui.Text($"{label}");
            var avail = ImGui.GetContentRegionAvail().X;
            var barHeight = 18f;
            var pos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var bgMin = pos;
            var bgMax = new Vector2(pos.X + avail, pos.Y + barHeight);
            drawList.AddRectFilled(bgMin, bgMax, ImGuiHelper.Color(new Vector4(0.15f, 0.15f, 0.15f, 1f)), 3f);
            float fillW = avail * Math.Clamp(percent, 0, 100) / 100f;
            if (fillW > 0)
                drawList.AddRectFilled(bgMin, new Vector2(pos.X + fillW, pos.Y + barHeight), ImGuiHelper.Color(fillColor), 3f);
            var text = $"{percent}%";
            var textSize = ImGui.CalcTextSize(text);
            drawList.AddText(new Vector2(pos.X + (avail - textSize.X) * 0.5f, pos.Y + (barHeight - textSize.Y) * 0.5f),
                ImGuiHelper.Color(new Vector4(1f, 1f, 1f, 1f)), text);
            ImGui.Dummy(new Vector2(avail, barHeight + 4f));
        }

        private void DrawVitalsOverlay()
        {
            ImGui.SetNextWindowBgAlpha(0.55f);
            ImGui.SetNextWindowSize(new Vector2(220, 0), ImGuiCond.FirstUseEver);
            if (!GameHelper.Ui.ImGuiTheme.BeginWindow($"AutoPot · Ressources###AutopotVitals",
                    ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.End();
                return;
            }

            DrawVitalBar("Vie", lastVitals.HpPercent, new Vector4(0.85f, 0.15f, 0.15f, 1f));
            DrawVitalBar("ES", lastVitals.EsPercent, new Vector4(0.92f, 0.92f, 0.92f, 1f));
            DrawVitalBar("Mana", lastVitals.MpPercent, new Vector4(0.2f, 0.45f, 0.95f, 1f));
            ImGui.End();
        }

        private void EvaluateTriggers()
        {
            bool key1Trigger = Settings.Key1Enabled && ShouldTriggerKey1(lastVitals);
            bool key2Trigger = Settings.Key2Enabled && ShouldTriggerKey2(lastVitals);

            if (key1Trigger && key1Cooldown.ElapsedMilliseconds >= Settings.Key1DelayMs)
            {
                bool sent = _vigemReady
                    ? PressViGEmButton(Settings.ControllerKey1Button)
                    : MiscHelper.KeyUp(Settings.Key1);
                if (sent) key1Cooldown.Restart();
            }

            if (key2Trigger && key2Cooldown.ElapsedMilliseconds >= Settings.Key2DelayMs)
            {
                bool sent = _vigemReady
                    ? PressViGEmButton(Settings.ControllerKey2Button)
                    : MiscHelper.KeyUp(Settings.Key2);
                if (sent) key2Cooldown.Restart();
            }
        }

        private bool ShouldTriggerKey1(VitalsSnapshot v) => Settings.LogicMode switch
        {
            LogicMode.ManaAndLife => v.HpPercent <= Settings.HpThresholdPercent,
            LogicMode.LifeOnly => v.HpPercent <= Settings.HpThresholdPercent,
            LogicMode.EnergyShield => v.EsPercent <= Settings.HpThresholdPercent,
            LogicMode.HybridLifeEs => v.HybridPercent <= Settings.HpThresholdPercent,
            LogicMode.ManaOnly => false,
            LogicMode.ManaAndEs => v.EsPercent <= Settings.HpThresholdPercent,
            LogicMode.LifeEsMana => v.HpPercent <= Settings.HpThresholdPercent || v.EsPercent <= Settings.HpThresholdPercent,
            _ => false,
        };

        private bool ShouldTriggerKey2(VitalsSnapshot v) => Settings.LogicMode switch
        {
            LogicMode.ManaAndLife => v.MpPercent <= Settings.MpThresholdPercent,
            LogicMode.LifeOnly => false,
            LogicMode.EnergyShield => false,
            LogicMode.HybridLifeEs => false,
            LogicMode.ManaOnly => v.MpPercent <= Settings.MpThresholdPercent,
            LogicMode.ManaAndEs => v.MpPercent <= Settings.MpThresholdPercent,
            LogicMode.LifeEsMana => v.MpPercent <= Settings.MpThresholdPercent,
            _ => false,
        };

        private bool TrySafetyLogout()
        {
            if (safetyLogoutTriggered)
                return true;

            if (!Settings.HpDisconnectEnabled && !Settings.EsDisconnectEnabled && !Settings.MpDisconnectEnabled)
                return false;

            if (!IsSafetyLogoutRearmed())
                return false;

            if (!CanSafetyLogout(out _))
                return false;

            if (!lastVitals.Valid)
                return false;

            if (Settings.HpDisconnectEnabled && lastVitals.HpPercent <= Settings.HpDisconnectPercent)
            {
                TriggerSafetyLogout("HP");
                return true;
            }

            if (Settings.EsDisconnectEnabled && lastVitals.HasEnergyShield
                && lastVitals.EsPercent <= Settings.EsDisconnectPercent)
            {
                TriggerSafetyLogout("ES");
                return true;
            }

            if (Settings.MpDisconnectEnabled && lastVitals.MpPercent <= Settings.MpDisconnectPercent)
            {
                TriggerSafetyLogout("MP");
                return true;
            }

            return false;
        }

        private void TriggerSafetyLogout(string vital)
        {
            safetyLogoutTriggered = true;
            safetyLogoutCooldown.Restart();
            safetyLogoutWaitingRecovery = Settings.SafetyLogoutRequireRecovery;
            MiscHelper.KillTCPConnectionForProcess(Core.Process.Pid);
            Settings.HideoutReturnPending = Settings.ReturnToHideoutAfterLogout;
            returnSawCharacterSelection = false;
            PersistReturnState();
            statusDetail = $"Déconnexion d'urgence déclenchée ({vital}).";
        }

        private void PersistReturnState()
        {
            try { SaveSettings(); }
            catch (Exception ex) { Console.WriteLine($"[AutoPot.Return] Could not save return state: {ex.Message}"); }
        }

        internal static bool HasReturnPosition(Vector2 point, Vector2 size) =>
            float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(size.X) && float.IsFinite(size.Y) &&
            size.X >= 160 && size.Y >= 100 && point.X >= 0 && point.Y >= 0 && point.X < size.X && point.Y < size.Y;

        internal static bool ShouldProtectHideoutReturn(bool enabled, bool pending, bool sawSelection,
            GameStateTypes state, bool alive, bool town, bool hideout) =>
            enabled && pending && sawSelection && state == GameStateTypes.InGameState && alive && !town && !hideout;

        private bool UpdateHideoutReturn()
        {
            if (!Settings.HideoutReturnPending && !capturePlayPosition && returnInputStep == 0 && !returnControlHeld) return false;
            var state = Core.States.GameCurrentState;
            var window = GetReturnGameWindow();
            if (returnControlHeld && returnInputStep == 0) ReleaseReturnControl();
            if (!Settings.ReturnToHideoutAfterLogout && Settings.HideoutReturnPending)
            {
                Settings.HideoutReturnPending = false;
                PersistReturnState();
            }
            if (state == GameStateTypes.SelectCharacterState && Settings.HideoutReturnPending) returnSawCharacterSelection = true;
            if (Settings.HideoutReturnPending && returnSawCharacterSelection && state == GameStateTypes.InGameState && lastVitals.Valid)
            {
                var area = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
                if (area.IsTown || area.IsHideout)
                {
                    Settings.HideoutReturnPending = false;
                    returnSawCharacterSelection = false;
                    returnStatus = "Retour dans une zone sûre confirmé.";
                    PersistReturnState();
                }
                else if (ShouldProtectHideoutReturn(Settings.ReturnToHideoutAfterLogout, Settings.HideoutReturnPending,
                    returnSawCharacterSelection, state, lastVitals.HpPercent > 0, area.IsTown, area.IsHideout))
                {
                    // Returning normally resumes the map. Disconnect before resuming
                    // combat, then leave the supported Ctrl+Play action to the user.
                    returnSawCharacterSelection = false;
                    safetyLogoutTriggered = true;
                    MiscHelper.KillTCPConnectionForProcess(Core.Process.Pid);
                    returnStatus = "Reprise de carte interrompue. Utilise « Retour au repaire » ou Ctrl + clic sur « Jouer ».";
                    return true;
                }
            }
            if (capturePlayPosition)
            {
                bool pressed = (ReturnKeyState(0x77) & 0x8000) != 0; // F8
                if (pressed && !captureKeyWasDown && state == GameStateTypes.SelectCharacterState &&
                    window != IntPtr.Zero && ReturnForegroundWindow() == window && ReturnGetCursor(out var cursor) &&
                    ReturnScreenToClient(window, ref cursor) && ReturnClientRect(window, out var rect))
                {
                    var point = new Vector2(cursor.X, cursor.Y);
                    var size = new Vector2(rect.Right - rect.Left, rect.Bottom - rect.Top);
                    if (HasReturnPosition(point, size))
                    {
                        Settings.HideoutPlayPosition = point;
                        Settings.HideoutPlayWindowSize = size;
                        capturePlayPosition = false;
                        returnStatus = "Bouton Jouer mémorisé. Le retour au repaire est prêt.";
                        PersistReturnState();
                    }
                }
                captureKeyWasDown = pressed;
                if ((ReturnKeyState(0x1B) & 0x8000) != 0) capturePlayPosition = false;
            }
            if (returnInputStep > 0)
            {
                if (state != GameStateTypes.SelectCharacterState || window == IntPtr.Zero || ReturnForegroundWindow() != window)
                {
                    ReleaseReturnControl();
                    returnInputStep = 0;
                    return false;
                }
                if (returnInputTimer.ElapsedMilliseconds > 2000)
                {
                    ReleaseReturnControl();
                    returnInputStep = 0;
                    returnStatus = "Retour interrompu. Relâche les touches puis réessaie.";
                }
                else if (returnInputStep == 1 && returnInputTimer.ElapsedMilliseconds >= 120 && ReturnInputsReleased(false))
                {
                    if (!ReturnClientRect(window, out var rect) || !ReturnPositionMatches(rect))
                    {
                        returnInputStep = 0;
                        returnStatus = "La taille du jeu a changé : mémorise à nouveau le bouton Jouer avec F8.";
                    }
                    else
                    {
                        var point = new NativePoint { X = (int)Settings.HideoutPlayPosition.X, Y = (int)Settings.HideoutPlayPosition.Y };
                        if (!ReturnClientToScreen(window, ref point) || !ReturnSetCursor(point.X, point.Y) ||
                            ReturnSendInput(1, new[] { ReturnKeyInput(0) }, Marshal.SizeOf<ReturnInput>()) != 1)
                        {
                            returnInputStep = 0;
                            returnStatus = "Le jeu n'a pas accepté le retour. Utilise Ctrl + clic sur Jouer.";
                        }
                        else
                        {
                            returnControlHeld = true;
                            returnClickPoint = point;
                            returnInputStep = 2;
                            returnInputTimer.Restart();
                        }
                    }
                }
                else if (returnInputStep == 2 && returnInputTimer.ElapsedMilliseconds >= 100)
                {
                    if (!ReturnGetCursor(out var cursor) || cursor.X != returnClickPoint.X || cursor.Y != returnClickPoint.Y ||
                        !ReturnInputsReleased(true) || !ReturnClientRect(window, out var rect) || !ReturnPositionMatches(rect))
                    {
                        ReleaseReturnControl();
                        returnInputStep = 0;
                        returnStatus = "Retour annulé : le curseur ou une touche a changé.";
                    }
                    else
                    {
                        uint sent = ReturnSendInput(2, new[] { ReturnMouseInput(0x0002), ReturnMouseInput(0x0004) }, Marshal.SizeOf<ReturnInput>());
                        if (sent != 2)
                        {
                            ReturnSendInput(1, new[] { ReturnMouseInput(0x0004) }, Marshal.SizeOf<ReturnInput>());
                            ReleaseReturnControl();
                            returnInputStep = 0;
                            returnStatus = "Clic refusé. Utilise Ctrl + clic sur Jouer.";
                        }
                        else { returnInputStep = 3; returnInputTimer.Restart(); returnStatus = "Connexion au repaire demandée…"; }
                    }
                }
                else if (returnInputStep == 3 && returnInputTimer.ElapsedMilliseconds >= 100)
                {
                    ReleaseReturnControl();
                    returnInputStep = 0;
                }
                return true;
            }
            if (state == GameStateTypes.SelectCharacterState && (Settings.HideoutReturnPending || capturePlayPosition))
                DrawHideoutReturnPanel();
            return false;
        }

        private IntPtr GetReturnGameWindow()
        {
            uint pid = Core.Process.Pid;
            if (pid == 0) { returnGamePid = 0; returnGameWindow = IntPtr.Zero; return IntPtr.Zero; }
            if (pid != returnGamePid || returnGameWindow == IntPtr.Zero || !ReturnIsWindow(returnGameWindow))
            {
                returnGamePid = pid;
                returnGameWindow = IntPtr.Zero;
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    returnGameWindow = process.MainWindowHandle;
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception) { }
            }
            return returnGameWindow;
        }
        private bool ReturnPositionMatches(NativeRect rect) =>
            HasReturnPosition(Settings.HideoutPlayPosition, Settings.HideoutPlayWindowSize) &&
            Settings.HideoutPlayWindowSize == new Vector2(rect.Right - rect.Left, rect.Bottom - rect.Top);

        private static bool ReturnInputsReleased(bool ownsControl)
        {
            foreach (int key in new[] { 0x01, 0x02, 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if (!(key == 0x11 && ownsControl) && (ReturnKeyState(key) & 0x8000) != 0) return false;
            return true;
        }

        private void ReleaseReturnControl()
        {
            if (returnControlHeld && ReturnSendInput(1, new[] { ReturnKeyInput(0x0002) }, Marshal.SizeOf<ReturnInput>()) == 1)
                returnControlHeld = false;
        }

        private void DrawHideoutReturnPanel()
        {
            ImGui.SetNextWindowPos(new Vector2(ImGui.GetIO().DisplaySize.X * .5f, 30), ImGuiCond.Always, new Vector2(.5f, 0));
            ImGui.SetNextWindowSize(new Vector2(Math.Min(490, ImGui.GetIO().DisplaySize.X - 24), 0), ImGuiCond.Always);
            if (GameHelper.Ui.ImGuiTheme.BeginWindow("AutoPot · Retour sécurisé###AutoPotReturn",
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
            {
                ImGui.TextColored(new Vector4(.45f, .75f, 1, 1), GameHelper.Localization.OverlayLocalization.T("ui.labels.returning_to_the_hideout", "Returning to the hideout"));
                GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.choose_your_character_a_normal_click_on_play_may_return_you_to_the_map", "Choose your character. A normal click on Play may return you to the map."));
                if (capturePlayPosition || !HasReturnPosition(Settings.HideoutPlayPosition, Settings.HideoutPlayWindowSize))
                {
                    capturePlayPosition = true;
                    GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.place_the_pointer_in_the_centre_of_play_then_press_f8_to_save_its_posi", "Place the pointer in the centre of Play, then press F8 to save its position."));
                }
                ImGui.BeginDisabled(!HasReturnPosition(Settings.HideoutPlayPosition, Settings.HideoutPlayWindowSize));
                if (ImGui.Button(GameHelper.Localization.OverlayLocalization.Title("ui.labels.return_to_hideout", "Return to hideout", "Retour au repaire"), new Vector2(-1, 0)))
                {
                    var window = GetReturnGameWindow();
                    if (window != IntPtr.Zero && Core.States.GameCurrentState == GameStateTypes.SelectCharacterState && ReturnSetForeground(window))
                    {
                        returnInputStep = 1;
                        returnInputTimer.Restart();
                    }
                    else returnStatus = "Donne le focus au jeu, puis réessaie.";
                }
                ImGui.EndDisabled();
                if (!string.IsNullOrEmpty(returnStatus)) ImGui.TextWrapped(returnStatus);
                GameHelper.Utils.ImGuiHelper.WrappedText(GameHelper.Localization.OverlayLocalization.T("ui.labels.you_can_also_hold_ctrl_and_click_play_yourself", "You can also hold Ctrl and click Play yourself."));
            }
            ImGui.End();
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct ReturnMouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct ReturnKey { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] private struct ReturnInputData
        {
            [FieldOffset(0)] public ReturnMouse Mouse;
            [FieldOffset(0)] public ReturnKey Key;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ReturnInput { public uint Type; public ReturnInputData Data; }
        private static ReturnInput ReturnKeyInput(uint flags) => new() { Type = 1, Data = new ReturnInputData { Key = new ReturnKey { Key = 0x11, Flags = flags } } };
        private static ReturnInput ReturnMouseInput(uint flags) => new() { Data = new ReturnInputData { Mouse = new ReturnMouse { Flags = flags } } };
        [DllImport("user32.dll", EntryPoint = "SendInput")] private static extern uint ReturnSendInput(uint count, ReturnInput[] inputs, int size);
        [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")] private static extern short ReturnKeyState(int key);
        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern IntPtr ReturnForegroundWindow();
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] private static extern bool ReturnSetForeground(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "IsWindow")] private static extern bool ReturnIsWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetCursorPos")] private static extern bool ReturnGetCursor(out NativePoint point);
        [DllImport("user32.dll", EntryPoint = "SetCursorPos")] private static extern bool ReturnSetCursor(int x, int y);
        [DllImport("user32.dll", EntryPoint = "ScreenToClient")] private static extern bool ReturnScreenToClient(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll", EntryPoint = "ClientToScreen")] private static extern bool ReturnClientToScreen(IntPtr window, ref NativePoint point);
        [DllImport("user32.dll", EntryPoint = "GetClientRect")] private static extern bool ReturnClientRect(IntPtr window, out NativeRect rect);
        private bool IsSafetyLogoutRearmed()
        {
            if (Settings.SafetyLogoutCooldownSeconds > 0 &&
                safetyLogoutCooldown.IsRunning &&
                safetyLogoutCooldown.Elapsed.TotalSeconds < Settings.SafetyLogoutCooldownSeconds)
            {
                return false;
            }

            if (safetyLogoutWaitingRecovery && !AreSafetyLogoutVitalsRecovered())
            {
                return false;
            }

            safetyLogoutWaitingRecovery = false;
            return true;
        }

        private bool AreSafetyLogoutVitalsRecovered()
        {
            if (!lastVitals.Valid)
            {
                return false;
            }

            if (Settings.HpDisconnectEnabled && lastVitals.HpPercent <= Settings.HpDisconnectPercent)
            {
                return false;
            }

            if (Settings.EsDisconnectEnabled && lastVitals.HasEnergyShield
                && lastVitals.EsPercent <= Settings.EsDisconnectPercent)
            {
                return false;
            }

            if (Settings.MpDisconnectEnabled && lastVitals.MpPercent <= Settings.MpDisconnectPercent)
            {
                return false;
            }

            return true;
        }

        private bool CanSafetyLogout(out string reason)
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                reason = $"Game state is {Core.States.GameCurrentState}, not InGameState.";
                safetyLogoutTriggered = false;
                return false;
            }

            var area = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (area.IsTown || area.IsHideout)
            {
                reason = "Player is in town.";
                return false;
            }

            if (!Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Life>(out var life))
            {
                reason = "Cannot read player Life component.";
                return false;
            }

            if (life.Health.Current <= 0)
            {
                reason = "Player is dead.";
                safetyLogoutTriggered = false;
                return false;
            }

            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Buffs>(out var buffs)
                && buffs.StatusEffects.ContainsKey("grace_period"))
            {
                reason = "Player has grace period.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void RefreshVitals()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                lastVitals = default;
                safetyLogoutTriggered = false;
                return;
            }

            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Life>(out var life))
                lastVitals = VitalsSnapshot.FromLife(life);
            else
                lastVitals = default;
        }

        private void UpdateServiceStatus()
        {
            if (!Settings.EnableAutoPot)
            {
                serviceStatus = "Stopped";
                statusDetail = "Enable AutoPot to start monitoring.";
                return;
            }

            if (CanExecute(out var reason))
            {
                serviceStatus = "Running";
                statusDetail = "Monitoring vitals and pressing configured keys.";
            }
            else
            {
                serviceStatus = "Stopped";
                statusDetail = reason;
            }
        }

        private bool CanExecute(out string reason)
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                reason = $"Game state is {Core.States.GameCurrentState}, not InGameState.";
                return false;
            }

            if (!Core.Process.Foreground)
            {
                reason = "Game window is not in the foreground.";
                return false;
            }

            var area = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (area.IsTown)
            {
                reason = "Player is in town.";
                return false;
            }

            if (!Settings.RunInHideout && area.IsHideout)
            {
                reason = "Player is in hideout (enable 'Run in hideout' to allow).";
                return false;
            }

            if (!Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Life>(out var life))
            {
                reason = "Cannot read player Life component.";
                return false;
            }

            if (life.Health.Current <= 0)
            {
                reason = "Player is dead.";
                return false;
            }

            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Buffs>(out var buffs)
                && buffs.StatusEffects.ContainsKey("grace_period"))
            {
                reason = "Player has grace period.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static string DisplayLogicMode(LogicMode mode) => mode switch
        {
            LogicMode.ManaAndLife => GameHelper.Localization.OverlayLocalization.T("ui.labels.life_mana", "Life + mana"),
            LogicMode.LifeOnly => GameHelper.Localization.OverlayLocalization.T("ui.labels.life", "Life"),
            LogicMode.EnergyShield => GameHelper.Localization.OverlayLocalization.T("ui.labels.energy_shield", "Energy shield"),
            LogicMode.HybridLifeEs => GameHelper.Localization.OverlayLocalization.T("ui.labels.combined_life_and_energy_shield", "Combined life and energy shield"),
            LogicMode.ManaOnly => "Mana",
            LogicMode.ManaAndEs => GameHelper.Localization.OverlayLocalization.T("ui.labels.energy_shield_mana", "Energy shield + mana"),
            LogicMode.LifeEsMana => GameHelper.Localization.OverlayLocalization.T("ui.labels.life_energy_shield_mana", "Life + energy shield + mana"),
            _ => mode.ToString()
        };

        private static string LocalizeServiceStatus(string status) => status switch
        {
            "Running" => GameHelper.Localization.OverlayLocalization.T("ui.labels.monitoring_active", "Monitoring active"),
            "Stopped" => GameHelper.Localization.OverlayLocalization.T("ui.labels.paused", "Paused"),
            _ => status,
        };

        private static string LocalizeReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return reason;

            if (reason.StartsWith("Game state is ", StringComparison.Ordinal)) return GameHelper.Localization.OverlayLocalization.T("ui.labels.waiting_for_your_character_in_a_game_area", "Waiting for your character in a game area.");
            return reason switch
            {
                "Enable AutoPot to start monitoring." => GameHelper.Localization.OverlayLocalization.T("ui.labels.enable_automatic_flasks_to_start", "Enable automatic flasks to start."),
                "Monitoring vitals and pressing configured keys." => GameHelper.Localization.OverlayLocalization.T("ui.labels.flasks_follow_the_configured_thresholds_and_keys", "Flasks follow the configured thresholds and keys."),
                "Game window is not in the foreground." => GameHelper.Localization.OverlayLocalization.T("ui.labels.game_is_in_the_background", "Game is in the background."),
                "Player is in town." => GameHelper.Localization.OverlayLocalization.T("ui.labels.you_are_in_a_safe_area", "You are in a safe area."),
                "Cannot read player Life component." => GameHelper.Localization.OverlayLocalization.T("ui.labels.character_resources_are_unreadable", "Character resources are unreadable."),
                "Player is dead." => GameHelper.Localization.OverlayLocalization.T("ui.labels.character_is_dead", "Character is dead."),
                "Player has grace period." => GameHelper.Localization.OverlayLocalization.T("ui.labels.temporary_protection_after_loading", "Temporary protection after loading."),
                "Player is in hideout (enable 'Run in hideout' to allow)." =>
                    GameHelper.Localization.OverlayLocalization.T("ui.labels.flasks_are_paused_in_the_hideout", "Flasks are paused in the hideout."),
                _ => reason,
            };
        }
    }
}
