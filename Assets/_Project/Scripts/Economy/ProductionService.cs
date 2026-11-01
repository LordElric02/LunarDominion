using System;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Research;

namespace LunarDominion.Economy
{
    /// <summary>Generator production, including the 8-hour offline cap (GDD §4).
    /// Fractional production is carried, not lost, between ticks.</summary>
    public class ProductionService
    {
        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly BalanceData _balance;
        private readonly ResearchService _research;

        public ProductionService(GameState state, EconomyService economy, BalanceData balance,
            ResearchService research = null)
        {
            _state = state;
            _economy = economy;
            _balance = balance;
            _research = research;
        }

        public void Tick(float deltaHours, IReadOnlyList<BuildingDefinition> definitions)
        {
            ApplyHours(deltaHours, definitions);
        }

        public void ApplyOfflineProduction(IReadOnlyList<BuildingDefinition> definitions, DateTime lastSaveUtc, DateTime nowUtc)
        {
            double elapsedHours = (nowUtc - lastSaveUtc).TotalHours;
            if (elapsedHours <= 0) return;
            float capped = (float)Math.Min(elapsedHours, _balance.OfflineProductionCapHours);
            ApplyHours(capped, definitions);
        }

        private void ApplyHours(float hours, IReadOnlyList<BuildingDefinition> definitions)
        {
            foreach (var def in definitions)
            {
                int level = _state.GetLevel(def.Id);
                float rate = _economy.ProductionPerHour(def, level);
                if (_research != null) rate *= _research.ProductionMultiplier(def.ProducedResource);
                if (rate <= 0f) continue;
                float amount = rate * hours;
                switch (def.ProducedResource)
                {
                    case 1:
                        _state.ProductionCarryBiomass += amount;
                        int wholeB = (int)_state.ProductionCarryBiomass;
                        if (wholeB > 0) { _state.ProductionCarryBiomass -= wholeB; _economy.EarnBiomass(wholeB); }
                        break;
                    case 2:
                        _state.ProductionCarryAlloy += amount;
                        int wholeA = (int)_state.ProductionCarryAlloy;
                        if (wholeA > 0) { _state.ProductionCarryAlloy -= wholeA; _economy.EarnAlloy(wholeA); }
                        break;
                    case 3:
                        _state.ProductionCarryHelium3 += amount;
                        int wholeH = (int)_state.ProductionCarryHelium3;
                        if (wholeH > 0) { _state.ProductionCarryHelium3 -= wholeH; _economy.EarnHelium3(wholeH); }
                        break;
                }
            }
        }
    }
}
