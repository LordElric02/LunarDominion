using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>Data for one building, from the approved GDD §3–§4.
    /// Create assets via Assets > Create > Lunar Dominion > Building Definition.
    /// If no assets are assigned, BuildingCatalog creates these at runtime
    /// from the same GDD numbers, so the scene works out of the box.</summary>
    [CreateAssetMenu(fileName = "NewBuildingDefinition", menuName = "Lunar Dominion/Building Definition")]
    public class BuildingDefinition : ScriptableObject
    {
        public BuildingId Id;
        public string DisplayName = "Building";
        [TextArea] public string Description = "";
        public int UnlockNexusLevel = 1;

        // Base cost to upgrade TO level 2. Cost to level N = base * CostGrowth^(N-2).
        public int BaseBiomassCost;
        public int BaseAlloyCost;
        public int BaseHelium3Cost;

        // Production per hour at level 1. 0 for non-generators.
        // ProducedResource: 0 = none, 1 = Biomass, 2 = Alloy, 3 = Helium-3.
        public int ProducedResource;
        public float BaseProductionPerHour;

        public bool IsCommandNexus => Id == BuildingId.CommandNexus;
    }
}
