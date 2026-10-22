using System.Diagnostics;
using System.Text.Json;
using System.Numerics;
using ExileCenter;
using ExileCenter.Plugin;
using ImGuiNET;

namespace CorpseCleaner;

public sealed class CorpseCleanerSettings : IPSettings
{
    public string GameDirectory { get; set; } = "";
    public string OodleSource { get; set; } = "";
    public bool HideCorpses { get; set; } = true;
    public bool PermanentCamera { get; set; }
    public float CameraDistance { get; set; } = 2f;
    public bool MapBorders { get; set; }
    public float[] BorderColor { get; set; } = [0.5f, 0.5f, 1f, 0.5f];
    public float[] FogColor { get; set; } = [0f, 0f, 0f, 0f];
    public float[] ExploredColor { get; set; } = [0f, 0f, 0f, 0.3f];
    public float[] ExploringColor { get; set; } = [0f, 0.5f, 1f, 0.5f];
    public float BorderGlow { get; set; } = 12f;
    public float ExploringGlow { get; set; } = 1f;
    public float RevealThreshold { get; set; } = 0.18f;

    internal void Normalize()
    {
        CameraDistance = Math.Clamp(float.IsFinite(CameraDistance) ? CameraDistance : 2f, 0.5f, 3f);
        if (BorderColor == null || BorderColor.Length != 4 || BorderColor.Any(v => !float.IsFinite(v))) BorderColor = [0.5f, 0.5f, 1f, 0.5f];
        if (FogColor == null || FogColor.Length != 4 || FogColor.Any(v => !float.IsFinite(v))) FogColor = [0f, 0f, 0f, 0f];
        BorderColor = BorderColor.Select(v => Math.Clamp(v, 0f, 1f)).ToArray();
        FogColor = FogColor.Select(v => Math.Clamp(v, 0f, 1f)).ToArray();
        if (ExploredColor == null || ExploredColor.Length != 4 || ExploredColor.Any(v => !float.IsFinite(v))) ExploredColor = [0f, 0f, 0f, 0.3f];
        if (ExploringColor == null || ExploringColor.Length != 4 || ExploringColor.Any(v => !float.IsFinite(v))) ExploringColor = [0f, 0.5f, 1f, 0.5f];
        ExploredColor = ExploredColor.Select(v => Math.Clamp(v, 0f, 1f)).ToArray();
        ExploringColor = ExploringColor.Select(v => Math.Clamp(v, 0f, 1f)).ToArray();
        BorderGlow = Math.Clamp(float.IsFinite(BorderGlow) ? BorderGlow : 12f, 1f, 20f);
        ExploringGlow = Math.Clamp(float.IsFinite(ExploringGlow) ? ExploringGlow : 1f, 1f, 20f);
        RevealThreshold = Math.Clamp(float.IsFinite(RevealThreshold) ? RevealThreshold : 0.18f, 0f, 1f);
    }

    internal PermanentVisualOptions Snapshot() => new()
    {
        HideCorpses = HideCorpses, CameraDistance = PermanentCamera ? CameraDistance : null,
        MapBorders = MapBorders, BorderColor = (float[])BorderColor.Clone(), FogColor = (float[])FogColor.Clone(),
        ExploredColor = (float[])ExploredColor.Clone(), ExploringColor = (float[])ExploringColor.Clone(),
        BorderGlow = BorderGlow, ExploringGlow = ExploringGlow, RevealThreshold = RevealThreshold
    };

    internal void ResetMinimapColors()
    {
        ExploredColor = [0f, 0f, 0f, 0.3f]; BorderColor = [1f, 1f, 1f, 0.1f];
        ExploringColor = [0f, 0.5f, 1f, 0.5f];
        BorderGlow = 12f; ExploringGlow = 1f; RevealThreshold = 0.18f;
    }
}

public sealed class CorpseCleanerCore : PCore<CorpseCleanerSettings>
{
    private Task<string>? operation;
    private CancellationTokenSource? cancellation;
    private string status = "", progress = "";
    private string minimapHex = "FFFFFFFF";
    private bool gameRunning = true;
    private DateTime nextCheck;
    private string SettingsPath => Path.Combine(DllDirectory, "config", "settings.json");

    public override void OnEnable(bool isGameOpened)
    {
        try
        {
            string saved = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : "{}";
            Settings = JsonSerializer.Deserialize<CorpseCleanerSettings>(saved) ?? new();
            using var json = JsonDocument.Parse(saved);
            // First upgrade after a prepared minimap patch: import its recorded colors,
            // without overwriting preferences already saved by the new UI.
            if (!json.RootElement.TryGetProperty(nameof(Settings.ExploredColor), out _) && Settings.GameDirectory.Length > 0 &&
                CorpseArchive.ReadMinimapOptions(Settings.GameDirectory) is { } map)
            {
                Settings.MapBorders = true;
                Settings.ExploredColor = map.ExploredColor; Settings.BorderColor = map.BorderColor; Settings.ExploringColor = map.ExploringColor;
                Settings.BorderGlow = map.BorderGlow; Settings.ExploringGlow = map.ExploringGlow; Settings.RevealThreshold = map.RevealThreshold;
            }
        }
        catch (Exception ex) { status = ex.Message; }
        Settings.Normalize();
        nextCheck = DateTime.MinValue;
    }

    public override void OnDisable() => cancellation?.Cancel();

    public override void SaveSettings()
    {
        Settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
        else File.Move(temporary, SettingsPath);
    }

    public override void DrawUI()
    {
        if (operation?.IsCompleted == true)
        {
            try { status = operation.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { status = PluginText.T("cancelled", "Operation cancelled."); }
            catch (Exception ex) { status = ex.Message; }
            operation = null; progress = ""; cancellation?.Dispose(); cancellation = null;
        }
        if (DateTime.UtcNow < nextCheck) return;
        nextCheck = DateTime.UtcNow.AddSeconds(1);
        try { gameRunning = CorpseArchive.GameRunning(); }
        catch { gameRunning = true; }
        if (Settings.GameDirectory.Length == 0 && Core.Process.Pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)Core.Process.Pid);
                string? file = process.MainModule?.FileName;
                if (file != null) { Settings.GameDirectory = Path.GetDirectoryName(file)!; SaveSettings(); }
            }
            catch { }
        }
    }

    public override void DrawSettings()
    {
        DrawUI(); // Settings remain usable and jobs finish while the game is closed.
        ImGui.TextWrapped(PluginText.T("help", "Hide monster corpses using a reversible game-file patch. Supports Steam/Epic bundles and the standalone Content.ggpk. Changes take effect after restarting the game."));
        ImGui.TextWrapped(PluginText.T("scope", "Permanent file patches stay active with ExileCenter closed, until restoration or a game update. To change an applied patch, restore it first, then apply the selected options with the game closed."));
        string directory = Settings.GameDirectory;
        ImGui.BeginDisabled(operation != null);
        bool changed = false;
        if (ImGui.CollapsingHeader(PluginText.Label("permanent", "Permanent options", "PermanentVisuals"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool corpses = Settings.HideCorpses, camera = Settings.PermanentCamera, borders = Settings.MapBorders;
            changed |= ImGui.Checkbox(PluginText.Label("corpses", "Hide monster corpses", "PermanentCorpses"), ref corpses);
            changed |= ImGui.Checkbox(PluginText.Label("camera", "Save a permanent camera distance", "PermanentCamera"), ref camera);
            if (camera)
            {
                float distance = Settings.CameraDistance * 100f;
                if (ImGui.SliderFloat(PluginText.Label("distance", "Saved camera distance", "PermanentDistance"), ref distance, 50f, 300f, "%.0f %%"))
                { Settings.CameraDistance = distance / 100f; changed = true; }
                if (ImGui.Button(PluginText.Label("capture_camera", "Use the last distance measured by CameraZoom", "CaptureCamera"))) UseSavedCamera();
                ImGui.TextWrapped(PluginText.T("camera_help", "This is the starting distance, applied when the character is positioned. Scripted cameras remain unchanged. To save a distance used with the mouse wheel, close ExileCenter normally first, then reopen it and use the measured distance."));
            }
            changed |= ImGui.Checkbox(PluginText.Label("map", "Show all map borders without revealing exploration", "PermanentMap"), ref borders);
            if (borders)
            {
                changed |= DrawMinimapColors();
            }
            Settings.HideCorpses = corpses; Settings.PermanentCamera = camera; Settings.MapBorders = borders;
        }
        changed |= ImGui.InputText(PluginText.Label("directory", "PoE 2 folder", "GameDirectory"), ref directory, 1024);
        if (changed) { Settings.GameDirectory = directory; SaveSettings(); }
        DrawWrappedText(PluginText.T("native_help", "The archive codec is included in ExileCenter. No external DLL or ExileForge is required. Modified archives may use more disk space."));
        if (gameRunning) ImGui.TextWrapped(PluginText.T("close_game", "PoE is open. Close the game to apply or restore this patch. There is no instant in-game toggle."));
        ImGui.TextWrapped(PluginText.T("backup", "The original archive is kept in .ExileCenterCadavres inside the game folder. Standalone installations need enough free space for a full copy of Content.ggpk. Restoration is refused if a game update or another tool has changed the patched archive."));
        ImGui.BeginDisabled(gameRunning || directory.Length == 0);
        if (ImGui.Button(PluginText.Label("apply", "Apply selected permanent options (game closed)", "Apply"))) Start(false);
        ImGui.SameLine();
        if (ImGui.Button(PluginText.Label("restore", "Restore original files", "Restore"))) Start(true);
        ImGui.Separator();
        ImGui.BeginDisabled(!Settings.MapBorders);
        if (ImGui.Button(PluginText.Label("apply_map", "Apply minimap only (preserve corpses)", "ApplyMap"))) Start(false, true);
        ImGui.EndDisabled();
        if (ImGui.Button(PluginText.Label("restore_map", "Restore minimap only (preserve corpses)", "RestoreMap"))) Start(true, true);
        ImGui.EndDisabled(); ImGui.EndDisabled();
        if (operation != null)
        {
            DrawWrappedText(Volatile.Read(ref progress));
            if (ImGui.Button(PluginText.Label("cancel", "Cancel preparation", "Cancel"))) cancellation?.Cancel();
        }
        if (status.Length > 0) DrawWrappedText(status);
    }

    private static void DrawWrappedText(string text)
    {
        ImGui.PushTextWrapPos(0);
        try { ImGui.TextUnformatted(text); }
        finally { ImGui.PopTextWrapPos(); }
    }

    private bool DrawMinimapColors()
    {
        bool changed = false;
        float[] explored = Settings.ExploredColor, border = Settings.BorderColor, exploring = Settings.ExploringColor;
        changed |= DrawMapColor("explored_color", "Explored area (fill)", ref explored);
        changed |= DrawMapColor("border_color", "Border / corner (static edge)", ref border);
        float borderGlow = Settings.BorderGlow, exploringGlow = Settings.ExploringGlow, threshold = Settings.RevealThreshold;
        changed |= ImGui.SliderFloat(PluginText.Label("border_glow", "Border glow", "BorderGlow"), ref borderGlow, 1f, 20f, "%.1f x");
        changed |= DrawMapColor("exploring_color", "Currently exploring (live vision edge)", ref exploring);
        changed |= ImGui.SliderFloat(PluginText.Label("exploring_glow", "Exploring glow", "ExploringGlow"), ref exploringGlow, 1f, 20f, "%.1f x");
        changed |= ImGui.SliderFloat(PluginText.Label("reveal_threshold", "Reveal threshold (unexplored dimness)", "RevealThreshold"), ref threshold, 0f, 1f, "%.2f");
        Settings.ExploredColor = explored; Settings.BorderColor = border; Settings.ExploringColor = exploring;
        Settings.BorderGlow = borderGlow; Settings.ExploringGlow = exploringGlow; Settings.RevealThreshold = threshold;
        if (ImGui.Button(PluginText.Label("reset_map", "Reset minimap colors to defaults", "ResetMap")))
        { Settings.ResetMinimapColors(); changed = true; }
        DrawWrappedText(PluginText.T("map_help", "Changes the two native minimap shaders. Explored fill, static borders and the exploration edge have separate RGBA colors. Glow multiplies RGB only. The threshold makes unexplored contours visible; exploration continues as you move. Restore minimap only before applying new values, with PoE closed. ExileCenter can then be closed."));
        return changed;
    }

    private bool DrawMapColor(string key, string fallback, ref float[] channels)
    {
        var color = new Vector4(channels[0], channels[1], channels[2], channels[3]);
        var before = color;
        ImGui.PushID(key);
        DrawWrappedText(PluginText.T(key, fallback));
        if (ImGui.ColorButton("##preview", color, ImGuiColorEditFlags.AlphaPreview,
            new Vector2(Math.Clamp(ImGui.GetContentRegionAvail().X, 1f, 360f), 22f)))
        { minimapHex = MapHex(color); ImGui.OpenPopup("picker"); }
        if (ImGui.BeginPopup("picker"))
        {
            if (ImGui.BeginTabBar("ColorTabs"))
            {
                if (ImGui.BeginTabItem(PluginText.T("palette", "Standard")))
                {
                    for (int row = 0; row < 8; row++)
                    for (int column = 0; column < 12; column++)
                    {
                        if (column > 0) ImGui.SameLine(0, 2);
                        float r, g, b;
                        if (row == 0) r = g = b = column / 11f;
                        else ImGui.ColorConvertHSVtoRGB(column / 12f, row < 4 ? row / 3f : 1f, row < 4 ? 1f : (11f - row) / 8f, out r, out g, out b);
                        if (ImGui.ColorButton("##swatch" + (row * 12 + column), new Vector4(r, g, b, 1f), ImGuiColorEditFlags.NoTooltip, new Vector2(18)))
                        { color.X = r; color.Y = g; color.Z = b; minimapHex = MapHex(color); }
                    }
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(PluginText.T("advanced", "Advanced")))
                {
                    ImGui.SetNextItemWidth(300);
                    if (ImGui.ColorPicker4("##rgba", ref color, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreviewHalf |
                        ImGuiColorEditFlags.DisplayRGB | ImGuiColorEditFlags.Uint8 | ImGuiColorEditFlags.PickerHueBar)) minimapHex = MapHex(color);
                    ImGui.SetNextItemWidth(140);
                    if (ImGui.InputText("#AARRGGBB", ref minimapHex, 9, ImGuiInputTextFlags.CharsHexadecimal) && minimapHex.Length == 8 &&
                        uint.TryParse(minimapHex, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint argb))
                        color = new Vector4((argb >> 16) & 255, (argb >> 8) & 255, argb & 255, argb >> 24) / 255f;
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            if (ImGui.Button(PluginText.Label("close_picker", "Close", "ClosePicker"))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.PopID();
        if (color == before) return false;
        channels = [color.X, color.Y, color.Z, color.W];
        return true;
    }

    private static string MapHex(Vector4 color) =>
        ((uint)Math.Clamp((int)MathF.Round(color.W * 255f), 0, 255) << 24 |
         (uint)Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255) << 16 |
         (uint)Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255) << 8 |
         (uint)Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255)).ToString("X8");

    private void Start(bool restore, bool minimapOnly = false)
    {
        if (operation != null) return;
        PermanentVisualOptions? options = null;
        if (!restore)
        {
            try
            {
                options = minimapOnly ? Settings.Snapshot() with { HideCorpses = false, CameraDistance = null, MapBorders = true } :
                    Settings.Snapshot() with { MapBorders = false };
                options.Validate();
            }
            catch (Exception ex) { status = ex.Message; return; }
        }
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        string directory = Settings.GameDirectory, native = Settings.OodleSource, cache = Path.Combine(DllDirectory, "config", "native");
        status = ""; progress = PluginText.T("working", "Preparing…");
        operation = Task.Run(() => restore ? CorpseArchive.Restore(directory, token, minimapOnly: minimapOnly) :
            CorpseArchive.ApplyVisuals(directory, native, cache, options!, token, progress: text => Volatile.Write(ref progress, text), minimapOnly: minimapOnly), token);
    }

    private void UseSavedCamera()
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(DllDirectory, "..", "CameraZoom", "config", "settings.txt"));
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (!json.RootElement.TryGetProperty("LastDistance", out var value) || !value.TryGetSingle(out float distance) ||
                !float.IsFinite(distance) || distance < 0.5f || distance > 3f)
                throw new InvalidDataException(PluginText.T("camera_missing", "No measured distance between 50% and 300% is available. Choose the distance with the slider."));
            Settings.CameraDistance = distance; SaveSettings();
            status = PluginText.T("camera_saved", "Measured distance selected. Close the game, restore any previous patch, then apply the selected permanent options.");
        }
        catch (Exception ex) { status = ex.Message; }
    }
}
