// <copyright file="PCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace ExileCenter.Plugin
{
    using ExileCenter.Localization;
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using ImGuiNET;

    /// <summary>
    ///     Interface for creating plugins.
    /// </summary>
    /// <typeparam name="TSettings">plugin's setting class name.</typeparam>
    public abstract class PCore<TSettings> : IPCore
        where TSettings : IPSettings, new()
    {
        /// <summary>
        ///     Gets or sets the plugin root directory folder.
        /// </summary>
        public string DllDirectory = null!;

        /// <summary>
        ///     Gets or sets the plugin settings.
        /// </summary>
        public TSettings Settings = new();

        private PluginLocalization? pluginText;

        /// <summary>
        ///     Gets localized text from the plugin-owned Localization directory.
        /// </summary>
        protected PluginLocalization PluginText => this.pluginText ??= new PluginLocalization(this.DllDirectory);

        /// <summary>Consistent, scrollable settings pages; only the selected page is drawn.</summary>
        protected void DrawSettingsTabs(string id, params (string key, string title, Action draw)[] pages)
        {
            ImGui.PushID(id);
            try
            {
                if (!ImGui.BeginTabBar("##SettingsPages", ImGuiTabBarFlags.FittingPolicyScroll)) return;
                try
                {
                    foreach (var page in pages)
                    {
                        using var theme = ExileCenter.Ui.ImGuiTheme.UsePluginTheme(this.GetType().Assembly.GetName().Name + "/" + page.key);
                        var title = OverlayLocalization.T("settings.section." + page.key, page.title);
                        if (!ImGui.BeginTabItem(title + "###" + page.key)) continue;
                        try
                        {
                            var visible = ImGui.BeginChild("##SettingsBody", Vector2.Zero);
                            try
                            {
                                if (!visible) continue;
                                ImGui.PushItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X * 0.35f, 120f, 250f));
                                ImGui.PushTextWrapPos(0f);
                                try { page.draw(); }
                                finally { ImGui.PopTextWrapPos(); ImGui.PopItemWidth(); }
                            }
                            finally { ImGui.EndChild(); }
                        }
                        finally { ImGui.EndTabItem(); }
                    }
                }
                finally { ImGui.EndTabBar(); }
            }
            finally { ImGui.PopID(); }
        }

        /// <inheritdoc />
        public virtual string GetDescription() => this.PluginText.T("plugin.description",
            OverlayLocalization.T("settings.plugin.help." + this.GetType().Assembly.GetName().Name, string.Empty));

        /// <inheritdoc />
        public virtual IReadOnlyCollection<string> ConflictsWith => Array.Empty<string>();

        /// <inheritdoc />
        public virtual int ConflictPriority => 0;

        /// <inheritdoc />
        public abstract void OnDisable();

        /// <inheritdoc />
        public abstract void OnEnable(bool isGameOpened);

        /// <inheritdoc />
        public abstract void DrawSettings();

        /// <inheritdoc />
        public abstract void DrawUI();

        /// <inheritdoc />
        public abstract void SaveSettings();

        /// <inheritdoc />
        public void SetPluginDllLocation(string dllLocation)
        {
            this.DllDirectory = dllLocation;
        }
    }
}
