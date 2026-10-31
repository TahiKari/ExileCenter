using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LibBundle3;
using LibBundle3.Records;
using LibBundledGGPK3;
using LibGGPK3;
using ArchiveIndex = LibBundle3.Index;
using Asset = LibBundle3.Records.FileRecord;
using GGDirectory = LibGGPK3.Records.DirectoryRecord;
using GGFile = LibGGPK3.Records.FileRecord;

namespace CorpseCleaner;

internal static class CorpseArchive
{
    private const string BaseScript = "metadata/monsters/monster.ot";
    private const string Storage = ".ExileCenterCadavres";
    private static string StoragePath(string root)
    {
        string current = Safe(root, Storage), legacy = Safe(root, ".GameHelperCadavres");
        return LoadState(current) == null && LoadState(legacy) != null ? legacy : current;
    }
    private sealed class State
    {
        public string Id { get; set; } = "";
        public string Archive { get; set; } = "";
        public string OriginalHash { get; set; } = "";
        public string PatchedHash { get; set; } = "";
        public int Files { get; set; }
        // Older manifests refer to bundles already indexed by the game; retain their path for restoration.
        public string BundleDirectory { get; set; } = "GameHelperCadavres";
        public PermanentVisualOptions? MinimapOptions { get; set; }
    }

    internal static bool GameRunning()
    {
        var processes = Process.GetProcesses();
        try { return processes.Any(p => p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase)); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static string ArchivePath(string directory)
    {
        string root = Path.GetFullPath(directory.Trim().Trim('"'));
        if (File.Exists(Path.Combine(root, "Content.ggpk"))) return "Content.ggpk";
        if (File.Exists(Path.Combine(root, "Bundles2", "_.index.bin"))) return "Bundles2/_.index.bin";
        throw new FileNotFoundException("Choisissez le dossier de PoE 2 contenant Content.ggpk ou Bundles2/_.index.bin.");
    }

    internal static string Apply(string directory, string nativePath, string nativeCache, CancellationToken token,
        Action? guard = null, Action<string>? progress = null)
        => ApplyVisuals(directory, nativePath, nativeCache, new PermanentVisualOptions(), token, guard, progress);

    internal static PermanentVisualOptions? ReadMinimapOptions(string directory) =>
        LoadState(Safe(Path.GetFullPath(directory), ".ExileCenterMinimap"))?.MinimapOptions;

    internal static string ApplyVisuals(string directory, string nativePath, string nativeCache, PermanentVisualOptions options,
        CancellationToken token, Action? guard = null, Action<string>? progress = null, bool minimapOnly = false)
    {
        options.Validate();
        guard ??= EnsureClosed;
        DateTime nextGuard = DateTime.MinValue;
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= nextGuard) { guard(); nextGuard = DateTime.UtcNow.AddMilliseconds(250); }
        }
        Check();
        string root = Path.GetFullPath(directory.Trim().Trim('"'));
        string relative = ArchivePath(root), archive = Safe(root, relative);
        string storage = minimapOnly ? Safe(root, ".ExileCenterMinimap") : StoragePath(root);
        if (minimapOnly && (options.HideCorpses || options.CameraDistance != null || !options.MapBorders))
            throw new InvalidDataException("La sauvegarde minimap ne peut contenir que les deux shaders de carte.");
        if (!minimapOnly && LoadState(Safe(root, ".ExileCenterMinimap")) != null)
            throw new InvalidOperationException("Restaurez uniquement la minimap avant de modifier les autres patches.");
        if (Directory.Exists(storage) && (File.GetAttributes(storage) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Le dossier de sauvegarde ne doit pas être une jonction.");
        if (LoadState(storage) != null) throw new InvalidOperationException("Une sauvegarde de patch existe déjà. Restaurez-la avant de réappliquer le patch.");
        NativeOodle.Ensure(nativePath, nativeCache);
        bool ggpk = relative == "Content.ggpk";
        string id = Guid.NewGuid().ToString("N"), job = Safe(storage, id);
        string staged = Safe(job, "prepared.bin"), backup = Safe(job, "original.bin");
        string bundleDirectory = minimapOnly ? "ExileCenterMinimap" : "ExileCenterCadavres";
        string bundleRelative = bundleDirectory + "/" + id + ".bundle.bin";
        string stageBundle = Safe(job, bundleRelative), installedBundle = Safe(root, "Bundles2/" + bundleRelative);
        var drive = new DriveInfo(Path.GetPathRoot(archive)!);
        if (drive.AvailableFreeSpace < new FileInfo(archive).Length + 256L * 1024 * 1024)
            throw new IOException("Espace libre insuffisant pour préparer une copie et conserver l'archive d'origine.");
        Directory.CreateDirectory(job);
        bool committed = false, movedBundle = false;
        string pending = Safe(storage, "pending.json"), stateFile = Safe(storage, "state.json");
        try
        {
            progress?.Invoke(ggpk ? "Préparation d'une copie de Content.ggpk (la copie peut être volumineuse)…" : "Préparation de l'index Steam/Epic…");
            // Keep the original locked against writers while preparing its replacement.
            using var original = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
            string originalHash = CopyHash(original, staged, Check);
            if (minimapOnly && LoadState(StoragePath(root)) is { } corpseState && originalHash != corpseState.PatchedHash)
                throw new InvalidDataException("L'archive ne correspond plus au patch de cadavres enregistré. Minimap annulée pour préserver les changements existants.");
            var changes = new List<(string Path, byte[] Bytes)>();
            progress?.Invoke("Analyse des ressources sélectionnées…");
            using (var session = Open(staged, ggpk, Path.Combine(root, "Bundles2"), job, true))
            {
                var index = session.Index;
                _ = index.ParsePaths(); // Some game versions contain unresolvable unrelated paths.
                index.TryGetFile(BaseScript, out var baseline);
                if (options.HideCorpses && baseline == null) throw new InvalidDataException("Script de base des monstres introuvable : cette version n'est pas prise en charge.");
                var candidates = index.Files.Values.Where(f => options.HideCorpses && f.Path != null &&
                    f.Path.StartsWith("metadata/monsters/", StringComparison.OrdinalIgnoreCase) &&
                    f.Path.EndsWith(".ot", StringComparison.OrdinalIgnoreCase)).ToList();
                if (options.HideCorpses && baseline != null && !candidates.Contains(baseline)) candidates.Add(baseline);
                foreach (var group in candidates.GroupBy(f => f.BundleRecord))
                {
                    Check();
                    using var sourceBundle = session.Factory.GetBundle(group.Key);
                    foreach (var asset in group)
                    {
                        Check();
                        if (asset.Size > 512 * 1024) throw new InvalidDataException("Script de monstre trop volumineux : " + asset.Path);
                        byte[]? patch = CorpseScript.Patch(asset.Read(sourceBundle).ToArray(), asset == baseline);
                        if (patch != null) changes.Add((asset.Path, patch));
                    }
                }
                if (options.HideCorpses && !changes.Any(c => c.Path.Equals(BaseScript, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Le script de base n'a pas pu être modifié. Aucun fichier du jeu n'a été remplacé.");
                void AddResource(string path, Func<byte[], byte[]> patch)
                {
                    Check();
                    if (!index.TryGetFile(path, out var asset) || asset.Size > 512 * 1024)
                        throw new InvalidDataException("Ressource absente ou incompatible : " + path);
                    changes.Add((path, patch(asset.Read().ToArray())));
                }
                if (options.CameraDistance is float distance)
                    AddResource("metadata/characters/character.ot", bytes => CorpseScript.PatchCamera(bytes, distance));
                if (options.MapBorders)
                {
                    AddResource("shaders/minimap_blending_pixel.hlsl", bytes => CorpseScript.PatchMinimap(bytes, options));
                    AddResource("shaders/minimap_visibility_pixel.hlsl", bytes => CorpseScript.PatchMinimapVisibility(bytes, options));
                }
                if (changes.Sum(c => (long)c.Bytes.Length) > 128 * 1024 * 1024)
                    throw new InvalidDataException("Taille du patch inattendue : modification annulée.");
                progress?.Invoke("Préparation de " + changes.Count + " fichiers, sans changement des points de vie…");
                using var data = new MemoryStream();
                using (var output = index.NewBundle(bundleRelative[..^11]))
                {
                    foreach (var change in changes)
                    {
                        Check();
                        if (!index.TryGetFile(change.Path, out var asset)) throw new InvalidDataException("Script manquant : " + change.Path);
                        asset.Redirect(output.Record!, checked((int)data.Position), change.Bytes.Length);
                        data.Write(change.Bytes);
                    }
                    output.Save(data.GetBuffer().AsSpan(0, checked((int)data.Length)));
                }
                Check(); index.Save();
            }
            progress?.Invoke("Vérification des fichiers préparés…");
            using (var verified = Open(staged, ggpk, Path.Combine(root, "Bundles2"), job, false))
                foreach (var change in changes)
                {
                    Check();
                    if (!verified.Index.TryGetFile(change.Path, out var asset) || !asset.Read().Span.SequenceEqual(change.Bytes))
                        throw new InvalidDataException("Échec de vérification : " + change.Path);
                }
            Flush(staged);
            var state = new State { Id = id, Archive = relative, OriginalHash = originalHash, PatchedHash = Hash(staged, Check), Files = changes.Count, BundleDirectory = bundleDirectory,
                MinimapOptions = minimapOnly ? options : null };
            Check();
            if (!ggpk)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(installedBundle)!);
                Flush(stageBundle);
                File.Move(stageBundle, installedBundle); // Unique name; never overwrite another tool's bundle.
                movedBundle = true;
            }
            File.WriteAllText(pending, JsonSerializer.Serialize(state));
            Flush(pending);
            Check(); guard(); original.Dispose(); guard(); token.ThrowIfCancellationRequested();
            File.Replace(staged, archive, backup); // The game archive is changed only by this atomic operation.
            committed = true;
            File.Move(pending, stateFile);
            return $"Réglages permanents appliqués : {changes.Count} fichiers. Ils restent actifs sans ExileCenter jusqu'à restauration ou mise à jour du jeu. Sauvegarde conservée dans {storage}.";
        }
        finally
        {
            if (!committed)
            {
                if (movedBundle && File.Exists(installedBundle)) File.Delete(installedBundle);
                if (File.Exists(pending)) File.Delete(pending);
                if (File.Exists(staged)) File.Delete(staged);
                if (File.Exists(stageBundle)) File.Delete(stageBundle);
            }
        }
    }

    internal static string Restore(string directory, CancellationToken token, Action? guard = null, bool minimapOnly = false)
    {
        guard ??= EnsureClosed;
        void Check() { token.ThrowIfCancellationRequested(); guard(); }
        Check();
        string root = Path.GetFullPath(directory.Trim().Trim('"'));
        State? minimap = LoadState(Safe(root, ".ExileCenterMinimap"));
        string storage = minimapOnly ? Safe(root, ".ExileCenterMinimap") : StoragePath(root);
        State? selected = minimapOnly ? minimap : LoadState(storage);
        State top = minimap ?? selected ?? throw new FileNotFoundException("Aucune sauvegarde CorpseCleaner dans ce dossier.");
        if (minimap != null && minimap.BundleDirectory != "ExileCenterMinimap" || selected != null &&
            (minimapOnly != (selected.BundleDirectory == "ExileCenterMinimap") || selected.Archive != top.Archive))
            throw new InvalidDataException("La sauvegarde ne correspond pas au type de patch demandé.");
        string archive = Safe(root, top.Archive);
        using var current = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        string currentHash = Hash(current, Check);
        if (currentHash != top.PatchedHash && currentHash != top.OriginalHash && ArchiveReplacedPatches(root, archive, currentHash, Check))
            return "Les anciens patches ne sont plus référencés par l'index actuel (mise à jour ou réparation du jeu). États archivés, sauvegardes conservées. Aucun fichier du jeu remplacé.";
        if (!minimapOnly && minimap != null)
            throw new InvalidOperationException("Restaurez uniquement la minimap avant de restaurer les autres patches.");
        State state = selected ?? throw new FileNotFoundException("Aucune sauvegarde CorpseCleaner dans ce dossier.");
        string backup = Safe(storage, state.Id + "/original.bin");
        if (currentHash == state.OriginalHash)
        {
            // Recover a preparation interrupted before the atomic replacement,
            // or a restoration interrupted only while cleaning up its manifest.
            foreach (string name in new[] { "state.json", "pending.json" })
            {
                string path = Safe(storage, name); if (File.Exists(path)) File.Delete(path);
            }
            if (state.Archive != "Content.ggpk")
            {
                string unused = Safe(root, "Bundles2/" + state.BundleDirectory + "/" + state.Id + ".bundle.bin");
                if (File.Exists(unused)) File.Delete(unused);
            }
            return "L'archive d'origine est déjà en place. L'opération interrompue a été annulée.";
        }
        if (currentHash != state.PatchedHash)
            throw new InvalidDataException("Le jeu ou un autre outil a modifié l'archive depuis le patch. Restauration refusée pour préserver ces changements.");
        if (!File.Exists(backup)) throw new InvalidDataException("La sauvegarde d'origine est absente.");
        using var saved = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Hash(saved, Check) != state.OriginalHash)
            throw new InvalidDataException("La sauvegarde d'origine est absente ou altérée.");
        Check(); current.Dispose(); saved.Dispose(); guard(); token.ThrowIfCancellationRequested();
        // Rename the patched archive aside, retaining both versions until restoration is confirmed.
        string previous = Safe(storage, state.Id + "/patched.bin");
        File.Replace(backup, archive, previous);
        if (Hash(archive, () => { }) != state.OriginalHash)
            throw new InvalidDataException("La restauration n'a pas pu être vérifiée. Les deux versions ont été conservées.");
        foreach (string name in new[] { "state.json", "pending.json" })
        {
            string path = Safe(storage, name); if (File.Exists(path)) File.Delete(path);
        }
        if (state.Archive != "Content.ggpk")
        {
            string bundle = Safe(root, "Bundles2/" + state.BundleDirectory + "/" + state.Id + ".bundle.bin");
            if (File.Exists(bundle)) File.Delete(bundle);
        }
        File.Delete(previous);
        return "Fichiers d'origine restaurés et vérifiés. Les autres réglages du jeu ont été conservés.";
    }

    private static bool ArchiveReplacedPatches(string root, string archive, string currentHash, Action check)
    {
        // A different hash alone does not prove removal: preserve the refusal whenever
        // any resource still points at an ExileCenter or historical GameHelper bundle.
        string[] directories = [".ExileCenterMinimap", ".ExileCenterCadavres", ".GameHelperCadavres"];
        var states = directories.Select(name => (Storage: Safe(root, name), State: LoadState(Safe(root, name))))
            .Where(item => item.State != null).Select(item => (item.Storage, State: item.State!)).ToArray();
        if (states.Any(item => Safe(root, item.State.Archive) != archive || currentHash == item.State.PatchedHash ||
            (item.State.BundleDirectory == "ExileCenterMinimap") != item.Storage.EndsWith(".ExileCenterMinimap", StringComparison.OrdinalIgnoreCase))) return false;
        check();
        NativeOodle.Ensure("", "");
        using (var session = Open(archive, Path.GetFileName(archive) == "Content.ggpk", Safe(root, "Bundles2"), root, false))
        {
            if (session.Index.Files.Count == 0) throw new InvalidDataException("Index vide : sauvegardes conservées, vérifiez les fichiers avec le lanceur officiel.");
            int inspected = 0;
            foreach (var asset in session.Index.Files.Values)
            {
                if ((inspected++ & 4095) == 0) check();
                string path = asset.BundleRecord.Path.Replace('\\', '/');
                if (path.StartsWith("ExileCenterMinimap/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("ExileCenterCadavres/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("GameHelperCadavres/", StringComparison.OrdinalIgnoreCase)) return false;
            }
        }
        var manifests = new List<(string Source, string Archived)>();
        string suffix = ".superseded-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ") + "-" + Guid.NewGuid().ToString("N");
        foreach (var item in states)
        {
            check();
            if ((File.GetAttributes(item.Storage) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Le dossier de sauvegarde ne doit pas être une jonction.");
            string saved = Safe(item.Storage, item.State.Id + "/original.bin");
            if (!File.Exists(saved) || Hash(saved, check) != item.State.OriginalHash)
                throw new InvalidDataException("Sauvegarde absente ou altérée : états conservés, vérifiez les fichiers avec le lanceur officiel.");
            string[] active = new[] { "state.json", "pending.json" }.Select(name => Safe(item.Storage, name)).Where(File.Exists).ToArray();
            if (active.Length != 1) throw new InvalidDataException("Plusieurs états de préparation : sauvegardes conservées.");
            manifests.Add((active[0], active[0] + suffix));
        }
        var moved = new List<(string Source, string Archived)>();
        try
        {
            foreach (var manifest in manifests)
            {
                check();
                File.Move(manifest.Source, manifest.Archived);
                moved.Add(manifest);
            }
            check();
        }
        catch
        {
            foreach (var manifest in moved.AsEnumerable().Reverse()) File.Move(manifest.Archived, manifest.Source);
            throw;
        }
        return true;
    }

    internal static void EnsureClosed()
    {
        if (GameRunning()) throw new InvalidOperationException("PoE est ouvert. Fermez le jeu avant d'appliquer ou de restaurer ce patch.");
    }

    private static State? LoadState(string storage)
    {
        string path = new[] { "state.json", "pending.json" }.Select(n => Safe(storage, n)).FirstOrDefault(File.Exists) ?? "";
        if (path.Length == 0) return null;
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Manifest de sauvegarde invalide.");
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(path));
        if (state == null || !System.Text.RegularExpressions.Regex.IsMatch(state.Id, "^[a-f0-9]{32}$") ||
            state.Archive is not ("Content.ggpk" or "Bundles2/_.index.bin") ||
            state.BundleDirectory is not ("ExileCenterCadavres" or "GameHelperCadavres" or "ExileCenterMinimap") ||
            state.OriginalHash.Length != 64 || state.PatchedHash.Length != 64)
            throw new InvalidDataException("Manifest de sauvegarde invalide.");
        return state;
    }

    internal static string Safe(string root, string relative)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Chemin d'archive invalide.");
        return path;
    }

    private static string CopyHash(Stream input, string destination, Action check)
    {
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024]; int read;
        while ((read = input.Read(buffer)) != 0) { check(); hash.AppendData(buffer, 0, read); output.Write(buffer, 0, read); }
        output.Flush(true);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Flush(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Flush(true);
    }

    private static string Hash(string path, Action check)
    {
        using var input = File.OpenRead(path);
        return Hash(input, check);
    }

    private static string Hash(Stream input, Action check)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024]; int read;
        while ((read = input.Read(buffer)) != 0) { check(); hash.AppendData(buffer, 0, read); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class PatchIndex(Stream stream, IBundleFactory factory) : ArchiveIndex(stream, false, false, factory)
    {
        internal Bundle NewBundle(string path) => CreateBundle(path);
    }

    private sealed class Session(PatchIndex index, IBundleFactory factory, IDisposable? owner, Stream source) : IDisposable
    {
        internal PatchIndex Index { get; } = index;
        internal IBundleFactory Factory { get; } = factory;
        public void Dispose()
        {
            try { Index.Dispose(); }
            finally { try { owner?.Dispose(); } finally { source.Dispose(); } }
        }
    }

    private static Session Open(string archive, bool ggpk, string originals, string stage, bool writable)
    {
        var stream = new FileStream(archive, FileMode.Open, writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.Read);
        GGPK? owner = null;
        try
        {
            IBundleFactory factory;
            Stream indexStream;
            if (ggpk)
            {
                owner = new GGPK(stream);
                var directory = owner.Root["Bundles2"] as GGDirectory ?? throw new InvalidDataException("Bundles2 absent de Content.ggpk.");
                var index = directory["_.index.bin"] as GGFile ?? throw new InvalidDataException("Index absent de Content.ggpk.");
                indexStream = new GGFileStream(index);
                factory = new GGPKBundleFactory(owner, directory);
            }
            else
            {
                indexStream = stream;
                factory = new StagedFactory(originals, stage);
            }
            return new Session(new PatchIndex(indexStream, factory), factory, owner, stream);
        }
        catch { owner?.Dispose(); stream.Dispose(); throw; }
    }

    private sealed class StagedFactory(string originals, string stage) : IBundleFactory
    {
        public Bundle GetBundle(BundleRecord record)
        {
            string prepared = Safe(stage, record.Path), source = File.Exists(prepared) ? prepared : Safe(originals, record.Path);
            return new Bundle(new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read), false, record);
        }
        public Stream CreateBundle(string path)
        {
            if (!path.StartsWith("ExileCenterCadavres/", StringComparison.Ordinal) &&
                !path.StartsWith("ExileCenterMinimap/", StringComparison.Ordinal)) throw new InvalidDataException("Création de bundle hors du patch refusée.");
            string destination = Safe(stage, path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            return new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        }
        public bool DeleteBundle(string path) => false; // Preserve all original and third-party bundle files.
    }
}
