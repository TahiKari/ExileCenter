using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LibBundle3;

namespace CorpseCleaner;

internal static class NativeOodle
{
    private static IntPtr handle;
    private static string loadedHash = "";

    internal static string FindSource(string path, string cacheDirectory)
    {
        path = path.Trim().Trim('"');
        if (path.Length > 0 && !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return path;
        if (!Directory.Exists(cacheDirectory)) return "";
        // Reuse the plugin's own verified cache, including installations with an old executable setting.
        var libraries = Directory.GetDirectories(cacheDirectory)
            .Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), "^[A-Fa-f0-9]{64}$"))
            .Select(d => Path.Combine(d, "oo2core.dll")).Where(File.Exists).ToArray();
        if (libraries.Length != 1) return "";
        string library = libraries[0];
        if (new FileInfo(library).Length is < 1 or >= 16 * 1024 * 1024 ||
            !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library)))
                .Equals(Path.GetFileName(Path.GetDirectoryName(library)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bibliothèque Oodle en cache altérée. Sélectionnez une copie intacte de oo2core.dll.");
        return library;
    }

    internal static void Ensure(string path, string cacheDirectory)
    {
        if (Path.GetExtension(path.Trim().Trim('"')).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Les exécutables ne sont plus utilisés. Sélectionnez directement oo2core.dll.");
        path = FindSource(path, cacheDirectory);
        if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) || new FileInfo(path).Length is < 1 or >= 16 * 1024 * 1024)
            throw new InvalidDataException("Sélectionnez une bibliothèque oo2core.dll compatible. Aucun exécutable tiers n'est nécessaire.");
        byte[] bytes = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (handle != IntPtr.Zero)
        {
            if (hash != loadedHash) throw new InvalidOperationException("Redémarrez ExileCenter avant de changer de bibliothèque Oodle.");
            return;
        }
        string directory = Path.Combine(cacheDirectory, hash);
        Directory.CreateDirectory(directory);
        string extracted = Path.Combine(directory, "oo2core.dll");
        if (!File.Exists(extracted)) File.WriteAllBytes(extracted, bytes);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(extracted))) != hash) throw new InvalidDataException("Bibliothèque Oodle altérée.");
        IntPtr candidate = NativeLibrary.Load(extracted);
        try
        {
            foreach (string export in new[] { "OodleLZ_Compress", "OodleLZ_Decompress", "OodleLZDecoder_MemorySizeNeeded", "OodleLZ_GetCompressedBufferSizeNeeded", "OodleLZ_GetCompressScratchMemBound" })
                if (!NativeLibrary.TryGetExport(candidate, export, out _)) throw new InvalidDataException("Bibliothèque Oodle incompatible.");
            NativeLibrary.SetDllImportResolver(typeof(Oodle).Assembly, Resolve);
            handle = candidate; loadedHash = hash;
        }
        catch { NativeLibrary.Free(candidate); throw; }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? search) => name == "oo2core" ? handle : IntPtr.Zero;

}
