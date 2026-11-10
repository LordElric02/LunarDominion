using System;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Economy;

namespace LunarDominion.Progression
{
    /// <summary>Hospital rules only: healing injured units back into the
    /// army. A unit that falls in battle is injured, not dead — it waits
    /// in State.Injured until healed here (see ExpeditionService, which
    /// moves casualties to Injured). Healing costs 50% of the unit's
    /// recruit cost, reduced by 2% per Hospital level above 1 (level 30
    /// heals at 42% of the 50% base, i.e. ~21% of recruit cost), and is
    /// instant — pay, and the unit is back in the army with its level
    /// and XP intact. This service owns no UI; the BuildingPanelView
    /// shows the injured list and calls TryHeal. Future: a permadeath
    /// encounter type will remove its casualties permanently instead of
    /// injuring them — that branch lives in ExpeditionService, not here.</summary>
    public class HospitalService
    {
        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly List<UnitDefinition> _units;

        // Base heal cost as a fraction of the recruit cost, before the
        // Hospital-level discount.
        public const float BaseHealFraction = 0.5f;
        // Discount per Hospital level above 1. Level 30: 1 - 0.02*29 = 0.42.
        public const float DiscountPerLevel = 0.02f;
        // The discount never takes the multiplier below this.
        public const float MinMultiplier = 0.4f;

        public HospitalService(GameState state, EconomyService economy, List<UnitDefinition> units)
        {
            _state = state;
            _economy = economy;
            _units = units;
        }

        public bool HasInjured => _state.Injured.Count > 0;

        public int HospitalLevel => _state.GetLevel(BuildingId.Hospital);

        /// <summary>Cost multiplier from the Hospital level: 1.0 at
        /// level 1, -2% per level above 1, floored at 0.4. A locked
        /// Hospital (level 0) heals at full price — the building must
        /// be built before any healing, so this only matters if the
        /// panel is opened while locked.</summary>
        public float CostMultiplier()
        {
            int level = HospitalLevel;
            if (level <= 1) return 1f;
            return Math.Max(MinMultiplier, 1f - DiscountPerLevel * (level - 1));
        }

        /// <summary>Heal cost for one injured unit: 50% of its recruit
        /// cost, times the Hospital-level multiplier. Each resource is
        /// rounded up so a cost is never free by rounding.</summary>
        public ResourceCost HealCost(OwnedUnit unit)
        {
            var def = _units.Find(u => u.Id == unit.Id);
            if (def == null) return ResourceCost.Zero;
            float mult = BaseHealFraction * CostMultiplier();
            return new ResourceCost(
                (int)Math.Ceiling(def.BaseBiomassCost * mult),
                (int)Math.Ceiling(def.BaseAlloyCost * mult),
                (int)Math.Ceiling(def.BaseHelium3Cost * mult));
        }

        /// <summary>Heal one injured unit: spend its heal cost, move it
        /// from Injured back to the Army — or back to Heroes if it is a
        /// named hero (2026-11-10). Returns false without spending or
        /// moving if the unit is not injured, the Hospital is locked,
        /// or the player cannot afford it.</summary>
        public bool TryHeal(OwnedUnit unit, out string reason)
        {
            reason = "";
            if (unit == null || !_state.Injured.Contains(unit))
            {
                reason = "That unit is not injured.";
                return false;
            }
            if (HospitalLevel <= 0)
            {
                reason = "Build the Hospital first.";
                return false;
            }
            var cost = HealCost(unit);
            if (!_economy.CanAfford(cost))
            {
                reason = $"Not enough resources to heal (need {cost.Biomass} Biomass, {cost.Alloy} Alloy, {cost.Helium3} Helium-3).";
                return false;
            }
            if (!_economy.Spend(cost)) return false;
            _state.Injured.Remove(unit);
            // Heroes heal back to the Heroes list, regular units to the Army.
            var def = _units.Find(u => u.Id == unit.Id);
            bool isHero = def != null && def.IsHero;
            if (isHero)
            {
                if (_state.Heroes == null) _state.Heroes = new List<OwnedUnit>();
                _state.Heroes.Add(unit);
                reason = def != null
                    ? $"{def.DisplayName} healed and back with the heroes."
                    : "Hero healed and back with the heroes.";
            }
            else
            {
                _state.Army.Add(unit);
                reason = def != null
                    ? $"{def.DisplayName} healed and back in the army."
                    : "Unit healed and back in the army.";
            }
            return true;
        }
    }
}
