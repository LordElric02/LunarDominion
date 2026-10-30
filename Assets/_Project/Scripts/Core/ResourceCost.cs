using System;

namespace LunarDominion.Core
{
    /// <summary>An amount of each resource. Used for costs, rewards and balances.
    /// No gameplay rules live here — this is data only.</summary>
    [Serializable]
    public struct ResourceCost
    {
        public int Biomass;
        public int Alloy;
        public int Helium3;

        public ResourceCost(int biomass, int alloy, int helium3)
        {
            Biomass = biomass;
            Alloy = alloy;
            Helium3 = helium3;
        }

        public static ResourceCost Zero => new ResourceCost(0, 0, 0);

        public override string ToString()
        {
            return $"{Biomass} Biomass, {Alloy} Alloy, {Helium3} Helium-3";
        }
    }
}
