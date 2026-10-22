using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Build as a standalone net10.0-windows console fixture and run with run-check.ps1.
// Argument: exact packaged runtime. All writes stay in a unique local test folder.
string package = Path.GetFullPath(args[0]);
string plugin = Path.Combine(package, "Plugins", "CameraZoom");
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    foreach (string directory in new[] { plugin, package })
    {
        string file = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(file)) return context.LoadFromAssemblyPath(file);
    }
    return null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(plugin, "CameraZoom.dll"));
var installer = assembly.GetType("CameraZoom.PermanentCamera", true)!;
var settingsType = assembly.GetType("CameraZoom.CameraZoomSettings", true)!;
object settings = Activator.CreateInstance(settingsType)!;
void Set(string key, object value) => settingsType.GetProperty(key)!.SetValue(settings, value);
object? Call(string name, params object?[] values)
{
    try { return installer.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, values); }
    catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
}
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
void Reject(Action action, string name) { try { action(); } catch { passed++; return; } throw new Exception(name); }
string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
string root = Path.GetFullPath(Path.Combine("data", "camera-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
Set("Enabled", true); Set("MinimumZoom", 0.25f); Set("MaximumZoom", 2f);
byte[] encoded = (byte[])Call("Encode", settings)!;
Check(encoded.Length == 20 && BitConverter.ToUInt32(encoded) == 0x315A4345 && BitConverter.ToUInt32(encoded, 8) == 1, "native configuration header");
Check(BitConverter.ToSingle(encoded, 12) == 0.25f && BitConverter.ToSingle(encoded, 16) == 2f, "native limits exact");
Set("MinimumZoom", float.NaN); Set("MaximumZoom", float.PositiveInfinity);
encoded = (byte[])Call("Encode", settings)!;
Check(BitConverter.ToSingle(encoded, 12) == 0 && BitConverter.ToSingle(encoded, 16) == 3, "nonfinite settings normalized");
Set("MinimumZoom", 9.99f); Set("MaximumZoom", -1f);
encoded = (byte[])Call("Encode", settings)!;
Check(BitConverter.ToSingle(encoded, 16) == 10f, "inverted settings normalized");
string atomic = Path.Combine(root, "atomic.bin");
Call("AtomicWrite", atomic, encoded);
byte[] first = encoded, second = new byte[20];
using var stop = new CancellationTokenSource();
var reader = Task.Run(() =>
{
    while (!stop.IsCancellationRequested)
    {
        using var stream = new FileStream(atomic, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream(); stream.CopyTo(memory);
        byte[] bytes = memory.ToArray();
        if (!bytes.SequenceEqual(first) && !bytes.SequenceEqual(second)) throw new Exception("partial atomic config visible");
    }
});
try { for (int i = 0; i < 200; i++) Call("AtomicWrite", atomic, i % 2 == 0 ? second : first); }
finally { stop.Cancel(); reader.GetAwaiter().GetResult(); }
Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "atomic config has no temporary leftovers");
File.WriteAllText(Path.Combine(root, "PathOfExileSteam.exe"), "fixture-only");
string executableHash = Hash(Path.Combine(root, "PathOfExileSteam.exe"));
string native = Path.Combine(root, "version.dll");
File.WriteAllText(native, "unrelated DLL");
Reject(() => Call("Install", root, plugin, settings), "foreign DLL installation refusal");
Check(File.ReadAllText(native) == "unrelated DLL", "foreign DLL preserved");
File.Delete(native);
string storage = Path.Combine(root, ".ExileCenterCadavres"); Directory.CreateDirectory(storage);
string state = Path.Combine(storage, "state.json");
File.WriteAllText(state, "{\"Files\":235}");
Reject(() => Call("Install", root, plugin, settings), "legacy visual camera state requires migration");
File.WriteAllText(state, "{\"CameraDistance\":2}");
Reject(() => Call("Install", root, plugin, settings), "asset/native camera conflict refused");
File.WriteAllText(state, "{\"CameraDistance\":null,\"Files\":235}");
string corpseHash = Hash(state);
Set("MinimumZoom", 0.25f); Set("MaximumZoom", 2f);
Call("Install", root, plugin, settings);
Check((bool)Call("Installed", root)!, "installation ownership verified");
Check(Hash(native) == Hash(Path.Combine(plugin, "ExileCenter.Camera.dll")), "installed native identical to package");
Set("MinimumZoom", 0.5f); Set("MaximumZoom", 2.5f); Set("Enabled", false);
Call("Sync", root, settings);
byte[] synced = File.ReadAllBytes(Path.Combine(root, "ExileCenter.Camera.bin"));
Check(BitConverter.ToUInt32(synced, 8) == 0 && BitConverter.ToSingle(synced, 12) == 0.5f && BitConverter.ToSingle(synced, 16) == 2.5f, "saved changes synchronized without application lifetime");
Call("Install", root, plugin, settings);
Check((bool)Call("Installed", root)!, "repeat installation is idempotent");
File.WriteAllText(native, "changed after installation");
Reject(() => Call("Remove", root), "changed DLL removal refused");
Reject(() => Call("Sync", root, settings), "changed DLL synchronization refused");
Check(File.ReadAllText(native) == "changed after installation", "changed DLL preserved");
File.Copy(Path.Combine(plugin, "ExileCenter.Camera.dll"), native, true);
Call("Remove", root);
Check(!File.Exists(native) && !File.Exists(Path.Combine(root, "ExileCenter.Camera.bin")) && !(bool)Call("Installed", root)!, "owned files removed");
Check(Hash(state) == corpseHash && Hash(Path.Combine(root, "PathOfExileSteam.exe")) == executableHash, "corpse patch and executable untouched");
Console.WriteLine($"{passed} autonomous camera installation/settings checks passed.");
