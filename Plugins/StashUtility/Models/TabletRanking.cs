namespace StashUtility.Models
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    public sealed record TabletPriority(string Rank, float? Minimum = null);

    public sealed record TabletQuality(string Rank, int PriorityCount, int BestCount,
        string OrderKey, string Label, bool Uncertain, int MaximumBestCountUpperBound);

    /// <summary>
    /// Orders canonical modifier combinations, independently of filtering and UI.
    /// No global ordinal is claimed: the reference does not establish all legal
    /// prefix/suffix budgets, item-level pools or corruption restrictions in 0.5.5.
    /// p means matching priorities of the best selected grade, never a position.
    /// </summary>
    public sealed class TabletRankingEngine
    {
        private const string Grades = "SABCDEF";
        private readonly TabletMetaCatalog catalog;
        private readonly Dictionary<string, TabletQuality> cache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> maxima = new(StringComparer.Ordinal);
        public const int CacheLimit = 1024;
        public int CachedResults { get { lock (cache) return cache.Count; } }

        public TabletRankingEngine(TabletMetaCatalog catalog) => this.catalog = catalog;

        public static bool Compatible(IReadOnlyList<TabletMetaModifier> mods,
            int? maximumPrefixes = null, int? maximumSuffixes = null)
        {
            if (mods == null || mods.Count > 4 || mods.Any(m => m == null) ||
                mods.Select(m => m.Id).Distinct().Count() != mods.Count ||
                mods.Select(m => m.TabletType).Distinct().Count() > 1) return false;
            if (maximumPrefixes.HasValue && mods.Count(m => m.Affix == "prefix") > maximumPrefixes.Value ||
                maximumSuffixes.HasValue && mods.Count(m => m.Affix == "suffix") > maximumSuffixes.Value) return false;
            var groups = new HashSet<string>(StringComparer.Ordinal);
            return mods.All(m => (m.Groups ?? new()).All(groups.Add));
        }

        public TabletQuality Evaluate(TabletObservation observation, IReadOnlyDictionary<string, TabletPriority> priorities)
        {
            priorities ??= new Dictionary<string, TabletPriority>();
            var available = catalog.ForType(observation?.TabletType).ToArray();
            // Ignore stale identifiers belonging to other types. Canonical identities,
            // ranks and thresholds all belong in the key, including an explicit ignore.
            var selected = available.Where(m => priorities.TryGetValue(m.Id, out var p) &&
                p != null && p.Rank?.Length == 1 && Grades.Contains(p.Rank) &&
                (!p.Minimum.HasValue || float.IsFinite(p.Minimum.Value)))
                .ToDictionary(m => m.Id, m => priorities[m.Id], StringComparer.Ordinal);
            string configuration = (observation?.TabletType ?? "") + "|" + string.Join(";", selected.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => p.Key + "=" + p.Value.Rank + ":" + Number(p.Value.Minimum)));
            var detected = observation?.Mods ?? new();
            string signature = configuration + "|" + observation?.Readable + "|" + string.Join(";", detected
                .Select(m => (m?.Modifier != null && available.Any(a => ReferenceEquals(a, m.Modifier)) ? "K:" : "U:") +
                    (m?.Modifier?.Id ?? "?" + m?.RawId) + ":" + Number(m?.Roll)).OrderBy(s => s, StringComparer.Ordinal));
            lock (cache)
            {
                if (cache.TryGetValue(signature, out var existing)) return existing;
                var unique = detected.Where(m => m?.Modifier != null && available.Any(a => ReferenceEquals(a, m.Modifier)))
                    .GroupBy(m => m.Modifier.Id).Select(g => g.OrderByDescending(m => m.Roll).First()).ToArray();
                bool Matches(TabletDetectedMod m) => selected.TryGetValue(m.Modifier.Id, out var p) &&
                    (!p.Minimum.HasValue || m.Roll.HasValue && float.IsFinite(m.Roll.Value) && m.Roll.Value + .00001f >= p.Minimum.Value);
                var matched = unique.Where(Matches).ToArray();
                string rank = matched.Select(m => selected[m.Modifier.Id].Rank).OrderBy(r => Grades.IndexOf(r, StringComparison.Ordinal)).FirstOrDefault() ?? "";
                int bestCount = matched.Count(m => selected[m.Modifier.Id].Rank == rank);
                bool uncertain = observation == null || observation.Uncertain || unique.Length != detected.Count ||
                    !Compatible(unique.Select(m => m.Modifier).ToArray()) || unique.Any(m =>
                        selected.TryGetValue(m.Modifier.Id, out var p) && p.Minimum.HasValue &&
                        (!m.Roll.HasValue || !float.IsFinite(m.Roll.Value)));
                // Selected matches use their configured grade; all other known mods
                // retain reference quality ONLY for secondary comparison.
                var ordered = unique.Select(m => new { Mod = m.Modifier,
                    Grade = Grades.IndexOf(Matches(m) ? selected[m.Modifier.Id].Rank : m.Modifier.Rank, StringComparison.Ordinal) })
                    .OrderBy(m => m.Grade).ThenBy(m => Grades.IndexOf(m.Mod.Rank, StringComparison.Ordinal)).ThenBy(m => m.Mod.SubRank).ToArray();
                string gradeKey = string.Concat(ordered.Select(m => m.Grade.ToString(CultureInfo.InvariantCulture)))
                    .PadRight(4, '7'); // An empty slot is worse than F, never a fabricated F affix.
                string individualKey = string.Join("", ordered.Select(m => Grades.IndexOf(m.Mod.Rank, StringComparison.Ordinal)
                    + m.Mod.SubRank.ToString("D6", CultureInfo.InvariantCulture))).PadRight(28, '9');
                string key = (rank.Length == 0 ? 7 : Grades.IndexOf(rank, StringComparison.Ordinal)) +
                    (4 - bestCount).ToString(CultureInfo.InvariantCulture) + gradeKey + individualKey + (uncertain ? "1" : "0");
                string maximumKey = configuration + "|" + rank;
                if (!maxima.TryGetValue(maximumKey, out int maximum))
                {
                    var pool = available.Where(m => selected.TryGetValue(m.Id, out var p) && p.Rank == rank).ToArray();
                    var current = new List<TabletMetaModifier>();
                    void Visit(int start)
                    {
                        maximum = Math.Max(maximum, current.Count);
                        if (maximum == 4) return;
                        for (int i = start; i < pool.Length; i++)
                        {
                            current.Add(pool[i]);
                            if (Compatible(current)) Visit(i + 1);
                            current.RemoveAt(current.Count - 1);
                            if (maximum == 4) return;
                        }
                    }
                    Visit(0);
                    if (maxima.Count >= CacheLimit) maxima.Clear();
                    maxima[maximumKey] = maximum;
                }
                var result = new TabletQuality(rank, matched.Length, bestCount, key,
                    rank.Length == 0 ? "N/C" : rank + (uncertain ? "·?" : "·" + bestCount + "p"), uncertain, maximum);
                if (cache.Count >= CacheLimit) cache.Clear();
                cache[signature] = result;
                return result;
            }
        }

        private static string Number(float? number) => number?.ToString("R", CultureInfo.InvariantCulture) ?? "-";
    }
}
