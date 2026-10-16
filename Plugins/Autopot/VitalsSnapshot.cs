using ExileCenter.RemoteObjects.Components;
using GameOffsets.Objects.Components;

namespace Autopot
{
    internal readonly struct VitalsSnapshot
    {
        public readonly int HpPercent;
        public readonly int EsPercent;
        public readonly int MpPercent;
        public readonly int HybridPercent;
        public readonly bool HasEnergyShield;
        public readonly bool Valid;

        public VitalsSnapshot(int hp, int es, int mp, int hybrid, bool hasEnergyShield, bool valid)
        {
            HpPercent = hp;
            EsPercent = es;
            MpPercent = mp;
            HybridPercent = hybrid;
            HasEnergyShield = hasEnergyShield;
            Valid = valid;
        }

        public static VitalsSnapshot FromLife(Life life)
        {
            if (life == null)
                return new VitalsSnapshot(0, 0, 0, 0, false, false);

            int hp = life.Health.CurrentInPercent();
            int es = life.EnergyShield.CurrentInPercent();
            int mp = life.Mana.CurrentInPercent();
            int hybrid = EffectiveHybridPercent(life.Health, life.EnergyShield);
            bool hasEs = life.EnergyShield.Total > 0;
            return new VitalsSnapshot(hp, es, mp, hybrid, hasEs, true);
        }

        private static int EffectiveHybridPercent(VitalStruct health, VitalStruct es)
        {
            int cur = health.Current + es.Current;
            int max = health.Unreserved + es.Unreserved;
            if (max <= 0)
                return 0;
            return (int)System.Math.Round(100.0 * cur / max);
        }
    }
}
