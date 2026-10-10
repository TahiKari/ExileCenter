// <copyright file="ImGuiTheme.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
{
    using System.Numerics;
    using ImGuiNET;

    /// <summary>
    ///     Central dark theme for all GameHelper windows.
    /// </summary>
    internal static class ImGuiTheme
    {
        internal static readonly Vector4 Accent = new(0.36f, 0.55f, 0.94f, 1f);
        internal static readonly Vector4 AccentMuted = new(0.28f, 0.42f, 0.72f, 1f);
        internal static readonly Vector4 TextMuted = new(0.65f, 0.68f, 0.75f, 1f);
        internal static readonly Vector4 Success = new(0.35f, 0.78f, 0.45f, 1f);
        internal static readonly Vector4 Danger = new(0.85f, 0.35f, 0.35f, 1f);
        internal static readonly Vector4 SectionBg = new(0.14f, 0.15f, 0.19f, 1f);

        // Scoped to the settings window: in-game plugin overlays keep their existing theme.
        internal static void PushSettingsStyle()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(18f, 16f));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 6f));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 9f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
            ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 5f);
            ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0f, 0.5f));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.075f, 0.085f, 0.105f, 0.98f));
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.105f, 0.12f, 0.15f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.24f, 0.28f, 0.35f, 0.55f));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.15f, 0.175f, 0.22f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.20f, 0.235f, 0.30f, 1f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.23f, 0.28f, 0.36f, 1f));
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.215f, 0.28f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.32f, 0.43f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, AccentMuted);
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.18f, 0.24f, 0.34f, 1f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.24f, 0.31f, 0.43f, 1f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, AccentMuted);
            ImGui.PushStyleColor(ImGuiCol.TabSelected, new Vector4(0.19f, 0.27f, 0.39f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TabSelectedOverline, Accent);
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new Vector4(0.09f, 0.105f, 0.135f, 1f));
            ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.11f, 0.14f, 0.19f, 1f));
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
            style.ScrollbarSize = 14f;
            style.IndentSpacing = 18f;

            var colors = style.Colors;
            colors[(int)ImGuiCol.Text] = new Vector4(0.92f, 0.93f, 0.96f, 1f);
            colors[(int)ImGuiCol.TextDisabled] = TextMuted;
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.10f, 0.11f, 0.14f, 0.97f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.12f, 0.13f, 0.17f, 1f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.11f, 0.12f, 0.16f, 0.98f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.22f, 0.24f, 0.30f, 0.55f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.17f, 0.22f, 1f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.20f, 0.22f, 0.28f, 1f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.24f, 0.26f, 0.33f, 1f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.09f, 0.10f, 0.13f, 1f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.14f, 0.16f, 0.22f, 1f);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.11f, 0.12f, 0.16f, 1f);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.10f, 0.11f, 0.14f, 0.6f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.28f, 0.30f, 0.38f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.34f, 0.36f, 0.44f, 1f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = Accent;
            colors[(int)ImGuiCol.CheckMark] = Accent;
            colors[(int)ImGuiCol.SliderGrab] = AccentMuted;
            colors[(int)ImGuiCol.SliderGrabActive] = Accent;
            colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.22f, 0.30f, 1f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.26f, 0.30f, 0.40f, 1f);
            colors[(int)ImGuiCol.ButtonActive] = AccentMuted;
            colors[(int)ImGuiCol.Header] = new Vector4(0.18f, 0.20f, 0.28f, 1f);
            colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.24f, 0.28f, 0.38f, 1f);
            colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.30f, 0.36f, 0.50f, 1f);
            colors[(int)ImGuiCol.Separator] = new Vector4(0.24f, 0.26f, 0.32f, 0.6f);
            colors[(int)ImGuiCol.Tab] = new Vector4(0.14f, 0.15f, 0.20f, 1f);
            colors[(int)ImGuiCol.TabHovered] = new Vector4(0.26f, 0.30f, 0.42f, 1f);
            colors[(int)ImGuiCol.TabSelected] = new Vector4(0.22f, 0.28f, 0.42f, 1f);
            colors[(int)ImGuiCol.TabSelectedOverline] = Accent;
        }

        internal static void SectionHeader(string title, string? subtitle = null)
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, Accent);
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
            ImGui.PushStyleColor(ImGuiCol.ChildBg, SectionBg);
            ImGui.BeginChild(id, Vector2.Zero, ImGuiChildFlags.Borders);
        }

        internal static void EndPanel()
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
    }
}
