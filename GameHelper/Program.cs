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
        internal static readonly string UpdateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Gamehelpers", "Maj GameHelper");
        private static readonly string InstallDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        private const string StateName = "local-update-state.json";
        private const string ErrorName = "local-update-error.txt";
        private const string Repository = "TahiKari/Gamehelper2";
        private const string GitHubPackageName = "GameHelper-Personnalise-win-x64.zip";
        private static readonly HttpClient GitHubClient = new() { Timeout = TimeSpan.FromSeconds(8) };
        private static readonly string? CurrentReleaseTag = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "GameHelperReleaseTag")?.Value;
        private sealed record Package(string Id, string PackageFile, string Sha256, string Label)
        {
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
        internal static bool IsLocalInstallation => Directory.Exists(UpdateDirectory) || File.Exists(Path.Combine(InstallDirectory, StateName));
        internal static bool IsAvailable => offer != null;

        internal static async Task<bool> InstallBeforeStartupAsync(Func<string, bool>? confirm = null)
        {
            var result = await CheckAvailableAsync();
            offer = result.Offer;
            error = result.Error;
            // A failed installer restarts the old version. Leave retries to the update page.
            if (offer == null || error != null) return false;
            lastNotified = offer.Id;
            var question = L.F("updates.startup_question", "{0}\n\nInstall this update before starting GameHelper?\nChoose No to continue with the current version. Your settings are preserved.", offer.Label);
            if (!(confirm?.Invoke(question) ?? (StartupMessageBox(IntPtr.Zero, question, "GameHelper", 0x124) == 6))) return false;
            try
            {
                var stage = await PreparePackageAsync(offer, InstallDirectory, UpdateDirectory, Environment.ProcessId);
                StartInstaller(stage);
                installerStarted = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Console.WriteLine("[Local update startup] " + ex);
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StartupMessageBox(IntPtr owner, string text, string caption, uint flags);

        private static Version? ReleaseVersion(string? tag)
        {
            if (tag == null) return null;
            var match = System.Text.RegularExpressions.Regex.Match(tag, @"^v(\d+\.\d+\.\d+)(?:-personnalise\.(\d+))?$");
            return match.Success && Version.TryParse(match.Groups[1].Value + "." +
                (match.Groups[2].Success ? match.Groups[2].Value : "0"), out var version) ? version : null;
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
                { DownloadUrl = url, DownloadSize = size };
        }

        private static async Task<CheckResult> CheckAvailableAsync()
        {
            var local = await Task.Run(() => Check(UpdateDirectory, InstallDirectory));
            if (local.Offer != null || local.Error != null) return local;
            if (DateTime.UtcNow >= nextGitHubCheck)
            {
                nextGitHubCheck = DateTime.UtcNow.AddMinutes(10);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
                    request.Headers.UserAgent.ParseAdd("GameHelper-Personal/1.0");
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
            return githubResult;
        }

        private static async Task<string> PreparePackageAsync(Package package, string installRoot, string updateRoot, int processId)
        {
            if (package.DownloadUrl == null) return await Task.Run(() => Prepare(package, installRoot, updateRoot, processId));
            var downloadRoot = Path.Combine(Path.GetTempPath(), "GameHelper-Download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadRoot);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                using (var response = await GitHubClient.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
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
                var manifestPath = Path.Combine(updateRoot, "GameHelper-Maj.json");
                if (!File.Exists(manifestPath)) return new(null, installationError);
                var candidate = JsonSerializer.Deserialize<Package>(File.ReadAllText(manifestPath));
                if (candidate == null || candidate.DownloadUrl != null || string.IsNullOrWhiteSpace(candidate.Id) || string.IsNullOrWhiteSpace(candidate.Label) ||
                    candidate.Sha256 == null || candidate.Sha256.Length != 64 || !candidate.Sha256.All(Uri.IsHexDigit) ||
                    string.IsNullOrWhiteSpace(candidate.PackageFile) || candidate.PackageFile != Path.GetFileName(candidate.PackageFile) ||
                    !candidate.PackageFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("GameHelper-Maj.json");
                var statePath = Path.Combine(installRoot, StateName);
                if (File.Exists(statePath))
                {
                    using var state = JsonDocument.Parse(File.ReadAllText(statePath));
                    if (state.RootElement.GetProperty("Id").GetString() == candidate.Id &&
                        string.Equals(state.RootElement.GetProperty("Sha256").GetString(), candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                        return new(null, installationError);
                }
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
                offer = result.Offer;
                error = result.Error;
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
                    var stage = prepareTask.GetAwaiter().GetResult();
                    prepareTask = null;
                    Coroutine.CoroutineHandler.RaiseEvent(CoroutineEvents.GameHelperEvents.TimeToSaveAllSettings);
                    StartInstaller(stage);
                    installerStarted = true;
                    Core.GHSettings.IsOverlayRunning = false;
                }
                catch (Exception ex) { prepareTask = null; error = ex.Message; }
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
                ImGui.TextColored(Ui.ImGuiTheme.Accent, L.T("updates.available", "Local update available"));
                ImGui.Text(L.F("updates.toast", "Open GameHelper with {0} to install it.", Core.GHSettings.MainMenuHotKey));
            }
            ImGui.End();
        }

        internal static void DrawSettings()
        {
            Ui.ImGuiTheme.SectionHeader(L.T("updates.title", "Updates"), L.T("updates.description", "Updates from GitHub or your local folder. Your settings are preserved."));
            ImGui.TextWrapped("https://github.com/" + Repository + "/releases");
            if (Directory.Exists(UpdateDirectory)) ImGui.TextWrapped(UpdateDirectory);
            ImGui.TextDisabled(L.T("updates.poll", "Checked at startup; GitHub every 10 minutes, local updates every 5 seconds."));
            ImGui.Separator();
            if (prepareTask != null) ImGui.TextWrapped(L.T("updates.preparing", "Downloading, checking and preparing the update…"));
            else if (offer != null)
            {
                ImGui.TextColored(Ui.ImGuiTheme.Accent, L.T("updates.available", "Local update available"));
                ImGui.TextWrapped(offer.Label);
                if (ImGui.Button(L.T("updates.apply", "Update and restart")))
                {
                    error = null;
                    var selected = offer;
                    prepareTask = PreparePackageAsync(selected, InstallDirectory, UpdateDirectory, Environment.ProcessId);
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

        private static bool IsPackageFile(string relative)
        {
            var segments = relative.Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith('.') || segment.EndsWith(' '))) return false;
            if (segments.Any(segment => segment.Equals("config", StringComparison.OrdinalIgnoreCase) || segment.Equals("configs", StringComparison.OrdinalIgnoreCase)) ||
                Path.GetFileName(relative).Equals("settings.json", StringComparison.OrdinalIgnoreCase)) return false;
            if (segments.Length == 1)
                return relative.Equals("GameHelper.exe", StringComparison.OrdinalIgnoreCase) || relative.Equals("GameHelper.App.exe", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    relative is "GameHelper.deps.json" or "GameHelper.runtimeconfig.json" or "LICENSE" or "CREDITS.md" or "README.md";
            return segments[0] is "Plugins" or "Localization" or "fonts" && !relative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeTarget(string root, string relative)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(relative);
            for (var current = target; current != null && current.Length >= fullRoot.Length - 1; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
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

        private static void StartInstaller(string stage)
        {
            var script = InstallerScript.Replace("__PLAN__", Path.Combine(stage, "plan.json").Replace("'", "''"));
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            using var process = Process.Start(new ProcessStartInfo(powershell)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded },
            }) ?? throw new IOException("Cannot start local installer.");
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
                    if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'Reparse point in target'}
                }
                return $path
            }
            function CopyRetry([string]$source,[string]$destination) {
                for($attempt=0;$attempt -lt 20;$attempt++) {
                    try { [IO.File]::Copy($source,$destination,$true); return } catch { if($attempt -eq 19){throw}; Start-Sleep -Milliseconds 150 }
                }
            }
            try {
                $owner=$null
                try {$owner=[Diagnostics.Process]::GetProcessById([int]$plan.ProcessId)} catch [ArgumentException] {}
                if($owner -and -not $owner.WaitForExit(60000)) {throw 'GameHelper did not close in time'}
                $canRestart=$true
                foreach($file in $plan.Files) {
                    $destination=Target $file.Relative
                    $source=Join-Path (Join-Path $plan.Stage 'payload') $file.Relative
                    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $file.Hash) {throw 'Staged file checksum failed'}
                    if(Test-Path -LiteralPath $destination) {
                        $handle=[IO.File]::Open($destination,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
                        $handle.Dispose()
                        $saved=Join-Path $backup $file.Relative
                        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
                        [IO.File]::Copy($destination,$saved,$true)
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
                if($canRestart){try {Start-Process -FilePath (Target 'GameHelper.exe') -WorkingDirectory $root -WindowStyle Hidden -ErrorAction Stop} catch {}}
            }
            """;
    }
}
