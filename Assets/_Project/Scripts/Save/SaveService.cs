using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LunarDominion.Core;
using LunarDominion.Expeditions;
using UnityEngine;

namespace LunarDominion.Save
{
    /// <summary>Versioned JSON save (GDD §7). Atomic write via temp file + replace.
    /// On load, values are validated and clamped — never a silent corrupt state.</summary>
    [Serializable]
    public class SaveData
    {
        public int SaveVersion = 1;
        public int Biomass;
        public int Alloy;
        public int Helium3;
        public int Energy;
        public int[] BuildingLevels = new int[9];
        // Research (2026-11-01). Old saves without these fields load
        // as unresearched with no research in progress, which is correct.
        public int[] ResearchLevels = new int[10];
        public int ActiveResearchId = -1;
        public int ActiveResearchTargetLevel;
        public string ActiveResearchFinishUtc = "";
        public List<OwnedUnit> Army = new List<OwnedUnit>();
        // Hospital (2026-11-09): injured units waiting to be healed.
        // Old saves without this field load with an empty list.
        public List<OwnedUnit> Injured = new List<OwnedUnit>();
        // Heroes (2026-11-10): named characters (Reyes, Vega, Okafor).
        // Old saves without this field load with an empty list.
        public List<OwnedUnit> Heroes = new List<OwnedUnit>();
        public string LastEnergyDate = "";
        public string LastSaveTimeUtc = "";
        // Fractional production carry (<1 per resource). Phase 6 fix:
        // previously in GameState only, so it reset to 0 on every load.
        // Old saves without these fields load as 0, which is correct.
        public float ProductionCarryBiomass;
        public float ProductionCarryAlloy;
        public float ProductionCarryHelium3;
        // Map excursions (2026-11-09): node regen timestamps, one per
        // resource node. Old saves without this field load as all "".
        public string[] NodeRegenUtc = new string[18];
        // Node occupation (2026-11-09): the army farming a node.
        // Old saves load with no occupation (-1). LEGACY singular
        // fields (pre two-armies): still read on load and migrated
        // into slot 0; no longer written.
        public int OccupiedNodeIndex = -1;
        public string OccupationEndUtc = "";
        public int OccupationAmount;
        public int OccupationKind;
        public int OccupationTroops;
        // Node occupations per army slot (two armies, 2026-11-11):
        // slot 0 = Army 1, slot 1 = Army 2.
        public int[] OccupiedNodeIndices;
        public string[] OccupationEndUtcs;
        public int[] OccupationAmounts;
        public int[] OccupationKinds;
        public int[] OccupationTroopCounts;
        // Boss lair (2026-11-09, ladder 2026-11-10): highest boss level
        // defeated (0 = none yet, up to 15). Old saves load as 0.
        public string BossRespawnUtc = "";
        public int BossLevelDefeated = 0;
        // Enemy camp field (2026-11-10): 36 slots. CampSlotTier[i] =
        // the tier in slot i (0 = empty); CampSlotRespawnUtc[i] = when
        // an empty slot refills ("" = not waiting). Old saves load
        // with all 0/"" and the field is populated on first sync.
        public int[] CampSlotTier = new int[36];
        public string[] CampSlotRespawnUtc = new string[36];
        // Standing armies (2026-11-12): one entry per army, the
        // roster as unit serials (see OwnedUnit.Serial), plus
        // the next serial to hand out. Old saves without these
        // load with empty rosters and serials assigned on load.
        // The wrapper class matters: JsonUtility silently drops
        // jagged arrays (int[][]), which is exactly what the first
        // version used — rosters saved as nothing and loaded back
        // empty (found by Patrick's restart check 2026-11-12).
        // An array of serializable slots, each holding a flat
        // int[], serializes correctly.
        [Serializable]
        public class RosterSlot
        {
            public int[] Serials = new int[0];
        }
        public RosterSlot[] ArmyRosters;
        public int NextUnitSerial = 1;
    }

    public class SaveService
    {
        private readonly BalanceData _balance;
        public string SavePath => Path.Combine(Application.persistentDataPath, "lunar-dominion-save.json");

        public SaveService(BalanceData balance) { _balance = balance; }

        public GameState NewGame()
        {
            var state = new GameState
            {
                Biomass = _balance.StartingBiomass,
                Alloy = _balance.StartingAlloy,
                Helium3 = _balance.StartingHelium3,
                Energy = _balance.StartingEnergy,
                LastEnergyDate = DateTime.Now.ToString("yyyy-MM-dd"),
                LastSaveTimeUtc = DateTime.UtcNow.ToString("o")
            };
            state.SetLevel(BuildingId.CommandNexus, 1);
            state.SetLevel(BuildingId.HydroponicsDome, 1);
            state.SetLevel(BuildingId.AlloyExtractor, 1);
            state.SetLevel(BuildingId.MarineFoundry, 1);
            return state;
        }

        public void Save(GameState state)
        {
            state.LastSaveTimeUtc = DateTime.UtcNow.ToString("o");
            var data = new SaveData
            {
                Biomass = state.Biomass,
                Alloy = state.Alloy,
                Helium3 = state.Helium3,
                Energy = state.Energy,
                BuildingLevels = (int[])state.BuildingLevels.Clone(),
                ResearchLevels = state.ResearchLevels != null ? (int[])state.ResearchLevels.Clone() : new int[10],
                ActiveResearchId = state.ActiveResearchId,
                ActiveResearchTargetLevel = state.ActiveResearchTargetLevel,
                ActiveResearchFinishUtc = state.ActiveResearchFinishUtc ?? "",
                Army = ValidateArmy(state.Army),
                Injured = ValidateArmy(state.Injured),
                Heroes = ValidateArmy(state.Heroes),
                LastEnergyDate = state.LastEnergyDate,
                LastSaveTimeUtc = state.LastSaveTimeUtc,
                ProductionCarryBiomass = state.ProductionCarryBiomass,
                ProductionCarryAlloy = state.ProductionCarryAlloy,
                ProductionCarryHelium3 = state.ProductionCarryHelium3,
                NodeRegenUtc = state.NodeRegenUtc != null ? (string[])state.NodeRegenUtc.Clone() : new string[18],
                OccupiedNodeIndices = CopyIntSlots(state.OccupiedNodeIndex, -1),
                OccupationEndUtcs = CopyStringSlots(state.OccupationEndUtc),
                OccupationAmounts = CopyIntSlots(state.OccupationAmount, 0),
                OccupationKinds = CopyIntSlots(state.OccupationKind, 0),
                OccupationTroopCounts = CopyIntSlots(state.OccupationTroops, 0),
                BossRespawnUtc = state.BossRespawnUtc ?? "",
                BossLevelDefeated = Math.Max(0, Math.Min(state.BossLevelDefeated, EncounterCatalog.MaxBossLevel)),
                CampSlotTier = ValidateCampSlotTiers(state.CampSlotTier),
                CampSlotRespawnUtc = ValidateCampSlotRespawns(state.CampSlotRespawnUtc),
                ArmyRosters = SerializeRosters(state),
                NextUnitSerial = Math.Max(1, state.NextUnitSerial)
            };
            string json = JsonUtility.ToJson(data, true);
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, json);
            // Phase 6 fix (S5): File.Replace swaps the temp file into
            // place atomically. The old delete-then-move had a window
            // where a crash left no save file at all.
            if (File.Exists(SavePath)) File.Replace(temp, SavePath, null);
            else File.Move(temp, SavePath);
        }

        public bool TryLoad(out GameState state, out string message)
        {
            state = null;
            message = "";
            try
            {
                if (!File.Exists(SavePath)) { message = "No save found."; return false; }
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                if (data == null || data.SaveVersion != 1) { message = "Save version not supported."; return false; }
                state = new GameState
                {
                    Biomass = Math.Max(0, data.Biomass),
                    Alloy = Math.Max(0, data.Alloy),
                    Helium3 = Math.Max(0, data.Helium3),
                    Energy = Math.Max(0, Math.Min(data.Energy, _balance.MaxEnergy)),
                    BuildingLevels = ExpandBuildingLevels(data.BuildingLevels),
                    ResearchLevels = ValidateResearchLevels(data.ResearchLevels),
                    ActiveResearchId = ValidateActiveResearch(data),
                    ActiveResearchTargetLevel = data.ActiveResearchTargetLevel,
                    ActiveResearchFinishUtc = data.ActiveResearchFinishUtc ?? "",
                    Army = ValidateArmy(data.Army),
                    Injured = ValidateArmy(data.Injured),
                    Heroes = ValidateArmy(data.Heroes),
                    LastEnergyDate = data.LastEnergyDate ?? "",
                    LastSaveTimeUtc = data.LastSaveTimeUtc ?? "",
                    ProductionCarryBiomass = Math.Max(0f, data.ProductionCarryBiomass),
                    ProductionCarryAlloy = Math.Max(0f, data.ProductionCarryAlloy),
                    ProductionCarryHelium3 = Math.Max(0f, data.ProductionCarryHelium3),
                    NodeRegenUtc = ValidateNodeRegenUtc(data.NodeRegenUtc),
                    BossRespawnUtc = data.BossRespawnUtc ?? "",
                    BossLevelDefeated = Math.Max(0, Math.Min(data.BossLevelDefeated, EncounterCatalog.MaxBossLevel)),
                    CampSlotTier = ValidateCampSlotTiers(data.CampSlotTier),
                    CampSlotRespawnUtc = ValidateCampSlotRespawns(data.CampSlotRespawnUtc)
                };
                ApplyOccupations(data, state);
                if (state.ActiveResearchId < 0)
                {
                    state.ActiveResearchTargetLevel = 0;
                    state.ActiveResearchFinishUtc = "";
                }
                // Heroes recover instantly after a fight and are never
                // injured (Patrick 2026-11-10): a hero left in the
                // Injured list by an older save returns to Heroes here.
                for (int i = state.Injured.Count - 1; i >= 0; i--)
                {
                    var id = state.Injured[i].Id;
                    if (id == UnitId.HeroReyes || id == UnitId.HeroVega || id == UnitId.HeroOkafor)
                    {
                        state.Heroes.Add(state.Injured[i]);
                        state.Injured.RemoveAt(i);
                    }
                }
                // Standing armies (2026-11-12): every unit needs a
                // unique serial before rosters can resolve. Units
                // from older saves arrive with Serial 0 (or, after
                // ValidateArmy, whatever the save carried); assign
                // fresh serials to any missing or duplicated ones,
                // keeping the saved ones stable where possible.
                state.NextUnitSerial = Math.Max(1, data.NextUnitSerial);
                var usedSerials = new System.Collections.Generic.HashSet<int>();
                foreach (var list in new[] { state.Army, state.Injured, state.Heroes })
                {
                    foreach (var unit in list)
                    {
                        if (unit.Serial <= 0 || usedSerials.Contains(unit.Serial))
                        {
                            while (usedSerials.Contains(state.NextUnitSerial)) state.NextUnitSerial++;
                            unit.Serial = state.NextUnitSerial++;
                        }
                        usedSerials.Add(unit.Serial);
                    }
                }
                ResolveRosters(data, state);
                // Clamp levels: 0-30, and no building above the Nexus.
                int nexus = Math.Max(1, Math.Min(state.NexusLevel, _balance.MaxBuildingLevel));
                state.SetLevel(BuildingId.CommandNexus, nexus);
                for (int i = 0; i < 9; i++)
                {
                    int level = Math.Max(0, Math.Min(state.BuildingLevels[i], _balance.MaxBuildingLevel));
                    if (i != (int)BuildingId.CommandNexus) level = Math.Min(level, nexus);
                    state.BuildingLevels[i] = level;
                }
                // A research level may not exceed the Lab level that
                // was needed to start it; clamp defensively on load.
                int lab = state.GetLevel(BuildingId.ResearchLab);
                for (int i = 0; i < state.ResearchLevels.Length; i++)
                {
                    if (state.ResearchLevels[i] > lab && lab < 5)
                        state.ResearchLevels[i] = Math.Min(state.ResearchLevels[i], Math.Max(0, lab));
                }
                message = "Save loaded.";
                return true;
            }
            catch (Exception e)
            {
                message = "Save could not be loaded: " + e.Message;
                return false;
            }
        }

        // Research Lab (2026-11-01) grew the array 7 -> 8; Hospital
        // (2026-11-09) grew it 8 -> 9. Old saves copy their entries and
        // load with the new building at 0 (locked).
        private int[] ExpandBuildingLevels(int[] saved)
        {
            var result = new int[9];
            if (saved == null) return result;
            int count = Math.Min(saved.Length, 9);
            for (int i = 0; i < count; i++) result[i] = saved[i];
            return result;
        }

        private int[] ValidateCampSlotTiers(int[] saved)
        {
            var result = new int[36];
            if (saved == null) return result;
            for (int i = 0; i < result.Length && i < saved.Length; i++)
            {
                result[i] = Math.Max(0, Math.Min(saved[i], 3));
            }
            return result;
        }

        private string[] ValidateCampSlotRespawns(string[] saved)
        {
            var result = new string[36];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = saved != null && i < saved.Length && saved[i] != null ? saved[i] : "";
            }
            return result;
        }

        private int[] ValidateResearchLevels(int[] saved)
        {
            var result = new int[10];
            if (saved == null) return result;
            int count = Math.Min(saved.Length, 10);
            for (int i = 0; i < count; i++) result[i] = Math.Max(0, Math.Min(saved[i], 5));
            return result;
        }

        // Map excursions (2026-11-09): one regen timestamp per resource
        // node. Old saves have no field (null) and load as all available.
        // Only keep parseable UTC timestamps; anything else means available.
        private string[] ValidateNodeRegenUtc(string[] saved)
        {
            var result = new string[18];
            if (saved == null) return result;
            int count = Math.Min(saved.Length, 18);
            for (int i = 0; i < count; i++)
            {
                string s = saved[i];
                if (string.IsNullOrEmpty(s)) { result[i] = ""; continue; }
                result[i] = DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out _)
                    ? s
                    : "";
            }
            return result;
        }

        // Node occupation (2026-11-09): -1 means no occupation. A
        // claimed node needs a parseable UTC end time, otherwise the
        // occupation is dropped (the node reads as available).
        private int ValidateOccupiedNode(int index, string endUtc)
        {
            if (index < 0 || index > 17) return -1;
            if (string.IsNullOrEmpty(endUtc)) return -1;
            if (!DateTime.TryParse(endUtc, null, DateTimeStyles.RoundtripKind, out _)) return -1;
            return index;
        }

        // Per-slot occupation arrays (one per standing army —
        // 3 since 2026-11-12): copy a GameState array into a
        // SaveData array of exactly MaxArmies slots, padding
        // with the slot's empty value.
        private static int[] CopyIntSlots(int[] source, int emptyValue)
        {
            var result = new int[GameState.MaxArmies];
            for (int i = 0; i < GameState.MaxArmies; i++)
                result[i] = source != null && i < source.Length ? source[i] : emptyValue;
            return result;
        }

        private static string[] CopyStringSlots(string[] source)
        {
            var result = new string[GameState.MaxArmies];
            for (int i = 0; i < GameState.MaxArmies; i++)
                result[i] = source != null && i < source.Length && source[i] != null ? source[i] : "";
            return result;
        }

        private static int SlotValue(int[] values, int slot, int fallback)
        {
            return values != null && slot < values.Length ? values[slot] : fallback;
        }

        private static string SlotValue(string[] values, int slot, string fallback)
        {
            return values != null && slot < values.Length && values[slot] != null
                ? values[slot]
                : (fallback ?? "");
        }

        // Fill the state's per-slot occupations from a save: prefer
        // the two-armies arrays; slot 0 falls back to the legacy
        // singular fields so a farm in progress in an older save is
        // migrated, not lost.
        private void ApplyOccupations(SaveData data, GameState state)
        {
            // One slot per standing army (3 since 2026-11-12);
            // legacy singular fields still feed slot 0.
            for (int slot = 0; slot < GameState.MaxArmies; slot++)
            {
                int index = SlotValue(data.OccupiedNodeIndices, slot, slot == 0 ? data.OccupiedNodeIndex : -1);
                string end = SlotValue(data.OccupationEndUtcs, slot, slot == 0 ? data.OccupationEndUtc : "");
                int amount = SlotValue(data.OccupationAmounts, slot, slot == 0 ? data.OccupationAmount : 0);
                int kind = SlotValue(data.OccupationKinds, slot, slot == 0 ? data.OccupationKind : 0);
                int troops = SlotValue(data.OccupationTroopCounts, slot, slot == 0 ? data.OccupationTroops : 0);
                state.OccupiedNodeIndex[slot] = ValidateOccupiedNode(index, end);
                state.OccupationEndUtc[slot] = end ?? "";
                state.OccupationAmount[slot] = Math.Max(0, amount);
                state.OccupationKind[slot] = Math.Max(0, Math.Min(kind, 2));
                state.OccupationTroops[slot] = Math.Max(0, Math.Min(troops, 8));
            }
        }

        private int ValidateActiveResearch(SaveData data)
        {
            if (data.ActiveResearchId < 0 || data.ActiveResearchId > 9) return -1;
            if (data.ActiveResearchTargetLevel < 1 || data.ActiveResearchTargetLevel > 5) return -1;
            if (string.IsNullOrEmpty(data.ActiveResearchFinishUtc)) return -1;
            // Same UTC round-trip pitfall as ResearchService: plain
            // TryParse would shift the finish to local time.
            if (!DateTime.TryParse(data.ActiveResearchFinishUtc, null, DateTimeStyles.RoundtripKind, out _)) return -1;
            return data.ActiveResearchId;
        }

        // Phase 2 saves have no Army field (null after JSON load), which
        // correctly loads as an empty army. Drop invalid entries, never
        // a whole save, and clamp levels/XP into valid ranges.
        private List<OwnedUnit> ValidateArmy(List<OwnedUnit> army)
        {
            var result = new List<OwnedUnit>();
            if (army == null) return result;
            foreach (var unit in army)
            {
                if (unit == null) continue;
                int id = (int)unit.Id;
                // Valid UnitIds: 0-8 regular units, 9-11 heroes,
                // 12-20 the tier 4-6 units (2026-11-10). Anything else
                // is dropped, never a whole save.
                if (id < 0 || id > 20)
                {
                    // Phase 6 fix (S4): §9 asks for a warning on drop.
                    Debug.LogWarning($"[Lunar Dominion] Dropped unknown unit ID {id} from save.");
                    continue;
                }
                result.Add(new OwnedUnit
                {
                    Id = unit.Id,
                    Level = Math.Max(1, Math.Min(unit.Level, _balance.MaxUnitLevel)),
                    XP = Math.Max(0, unit.XP),
                    Serial = unit.Serial
                });
            }
            return result;
        }

        // Standing armies (2026-11-12): rosters persist as unit
        // serials. A roster entry whose unit no longer exists is
        // dropped on load, never the whole save.
        private static SaveData.RosterSlot[] SerializeRosters(GameState state)
        {
            var result = new SaveData.RosterSlot[GameState.MaxArmies];
            for (int army = 0; army < GameState.MaxArmies; army++)
            {
                var roster = state.GetRoster(army);
                var serials = new System.Collections.Generic.List<int>();
                if (roster != null)
                {
                    foreach (var unit in roster)
                    {
                        if (unit != null && unit.Serial > 0) serials.Add(unit.Serial);
                    }
                }
                result[army] = new SaveData.RosterSlot { Serials = serials.ToArray() };
            }
            return result;
        }

        private static void ResolveRosters(SaveData data, GameState state)
        {
            state.ArmyRosters = new System.Collections.Generic.List<OwnedUnit>[GameState.MaxArmies];
            if (data.ArmyRosters == null) return;
            var bySerial = new System.Collections.Generic.Dictionary<int, OwnedUnit>();
            foreach (var list in new[] { state.Army, state.Injured, state.Heroes })
            {
                foreach (var unit in list)
                {
                    if (unit.Serial > 0 && !bySerial.ContainsKey(unit.Serial))
                        bySerial[unit.Serial] = unit;
                }
            }
            var claimed = new System.Collections.Generic.HashSet<int>();
            for (int army = 0; army < GameState.MaxArmies && army < data.ArmyRosters.Length; army++)
            {
                var roster = state.GetRoster(army);
                var slot = data.ArmyRosters[army];
                if (slot == null || slot.Serials == null) continue;
                var serials = slot.Serials;
                int heroes = 0;
                foreach (int serial in serials)
                {
                    if (roster.Count >= 8) break;
                    if (claimed.Contains(serial)) continue;
                    if (!bySerial.TryGetValue(serial, out var unit)) continue;
                    bool isHero = unit.Id == UnitId.HeroReyes || unit.Id == UnitId.HeroVega || unit.Id == UnitId.HeroOkafor;
                    if (isHero && heroes >= 1) continue;
                    roster.Add(unit);
                    claimed.Add(serial);
                    if (isHero) heroes++;
                }
            }
        }

        public void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
