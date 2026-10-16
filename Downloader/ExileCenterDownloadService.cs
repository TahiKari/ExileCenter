namespace Downloader

{

    using System;

    using System.Collections.Generic;

    using System.IO;

    using System.Linq;

    using System.Net.Http;

    using System.Security.Cryptography;

    using System.Threading;

    using System.Threading.Tasks;

    using Newtonsoft.Json.Linq;
    using Shared.UpdateSecurity;



    internal sealed class ExileCenterDownloadService

    {

        private const string DefaultPluginsJson = """

            {

              "Atlas": { "Enable": true, "AutoStart": true },

              "AutoHotKeyTrigger": { "Enable": false, "AutoStart": false },

              "AuraTracker": { "Enable": true, "AutoStart": true },

              "HealthBars": { "Enable": true, "AutoStart": true },

              "Hiveblood": { "Enable": true, "AutoStart": true },

              "LootTracker": { "Enable": true, "AutoStart": true },

              "LootValue": { "Enable": true, "AutoStart": true },

              "PlayerBuffBar": { "Enable": true, "AutoStart": true },

              "PreloadAlert": { "Enable": false, "AutoStart": false },

              "Radar": { "Enable": true, "AutoStart": true },

              "RitualHelper": { "Enable": true, "AutoStart": true },

              "RunecraftHelper": { "Enable": true, "AutoStart": true },

              "SekhemaHelper": { "Enable": true, "AutoStart": true },

              "SimpleBars": { "Enable": false, "AutoStart": false }

            }

            """;



        private static readonly HttpClient HttpClient = CreateClient();



        private static readonly string[] RequiredRootFiles =

        [

            "ExileCenter.exe",

            "ExileCenter.dll",

            "ExileCenter.deps.json",

            "ExileCenter.runtimeconfig.json",



        ];



        private static HttpClient CreateClient()

        {

            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("ExileCenterDownloader/1.0");

            return client;

        }



        internal async Task<DownloadResult> DownloadAsync(

            string targetDir,

            bool force,

            IProgress<string>? log,

            CancellationToken cancellationToken)

        {

            targetDir = Path.GetFullPath(targetDir);

            if (Directory.Exists(targetDir) &&

                Directory.EnumerateFileSystemEntries(targetDir).Any() &&

                !force)

            {

                return DownloadResult.Fail(

                    2,

                    DownloaderLocalization.B(

                        "The target folder is not empty. Choose an empty folder or use --force.",

                        "Der Zielordner ist nicht leer. Leeren Ordner waehlen oder --force verwenden."));

            }



            Directory.CreateDirectory(targetDir);



            log?.Report(DownloaderLocalization.B(

                "Loading manifest.json ...",

                "Lade manifest.json ..."));

            JObject manifest;

            try

            {

                manifest = await this.FetchManifestAsync(cancellationToken).ConfigureAwait(false);

            }

            catch (Exception ex)

            {

                return DownloadResult.Fail(

                    1,

                    $"{DownloaderLocalization.B("Manifest not reachable", "Manifest nicht erreichbar")}: {ex.Message}. " +

                    DownloaderLocalization.B(

                        "Is a release published on GitHub?",

                        "Ist ein Release auf GitHub veroeffentlicht?"));

            }



            var version = manifest["version"]?.ToString();

            if (string.IsNullOrWhiteSpace(version))

            {

                return DownloadResult.Fail(

                    1,

                    DownloaderLocalization.B(

                        "manifest.json is invalid or empty. Is a release published on GitHub?",

                        "manifest.json ist ungueltig oder leer. Ist ein Release auf GitHub veroeffentlicht?"));

            }



            log?.Report($"{DownloaderLocalization.B("Version", "Version")}: {version}");

            var published = manifest["published"]?.ToString();

            if (!string.IsNullOrWhiteSpace(published))

            {

                log?.Report($"{DownloaderLocalization.B("Published", "Veroeffentlicht")}: {published}");

            }



            if (UpdateZipPackage.TryRead(manifest, out var zipPackage))

            {

                log?.Report($"{DownloaderLocalization.B("Package", "Paket")}: {zipPackage.Name}");

                var zipPath = Path.Combine(targetDir, zipPackage.Name);

                try

                {

                    var url = DownloadConfig.FileDownloadUrl(version!, zipPackage.Name);

                    log?.Report(DownloaderLocalization.B(

                        "Downloading full package ...",

                        "Lade Vollstaendiges Paket ..."));

                    await this.DownloadFileAsync(url, zipPath, cancellationToken).ConfigureAwait(false);

                    var actualHash = ComputeSha256(zipPath);

                    if (!actualHash.Equals(zipPackage.Hash, StringComparison.OrdinalIgnoreCase))

                    {

                        throw new InvalidOperationException(DownloaderLocalization.B(

                            $"Hash mismatch (got {actualHash}, expected {zipPackage.Hash})",

                            $"Hash stimmt nicht (ist {actualHash}, erwartet {zipPackage.Hash})"));

                    }



                    log?.Report(DownloaderLocalization.B(

                        "Extracting package ...",

                        "Entpacke Paket ..."));

                    UpdateZipPackage.ExtractToDirectory(zipPath, targetDir);

                    try { File.Delete(zipPath); } catch { }

                }

                catch (Exception ex)

                {

                    return DownloadResult.Fail(

                        1,

                        $"{DownloaderLocalization.B("Package download failed", "Paket-Download fehlgeschlagen")}: {ex.Message}");

                }

            }

            else

            {

            var files = manifest["files"] as JArray;

            if (files == null || files.Count == 0)

            {

                return DownloadResult.Fail(

                    1,

                    DownloaderLocalization.B(

                        "manifest.json is invalid or empty. Is a release published on GitHub?",

                        "manifest.json ist ungueltig oder leer. Ist ein Release auf GitHub veroeffentlicht?"));

            }



            log?.Report($"{DownloaderLocalization.B("Files", "Dateien")}: {files.Count}");



            var failed = new List<string>();

            var index = 0;

            foreach (var entry in files)

            {

                cancellationToken.ThrowIfCancellationRequested();

                index++;



                var relativePath = entry["path"]?.ToString();

                var expectedHash = entry["hash"]?.ToString();

                if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(expectedHash))

                {

                    continue;

                }



                var packageName = entry["package"]?.ToString()

                    ?? relativePath.Replace('\\', '/').Replace('/', '.');

                if (!UpdatePathSecurity.TryResolvePath(targetDir, relativePath, out var destPath))
                {
                    failed.Add(relativePath);
                    log?.Report($"  [{index}/{files.Count}] {DownloaderLocalization.B("ERROR", "FEHLER")}: {relativePath} - unsafe path");
                    continue;
                }

                if (File.Exists(destPath))

                {

                    var localHash = ComputeSha256(destPath);

                    if (localHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))

                    {

                        log?.Report($"  [{index}/{files.Count}] {DownloaderLocalization.B("skipped", "uebersprungen")}: {relativePath}");

                        continue;

                    }

                }



                var url = DownloadConfig.FileDownloadUrl(version!, packageName);

                try

                {

                    await this.DownloadFileAsync(url, destPath, cancellationToken).ConfigureAwait(false);

                    var actualHash = ComputeSha256(destPath);

                    if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))

                    {

                        throw new InvalidOperationException(DownloaderLocalization.B(

                            $"Hash mismatch (got {actualHash}, expected {expectedHash})",

                            $"Hash stimmt nicht (ist {actualHash}, erwartet {expectedHash})"));

                    }



                    log?.Report($"  [{index}/{files.Count}] OK: {relativePath}");

                }

                catch (Exception ex)

                {

                    failed.Add(relativePath);

                    log?.Report($"  [{index}/{files.Count}] {DownloaderLocalization.B("ERROR", "FEHLER")}: {relativePath} - {ex.Message}");

                }

            }



            if (failed.Count > 0)

            {

                return DownloadResult.Fail(

                    1,

                    DownloaderLocalization.B(

                        $"Download finished with errors ({failed.Count} file(s)).",

                        $"Download mit Fehlern beendet ({failed.Count} Datei(en))."));

            }

            }



            await File.WriteAllTextAsync(

                Path.Combine(targetDir, "VERSION.txt"),

                $"ExileCenter {version}\nDownloaded: {DateTime.UtcNow:o}",

                cancellationToken).ConfigureAwait(false);



            var pluginsJson = Path.Combine(targetDir, "configs", "plugins.json");

            if (!File.Exists(pluginsJson))

            {

                Directory.CreateDirectory(Path.GetDirectoryName(pluginsJson)!);

                await File.WriteAllTextAsync(pluginsJson, DefaultPluginsJson, cancellationToken).ConfigureAwait(false);

                log?.Report(DownloaderLocalization.B(

                    "Default plugins.json created.",

                    "Standard plugins.json erstellt."));

            }



            UpdateFileHashesCatalog.SaveFromManifest(targetDir, manifest);

            UpdateStateHelper.Save(targetDir, published ?? string.Empty, version!,

                UpdateZipPackage.TryRead(manifest, out var pkg) ? pkg.Hash : null);



            var missingRequired = this.FindMissingRequiredFiles(targetDir);

            if (missingRequired != null)

            {

                return DownloadResult.Fail(

                    1,

                    DownloaderLocalization.B(

                        $"Installation incomplete. Missing: {missingRequired}. Delete the folder and download again into an empty folder.",

                        $"Installation unvollstaendig. Fehlend: {missingRequired}. Ordner loeschen und erneut in einen LEEREN Ordner laden."));

            }



            return DownloadResult.Ok(targetDir, version!);

        }



        private async Task<JObject> FetchManifestAsync(CancellationToken cancellationToken)
        {
            string repository = UpdateRepositoryConfig.Repository;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var release = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            string tag = release["tag_name"]?.ToString() ?? "";
            var assets = release["assets"]?.OfType<JObject>().Where(a => a["name"]?.ToString() == "ExileCenter-Complete-win-x64.zip" && a["state"]?.ToString() == "uploaded").ToArray();
            if (assets?.Length != 1 || release["draft"]?.Value<bool>() == true || release["prerelease"]?.Value<bool>() == true ||
                !System.Text.RegularExpressions.Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$"))
                throw new InvalidDataException("No complete ExileCenter release available.");
            string expectedUrl = $"https://github.com/{repository}/releases/download/{Uri.EscapeDataString(tag)}/ExileCenter-Complete-win-x64.zip";
            string digest = assets[0]["digest"]?.ToString() ?? "";
            if (assets[0]["browser_download_url"]?.ToString() != expectedUrl ||
                !System.Text.RegularExpressions.Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("Invalid release URL or SHA256.");
            return new JObject { ["version"] = tag, ["published"] = release["published_at"],
                ["package"] = new JObject { ["name"] = "ExileCenter-Complete-win-x64.zip", ["hash"] = digest[7..], ["size"] = assets[0]["size"] } };
        }
        private async Task DownloadFileAsync(

            string url,

            string destinationPath,

            CancellationToken cancellationToken)

        {

            var dir = Path.GetDirectoryName(destinationPath);

            if (!string.IsNullOrEmpty(dir))

            {

                Directory.CreateDirectory(dir);

            }



            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            using var response = await HttpClient.SendAsync(

                request,

                HttpCompletionOption.ResponseHeadersRead,

                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await using var file = File.Create(destinationPath);

            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

        }



        private string? FindMissingRequiredFiles(string targetDir)

        {

            var missing = RequiredRootFiles

                .Where(file => !File.Exists(Path.Combine(targetDir, file)))

                .ToList();

            return missing.Count == 0 ? null : string.Join(", ", missing);

        }



        private static string ComputeSha256(string filePath)

        {

            using var sha = SHA256.Create();

            using var stream = File.OpenRead(filePath);

            return Convert.ToHexString(sha.ComputeHash(stream));

        }

    }



    internal sealed class DownloadResult

    {

        internal int ExitCode { get; init; }

        internal string Message { get; init; } = string.Empty;

        internal string? TargetDir { get; init; }

        internal string? Version { get; init; }



        internal static DownloadResult Ok(string targetDir, string version) =>

            new() { ExitCode = 0, TargetDir = targetDir, Version = version, Message = "OK" };



        internal static DownloadResult Fail(int exitCode, string message) =>

            new() { ExitCode = exitCode, Message = message };

    }

}


