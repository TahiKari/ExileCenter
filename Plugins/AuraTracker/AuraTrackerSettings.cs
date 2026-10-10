using System.Numerics;
using System.Linq;
using GameHelper.Plugin;
using GameHelper.RemoteEnums; // Rarity

namespace AuraTracker
{
    public sealed class AuraTrackerSettings : IPSettings
    {
        // General / filters
        public bool DrawWhenGameInBackground = false;

        public float ScreenRangePx = 1800;    // “nearby” in screen space
        public int MaxEnemies = 5;

        // Optional filters (e.g. tamable beasts + specific auras for Spirit Walker builds)
        public bool OnlyBeasts = false;
        public bool EnableAuraFilter = false;
        public bool AuraFilterMatchAll = false; // false = match any listed aura; true = require all
        public List<string> AuraFilters = new();

        public int ChipColorSeed = 0;

        // Show this rarity and above
        public Rarity MinRarityToShow = Rarity.Magic;

        // Left list anchor & spacing (requested defaults)
        public Vector2 LeftAnchor = new(350f, 250f);
        public float EntrySpacing = 16f;
        public float BarToBuffSpacing = 3f;
        public float MaxListHeight = 0f; // 0 = use overlay height

        // Content width: fixed (no auto). Default 300.
        public float PanelWidth = 300f;

        // Panel visuals
        public bool ShowPanelBackground = true;
        public Vector4 PanelBg = new(0f, 0f, 0f, 0.35f);
        public Vector4 PanelBorder = new(0f, 0f, 0f, 0.8f);
        public Vector2 PanelPadding = new(8f, 8f);
        public float PanelCornerRadius = 6f;
        public float PanelRightSafeMargin = 24f;

        // Fancy visuals
        public bool FancyPanelShadow = true;
        public bool FancyRarityStripe = true;
        public bool FancyBarGloss = true;
        public bool FancyBarInnerBorder = true;
        public bool FancyEsDivider = true;
        public bool FancyChipGloss = true;

        // Style amounts
        public float PanelShadowSize = 10f;   // px
        public float PanelShadowAlpha = 0.25f;

        public float ChipCornerRadius = 6f;
        public float ChipShadowAlpha = 0.25f;
        public float ChipGlossAlpha = 0.25f;

        public float BarCornerRadius = 5f;
        public float BarInnerBorderAlpha = 0.35f;
        public float EsDividerAlpha = 0.75f;

        // Bar (width always follows panel width; Y = height)
        public Vector4 BarBg = new(0f, 0f, 0f, 0.5f);
        public Vector4 BarHpFill = new(1f, 0.35f, 0.2f, 1f);   // HP segment color
        public Vector4 BarEsFill = new(0f, 1f, 1f, 1f);        // ES segment color
        public Vector2 BarSize = new(180f, 18f);             // slightly taller by default
        public bool ShowHpPercent = false;                  // toggle percent vs absolute

        // Buff chips
        public float BuffPad = 2f;
        public int MaxBuffsPerEnemy = 12;
        public float BuffBgAlpha = 0.35f;
        public float BuffTextScale = 1.0f;
        public bool ShowDurations = true; // finite only; indefinite/infinite hidden

        // DPS overlay
        public bool ShowDps = true;
        public bool ShowOverallDps = false;
        public float DpsSmoothingSeconds = 0.7f;
        public Vector4 DpsTextColor = new(1f, 1f, 0.6f, 1f); // soft yellow-white
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

        public sealed class ChipColorOverride
        {
            // Match against the chip base text
            public string Match = "";
            // RGB is respected; alpha is ignored so global BuffBgAlpha still applies
            public Vector4 Color = new(1f, 1f, 1f, 1f);
        }

        // User-defined overrides
        public List<ChipColorOverride> ChipOverrides = new();

    }
}
