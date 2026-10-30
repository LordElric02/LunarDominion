using System;
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
        public string LastEnergyDate = "";
        public string LastSaveTimeUtc = "";
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
                LastEnergyDate = state.LastEnergyDate,
                LastSaveTimeUtc = state.LastSaveTimeUtc
            };
            string json = JsonUtility.ToJson(data, true);
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(SavePath)) File.Delete(SavePath);
            File.Move(temp, SavePath);
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
                    LastEnergyDate = data.LastEnergyDate ?? "",
                    LastSaveTimeUtc = data.LastSaveTimeUtc ?? ""
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

        public void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }
    }
}
