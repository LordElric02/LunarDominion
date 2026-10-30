using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Economy;

namespace LunarDominion.Units
{
    public enum RecruitStatus
    {
        CanRecruit,
        BarracksNotBuilt,
        TierLocked,
        InsufficientResources
    }

    /// <summary>Recruitment rules only: barracks built, tier unlocked by
    /// barracks level, affordability. UI displays the status/reason —
    /// it never recomputes it. Recruitment is instant in this prototype
    /// (GDD C1: time = 0, not free — costs and prerequisites still apply).
    /// There is no total army cap in the prototype (GDD §5); the squad
    /// cap of 8 applies to expeditions in Phase 4, not to recruitment.</summary>
    public class RecruitmentService
    {
        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly List<BuildingDefinition> _buildings;

        public RecruitmentService(GameState state, EconomyService economy, List<BuildingDefinition> buildings)
        {
            _state = state;
            _economy = economy;
            _buildings = buildings;
        }

        /// <summary>Barracks level required for a tier (GDD §3).</summary>
        public static int RequiredBarracksLevel(int tier)
        {
            switch (tier)
            {
                case 2: return 10;
                case 3: return 20;
                default: return 1;
            }
        }

        public string BarracksName(BuildingId id)
        {
            if (_buildings != null)
            {
                var def = _buildings.Find(b => b.Id == id);
                if (def != null) return def.DisplayName;
            }
            return id.ToString();
        }

        public int CountOwned(UnitId id)
        {
            int count = 0;
            if (_state.Army == null) return 0;
            foreach (var unit in _state.Army)
            {
                if (unit != null && unit.Id == id) count++;
            }
            return count;
        }

        public RecruitStatus CheckRecruit(UnitDefinition def, out ResourceCost cost, out string reason)
        {
            cost = def.Cost;
            reason = "";
            int barracksLevel = _state.GetLevel(def.BarracksBuilding);
            if (barracksLevel == 0)
            {
                reason = $"Build {BarracksName(def.BarracksBuilding)} first.";
                return RecruitStatus.BarracksNotBuilt;
            }
            int required = RequiredBarracksLevel(def.Tier);
            if (barracksLevel < required)
            {
                reason = $"Requires {BarracksName(def.BarracksBuilding)} level {required} (currently {barracksLevel}).";
                return RecruitStatus.TierLocked;
            }
            if (!_economy.CanAfford(cost))
            {
                reason = "Not enough resources.";
                return RecruitStatus.InsufficientResources;
            }
            reason = "Ready to recruit.";
            return RecruitStatus.CanRecruit;
        }

        public bool TryRecruit(UnitDefinition def, out string reason)
        {
            var status = CheckRecruit(def, out var cost, out reason);
            if (status != RecruitStatus.CanRecruit) return false;
            if (!_economy.Spend(cost)) { reason = "Not enough resources."; return false; }
            if (_state.Army == null) _state.Army = new List<OwnedUnit>();
            _state.Army.Add(new OwnedUnit(def.Id));
            reason = $"{def.DisplayName} recruited at level 1.";
            return true;
        }
    }
}
