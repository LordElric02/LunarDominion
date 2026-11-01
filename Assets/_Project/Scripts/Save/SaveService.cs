using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LunarDominion.Core;
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
        public int[] BuildingLevels = new int[8];
        // Research (2026-11-01). Old saves without these fields load
        // as unresearched with no research in progress, which is correct.
        public int[] ResearchLevels = new int[10];
        public int ActiveResearchId = -1;
        public int ActiveResearchTargetLevel;
        public string ActiveResearchFinishUtc = "";
        public List<OwnedUnit> Army = new List<OwnedUnit>();
        public string LastEnergyDate = "";
        public string LastSaveTimeUtc = "";
        // Fractional production carry (<1 per resource). Phase 6 fix:
        // previously in GameState only, so it reset to 0 on every load.
        // Old saves without these fields load as 0, which is correct.
        public float ProductionCarryBiomass;
        public float ProductionCarryAlloy;
        public float ProductionCarryHelium3;
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
                LastEnergyDate = state.LastEnergyDate,
                LastSaveTimeUtc = state.LastSaveTimeUtc,
                ProductionCarryBiomass = state.ProductionCarryBiomass,
                ProductionCarryAlloy = state.ProductionCarryAlloy,
                ProductionCarryHelium3 = state.ProductionCarryHelium3
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
                    LastEnergyDate = data.LastEnergyDate ?? "",
                    LastSaveTimeUtc = data.LastSaveTimeUtc ?? "",
                    ProductionCarryBiomass = Math.Max(0f, data.ProductionCarryBiomass),
                    ProductionCarryAlloy = Math.Max(0f, data.ProductionCarryAlloy),
                    ProductionCarryHelium3 = Math.Max(0f, data.ProductionCarryHelium3)
                };
                if (state.ActiveResearchId < 0)
                {
                    state.ActiveResearchTargetLevel = 0;
                    state.ActiveResearchFinishUtc = "";
                }
                // Clamp levels: 0-30, and no building above the Nexus.
                int nexus = Math.Max(1, Math.Min(state.NexusLevel, _balance.MaxBuildingLevel));
                state.SetLevel(BuildingId.CommandNexus, nexus);
                for (int i = 0; i < 8; i++)
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

        // Research Lab (2026-11-01) grew the array 7 -> 8. Old saves
        // copy their 7 entries and load with the Lab at 0 (locked).
        private int[] ExpandBuildingLevels(int[] saved)
        {
            var result = new int[8];
            if (saved == null) return result;
            int count = Math.Min(saved.Length, 8);
            for (int i = 0; i < count; i++) result[i] = saved[i];
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
                if (id < 0 || id > 8)
                {
                    // Phase 6 fix (S4): §9 asks for a warning on drop.
                    Debug.LogWarning($"[Lunar Dominion] Dropped unknown unit ID {id} from save.");
                    continue;
                }
                result.Add(new OwnedUnit
                {
                    Id = unit.Id,
                    Level = Math.Max(1, Math.Min(unit.Level, _balance.MaxUnitLevel)),
                    XP = Math.Max(0, unit.XP)
                });
            }
            return result;
        }

        public void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
