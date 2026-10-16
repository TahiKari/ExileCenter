// <copyright file="Program.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO.Compression;
    using System.Linq;
    using System.Numerics;
    using System.Net.Http;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Threading;
    using ImGuiNET;
    using L = GameHelper.Localization.OverlayLocalization;
    using Utils;

    /// <summary>
    ///     Class executed when the application starts.
    /// </summary>
    internal class Program
    {
        /// <summary>
        ///     function executed when the application starts.
        /// </summary>
        // Returns true if the launcher (GameHelper.App.exe) started us.
        // The launcher renames the exe before starting it, so our process name
        // differs from "GameHelper.exe" when we were launched via GameHelper.App.exe.
        private static bool WasStartedByLauncher()
        {
            var myExe = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
            return !string.Equals(myExe, "GameHelper.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task Main()
        {
            // If the user started GameHelper.exe directly (not via launcher),
            // redirect to GameHelper.App.exe so they get update checks.
            if (!WasStartedByLauncher() && !LocalUpdates.IsLocalInstallation)
            {
                var launcherPath = Path.Combine(AppContext.BaseDirectory, "GameHelper.App.exe");
                if (File.Exists(launcherPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = launcherPath,
                        WorkingDirectory = AppContext.BaseDirectory,
                        UseShellExecute = true,
                    });
                    return;
                }
            }

            AppDomain.CurrentDomain.UnhandledException += (sender, exceptionArgs) =>
            {
                var errorText = "Program exited with message:\n " + exceptionArgs.ExceptionObject;
                File.AppendAllText("Error.log", $"{DateTime.Now:g} {errorText}\r\n{new string('-', 30)}\r\n");

                // Do NOT call Environment.Exit — it skips `using` Dispose and leaks
                // the SafeMemoryHandle (audit F-061). The runtime will terminate
                // the process naturally because IsTerminating == true for unhandled
                // exceptions on the main thread.
            };

            JsonHelper.InitializeDefaultSettings(Directory.GetCurrentDirectory());
            if (await LocalUpdates.InstallBeforeStartupAsync()) return;

            using (Core.Overlay = new GameOverlay(MiscHelper.GenerateRandomString()))
            {
                await Core.Overlay.Run();
                LocalUpdates.CompleteShutdown();
            }
        }
    }

    // Local packages take priority; public GitHub releases use the same verified installer.
    internal static class LocalUpdates
    {
        internal static readonly string UpdateDirectory = ResolveUpdateDirectory();

        private static string ResolveUpdateDirectory()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string current = Path.Combine(desktop, "ExileCenter", "Maj ExileCenter");
            return new[] { current, Path.Combine(desktop, "ExileCenter", "Maj GameHelper"),
                Path.Combine(desktop, "Gamehelpers", "Maj GameHelper") }.FirstOrDefault(Directory.Exists) ?? current;
        }
        private static readonly string InstallDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        private const string StateName = "local-update-state.json";
        private const string ErrorName = "local-update-error.txt";
        private const string Repository = "TahiKari/ExileCenter";
        private const string GitHubPackageName = "ExileCenter-win-x64.zip";
        private static readonly HttpClient GitHubClient = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly HttpClient PackageClient = new() { Timeout = TimeSpan.FromMinutes(3) };
        private static readonly string? CurrentReleaseTag = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "GameHelperReleaseTag")?.Value;
        private sealed record Package(string Id, string PackageFile, string Sha256, string Label)
        {
            public string? ReleaseTag { get; init; }
            public string? Changes { get; init; }
            public string? DownloadUrl { get; init; }
            public long DownloadSize { get; init; }
        }
        private sealed record CheckResult(Package? Offer, string? Error);
        private static Task<CheckResult>? checkTask;
        private static Task<string>? prepareTask;
        private static Package? offer;
        private static DateTime nextCheck;
        private static DateTime nextGitHubCheck;
        private static CheckResult githubResult = new(null, null);
        private static DateTime toastUntil;
        private static string? lastNotified;
        private static string? error;
        private static bool installerStarted;
        private static long downloadBytes;
        private static long downloadTotal;
        internal static bool IsLocalInstallation => Directory.Exists(UpdateDirectory) || File.Exists(Path.Combine(InstallDirectory, StateName));
        internal static bool IsAvailable => offer != null;
        internal static string InstalledVersion => DisplayVersion(CurrentReleaseTag) ?? Core.GetVersion();

        internal static async Task<bool> InstallBeforeStartupAsync(Func<string, bool>? confirm = null)
        {
            var result = await CheckAvailableAsync();
            offer = result.Offer;
            error = result.Error;
            // A failed installer restarts the old version. Leave retries to the update page.
            if (offer == null || error != null) return false;
            lastNotified = offer.Id;
            var summary = offer.Label;
            if (!string.IsNullOrWhiteSpace(offer.Changes))
                summary += "\n\n" + L.T("updates.changes", "What changes in this update") + "\n" + offer.Changes;
            var question = L.F("updates.startup_question", "{0}\n\nInstall this update before starting GameHelper?\nYou can also continue with the current version. Your settings are preserved.", L.NormalizeDisplayText(summary, false));
            try
            {
                if (confirm != null)
                {
                    if (!confirm(question)) return false;
                    await PrepareInstallerAsync(offer, InstallDirectory, UpdateDirectory, Environment.ProcessId);
                    installerStarted = true;
                }
                else
                {
                    using var prompt = new StartupUpdateWindow(question, UpdateTitle(offer), async () =>
                    {
                        await PrepareInstallerAsync(offer, InstallDirectory, UpdateDirectory, Environment.ProcessId);
                        installerStarted = true;
                    });
                    await prompt.Run();
                    if (!prompt.InstallRequested) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                RecordUpdateError(ex);
                return false;
            }
        }

        private sealed class StartupUpdateWindow : ClickableTransparentOverlay.Overlay
        {
            private readonly string question;
            private readonly string title;
            private readonly Func<Task> install;
            private Task? installTask;
            private string? installError;
            private SettingsDisplay? display;
            private volatile bool closeRequested;
            internal bool InstallRequested { get; private set; }

            internal StartupUpdateWindow(string question, string title, Func<Task> install) : base("ExileCenter update", true, 1, 1)
            {
                this.question = question;
                this.title = title;
                this.install = install;
                this.VSync = true;
            }

            public override void Close()
            {
                if (this.installTask?.IsCompleted == false) return;
                this.closeRequested = true;
            }

            protected override Task PostInitialized()
            {
                Ui.ImGuiTheme.Apply();
                var settings = Core.GHSettings;
                if (MiscHelper.TryConvertStringToImGuiGlyphRanges(settings.FontCustomGlyphRange, out var ranges))
                    this.ReplaceFont(settings.FontPathName, settings.FontSize, ranges);
                else
                    this.ReplaceFont(settings.FontPathName, settings.FontSize, settings.FontLanguage);
                this.display = new SettingsDisplay(this, this.Draw, this.Close, new Settings.State(),
                    () => this.title, _ => { },
                    new System.Drawing.Size(620, 360), new System.Drawing.Size(760, 480));
                return Task.CompletedTask;
            }

            protected override void Render()
            {
                if (this.installTask?.IsCompleted == true)
                {
                    try
                    {
                        this.installTask.GetAwaiter().GetResult();
                        this.InstallRequested = true;
                        this.Close();
                    }
                    catch (Exception ex)
                    {
                        this.installError = ex.Message;
                        RecordUpdateError(ex);
                    }
                    this.installTask = null;
                }
                if (!this.closeRequested) this.display?.Render(true);
                if (!this.closeRequested) return;
                this.display?.Dispose();
                this.display = null;
                base.Close();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { this.display?.Dispose(); this.display = null; }
                base.Dispose(disposing);
            }

            private void Draw()
            {
                Ui.ImGuiTheme.PushSettingsStyle();
                try
                {
                    ImGui.SetNextWindowPos(Vector2.Zero);
                    ImGui.SetNextWindowSize(ImGui.GetIO().DisplaySize);
                    ImGui.Begin("##StartupUpdate", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                        ImGuiWindowFlags.NoSavedSettings);
                    try
                    {
                        Ui.ImGuiTheme.SectionHeader(this.title ?? L.T("updates.available", "Update available"));
                        if (ImGui.BeginChild("##UpdateDetails", new Vector2(0, -62), ImGuiChildFlags.Borders))
                        {
                            if (this.installError != null)
                            {
                                ImGui.TextWrapped(L.F("updates.error", "Update failed: {0}", this.installError));
                                ImGui.Separator();
                            }
                            ImGui.TextWrapped(this.question);
                        }
                        ImGui.EndChild();
                        ImGui.Spacing();
                        if (this.installTask != null)
                        {
                            DrawPreparationProgress();
                            return;
                        }
                        var width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
                        ImGui.PushStyleColor(ImGuiCol.Button, Ui.ImGuiTheme.AccentMuted);
                        if (ImGui.Button(L.T("updates.startup_install", "Install update"), new Vector2(width, 38)))
                        {
                            this.installError = null;
                            this.installTask = this.install();
                        }
                        ImGui.PopStyleColor();
                        ImGui.SameLine();
                        if (ImGui.Button(L.T("updates.startup_continue", "Continue without updating"), new Vector2(width, 38))) this.Close();
                        ImGui.SetItemDefaultFocus();
                        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) this.Close();
                    }
                    finally { ImGui.End(); }
                }
                finally { Ui.ImGuiTheme.PopSettingsStyle(); }
            }
        }

        private static Version? ReleaseVersion(string? tag)
        {
            if (tag == null) return null;
            var match = System.Text.RegularExpressions.Regex.Match(tag, @"^v(\d+\.\d+\.\d+)(?:-personnalise\.(\d+))?$");
            return match.Success && Version.TryParse(match.Groups[1].Value + "." +
                (match.Groups[2].Success ? match.Groups[2].Value : "0"), out var version) ? version : null;
        }

        private static string? DisplayVersion(string? tag)
        {
            var version = ReleaseVersion(tag);
            if (version == null) return null;
            return tag!.Contains("-personnalise.", StringComparison.Ordinal)
                ? "P-V1." + Math.Max(0, version.Revision - 5)
                : "v" + version.ToString(3);
        }

        private static string UpdateTitle(Package package)
        {
            var version = DisplayVersion(package.ReleaseTag);
            if (version == null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(package.Label, @"\bP-V\d+(?:\.\d+)*[a-z]?\b");
                if (match.Success) version = match.Value;
            }
            return version == null ? L.T("updates.available", "Update available")
                : "ExileCenter " + version + " · " + L.T("updates.title", "Updates");
        }

        private static Package? ParseGitHubRelease(JsonElement release, string? installedTag)
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
            var tag = release.GetProperty("tag_name").GetString();
            var current = ReleaseVersion(installedTag);
            var available = ReleaseVersion(tag);
            if (current == null || available == null || available <= current) return null;
            var assets = release.GetProperty("assets").EnumerateArray().Where(asset =>
                asset.GetProperty("name").GetString() == GitHubPackageName && asset.GetProperty("state").GetString() == "uploaded").ToArray();
            if (assets.Length != 1) return null;
            var asset = assets[0];
            var url = $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag!)}/{GitHubPackageName}";
            var digest = asset.GetProperty("digest").GetString();
            long size = asset.GetProperty("size").GetInt64();
            if (asset.GetProperty("browser_download_url").GetString() != url || size <= 0 || size > 128L * 1024 * 1024 ||
                digest == null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || !digest[7..].All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid GitHub release package.");
            return new("github:" + tag, GitHubPackageName, digest[7..], release.GetProperty("name").GetString() ?? tag!)
            {
                ReleaseTag = tag,
                Changes = release.TryGetProperty("body", out var body) ? body.GetString() : null,
                DownloadUrl = url,
                DownloadSize = size
            };
        }

        private static async Task<CheckResult> CheckAvailableAsync()
        {
            var local = await Task.Run(() => Check(UpdateDirectory, InstallDirectory));
            if (local.Offer != null) return local;
            if (DateTime.UtcNow >= nextGitHubCheck)
            {
                nextGitHubCheck = DateTime.UtcNow.AddMinutes(2);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
                    request.Headers.UserAgent.ParseAdd("ExileCenter/1.0");
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using var response = await GitHubClient.SendAsync(request);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) githubResult = new(null, null);
                    else
                    {
                        response.EnsureSuccessStatusCode();
                        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        githubResult = new(ParseGitHubRelease(json.RootElement, CurrentReleaseTag), null);
                    }
                }
                catch (Exception ex) { githubResult = new(null, ex.Message); }
            }
            return new(githubResult.Offer, local.Error ?? githubResult.Error);
        }

        private static async Task<string> PrepareInstallerAsync(Package package, string installRoot, string updateRoot, int processId)
        {
            var stage = await PreparePackageAsync(package, installRoot, updateRoot, processId);
            await StartInstallerAsync(stage);
            return stage;
        }

        private static async Task<string> PreparePackageAsync(Package package, string installRoot, string updateRoot, int processId)
        {
            Interlocked.Exchange(ref downloadBytes, 0);
            Interlocked.Exchange(ref downloadTotal, package.DownloadSize);
            if (package.DownloadUrl == null) return await Task.Run(() => Prepare(package, installRoot, updateRoot, processId));
            var downloadRoot = Path.Combine(Path.GetTempPath(), "GameHelper-Download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadRoot);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                using (var response = await PackageClient.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long length && length != package.DownloadSize)
                        throw new InvalidDataException("GitHub package size changed.");
                    using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                    using var output = new FileStream(Path.Combine(downloadRoot, package.PackageFile), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, timeout.Token)) != 0)
                    {
                        if ((total += read) > package.DownloadSize) throw new InvalidDataException("GitHub package too large.");
                        await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                        Interlocked.Exchange(ref downloadBytes, total);
                    }
                    if (total != package.DownloadSize) throw new InvalidDataException("Incomplete GitHub download.");
                }
                return await Task.Run(() => Prepare(package, installRoot, downloadRoot, processId));
            }
            finally { Directory.Delete(downloadRoot, true); }
        }

        private static CheckResult Check(string updateRoot, string installRoot)
        {
            try
            {
                var errorPath = Path.Combine(installRoot, ErrorName);
                var installationError = File.Exists(errorPath) ? File.ReadAllText(errorPath) : null;
                var manifestPath = Path.Combine(updateRoot, "ExileCenter-Maj.json");
                if (!File.Exists(manifestPath)) manifestPath = Path.Combine(updateRoot, "GameHelper-Maj.json");
                if (!File.Exists(manifestPath)) return new(null, installationError);
                var candidate = JsonSerializer.Deserialize<Package>(File.ReadAllText(manifestPath));
                if (candidate == null || candidate.DownloadUrl != null || string.IsNullOrWhiteSpace(candidate.Id) || string.IsNullOrWhiteSpace(candidate.Label) ||
                    candidate.Sha256 == null || candidate.Sha256.Length != 64 || !candidate.Sha256.All(Uri.IsHexDigit) ||
                    string.IsNullOrWhiteSpace(candidate.PackageFile) || candidate.PackageFile != Path.GetFileName(candidate.PackageFile) ||
                    !candidate.PackageFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Path.GetFileName(manifestPath));
                var statePath = Path.Combine(installRoot, StateName);
                if (File.Exists(statePath))
                {
                    using var state = JsonDocument.Parse(File.ReadAllText(statePath));
                    if (state.RootElement.GetProperty("Id").GetString() == candidate.Id &&
                        string.Equals(state.RootElement.GetProperty("Sha256").GetString(), candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                        return new(null, installationError);
                }
                if (ReleaseVersion(candidate.ReleaseTag) is { } available &&
                    ReleaseVersion(CurrentReleaseTag) is { } installed && available <= installed)
                    return new(null, installationError);
                if (!File.Exists(Path.Combine(updateRoot, candidate.PackageFile))) return new(null, installationError);
                return new(candidate, installationError);
            }
            catch (Exception ex) { return new(null, ex.Message); }
        }

        internal static void Render()
        {
            if (installerStarted) return;
            if (checkTask?.IsCompleted == true)
            {
                var result = checkTask.GetAwaiter().GetResult();
                checkTask = null;
                if (prepareTask == null)
                {
                    offer = result.Offer;
                    error = result.Error;
                }
                if (offer != null && lastNotified != offer.Id)
                {
                    lastNotified = offer.Id;
                    toastUntil = DateTime.UtcNow.AddSeconds(10);
                }
            }
            if (prepareTask?.IsCompleted == true)
            {
                try
                {
                    prepareTask.GetAwaiter().GetResult();
                    prepareTask = null;
                    Coroutine.CoroutineHandler.RaiseEvent(CoroutineEvents.GameHelperEvents.TimeToSaveAllSettings);
                    installerStarted = true;
                    Core.GHSettings.IsOverlayRunning = false;
                }
                catch (Exception ex) { prepareTask = null; RecordUpdateError(ex); }
            }
            if (prepareTask == null && checkTask == null && DateTime.UtcNow >= nextCheck)
            {
                nextCheck = DateTime.UtcNow.AddSeconds(5);
                checkTask = CheckAvailableAsync();
            }
            if (offer == null || DateTime.UtcNow >= toastUntil) return;
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X + viewport.Size.X / 2f, viewport.Pos.Y + 28f), ImGuiCond.Always, new Vector2(0.5f, 0));
            ImGui.SetNextWindowBgAlpha(0.85f);
            if (ImGui.Begin("##LocalUpdateNotification", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
            {
                ImGui.TextColored(Ui.ImGuiTheme.Accent, UpdateTitle(offer));
                ImGui.Text(L.F("updates.toast", "Open GameHelper with {0} to install it.", Core.GHSettings.MainMenuHotKey));
            }
            ImGui.End();
        }

        internal static void DrawSettings()
        {
            Ui.ImGuiTheme.SectionHeader(L.T("updates.title", "Updates"), L.T("updates.description", "Updates from GitHub or your local folder. Your settings are preserved."));
            ImGui.TextWrapped("https://github.com/" + Repository + "/releases");
            if (Directory.Exists(UpdateDirectory)) ImGui.TextWrapped(UpdateDirectory);
            ImGui.TextDisabled(L.T("updates.poll", "Checked at startup; GitHub every 2 minutes, local updates every 5 seconds."));
            ImGui.Separator();
            if (prepareTask != null) DrawPreparationProgress();
            else if (offer != null)
            {
                ImGui.TextColored(Ui.ImGuiTheme.Accent, UpdateTitle(offer));
                ImGui.TextWrapped(L.NormalizeDisplayText(offer.Label, false));
                if (!string.IsNullOrWhiteSpace(offer.Changes))
                {
                    ImGui.TextColored(Ui.ImGuiTheme.Accent, L.T("updates.changes", "What changes in this update"));
                    ImGui.TextWrapped(L.NormalizeDisplayText(offer.Changes, false));
                }
                if (ImGui.Button(L.T("updates.apply", "Update and restart")))
                {
                    error = null;
                    var selected = offer;
                    prepareTask = PrepareInstallerAsync(selected, InstallDirectory, UpdateDirectory, Environment.ProcessId);
                }
            }
            else ImGui.TextWrapped(L.T("updates.none", "No update available."));
            ImGui.BeginDisabled(prepareTask != null);
            if (ImGui.Button(L.T("updates.check", "Check now"))) { nextCheck = DateTime.MinValue; nextGitHubCheck = DateTime.MinValue; }
            ImGui.SameLine();
            if (ImGui.Button(L.T("updates.folder", "Open update folder")))
            {
                try
                {
                    Directory.CreateDirectory(UpdateDirectory);
                    Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { UpdateDirectory }, UseShellExecute = true });
                }
                catch (Exception ex) { error = ex.Message; }
            }
            ImGui.EndDisabled();
            ImGui.TextWrapped(L.T("updates.restart_hint", "GameHelper briefly closes to replace its files, then restarts. The game stays open."));
            if (error != null) ImGui.TextWrapped(L.F("updates.error", "Update failed: {0}", error));
        }

        private static void DrawPreparationProgress()
        {
            ImGui.TextWrapped(L.T("updates.preparing", "Downloading, checking and preparing the update…"));
            var total = Interlocked.Read(ref downloadTotal);
            var received = Interlocked.Read(ref downloadBytes);
            if (total > 0)
                ImGui.ProgressBar((float)received / total, new Vector2(-1, 0), $"{received / 1048576d:0.0} / {total / 1048576d:0.0} MB");
        }

        private static void RecordUpdateError(Exception ex)
        {
            error = ex.Message;
            Console.WriteLine("[Local update] " + ex);
            try { File.WriteAllText(Path.Combine(InstallDirectory, ErrorName), ex.ToString()); }
            catch (Exception logError) { Console.WriteLine("[Local update log] " + logError.Message); }
        }

        private static bool IsPackageFile(string relative)
        {
            var segments = relative.Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith('.') || segment.EndsWith(' '))) return false;
            if (segments.Any(segment => segment.Equals("config", StringComparison.OrdinalIgnoreCase) || segment.Equals("configs", StringComparison.OrdinalIgnoreCase)) ||
                Path.GetFileName(relative).Equals("settings.json", StringComparison.OrdinalIgnoreCase)) return false;
            if (segments.Length == 1)
                return relative.Equals("ExileCenter.exe", StringComparison.OrdinalIgnoreCase) || relative.Equals("GameHelper.exe", StringComparison.OrdinalIgnoreCase) || relative.Equals("GameHelper.App.exe", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    relative is "GameHelper.deps.json" or "GameHelper.runtimeconfig.json" or "LICENSE" or "CREDITS.md" or "README.md" or "SECURITY.md";
            return segments[0] is "Plugins" or "Localization" or "fonts" && !relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeTarget(string root, string relative)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(relative);
            for (var current = target; current != null && current.Length >= fullRoot.Length - 1; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0 &&
                    !UpdatePathHelper.IsAllowedReparsePoint(current))
                    throw new InvalidDataException("Reparse point: " + current);
            return target;
        }

        private static string Prepare(Package package, string installRoot, string updateRoot, int processId)
        {
            var stage = Path.Combine(Path.GetTempPath(), "GameHelper-Update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var localZip = Path.Combine(stage, "package.zip");
            File.Copy(Path.Combine(updateRoot, package.PackageFile), localZip);
            using (var stream = File.OpenRead(localZip))
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA256: " + package.PackageFile);
            var payload = Path.Combine(stage, "payload");
            var files = new List<object>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalSize = 0;
            using (var archive = ZipFile.OpenRead(localZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    var relative = entry.FullName.Replace('\\', '/');
                    if (!IsPackageFile(relative) || !names.Add(relative) || names.Count > 10000 || (totalSize += entry.Length) > 512L * 1024 * 1024)
                        throw new InvalidDataException("Invalid package file: " + relative);
                    var target = SafeTarget(installRoot, relative);
                    if (relative.Equals("GameOffsets.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        using var incoming = entry.Open();
                        using var installed = File.OpenRead(target);
                        if (!SHA256.HashData(incoming).SequenceEqual(SHA256.HashData(installed)))
                            throw new InvalidDataException("GameOffsets.dll must remain unchanged.");
                        continue;
                    }
                    var staged = SafeTarget(payload, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    entry.ExtractToFile(staged);
                    using var stream = File.OpenRead(staged);
                    files.Add(new { Relative = relative, Hash = Convert.ToHexString(SHA256.HashData(stream)) });
                }
            }
            if (!names.Contains("GameHelper.dll") || !names.Contains("GameHelper.exe")) throw new InvalidDataException("GameHelper.dll / GameHelper.exe");
            File.WriteAllText(Path.Combine(stage, "plan.json"), JsonSerializer.Serialize(new
            {
                Root = Path.GetFullPath(installRoot), Stage = stage, ProcessId = processId, Files = files,
                State = new { package.Id, package.Sha256, package.Label },
            }));
            return stage;
        }

        private static async Task StartInstallerAsync(string stage)
        {
            using var source = typeof(UpdatePathHelper).Assembly.GetManifestResourceStream("GameHelper.Utils.UpdatePathHelper.cs")
                ?? throw new InvalidDataException("Update path helper is missing.");
            using var reader = new StreamReader(source);
            var script = InstallerScript.Replace("__PLAN__", Path.Combine(stage, "plan.json").Replace("'", "''"))
                .Replace("__UPDATE_PATH_HELPER__", reader.ReadToEnd());
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            using var process = Process.Start(new ProcessStartInfo(powershell)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded },
            }) ?? throw new IOException("Cannot start local installer.");
            var failure = process.StandardError.ReadToEndAsync();
            var readyPath = Path.Combine(stage, "ready");
            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (File.Exists(readyPath)) return;
                if (process.HasExited) throw new IOException("Cannot start local installer: " + await failure);
                await Task.Delay(100);
            }
            if (!process.HasExited) process.Kill();
            throw new IOException("Local installer did not become ready. GameHelper has not been closed.");
        }

        internal static void CompleteShutdown()
        {
            if (!installerStarted) return;
            foreach (var container in Plugin.PManager.Plugins.ToArray())
            {
                if (!container.Metadata.Enable) continue;
                try { container.Plugin.OnDisable(); }
                catch (Exception ex) { Console.WriteLine("[Local update shutdown] " + ex); }
            }
        }

        // The installer runs outside GameHelper, waits for its exit, and restores old files on failure.
        private const string InstallerScript = """
            $ErrorActionPreference='Stop'
            $plan=Get-Content -LiteralPath '__PLAN__' -Raw | ConvertFrom-Json
            $root=[IO.Path]::GetFullPath($plan.Root).TrimEnd([char]92)+[IO.Path]::DirectorySeparatorChar
            $backup=Join-Path $plan.Stage 'backup'
            $changed=[Collections.Generic.List[object]]::new()
            $canRestart=$false
            function Target([string]$relative) {
                $path=[IO.Path]::GetFullPath((Join-Path $root $relative))
                if(-not $path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) {throw 'Invalid target path'}
                for($parent=$path; $parent -and $parent.Length -ge $root.Length-1; $parent=[IO.Path]::GetDirectoryName($parent)) {
                    if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                        if(-not ('GameHelper.Utils.UpdatePathHelper' -as [type])) {
                            Add-Type -TypeDefinition @'
            __UPDATE_PATH_HELPER__
            '@
                        }
                        if(-not [GameHelper.Utils.UpdatePathHelper]::IsAllowedReparsePoint($parent)) {throw 'Reparse point in target'}
                    }
                }
                return $path
            }
            function CopyRetry([string]$source,[string]$destination) {
                for($attempt=0;$attempt -lt 20;$attempt++) {
                    try { [IO.File]::Copy($source,$destination,$true); return } catch { if($attempt -eq 19){throw}; Start-Sleep -Milliseconds 150 }
                }
            }
            try {
                [IO.File]::WriteAllText((Join-Path $plan.Stage 'ready'),'ready')
                $owner=$null
                try {$owner=[Diagnostics.Process]::GetProcessById([int]$plan.ProcessId)} catch [ArgumentException] {}
                if($owner -and -not $owner.WaitForExit(60000)) {throw 'GameHelper did not close in time'}
                $canRestart=$true
                foreach($file in $plan.Files) {
                    $destination=Target $file.Relative
                    $source=Join-Path (Join-Path $plan.Stage 'payload') $file.Relative
                    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.Hash) {throw 'Staged file checksum failed'}
                    if(Test-Path -LiteralPath $destination) {
                        for($attempt=0;$attempt -lt 20;$attempt++) {
                            try {
                                $handle=[IO.File]::Open($destination,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
                                $handle.Dispose()
                                break
                            } catch {if($attempt -eq 19){throw}; Start-Sleep -Milliseconds 150}
                        }
                        $saved=Join-Path $backup $file.Relative
                        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
                        CopyRetry $destination $saved
                    }
                }
                $statePath=Target 'local-update-state.json'
                $previousState=if(Test-Path -LiteralPath $statePath){[IO.File]::ReadAllText($statePath)}else{$null}
                foreach($file in $plan.Files) {
                    $destination=Target $file.Relative
                    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                    $changed.Add($file)
                    CopyRetry (Join-Path (Join-Path $plan.Stage 'payload') $file.Relative) $destination
                }
                [IO.File]::WriteAllText($statePath,($plan.State | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
                $errorPath=Target 'local-update-error.txt'
                if(Test-Path -LiteralPath $errorPath){Remove-Item -LiteralPath $errorPath}
                Start-Process -FilePath (Target 'GameHelper.exe') -WorkingDirectory $root -WindowStyle Hidden -ErrorAction Stop
            } catch {
                $failure=$_.Exception.Message
                for($index=$changed.Count-1;$index -ge 0;$index--) {
                    $file=$changed[$index]
                    try {
                        $destination=Target $file.Relative
                        $saved=Join-Path $backup $file.Relative
                        if(Test-Path -LiteralPath $saved){CopyRetry $saved $destination}
                        elseif(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination}
                    } catch {$failure+='; rollback: '+$_.Exception.Message; $canRestart=$false}
                }
                if($changed.Count -gt 0) {
                    try {
                        $statePath=Target 'local-update-state.json'
                        if($null -ne $previousState){[IO.File]::WriteAllText($statePath,$previousState)}
                        elseif(Test-Path -LiteralPath $statePath){Remove-Item -LiteralPath $statePath}
                    } catch {$failure+='; state: '+$_.Exception.Message}
                }
                try {[IO.File]::WriteAllText((Target 'local-update-error.txt'),$failure)} catch {}
                try {[IO.File]::WriteAllText((Join-Path $plan.Stage 'error.txt'),$failure)} catch {}
                if($canRestart){try {Start-Process -FilePath (Target 'GameHelper.exe') -WorkingDirectory $root -WindowStyle Hidden -ErrorAction Stop} catch {}}
            }
            """;
    }
}
