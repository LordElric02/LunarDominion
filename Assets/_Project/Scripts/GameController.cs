using System;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Economy;
using LunarDominion.Progression;
using LunarDominion.Expeditions;
using LunarDominion.Save;
using LunarDominion.Units;
using UnityEngine;

namespace LunarDominion
{
    /// <summary>Thin composition root (GDD §7, rule 3). Wires services together,
    /// ticks production, and saves on pause/quit. Contains no balance formulas
    /// and no UI layout — those live in services and views.</summary>
    public class GameController : MonoBehaviour
    {
        [Header("Assign in Inspector (optional — defaults are created if empty)")]
        public BalanceData Balance;
        public List<BuildingDefinition> BuildingDefinitions;
        public List<UnitDefinition> UnitDefinitions;
        public List<EncounterDefinition> EncounterDefinitions;

        public GameState State { get; private set; }
        public EconomyService Economy { get; private set; }
        public ProductionService Production { get; private set; }
        public EnergyService Energy { get; private set; }
        public ProgressionService Progression { get; private set; }
        public RecruitmentService Recruitment { get; private set; }
        public ExpeditionService Expeditions { get; private set; }
        public SaveService Save { get; private set; }

        public event Action OnStateChanged;

        private float _tickTimer;

        private void Awake()
        {
            if (Balance == null) Balance = BalanceData.Default();
            if (BuildingDefinitions == null || BuildingDefinitions.Count == 0)
                BuildingDefinitions = BuildingCatalog.CreateDefaultDefinitions();
            if (UnitDefinitions == null || UnitDefinitions.Count == 0)
                UnitDefinitions = UnitCatalog.CreateDefaultDefinitions();
            if (EncounterDefinitions == null || EncounterDefinitions.Count == 0)
                EncounterDefinitions = EncounterCatalog.CreateDefaultEncounters();

            Save = new SaveService(Balance);
            if (!Save.TryLoad(out var loaded, out _))
            {
                State = Save.NewGame();
            }
            else
            {
                State = loaded;
            }

            Economy = new EconomyService(State, Balance);
            Production = new ProductionService(State, Economy, Balance);
            Energy = new EnergyService(State, Balance);
            Progression = new ProgressionService(State, Economy, Balance);
            Recruitment = new RecruitmentService(State, Economy, BuildingDefinitions);
            Expeditions = new ExpeditionService(State, Economy, Energy, Balance, UnitDefinitions);

            // Apply offline production now that Economy exists.
            if (DateTime.TryParse(State.LastSaveTimeUtc, out var parsedLastSave))
            {
                // Only apply if this was a loaded save, not a fresh game.
                if (!string.IsNullOrEmpty(State.LastSaveTimeUtc))
                    Production.ApplyOfflineProduction(BuildingDefinitions, parsedLastSave, DateTime.UtcNow);
            }

            Energy.RefillIfNewDay(DateTime.Now);
            Economy.ClampResources();
            Debug.Log($"[Lunar Dominion] Started. PrototypeInstantBuild={Balance.PrototypeInstantBuild}, Nexus Lv {State.NexusLevel}");
        }

        private void Update()
        {
            // Production tick: once per second, in hours.
            _tickTimer += Time.deltaTime;
            if (_tickTimer >= 1f)
            {
                Production.Tick(_tickTimer / 3600f, BuildingDefinitions);
                _tickTimer = 0f;
                OnStateChanged?.Invoke();
            }
        }

        public void NotifyChanged()
        {
            OnStateChanged?.Invoke();
        }

        public void SaveGame()
        {
            Save.Save(State);
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveGame();
        }

        private void OnApplicationQuit()
        {
            SaveGame();
        }

        // Dev / test helpers. Called only by DevHUDView, never by gameplay UI.
        public void GrantResources(int biomass, int alloy, int helium3)
        {
            Economy.Earn(new ResourceCost(biomass, alloy, helium3));
            NotifyChanged();
        }

        public void GrantEnergy(int amount)
        {
            Energy.Grant(amount);
            NotifyChanged();
        }

        public void ResetGame()
        {
            Save.DeleteSave();
            State = Save.NewGame();
            Economy = new EconomyService(State, Balance);
            Production = new ProductionService(State, Economy, Balance);
            Energy = new EnergyService(State, Balance);
            Progression = new ProgressionService(State, Economy, Balance);
            Recruitment = new RecruitmentService(State, Economy, BuildingDefinitions);
            Expeditions = new ExpeditionService(State, Economy, Energy, Balance, UnitDefinitions);
            NotifyChanged();
        }
    }
}
