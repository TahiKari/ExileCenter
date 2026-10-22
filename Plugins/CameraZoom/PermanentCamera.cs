namespace CameraZoom;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

// Owns only these three files in the selected game directory. Never edits an EXE,
// the registry, shaders, game preferences or any third-party version.dll.
internal static class PermanentCamera
{
    internal const string ConfigName = "ExileCenter.Camera.bin";
    private const string OwnerName = "ExileCenter.Camera.owner.json";
    private sealed record Owner(int Format, string Sha256);

    internal static bool Installed(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        string owner = Path.Combine(directory, OwnerName);
        if (!File.Exists(owner)) return false;
        VerifyOwner(directory);
        return true;
    }

    private static void VerifyOwner(string directory)
    {
        var owner = JsonSerializer.Deserialize<Owner>(File.ReadAllText(Path.Combine(directory, OwnerName)));
        if (owner?.Format != 1 || owner.Sha256?.Length != 64 ||
            !string.Equals(Hash(Path.Combine(directory, "version.dll")), owner.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le composant caméra installé a changé. Aucun fichier n’a été remplacé.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static byte[] Encode(CameraZoomSettings settings)
    {
        settings.Normalize();
        byte[] bytes = new byte[20];
        BitConverter.GetBytes(0x315A4345u).CopyTo(bytes, 0);
        BitConverter.GetBytes(20u).CopyTo(bytes, 4);
        BitConverter.GetBytes(settings.Enabled ? 1u : 0u).CopyTo(bytes, 8);
        BitConverter.GetBytes(settings.MinimumZoom).CopyTo(bytes, 12);
        BitConverter.GetBytes(settings.MaximumZoom).CopyTo(bytes, 16);
        return bytes;
    }

    internal static void AtomicWrite(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void Sync(string directory, CameraZoomSettings settings)
    {
        if (Installed(directory)) AtomicWrite(Path.Combine(directory, ConfigName), Encode(settings));
    }

    internal static bool GameRunning()
    {
        var processes = Process.GetProcesses();
        try { return processes.Any(p => p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase)); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static void RequireClosed()
    {
        if (GameRunning()) throw new InvalidOperationException("Ferme PoE avant d’installer ou de retirer la caméra autonome.");
    }

    internal static void Install(string directory, string pluginDirectory, CameraZoomSettings settings)
    {
        RequireClosed();
        directory = Path.GetFullPath(directory);
        if (!new[] { "PathOfExileSteam.exe", "PathOfExile.exe", "PathOfExile_x64.exe" }.Any(n => File.Exists(Path.Combine(directory, n))))
            throw new InvalidDataException("Sélectionne le dossier contenant l’exécutable de PoE 2.");
        // The older asset-based camera must be restored first, otherwise both
        // distance multipliers would apply. Corpse/map options remain independent.
        foreach (string storage in new[] { ".ExileCenterCadavres", ".GameHelperCadavres" })
        {
            string state = Path.Combine(directory, storage, "state.json");
            if (!File.Exists(state)) continue;
            using var json = JsonDocument.Parse(File.ReadAllText(state));
            if (!json.RootElement.TryGetProperty("CameraDistance", out var camera) || camera.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("Le patch visuel contient une caméra ou provient d’une ancienne version. Restaure-le puis réapplique tes options dans Réglages visuels permanents, caméra décochée. Installe ensuite la caméra autonome ici.");
        }
        string source = Path.Combine(pluginDirectory, "ExileCenter.Camera.dll");
        using var resource = typeof(PermanentCamera).Assembly.GetManifestResourceStream("CameraZoom.Native.sha256")
            ?? throw new InvalidDataException("Empreinte du composant caméra absente.");
        using var reader = new StreamReader(resource);
        string expected = reader.ReadToEnd().Trim();
        if (expected.Length != 64 || !string.Equals(Hash(source), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le composant caméra fourni est incomplet ou altéré.");
        string dll = Path.Combine(directory, "version.dll"), owner = Path.Combine(directory, OwnerName), config = Path.Combine(directory, ConfigName);
        if (File.Exists(owner))
        {
            VerifyOwner(directory);
            if (!string.Equals(Hash(dll), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Retire l’ancienne caméra autonome avec le bouton ci-dessous, puis installe la nouvelle version.");
            Sync(directory, settings);
            return;
        }
        if (File.Exists(dll) || File.Exists(config))
            throw new InvalidOperationException("Des fichiers version.dll ou ExileCenter.Camera.bin existent déjà. Installation refusée pour les préserver.");
        string staged = dll + "." + Guid.NewGuid().ToString("N") + ".tmp";
        bool installed = false, ownConfig = false, ownOwner = false;
        try
        {
            File.Copy(source, staged, false);
            if (!string.Equals(Hash(staged), expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Copie caméra invalide.");
            RequireClosed();
            using (var stream = new FileStream(config, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { ownConfig = true; stream.Write(Encode(settings)); stream.Flush(true); }
            using (var stream = new FileStream(owner, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { ownOwner = true; JsonSerializer.Serialize(stream, new Owner(1, expected)); stream.Flush(true); }
            RequireClosed();
            File.Move(staged, dll); // Last step: no partial DLL can be loaded by PoE.
            installed = true;
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
            if (!installed) { if (ownOwner) File.Delete(owner); if (ownConfig) File.Delete(config); }
        }
    }

    internal static void Remove(string directory)
    {
        RequireClosed(); VerifyOwner(directory);
        File.Delete(Path.Combine(directory, "version.dll"));
        File.Delete(Path.Combine(directory, ConfigName));
        File.Delete(Path.Combine(directory, OwnerName));
        File.Delete(Path.Combine(directory, "ExileCenter.Camera.status"));
    }
}
