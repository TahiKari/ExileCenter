namespace StashUtility.Models
{
    public class TabletMod
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Affix { get; set; } = "prefix";

        public float MinRoll { get; set; } = 0f;
        public float MaxRoll { get; set; } = 0f;
        public int RollValueIndex { get; set; }
        public float RollDivisor { get; set; } = 1f;
        public string[] TabletTypes { get; set; } = System.Array.Empty<string>();

        public TabletMod(string id, string name, string affix = "prefix")
        {
            Id = id;
            Name = name;
            Affix = affix;
        }
    }

    public readonly record struct TabletClassification(
        string Rank, int RankIndex, string RankLabel, System.Numerics.Vector4 RankColor, int SortScore)
    {
        public bool IsRanked => RankIndex > 0;
    }
}
