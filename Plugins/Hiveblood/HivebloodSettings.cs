namespace Hiveblood
{
    using System;
    using System.Numerics;
    using ExileCenter.Plugin;

    public enum HivebloodOverlayAnchor
    {
        /// <summary>Top-left corner of the inventory panel + offset.</summary>
        InventoryTopLeft = 0,

        /// <summary>Bottom of the inventory panel (near the gold line) + offset.</summary>
        InventoryBottomNearGold = 1,

        /// <summary>Fixed screen position (<see cref="OverlayScreenPosition"/>).</summary>
        CustomScreen = 2,
    }

    public sealed class HivebloodSettings : IPSettings
    {
        public const long HivebloodCap = 100_000;

        public bool ShowOnlyWithInventory = true;
        public bool ShowAlways = false;

        public HivebloodOverlayAnchor OverlayAnchor = HivebloodOverlayAnchor.InventoryBottomNearGold;
        public Vector2 OverlayOffset = new(8f, -4f);
        public Vector2 OverlayScreenPosition = new(40f, 120f);

        public float OverlayFontScale = 1.05f;
        public Vector4 TextColor = new(0.78f, 0.45f, 0.95f, 1f);
        public Vector4 ShadowColor = new(0f, 0f, 0f, 0.85f);
        public bool WarnNearCap = true;
        public int WarnThreshold = 95_000;
        public bool ShowSessionGains = true;
        public bool DebugStatusLine = false;
        public bool ShowPositionDummy;

        /// <summary>How often the UI tree is scanned for Hiveblood text (lower = more CPU).</summary>
        public int ScanIntervalMs = 300;

        public long EstimatedAmount;
        public bool HasSyncedOnce;
        public DateTime LastTreeSyncUtc = DateTime.MinValue;
        public long SessionGainSinceSync;
    }
}
