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

        // The player's chosen name (2026-11-24, ahead of the
        // Firebase account work — this becomes the account
        // display name). Empty until the player sets one from
        // the TopBar; EffectiveUsername is what the UI shows.
        public string Username = "";

        public string EffectiveUsername =>
            string.IsNullOrWhiteSpace(Username) ? "Commander" : Username.Trim();

        // Save lineage ID (2026-11-26): a GUID minted once when
        // a game is created (or when a pre-ID save first loads)
        // and carried in every save of that game forever. The
        // cloud merge safeguard compares IDs, not names, so
        // renaming the player no longer reads as a different
        // game; two games that merely share a name (two fresh
        // "Commander" starts) still read as different games.
        public string SaveId = "";

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

        // Heroes (2026-11-10): named characters (Reyes, Vega, Okafor),
        // one of each, max 3. They live here, separate from the Army —
        // they fight bosses in the lair view, and march with the army
        // in map squads (2026-11-10) where they gain XP from camp
        // fights. They keep their level and XP like army units. Old
        // saves load with an empty list. Injured heroes go to the
        // Injured list (shared with army units) and heal back here,
        // not to the Army.
        public List<OwnedUnit> Heroes = new List<OwnedUnit>();

        public string LastEnergyDate = "";
        public string LastSaveTimeUtc = "";

        // Map excursions (2026-11-09): one regen timestamp per resource
        // node (18 nodes, in ExteriorView NodeDefs order, indices 0-17).
        // "" = available. UTC round-trip format, same as
        // ActiveResearchFinishUtc. Old saves load with all "" (available).
        public string[] NodeRegenUtc = new string[18];

        // Resource node levels (2026-11-16): NodeLevel[i] is node
        // i's current level (1-6; 0 = never rolled). Rolled fresh
        // by Nexus level whenever the node regenerates after a
        // harvest (MapExcursionService.RollNodeLevel), so the node
        // field grows with the player like the camp field. The
        // yield multiplier lives in ExteriorView (the view prices
        // the node's Amount from the mirrored level).
        public int[] NodeLevel = new int[18];

        // Enemy camp field (2026-11-10, replaces the 3-camp system):
        // 36 camp slots spread across the map. CampSlotTier[i] is the
        // tier of the camp in slot i (0 = empty, waiting to respawn);
        // CampSlotRespawnUtc[i] is when an empty slot refills ("" =
        // not waiting). A slot's tier is rolled fresh on every spawn,
        // weighted by Nexus level (see MapExcursionService), so the
        // field grows with the player. Old saves load with all 0/""
        // and the whole field is populated on first sync.
        public int[] CampSlotTier = new int[36];
        public string[] CampSlotRespawnUtc = new string[36];

        // Boss lair (2026-11-09, redesigned 2026-11-10): the War Robot
        // boss. BossLevelDefeated = the highest boss level beaten
        // (0 = none yet, up to MaxBossLevel 15). Defeating a level
        // unlocks the next; the boss NEVER vanishes now (2026-11-10) —
        // the player can fight any unlocked level at will, energy
        // permitting. BossRespawnUtc is a leftover from the old
        // vanish/respawn design and is no longer used (always "").
        public string BossRespawnUtc = "";
        public int BossLevelDefeated = 0;

        // Leaderboards (2026-11-24): Robot Lair Damage career
        // totals. LairDamageScore is the ranked number; the two
        // damage totals are its breakdown. Points per fight =
        // (boss damage + drone damage) x boss level — see
        // LeaderboardService.
        public int LairDamageScore;
        public int LairBossDamage;
        public int LairDroneDamage;

        // Node occupation (2026-11-09; per army slot 2026-11-11): an
        // army farms a node for a real-time duration (120 min / troops
        // sent) instead of collecting instantly. Arrays are per army
        // slot (0 = Army 1, 1 = Army 2 — two armies, gated at Nexus
        // 10). -1 = that army is not farming. Kind is the resource
        // (0 Biomass, 1 Alloy, 2 Helium-3 — the MapPointKind order
        // without Camp). End is UTC round-trip. Persisted so an
        // occupation survives an app restart; if the end time passes
        // while the app is closed, the harvest is granted on the
        // next load.
        public int[] OccupiedNodeIndex = { -1, -1, -1 };
        public string[] OccupationEndUtc = { "", "", "" };
        public int[] OccupationAmount = new int[MaxArmies];
        public int[] OccupationKind = new int[MaxArmies];
        public int[] OccupationTroops = new int[MaxArmies];

        // March record (2026-11-26, Patrick): an army sent out
        // keeps travelling while the app is closed, so each leg
        // of an excursion is persisted as it starts — phase
        // (0 = home, 1 = outbound, 2 = returning), the target
        // point (MapPointKind + Index + camp tier at send time),
        // the leg's endpoints (XZ; Y is terrain), its depart /
        // arrive UTC, and the squad as unit serials (the roster
        // snapshot StartExcursion took, in save-stable form).
        // On load, MapExcursionService.RestoreMarches replays
        // the record against the clock: still travelling →
        // resume mid-route; arrived while closed → the fight
        // or occupation resolves with its true times, chaining
        // into the return leg or home as needed.
        public int[] MarchPhase = new int[MaxArmies];
        public int[] MarchTargetKind = new int[MaxArmies];
        public int[] MarchTargetIndex = { -1, -1, -1 };
        public int[] MarchTargetTier = new int[MaxArmies];
        public float[] MarchFromX = new float[MaxArmies];
        public float[] MarchFromZ = new float[MaxArmies];
        public float[] MarchToX = new float[MaxArmies];
        public float[] MarchToZ = new float[MaxArmies];
        public string[] MarchDepartUtc = { "", "", "" };
        public string[] MarchArriveUtc = { "", "", "" };
        public List<int>[] MarchSquadSerials = new List<int>[MaxArmies];

        // Standing armies (2026-11-12, Patrick — replaces the
        // send-time squads): up to MaxArmies standing armies are
        // built in the Army tab — a hero pick plus assigned
        // troops — and the player picks which army to send from
        // the map. The count is data-driven (gates array) so
        // more armies can be added later by extending the
        // arrays; MaxArmies is the current cap.
        public const int MaxArmies = 3;
        public static readonly int[] ArmyNexusGates = { 1, 10, 20 };
        // Army size by Nexus (2026-11-16, Patrick — the big-armies
        // rebalance): how many units one standing army can field.
        // 20 per army at Nexus 1-9, 40 at 10-19, 80 at 20-29, 160
        // at Nexus 30. Camp encounters scale their spawn counts
        // by the same band (ArmyBandFactor) so the tier balance
        // holds at every size, and camp XP pools scale with it.
        public static int ArmySlotCapForNexus(int nexusLevel)
        {
            if (nexusLevel >= 30) return 160;
            if (nexusLevel >= 20) return 80;
            if (nexusLevel >= 10) return 40;
            return 20;
        }

        /// <summary>How many units each standing army can field
        /// at the current Nexus level.</summary>
        public int ArmySlotCap => ArmySlotCapForNexus(NexusLevel);

        /// <summary>Camp scaling factor for the current army-size
        /// band: 1 at Nexus 1-9, doubling per band to 8 at 30.</summary>
        public static int ArmyBandFactor(int nexusLevel)
        {
            return ArmySlotCapForNexus(nexusLevel) / 20;
        }

        // One roster per army: the actual OwnedUnit instances
        // (troops from Army, at most one hero from Heroes),
        // capped at ArmySlotCap including the hero. Saved as unit
        // serials (see SaveService). Entries may be null until
        // GetRoster.
        public List<OwnedUnit>[] ArmyRosters = new List<OwnedUnit>[MaxArmies];
        public int NextUnitSerial = 1;

        // Inbox (2026-11-15, Patrick): game statuses (battle results,
        // harvests, boss reports) are delivered here instead of
        // pop-ups, so they can land while the player is on any tab.
        // Chronological order (oldest first); the view shows newest
        // first. Capped at MaxInboxMessages — the oldest are dropped.
        // InboxSeeded records that the one-time welcome message has
        // been delivered (old saves load unseeded and receive it on
        // their next launch).
        public const int MaxInboxMessages = 60;
        public List<InboxMessage> Inbox = new List<InboxMessage>();
        public bool InboxSeeded;

        /// <summary>The roster list for an army (created on first
        /// use). Null for an out-of-range army index.</summary>
        public List<OwnedUnit> GetRoster(int army)
        {
            if (army < 0 || army >= MaxArmies) return null;
            if (ArmyRosters == null) ArmyRosters = new List<OwnedUnit>[MaxArmies];
            if (ArmyRosters[army] == null) ArmyRosters[army] = new List<OwnedUnit>();
            return ArmyRosters[army];
        }

        /// <summary>True when this Nexus level unlocks the army.</summary>
        public bool ArmyUnlocked(int army)
        {
            return army >= 0 && army < MaxArmies && NexusLevel >= ArmyNexusGates[army];
        }
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
