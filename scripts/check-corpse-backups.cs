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
const string Base = "metadata/monsters/monster.ot";
const string Child = "metadata/monsters/test/test.ot";
const string Other = "metadata/items/test.ot";
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
Reject(() => CorpseScript.Patch(Utf16("Life { on_death={this.ef_corpse_dead=true;} }"), true), "existing Exile Forge corpse patch rejected");
Check(CorpseScript.Patch(Utf16("Actor {} // Life {\r\n"), false) == null, "comments are ignored");
Check(CorpseScript.Patch(Utf16("Life { label=\"}\\\"{\" /* } */ }"), true) != null, "quoted and commented braces handled");
var animation = Encoding.Unicode.GetString(CorpseScript.Patch(Utf16("Actor { on_state_in_sky_0 = { EnableRendering(); } }"), false)!);
Check(animation.Contains("EnableRendering(); IfExists(") && animation.Contains("DisableRendering();"), "late animation enables preserved for living entities and guarded for corpses");
Reject(() => CorpseArchive.Safe(Path.GetFullPath("data"), "../outside"), "path traversal rejected");
Reject(() => CorpseArchive.Apply("missing", "missing", "native", default, () => throw new InvalidOperationException("game open")), "apply refused while game is open before any file access");
Reject(() => CorpseArchive.Restore("missing", default, () => throw new InvalidOperationException("game open")), "restore refused while game is open before any file access");

string source = args[0], cache = Path.GetFullPath("native");
NativeOodle.Ensure(source, cache);
Console.WriteLine("Native Oodle ready; Exile Forge was not executed.");
string? legacyPackage = args.Length == 3 && args[1] == "--legacy-package" ? args[2] : null;
if (args.Length > 1 && legacyPackage == null)
{
    // Read only three known assets from the real Steam index; never open it for writing.
    string game = args[1], indexPath = Path.Combine(game, "Bundles2", "_.index.bin");
    string before = Hash(indexPath);
    using var input = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var index = new ArchiveIndex(input, true, false, new ReadFactory(Path.Combine(game, "Bundles2")));
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
var payloads = new Dictionary<string, byte[]> { [Base] = Utf16(Script), [Child] = Utf16(Script), [Other] = Utf16("Life { base_maximum_life = 50 }") };
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
        Check(Directory.Exists(Path.Combine(game, ".GameHelperCadavres")), "historical plugin creates its actual legacy backup " + ggpk);
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
    plugin.Settings.GameDirectory = Path.Combine(data, "steam");
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