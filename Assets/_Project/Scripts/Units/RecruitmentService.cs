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
    /// cap of 8 applies to expeditions in Phase 4, not to recruitment.
    /// Heroes (2026-11-10): named characters, one of each, max 3 total.
    /// They live in GameState.Heroes, not the Army, and fight bosses in
    /// the lair view.</summary>
    public class RecruitmentService
    {
        // Max heroes (2026-11-10): one of each named hero (Reyes,
        // Vega, Okafor) — 3 total.
        public const int MaxHeroes = 3;

        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly List<BuildingDefinition> _buildings;

        public RecruitmentService(GameState state, EconomyService economy, List<BuildingDefinition> buildings)
        {
            _state = state;
            _economy = economy;
            _buildings = buildings;
        }

        /// <summary>True when this unit is a named hero (not a regular
        /// troop). Heroes live in GameState.Heroes and fight bosses.</summary>
        public static bool IsHeroUnit(UnitId id)
        {
            return id == UnitId.HeroReyes || id == UnitId.HeroVega || id == UnitId.HeroOkafor;
        }

        /// <summary>How many heroes the player has recruited (0-3).</summary>
        public int CountHeroes()
        {
            return _state.Heroes != null ? _state.Heroes.Count : 0;
        }

        /// <summary>Barracks level required for a tier (GDD §3).
        /// Tiers 4-6 (2026-11-10) unlock in the barracks' final
        /// stretch; Tier 6 needs a maxed barracks (and so Nexus 30,
        /// since a barracks cannot exceed the Nexus level).</summary>
        public static int RequiredBarracksLevel(int tier)
        {
            switch (tier)
            {
                case 2: return 10;
                case 3: return 20;
                case 4: return 25;
                case 5: return 28;
                case 6: return 30;
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
            // Heroes live in the Heroes list, regular units in the Army.
            var list = IsHeroUnit(id) ? _state.Heroes : _state.Army;
            if (list == null) return 0;
            foreach (var unit in list)
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
            // Heroes (2026-11-10): one of each named hero, max 3 total.
            // A hero already recruited cannot be recruited again.
            if (def.IsHero)
            {
                if (CountOwned(def.Id) > 0)
                {
                    reason = $"{def.DisplayName} is already recruited.";
                    return RecruitStatus.TierLocked;
                }
                if (CountHeroes() >= MaxHeroes)
                {
                    reason = $"Hero limit reached ({MaxHeroes} max).";
                    return RecruitStatus.TierLocked;
                }
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
            // Heroes go to the Heroes list, regular units to the Army.
            if (def.IsHero)
            {
                if (_state.Heroes == null) _state.Heroes = new List<OwnedUnit>();
                _state.Heroes.Add(new OwnedUnit(def.Id));
            }
            else
            {
                if (_state.Army == null) _state.Army = new List<OwnedUnit>();
                _state.Army.Add(new OwnedUnit(def.Id));
            }
            reason = $"{def.DisplayName} recruited at level 1.";
            return true;
        }
    }
}
