using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using LibBundle3;
using ArchiveIndex = LibBundle3.Index;

unsafe class Program
{
    static delegate* unmanaged[Cdecl]<byte*, nint, byte*, nint, int, int, int, byte*, nint, void*, void*, void*, nint, int, nint> official;
    static delegate* unmanaged[Cdecl]<byte*, nint, byte*, nint, int, int, int, byte*, nint, void*, void*, void*, nint, int, nint> open;
    static delegate* unmanaged[Cdecl]<int, byte*, nint, byte*, int, void*, void*, void*, void*, nint, nint> encode;
    static int files, chunks;
    static long compared;
    static string lastPath = "";
    static int lastChunk;
    static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    static int Main(string[] args)
    {
        string game = args[2], statePath = Path.Combine(game, ".ExileCenterCadavres", "state.json");
        using var state = JsonDocument.Parse(File.ReadAllText(statePath));
        string original = Path.Combine(game, ".ExileCenterCadavres", state.RootElement.GetProperty("Id").GetString()!, "original.bin");
        string active = Path.Combine(game, "Bundles2", "_.index.bin");
        string originalHash = state.RootElement.GetProperty("OriginalHash").GetString()!, activeHash = state.RootElement.GetProperty("PatchedHash").GetString()!;
        if (Hash(args[0]) != "4758C7901CBD68DF1848B7AFED48C1975AD59B509886C21B87C0E91D717446B5" || Hash(original) != originalHash || Hash(active) != activeHash)
            throw new InvalidDataException("Reference files changed before read-only test");
        var native = NativeLibrary.Load(Path.GetFullPath(args[0]));
        var libre = NativeLibrary.Load(Path.GetFullPath(args[1]));
        official = (delegate* unmanaged[Cdecl]<byte*, nint, byte*, nint, int, int, int, byte*, nint, void*, void*, void*, nint, int, nint>)NativeLibrary.GetExport(native, "OodleLZ_Decompress");
        open = (delegate* unmanaged[Cdecl]<byte*, nint, byte*, nint, int, int, int, byte*, nint, void*, void*, void*, nint, int, nint>)NativeLibrary.GetExport(libre, "OodleLZ_Decompress");
        encode = (delegate* unmanaged[Cdecl]<int, byte*, nint, byte*, int, void*, void*, void*, void*, nint, nint>)NativeLibrary.GetExport(libre, "OodleLZ_Compress");
        WriterChecks();
        NativeLibrary.SetDllImportResolver(typeof(Oodle).Assembly, (name, _, _) => name == "oo2core" ? native : IntPtr.Zero);
        bool passed = false;
        string failure = "";
        try
        {
            CompareBundle(original);
            CompareBundle(active);
            CompareBundle(Path.Combine(game, "Bundles2", state.RootElement.GetProperty("BundleDirectory").GetString()!, state.RootElement.GetProperty("Id").GetString()! + ".bundle.bin"));
            using var input = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var index = new ArchiveIndex(input, false, false, new ReadFactory(Path.Combine(game, "Bundles2")));
            index.ParsePaths();
            var bundles = index.Files.Values.Where(f => f.Path != null && f.Path.StartsWith("metadata/monsters/", StringComparison.OrdinalIgnoreCase) && f.Path.EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.BundleRecord.Path).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
            Console.WriteLine($"Found {bundles.Count} relevant original bundles.");
            foreach (var bundle in bundles) CompareBundle(Path.Combine(game, "Bundles2", bundle));
            passed = true;
        }
        catch (Exception error) { failure = error.Message; Console.WriteLine("FAIL " + failure); }
        bool unchanged = Hash(original) == originalHash && Hash(active) == activeHash;
        var report = new { Passed = passed, Files = files, Chunks = chunks, Bytes = compared, LastFile = lastPath, LastChunk = lastChunk, Failure = failure, GameArchivesUnchanged = unchanged, OpenLibrarySha256 = Hash(args[1]) };
        File.WriteAllText("result.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
        return passed && unchanged ? 0 : 1;
    }
    static void WriterChecks()
    {
        int checks = 0;
        foreach (int size in new[] { 1, 2, 63, 64, 16383, 16384, 16385, 262143, 262144, 262145, 524287, 524288, 524289, 1048593 })
        foreach (bool random in new[] { false, true })
        {
            byte[] data = new byte[size], compressed = Enumerable.Repeat((byte)0xA5, size + 2 * ((size + 262143) / 262144) + 128).ToArray();
            if (random) new Random(20261022 + size).NextBytes(data);
            byte[] expected = (byte[])data.Clone(), decoded = new byte[size + 64], reference = new byte[size + 64];
            fixed (byte* src = data, enc = compressed, dst = decoded, gold = reference)
            {
                nint length = encode(13, src, size, enc, 4, null, null, null, null, 0);
                if (length != size + 2 * ((size + 262143) / 262144)) throw new Exception("Stored writer length");
                if (compressed.AsSpan((int)length).IndexOfAnyExcept((byte)0xA5) >= 0 || !data.AsSpan().SequenceEqual(expected)) throw new Exception("Writer buffer overrun or input modification");
                if (open(enc, length, dst, size, 1, 0, 0, null, 0, null, null, null, 0, 3) != size || !decoded.AsSpan(0, size).SequenceEqual(data)) throw new Exception("Independent writer/reader round trip");
                if (official(enc, length, gold, size, 1, 0, 0, null, 0, null, null, null, 0, 3) != size || !reference.AsSpan(0, size).SequenceEqual(data)) throw new Exception("Stored writer incompatible with reference decoder");
                Array.Fill(decoded, (byte)0x7A);
                if (open(enc, length - 1, dst, size, 1, 0, 0, null, 0, null, null, null, 0, 3) != 0 || decoded.AsSpan().IndexOfAnyExcept((byte)0x7A) >= 0) throw new Exception("Truncated chunk returned partial data");
                if (open(enc, length, dst, size, 1, 0, 0, null, 0, null, null, null, 0, 1) != 0) throw new Exception("Unsupported incremental call accepted");
                checks += 6;
            }
        }
        Console.WriteLine($"PASS writer cross-decoding, boundaries, unchanged buffers and truncated inputs: {checks} checks");
    }
    static void CompareBundle(string path)
    {
        lastPath = path;
        using var input = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        byte[] header = input.ReadBytes(60);
        if (header.Length != 60) throw new InvalidDataException("Truncated bundle header");
        int total = BitConverter.ToInt32(header, 0), count = BitConverter.ToInt32(header, 36), size = BitConverter.ToInt32(header, 40);
        if (total <= 0 || count <= 0 || count > 100000 || size < 1 || size > 16 * 1024 * 1024 || ((long)total + size - 1) / size != count)
            throw new InvalidDataException("Unexpected bundle framing");
        int[] lengths = new int[count];
        for (int i = 0; i < count; i++) lengths[i] = input.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            lastChunk = i;
            int length = lengths[i], outputSize = Math.Min(size, total - i * size);
            if (length <= 0 || length > 32 * 1024 * 1024) throw new InvalidDataException("Unexpected chunk length");
            byte[] compressed = new byte[length + 64], reference = new byte[outputSize + 64], candidate = new byte[outputSize + 64];
            input.BaseStream.ReadExactly(compressed.AsSpan(0, length));
            nint expected, actual;
            fixed (byte* src = compressed, dst = reference, other = candidate)
            {
                expected = official(src, length, dst, outputSize, 1, 0, 0, null, 0, null, null, null, 0, 3);
                actual = open(src, length, other, outputSize, 1, 0, 0, null, 0, null, null, null, 0, 3);
            }
            if (expected != outputSize || actual != outputSize) throw new InvalidDataException($"Chunk {i}: decoded length official={expected}, open={actual}, expected={outputSize}");
            if (!reference.AsSpan(0, outputSize).SequenceEqual(candidate.AsSpan(0, outputSize)))
            {
                int first = 0; while (reference[first] == candidate[first]) first++;
                throw new InvalidDataException($"Chunk {i}: unequal bytes, first difference at {first}; official={reference[first]}, open={candidate[first]}");
            }
            chunks++; compared += outputSize;
        }
        files++;
        Console.WriteLine($"PASS bundle={Path.GetFileName(path)}, chunks={count}, bytes={total}");
    }
}
sealed class ReadFactory(string directory) : DriveBundleFactory(directory)
{
    public override Bundle GetBundle(LibBundle3.Records.BundleRecord record) => new(new FileStream(Path.Combine(BaseDirectory, record.Path), FileMode.Open, FileAccess.Read, FileShare.Read), false, record);
    public override Stream CreateBundle(string path) => throw new InvalidOperationException("Read-only test");
    public override bool DeleteBundle(string path) => throw new InvalidOperationException("Read-only test");
}