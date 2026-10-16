using System.Diagnostics;
using System.Text.Json;
using ExileCenter;
using ExileCenter.Plugin;
using ImGuiNET;

namespace CorpseCleaner;

public sealed class CorpseCleanerSettings : IPSettings
{
    public string GameDirectory { get; set; } = "";
    public string OodleSource { get; set; } = "";
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
        if (Settings.OodleSource.Length == 0)
        {
            string known = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "assistant_downloader", "Exile forge", "Exile.Forge.exe");
            if (File.Exists(known)) Settings.OodleSource = known;
        }
        nextCheck = DateTime.MinValue;
    }

    public override void OnDisable() => cancellation?.Cancel();

    public override void SaveSettings()
    {
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
        ImGui.TextWrapped(PluginText.T("scope", "Only corpse rendering is changed. Health values, health bars and gameplay settings are preserved. CPU/FPS gains depend on the scene."));
        string directory = Settings.GameDirectory, native = Settings.OodleSource;
        ImGui.BeginDisabled(operation != null);
        bool changed = ImGui.InputText(PluginText.Label("directory", "PoE 2 folder", "GameDirectory"), ref directory, 1024);
        changed |= ImGui.InputText(PluginText.Label("native", "oo2core.dll or Exile Forge executable", "OodleSource"), ref native, 1024);
        if (changed) { Settings.GameDirectory = directory; Settings.OodleSource = native; SaveSettings(); }
        ImGui.TextWrapped(PluginText.T("native_help", "The Oodle library is supplied by your own installation. Selecting Exile Forge extracts only oo2core.dll; its executable is never launched."));
        if (gameRunning) ImGui.TextWrapped(PluginText.T("close_game", "PoE is open. Close the game to apply or restore this patch. There is no instant in-game toggle."));
        ImGui.TextWrapped(PluginText.T("backup", "The original archive is kept in .ExileCenterCadavres inside the game folder. Standalone installations need enough free space for a full copy of Content.ggpk. Restoration is refused if a game update or another tool has changed the patched archive."));
        ImGui.BeginDisabled(gameRunning || directory.Length == 0);
        if (ImGui.Button(PluginText.Label("apply", "Hide corpses (game closed)", "Apply"))) Start(false);
        ImGui.SameLine();
        if (ImGui.Button(PluginText.Label("restore", "Restore original files", "Restore"))) Start(true);
        ImGui.EndDisabled(); ImGui.EndDisabled();
        if (operation != null)
        {
            ImGui.TextWrapped(Volatile.Read(ref progress));
            if (ImGui.Button(PluginText.Label("cancel", "Cancel preparation", "Cancel"))) cancellation?.Cancel();
        }
        if (status.Length > 0) ImGui.TextWrapped(status);
    }

    private void Start(bool restore)
    {
        if (operation != null) return;
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        string directory = Settings.GameDirectory, native = Settings.OodleSource, cache = Path.Combine(DllDirectory, "config", "native");
        status = ""; progress = PluginText.T("working", "Preparing…");
        operation = Task.Run(() => restore ? CorpseArchive.Restore(directory, token) :
            CorpseArchive.Apply(directory, native, cache, token, progress: text => Volatile.Write(ref progress, text)), token);
    }
}
