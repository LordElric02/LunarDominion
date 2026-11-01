using System;
using LunarDominion.Core;
using LunarDominion.Research;

namespace LunarDominion.Economy
{
    /// <summary>Money only. Knows nothing about UI, buildings' unlocks, or combat.
    /// All validation lives here so UI can never spend what the player lacks.</summary>
    public class EconomyService
    {
        private readonly GameState _state;
        private readonly BalanceData _balance;

        // Set by GameController after ResearchService is created.
        // Null means no research bonuses (e.g. before wiring).
        public ResearchService Research { get; set; }

        public EconomyService(GameState state, BalanceData balance)
        {
            _state = state;
            _balance = balance;
        }

        public int StorageCap
        {
            get
            {
                int baseCap = _balance.StorageBase + _balance.StoragePerNexusLevel * _state.NexusLevel;
                float mult = Research != null ? Research.StorageMultiplier() : 1f;
                return (int)Math.Round(baseCap * mult);
            }
        }

        public bool CanAfford(ResourceCost cost)
        {
            return _state.Biomass >= cost.Biomass
                && _state.Alloy >= cost.Alloy
                && _state.Helium3 >= cost.Helium3;
        }

        public bool Spend(ResourceCost cost)
        {
            if (!CanAfford(cost)) return false;
            _state.Biomass -= cost.Biomass;
            _state.Alloy -= cost.Alloy;
            _state.Helium3 -= cost.Helium3;
            ClampResources();
            return true;
        }

        public void Earn(ResourceCost amount)
        {
            _state.Biomass += amount.Biomass;
            _state.Alloy += amount.Alloy;
            _state.Helium3 += amount.Helium3;
            ClampResources();
        }

        public void EarnBiomass(int amount) { _state.Biomass += amount; ClampResources(); }
        public void EarnAlloy(int amount) { _state.Alloy += amount; ClampResources(); }
        public void EarnHelium3(int amount) { _state.Helium3 += amount; ClampResources(); }

        public void ClampResources()
        {
            int cap = StorageCap;
            _state.Biomass = Math.Max(0, Math.Min(_state.Biomass, cap));
            _state.Alloy = Math.Max(0, Math.Min(_state.Alloy, cap));
            _state.Helium3 = Math.Max(0, Math.Min(_state.Helium3, cap));
            _state.Energy = Math.Max(0, Math.Min(_state.Energy, _balance.MaxEnergy));
        }

        /// <summary>Upgrade cost to reach targetLevel, from the GDD unified formula.</summary>
        public ResourceCost UpgradeCost(BuildingDefinition def, int targetLevel)
        {
            if (targetLevel <= 1) return ResourceCost.Zero;
            // Nexus high-level Helium-3 is a sampled extra in the GDD, not part of
            // the base formula: 1,000 at 20, 4,000 at 25, 12,000 at 30.
            int extraHelium = 0;
            if (def.IsCommandNexus)
            {
                if (targetLevel == 20) extraHelium = 1000;
                else if (targetLevel == 25) extraHelium = 4000;
                else if (targetLevel == 30) extraHelium = 12000;
            }
            float factor = (float)Math.Pow(_balance.CostGrowth, targetLevel - 2);
            return new ResourceCost(
                (int)Math.Round(def.BaseBiomassCost * factor),
                (int)Math.Round(def.BaseAlloyCost * factor),
                (int)Math.Round(def.BaseHelium3Cost * factor) + extraHelium);
        }

        public float ProductionPerHour(BuildingDefinition def, int level)
        {
            if (level <= 0 || def.BaseProductionPerHour <= 0f) return 0f;
            return def.BaseProductionPerHour * (1f + _balance.ProductionGrowth * (level - 1));
        }
    }
}
