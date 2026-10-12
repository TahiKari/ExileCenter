using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LibBundle3;

namespace CorpseCleaner;

internal static class NativeOodle
{
    private static IntPtr handle;
    private static string loadedHash = "";

    internal static void Ensure(string path, string cacheDirectory)
    {
        byte[] bytes;
        if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) bytes = FromBundle(path);
        else if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) && new FileInfo(path).Length < 16 * 1024 * 1024) bytes = File.ReadAllBytes(path);
        else throw new InvalidDataException("Sélectionnez oo2core.dll ou l'exécutable Exile Forge.");
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (handle != IntPtr.Zero)
        {
            if (hash != loadedHash) throw new InvalidOperationException("Redémarrez GameHelper avant de changer de bibliothèque Oodle.");
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

    // Extract one native dependency from a standard .NET bundle. Never execute its host.
    internal static byte[] FromBundle(string path)
    {
        using var stream = File.OpenRead(path);
        byte[] head = new byte[(int)Math.Min(stream.Length, 32 * 1024 * 1024)];
        stream.ReadExactly(head);
        ReadOnlySpan<byte> signature = new byte[] { 0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae };
        int marker = head.AsSpan().IndexOf(signature);
        if (marker < 8) throw new InvalidDataException("Ce fichier n'est pas un bundle .NET pris en charge.");
        long header = BitConverter.ToInt64(head, marker - 8);
        if (header < 0 || header >= stream.Length) throw new InvalidDataException("En-tête de bundle invalide.");
        stream.Position = header;
        using var reader = new BinaryReader(stream, Encoding.UTF8, true);
        uint major = reader.ReadUInt32(); _ = reader.ReadUInt32(); int count = reader.ReadInt32(); _ = ReadText(reader);
        if (major is not (2 or 6) || count < 1 || count > 2048) throw new InvalidDataException("Version de bundle non prise en charge.");
        stream.Seek(40, SeekOrigin.Current);
        for (int i = 0; i < count; i++)
        {
            long offset = reader.ReadInt64(), size = reader.ReadInt64(), compressed = major >= 6 ? reader.ReadInt64() : 0;
            byte kind = reader.ReadByte(); string name = ReadText(reader);
            if (!name.Equals("oo2core.dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (kind != 2 || compressed != 0 || size < 1 || size > 16 * 1024 * 1024 || offset < 0 || offset > stream.Length - size)
                throw new InvalidDataException("Dépendance Oodle non prise en charge.");
            stream.Position = offset; byte[] bytes = new byte[(int)size]; stream.ReadExactly(bytes); return bytes;
        }
        throw new FileNotFoundException("oo2core.dll n'est pas présent dans cet exécutable.");
    }

    private static string ReadText(BinaryReader reader)
    {
        int size = reader.Read7BitEncodedInt();
        if (size < 0 || size > 1024) throw new InvalidDataException("Nom de bundle invalide.");
        byte[] bytes = reader.ReadBytes(size);
        if (bytes.Length != size) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }
}
