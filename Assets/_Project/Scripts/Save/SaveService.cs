using System;
using System.Collections.Generic;
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
        public int[] BuildingLevels = new int[7];
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
                    BuildingLevels = data.BuildingLevels != null && data.BuildingLevels.Length == 7
                        ? (int[])data.BuildingLevels.Clone() : new int[7],
                    Army = ValidateArmy(data.Army),
                    LastEnergyDate = data.LastEnergyDate ?? "",
                    LastSaveTimeUtc = data.LastSaveTimeUtc ?? "",
                    ProductionCarryBiomass = Math.Max(0f, data.ProductionCarryBiomass),
                    ProductionCarryAlloy = Math.Max(0f, data.ProductionCarryAlloy),
                    ProductionCarryHelium3 = Math.Max(0f, data.ProductionCarryHelium3)
                };
                // Clamp levels: 0-30, and no building above the Nexus.
                int nexus = Math.Max(1, Math.Min(state.NexusLevel, _balance.MaxBuildingLevel));
                state.SetLevel(BuildingId.CommandNexus, nexus);
                for (int i = 0; i < 7; i++)
                {
                    int level = Math.Max(0, Math.Min(state.BuildingLevels[i], _balance.MaxBuildingLevel));
                    if (i != (int)BuildingId.CommandNexus) level = Math.Min(level, nexus);
                    state.BuildingLevels[i] = level;
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
