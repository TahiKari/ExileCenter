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

    internal void Normalize()
    {
        CameraDistance = Math.Clamp(float.IsFinite(CameraDistance) ? CameraDistance : 2f, 0.5f, 3f);
        if (BorderColor == null || BorderColor.Length != 4 || BorderColor.Any(v => !float.IsFinite(v))) BorderColor = [0.5f, 0.5f, 1f, 0.5f];
        if (FogColor == null || FogColor.Length != 4 || FogColor.Any(v => !float.IsFinite(v))) FogColor = [0f, 0f, 0f, 0f];
        BorderColor = BorderColor.Select(v => Math.Clamp(v, 0f, 1f)).ToArray();
        FogColor = FogColor.Select(v => Math.Clamp(v, 0f, 1f)).ToArray();
    }

    internal PermanentVisualOptions Snapshot() => new()
    {
        HideCorpses = HideCorpses, CameraDistance = PermanentCamera ? CameraDistance : null,
        MapBorders = MapBorders, BorderColor = (float[])BorderColor.Clone(), FogColor = (float[])FogColor.Clone()
    };
}

public sealed class CorpseCleanerCore : PCore<CorpseCleanerSettings>
{
    private Task<string>? operation;
    private CancellationTokenSource? cancellation;
    private string status = "", progress = "";
    private bool gameRunning = true;
    private DateTime nextCheck;
    private string SettingsPath => Path.Combine(DllDirectory, "config", "settings.json");

    public override void OnEnable(bool isGameOpened)
    {
        try { if (File.Exists(SettingsPath)) Settings = JsonSerializer.Deserialize<CorpseCleanerSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
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
                var edge = new Vector4(Settings.BorderColor[0], Settings.BorderColor[1], Settings.BorderColor[2], Settings.BorderColor[3]);
                var fog = new Vector4(Settings.FogColor[0], Settings.FogColor[1], Settings.FogColor[2], Settings.FogColor[3]);
                if (ImGui.ColorEdit4(PluginText.Label("border_color", "Border color and opacity", "MapBorderColor"), ref edge))
                { Settings.BorderColor = [edge.X, edge.Y, edge.Z, edge.W]; changed = true; }
                if (ImGui.ColorEdit4(PluginText.Label("fog_color", "Unexplored tint and opacity", "MapFogColor"), ref fog))
                { Settings.FogColor = [fog.X, fog.Y, fog.Z, fog.W]; changed = true; }
                ImGui.TextWrapped(PluginText.T("map_help", "Only the map rendering is changed. Exploration, fog visibility, Atlas and map icons are preserved. The game's map transparency setting also applies."));
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

    private void Start(bool restore)
    {
        if (operation != null) return;
        PermanentVisualOptions? options = null;
        if (!restore)
        {
            try { options = Settings.Snapshot(); options.Validate(); }
            catch (Exception ex) { status = ex.Message; return; }
        }
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        string directory = Settings.GameDirectory, native = Settings.OodleSource, cache = Path.Combine(DllDirectory, "config", "native");
        status = ""; progress = PluginText.T("working", "Preparing…");
        operation = Task.Run(() => restore ? CorpseArchive.Restore(directory, token) :
            CorpseArchive.ApplyVisuals(directory, native, cache, options!, token, progress: text => Volatile.Write(ref progress, text)), token);
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
