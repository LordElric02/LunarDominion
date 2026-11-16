using System.Collections.Generic;
using LunarDominion.Core;
using UnityEngine;

namespace LunarDominion.Expeditions
{
    /// <summary>Data for one expedition tier, from GDD §5–§6.
    /// Create assets via Assets > Create > Lunar Dominion > Encounter Definition.
    /// If no assets are assigned, EncounterCatalog creates these at runtime
    /// from the same GDD numbers.</summary>
    [CreateAssetMenu(fileName = "NewEncounterDefinition", menuName = "Lunar Dominion/Encounter Definition")]
    public class EncounterDefinition : ScriptableObject
    {
        public int Tier = 1;
        /// <summary>True only for the A.R.E.S. boss encounters.
        /// Camp tiers run 1-6 since 2026-11-16, so Tier == 4 no
        /// longer means "boss" — anything that means the boss
        /// (the hero XP rate, the lair lookup) keys on this flag
        /// instead.</summary>
        public bool IsBoss;
        public string DisplayName = "Encounter";
        public int RequiredNexusLevel = 1;
        public int EnergyCost = 10;
        public ResourceCost Reward;
        public int XpPool;
        public List<EnemySpawn> Spawns = new List<EnemySpawn>();
    }
}
