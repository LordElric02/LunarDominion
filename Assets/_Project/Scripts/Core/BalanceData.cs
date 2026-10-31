using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>All tunable balance numbers from the GDD, in one place.
    /// Create via Assets > Create > Lunar Dominion > Balance Data.
    /// Services read from this asset; they never hard-code these numbers.
    /// If no asset is assigned, BalanceData.Default() returns the GDD values.</summary>
    [CreateAssetMenu(fileName = "BalanceData", menuName = "Lunar Dominion/Balance Data")]
    public class BalanceData : ScriptableObject
    {
        [Header("Progression")]
        public int MaxBuildingLevel = 30;
        public int MaxUnitLevel = 20;
        public int MaxSquadSize = 8;
        public float CostGrowth = 1.22f;
        public float ProductionGrowth = 0.22f;

        [Header("Prototype flags (GDD §7, rule 5)")]
        public bool PrototypeInstantBuild = true;

        [Header("Energy (GDD §6)")]
        // Proof-of-concept early-game abundance (2026-10-31, Patrick's call):
        // was 100, then 1500. Refill at local midnight now gives 5000.
        public int MaxEnergy = 5000;

        [Header("Production (GDD §4)")]
        public float OfflineProductionCapHours = 8f;

        [Header("Storage (GDD §4, provisional)")]
        public int StorageBase = 500;
        public int StoragePerNexusLevel = 250;

        [Header("Starting resources (GDD §4, provisional)")]
        public int StartingBiomass = 500;
        public int StartingAlloy = 400;
        public int StartingHelium3 = 150;
        // Proof-of-concept early-game abundance (2026-10-31, Patrick's call):
        // was 100, then 1500. A new game starts with 5000.
        public int StartingEnergy = 5000;

        public static BalanceData Default()
        {
            var data = CreateInstance<BalanceData>();
            return data;
        }
    }
}
