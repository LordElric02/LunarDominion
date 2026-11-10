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
        // Research Lab (2026-11-01) is index 7; Hospital (2026-11-09) is
        // index 8. Old saves with fewer entries load the missing ones at 0.
        public int[] BuildingLevels = new int[9];

        // Research (2026-11-01), indexed by (int)ResearchId. One active
        // research at a time: -1 means none in progress.
        public int[] ResearchLevels = new int[10];
        public int ActiveResearchId = -1;
        public int ActiveResearchTargetLevel;
        public string ActiveResearchFinishUtc = "";

        // One entry per recruited unit instance (Phase 3). Units are
        // tracked individually so Phase 4 XP/levels can differ per unit.
        public List<OwnedUnit> Army = new List<OwnedUnit>();

        // Hospital (2026-11-09): units that fell in battle are injured,
        // not dead — they move here from the Army and cannot fight until
        // healed in the Hospital (see HospitalService). They keep their
        // level and XP. Old saves load with an empty list. Future: a
        // permadeath encounter type (e.g. attacking a base) will remove
        // its casualties permanently instead of injuring them — that
        // branch lives where casualties are applied (ExpeditionService).
        public List<OwnedUnit> Injured = new List<OwnedUnit>();

        public string LastEnergyDate = "";
        public string LastSaveTimeUtc = "";

        // Map excursions (2026-11-09): one regen timestamp per resource
        // node (9 nodes, in ExteriorView NodeDefs order, indices 0-8).
        // "" = available. UTC round-trip format, same as
        // ActiveResearchFinishUtc. Old saves load with all "" (available).
        public string[] NodeRegenUtc = new string[9];

        // Enemy camps (2026-11-09): a camp defeated in battle vanishes,
        // and another of the same tier respawns elsewhere after
        // CampRespawnHours. One respawn timestamp per tier (index
        // tier-1); "" = the camp is active. One spot index per tier
        // (index tier-1) into ExteriorView.CampSpots; 0 = the original
        // position. Old saves load with all "" / 0 (active at the
        // original spots).
        public string[] CampRespawnUtc = new string[3];
        public int[] CampSpotIndex = new int[3];

        // Boss lair (2026-11-09): the War Robot boss. One respawn
        // timestamp; "" = the boss is active at its lair. When the
        // boss is defeated the lair vanishes, and it respawns at the
        // SAME spot after the timer (lairs do not move, unlike camps).
        // Old saves load with "" (active).
        public string BossRespawnUtc = "";

        // Node occupation (2026-11-09): the army farms a node for a
        // real-time duration (120 min / troops sent) instead of
        // collecting instantly. -1 = no occupation in progress.
        // Kind is the resource (0 Biomass, 1 Alloy, 2 Helium-3 — the
        // MapPointKind order without Camp). End is UTC round-trip.
        // Persisted so an occupation survives an app restart; if the
        // end time passes while the app is closed, the harvest is
        // granted on the next load.
        public int OccupiedNodeIndex = -1;
        public string OccupationEndUtc = "";
        public int OccupationAmount;
        public int OccupationKind;
        public int OccupationTroops;
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
