// <copyright file="WorldAreaNames.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    ///     Maps internal WorldArea ids (e.g. "MapHiddenGrotto", "G1_1") to their
    ///     human-readable in-game names (e.g. "Hidden Grotto", "The Riverbank").
    ///     <para>
    ///     The mapping is loaded once from the embedded <c>Data/WorldAreaNames.tsv</c>
    ///     resource. To refresh it after a game patch, regenerate that file and rebuild.
    ///     </para>
    /// </summary>
    public static class WorldAreaNames
    {
        private const string ResourceSuffix = "Data.WorldAreaNames.tsv";

        private static readonly Lazy<IReadOnlyDictionary<string, string>> Map = new(Load);

        /// <summary>
        ///     Gets the number of known id -> name mappings.
        /// </summary>
        public static int Count => Map.Value.Count;

        /// <summary>
        ///     Gets the human-readable name for an internal WorldArea id, or the id
        ///     itself when no mapping is known (so callers always get a usable string).
        /// </summary>
        /// <param name="internalId">internal WorldArea id read from game memory.</param>
        /// <returns>display name, or <paramref name="internalId" /> when unmapped.</returns>
        public static string GetDisplayName(string internalId)
        {
            if (string.IsNullOrEmpty(internalId))
            {
                return internalId;
            }

            return Map.Value.TryGetValue(internalId, out var name) ? name : internalId;
        }

        /// <summary>
        ///     Tries to get the human-readable name for an internal WorldArea id.
        /// </summary>
        /// <param name="internalId">internal WorldArea id read from game memory.</param>
        /// <param name="displayName">resolved display name when found.</param>
        /// <returns>true if a mapping exists, otherwise false.</returns>
        public static bool TryGetDisplayName(string internalId, out string displayName)
        {
            if (!string.IsNullOrEmpty(internalId) && Map.Value.TryGetValue(internalId, out var name))
            {
                displayName = name;
                return true;
            }

            displayName = internalId;
            return false;
        }

        private static IReadOnlyDictionary<string, string> Load()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
                if (resourceName == null)
                {
                    Console.WriteLine($"[WorldAreaNames] Embedded resource '{ResourceSuffix}' not found.");
                    return result;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return result;
                }

                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    var tab = line.IndexOf('\t');
                    if (tab <= 0)
                    {
                        continue;
                    }

                    var id = line[..tab];
                    var name = line[(tab + 1)..];
                    result[id] = name;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldAreaNames] Failed to load mapping: {ex}");
            }

            return result;
        }
    }
}
