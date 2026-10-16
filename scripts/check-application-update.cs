using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImGuiNET;

internal static partial class UpdateChecks
{
    static Type updates = null!;
    static Type packageType = null!;
    static readonly BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    static async Task<object?> CallAsync(string method, params object[] args)
    {
        var task = (Task)updates.GetMethod(method, PrivateStatic)!.Invoke(null, args)!;
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
    static object Package(byte[] bytes, string? url = null, string? hash = null)
    {
        var package = Activator.CreateInstance(packageType, new object[] { "fixture:" + Guid.NewGuid(), "update.zip", hash ?? Convert.ToHexString(SHA256.HashData(bytes)), "fixture" })!;
        packageType.GetProperty("DownloadUrl")!.SetValue(package, url);
        packageType.GetProperty("DownloadSize")!.SetValue(package, url == null ? 0L : (long)bytes.Length);
        return package;
    }
    static async Task ExpectFailure(Func<Task> action, string name)
    {
        try { await action(); } catch (Exception) { Check(true, name); return; }
        throw new Exception("Expected failure: " + name);
    }
    static byte[] Zip(Dictionary<string, byte[]> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var pair in files) { using var output = archive.CreateEntry(pair.Key).Open(); output.Write(pair.Value); }
        return stream.ToArray();
    }
    static (string Url, Task Serving) Serve(byte[] bytes, int headerDelay = 0, bool truncated = false)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                await Task.Delay(headerDelay);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"));
                await stream.WriteAsync(bytes.AsMemory(0, bytes.Length - (truncated ? 1 : 0)));
            }
            finally { listener.Stop(); }
        });
        return ($"http://127.0.0.1:{port}/update.zip", serving);
    }
    static async Task WaitFor(string path)
    {
        for (var attempt = 0; attempt < 100 && !File.Exists(path); attempt++) await Task.Delay(100);
        Check(File.Exists(path), "installer produced " + Path.GetFileName(path));
    }
    static async Task<int> Main(string[] args)
    {
        if (Path.GetFileName(Environment.ProcessPath)!.Equals("ExileCenter.exe", StringComparison.OrdinalIgnoreCase))
        { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "restart-ok.txt"), "harmless fixture restarted"); return 0; }
        if (args.Contains("--wait-owner")) { await Task.Delay(2500); return 0; }
        if (args.Contains("--onedrive")) { await RunOneDriveChecks(); return 0; }
        if (args.Length == 2 && args[0] == "--transition-package") { TransitionPackageCheck(args[1]); return 0; }
        if (args.Contains("--version-only")) { var tag = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ExileCenter.dll")).GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "ExileCenterReleaseTag").Value; Check(tag == "v1.5.11-personnalise.38", "CI binary embeds the published release tag"); return 0; }
        if (args.Length == 4 && args[0] == "--brand-migration") { await BrandMigration(args[1], args[2], args[3]); return 0; }
        if (args.Contains("--public-package")) await PublicPackageCheck(); else await RunChecks();
        return 0;
    }
    static async Task PublicPackageCheck()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ExileCenter.dll"));
        updates = assembly.GetType("ExileCenter.LocalUpdates", true)!;
        packageType = updates.GetNestedType("Package", BindingFlags.NonPublic)!;
        var root = Path.Combine(AppContext.BaseDirectory, "public-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameOffsets.dll"), Path.Combine(root, "GameOffsets.dll"));
        var package = Activator.CreateInstance(packageType, new object[] { "github:v1.5.11-personnalise.22", "update.zip", "56bb91542644ae4311fcbd23ddce6dad64c3ae57c984a8771674ea88ceb2e30b", "P-V1.17" })!;
        packageType.GetProperty("DownloadUrl")!.SetValue(package, "https://github.com/TahiKari/Gamehelper2/releases/download/v1.5.11-personnalise.22/GameHelper-Personnalise-win-x64.zip");
        packageType.GetProperty("DownloadSize")!.SetValue(package, 22789735L);
        var stage = (string)(await CallAsync("PreparePackageAsync", package, root, root, int.MaxValue))!;
        Check((long)updates.GetField("downloadBytes", PrivateStatic)!.GetValue(null)! == 22789735L, "real public GitHub package downloaded and SHA256 verified");
        Check(Directory.GetFiles(Path.Combine(stage, "payload"), "*", SearchOption.AllDirectories).Length > 300, "complete public package prepared without replacing or starting GameHelper");
    }
    static void TransitionPackageCheck(string path)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ExileCenter.dll"));
        updates = assembly.GetType("ExileCenter.LocalUpdates", true)!;
        packageType = updates.GetNestedType("Package", BindingFlags.NonPublic)!;
        var repository = (string)updates.GetField("Repository", PrivateStatic)!.GetRawConstantValue()!;
        Check(repository == "TahiKari/Gamehelper2", "transition is tested with the actual old P-V1.28 updater");
        var root = Path.Combine(AppContext.BaseDirectory, "migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameOffsets.dll"), Path.Combine(root, "GameOffsets.dll"));
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new {
            draft = false, prerelease = false, tag_name = "v1.5.11-personnalise.35", name = "ExileCenter P-V1.30", body = "Migration ExileCenter",
            assets = new[] { new { name = "GameHelper-Personnalise-win-x64.zip", state = "uploaded", size = new FileInfo(path).Length,
                digest = "sha256:" + hash.ToLowerInvariant(), browser_download_url = "https://github.com/TahiKari/Gamehelper2/releases/download/v1.5.11-personnalise.35/GameHelper-Personnalise-win-x64.zip" } }
        }));
        var offer = updates.GetMethod("ParseGitHubRelease", PrivateStatic)!.Invoke(null, new object[] { json.RootElement, "v1.5.11-personnalise.33" });
        Check(offer != null, "old installed release accepts the ExileCenter transition asset");
        var package = Activator.CreateInstance(packageType, new object[] { "fixture:migration", Path.GetFileName(path), hash, "ExileCenter P-V1.30" })!;
        var stage = (string)updates.GetMethod("Prepare", PrivateStatic)!.Invoke(null, new object[] { package, root, Path.GetDirectoryName(path)!, int.MaxValue })!;
        Check(File.Exists(Path.Combine(stage, "payload", "ExileCenter.dll")), "old updater stages the complete new core without starting it");
        Check(Directory.GetFiles(Path.Combine(stage, "payload"), "*", SearchOption.AllDirectories).Length > 350, "old updater accepts all plugin and translation paths in the transition package");
        Check(!File.Exists(Path.Combine(stage, "payload", "GameOffsets.dll")), "transition preserves the old validated offsets");
        Check(!File.Exists(Path.Combine(stage, "payload", "ExileCenter.exe")), "transition obeys the old executable whitelist");
        Check(File.ReadAllText(Path.Combine(stage, "payload", "README.md")).StartsWith("# ExileCenter"), "transition delivers the rewritten ExileCenter guide");
        Check(!File.Exists(Path.Combine(root, "ExileCenter.exe")), "verification never installs or starts the application");
    }
    static async Task RunChecks()
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ExileCenter.dll"));
        updates = assembly.GetType("ExileCenter.LocalUpdates", true)!;
        packageType = updates.GetNestedType("Package", BindingFlags.NonPublic)!;
        var localRoot = Path.Combine(Path.GetTempPath(), "ExileCenterManifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(localRoot);
        File.WriteAllText(Path.Combine(localRoot, "fixture.zip"), "isolated manifest fixture");
        string Manifest(string id) => JsonSerializer.Serialize(new { Id = id, PackageFile = "fixture.zip", Sha256 = new string('a', 64), Label = "ExileCenter fixture" });
        var newManifest = Path.Combine(localRoot, "ExileCenter-Maj.json");
        var oldManifest = Path.Combine(localRoot, "GameHelper-Maj.json");
        var localCheck = updates.GetMethod("Check", PrivateStatic)!;
        string? OfferedId()
        {
            var result = localCheck.Invoke(null, new object[] { localRoot, localRoot })!;
            var offer = result.GetType().GetProperty("Offer")!.GetValue(result);
            return offer == null ? null : (string)packageType.GetProperty("Id")!.GetValue(offer)!;
        }
        File.WriteAllText(newManifest, Manifest("new")); File.WriteAllText(oldManifest, "broken old manifest");
        Check(OfferedId() == "new", "ExileCenter manifest takes priority over the legacy manifest");
        File.Delete(newManifest); File.WriteAllText(oldManifest, Manifest("legacy"));
        Check(OfferedId() == "legacy", "previous local manifests remain readable for migration");
        foreach (string tag in new[] { "v1.5.11-personnalise.35", "v1.5.11-personnalise.36" })
        {
            File.WriteAllText(newManifest, JsonSerializer.Serialize(new { Id = "same-or-older", PackageFile = "fixture.zip", Sha256 = new string('a', 64), Label = "ExileCenter fixture", ReleaseTag = tag }));
            Check(OfferedId() == null, "local manifest cannot reinstall or downgrade installed version: " + tag);
        }
        var updateDirectory = (string)updates.GetField("UpdateDirectory", PrivateStatic)!.GetValue(null)!;
        Check(updateDirectory.EndsWith(Path.Combine("ExileCenter", "Maj ExileCenter"), StringComparison.OrdinalIgnoreCase), "local updates resolve the renamed ExileCenter folder");
        var parse = updates.GetMethod("ParseGitHubRelease", PrivateStatic)!;
        var repository = (string)updates.GetField("Repository", PrivateStatic)!.GetRawConstantValue()!;
        Check(repository == "TahiKari/ExileCenter", "updates use the independent ExileCenter repository");
        object? Parse(string repo, string installed)
        {
            var json = JsonSerializer.Serialize(new {
                draft = false, prerelease = false, tag_name = "v1.5.11-personnalise.35", name = "ExileCenter P-V1.30", body = "Migration ExileCenter",
                assets = new[] { new { name = "ExileCenter-Complete-win-x64.zip", state = "uploaded", size = 1024,
                    digest = "sha256:" + new string('a', 64), browser_download_url = "https://github.com/" + repo + "/releases/download/v1.5.11-personnalise.35/ExileCenter-Complete-win-x64.zip" } }
            });
            using var document = JsonDocument.Parse(json);
            return parse.Invoke(null, new object[] { document.RootElement, installed });
        }
        var migration = Parse(repository, "v1.5.11-personnalise.33");
        Check(migration != null, "GameHelper P-V1.28 can advance to ExileCenter P-V1.30");
        Check(Parse(repository, "v1.5.11-personnalise.35") == null, "same release is not reinstalled");
        Check(Parse(repository, "v1.5.11-personnalise.36") == null, "newer installation is not downgraded");
        try { Parse("MordWraith/Gamehelper", "v1.5.11-personnalise.33"); throw new Exception("foreign download accepted"); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { Check(true, "foreign repository asset rejected"); }
        var title = (string)updates.GetMethod("UpdateTitle", PrivateStatic)!.Invoke(null, new[] { migration })!;
        Check(title.StartsWith("ExileCenter P-V1.30"), "migration prompt uses ExileCenter and the embedded version");
        Check(((HttpClient)updates.GetField("PackageClient", PrivateStatic)!.GetValue(null)!).Timeout == TimeSpan.FromMinutes(3), "download timeout independent of the short API timeout");
        var root = Path.Combine(AppContext.BaseDirectory, "isolated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "GameOffsets.dll"), Path.Combine(root, "GameOffsets.dll"));
        foreach (var file in new[] { "UpdateChecks.dll", "UpdateChecks.deps.json", "UpdateChecks.runtimeconfig.json", "ImGui.NET.dll" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, file), Path.Combine(root, file));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "UpdateChecks.exe"), Path.Combine(root, "ExileCenter.exe"));
        Directory.CreateDirectory(Path.Combine(root, "configs"));
        File.WriteAllText(Path.Combine(root, "configs", "settings.json"), "personal settings preserved");
        var offsets = File.ReadAllBytes(Path.Combine(root, "GameOffsets.dll"));
        var files = new Dictionary<string, byte[]> {
            ["ExileCenter.exe"] = File.ReadAllBytes(Path.Combine(root, "ExileCenter.exe")),
            ["ExileCenter.dll"] = Encoding.UTF8.GetBytes("new fixture version"),
            ["ExileCenter.exe"] = File.ReadAllBytes(Path.Combine(root, "ExileCenter.exe")),
            ["SECURITY.md"] = Encoding.UTF8.GetBytes("ExileCenter security documentation"),
            ["GameOffsets.dll"] = offsets
        };
        var zip = Zip(files);
        var server = Serve(zip, headerDelay: 9000);
        var stage = (string)(await CallAsync("PreparePackageAsync", Package(zip, server.Url), root, root, int.MaxValue))!;
        await server.Serving;
        Check((long)updates.GetField("downloadBytes", PrivateStatic)!.GetValue(null)! == zip.Length, "slow HTTP headers beyond 8 seconds download fully and report progress");
        Check(!File.Exists(Path.Combine(stage, "payload", "GameOffsets.dll")), "validated offsets excluded from installation payload");
        await CallAsync("StartInstallerAsync", stage);
        Check(File.Exists(Path.Combine(stage, "ready")), "installer acknowledges startup");
        await WaitFor(Path.Combine(root, "restart-ok.txt"));
        Check(File.ReadAllText(Path.Combine(root, "ExileCenter.dll")) == "new fixture version", "installer applies staged files");
        Check(File.ReadAllText(Path.Combine(root, "configs", "settings.json")) == "personal settings preserved", "installer preserves user settings");
        Check(File.ReadAllBytes(Path.Combine(root, "GameOffsets.dll")).SequenceEqual(offsets), "installer leaves offsets byte-identical");
        Check(File.Exists(Path.Combine(root, "ExileCenter.exe")) && File.Exists(Path.Combine(root, "SECURITY.md")), "branded executable and documentation migrate through the installer");
        Check(File.Exists(Path.Combine(root, "local-update-state.json")), "successful installation records its state");
        var broken = Serve(zip, truncated: true);
        await ExpectFailure(async () => await CallAsync("PreparePackageAsync", Package(zip, broken.Url), root, root, int.MaxValue), "truncated download rejected");
        await broken.Serving;
        File.WriteAllBytes(Path.Combine(root, "update.zip"), zip);
        await ExpectFailure(async () => await CallAsync("PreparePackageAsync", Package(zip, hash: new string('0', 64)), root, root, int.MaxValue), "SHA256 mismatch rejected");
        files["../outside.dll"] = Encoding.UTF8.GetBytes("must not escape");
        var unsafeZip = Zip(files);
        File.WriteAllBytes(Path.Combine(root, "update.zip"), unsafeZip);
        await ExpectFailure(async () => await CallAsync("PreparePackageAsync", Package(unsafeZip), root, root, int.MaxValue), "unsafe ZIP path rejected");
        Check(!File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "outside.dll")), "no file escapes the isolated destination");
        var badStage = Path.Combine(root, "bad-installer");
        Directory.CreateDirectory(badStage);
        await ExpectFailure(async () => await CallAsync("StartInstallerAsync", badStage), "installer bootstrap failure reported instead of silent shutdown");
        var windowType = updates.GetNestedType("StartupUpdateWindow", BindingFlags.NonPublic)!;
        var window = RuntimeHelpers.GetUninitializedObject(windowType);
        var pending = new TaskCompletionSource();
        windowType.GetField("installTask", PrivateInstance)!.SetValue(window, pending.Task);
        windowType.GetMethod("Close", BindingFlags.Instance | BindingFlags.Public)!.Invoke(window, null);
        Check(!(bool)windowType.GetField("closeRequested", PrivateInstance)!.GetValue(window)!, "startup window stays open during installation");
        pending.SetException(new IOException("fixture download error"));
        windowType.GetMethod("Render", PrivateInstance)!.Invoke(window, null);
        Check(!(bool)windowType.GetField("closeRequested", PrivateInstance)!.GetValue(window)!, "failed installation keeps the startup window open");
        Check((string)windowType.GetField("installError", PrivateInstance)!.GetValue(window)! == "fixture download error", "startup error remains available for display and retry");
        Check(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "local-update-error.txt")).Contains("fixture download error"), "preparation error persisted in a diagnostic file");
        windowType.GetField("question", PrivateInstance)!.SetValue(window, "fixture update");
        windowType.GetField("title", PrivateInstance)!.SetValue(window, "fixture title");
        var context = ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO(); unsafe { io.NativePtr->IniFilename = null; }
            io.DisplaySize = new Vector2(760, 480); io.DeltaTime = 1f / 60f;
            io.Fonts.AddFontDefault(); io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytes);
            ImGui.NewFrame(); windowType.GetMethod("Draw", PrivateInstance)!.Invoke(window, null); ImGui.Render();
            Check(ImGui.GetDrawData().TotalVtxCount > 0, "startup error UI renders in an isolated ImGui context");
            windowType.GetField("installTask", PrivateInstance)!.SetValue(window, new TaskCompletionSource().Task);
            ImGui.NewFrame(); windowType.GetMethod("Draw", PrivateInstance)!.Invoke(window, null); ImGui.Render();
            Check(ImGui.GetDrawData().TotalVtxCount > 0, "download progress UI renders without closing the window");
        }
        finally { ImGui.DestroyContext(context); }
        Console.WriteLine($"{passed} checks passed; only isolated fixtures were modified or restarted.");
    }
    static async Task BrandMigration(string fullZip, string transitionZip, string oldRuntime)
    {
        string work = Path.Combine(AppContext.BaseDirectory, "brand-migration-" + Guid.NewGuid().ToString("N"));
        string native = Path.Combine(work, "native"), root = Path.Combine(work, "installed");
        Directory.CreateDirectory(root);
        ZipFile.ExtractToDirectory(fullZip, native);
        using (var archive = ZipFile.OpenRead(fullZip))
            Check(!archive.Entries.Any(e => e.FullName.Contains("GameHelper", StringComparison.OrdinalIgnoreCase)), "complete package has no former brand in any filename");
        string[] settings = { "configs/core_settings.json", "configs/plugins.json", "Plugins/StashUtility/config/settings.txt", "Plugins/KikiMeter/config/settings.txt" };
        foreach (string file in settings) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, file))!); File.WriteAllText(Path.Combine(root, file), "personal unchanged: " + file); }
        foreach (string file in new[] { "GameHelper.exe", "GameHelper.dll", "GameOffsets.dll" }) File.Copy(Path.Combine(oldRuntime, file), Path.Combine(root, file));
        File.Copy(Path.Combine(oldRuntime, "GameHelper.exe"), Path.Combine(root, "ExileCenter.exe"));
        File.WriteAllText(Path.Combine(root, "GameHelper.App.dll"), "obsolete launcher");
        File.WriteAllText(Path.Combine(root, "GameHelper-personal-notes.txt"), "must stay");
        byte[] offsets = File.ReadAllBytes(Path.Combine(root, "GameOffsets.dll"));
        File.Copy(transitionZip, Path.Combine(work, "transition.zip"));
        var oldContext = new System.Runtime.Loader.AssemblyLoadContext("old-updater-" + Guid.NewGuid(), true);
        oldContext.Resolving += (_, name) => { string file = Path.Combine(oldRuntime, name.Name + ".dll"); return File.Exists(file) ? oldContext.LoadFromAssemblyPath(file) : null; };
        var oldAssembly = oldContext.LoadFromAssemblyPath(Path.Combine(oldRuntime, "GameHelper.dll"));
        var oldUpdates = oldAssembly.GetType("GameHelper.LocalUpdates", true)!;
        var oldPackage = oldUpdates.GetNestedType("Package", BindingFlags.NonPublic)!;
        object package = Activator.CreateInstance(oldPackage, new object[] { "migration-fixture", "transition.zip", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(transitionZip))), "ExileCenter P-V1.33" })!;
        string stage = (string)oldUpdates.GetMethod("Prepare", PrivateStatic)!.Invoke(null, new object[] { package, root, work, int.MaxValue })!;
        Check(Directory.GetFiles(Path.Combine(stage, "payload"), "*", SearchOption.AllDirectories).Length >= 400, "unmodified P-V1.32 updater accepts the entire migration package");
        Check(!File.Exists(Path.Combine(stage, "payload", "GameOffsets.dll")), "old updater excludes offsets from replacement");
        string script = (string)oldUpdates.GetField("InstallerScript", PrivateStatic)!.GetRawConstantValue()!;
        using (var stream = oldAssembly.GetManifestResourceStream("GameHelper.Utils.UpdatePathHelper.cs")!)
        using (var reader = new StreamReader(stream)) script = script.Replace("__UPDATE_PATH_HELPER__", reader.ReadToEnd());
        script = script.Replace("__PLAN__", Path.Combine(stage, "plan.json").Replace("'", "''"));
        // Only the restart arguments differ: --version prevents an overlay or game interaction in the fixture.
        string restart = "Start-Process -FilePath (Target 'GameHelper.exe') -WorkingDirectory";
        Check(script.Contains(restart), "historical installer restart location identified");
        script = script.Replace(restart, "Start-Process -FilePath (Target 'GameHelper.exe') -ArgumentList '--version' -WorkingDirectory");
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass"); start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using (var worker = System.Diagnostics.Process.Start(start)!) { var errorTask = worker.StandardError.ReadToEndAsync(); await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); string errors = await errorTask.WaitAsync(TimeSpan.FromSeconds(10)); Check(worker.ExitCode == 0, "historical installer exits successfully: " + errors); }
        for (int attempt = 0; attempt < 200 && File.Exists(Path.Combine(root, "GameHelper.exe")); attempt++) await Task.Delay(100);
        Check(!File.Exists(Path.Combine(root, "GameHelper.exe")) && !File.Exists(Path.Combine(root, "GameHelper.dll")) && !File.Exists(Path.Combine(root, "GameHelper.App.dll")), "real migration removes obsolete executables and DLLs after the bridge exits");
        foreach (string file in new[] { "ExileCenter.exe", "ExileCenter.dll", "ExileCenter.deps.json", "ExileCenter.runtimeconfig.json" })
            Check(File.ReadAllBytes(Path.Combine(root, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(native, file))), "migration installs the exact native " + file);
        foreach (string file in settings) Check(File.ReadAllText(Path.Combine(root, file)) == "personal unchanged: " + file, "migration preserves " + file);
        Check(File.ReadAllBytes(Path.Combine(root, "GameOffsets.dll")).SequenceEqual(offsets), "real migration leaves validated offsets byte-identical");
        Check(File.ReadAllText(Path.Combine(root, "GameHelper-personal-notes.txt")) == "must stay", "cleanup only removes explicitly listed binaries");
        Check(!File.Exists(Path.Combine(root, "local-update-error.txt")) && !File.Exists(Path.Combine(root, "Error.log")), "migration reports no runtime or installer error");
        var current = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ExileCenter.dll"));
        Check(current.GetName().Name == "ExileCenter" && !current.GetTypes().Any(t => t.Namespace?.StartsWith("GameHelper") == true), "native assembly and namespaces use ExileCenter");
        string shortcuts = Path.Combine(work, "shortcuts"); Directory.CreateDirectory(shortcuts);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true)!)!;
        dynamic link = shell.CreateShortcut(Path.Combine(shortcuts, "GameHelper.lnk"));
        link.TargetPath = Path.Combine(root, "GameHelper.exe"); link.Arguments = "--personal-choice"; link.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        link = shell.CreateShortcut(Path.Combine(shortcuts, "GameHelper-Other.lnk"));
        link.TargetPath = Path.Combine(work, "other", "GameHelper.exe"); link.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        current.GetType("ExileCenter.LocalUpdates", true)!.GetMethod("MigrateShortcuts", PrivateStatic)!.Invoke(null, new object[] { shortcuts, root });
        Check(File.Exists(Path.Combine(shortcuts, "ExileCenter.lnk")) && !File.Exists(Path.Combine(shortcuts, "GameHelper.lnk")), "shortcut display name migrates to ExileCenter");
        link = shell.CreateShortcut(Path.Combine(shortcuts, "ExileCenter.lnk"));
        Check((string)link.TargetPath == Path.Combine(root, "ExileCenter.exe") && (string)link.Arguments == "--personal-choice", "shortcut points to the new executable and retains personal arguments");
        Check(File.Exists(Path.Combine(shortcuts, "GameHelper-Other.lnk")), "shortcut targeting another installation is untouched");
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        var loadContextType = current.GetType("ExileCenter.Plugin.PluginAssemblyLoadContext", true)!;
        foreach (string directory in Directory.GetDirectories(Path.Combine(native, "Plugins")))
        {
            string file = Path.Combine(directory, Path.GetFileName(directory) + ".dll"); if (!File.Exists(file)) continue;
            var context = (System.Runtime.Loader.AssemblyLoadContext)Activator.CreateInstance(loadContextType, new object[] { file })!;
            var plugin = context.LoadFromAssemblyPath(file);
            Check(plugin.GetTypes().Length > 0 && !plugin.GetReferencedAssemblies().Any(a => a.Name == "GameHelper"), "plugin loads against the new core: " + Path.GetFileName(directory));
            context.Unload();
        }
        updates = current.GetType("ExileCenter.LocalUpdates", true)!; packageType = updates.GetNestedType("Package", BindingFlags.NonPublic)!;
        File.Copy(fullZip, Path.Combine(work, "update.zip"));
        var nextPackage = Package(File.ReadAllBytes(fullZip));
        string nextStage = (string)updates.GetMethod("Prepare", PrivateStatic)!.Invoke(null, new object[] { nextPackage, root, work, int.MaxValue })!;
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(nextStage, "plan.json")));
        Check(plan.RootElement.GetProperty("EntryPoint").GetString() == "ExileCenter.exe", "subsequent complete updates restart ExileCenter directly");
        Console.WriteLine(passed + " application update and migration checks passed.");
    }
}
