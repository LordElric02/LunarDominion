using System.Collections.Generic;
using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>Builds the ten approved research definitions at runtime
    /// when the project has no ResearchDefinition assets yet. Costs
    /// and effects match the Research proposal (2026-11-01, revised
    /// V1). Replace with assigned ScriptableObject assets later
    /// without changing any service code.</summary>
    public static class ResearchCatalog
    {
        public const int MaxResearchLevel = 5;

        public static List<ResearchDefinition> CreateDefaultDefinitions()
        {
            var list = new List<ResearchDefinition>();
            // Base branch, in chain order.
            list.Add(Make(ResearchId.HydroponicsEfficiency, "Hydroponics Efficiency",
                "Biomass production +3% per level.", ResearchBranch.Base, 1,
                ResearchId.None, 0, 150, 100, 0, ResearchEffect.ProductionBiomass, 0.03f));
            list.Add(Make(ResearchId.AlloyRefining, "Alloy Refining",
                "Alloy production +3% per level.", ResearchBranch.Base, 1,
                ResearchId.HydroponicsEfficiency, 2, 150, 100, 0, ResearchEffect.ProductionAlloy, 0.03f));
            list.Add(Make(ResearchId.HeliumExtraction, "Helium Extraction",
                "Helium-3 production +3% per level.", ResearchBranch.Base, 4,
                ResearchId.AlloyRefining, 2, 200, 150, 0, ResearchEffect.ProductionHelium, 0.03f));
            list.Add(Make(ResearchId.DeepStorage, "Deep Storage",
                "Storage cap +5% per level.", ResearchBranch.Base, 5,
                ResearchId.HeliumExtraction, 2, 200, 200, 0, ResearchEffect.StorageCap, 0.05f));
            // Combat branch, in chain order.
            list.Add(Make(ResearchId.InfantryTactics, "Infantry Tactics",
                "Infantry HP +2%, ATK +2% per level.", ResearchBranch.Combat, 1,
                ResearchId.None, 0, 200, 100, 0, ResearchEffect.InfantryStats, 0.02f));
            list.Add(Make(ResearchId.RangedOptics, "Ranged Optics",
                "Ranged HP +2%, ATK +2% per level.", ResearchBranch.Combat, 5,
                ResearchId.InfantryTactics, 3, 0, 100, 100, ResearchEffect.RangedStats, 0.02f));
            list.Add(Make(ResearchId.MechanizedPlating, "Mechanized Plating",
                "Mechanized HP +2%, DEF +2% per level.", ResearchBranch.Combat, 8,
                ResearchId.RangedOptics, 3, 0, 200, 100, ResearchEffect.MechanizedStats, 0.02f));
            // Expeditions branch, in chain order.
            list.Add(Make(ResearchId.SalvageProtocols, "Salvage Protocols",
                "Expedition rewards +3% per level.", ResearchBranch.Expeditions, 1,
                ResearchId.None, 0, 200, 150, 0, ResearchEffect.ExpeditionReward, 0.03f));
            list.Add(Make(ResearchId.CombatTraining, "Combat Training",
                "Expedition XP pool +5% per level.", ResearchBranch.Expeditions, 8,
                ResearchId.SalvageProtocols, 3, 200, 200, 0, ResearchEffect.ExpeditionXp, 0.05f));
            list.Add(Make(ResearchId.FlawlessDrills, "Flawless Drills",
                "Flawless XP bonus +5% per level (base 25%, up to 50%).", ResearchBranch.Expeditions, 8,
                ResearchId.CombatTraining, 3, 0, 150, 100, ResearchEffect.FlawlessBonus, 0.05f));
            return list;
        }

        private static ResearchDefinition Make(ResearchId id, string name, string desc,
            ResearchBranch branch, int unlock, ResearchId prereq, int prereqLevel,
            int baseB, int baseA, int baseH, ResearchEffect effect, float perLevel)
        {
            var def = ScriptableObject.CreateInstance<ResearchDefinition>();
            def.Id = id;
            def.DisplayName = name;
            def.Description = desc;
            def.Branch = branch;
            def.UnlockNexusLevel = unlock;
            def.PrerequisiteId = prereq;
            def.PrerequisiteLevel = prereqLevel;
            def.BaseBiomassCost = baseB;
            def.BaseAlloyCost = baseA;
            def.BaseHelium3Cost = baseH;
            def.Effect = effect;
            def.EffectPerLevel = perLevel;
            return def;
        }
    }
}
