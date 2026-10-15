// <copyright file="ImGuiTheme.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
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
                    using var stream = typeof(ImGuiTheme).Assembly.GetManifestResourceStream("GameHelper.Data.Poe2Background.png");
                    if (stream == null) return;
                    var configuration = Configuration.Default.Clone();
                    configuration.PreferContiguousImageBuffers = true;
                    using var image = Image.Load<Rgba32>(new SixLabors.ImageSharp.Formats.DecoderOptions { Configuration = configuration }, stream);
                    backgroundSize = new Vector2(image.Width, image.Height);
                    Core.Overlay.AddOrGetImagePointer("GameHelper.Poe2Background", image, false, out backgroundTexture);
                }
                catch (Exception ex) { Console.WriteLine($"[Settings.Background] {ex.Message}"); }
            }
            if (backgroundTexture == IntPtr.Zero || backgroundSize.X <= 0 || backgroundSize.Y <= 0) return;

            float header = ((flags & ImGuiWindowFlags.NoTitleBar) == 0 ? ImGui.GetFrameHeight() : 0) +
                ((flags & ImGuiWindowFlags.MenuBar) != 0 ? ImGui.GetFrameHeight() : 0);
            var min = ImGui.GetWindowPos() + new Vector2(1, header);
            var max = ImGui.GetWindowPos() + ImGui.GetWindowSize() - Vector2.One;
            if (ImGui.GetScrollMaxY() > 0) max.X -= ImGui.GetStyle().ScrollbarSize;
            if (ImGui.GetScrollMaxX() > 0) max.Y -= ImGui.GetStyle().ScrollbarSize;
            var size = max - min;
            if (size.X <= 0 || size.Y <= 0) return;
            // Fill the body without stretching the artwork or covering the title/menu bars.
            float sourceAspect = backgroundSize.X / backgroundSize.Y;
            float targetAspect = size.X / size.Y;
            var crop = sourceAspect > targetAspect
                ? new Vector2((1 - targetAspect / sourceAspect) / 2, 0)
                : new Vector2(0, (1 - sourceAspect / targetAspect) / 2);
            var draw = ImGui.GetWindowDrawList();
            // Blend the artwork into the panel instead of making translucent HUDs opaque.
            draw.AddImage(backgroundTexture, min, max, crop, Vector2.One - crop,
                ImGui.GetColorU32(new Vector4(1, 1, 1, 1 - shade)));
        }

        public static readonly Vector4 Accent = new(0.78f, 0.61f, 0.37f, 1f);
        public static readonly Vector4 AccentMuted = new(0.44f, 0.33f, 0.21f, 1f);
        internal static readonly Vector4 TextMuted = new(0.76f, 0.72f, 0.62f, 1f);
        internal static readonly Vector4 Success = new(0.35f, 0.78f, 0.45f, 1f);
        internal static readonly Vector4 Danger = new(0.85f, 0.35f, 0.35f, 1f);
        internal static readonly Vector4 SectionBg = new(0.171f, 0.1425f, 0.105f, 1f);

        private static readonly string[] PluginThemeNames =
        {
            "LootTracker", "StashUtility", "AutoHotKeyTrigger", "KikiMeter", "CorpseCleaner", "AuraTracker",
            "Radar", "RitualWispAlert", "WorldDrawing", "Atlas2", "PickupHelper", "PreloadAlert", "HealthBars",
            "AutoPot", "SekhemaHelper", "AmanamuVoidAlert", "PlayerBuffBar", "SimpleBars", "Hiveblood",
            "RunecraftHelper", "LootValue", "StashValueByZx0", "CameraZoom", "Atlas", "RitualHelper",
        };

        internal static string? ActivePluginTheme { get; private set; }

        /// <summary>Returns a dark-fantasy metal, gem or spectral accent, shared by a plugin's sections.</summary>
        public static Vector4 PluginAccent(string key)
        {
            int separator = key.IndexOf('/');
            string plugin = separator < 0 ? key : key[..separator];
            uint hash = 2166136261;
            foreach (char character in key) hash = unchecked((hash ^ character) * 16777619);
            var accent = plugin switch
            {
                "LootTracker" => new Vector4(.82f, .66f, .34f, 1), // Ancient gold
                "StashUtility" => new Vector4(.74f, .48f, .28f, 1), // Vault bronze
                "AutoHotKeyTrigger" => new Vector4(.73f, .40f, .26f, 1), // Ember
                "KikiMeter" => new Vector4(.72f, .30f, .33f, 1), // Garnet
                "CorpseCleaner" => new Vector4(.63f, .68f, .43f, 1), // Tarnished bone
                "AuraTracker" => new Vector4(.40f, .69f, .51f, 1), // Jade
                "Radar" => new Vector4(.39f, .66f, .72f, 1), // Spectral cyan
                "RitualWispAlert" => new Vector4(.51f, .72f, .65f, 1), // Ghostlight
                "WorldDrawing" => new Vector4(.56f, .65f, .48f, 1), // Weathered moss
                "Atlas2" => new Vector4(.49f, .58f, .77f, 1), // Celestial sapphire
                "PickupHelper" => new Vector4(.73f, .62f, .45f, 1), // Aged brass
                "PreloadAlert" => new Vector4(.77f, .53f, .36f, 1), // Smouldering copper
                "HealthBars" => new Vector4(.76f, .35f, .28f, 1), // Bloodstone
                "AutoPot" => new Vector4(.65f, .36f, .49f, 1), // Alchemical ruby
                "SekhemaHelper" => new Vector4(.79f, .70f, .49f, 1), // Desert gold
                "AmanamuVoidAlert" => new Vector4(.57f, .43f, .73f, 1), // Abyssal amethyst
                "PlayerBuffBar" => new Vector4(.38f, .67f, .62f, 1), // Verdigris
                "SimpleBars" => new Vector4(.60f, .66f, .73f, 1), // Cold steel
                "Hiveblood" => new Vector4(.71f, .57f, .29f, 1), // Dark amber
                "RunecraftHelper" => new Vector4(.43f, .58f, .69f, 1), // Runic blue
                "LootValue" => new Vector4(.66f, .70f, .46f, 1), // Electrum
                "StashValueByZx0" => new Vector4(.62f, .52f, .73f, 1), // Royal violet
                "CameraZoom" => new Vector4(.67f, .65f, .59f, 1), // Obsidian silver
                "Atlas" => new Vector4(.65f, .57f, .43f, 1), // Cartographer's parchment
                "RitualHelper" => new Vector4(.68f, .43f, .61f, 1), // Ritual incense
                _ => PluginAccent(PluginThemeNames[(int)(hash % (uint)PluginThemeNames.Length)]),
            };
            // Section variants retain the parent palette instead of jumping to another hue.
            return separator < 0 ? accent : Vector4.Lerp(accent,
                (hash & 1) == 0 ? new Vector4(.91f, .86f, .73f, 1) : new Vector4(.32f, .29f, .25f, 1), .08f);
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
                Vector4 Tint(float amount) => Vector4.Lerp(new Vector4(.055f, .05f, .045f, 1), accent, amount);
                var colors = ImGui.GetStyle().Colors;
                var windowBg = Tint(.085f); windowBg.W = colors[(int)ImGuiCol.WindowBg].W;
                var childBg = Tint(.13f); childBg.W = colors[(int)ImGuiCol.ChildBg].W;
                ImGui.PushStyleColor(ImGuiCol.WindowBg, windowBg);
                ImGui.PushStyleColor(ImGuiCol.ChildBg, childBg);
                ImGui.PushStyleColor(ImGuiCol.PopupBg, Tint(.11f));
                ImGui.PushStyleColor(ImGuiCol.FrameBg, Tint(.18f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Tint(.31f));
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Tint(.40f));
                ImGui.PushStyleColor(ImGuiCol.TitleBg, Tint(.10f));
                ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Tint(.23f));
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
                ImGui.PushStyleColor(ImGuiCol.Button, Tint(.22f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Tint(.40f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Tint(.53f));
                ImGui.PushStyleColor(ImGuiCol.Header, Tint(.28f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Tint(.43f));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, Tint(.53f));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, Tint(.40f));
                ImGui.PushStyleColor(ImGuiCol.TabSelected, Tint(.32f));
                ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, accent);
                ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(accent.X, accent.Y, accent.Z, .40f));
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
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
            ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5f);
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0f, 0.5f));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.0945f, 0.0808f, 0.0563f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.135f, 0.114f, 0.0788f, 0.42f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.315f, 0.266f, 0.18f, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.198f, 0.1663f, 0.1125f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.27f, 0.2233f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.324f, 0.266f, 0.1725f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.252f, 0.2043f, 0.135f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.387f, 0.304f, 0.1875f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, AccentMuted);
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.306f, 0.228f, 0.135f, 1f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.387f, 0.2945f, 0.18f, 1f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, AccentMuted);
            ImGui.PushStyleColor(ImGuiCol.TabSelected, new Vector4(0.351f, 0.2565f, 0.1425f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, Accent);
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.1215f, 0.0998f, 0.0675f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.171f, 0.133f, 0.0825f, 1f));
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
            style.WindowRounding = 6f;
            style.ChildRounding = 5f;
            style.FrameRounding = 4f;
            style.PopupRounding = 5f;
            style.ScrollbarRounding = 5f;
            style.GrabRounding = 4f;
            style.TabRounding = 4f;
            style.WindowPadding = new Vector2(14f, 12f);
            style.FramePadding = new Vector2(8f, 5f);
            style.ItemSpacing = new Vector2(10f, 7f);
            style.ItemInnerSpacing = new Vector2(8f, 5f);
            style.ScrollbarSize = 17f;
            style.IndentSpacing = 18f;

            var colors = style.Colors;
            colors[(int)ImGuiCol.Text] = new Vector4(0.93f, 0.91f, 0.86f, 1f);
            colors[(int)ImGuiCol.TextDisabled] = TextMuted;
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.126f, 0.1045f, 0.075f, 0.97f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.153f, 0.1235f, 0.09f, 1f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.144f, 0.114f, 0.0825f, 0.98f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.27f, 0.228f, 0.165f, 0.55f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.198f, 0.1615f, 0.12f, 1f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.252f, 0.209f, 0.15f, 1f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.297f, 0.247f, 0.18f, 1f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.117f, 0.095f, 0.0675f, 1f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.198f, 0.152f, 0.105f, 1f);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.144f, 0.114f, 0.0825f, 1f);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.126f, 0.1045f, 0.075f, 0.6f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.342f, 0.285f, 0.21f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.396f, 0.342f, 0.255f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = Accent;
            colors[(int)ImGuiCol.CheckMark] = Accent;
            colors[(int)ImGuiCol.SliderGrab] = AccentMuted;
            colors[(int)ImGuiCol.SliderGrabActive] = Accent;
            colors[(int)ImGuiCol.Button] = new Vector4(0.27f, 0.209f, 0.15f, 1f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.36f, 0.285f, 0.195f, 1f);
            colors[(int)ImGuiCol.ButtonActive] = AccentMuted;
            colors[(int)ImGuiCol.Header] = new Vector4(0.252f, 0.19f, 0.135f, 1f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.342f, 0.266f, 0.18f, 1f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.45f, 0.342f, 0.225f, 1f);
            colors[(int)ImGuiCol.Separator] = new Vector4(0.288f, 0.247f, 0.18f, 0.6f);
            colors[(int)ImGuiCol.Tab] = new Vector4(0.18f, 0.1425f, 0.105f, 1f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.378f, 0.285f, 0.195f, 1f);
            colors[(int)ImGuiCol.TabSelected] = new Vector4(0.378f, 0.266f, 0.165f, 1f);
            colors[(int)ImGuiCol.TabSelectedOverline] = Accent;
            colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.22f, 0.18f, 0.13f, 1f);
            colors[(int)ImGuiCol.TabDimmedSelectedOverline] = AccentMuted;
            colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.58f, 0.43f, 0.24f, 0.45f);
            colors[(int)ImGuiCol.NavCursor] = Accent;
            colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.58f, 0.43f, 0.24f, 0.25f);
            colors[(int)ImGuiCol.ResizeGripHovered] = AccentMuted;
            colors[(int)ImGuiCol.ResizeGripActive] = Accent;
            colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.58f, 0.43f, 0.24f, 0.65f);
            colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.20f, 0.18f, 0.145f, 1f);
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
                ? Vector4.Lerp(new Vector4(.055f, .05f, .045f, 1), PluginAccent(key), .16f) : SectionBg);
            ImGui.BeginChild(id, Vector2.Zero, ImGuiChildFlags.Borders);
        }

        internal static void EndPanel()
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
    }
}
