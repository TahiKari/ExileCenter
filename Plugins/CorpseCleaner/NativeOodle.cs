using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using LibBundle3;

namespace CorpseCleaner;

internal static class NativeOodle
{
    private static IntPtr handle;
    private static readonly object Gate = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CodecVersion();

    internal static void Ensure(string path, string cacheDirectory)
    {
        // Legacy settings/arguments are retained for compatibility, never opened.
        // A fresh install uses only the codec shipped alongside this plugin.
        lock (Gate)
        {
            if (handle != IntPtr.Zero) return;
            var assembly = typeof(NativeOodle).Assembly;
            string library = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "ExileCenter.ArchiveCodec.dll");
            using var manifest = assembly.GetManifestResourceStream("CorpseCleaner.ArchiveCodec.sha256")
                ?? throw new InvalidDataException("Empreinte du codec intégré absente. Réinstallez le paquet complet ExileCenter.");
            using var reader = new StreamReader(manifest);
            string expected = reader.ReadToEnd().Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(expected, "^[A-Fa-f0-9]{64}$") ||
                !File.Exists(library) || new FileInfo(library).Length is < 1 or >= 16 * 1024 * 1024 ||
                !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Codec intégré absent ou altéré. Réinstallez le paquet complet ExileCenter.");
            IntPtr candidate = NativeLibrary.Load(library);
            try
            {
                foreach (string export in new[] { "OodleLZ_Compress", "OodleLZ_Decompress", "OodleLZDecoder_MemorySizeNeeded", "OodleLZ_GetCompressedBufferSizeNeeded", "OodleLZ_GetCompressScratchMemBound", "ExileCenterArchiveCodecVersion" })
                    if (!NativeLibrary.TryGetExport(candidate, export, out _)) throw new InvalidDataException("Codec intégré incompatible.");
                if (Marshal.GetDelegateForFunctionPointer<CodecVersion>(NativeLibrary.GetExport(candidate, "ExileCenterArchiveCodecVersion"))() != 1)
                    throw new InvalidDataException("Version du codec intégré incompatible.");
                NativeLibrary.SetDllImportResolver(typeof(Oodle).Assembly, Resolve);
                handle = candidate;
            }
            catch { NativeLibrary.Free(candidate); throw; }
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? search) => name == "oo2core" ? handle : IntPtr.Zero;

}
