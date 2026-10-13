namespace StashUtility.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Newtonsoft.Json;

    public sealed class TabletMetaModifier
    {
        public string Id, TabletType, CanonicalType, Code, Rank, RankLabel, EnglishText, FrenchText, Affix;
        public string Justification, RankOrigin, Source, AffixSource, Strategy, Family, Availability;
        public int SubRank, SourceRow;
        public List<string> GameModIds = new(), MatchEvidence = new();
        public Dictionary<string, object> SourceFields = new();
    }

    public sealed class TabletMetaCatalog
    {
        public string Version, SourceFile, SourceSha256;
        public List<string> RankOrder = new();
        public Dictionary<string, string> Types = new();
        public List<Dictionary<string, string>> MethodAndSources = new();
        public List<TabletMetaModifier> Modifiers = new();
        public List<Dictionary<string, string>> ImportIssues = new();
        private Dictionary<(string Type, string Id), TabletMetaModifier> gameLookup;
        private Dictionary<string, TabletMetaModifier> byId;
        private Dictionary<(string Type, string Rank), TabletMetaModifier> bySubRank;

        public IEnumerable<TabletMetaModifier> ForType(string type) => Modifiers.Where(m => m.TabletType == type)
            .OrderBy(m => RankOrder.IndexOf(m.Rank)).ThenBy(m => m.SubRank);

        public TabletMetaModifier Find(string type, string id)
        {
            byId ??= Modifiers.ToDictionary(m => m.Id, StringComparer.Ordinal);
            return id != null && byId.TryGetValue(id, out var modifier) && modifier.TabletType == type ? modifier : null;
        }

        public TabletMetaModifier FindSubRank(string type, string rank)
        {
            bySubRank ??= Modifiers.ToDictionary(m => (m.TabletType, m.RankLabel));
            return bySubRank.TryGetValue((type, rank), out var modifier) ? modifier : null;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            gameLookup = null;
            byId = null; bySubRank = null;
            if (RankOrder.Count == 0 || RankOrder.Distinct().Count() != RankOrder.Count) errors.Add("rank-order");
            if (Types.Count != 8 || Types.Values.Distinct().Count() != 8) errors.Add("tablet-types");
            if (Modifiers.Select(m => m.Id).Distinct().Count() != Modifiers.Count) errors.Add("duplicate-id");
            if (Modifiers.GroupBy(m => (m.TabletType, m.RankLabel)).Any(g => g.Count() != 1)) errors.Add("duplicate-subrank");
            foreach (var m in Modifiers)
            {
                if (!Types.TryGetValue(m.CanonicalType ?? "", out var type) || type != m.TabletType ||
                    string.IsNullOrWhiteSpace(m.Code) || m.Id != m.CanonicalType + ":" + m.Code ||
                    !RankOrder.Contains(m.Rank) || m.SubRank < 1 || m.RankLabel != m.Rank + m.SubRank ||
                    string.IsNullOrWhiteSpace(m.EnglishText) || string.IsNullOrWhiteSpace(m.FrenchText) ||
                    m.Affix is not ("prefix" or "suffix") || string.IsNullOrWhiteSpace(m.Justification)) errors.Add(m.Id ?? "missing-entry");
            }
            foreach (string type in Types.Values)
                if (!Modifiers.Any(m => m.TabletType == type)) errors.Add(type);
            foreach (string type in Types.Values)
                if (ForType(type).SelectMany(m => m.GameModIds.Select(id => (id, m.Id))).GroupBy(p => p.id, StringComparer.OrdinalIgnoreCase)
                    .Any(g => g.Select(p => p.Id).Distinct().Count() > 1)) errors.Add("ambiguous-game-id:" + type);
            return errors;
        }

        public TabletObservation Recognize(string type, IEnumerable<string> rawIds, bool readable = true)
        {
            var observation = new TabletObservation { TabletType = type, Readable = readable };
            gameLookup ??= Modifiers.Where(m => m.Availability == "active").SelectMany(m => m.GameModIds.Select(id => ((m.TabletType, id.ToUpperInvariant()), m)))
                .GroupBy(p => p.Item1).Where(g => g.Select(p => p.m.Id).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First().m);
            foreach (string raw in (rawIds ?? Array.Empty<string>()).Take(5))
            {
                string key = (raw ?? "").ToUpperInvariant();
                if (!gameLookup.TryGetValue((type, key), out var definition))
                {
                    int end = key.Length;
                    while (end > 0 && char.IsAsciiDigit(key[end - 1])) end--;
                    if (end < key.Length) gameLookup.TryGetValue((type, key[..end]), out definition);
                }
                observation.Mods.Add(new() { RawId = raw ?? "", Modifier = definition });
            }
            return observation;
        }

    }

    public enum TabletRuleKind { All, Any, Modifier, RankExact, SubRank, MinimumCount, NoRankBelow }
    public enum TabletFilterState { Accepted, Rejected, Uncertain, Invalid }
    public enum TabletFilterAction { Keep, Ignore, Signal }

    public sealed class TabletRuleNode
    {
        public string Id = Guid.NewGuid().ToString("N");
        public TabletRuleKind Kind = TabletRuleKind.All;
        public List<TabletRuleNode> Children = new();
        public string ModifierId = "", Rank = "S", RankLabel = "S1";
        public int Minimum = 1;
        public bool ExactCountRank;
        public float? MinimumRoll;
    }

    public sealed class TabletFilterProfile
    {
        public string Id = Guid.NewGuid().ToString("N"), Name = "Mon profil", TabletType = "";
        public TabletRuleNode Rule = new();
        public TabletFilterAction OnMatch = TabletFilterAction.Keep, OnMiss = TabletFilterAction.Ignore;
    }

    public sealed class TabletProfileArchive
    {
        public int SchemaVersion = 1;
        public string TabletType, CatalogSha256, ActiveProfileId;
        public bool Enabled;
        public List<TabletFilterProfile> Profiles = new();
    }

    public sealed class TabletDetectedMod
    {
        public string RawId;
        public TabletMetaModifier Modifier;
        public float? Roll;
    }

    public sealed class TabletObservation
    {
        public string TabletType;
        public bool Readable = true;
        public List<TabletDetectedMod> Mods = new();
        public bool Uncertain => !Readable || Mods == null || Mods.Count > 4 || Mods.Any(m => m == null || m.Modifier == null) ||
            Mods.Where(m => m.Modifier != null).Select(m => m.Modifier.Id).Distinct().Count() != Mods.Count;
    }

    public sealed class TabletNodeEvaluation
    {
        public TabletRuleNode Node;
        public bool? Matches;
        public int Count;
        public List<TabletNodeEvaluation> Children = new();
    }

    public sealed class TabletFilterResult
    {
        public TabletFilterState State;
        public TabletFilterAction Action;
        public string Error = "";
        public TabletObservation Observation;
        public TabletNodeEvaluation Rule;
    }

    public static class TabletFilterEngine
    {
        public const int MaximumDepth = 32, MaximumNodes = 256;

        public static string Validate(TabletFilterProfile profile, TabletMetaCatalog catalog)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 120 ||
                string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 64) return "profile";
            if (!catalog.Types.ContainsValue(profile.TabletType)) return "tablet-type";
            if (!Enum.IsDefined(profile.OnMatch) || !Enum.IsDefined(profile.OnMiss)) return "action";
            var seen = new HashSet<TabletRuleNode>(ReferenceEqualityComparer.Instance);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            string Visit(TabletRuleNode node, int depth)
            {
                if (node == null || !seen.Add(node) || depth > MaximumDepth || seen.Count > MaximumNodes) return "tree-limit";
                if (string.IsNullOrWhiteSpace(node.Id) || node.Id.Length > 64 || !ids.Add(node.Id)) return "node-id";
                if (!Enum.IsDefined(node.Kind)) return "condition-kind";
                if (node.MinimumRoll is { } minimum && (!float.IsFinite(minimum) ||
                    node.Kind is not (TabletRuleKind.Modifier or TabletRuleKind.SubRank))) return "minimum-roll";
                if (node.Kind is TabletRuleKind.All or TabletRuleKind.Any)
                {
                    if (node.Children == null || node.Children.Count == 0) return "empty-group";
                    foreach (var child in node.Children) { string error = Visit(child, depth + 1); if (error.Length > 0) return error; }
                }
                else
                {
                    if (node.Children == null || node.Children.Count != 0) return "leaf-children";
                    if (node.Kind == TabletRuleKind.Modifier && catalog.Find(profile.TabletType, node.ModifierId)?.Availability != "active") return "modifier";
                    if (node.Kind == TabletRuleKind.SubRank && catalog.FindSubRank(profile.TabletType, node.RankLabel)?.Availability != "active") return "subrank";
                    if (node.Kind is TabletRuleKind.RankExact or TabletRuleKind.MinimumCount or TabletRuleKind.NoRankBelow)
                        if (!catalog.RankOrder.Contains(node.Rank)) return "rank";
                    if (node.Kind == TabletRuleKind.MinimumCount && (node.Minimum < 1 || node.Minimum > 4)) return "count";
                }
                return "";
            }
            return Visit(profile.Rule, 0);
        }

        public static TabletFilterResult Evaluate(TabletFilterProfile profile, TabletMetaCatalog catalog, TabletObservation observation)
        {
            var result = new TabletFilterResult { Observation = observation, State = TabletFilterState.Invalid, Action = TabletFilterAction.Signal };
            result.Error = Validate(profile, catalog);
            if (result.Error.Length > 0) return result;
            if (observation == null || observation.TabletType != profile.TabletType) { result.Error = "tablet-type"; return result; }
            // Never count the same modifier twice, even when the input is malformed.
            var observed = (observation.Mods ?? new()).Where(m => m?.Modifier != null).Select(m => m.Modifier).ToArray();
            bool uncertain = observation.Uncertain || observed.Any(m => m.TabletType != profile.TabletType || catalog.Find(profile.TabletType, m.Id) == null);
            var mods = observed.Select(m => catalog.Find(profile.TabletType, m.Id)).Where(m => m != null).DistinctBy(m => m.Id).ToArray();
            TabletNodeEvaluation Visit(TabletRuleNode node)
            {
                var value = new TabletNodeEvaluation { Node = node };
                if (node.Kind is TabletRuleKind.All or TabletRuleKind.Any)
                {
                    value.Children = node.Children.Select(Visit).ToList();
                    value.Matches = node.Kind == TabletRuleKind.All
                        ? value.Children.Any(c => c.Matches == false) ? false : value.Children.Any(c => c.Matches == null) ? null : true
                        : value.Children.Any(c => c.Matches == true) ? true : value.Children.Any(c => c.Matches == null) ? null : false;
                    return value;
                }
                int threshold = catalog.RankOrder.IndexOf(node.Rank);
                value.Count = node.Kind switch
                {
                    TabletRuleKind.Modifier => mods.Count(m => m.Id == node.ModifierId),
                    TabletRuleKind.SubRank => mods.Count(m => m.RankLabel == node.RankLabel),
                    TabletRuleKind.RankExact => mods.Count(m => m.Rank == node.Rank),
                    TabletRuleKind.MinimumCount => mods.Count(m => node.ExactCountRank ? m.Rank == node.Rank : catalog.RankOrder.IndexOf(m.Rank) <= threshold),
                    TabletRuleKind.NoRankBelow => mods.Count(m => catalog.RankOrder.IndexOf(m.Rank) > threshold),
                    _ => 0
                };
                bool matches = node.Kind == TabletRuleKind.MinimumCount ? value.Count >= node.Minimum :
                    node.Kind == TabletRuleKind.NoRankBelow ? value.Count == 0 : value.Count > 0;
                if (node.MinimumRoll is { } minimum && value.Count > 0)
                {
                    var rolls = observation.Mods.Where(m => m?.Modifier != null &&
                        (node.Kind == TabletRuleKind.Modifier ? m.Modifier.Id == node.ModifierId : m.Modifier.RankLabel == node.RankLabel))
                        .Select(m => m.Roll).ToArray();
                    matches = rolls.Any(r => r is { } roll && float.IsFinite(roll) && roll >= minimum);
                    if (!matches && rolls.Any(r => r == null || !float.IsFinite(r.Value)))
                    { value.Matches = null; return value; }
                }
                value.Matches = uncertain && node.Kind == TabletRuleKind.NoRankBelow ? value.Count > 0 ? false : null :
                    uncertain && !matches ? null : matches;
                return value;
            }
            result.Rule = Visit(profile.Rule);
            result.State = uncertain || result.Rule.Matches == null ? TabletFilterState.Uncertain :
                result.Rule.Matches == true ? TabletFilterState.Accepted : TabletFilterState.Rejected;
            result.Action = result.State is TabletFilterState.Uncertain or TabletFilterState.Invalid ? TabletFilterAction.Signal :
                result.State == TabletFilterState.Accepted ? profile.OnMatch : profile.OnMiss;
            return result;
        }

        public static string Export(TabletProfileArchive archive)
        {
            string json = JsonConvert.SerializeObject(archive, Formatting.Indented);
            if (json.Length > 1024 * 1024) throw new FormatException("export-size");
            return json;
        }

        public static TabletProfileArchive Import(string json, TabletMetaCatalog catalog, string expectedType)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024) throw new FormatException("import-size");
            var archive = JsonConvert.DeserializeObject<TabletProfileArchive>(json,
                new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = MaximumDepth * 3 + 10 });
            if (archive == null || archive.SchemaVersion != 1 || archive.TabletType != expectedType || archive.Profiles == null ||
                archive.Profiles.Count > 100 || archive.Profiles.Select(p => p?.Id).Distinct().Count() != archive.Profiles.Count) throw new FormatException("import-schema");
            foreach (var profile in archive.Profiles)
                if (profile?.TabletType != expectedType || Validate(profile, catalog).Length > 0) throw new FormatException("import-rule");
            if (!string.IsNullOrEmpty(archive.ActiveProfileId) && !archive.Profiles.Any(p => p.Id == archive.ActiveProfileId)) throw new FormatException("import-active-profile");
            return archive;
        }
    }
}
