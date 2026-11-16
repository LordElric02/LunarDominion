using System;
using System.Collections.Generic;
using LunarDominion.Core;
using UnityEngine;

namespace LunarDominion.Expeditions
{
    /// <summary>Builds the three GDD §5 encounters plus the War Robot
    /// boss (2026-11-09) at runtime when the project has no
    /// EncounterDefinition assets yet.</summary>
    public static class EncounterCatalog
    {
        // Boss ladder (2026-11-10 redesign): 15 levels of A.R.E.S.-09.
        // Defeating a level unlocks the next; the boss never vanishes.
        // Level 1 is the entry fight (HP 5000 / ATK 120 / DEF 50);
        // each level compounds by the growth constants below, energy
        // +15, reward and XP pool x level.
        //
        // Retuned 2026-11-15. The 2026-11-10 crank (HP x1.30 /
        // ATK x1.18 / DEF x1.12) assumed future unit tiers would
        // eventually reach the top levels, but boss fights are
        // heroes-only for good (Patrick's 2026-11-12 decision), and
        // hero damage only grows x2.52 over 20 levels — so L15
        // (196,869 HP before Nexus scaling) was mathematically
        // unbeatable: a maxed team deals ~1,050/round against the
        // 20-round cap, and the boss's ATK one-shot maxed heroes.
        // The new curve grows HP/DEF gently and ATK faster, so
        // higher levels get deadlier rather than spongier. Tuned
        // with a simulator port of the real fight rules: a maxed
        // 4-hero team (Ward included) wins L15 in ~19 rounds at the
        // capped Nexus scale (see BossLairView.BossScale), the
        // 3-hero team times out, and mid-ladder frontier fights
        // land in the teens of rounds.
        public const int MaxBossLevel = 15;
        public const float BossHpGrowth = 1.06f;
        public const float BossAtkGrowth = 1.09f;
        public const float BossDefGrowth = 1.06f;

        /// <summary>War Robot encounter for a boss level (1-15, clamped).
        /// Level 1 = the entry boss; higher levels scale up steeply.</summary>
        public static EncounterDefinition CreateBossEncounter(int level)
        {
            level = Math.Max(1, Math.Min(level, MaxBossLevel));
            var boss = ScriptableObject.CreateInstance<EncounterDefinition>();
            boss.Tier = 4;
            boss.IsBoss = true;
            boss.DisplayName = $"A.R.E.S.-09 Lv {level}";
            boss.RequiredNexusLevel = 10;
            boss.EnergyCost = 50 + 15 * (level - 1);
            boss.Reward = new ResourceCost(1500 * level, 1200 * level, 400 * level);
            boss.XpPool = 1000 * level;
            int hp = (int)Math.Round(5000 * Math.Pow(BossHpGrowth, level - 1));
            int atk = (int)Math.Round(120 * Math.Pow(BossAtkGrowth, level - 1));
            int def = (int)Math.Round(50 * Math.Pow(BossDefGrowth, level - 1));
            boss.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "A.R.E.S.-09", Class = UnitClass.Mechanized, Health = hp, Attack = atk, Defense = def, Count = 1 }
            };
            return boss;
        }

        public static List<EncounterDefinition> CreateDefaultEncounters()
        {
            var list = new List<EncounterDefinition>();

            var tier1 = ScriptableObject.CreateInstance<EncounterDefinition>();
            tier1.Tier = 1;
            tier1.DisplayName = "Spore Crawler Pack";
            tier1.RequiredNexusLevel = 1;
            tier1.EnergyCost = 10;
            tier1.Reward = new ResourceCost(80, 50, 10);
            tier1.XpPool = 40;
            tier1.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "Spore Crawler", Class = UnitClass.Infantry, Health = 80, Attack = 12, Defense = 5, Count = 4 }
            };
            list.Add(tier1);

            var tier2 = ScriptableObject.CreateInstance<EncounterDefinition>();
            tier2.Tier = 2;
            tier2.DisplayName = "Void Stalker Pack";
            tier2.RequiredNexusLevel = 8;
            tier2.EnergyCost = 20;
            tier2.Reward = new ResourceCost(200, 150, 50);
            tier2.XpPool = 120;
            tier2.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "Void Stalker", Class = UnitClass.Ranged, Health = 120, Attack = 24, Defense = 8, Count = 5 }
            };
            list.Add(tier2);

            var tier3 = ScriptableObject.CreateInstance<EncounterDefinition>();
            tier3.Tier = 3;
            tier3.DisplayName = "Brood Tyrant";
            tier3.RequiredNexusLevel = 15;
            tier3.EnergyCost = 30;
            tier3.Reward = new ResourceCost(500, 400, 150);
            tier3.XpPool = 300;
            tier3.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "Brood Tyrant", Class = UnitClass.Mechanized, Health = 600, Attack = 40, Defense = 25, Count = 1 },
                new EnemySpawn { DisplayName = "Brood", Class = UnitClass.Mechanized, Health = 150, Attack = 20, Defense = 10, Count = 3 }
            };
            list.Add(tier3);

            // Camp tiers 4-6 (2026-11-16, Patrick: armies now run
            // to Tier 6, so the camp field does too). Stats sit on
            // the T1-T3 curve — calibrated in the camp sim against
            // full same-tier armies: camps are farm content that a
            // same-tier army beats with room to spare, exactly like
            // T1-T3 today; the boss ladder is the wall.
            // RequiredNexusLevel is the EnemyScale gate and drives
            // the "far stronger than your army" warning.
            var tier4 = ScriptableObject.CreateInstance<EncounterDefinition>();
            tier4.Tier = 4;
            tier4.DisplayName = "Ashen Ravager Pack";
            tier4.RequiredNexusLevel = 20;
            tier4.EnergyCost = 40;
            tier4.Reward = new ResourceCost(900, 750, 300);
            tier4.XpPool = 700;
            tier4.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "Ashen Ravager", Class = UnitClass.Infantry, Health = 950, Attack = 58, Defense = 36, Count = 1 },
                new EnemySpawn { DisplayName = "Ash Skirmisher", Class = UnitClass.Ranged, Health = 260, Attack = 34, Defense = 14, Count = 3 }
            };
            list.Add(tier4);

            var tier5 = ScriptableObject.CreateInstance<EncounterDefinition>();
            tier5.Tier = 5;
            tier5.DisplayName = "Dread Stalker Pack";
            tier5.RequiredNexusLevel = 25;
            tier5.EnergyCost = 50;
            tier5.Reward = new ResourceCost(1600, 1300, 550);
            tier5.XpPool = 1500;
            tier5.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "Dread Stalker", Class = UnitClass.Ranged, Health = 1400, Attack = 78, Defense = 48, Count = 1 },
                new EnemySpawn { DisplayName = "Dread Mauler", Class = UnitClass.Mechanized, Health = 380, Attack = 40, Defense = 22, Count = 4 }
            };
            list.Add(tier5);

            var tier6 = ScriptableObject.CreateInstance<EncounterDefinition>();
            tier6.Tier = 6;
            tier6.DisplayName = "Void Tyrant";
            tier6.RequiredNexusLevel = 28;
            tier6.EnergyCost = 60;
            tier6.Reward = new ResourceCost(2800, 2300, 1000);
            tier6.XpPool = 3000;
            tier6.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "Void Tyrant", Class = UnitClass.Mechanized, Health = 2100, Attack = 105, Defense = 65, Count = 1 },
                new EnemySpawn { DisplayName = "Void Brood", Class = UnitClass.Mechanized, Health = 550, Attack = 55, Defense = 30, Count = 4 }
            };
            list.Add(tier6);

            // Boss — War Robot (2026-11-09, Patrick's first boss; ladder
            // 2026-11-10): a single giant Mechanized enemy in a lair on
            // the map (not a camp). The IsBoss flag (not the Tier)
            // keeps it out of the camp tier loops now that camp tiers
            // run to 6. Infantry counters it (Mechanized is weak to
            // Infantry). Single-phase: one big enemy, no special attacks
            // yet — those come later. The default list carries Level 1;
            // the lair view builds the level it fights on demand.
            list.Add(CreateBossEncounter(1));

            return list;
        }
    }
}
