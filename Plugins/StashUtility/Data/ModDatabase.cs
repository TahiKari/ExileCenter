using System.Collections.Generic;
using StashUtility.Models;

namespace StashUtility.Data
{
    public static class ModDatabase
    {
        // Stable base IDs tagged runeforged in the PoE2 base-item export (2026-10-07).
        public static readonly HashSet<string> KalguuranGemIds = new(System.StringComparer.Ordinal)
        {
            "SupportGemConcussiveRunes",
            "SkillGemLeylines",
            "SkillGemFrostflameNova",
            "SkillGemHollowShell",
            "SupportGemRunicInfusion",
            "SupportGemOlrothsHubris",
            "SupportGemRuneforgedBlades",
            "SkillGemPoweredByVerisium",
            "SkillGemRefutation",
            "SkillGemRepulsion",
            "SkillGemTriskelionCascade",
            "SkillGemSkyfall",
            "SkillGemDetonateLiving",
            "SkillGemAnimusExchange",
            "SkillGemBitterDead",
            "SkillGemEternalMarch",
            "SkillGemGrimPillars",
            "SkillGemVerisiumManifestation",
            "SkillGemRemnantsOfKalguur",
            "SupportGemRunicExtraction",
            "SkillGemWardboundMinions",
            "SkillGemFragmentsOfThePast",
            "SkillGemConductiveRunes",
            "SkillGemAnimusSplinters",
            "SupportGemScouringFlame",
            "SkillGemRunicReprieve",
            "SupportGemFistOfKalguur",
            "SkillGemVoltaicBarrier",
            "SupportGemHealingRunes",
            "SupportOlrothsConviction",
            "SkillGemExplosiveTransmutation",
            "SkillGemRainOfBlades",
        };

        public static readonly List<WaystoneMod> AllWaystoneMods = new()
        {
             new WaystoneMod("MapMonsterDamageAsFire", "Monsters deal % of Damage as Extra Fire") { Affix = "prefix", MonsterEffectiveness = 16, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterDamageAsCold", "Monsters deal % of Damage as Extra Cold") { Affix = "prefix", ItemRarity = 14, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterDamageAsLightning", "Monsters deal % of Damage as Extra Lightning") { Affix = "prefix", PackSize = 8, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterDamageIncrease", "% increased Monster Damage") { Affix = "prefix", MonsterRarity = 25, WaystoneDropChance = 20 },
             new WaystoneMod("MapMonsterSpeedIncrease", "Monsters have % increased Attack, Cast and Movement Speed") { Affix = "unique", PackSize = 9, WaystoneDropChance = 25 },
             new WaystoneMod("MapMonsterCritIncrease", "Monsters have % increased Critical Hit Chance / Monsters have +% Critical Damage Bonus") { Affix = "prefix", PackSize = 9, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterLifeIncrease", "% more Monster Life") { Affix = "prefix", MonsterRarity = 23, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterElementalResistances", "+% Monster Elemental Resistances") { Affix = "suffix", ItemRarity = 14, WaystoneDropChance = 10 },
             new WaystoneMod("MapMonsterArmoured", "Monsters are Armoured") { Affix = "suffix", MonsterRarity = 18, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterEvasive", "Monsters are Evasive") { Affix = "suffix", PackSize = 6, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterEnergyShield", "Monsters gain % of maximum Life as Extra maximum Energy Shield") { Affix = "suffix", ItemRarity = 13, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterPoisoning", "Monsters have % chance to Poison on Hit") { Affix = "prefix", PackSize = 7, WaystoneDropChance = 10 },
             new WaystoneMod("MapMonsterBleeding", "Monsters have % chance to inflict Bleeding on Hit") { Affix = "prefix", MonsterEffectiveness = 13, WaystoneDropChance = 10 },
             new WaystoneMod("MapMonsterStunAilmentThreshold", "Monsters have % increased Ailment Threshold / Monsters have % increased Stun Threshold") { Affix = "suffix", MonsterEffectiveness = 13, WaystoneDropChance = 10 },
             new WaystoneMod("MapMonsterArmourBreak", "Monsters Break Armour equal to % of Physical Damage dealt") { Affix = "prefix", MonsterRarity = 19, WaystoneDropChance = 10 },
             new WaystoneMod("MapMonsterAccuracy", "Monsters have % increased Accuracy Rating") { Affix = "prefix", PackSize = 7, WaystoneDropChance = 10 },
             new WaystoneMod("MapMonsterDamageAsChaos", "Monsters deal % of Damage as Extra Chaos") { Affix = "prefix", ItemRarity = 15, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterStunBuildup", "Monsters have % increased Stun Buildup") { Affix = "prefix", MonsterEffectiveness = 13, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterElementAilmentChance", "Monster have % increased Elemental Ailment Application") { Affix = "suffix", ItemRarity = 11, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonsterAdditionalProjectiles", "Monsters fire # additional Projectiles") { Affix = "prefix", PackSize = 9, WaystoneDropChance = 25 },
             new WaystoneMod("MapMonsterIncreasedAreaOfEffect", "Monsters have % increased Area of Effect") { Affix = "suffix", WaystoneDropChance = 20 },
             new WaystoneMod("MapPlayerEnfeeble", "Players are periodically Cursed with Enfeeble") { Affix = "suffix", MonsterEffectiveness = 16, WaystoneDropChance = 20 },
             new WaystoneMod("MapPlayerTemporalChains", "Players are periodically Cursed with Temporal Chains") { Affix = "suffix", PackSize = 8, WaystoneDropChance = 20 },
             new WaystoneMod("MapPlayerElementalWeakness", "Players are periodically Cursed with Elemental Weakness") { Affix = "suffix", ItemRarity = 13, WaystoneDropChance = 20 },
             new WaystoneMod("MapSpreadBurningGround", "Area has patches of Ignited Ground") { Affix = "suffix", MonsterEffectiveness = 15, WaystoneDropChance = 15 },
             new WaystoneMod("MapSpreadChilledGround", "Area has patches of Chilled Ground") { Affix = "suffix", ItemRarity = 12, WaystoneDropChance = 15 },
             new WaystoneMod("MapSpreadShockedGround", "Area has patches of Shocked Ground") { Affix = "suffix", PackSize = 7, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonstersElementalPenetration", "Monster Damage Penetrates % Elemental Resistances") { Affix = "prefix", ItemRarity = 16, WaystoneDropChance = 20 },
             new WaystoneMod("MapPlayerMaximumResists", "-% maximum Player Resistances") { Affix = "suffix", PackSize = 10, WaystoneDropChance = 25 },
             new WaystoneMod("MapPlayerFlaskChargeGain", "Players gain % reduced Flask Charges") { Affix = "suffix", PackSize = 7, WaystoneDropChance = 15 },
             new WaystoneMod("MapPlayerRecoveryRate", "Players have % less Recovery Rate of Life and Energy Shield") { Affix = "suffix", ItemRarity = 15, WaystoneDropChance = 20 },
             new WaystoneMod("MapPlayerCooldownRecovery", "Players have % less Cooldown Recovery Rate") { Affix = "suffix", ItemRarity = 12, WaystoneDropChance = 15 },
             new WaystoneMod("MapMonstersBaseSelfCriticalMultiplier", "Monsters take % reduced Extra Damage from Critical Hits") { Affix = "suffix", MonsterRarity = 18, WaystoneDropChance = 10 },
             new WaystoneMod("MapMonstersCurseEffectOnSelf", "% less effect of Curses on Monsters") { Affix = "suffix", ItemRarity = 10, WaystoneDropChance = 10 },
        };

        // Catalogs verified on 2026-10-07 against RePoE's PoE2 mods/mods_by_base exports.
        // Keep configuration IDs stable; match jewels using the actual exported GameModId.
        public static readonly List<TabletMod> AllTabletMods = LoadCatalog<TabletMod>("tablet_mod_ranges.json", "mods");
        public static readonly List<JewelMod> AllJewelMods = LoadCatalog<JewelMod>("jewel_mod_ranges.json");

        public const int TabletFarmPresetVersion = 1;

        // Editable loot/currency farming synthesis for 0.5.5, not an official tier list.
        // Fubgun: mobalytics.gg/poe-2/profile/fubgun/atlas-trees/fubgun-s-atlas-tree-and-strats
        // Ritual: reddit.com/r/PathOfExile2/comments/1uq05ks/
        // Expedition tests: note.com/koni_pu3/n/n4e021aa8d05f
        // Abyss: farmofexile.com/poe2/article/omen-of-light-abyss-farming
        // Breach: reddit.com/r/PathOfExile2/comments/1winul5/
        // Delirium: exile.codex-wiki.com/guides/delirium-got-massively-buffed-in-the-new-patch-poe-2-0-5
        // Temple: reddit.com/r/PathOfExile2/comments/1veqoe5/
        // Less documented affixes are estimates based on their loot/density contribution.
        // D means low farming priority; it remains selected, as requested.
        private static readonly Dictionary<string, string> TabletFarmRanks = new(System.StringComparer.Ordinal)
        {
            ["TowerMonsterEffectiveness"] = "S",
            ["TowerDroppedItemRarityIncrease"] = "A",
            ["TowerPackSizeIncrease"] = "B",
            ["TowerMagicPackIncrease"] = "B",
            ["TowerRarePackIncrease"] = "S",
            ["TowerMonsterRarityIncrease"] = "A",
            ["TowerDroppedGoldIncrease"] = "D",
            ["TowerExperienceGainIncrease"] = "D",
            ["TowerRareChestCount"] = "D",
            ["TowerAdditionalEssence"] = "B",
            ["TowerAdditionalAzmeriWisp"] = "A",
            ["TowerAdditionalExile"] = "A",
            ["TowerAdditionalStoneCircle"] = "C",
            ["TowerMapDroppedMapsIncrease"] = "B",
            ["TowerAdditionalShrineChance"] = "D",
            ["TowerAdditionalShrine"] = "C",
            ["TowerAdditionalStrongboxChance"] = "C",
            ["TowerAdditionalStrongbox"] = "C",
            ["TowerAdditionalEssenceChance"] = "C",
            ["TowerAdditionalSpiritChance"] = "B",
            ["TowerAdditionalExileChance"] = "B",
            ["TowerStoneCircleChance"] = "D",
            ["TowerMapAdditionalModifier"] = "S",
            ["TowerMapAdditionalUniqueMonsterModifier"] = "C",

            ["TowerBreachHivebloodQuantity"] = "S",
            ["TowerBreachWombgiftQuantity"] = "S",
            ["TowerBreachWombgiftLevelChance"] = "A",
            ["TowerBreachBossChance"] = "B",
            ["TowerBreachAdditionalRares"] = "A",
            ["TowerBreachRareMonsterPotency"] = "A",
            ["TowerBreachMonsterQuantity"] = "A",

            ["TowerExpeditionExplosionRadius"] = "C",
            ["TowerExpeditionLogbookIncrease"] = "A",
            ["TowerExpeditionRunicMonsters"] = "A",
            ["TowerExpeditionIncreasedVerisium"] = "A",
            ["TowerExpeditionUnearthedRares"] = "A",
            ["TowerExpeditionAdditionalSentinels"] = "A",
            ["TowerExpeditionIncreasedMonsterRarity"] = "A",
            ["TowerExpeditionFrozenBosses"] = "B",
            ["TowerExpeditionBuriedStrongboxes"] = "C",
            ["TowerExpeditionVaalRemnants"] = "B",
            ["TowerExpeditionChanceForVerisiumRemnant"] = "S",
            ["TowerExpeditionChanceForTwinnedElites"] = "S",
            ["TowerExpedition2RunicModPassoverChance"] = "A",

            ["TowerDeliriumMonsterSplinterIncrease"] = "S",
            ["TowerDeliriumFogDissipationDelayNew"] = "B",
            ["TowerDeliriumFogPersistence"] = "B",
            // 0.5.5 caps monster-pack scaling at 100% deliriousness: this is situational.
            ["TowerDeliriumDifficultyIncrease"] = "B",
            ["TowerDeliriumPackSizeIncrease"] = "A",
            ["TowerDeliriumDoodadsIncrease"] = "S",
            ["TowerDeliriumRareMonsterPause"] = "B",
            ["TowerDeliriumBossChance"] = "A",
            ["TowerDeliriumAdditionalShardsChance"] = "A",

            ["TowerRitualTributeIncrease"] = "A",
            ["TowerRitualRerollCostIncrease"] = "A",
            ["TowerRitualDeferCostIncrease"] = "A",
            ["TowerRitualDeferSpeed"] = "C",
            ["TowerRitualAdditionalReroll"] = "S",
            ["TowerRitualChanceForNoCost"] = "B",
            ["TowerRitualRareMonsters"] = "B",
            ["TowerRitualMagicMonsters"] = "A",
            ["TowerRitualOmenChance"] = "S",

            ["TowerMapBossAdditionalStrongbox"] = "C",
            ["TowerMapBossAdditionalShrine"] = "C",
            ["TowerMapBossAdditionalEssence"] = "B",
            ["TowerMapBossAdditionalSpirit"] = "A",
            ["TowerMapBossWaystoneChance"] = "A",
            ["TowerMapBossExperience"] = "D",
            ["TowerMapBossRarity"] = "S",
            ["TowerMapBossQuantity"] = "S",

            ["TowerAbyssMonsterIncrease"] = "A",
            ["TowerAbyssRareMonsterIncrease"] = "S",
            ["TowerAbyssEffectivenessPerChasm"] = "A",
            ["TowerAbyssDepthsChance"] = "B",
            ["TowerAbyssAdditionalChance"] = "A",
            ["TowerAbyssIncreasedRewards"] = "A",
            ["TowerAbyss4AdditionalChance"] = "S",
            ["TowerAbyssExtraModifiers"] = "S",
            ["TowerAbyssExtraTickets"] = "A",

            ["TowerIncursionPackSize"] = "B",
            ["TowerIncursionExtraPacks"] = "B",
            ["TowerIncursionExtraPacksChance"] = "C",
            ["TowerIncursionSecondaryEncounters"] = "B",
            ["TowerIncursionTokenChance"] = "S",
            ["TowerIncursionBossChance"] = "A",
            ["TowerIncursionRareChestChance"] = "A",
        };

        public static string GetTabletFarmRank(string tabletType, string modId) => (tabletType, modId) switch
        {
            // Ritual favour farming does not scale with general item rarity/effectiveness.
            ("Ritual Tablet", "TowerMonsterEffectiveness") => "B",
            ("Ritual Tablet", "TowerDroppedItemRarityIncrease") => "C",
            ("Ritual Tablet", "TowerRarePackIncrease") => "A",
            ("Ritual Tablet", "TowerMonsterRarityIncrease") => "B",
            ("Ritual Tablet", "TowerMagicPackIncrease") => "C",
            ("Ritual Tablet", "TowerMapAdditionalModifier") => "B",
            ("Expedition Tablet", "TowerPackSizeIncrease") => "C",
            ("Breach Tablet" or "Delirium Tablet" or "Abyss Tablet", "TowerPackSizeIncrease") => "A",
            ("Delirium Tablet", "TowerMagicPackIncrease") => "A",
            // Omen farming mainly scales rare Abyss monsters and their Abyssal modifiers.
            ("Abyss Tablet", "TowerMonsterEffectiveness") => "B",
            ("Abyss Tablet", "TowerMonsterRarityIncrease") => "S",
            ("Abyss Tablet", "TowerAdditionalExile") => "S",
            ("Overseer Tablet" or "Temple Tablet", "TowerMapAdditionalUniqueMonsterModifier") => "A",
            ("Overseer Tablet", "TowerRarePackIncrease") => "A",
            ("Overseer Tablet" or "Temple Tablet", "TowerMapDroppedMapsIncrease") => "A",
            _ => TabletFarmRanks.TryGetValue(modId, out var rank) ? rank : null
        };

        private static List<T> LoadCatalog<T>(string filename, string property = null)
        {
            var assembly = typeof(ModDatabase).Assembly;
            var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(assembly.Location) ?? "", "Data", filename);
            if (System.IO.File.Exists(file))
            {
                try
                {
                    var catalog = ParseCatalog<T>(System.IO.File.ReadAllText(file), property);
                    if (catalog.Count > 0) return catalog;
                }
                catch (System.IO.IOException) { }
                catch (System.UnauthorizedAccessException) { }
                catch (Newtonsoft.Json.JsonException) { }
            }
            // A missing or damaged external file must not silently disable jewel filtering.
            using var stream = assembly.GetManifestResourceStream("StashUtility.Data." + filename);
            if (stream == null) return new List<T>();
            using var reader = new System.IO.StreamReader(stream);
            return ParseCatalog<T>(reader.ReadToEnd(), property);
        }

        private static List<T> ParseCatalog<T>(string json, string property)
        {
            var token = Newtonsoft.Json.Linq.JToken.Parse(json);
            if (property != null) token = token[property];
            return token?.ToObject<List<T>>() ?? new List<T>();
        }

    }
}
