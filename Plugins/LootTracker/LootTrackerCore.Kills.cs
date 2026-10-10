namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using GameHelper;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;

    public sealed partial class LootTrackerCore
    {
        // Per-monster bookkeeping for the kill tally. A reference type on purpose: an entry is updated
        // in place after a single dictionary probe, with no value-type write-back on each pass.
        private sealed class MonsterTally
        {
            public int RarityIndex;   // 0..3 → Normal / Magic / Rare / Unique
            public bool SeenAlive;    // observed alive at least once — guards against counting corpses
            public bool Tallied;      // already folded into the run's kill counts
        }

        // entity id → its tally. Lives only for the active map instance (cleared on every (re)entry).
        private readonly Dictionary<uint, MonsterTally> monsterTallies = new();
        private DateTime nextKillScanUtc = DateTime.MinValue;

        private void ResetKillTally() => this.monsterTallies.Clear();

        // Tally monster deaths for the active run, classified by rarity. Polled on a throttle rather
        // than every frame — a kill is a rare event and the awake-entity walk isn't free. Death is taken
        // straight from the core-computed EntityState (which already folds the is_dead stat / life
        // component), so no component read is needed to detect it. The magic-properties component is read
        // only the first time an entity is seen (to fix its rarity); entries already counted are skipped
        // with a bare dictionary hit.
        private void ScanKills()
        {
            // Kills still count after objective completion while the player remains in the map.
            if (this.current == null || !this.onMapArea)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now < this.nextKillScanUtc)
            {
                return;
            }

            this.nextKillScanUtc = now.AddMilliseconds(150);

            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            foreach (var pair in area.AwakeEntities)
            {
                var entity = pair.Value;
                if (!entity.IsValid || entity.EntityType != EntityTypes.Monster)
                {
                    continue;
                }

                bool dead = entity.EntityState == EntityStates.Useless;

                if (this.monsterTallies.TryGetValue(entity.Id, out var tally))
                {
                    if (tally.Tallied)
                    {
                        continue;
                    }

                    if (!dead)
                    {
                        tally.SeenAlive = true;
                    }
                    else if (tally.SeenAlive)
                    {
                        this.current.Kills[tally.RarityIndex]++;
                        tally.Tallied = true;
                    }

                    continue;
                }

                // First sighting: ignore friendly/hidden minions, then pin down the rarity once.
                if (entity.EntityState is EntityStates.MonsterFriendly or EntityStates.PinnacleBossHidden)
                {
                    continue;
                }

                if (!entity.TryGetComponent<ObjectMagicProperties>(out var omp, true))
                {
                    continue;
                }

                int idx = (int)omp.Rarity;
                if (idx < 0 || idx > 3)
                {
                    continue;
                }

                this.monsterTallies[entity.Id] = new MonsterTally
                {
                    RarityIndex = idx,
                    SeenAlive = !dead,
                };
            }
        }
    }
}
