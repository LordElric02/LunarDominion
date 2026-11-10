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

            // Boss — War Robot (2026-11-09, Patrick's first boss): a
            // single giant Mechanized enemy in a lair on the map (not a
            // camp — see MapExcursionService for the lair flow). Tier 4
            // keeps it out of the camp tier 1-3 loops. Infantry counters
            // it (Mechanized is weak to Infantry). Single-phase: one big
            // enemy, no special attacks yet — those come later.
            var boss = ScriptableObject.CreateInstance<EncounterDefinition>();
            boss.Tier = 4;
            boss.DisplayName = "War Robot";
            boss.RequiredNexusLevel = 10;
            boss.EnergyCost = 50;
            boss.Reward = new ResourceCost(1500, 1200, 400);
            boss.XpPool = 1000;
            boss.Spawns = new List<EnemySpawn>
            {
                new EnemySpawn { DisplayName = "War Robot", Class = UnitClass.Mechanized, Health = 1500, Attack = 60, Defense = 30, Count = 1 }
            };
            list.Add(boss);

            return list;
        }
    }
}
