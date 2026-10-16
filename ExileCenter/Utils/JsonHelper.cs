// <copyright file="JsonHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.IO;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    ///     Utility functions to help read/write to Json files.
    /// </summary>
    internal static class JsonHelper
    {
        /// <summary>Seeds bundled settings only when the user's file does not exist.</summary>
        public static void InitializeDefaultSettings(string directory)
        {
            using var stream = typeof(JsonHelper).Assembly.GetManifestResourceStream("GameHelper.Data.DefaultSettings.json")
                ?? throw new InvalidDataException("Bundled default settings are missing.");
            using var reader = new StreamReader(stream);
            var defaults = JObject.Parse(reader.ReadToEnd());
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (var setting in defaults.Properties())
            {
                var target = Path.GetFullPath(Path.Combine(root, setting.Name));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid default settings path: " + setting.Name);
                if (File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, setting.Value.ToString(Formatting.Indented));
                    try { File.Move(temporary, target); }
                    catch (IOException) when (File.Exists(target)) { }
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }

        /// <summary>
        ///     Creates new instance or loads from the file if it exists.
        ///     If the file is unreadable or unparseable, logs a warning,
        ///     renames the bad file aside (preserving it for diagnosis),
        ///     and falls back to a fresh default-constructed <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Class name to (De)serialize.</typeparam>
        /// <param name="file">file to load from.</param>
        /// <returns>class object containing the data (if data exists).</returns>
        public static T CreateOrLoadJsonFile<T>(FileInfo file)
            where T : new()
        {
            file.Refresh();
            file.Directory?.Create();
            if (file.Exists)
            {
                try
                {
                    var content = File.ReadAllText(file.FullName);
                    var loaded = JsonConvert.DeserializeObject<T>(content);
                    if (loaded != null)
                    {
                        return loaded;
                    }

                    Console.WriteLine($"[JsonHelper] {file.FullName} deserialized to null; falling back to defaults.");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[JsonHelper] {file.FullName} is corrupt or schema-mismatched: {ex.Message}. Falling back to defaults.");
                    QuarantineCorruptFile(file);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[JsonHelper] {file.FullName} could not be read: {ex.Message}. Falling back to defaults.");
                }
            }

            T obj = new();
            SafeToFile(obj, file);
            return obj;
        }

        /// <summary>
        ///     Saves the class object into the file atomically.
        ///     Writes to a `.tmp` sibling first, then renames over the target —
        ///     so a crash mid-write leaves either the old file intact or the
        ///     new file complete, never a half-written truncation.
        /// </summary>
        /// <param name="classObject">class object to save in the file.</param>
        /// <param name="file">file to save in.</param>
        public static void SafeToFile(object classObject, FileInfo file)
        {
            var content = JsonConvert.SerializeObject(classObject, Formatting.Indented);
            var tempPath = file.FullName + ".tmp";
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, file.FullName, overwrite: true);
        }

        private static void QuarantineCorruptFile(FileInfo file)
        {
            try
            {
                var corruptName = $"{file.FullName}.corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                File.Move(file.FullName, corruptName, overwrite: true);
                Console.WriteLine($"[JsonHelper] Renamed corrupt file to {corruptName} for inspection.");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[JsonHelper] Failed to quarantine corrupt file: {ex.Message}");
            }
        }
    }
}
