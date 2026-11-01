using System;
using System.Collections.Generic;

namespace LunarDominion.Core
{
    /// <summary>Runtime game state. Plain data — no UnityEngine dependencies
    /// except through SaveService, so services stay testable.</summary>
    [Serializable]
    public class GameState
    {
        public int Biomass;
        public int Alloy;
        public int Helium3;
        public int Energy;

        // Indexed by (int)BuildingId. All buildings start at level 1 once unlocked;
        // locked buildings are stored as 0 and shown as locked in the UI.
        // Research Lab (2026-11-01) is index 7; old saves with 7 entries
        // load with the Lab at 0.
        public int[] BuildingLevels = new int[8];

        // Research (2026-11-01), indexed by (int)ResearchId. One active
        // research at a time: -1 means none in progress.
        public int[] ResearchLevels = new int[10];
        public int ActiveResearchId = -1;
        public int ActiveResearchTargetLevel;
        public string ActiveResearchFinishUtc = "";

        // One entry per recruited unit instance (Phase 3). Units are
        // tracked individually so Phase 4 XP/levels can differ per unit.
        public List<OwnedUnit> Army = new List<OwnedUnit>();

        public string LastEnergyDate = "";
        public string LastSaveTimeUtc = "";
        public float ProductionCarryBiomass;
        public float ProductionCarryAlloy;
        public float ProductionCarryHelium3;

        public int GetLevel(BuildingId id)
        {
            int i = (int)id;
            return i >= 0 && i < BuildingLevels.Length ? BuildingLevels[i] : 0;
        }

        public void SetLevel(BuildingId id, int level)
        {
            BuildingLevels[(int)id] = level;
        }

        public int NexusLevel => GetLevel(BuildingId.CommandNexus);
    }
}
