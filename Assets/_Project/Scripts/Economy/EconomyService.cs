using System;
using System.Collections.Generic;
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

        // Set by GameController after construction (same pattern as
        // Research). Null means no Nexus definition wired: the cap falls
        // back to the plain linear formula.
        public BuildingDefinition NexusDefinition { get; set; }

        // Set by GameController after construction (same pattern as
        // NexusDefinition). Null means no building-upgrade floor: the
        // cap is the plain formula plus any Nexus floor.
        public List<BuildingDefinition> BuildingDefinitions { get; set; }

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
                // The > 0 guard keeps the floors working if a BalanceData
                // asset predates this field and deserializes it as 0.
                float factor = _balance.StorageCapNexusCostFactor > 0f
                    ? _balance.StorageCapNexusCostFactor : 1.5f;
                // Storage-cap floor (2026-11-05, soft-lock fix): the linear
                // cap grows slower than Nexus costs (x1.22 per level), so
                // at Nexus 17 the cap (4750) was already below the Nexus 18
                // cost (4817 Biomass) and no amount of winning could save
                // up for it. Keep the cap at or above the next Nexus
                // upgrade's Biomass cost times StorageCapNexusCostFactor, so
                // the next Nexus is always affordable. Biomass is the
                // binding resource (Nexus base 200B vs 160A, Helium only as
                // a sampled extra at 20/25/30), so flooring on Biomass
                // covers Alloy and Helium-3 too. No floor past max level.
                if (NexusDefinition != null && _state.NexusLevel < _balance.MaxBuildingLevel)
                {
                    int nextNexusBiomass = UpgradeCost(NexusDefinition, _state.NexusLevel + 1).Biomass;
                    int floor = (int)Math.Ceiling(nextNexusBiomass * factor);
                    if (floor > baseCap) baseCap = floor;
                }
                // Building-upgrade floor (2026-11-15, Nexus-30 trap fix):
                // the Nexus floor above switches off at max Nexus, where
                // the plain cap (8,000) sat far below a level-30 barracks
                // upgrade (up to 65,466 Alloy) — taking Nexus 30 before
                // maxing the barracks froze them mid-levels forever, with
                // tiers 4-6 locked behind barracks 25/28/30. Floor the
                // cap at the same factor over the priciest next upgrade
                // among all buildings, so any building's next step is
                // always saveable. Below max Nexus the Nexus floor
                // almost always exceeds this; the one exception — a
                // building sitting at the Nexus cap — just lets the
                // player pre-save for its next step, which is the
                // floor's purpose. At max Nexus the floor ratchets up
                // as buildings level.
                if (BuildingDefinitions != null)
                {
                    int priciest = 0;
                    foreach (var def in BuildingDefinitions)
                    {
                        int lvl = _state.GetLevel(def.Id);
                        if (lvl >= _balance.MaxBuildingLevel) continue;
                        var c = UpgradeCost(def, lvl + 1);
                        int m = Math.Max(c.Biomass, Math.Max(c.Alloy, c.Helium3));
                        if (m > priciest) priciest = m;
                    }
                    int floor = (int)Math.Ceiling(priciest * factor);
                    if (floor > baseCap) baseCap = floor;
                }
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
