using System.Numerics;
using GameHelper.Plugin;
using Newtonsoft.Json;

namespace KikiMeter;

public sealed class KikiMeterSettings : IPSettings
{
    public bool DrawWhenGameInBackground = false;
    public Vector4 DpsTextColor = new(1f, 1f, 0.6f, 1f);

    // Import the old meter once; the original AuraTracker file remains intact.
    internal static KikiMeterSettings Load(string pluginDirectory)
    {
        string path = Path.Join(pluginDirectory, "config", "KikiMeter.settings.json");
        if (!File.Exists(path))
            path = Path.Join(pluginDirectory, "..", "AuraTracker", "config", "AuraTracker.settings.json");
        return File.Exists(path)
            ? JsonConvert.DeserializeObject<KikiMeterSettings>(File.ReadAllText(path)) ?? new()
            : new();
    }

        public bool ShowDamageMeter = true;
        public bool DamageMeterLocked = true;
        public bool DamageMeterCollapsed = false;
        public Vector2 DamageMeterPosition = new(40f, 140f);
        public Vector2 DamageMeterSize = new(460f, 420f);
        public bool DamageMeterSeparateWindow = false;
        // Closing the window only hides it until the next GameHelper start.
        [Newtonsoft.Json.JsonIgnore]
        public bool DamageMeterWindowVisible = true;
        public bool DamageMeterWindowPositionSaved = false;
        public int DamageMeterWindowX, DamageMeterWindowY;
        public int DamageMeterWindowWidth = 540, DamageMeterWindowHeight = 580;
        public bool DamageMeterDetectSkillChanges = true;
        public string DamageMeterTestName = "";
        public DamageMeterReference? DamageMeterSingleReference;
        public DamageMeterReference? DamageMeterMultiReference;
        public List<DamageMeterMap> DamageMeterMaps = new();
        public static readonly int[] DamageMeterEnemySampleSizes = new[] { 10, 50 }
            .Concat(Enumerable.Range(1, 20).Select(n => n * 100)).ToArray();
        public bool DamageMeterMapShowTime = true, DamageMeterMapShowAverage = true;
        public bool DamageMeterMapShowPeak = true, DamageMeterMapShowImpact = true;
        public bool DamageMeterMapShowDamage = true, DamageMeterMapShowEnemies = true;
        public bool DamageMeterMapShowStatus = true;
        [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
        public List<int> DamageMeterMapSampleColumns = new() { 10, 50, 100, 200 };

        public sealed class DamageMeterMap
        {
            public string Id = Guid.NewGuid().ToString("N");
            public string Hash = "", Name = "", Label = "";
            public uint ProcessId;
            public int AreaLevel;
            public DateTime StartedUtc = DateTime.UtcNow;
            public bool Finished, ReadingInterrupted, SkillsChanged;
            public int EnemySampleVersion;
            public bool EnemySamplesPartial;
            public DamageMeterMapStats Overall = new(), Single = new(), Multi = new();
        }

        public sealed class DamageMeterMapStats
        {
            public double Seconds, Peak;
            public long Damage, LargestSample;
            public long LargestImpact;
            public List<DamageMeterEnemy> Enemies = new();
            [Newtonsoft.Json.JsonProperty(ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace)]
            public List<long> EnemySampleDamage = Enumerable.Repeat(0L, DamageMeterEnemySampleSizes.Length).ToList();
            public double Average => Seconds > 0 ? Damage / Seconds : 0;
        }

        public sealed class DamageMeterEnemy
        {
            public uint Id;
            public long Address, Damage;
        }

        public sealed class DamageMeterReference
        {
            public string Name = "";
            public double Average;
            public double Peak;
            public double Seconds;
            public int Targets;
            public bool Controlled;
        }

}
