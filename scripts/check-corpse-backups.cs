using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using CorpseCleaner;
using LibBundle3;
using LibGGPK3;
using ArchiveIndex = LibBundle3.Index;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
static void Reject(Action action, string name) { try { action(); } catch (Exception) { Console.WriteLine("PASS " + name); return; } throw new Exception("Not rejected: " + name); }
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text);
if (args.Length == 2 && args[0] == "--retired-camera")
{
    string fixture = Path.GetFullPath(Path.Combine("data", "retired-camera-" + Guid.NewGuid().ToString("N")));
    var loader = typeof(ExileCenter.Core).Assembly.GetType("ExileCenter.Plugin.PManager")!
        .GetMethod("ReadPluginFiles", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    foreach (string name in new[] { "CameraZoom", "RenamedCamera" })
    {
        string directory = Path.Combine(fixture, name); Directory.CreateDirectory(Path.Combine(directory, "config"));
        File.Copy(args[1], Path.Combine(directory, name + ".dll"));
        string settings = Path.Combine(directory, "config", "settings.txt"); File.WriteAllText(settings, "{\"MaximumZoom\":1.7}");
        string before = Hash(settings);
        Check(loader.Invoke(null, new object[] { new DirectoryInfo(directory) }) == null, "retired CameraZoom rejected before assembly loading: " + name);
        Check(Hash(settings) == before, "retired plugin settings preserved: " + name);
    }
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "CameraZoom"), "retired camera assembly was never loaded");
    string retained = Path.Combine(fixture, "CorpseCleaner"); Directory.CreateDirectory(retained);
    File.Copy(typeof(CorpseCleaner.CorpseCleanerCore).Assembly.Location, Path.Combine(retained, "CorpseCleaner.dll"));
    var loaded = loader.Invoke(null, new object[] { new DirectoryInfo(retained) }) as System.Runtime.CompilerServices.ITuple;
    Check(loaded != null, "resource-based CorpseCleaner still passes the production plugin loader");
    ((System.Runtime.Loader.AssemblyLoadContext)loaded![1]!).Unload();
    return;
}
if (args.Length == 3 && args[0] == "--read-minimap")
{
    NativeOodle.Ensure("", "unused");
    string indexPath = Path.Combine(args[1], "Bundles2", "_.index.bin"), before = Hash(indexPath);
    using var input = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var index = new ArchiveIndex(input, true, false, new ReadFactory(Path.Combine(args[1], "Bundles2")));
    Directory.CreateDirectory(args[2]);
    foreach (string name in new[] { "minimap_blending_pixel.hlsl", "minimap_visibility_pixel.hlsl" })
    {
        Check(index.TryGetFile("shaders/" + name, out var asset), "real minimap resource exists: " + name);
        File.WriteAllBytes(Path.Combine(args[2], name), asset!.Read().ToArray());
    }
    Check(Hash(indexPath) == before, "read-only minimap extraction preserves the game index");
    return;
}
const string Base = "metadata/monsters/monster.ot";
const string Child = "metadata/monsters/test/test.ot";
const string Other = "metadata/items/test.ot";
const string Character = "metadata/characters/character.ot";
const string Blending = "shaders/minimap_blending_pixel.hlsl";
const string Visibility = "shaders/minimap_visibility_pixel.hlsl";
const string CameraScript = "version 3\r\nStats { life = 100 }\r\nPositioned { team = 1 on_initial_position_set = { KeepPosition(); } }\r\nActor { on_start = { KeepCamera(); } }";
const string MapShader = "float4 walkable_color = float4(1.0f, 1.0f, 1.0f, 0.01f);\nfloat4 walkability_map_color = lerp(walkable_color, float4(0.5f, 0.5f, 1.0f, 0.5f), walkable_to_edge_ratio);\nwalkability_map_color = lerp(walkability_map_color * float4(1.0f, 1.0f, 1.0f, visibility), float4(0.0f, 0.5f, 1.0f, 0.5f), pow(saturate(unexplored_edge), 25.0f));\nfloat max_walkable_fade = lerp(1.0f, 0.3f, walkability_opacity);";
const string VisibilityShader = "float4 res_color = float4(max(ratio, prev_ratio), 0.0f, 0.0f, 1.0f);\nif (visibility_fully_revealed > 0.5f)\n\tres_color = float4(1.0f, 0.0f, 0.0f, 1.0f);\nif (visibility_walkable_revealed > 0.5f) { KeepWalkableReveal(); }\nreturn res_color;";
const string Script = "version 2\r\nextends \"Metadata/Parent\"\r\nLife\r\n{\r\n\tbase_maximum_life = 1000\r\n\ton_death = { KeepDeath(); If( true, () { KeepNested(); } ); }\r\n\ton_spawned_alive = { DisableRendering(); }\r\n\ton_revive = { KeepRevive(); DisableRendering(); }\r\n}\r\nActor\r\n{\r\n\ton_death = { KeepActor(); }\r\n}\r\nStats { armour = 500 }\r\n";
var patched = Encoding.Unicode.GetString(CorpseScript.Patch(Utf16(Script), true)!);
Check(patched.Contains("base_maximum_life = 1000") && patched.Contains("armour = 500"), "health and stats unchanged");
Check(patched.Contains("KeepDeath(); If( true, () { KeepNested(); } );") && patched.Contains("KeepActor();"), "existing nested event and Actor scripts preserved");
Check(!patched.Contains("Delete();") && !patched.Contains("corpse_usable"), "no entity deletion or corpse gameplay changes");
Check(patched.Contains("on_spawned_alive = { this.gh_corpse_hidden = false;  DisableRendering(); }"), "initially hidden living monsters remain hidden");
Check(patched.Contains("EnableRendering();  KeepRevive(); DisableRendering();"), "original revive rendering decision preserved");
Check(CorpseScript.Patch(Utf16(patched), true) == null, "patch idempotence");
Reject(() => CorpseScript.Patch(Utf16("Life {"), true), "malformed script rejected");
Reject(() => CorpseScript.Patch(Utf16("Life {} Life {}"), true), "duplicate Life blocks rejected");
Check(CorpseScript.Patch(Utf16("Life { corpse_usable = false prevent_corpse_sinking = true } Actor {} Life { corpse_usable = false }"), false) == null, "official repeated child Life blocks without events stay byte-identical");
var repeated = Encoding.Unicode.GetString(CorpseScript.Patch(Utf16("Life { on_death = { FirstDeath(); } } Actor {} Life { on_death = { SecondDeath(); } }"), false)!);
Check(repeated.Contains("FirstDeath();") && repeated.Contains("SecondDeath();") && System.Text.RegularExpressions.Regex.Matches(repeated, "this.gh_corpse_hidden = true").Count == 2, "events in both child Life blocks preserved and patched");
Reject(() => CorpseScript.Patch(Utf16("Life { on_death = {} on_death = {} }"), false), "ambiguous duplicate event within one block still rejected");
Reject(() => CorpseScript.Patch(Utf16("Life { on_death={this.ef_corpse_dead=true;} }"), true), "existing Exile Forge corpse patch rejected");
Check(CorpseScript.Patch(Utf16("Actor {} // Life {\r\n"), false) == null, "comments are ignored");
Check(CorpseScript.Patch(Utf16("Life { label=\"}\\\"{\" /* } */ }"), true) != null, "quoted and commented braces handled");
var animation = Encoding.Unicode.GetString(CorpseScript.Patch(Utf16("Actor { on_state_in_sky_0 = { EnableRendering(); } }"), false)!);
Check(animation.Contains("EnableRendering(); IfExists(") && animation.Contains("DisableRendering();"), "late animation enables preserved for living entities and guarded for corpses");
Reject(() => CorpseArchive.Safe(Path.GetFullPath("data"), "../outside"), "path traversal rejected");
Reject(() => CorpseArchive.Apply("missing", "missing", "native", default, () => throw new InvalidOperationException("game open")), "apply refused while game is open before any file access");
Reject(() => CorpseArchive.Restore("missing", default, () => throw new InvalidOperationException("game open")), "restore refused while game is open before any file access");
var cameraText = Encoding.Unicode.GetString(CorpseScript.PatchCamera(Utf16(CameraScript), 1.75f));
Check(cameraText.Contains("KeepPosition();") && cameraText.Contains("life = 100") && cameraText.Contains("KeepCamera();"), "persistent camera preserves stats and existing callbacks");
Check(cameraText.Contains("CreateCameraZoomNode( 5000.0, 5000.0, 1.75 )") && !cameraText.Contains("1,75"), "persistent camera writes a culture-independent distance");
Check(Encoding.Unicode.GetString(CorpseScript.PatchCamera(Utf16("Positioned { team = 1 }"), 2f)).Contains("on_initial_position_set"), "missing camera callback added without replacing the component");
Reject(() => CorpseScript.PatchCamera(Utf16(cameraText), 2f), "previous permanent camera requires restoration");
Reject(() => CorpseScript.PatchCamera(Utf16(CameraScript), float.NaN), "invalid permanent distance rejected");
Reject(() => CorpseScript.PatchCamera(Utf16("Positioned {} Positioned {}"), 2f), "ambiguous camera component rejected");
Reject(() => new PermanentVisualOptions { HideCorpses = false }.Validate(), "empty permanent selection rejected");
var visuals = new PermanentVisualOptions { CameraDistance = 1.75f, MapBorders = true, BorderColor = [1f, 0.25f, 0f, 0.8f],
    ExploredColor = [0.1f, 0.2f, 0.3f, 0.4f], ExploringColor = [1f, 0f, 0f, 0.5f], BorderGlow = 2f, ExploringGlow = 3f };
string mapText = Encoding.UTF8.GetString(CorpseScript.PatchMinimap(Encoding.UTF8.GetBytes(MapShader), visuals));
Check(mapText.Contains("float4(2.0f, 0.5f, 0.0f, 0.8f)") && mapText.Contains("float4(3.0f, 0.0f, 0.0f, 0.5f)") &&
    mapText.Contains("float4 walkable_color = float4(0.1f, 0.2f, 0.3f, 0.4f)"), "three native layers have independent RGBA; glow multiplies RGB but preserves alpha");
Check(!mapText.Contains("visibility = 1") && !mapText.Contains("visibility_fully_revealed"), "persistent borders never change exploration state");
string visibilityText = Encoding.UTF8.GetString(CorpseScript.PatchMinimapVisibility(Encoding.UTF8.GetBytes(VisibilityShader), visuals));
Check(visibilityText.Contains("res_color = max(res_color, 0.18f)") && visibilityText.Contains("KeepWalkableReveal()") && visibilityText.Contains("max(ratio, prev_ratio)"), "native reveal threshold preserves the game's exploration calculations and walkable branch");
Reject(() => CorpseScript.PatchMinimapVisibility(Encoding.UTF8.GetBytes(visibilityText), visuals), "double visibility patch rejected");
Reject(() => CorpseScript.PatchMinimap(Encoding.UTF8.GetBytes(mapText), visuals), "double blending patch rejected");
Reject(() => CorpseScript.PatchMinimapVisibility(Encoding.UTF8.GetBytes("unknown shader"), visuals), "incompatible visibility shader rejected");
Reject(() => (visuals with { RevealThreshold = float.NaN }).Validate(), "non-finite reveal threshold rejected");
Reject(() => (visuals with { BorderGlow = 21f }).Validate(), "out-of-range glow rejected");
Reject(() => CorpseScript.PatchMinimap(Encoding.UTF8.GetBytes("unknown shader"), visuals), "incompatible shader refused before archive replacement");

string source = args[0], cache = Path.GetFullPath(Path.Combine("data", "unused-native-" + Guid.NewGuid().ToString("N")));
NativeOodle.Ensure(source, cache);
NativeOodle.Ensure("missing/Exile.Forge.exe", cache);
Check(!Directory.Exists(cache), "integrated codec needs no external library or native cache");
NativeOodle.Ensure("", cache);
Console.WriteLine("Integrated archive codec ready without Oodle or ExileForge.");
if (args.Length == 3 && args[2] == "--apply-native-minimap")
{
    CorpseArchive.EnsureClosed();
    string game = Path.GetFullPath(args[1]), liveIndex = Path.Combine(game, "Bundles2", "_.index.bin");
    string corpseState = Path.Combine(game, ".ExileCenterCadavres", "state.json"), corpseHash = Hash(corpseState);
    using var corpseManifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(corpseState));
    Check(Hash(liveIndex) == corpseManifest.RootElement.GetProperty("PatchedHash").GetString(), "live index matches the validated corpse-only patch");
    var selected = System.Text.Json.JsonSerializer.Deserialize<PermanentVisualOptions>(File.ReadAllText(source))!;
    selected.Validate();
    string savedIndex = Path.GetFullPath("before-minimap-" + Guid.NewGuid().ToString("N") + ".index.bin");
    File.Copy(liveIndex, savedIndex);
    string executable = Path.Combine(game, "PathOfExileSteam.exe"), executableHash = Hash(executable);
    Console.WriteLine(CorpseArchive.ApplyVisuals(game, "", "unused", selected, default, minimapOnly: true));
    try
    {
        using var beforeStream = File.OpenRead(savedIndex);
        using var beforeIndex = new ArchiveIndex(beforeStream, true, false, new ReadFactory(Path.Combine(game, "Bundles2")));
        using var afterStream = new FileStream(liveIndex, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var afterIndex = new ArchiveIndex(afterStream, true, false, new ReadFactory(Path.Combine(game, "Bundles2")));
        _ = beforeIndex.ParsePaths(); _ = afterIndex.ParsePaths();
        Check(beforeIndex.Files.Count == afterIndex.Files.Count, "live resource record count preserved");
        var changed = new List<string>();
        foreach (var pair in afterIndex.Files)
        {
            CheckRecord(pair.Key, pair.Value);
        }
        void CheckRecord(ulong key, LibBundle3.Records.FileRecord asset)
        {
            if (!beforeIndex.Files.TryGetValue(key, out var previous)) throw new Exception("Unexpected new resource");
            if (asset.BundleRecord.Path == previous.BundleRecord.Path && asset.Offset == previous.Offset && asset.Size == previous.Size &&
                asset.BundleRecord.UncompressedSize == previous.BundleRecord.UncompressedSize) return;
            string path = asset.Path ?? "";
            byte[] expected = path.Equals(Blending, StringComparison.OrdinalIgnoreCase) ? CorpseScript.PatchMinimap(previous.Read().ToArray(), selected) :
                path.Equals(Visibility, StringComparison.OrdinalIgnoreCase) ? CorpseScript.PatchMinimapVisibility(previous.Read().ToArray(), selected) :
                throw new Exception("Unexpected modified game resource: " + path);
            if (!asset.Read().Span.SequenceEqual(expected)) throw new Exception("Unexpected patched shader bytes");
            changed.Add(path);
        }
        Check(changed.Count == 2, "exactly the two native minimap shaders changed in the live index");
        Check(Hash(corpseState) == corpseHash && Hash(executable) == executableHash, "corpse backup manifest and game executable remain byte-identical");
        Check(!File.Exists(Path.Combine(game, "version.dll")), "no game-loaded camera proxy DLL");
        File.WriteAllText("minimap-live-receipt.json", System.Text.Json.JsonSerializer.Serialize(new {
            GameDirectory = game, Changed = changed, Records = afterIndex.Files.Count, CorpseManifestHash = corpseHash,
            OriginalIndexHash = Hash(savedIndex), PatchedIndexHash = Hash(liveIndex), ExecutableHash = executableHash,
            Options = selected, Backup = Path.Combine(game, ".ExileCenterMinimap", "state.json")
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    catch
    {
        CorpseArchive.Restore(game, default, minimapOnly: true);
        throw;
    }
    return;
}
if (args.Length == 3 && args[2] == "--stage-real-game")
{
    string game = Path.GetFullPath(args[1]), liveIndex = Path.Combine(game, "Bundles2", "_.index.bin");
    using var state = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(game, ".ExileCenterCadavres", "state.json")));
    string original = Path.Combine(game, ".ExileCenterCadavres", state.RootElement.GetProperty("Id").GetString()!, "original.bin");
    string originalHash = state.RootElement.GetProperty("OriginalHash").GetString()!, liveHash = state.RootElement.GetProperty("PatchedHash").GetString()!;
    Check(Hash(original) == originalHash && Hash(liveIndex) == liveHash, "real archives match their manifest before isolated staging");
    string stagedGame = Path.GetFullPath(Path.Combine("data", Guid.NewGuid().ToString("N"), "real-game"));
    Directory.CreateDirectory(Path.Combine(stagedGame, "Bundles2"));
    File.Copy(original, Path.Combine(stagedGame, "Bundles2", "_.index.bin"));
    using var saved = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var originalIndex = new ArchiveIndex(saved, false, false, new ReadFactory(Path.Combine(game, "Bundles2")));
    originalIndex.ParsePaths();
    foreach (var bundle in originalIndex.Files.Values.Where(f => f.Path != null && f.Path.StartsWith("metadata/monsters/", StringComparison.OrdinalIgnoreCase) && f.Path.EndsWith(".ot", StringComparison.OrdinalIgnoreCase)).Select(f => f.BundleRecord.Path).Distinct())
    {
        string destination = CorpseArchive.Safe(Path.Combine(stagedGame, "Bundles2"), bundle);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(CorpseArchive.Safe(Path.Combine(game, "Bundles2"), bundle), destination);
    }
    Console.WriteLine(CorpseArchive.Apply(stagedGame, "", "unused-native-cache", default, () => { }));
    using var prepared = new ArchiveIndex(Path.Combine(stagedGame, "Bundles2", "_.index.bin"), false);
    prepared.ParsePaths();
    int changed = 0;
    Check(prepared.Files.Count == originalIndex.Files.Count, "all real resource records preserved");
    foreach (var pair in prepared.Files)
    {
        if (!originalIndex.Files.TryGetValue(pair.Key, out var previous)) throw new Exception("Unexpected resource added");
        var asset = pair.Value;
        if (asset.BundleRecord.Path == previous.BundleRecord.Path && asset.Offset == previous.Offset && asset.Size == previous.Size && asset.BundleRecord.UncompressedSize == previous.BundleRecord.UncompressedSize) continue;
        if (asset.Path == null || !asset.Path.StartsWith("metadata/monsters/", StringComparison.OrdinalIgnoreCase) || !asset.Path.EndsWith(".ot", StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected changed resource " + asset.Path);
        byte[]? expected = CorpseScript.Patch(previous.Read().ToArray(), asset.Path.Equals(Base, StringComparison.OrdinalIgnoreCase));
        if (expected == null || !asset.Read().Span.SequenceEqual(expected)) throw new Exception("Unexpected script bytes " + asset.Path);
        changed++;
    }
    Check(changed == state.RootElement.GetProperty("Files").GetInt32(), "only the same corpse scripts change with the independent codec");
    Check(Hash(original) == originalHash && Hash(liveIndex) == liveHash, "real game and original backup remain unchanged");
    File.WriteAllText("staged-game.json", System.Text.Json.JsonSerializer.Serialize(new { Directory = stagedGame, Changed = changed, Records = prepared.Files.Count, LiveIndexUnchanged = true }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return;
}
string? legacyPackage = args.Length == 3 && args[1] == "--legacy-package" ? args[2] : null;
string? productionPackage = args.Length == 3 && args[1] == "--production-restore" ? Path.GetFullPath(args[2]) : null;
if (args.Length > 1 && legacyPackage == null && productionPackage == null)
{
    // Read only three known assets from the real Steam index; never open it for writing.
    string game = args[1], indexPath = Path.Combine(game, "Bundles2", "_.index.bin");
    string before = Hash(indexPath);
    using var input = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var index = new ArchiveIndex(input, true, false, new ReadFactory(Path.Combine(game, "Bundles2")));
    if (args.Length > 2 && args[2] == "--visual-assets")
    {
        Check(index.TryGetFile(Character, out var camera), "real character camera asset exists");
        string originalCamera = Encoding.Unicode.GetString(camera!.Read().Span);
        string preparedCamera = Encoding.Unicode.GetString(CorpseScript.PatchCamera(camera.Read().ToArray(), 2f));
        Check(preparedCamera.Contains("CreateCameraZoomNode") && preparedCamera.Contains("team = 1"), "real character script supports permanent camera insertion");
        Check(index.TryGetFile(Blending, out var blending) && index.TryGetFile(Visibility, out _), "real minimap shader assets exist");
        byte[] originalBlend = blending!.Read().ToArray();
        ShaderCompiler.Compile(originalBlend);
        ShaderCompiler.Compile(CorpseScript.PatchMinimap(originalBlend, visuals));
        ShaderCompiler.Compile(CorpseScript.PatchMinimap(originalBlend, new PermanentVisualOptions { MapBorders = true }));
        index.TryGetFile(Visibility, out var visibility);
        byte[] originalVisibility = visibility!.Read().ToArray();
        var installedScript = typeof(CorpseCleanerCore).Assembly.GetType("CorpseCleaner.CorpseScript", true)!;
        var installedOptionsType = installedScript.Assembly.GetType("CorpseCleaner.PermanentVisualOptions", true)!;
        var installedOptions = System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(visuals), installedOptionsType);
        byte[] InstalledPatch(string method, byte[] bytes) => (byte[])installedScript.GetMethod(method,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new[] { (object)bytes, installedOptions })!;
        byte[] installedBlend = InstalledPatch("PatchMinimap", originalBlend), installedVisibility = InstalledPatch("PatchMinimapVisibility", originalVisibility);
        Check(installedBlend.AsSpan().SequenceEqual(CorpseScript.PatchMinimap(originalBlend, visuals)) &&
            installedVisibility.AsSpan().SequenceEqual(CorpseScript.PatchMinimapVisibility(originalVisibility, visuals)), "the production plugin emits the verified native shader bytes");
        ShaderCompiler.Compile(installedBlend); ShaderCompiler.Compile(installedVisibility, "RenderVisibility");
        ShaderCompiler.Compile(originalVisibility, "RenderVisibility");
        foreach (float threshold in new[] { 0f, 0.18f, 1f })
            ShaderCompiler.Compile(CorpseScript.PatchMinimapVisibility(originalVisibility, visuals with { RevealThreshold = threshold }), "RenderVisibility");
        ShaderCompiler.Compile(CorpseScript.PatchMinimap(originalBlend, visuals with { BorderGlow = 20f, ExploringGlow = 20f, ExploredColor = [0, 0, 0, 0] }));
        Check(true, "both original and customized real minimap shaders compile, including threshold and glow limits");
        Check(Hash(indexPath) == before, "real game index unchanged after camera and shader verification");
        return;
    }
    if (args.Length > 2 && args[2] == "--scan-monsters")
    {
        index.ParsePaths();
        int scanned = 0, modified = 0;
        foreach (var group in index.Files.Values.Where(f => f.Path != null && f.Path.StartsWith("metadata/monsters/", StringComparison.OrdinalIgnoreCase) && f.Path.EndsWith(".ot", StringComparison.OrdinalIgnoreCase)).GroupBy(f => f.BundleRecord))
        {
            using var bundle = new ReadFactory(Path.Combine(game, "Bundles2")).GetBundle(group.Key);
            foreach (var asset in group)
            {
                try { if (CorpseScript.Patch(asset.Read(bundle).ToArray(), asset.Path.Equals(Base, StringComparison.OrdinalIgnoreCase)) != null) modified++; }
                catch (Exception ex) { throw new InvalidDataException(asset.Path + ": " + ex.Message, ex); }
                scanned++;
            }
        }
        Check(scanned > 0 && modified > 0, $"read-only preparation of {scanned} real monster scripts ({modified} changes)");
        Check(Hash(indexPath) == before, "real game index unchanged after full monster preparation");
        return;
    }
    if (args.Length > 2 && args[2] == "--portals")
    {
        index.ParsePaths();
        foreach (var path in new[] { "metadata/miscellaneousobjects/mapportal.ot", "metadata/miscellaneousobjects/multiplexportal.ot" })
            if (index.TryGetFile(path, out var asset)) Console.WriteLine(path + "\n" + Encoding.Unicode.GetString(asset.Read().Span));
        Check(Hash(indexPath) == before, "read-only portal metadata inspection");
        return;
    }
    foreach (string path in new[] { Base, "metadata/monsters/bossbase.ot", "metadata/monsters/bossbasestandalone.ot" })
    {
        if (!index.TryGetFile(path, out var asset)) continue;
        byte[] bytes = asset.Read().ToArray();
        Directory.CreateDirectory("samples"); File.WriteAllBytes(Path.Combine("samples", Path.GetFileName(path)), bytes);
        Console.WriteLine("READ-ONLY SAMPLE " + path + ": " + bytes.Length);
        if (Encoding.Unicode.GetString(bytes).Contains("ef_corpse_dead"))
            Reject(() => CorpseScript.Patch(bytes, path == Base), "existing external patch recognized: " + path);
        else Check(CorpseScript.Patch(bytes, path == Base) != null || path != Base, "real monster script compatibility: " + path);
    }
    Check(Hash(indexPath) == before, "real game index unchanged");
    return;
}

string data = Path.GetFullPath(Path.Combine("data", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(data);
var payloads = new Dictionary<string, byte[]> { [Base] = Utf16(Script), [Child] = Utf16(Script), [Other] = Utf16("Life { base_maximum_life = 50 }"), [Character] = Utf16(CameraScript), [Blending] = Encoding.UTF8.GetBytes(MapShader), [Visibility] = Encoding.UTF8.GetBytes(VisibilityShader) };
foreach (bool ggpk in new[] { false, true })
{
    string game = Path.Combine(data, ggpk ? "official" : "steam");
    CreateGame(game, payloads, ggpk);
    string archive = Path.Combine(game, ggpk ? "Content.ggpk" : "Bundles2/_.index.bin"), original = Hash(archive);
    Check(CorpseArchive.ArchivePath(game) == (ggpk ? "Content.ggpk" : "Bundles2/_.index.bin"), "launcher layout detection " + ggpk);
    Reject(() => CorpseArchive.Apply(game, source, cache, default, () => { }, text => { if (text.StartsWith("Vérification")) throw new IOException("fixture interruption"); }), "pre-commit failure leaves source untouched " + ggpk);
    Check(Hash(archive) == original, "original archive preserved after preparation failure " + ggpk);
    string result = CorpseArchive.Apply(game, source, cache, default, () => { }); Console.WriteLine(result);
    Check(Hash(archive) != original, "archive atomically patched " + ggpk);
    using (var session = ggpk ? new BundledSession(archive) : null)
    {
        using var index = ggpk ? null : new ArchiveIndex(archive, false);
        var active = session?.Index ?? index!;
        Check(active.TryGetFile(Base, out var baseAsset) && Encoding.Unicode.GetString(baseAsset.Read().Span).Contains(CorpseScript.Marker), "base script patched " + ggpk);
        Check(active.TryGetFile(Other, out var untouched) && untouched.Read().Span.SequenceEqual(payloads[Other]), "non-monster file unchanged " + ggpk);
    }
    Reject(() => CorpseArchive.Apply(game, source, cache, default, () => { }), "double apply blocked " + ggpk);
    Console.WriteLine(CorpseArchive.Restore(game, default, () => { }));
    Check(Hash(archive) == original, "bit-identical restoration " + ggpk);
    Reject(() => CorpseArchive.Apply(game, source, cache, new CancellationToken(true), () => { }), "cancelled operation leaves source unchanged " + ggpk);
    Check(Hash(archive) == original, "archive intact after cancellation " + ggpk);
    CorpseArchive.Apply(game, source, cache, default, () => { });
    using (var changed = File.Open(archive, FileMode.Append)) changed.WriteByte(42);
    string updated = Hash(archive);
    Reject(() => CorpseArchive.Restore(game, default, () => { }), "game update or external modification blocks restore " + ggpk);
    Check(Hash(archive) == updated, "external change preserved " + ggpk);
}
foreach (bool ggpk in new[] { false, true })
{
    string game = Path.Combine(data, ggpk ? "visual-official" : "visual-steam");
    CreateGame(game, payloads, ggpk);
    string archive = Path.Combine(game, ggpk ? "Content.ggpk" : "Bundles2/_.index.bin"), original = Hash(archive);
    CorpseArchive.ApplyVisuals(game, source, cache, visuals, default, () => { });
    using (var session = ggpk ? new BundledSession(archive) : null)
    {
        using var index = ggpk ? null : new ArchiveIndex(archive, false);
        var active = session?.Index ?? index!;
        Check(active.TryGetFile(Character, out var character) && Encoding.Unicode.GetString(character.Read().Span).Contains("1.75"), "camera persists in the closed archive without the plugin " + ggpk);
        Check(active.TryGetFile(Blending, out var blending) && Encoding.UTF8.GetString(blending.Read().Span).Contains("ExileCenter persistent minimap"), "borders and colors persist in the closed archive without the overlay " + ggpk);
        Check(active.TryGetFile(Visibility, out var visibility) && Encoding.UTF8.GetString(visibility.Read().Span).Contains("res_color = max(res_color, 0.18f)"), "native visibility shader receives the selected threshold " + ggpk);
        Check(active.TryGetFile(Base, out var monster) && Encoding.Unicode.GetString(monster.Read().Span).Contains(CorpseScript.Marker), "confirmed corpse behavior included with persistent camera and map " + ggpk);
    }
    CorpseArchive.Restore(game, default, () => { });
    Check(Hash(archive) == original, "combined visual patch restores the archive byte for byte " + ggpk);
    CorpseArchive.ApplyVisuals(game, source, cache, visuals with { HideCorpses = false, CameraDistance = null }, default, () => { });
    using (var session = ggpk ? new BundledSession(archive) : null)
    {
        using var index = ggpk ? null : new ArchiveIndex(archive, false);
        var active = session?.Index ?? index!;
        Check(active.TryGetFile(Base, out var monster) && monster.Read().Span.SequenceEqual(payloads[Base]) && active.TryGetFile(Character, out var character) && character.Read().Span.SequenceEqual(payloads[Character]), "map-only patch preserves camera and corpse scripts " + ggpk);
    }
    CorpseArchive.Restore(game, default, () => { });
    Check(Hash(archive) == original, "map-only restoration is exact " + ggpk);
}
foreach (bool ggpk in new[] { false, true })
{
    string game = Path.Combine(data, ggpk ? "minimap-over-corpses-official" : "minimap-over-corpses-steam");
    CreateGame(game, payloads, ggpk);
    string archive = Path.Combine(game, ggpk ? "Content.ggpk" : "Bundles2/_.index.bin"), original = Hash(archive);
    CorpseArchive.Apply(game, source, cache, default, () => { });
    string corpseIndex = Hash(archive), corpseState = Path.Combine(game, ".ExileCenterCadavres", "state.json"), corpseStateHash = Hash(corpseState);
    var minimap = visuals with { HideCorpses = false, CameraDistance = null };
    Reject(() => CorpseArchive.ApplyVisuals(game, source, cache, minimap, default, () => { }, text =>
        { if (text.StartsWith("Vérification")) throw new IOException("fixture interruption"); }, minimapOnly: true), "minimap pre-commit failure leaves the active corpse patch intact " + ggpk);
    Check(Hash(archive) == corpseIndex && Hash(corpseState) == corpseStateHash, "failed minimap preparation preserves the corpse index and backup manifest " + ggpk);
    CorpseArchive.ApplyVisuals(game, source, cache, minimap, default, () => { }, minimapOnly: true);
    using (var session = ggpk ? new BundledSession(archive) : null)
    {
        using var index = ggpk ? null : new ArchiveIndex(archive, false);
        var active = session?.Index ?? index!;
        Check(active.Files.Count == payloads.Count, "minimap adds no unrelated resource records " + ggpk);
        Check(active.TryGetFile(Base, out var monster) && monster.Read().Span.SequenceEqual(CorpseScript.Patch(payloads[Base], true)), "minimap leaves the validated corpse script byte-identical " + ggpk);
        Check(active.TryGetFile(Character, out var character) && character.Read().Span.SequenceEqual(payloads[Character]), "minimap leaves the camera script byte-identical " + ggpk);
        Check(active.TryGetFile(Blending, out var blending) && blending.Read().Span.SequenceEqual(CorpseScript.PatchMinimap(payloads[Blending], minimap)), "minimap writes the exact selected native colors " + ggpk);
        Check(active.TryGetFile(Visibility, out var visibility) && visibility.Read().Span.SequenceEqual(CorpseScript.PatchMinimapVisibility(payloads[Visibility], minimap)), "minimap writes the exact selected native visibility threshold " + ggpk);
    }
    Check(Hash(corpseState) == corpseStateHash, "separate minimap backup never rewrites the corpse manifest " + ggpk);
    var recorded = CorpseArchive.ReadMinimapOptions(game)!;
    Check(recorded.ExploredColor.SequenceEqual(minimap.ExploredColor) && recorded.ExploringGlow == minimap.ExploringGlow && recorded.RevealThreshold == minimap.RevealThreshold, "applied minimap colors, glow and threshold are recorded for upgrade and diagnosis " + ggpk);
    Reject(() => CorpseArchive.Restore(game, default, () => { }), "corpse restoration is blocked until the overlaid minimap patch is restored " + ggpk);
    Reject(() => CorpseArchive.ApplyVisuals(game, source, cache, minimap, default, () => { }, minimapOnly: true), "minimap reapplication requires explicit targeted restoration " + ggpk);
    CorpseArchive.Restore(game, default, () => { }, minimapOnly: true);
    Check(Hash(archive) == corpseIndex && Hash(corpseState) == corpseStateHash, "minimap-only restoration returns exactly to the active corpse patch " + ggpk);
    CorpseArchive.ApplyVisuals(game, source, cache, minimap with { RevealThreshold = 1f, ExploringGlow = 1f }, default, () => { }, minimapOnly: true);
    Check(CorpseArchive.ReadMinimapOptions(game)!.RevealThreshold == 1f, "new settings can be applied after minimap-only restoration " + ggpk);
    CorpseArchive.Restore(game, default, () => { }, minimapOnly: true);
    CorpseArchive.Restore(game, default, () => { });
    Check(Hash(archive) == original, "both nested patches restore the original archive exactly " + ggpk);
    string incompatible = game + "-incompatible";
    var missingShader = new Dictionary<string, byte[]>(payloads); missingShader.Remove(Visibility);
    CreateGame(incompatible, missingShader, ggpk);
    string incompatibleArchive = Path.Combine(incompatible, ggpk ? "Content.ggpk" : "Bundles2/_.index.bin"), incompatibleHash = Hash(incompatibleArchive);
    Reject(() => CorpseArchive.ApplyVisuals(incompatible, source, cache, minimap, default, () => { }, minimapOnly: true), "missing second shader rejects the whole minimap patch " + ggpk);
    Check(Hash(incompatibleArchive) == incompatibleHash, "neither shader is committed when the pair is incomplete " + ggpk);
}
var productionContext = productionPackage == null ? null : new LegacyBackupContext(productionPackage);
var productionRestore = productionContext?.LoadFromAssemblyPath(Path.Combine(productionPackage!, "Plugins", "CorpseCleaner", "CorpseCleaner.dll"))
    .GetType("CorpseCleaner.CorpseArchive", true)!.GetMethod("Restore", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
string RestoreUpdatedGame(string game, Action guard, bool minimapOnly = false) => productionRestore == null
    ? CorpseArchive.Restore(game, default, guard, minimapOnly)
    : (string)productionRestore.Invoke(null, new object?[] { game, CancellationToken.None, guard, minimapOnly })!;
foreach (bool ggpk in new[] { false, true })
foreach (bool minimapOnly in new[] { false, true })
{
    string game = Path.Combine(data, $"updated-{ggpk}-{minimapOnly}");
    CreateGame(game, payloads, ggpk);
    CorpseArchive.Apply(game, source, cache, default, () => { });
    CorpseArchive.ApplyVisuals(game, source, cache, visuals with { HideCorpses = false, CameraDistance = null }, default, () => { }, minimapOnly: true);
    string[] stores = { Path.Combine(game, ".ExileCenterMinimap"), Path.Combine(game, ".ExileCenterCadavres") };
    string[] manifests = stores.Select(store => Path.Combine(store, "state.json")).ToArray();
    string[] manifestHashes = manifests.Select(Hash).ToArray();
    var backups = manifests.Select(path =>
    {
        using var state = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return Path.Combine(Path.GetDirectoryName(path)!, state.RootElement.GetProperty("Id").GetString()!, "original.bin");
    }).ToArray();
    string[] backupHashes = backups.Select(Hash).ToArray();
    string archive = Path.Combine(game, ggpk ? "Content.ggpk" : "Bundles2/_.index.bin");
    // An unrelated edit while a patch is still referenced must never retire its state.
    using (var changed = File.Open(archive, FileMode.Append)) changed.WriteByte(42);
    string changedHash = Hash(archive);
    Reject(() => RestoreUpdatedGame(game, () => { }, minimapOnly), "active bundle references preserve the restoration refusal after external edits " + ggpk + minimapOnly);
    Check(Hash(archive) == changedHash && manifests.Select(Hash).SequenceEqual(manifestHashes), "refusal preserves both layered patch states and the changed game " + ggpk + minimapOnly);
    // Replacing the index with a newer complete game simulates a launcher update.
    var updatedPayloads = new Dictionary<string, byte[]>(payloads) { [Other] = Utf16("Life { base_maximum_life = 123456 } // new game version") };
    CreateGame(game, updatedPayloads, ggpk);
    string updatedHash = Hash(archive);
    // A failure between the two metadata moves must roll the first move back.
    Reject(() => RestoreUpdatedGame(game, () =>
    {
        if (!File.Exists(manifests[0])) throw new IOException("fixture archival interruption");
    }, minimapOnly), "interrupted stale-state archival rolls back " + ggpk + minimapOnly);
    Check(manifests.Select(Hash).SequenceEqual(manifestHashes) && Hash(archive) == updatedHash, "rollback preserves both original states and the updated game " + ggpk + minimapOnly);
    string result = RestoreUpdatedGame(game, () => { }, minimapOnly);
    Check(result.Contains("États archivés") && manifests.All(path => !File.Exists(path)), "either restore button retires both obsolete layers after a game update " + ggpk + minimapOnly);
    Check(stores.SelectMany(store => Directory.GetFiles(store, "state.json.superseded-*")).Select(Hash).Order().SequenceEqual(manifestHashes.Order()), "obsolete manifests are archived byte for byte " + ggpk + minimapOnly);
    Check(Hash(archive) == updatedHash && backups.Select(Hash).SequenceEqual(backupHashes), "updated game and original backups remain byte-identical " + ggpk + minimapOnly);
    Check(stores.All(store => !File.Exists(Path.Combine(store, "pending.json"))), "no active pending state remains after retirement " + ggpk + minimapOnly);
}
foreach (bool ggpk in new[] { false, true })
foreach (bool corruptBackup in new[] { false, true })
{
    string game = Path.Combine(data, $"unsafe-update-{ggpk}-{corruptBackup}");
    CreateGame(game, payloads, ggpk);
    CorpseArchive.Apply(game, source, cache, default, () => { });
    string manifest = Path.Combine(game, ".ExileCenterCadavres", "state.json"), stateHash = Hash(manifest);
    string archive = Path.Combine(game, ggpk ? "Content.ggpk" : "Bundles2/_.index.bin");
    if (corruptBackup)
    {
        using var state = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
        string saved = Path.Combine(Path.GetDirectoryName(manifest)!, state.RootElement.GetProperty("Id").GetString()!, "original.bin");
        using (var broken = File.Open(saved, FileMode.Append)) broken.WriteByte(1);
        CreateGame(game, new Dictionary<string, byte[]>(payloads) { [Other] = Utf16("new official data") }, ggpk);
    }
    else File.WriteAllBytes(archive, Array.Empty<byte>());
    string updatedHash = Hash(archive);
    Reject(() => RestoreUpdatedGame(game, () => { }), "unreadable index or damaged backup prevents stale-state retirement " + ggpk + corruptBackup);
    Check(Hash(manifest) == stateHash && Hash(archive) == updatedHash, "failed verification preserves the manifest and all game bytes " + ggpk + corruptBackup);
}
productionContext?.Unload();
if (productionPackage != null) Check(productionRestore != null, "update recovery exercised directly against the distributed production plugin");
if (legacyPackage != null)
{
    string pluginDirectory = Path.Combine(legacyPackage, "Plugins", "CorpseCleaner");
    var oldContext = new LegacyBackupContext(legacyPackage);

    var oldArchive = oldContext.LoadFromAssemblyPath(Path.Combine(pluginDirectory, "CorpseCleaner.dll")).GetType("CorpseCleaner.CorpseArchive", true)!;
    foreach (bool ggpk in new[] { false, true })
    {
        string game = Path.Combine(data, ggpk ? "legacy-official" : "legacy-steam"); CreateGame(game, payloads, ggpk);
        string archive = Path.Combine(game, ggpk ? "Content.ggpk" : "Bundles2/_.index.bin"), original = Hash(archive);
        oldArchive.GetMethod("Apply", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object?[] { game, source, Path.Combine(data, "legacy-native"), CancellationToken.None, new Action(() => { }), null });
        Check(Directory.Exists(Path.Combine(game, ".GameHelperCadavres")) || Directory.Exists(Path.Combine(game, ".ExileCenterCadavres")), "reference package creates its own backup " + ggpk);
        Reject(() => CorpseArchive.Apply(game, source, cache, default, () => { }), "new plugin refuses to overwrite a historical active patch " + ggpk);
        CorpseArchive.Restore(game, default, () => { });
        Check(Hash(archive) == original, "new plugin restores the historical archive byte for byte " + ggpk);
        CorpseArchive.Apply(game, source, cache, default, () => { });
        Check(File.Exists(Path.Combine(game, ".ExileCenterCadavres", "state.json")), "subsequent patches use the new backup directory " + ggpk);
        CorpseArchive.Restore(game, default, () => { });
        Check(Hash(archive) == original, "newly named backups restore correctly " + ggpk);
    }
}
var context = ImGuiNET.ImGui.CreateContext();
try
{
    var io = ImGuiNET.ImGui.GetIO();
    io.DisplaySize = new System.Numerics.Vector2(1280, 720); io.DeltaTime = 1f / 60;
    io.Fonts.AddFontDefault(); io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytes);
    string uiRoot = Path.Combine(data, "plugin"); Directory.CreateDirectory(Path.Combine(uiRoot, "Localization"));
    foreach (string language in new[] { "fr-FR", "en-US" }) File.Copy(Path.Combine("../../Plugins/CorpseCleaner/Localization", language + ".json"), Path.Combine(uiRoot, "Localization", language + ".json"));
    var plugin = new CorpseCleaner.CorpseCleanerCore(); plugin.SetPluginDllLocation(uiRoot); plugin.OnEnable(false);
    plugin.Settings.OodleSource = "missing/Exile.Forge.exe"; plugin.SaveSettings(); plugin.OnEnable(false);
    Check(plugin.Settings.OodleSource == "missing/Exile.Forge.exe" && !Directory.Exists(Path.Combine(uiRoot, "config", "native")), "legacy setting preserved without reading or copying an external library");
    plugin.Settings.GameDirectory = Path.Combine(data, "steam");
    plugin.Settings.PermanentCamera = true; plugin.Settings.MapBorders = true;
    plugin.Settings.ExploredColor = [0.2f, 0.3f, 0.4f, 0.5f]; plugin.Settings.ExploringColor = [1f, 0f, 0f, 0.75f];
    plugin.Settings.BorderGlow = 2.5f; plugin.Settings.ExploringGlow = 4f; plugin.Settings.RevealThreshold = 0.27f;
    plugin.SaveSettings(); plugin.OnEnable(false);
    Check(plugin.Settings.ExploredColor.SequenceEqual(new[] { 0.2f, 0.3f, 0.4f, 0.5f }) && plugin.Settings.ExploringColor[3] == 0.75f &&
        plugin.Settings.BorderGlow == 2.5f && plugin.Settings.RevealThreshold == 0.27f, "minimap RGBA, glow and visibility settings survive save and restart");
    string preservedDirectory = plugin.Settings.GameDirectory;
    plugin.Settings.GetType().GetMethod("ResetMinimapColors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(plugin.Settings, null);
    Check(plugin.Settings.BorderColor.SequenceEqual(new[] { 1f, 1f, 1f, 0.1f }) && plugin.Settings.BorderGlow == 12f && plugin.Settings.RevealThreshold == 0.18f &&
        plugin.Settings.GameDirectory == preservedDirectory && plugin.Settings.PermanentCamera && plugin.Settings.HideCorpses, "minimap reset restores Exile Forge color defaults without changing other settings");
    var mapHex = plugin.GetType().GetMethod("MapHex", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    Check((string)mapHex.Invoke(null, new object[] { new System.Numerics.Vector4(0f, 127f / 255f, 1f, 127f / 255f) })! == "7F007FFF", "advanced picker hex uses the same AARRGGBB channel order as the supplied screenshots");
    plugin.Settings.ExploringColor = [float.NaN, 0, 0, 0]; plugin.Settings.RevealThreshold = 4f; plugin.Settings.BorderGlow = -1f;
    plugin.Settings.GetType().GetMethod("Normalize", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(plugin.Settings, null);
    Check(plugin.Settings.ExploringColor.SequenceEqual(new[] { 0f, 0.5f, 1f, 0.5f }) && plugin.Settings.RevealThreshold == 1f && plugin.Settings.BorderGlow == 1f,
        "invalid persisted colors and out-of-range minimap values normalize before native patching");
    plugin.Settings.CameraDistance = 1.75f; plugin.SaveSettings(); plugin.OnEnable(false);
    Check(plugin.Settings.CameraDistance == 1.75f && plugin.Settings.PermanentCamera, "manual permanent camera distance survives restart without CameraZoom");
    string migrationGame = Path.Combine(data, "minimap-settings-migration");
    CreateGame(migrationGame, payloads, false);
    CorpseArchive.Apply(migrationGame, source, cache, default, () => { });
    var migrationOptions = visuals with { HideCorpses = false, CameraDistance = null };
    CorpseArchive.ApplyVisuals(migrationGame, source, cache, migrationOptions, default, () => { }, minimapOnly: true);
    File.WriteAllText(Path.Combine(uiRoot, "config", "settings.json"), System.Text.Json.JsonSerializer.Serialize(new {
        GameDirectory = migrationGame, HideCorpses = true, MapBorders = false, BorderColor = new[] { 0f, 0f, 0f, 0f }
    }));
    plugin.OnEnable(false);
    Check(plugin.Settings.MapBorders && plugin.Settings.HideCorpses && plugin.Settings.ExploredColor.SequenceEqual(migrationOptions.ExploredColor) &&
        plugin.Settings.ExploringGlow == migrationOptions.ExploringGlow, "first upgrade imports the prepared minimap settings without changing corpse preferences");
    plugin.Settings.ExploredColor = [0.8f, 0.7f, 0.6f, 0.5f]; plugin.SaveSettings(); plugin.OnEnable(false);
    Check(plugin.Settings.ExploredColor[0] == 0.8f, "subsequent restarts preserve edited minimap preferences instead of overwriting them with the applied patch");
    ExileCenter.Core.GHSettings.UiLanguage = ExileCenter.Localization.OverlayLanguage.French;
    for (int frame = 0; frame < 3; frame++)
    {
        ImGuiNET.ImGui.NewFrame(); ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(1000, 700)); ImGuiNET.ImGui.Begin("CorpseCleaner UI");
        plugin.DrawSettings(); ImGuiNET.ImGui.End(); ImGuiNET.ImGui.Render();
    }
    Check(ImGuiNET.ImGui.GetDrawData().TotalVtxCount > 0, "production plugin settings render in an isolated ImGui context");
    plugin.OnDisable();
}

finally { ImGuiNET.ImGui.DestroyContext(context); }
Console.WriteLine("ALL CORPSE CLEANER CHECKS PASSED");

static void CreateGame(string directory, Dictionary<string, byte[]> files, bool ggpk)
{
    Directory.CreateDirectory(Path.Combine(directory, "Bundles2"));
    using var payload = new MemoryStream();
    var positions = new Dictionary<string, int>();
    foreach (var file in files) { positions[file.Key] = (int)payload.Position; payload.Write(file.Value); }
    byte[] contentBundle = MakeBundle(payload.ToArray());
    using var names = new MemoryStream(); using (var strings = new BinaryWriter(names, Encoding.UTF8, true))
    {
        strings.Write(0); strings.Write(0);
        foreach (string name in files.Keys) { strings.Write(1); strings.Write(Encoding.UTF8.GetBytes(name)); strings.Write((byte)0); }
    }
    byte[] directoryBundle = MakeBundle(names.ToArray());
    using var raw = new MemoryStream(); using (var writer = new BinaryWriter(raw, Encoding.UTF8, true))
    {
        writer.Write(1); byte[] bundleName = Encoding.UTF8.GetBytes("fixture"); writer.Write(bundleName.Length); writer.Write(bundleName); writer.Write((int)payload.Length);
        writer.Write(files.Count);
        foreach (var file in files) { writer.Write(FixtureIndex.PathHash(file.Key)); writer.Write(0); writer.Write(positions[file.Key]); writer.Write(file.Value.Length); }
        writer.Write(1); writer.Write(FixtureIndex.DirectoryBytes((int)names.Length)); writer.Write(directoryBundle);
    }
    byte[] indexBundle = MakeBundle(raw.ToArray());
    if (!ggpk)
    {
        File.WriteAllBytes(Path.Combine(directory, "Bundles2", "fixture.bundle.bin"), contentBundle);
        File.WriteAllBytes(Path.Combine(directory, "Bundles2", "_.index.bin"), indexBundle);
    }
    else
    {
        string path = Path.Combine(directory, "Content.ggpk");
        using (var file = File.Create(path)) using (var writer = new BinaryWriter(file, Encoding.Unicode, true))
        {
            writer.Write(28); writer.Write(0x4b504747); writer.Write(3); writer.Write(28L); writer.Write(0L);
            writer.Write(50); writer.Write(0x52494450); writer.Write(1); writer.Write(0); writer.Write(new byte[32]); writer.Write((ushort)0);
        }
        using var archive = new GGPK(path);
        archive.Root.FindOrAddFile("Bundles2/_.index.bin", out var index); index.Write(indexBundle);
        archive.Root.FindOrAddFile("Bundles2/fixture.bundle.bin", out var fixture); fixture.Write(contentBundle);
    }
}
static byte[] MakeBundle(byte[] data)
{
    using var stream = new MemoryStream(); using (var bundle = new FixtureBundle(stream)) bundle.Save(data);
    return stream.ToArray();
}
sealed class FixtureBundle(Stream stream) : Bundle(stream, (LibBundle3.Records.BundleRecord?)null) { }
sealed class FixtureIndex(Stream stream) : ArchiveIndex(stream)
{
    internal static ulong PathHash(string path) => MurmurHash64A(Encoding.UTF8.GetBytes(path));
    internal static byte[] DirectoryBytes(int size) => MemoryMarshal.AsBytes(new DirectoryRecord[] { new(0xf42a94e69cff42fe, 0, size, size) }.AsSpan()).ToArray();
}
sealed class ReadFactory(string directory) : DriveBundleFactory(directory)
{
    public override Bundle GetBundle(LibBundle3.Records.BundleRecord record) => new(new FileStream(CorpseArchive.Safe(BaseDirectory, record.Path), FileMode.Open, FileAccess.Read, FileShare.Read), false, record);
    public override Stream CreateBundle(string path) => throw new Exception("Read-only source");
    public override bool DeleteBundle(string path) => throw new Exception("Read-only source");
}
sealed class BundledSession(string path) : IDisposable
{
    private readonly LibBundledGGPK3.BundledGGPK archive = new(path, false);
    internal ArchiveIndex Index => archive.Index;
    public void Dispose() => archive.Dispose();
}

sealed class LegacyBackupContext(string directory) : System.Runtime.Loader.AssemblyLoadContext(true)
{
    protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName name)
    {
        string file = Path.Combine(directory, "Plugins", "CorpseCleaner", name.Name + ".dll");
        if (!File.Exists(file)) file = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(file) ? LoadFromAssemblyPath(file) : null;
    }
}

static class ShaderCompiler
{
    internal static void Compile(byte[] bytes, string entry = "BlendMinimap")
    {
        string shader = "#define CBUFFER_BEGIN(name) cbuffer name {\n#define CBUFFER_END };\n#define TEXTURE2D_DECL(name) Texture2D name\n#define SAMPLE_TEX2D(tex,samp,uv) tex.Sample(samp,uv)\n#define PIXEL_RETURN_SEMANTIC SV_Target\nSamplerState SamplerLinearClampOffsetNoBias;\nSamplerState SamplerLinearClamp;\n" + Encoding.UTF8.GetString(bytes)
            .Replace("#include \"Shaders/Include/OETF.hlsl\"", "float4 OutputSDR(float4 color, float scale) { return color; }");
        byte[] source = Encoding.UTF8.GetBytes(shader);
        int result = D3DCompile(source, (nuint)source.Length, "minimap", IntPtr.Zero, IntPtr.Zero, entry, "ps_5_0", 0, 0, out var compiled, out var errors);
        try
        {
            if (result < 0) throw new InvalidDataException("HLSL compilation failed: " + (errors == null ? result.ToString("X8") : Marshal.PtrToStringAnsi(errors.GetBufferPointer(), checked((int)errors.GetBufferSize()))));
        }
        finally { if (compiled != null) Marshal.ReleaseComObject(compiled); if (errors != null) Marshal.ReleaseComObject(errors); }
    }

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DCompile(byte[] source, nuint size, string name, IntPtr defines, IntPtr include, string entry, string target, uint flags1, uint flags2, out ID3DBlob? compiled, out ID3DBlob? errors);
    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ID3DBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] nuint GetBufferSize();
    }
}
