// <copyright file="ImGuiTheme.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace ExileCenter.Ui
{
    using System;
    using System.Numerics;
    using ImGuiNET;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.PixelFormats;

    /// <summary>
    ///     Central dark theme for all GameHelper windows.
    /// </summary>
    public static class ImGuiTheme
    {
        private static bool backgroundLoaded;
        private static IntPtr backgroundTexture;
        private static Vector2 backgroundSize;

        internal static void DrawSettingsBackground(bool independent)
            => DrawWindowBackground(ImGuiWindowFlags.MenuBar | (independent ? ImGuiWindowFlags.NoTitleBar : ImGuiWindowFlags.None), 0.80f);

        /// <summary>Opens a themed panel without decorating transparent gameplay markers.</summary>
        public static bool BeginWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        {
            bool visible = ImGui.Begin(name, flags);
            if (visible) DrawWindowBackground(flags);
            return visible;
        }

        /// <summary>Opens a closable themed panel without changing its input or placement rules.</summary>
        public static bool BeginWindow(string name, ref bool open, ImGuiWindowFlags flags = ImGuiWindowFlags.None)
        {
            bool visible = ImGui.Begin(name, ref open, flags);
            if (visible) DrawWindowBackground(flags);
            return visible;
        }

        private static void DrawWindowBackground(ImGuiWindowFlags flags, float shade = 0.88f)
        {
            var windowSize = ImGui.GetWindowSize();
            if ((flags & (ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMouseInputs)) != 0 ||
                ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].W <= 0 || windowSize.X < 160 || windowSize.Y < 80) return;
            if (!backgroundLoaded && Core.Overlay != null)
            {
                backgroundLoaded = true;
                try
                {
                    using var stream = typeof(ImGuiTheme).Assembly.GetManifestResourceStream("ExileCenter.Data.Poe2Background.png");
                    if (stream == null) return;
                    var configuration = Configuration.Default.Clone();
                    configuration.PreferContiguousImageBuffers = true;
                    using var image = Image.Load<Rgba32>(new SixLabors.ImageSharp.Formats.DecoderOptions { Configuration = configuration }, stream);
                    backgroundSize = new Vector2(image.Width, image.Height);
                    Core.Overlay.AddOrGetImagePointer("ExileCenter.Poe2Background", image, false, out backgroundTexture);
                }
                catch (Exception ex) { Console.WriteLine($"[Settings.Background] {ex.Message}"); }
            }
            float header = ((flags & ImGuiWindowFlags.NoTitleBar) == 0 ? ImGui.GetFrameHeight() : 0) +
                ((flags & ImGuiWindowFlags.MenuBar) != 0 ? ImGui.GetFrameHeight() : 0);
            var min = ImGui.GetWindowPos() + new Vector2(1, header);
            var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - Vector2.One;
            if (ImGui.GetScrollMaxY() > 0) max.X -= ImGui.GetStyle().ScrollbarSize;
            if (ImGui.GetScrollMaxX() > 0) max.Y -= ImGui.GetStyle().ScrollbarSize;
            var size = max - min;
            if (size.X <= 0 || size.Y <= 0) return;
            var draw = ImGui.GetWindowDrawList();
            var position = ImGui.GetWindowPos();
            var end = position + windowSize;
            float alpha = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg].W;
            draw.PushClipRect(position, end, false);
            uint stone = ImGui.GetColorU32(new Vector4(.073f, .068f, .059f, alpha * .65f));
            uint shadow = ImGui.GetColorU32(new Vector4(.018f, .020f, .022f, alpha * .75f));
            draw.AddRectFilledMultiColor(min, max, stone, stone, shadow, shadow);
            if (backgroundTexture != IntPtr.Zero && backgroundSize.X > 0 && backgroundSize.Y > 0)
            {
                // Fill the body without stretching the artwork or covering the title/menu bars.
                float sourceAspect = backgroundSize.X / backgroundSize.Y;
                float targetAspect = size.X / size.Y;
                var crop = sourceAspect > targetAspect
                    ? new Vector2((1 - targetAspect / sourceAspect) / 2, 0)
                    : new Vector2(0, (1 - sourceAspect / targetAspect) / 2);
                draw.AddImage(backgroundTexture, min, max, crop, Vector2.One - crop,
                    ImGui.GetColorU32(new Vector4(1, 1, 1, (1 - shade) * alpha)));
            }

            // Inset metalwork stays inside the window and never covers controls or title text.
            uint bronze = ImGui.GetColorU32(new Vector4(AccentMuted.X, AccentMuted.Y, AccentMuted.Z, alpha));
            uint gold = ImGui.GetColorU32(new Vector4(Accent.X, Accent.Y, Accent.Z, alpha * .80f));
            draw.AddRect(position + Vector2.One, end - Vector2.One, bronze, 2, ImDrawFlags.None, 2);
            draw.AddRect(position + new Vector2(4), end - new Vector2(4), ImGui.GetColorU32(new Vector4(.22f, .18f, .12f, alpha * .65f)), 1);
            if (header > 0) draw.AddLine(new Vector2(position.X + 5, min.Y - 1), new Vector2(end.X - 5, min.Y - 1), bronze);
            for (int corner = 0; corner < 4; corner++)
            {
                var inward = new Vector2((corner & 1) == 0 ? 1 : -1, (corner & 2) == 0 ? 1 : -1);
                var point = new Vector2(inward.X > 0 ? position.X + 4 : end.X - 4, inward.Y > 0 ? position.Y + 4 : end.Y - 4);
                draw.AddLine(point, point + new Vector2(inward.X * 17, 0), gold, 2);
                draw.AddLine(point, point + new Vector2(0, inward.Y * 17), gold, 2);
            }
            draw.PopClipRect();
        }

        public static readonly Vector4 Accent = new(0.78f, 0.63f, 0.39f, 1f);
        public static readonly Vector4 AccentMuted = new(0.44f, 0.34f, 0.21f, 1f);
        internal static readonly Vector4 TextMuted = new(0.70f, 0.67f, 0.59f, 1f);
        internal static readonly Vector4 Success = new(0.35f, 0.78f, 0.45f, 1f);
        internal static readonly Vector4 Danger = new(0.85f, 0.35f, 0.35f, 1f);
        internal static readonly Vector4 SectionBg = new(0.065f, 0.061f, 0.053f, 1f);

        private static readonly string[] PluginThemeNames =
        {
            "LootTracker", "StashUtility", "AutoHotKeyTrigger", "KikiMeter", "CorpseCleaner", "AuraTracker",
            "Radar", "RitualWispAlert", "WorldDrawing", "Atlas2", "PickupHelper", "PreloadAlert", "HealthBars",
            "AutoPot", "SekhemaHelper", "AmanamuVoidAlert", "PlayerBuffBar", "SimpleBars", "Hiveblood",
            "RunecraftHelper", "LootValue", "StashValueByZx0", "CameraZoom", "Atlas", "RitualHelper",
        };

        internal static string? ActivePluginTheme { get; private set; }

        /// <summary>Softens the assigned colour without changing the plugin's hue or section identity.</summary>
        public static Vector4 PluginAccent(string key)
        {
            var accent = PluginBaseAccent(key);
            float peak = Math.Max(accent.X, Math.Max(accent.Y, accent.Z));
            return Vector4.Lerp(accent, new Vector4(peak, peak, peak, 1), .25f) * new Vector4(.88f, .88f, .88f, 1);
        }

        private static Vector4 PluginBaseAccent(string key)
        {
            int separator = key.IndexOf('/');
            string plugin = separator < 0 ? key : key[..separator];
            uint hash = 2166136261;
            foreach (char character in plugin) hash = unchecked((hash ^ character) * 16777619);
            return plugin switch
            {
                // Spaced in perceptual colour space, rather than several similar metal shades.
                "LootTracker" => new Vector4(.96f, .78f, .16f, 1), // Gold
                "StashUtility" => new Vector4(.68f, .52f, .20f, 1), // Ochre bronze
                "AutoHotKeyTrigger" => new Vector4(1f, .56f, .48f, 1), // Coral
                "KikiMeter" => new Vector4(1f, .04f, .68f, 1), // Fuchsia
                "CorpseCleaner" => new Vector4(.40f, .56f, .04f, 1), // Olive
                "AuraTracker" => new Vector4(.04f, .72f, .04f, 1), // Emerald
                "Radar" => new Vector4(.04f, .96f, 1f, 1), // Spectral cyan
                "RitualWispAlert" => new Vector4(.80f, 1f, .56f, 1), // Pale absinthe
                "WorldDrawing" => new Vector4(.52f, .84f, .48f, 1), // Sage
                "Atlas2" => new Vector4(.44f, .44f, 1f, 1), // Indigo
                "PickupHelper" => new Vector4(1f, .68f, 1f, 1), // Pink opal
                "PreloadAlert" => new Vector4(.72f, .68f, .04f, 1), // Sulphur
                "HealthBars" => new Vector4(1f, .04f, .12f, 1), // Scarlet
                "AutoPot" => new Vector4(.96f, .44f, 1f, 1), // Orchid
                "SekhemaHelper" => new Vector4(.96f, .92f, .88f, 1), // Ivory
                "AmanamuVoidAlert" => new Vector4(.84f, .04f, 1f, 1), // Abyssal violet
                "PlayerBuffBar" => new Vector4(.04f, .72f, .60f, 1), // Turquoise
                "SimpleBars" => new Vector4(.68f, .84f, 1f, 1), // Ice blue
                "Hiveblood" => new Vector4(.36f, 1f, .04f, 1), // Acid green
                "RunecraftHelper" => new Vector4(.04f, .56f, .68f, 1), // Deep teal
                "LootValue" => new Vector4(.76f, .44f, .68f, 1), // Dusty rose
                "StashValueByZx0" => new Vector4(.72f, .60f, .96f, 1), // Lavender
                "CameraZoom" => new Vector4(.56f, .56f, .60f, 1), // Steel
                "Atlas" => new Vector4(.28f, .68f, 1f, 1), // Azure
                "RitualHelper" => new Vector4(.64f, .72f, .72f, 1), // Silver
                _ => PluginBaseAccent(PluginThemeNames[(int)(hash % (uint)PluginThemeNames.Length)]),
            };
        }

        /// <summary>Applies a plugin accent only during the current draw, then restores the previous style.</summary>
        public static PluginThemeScope UsePluginTheme(string? key) => new(key);

        public readonly struct PluginThemeScope : IDisposable
        {
            private const int ColorCount = 43;
            private readonly IntPtr context;
            private readonly string? previousTheme;

            internal PluginThemeScope(string? key)
            {
                this.context = string.IsNullOrEmpty(key) ? IntPtr.Zero : ImGui.GetCurrentContext();
                this.previousTheme = ActivePluginTheme;
                if (this.context == IntPtr.Zero) return;
                ActivePluginTheme = key;
                var accent = PluginAccent(key!);
                Vector4 Tint(float amount) => Vector4.Lerp(new Vector4(.027f, .025f, .023f, 1), accent, amount * .35f);
                var colors = ImGui.GetStyle().Colors;
                var windowBg = Tint(.12f); windowBg.W = colors[(int)ImGuiCol.WindowBg].W;
                var childBg = Tint(.19f); childBg.W = colors[(int)ImGuiCol.ChildBg].W;
                ImGui.PushStyleColor(ImGuiCol.WindowBg, windowBg);
                ImGui.PushStyleColor(ImGuiCol.ChildBg, childBg);
                ImGui.PushStyleColor(ImGuiCol.PopupBg, Tint(.11f));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, Tint(.23f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Tint(.31f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Tint(.40f));
                ImGui.PushStyleColor(ImGuiCol.TitleBg, Tint(.10f));
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.15f, .112f, .066f, 1));
                ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Tint(.08f));
                ImGui.PushStyleColor(ImGuiCol.MenuBarBg, Tint(.12f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Tint(.07f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Tint(.29f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Tint(.44f));
                ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Tint(.60f));
                ImGui.PushStyleColor(ImGuiCol.Separator, Tint(.37f));
                ImGui.PushStyleColor(ImGuiCol.SeparatorHovered, Tint(.58f));
                ImGui.PushStyleColor(ImGuiCol.SeparatorActive, accent);
                ImGui.PushStyleColor(ImGuiCol.Tab, Tint(.14f));
                ImGui.PushStyleColor(ImGuiCol.TabDimmed, Tint(.10f));
                ImGui.PushStyleColor(ImGuiCol.TabDimmedSelected, Tint(.22f));
                ImGui.PushStyleColor(ImGuiCol.TabDimmedSelectedOverline, Tint(.46f));
                ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, Tint(.34f));
                ImGui.PushStyleColor(ImGuiCol.TableBorderLight, Tint(.21f));
                var selection = accent; selection.W = .28f;
                ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, selection);
                ImGui.PushStyleColor(ImGuiCol.ResizeGrip, Tint(.26f));
                ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Tint(.48f));
                ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, accent);
                ImGui.PushStyleColor(ImGuiCol.DockingPreview, selection);
                ImGui.PushStyleColor(ImGuiCol.CheckMark, accent);
                ImGui.PushStyleColor(ImGuiCol.SliderGrab, Tint(.55f));
                ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, accent);
                ImGui.PushStyleColor(ImGuiCol.Button, Tint(.30f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Tint(.40f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Tint(.53f));
                ImGui.PushStyleColor(ImGuiCol.Header, Tint(.33f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Tint(.43f));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(.24f, .065f, .050f, 1));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, Tint(.40f));
                ImGui.PushStyleColor(ImGuiCol.TabSelected, new Vector4(.20f, .058f, .043f, 1));
                ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, Accent);
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(AccentMuted.X, AccentMuted.Y, AccentMuted.Z, .85f));
                ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, Tint(.20f));
                ImGui.PushStyleColor(ImGuiCol.NavCursor, accent);
            }

            public void Dispose()
            {
                if (this.context == IntPtr.Zero) return;
                var current = ImGui.GetCurrentContext();
                try { ImGui.SetCurrentContext(this.context); ImGui.PopStyleColor(ColorCount); }
                finally { ImGui.SetCurrentContext(current); ActivePluginTheme = this.previousTheme; }
            }
        }

        // Roomier settings controls share the warm palette used by plugin windows.
        internal static void PushSettingsStyle()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 16f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 7f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 2f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0f, 0.5f));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.034f, .032f, .028f, .98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(.065f, .059f, .047f, .68f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.44f, .34f, .21f, .85f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.085f, .075f, .057f, 1));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(.16f, .13f, .08f, 1));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(.22f, .17f, .095f, 1));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.13f, .105f, .067f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.23f, .18f, .10f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.28f, .075f, .055f, 1));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(.12f, .095f, .057f, 1));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(.20f, .14f, .075f, 1));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(.25f, .065f, .045f, 1));
            ImGui.PushStyleColor(ImGuiCol.TabSelected, new Vector4(.20f, .058f, .043f, 1));
            ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, Accent);
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(.065f, .054f, .038f, 1));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.15f, .112f, .066f, 1));
        }

        internal static void PopSettingsStyle()
        {
            ImGui.PopStyleColor(16);
            ImGui.PopStyleVar(8);
        }

        internal static void Apply()
        {
            ImGui.StyleColorsDark();
            var style = ImGui.GetStyle();
            style.WindowRounding = 2f;
            style.ChildRounding = 1f;
            style.FrameRounding = 1f;
            style.PopupRounding = 2f;
            style.ScrollbarRounding = 1f;
            style.GrabRounding = 1f;
            style.TabRounding = 1f;
            style.WindowBorderSize = 2f;
            style.ChildBorderSize = 1f;
            style.PopupBorderSize = 2f;
            style.FrameBorderSize = 1f;
            style.TabBorderSize = 1f;
            style.WindowTitleAlign = new Vector2(.5f, .5f);
            style.WindowPadding = new Vector2(14f, 12f);
            style.FramePadding = new Vector2(8f, 5f);
            style.ItemSpacing = new Vector2(10f, 7f);
            style.ItemInnerSpacing = new Vector2(8f, 5f);
            style.ScrollbarSize = 17f;
            style.IndentSpacing = 18f;

            var colors = style.Colors;
            colors[(int)ImGuiCol.Text] = new Vector4(0.93f, 0.91f, 0.86f, 1f);
            colors[(int)ImGuiCol.TextDisabled] = TextMuted;
            colors[(int)ImGuiCol.WindowBg] = new Vector4(.035f, .033f, .030f, .97f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(.060f, .055f, .046f, 1);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(.045f, .040f, .031f, .98f);
            colors[(int)ImGuiCol.Border] = new Vector4(.44f, .34f, .21f, .85f);
            colors[(int)ImGuiCol.BorderShadow] = new Vector4(.008f, .007f, .006f, .80f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(.09f, .078f, .058f, 1);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(.16f, .13f, .08f, 1);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(.22f, .17f, .095f, 1);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(.055f, .045f, .030f, 1);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(.15f, .112f, .066f, 1);
            colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(.045f, .037f, .026f, 1);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(.065f, .054f, .038f, 1);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(.025f, .023f, .020f, .80f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(.26f, .20f, .125f, 1);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(.40f, .31f, .18f, 1);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = Accent;
            colors[(int)ImGuiCol.CheckMark] = Accent;
            colors[(int)ImGuiCol.SliderGrab] = AccentMuted;
            colors[(int)ImGuiCol.SliderGrabActive] = Accent;
            colors[(int)ImGuiCol.Button] = new Vector4(.15f, .12f, .075f, 1);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(.25f, .19f, .105f, 1);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(.28f, .075f, .055f, 1);
            colors[(int)ImGuiCol.Header] = new Vector4(.12f, .095f, .057f, 1);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(.20f, .14f, .075f, 1);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(.25f, .065f, .045f, 1);
            colors[(int)ImGuiCol.Separator] = new Vector4(0.288f, 0.247f, 0.18f, 0.6f);
            colors[(int)ImGuiCol.Tab] = new Vector4(.085f, .070f, .047f, 1);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(.22f, .16f, .085f, 1);
            colors[(int)ImGuiCol.TabSelected] = new Vector4(.20f, .058f, .043f, 1);
            colors[(int)ImGuiCol.TabSelectedOverline] = Accent;
            colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.22f, 0.18f, 0.13f, 1f);
            colors[(int)ImGuiCol.TabDimmedSelectedOverline] = AccentMuted;
            colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.58f, 0.43f, 0.24f, 0.45f);
            colors[(int)ImGuiCol.NavCursor] = Accent;
            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.58f, 0.43f, 0.24f, 0.25f);
            colors[(int)ImGuiCol.ResizeGripHovered] = AccentMuted;
            colors[(int)ImGuiCol.ResizeGripActive] = Accent;
            colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.58f, 0.43f, 0.24f, 0.65f);
            colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(.10f, .083f, .054f, 1);
            colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.34f, 0.28f, 0.20f, 0.7f);
            colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.27f, 0.235f, 0.185f, 0.5f);
            colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(0.26f, 0.23f, 0.18f, 0.22f);
        }

        internal static void SectionHeader(string title, string? subtitle = null)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.CheckMark]);
            ImGui.TextUnformatted(title);
            ImGui.PopStyleColor();
            if (!string.IsNullOrEmpty(subtitle))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, TextMuted);
                ImGui.PushTextWrapPos(0);
                ImGui.TextUnformatted(subtitle);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }

            ImGui.Separator();
            ImGui.Spacing();
        }

        internal static void BeginPanel(string id)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ActivePluginTheme is { } key
                ? Vector4.Lerp(new Vector4(.027f, .025f, .023f, 1), PluginAccent(key), .045f) : SectionBg);
            ImGui.BeginChild(id, Vector2.Zero, ImGuiChildFlags.Borders);
        }

        internal static void EndPanel()
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
    }
}
