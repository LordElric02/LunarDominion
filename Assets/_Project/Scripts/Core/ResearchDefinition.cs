using LunarDominion.Core;
using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>Stable IDs for the ten research nodes. These IDs are what
    /// the save file stores, so never renumber them.</summary>
    public enum ResearchId
    {
        None = -1,
        HydroponicsEfficiency = 0,
        AlloyRefining = 1,
        HeliumExtraction = 2,
        DeepStorage = 3,
        InfantryTactics = 4,
        RangedOptics = 5,
        MechanizedPlating = 6,
        SalvageProtocols = 7,
        CombatTraining = 8,
        FlawlessDrills = 9
    }

    public enum ResearchBranch { Base, Combat, Expeditions }

    public enum ResearchEffect
    {
        ProductionBiomass,
        ProductionAlloy,
        ProductionHelium,
        StorageCap,
        InfantryStats,
        RangedStats,
        MechanizedStats,
        ExpeditionReward,
        ExpeditionXp,
        FlawlessBonus
    }

    /// <summary>Data for one research node, from the approved Research
    /// proposal (2026-11-01, revised V1). If no assets are assigned,
    /// ResearchCatalog creates these at runtime from the same numbers,
    /// so the scene works out of the box.</summary>
    [CreateAssetMenu(fileName = "NewResearchDefinition", menuName = "Lunar Dominion/Research Definition")]
    public class ResearchDefinition : ScriptableObject
    {
        public ResearchId Id;
        public string DisplayName = "Research";
        [TextArea] public string Description = "";
        public ResearchBranch Branch;
        public int UnlockNexusLevel = 1;
        // Prerequisite node in the same branch, or ResearchId.None.
        public ResearchId PrerequisiteId = ResearchId.None;
        public int PrerequisiteLevel;
        // Base cost to research level 1. Cost to level N =
        // base * CostGrowth^(N-1), so level 5 costs about 2.2x level 1.
        public int BaseBiomassCost;
        public int BaseAlloyCost;
        public int BaseHelium3Cost;
        public ResearchEffect Effect;
        // Bonus per level, as a fraction: 0.03 = +3% per level.
        public float EffectPerLevel;
    }
}
