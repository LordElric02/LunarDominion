using System;
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
        InsufficientResources,
        ArmyFull
    }

    public enum PromoteStatus
    {
        CanPromote,
        BarracksNotBuilt,
        TierLocked,
        NoCandidate,
        InsufficientResources
    }

    /// <summary>Recruitment rules only: barracks built, tier unlocked by
    /// barracks level, affordability, troop limit. UI displays the
    /// status/reason — it never recomputes it. Recruitment is instant
    /// in this prototype (GDD C1: time = 0, not free — costs and
    /// prerequisites still apply).
    /// Troop limit (2026-11-16, Patrick): the total number of troops
    /// is driven by BOTH the barracks and the Nexus. Every level of
    /// the three military barracks (Marine Foundry, Nova Arsenal,
    /// Aegis Motor Pool) raises the allowance by 6 over a base of
    /// 44, and the Nexus sets the upper limit: 50 troops, rising to
    /// 100 at Nexus 10, 250 at Nexus 20 and 500 at Nexus 30. The
    /// standing-army slot cap of 8 per army is separate and unchanged.
    /// Heroes (2026-11-10): named characters, one of each, max 4 total.
    /// They live in GameState.Heroes, not the Army, and fight bosses in
    /// the lair view. Heroes do not count against the troop limit.</summary>
    public class RecruitmentService
    {
        // Max heroes: one of each named hero — 3 at launch
        // (2026-11-10: Reyes, Vega, Okafor), 4 since the Medic
        // (2026-11-15: Ward).
        public const int MaxHeroes = 4;

        // The Medic's recruit gate (2026-11-15): a late-game
        // hero, unlocked by the Hospital rather than a barracks.
        public const int MedicHospitalLevel = 10;

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
            return id == UnitId.HeroReyes || id == UnitId.HeroVega || id == UnitId.HeroOkafor
                || id == UnitId.HeroWard;
        }

        /// <summary>How many heroes the player has recruited (0-3).</summary>
        public int CountHeroes()
        {
            return _state.Heroes != null ? _state.Heroes.Count : 0;
        }

        /// <summary>The Nexus ceiling on total troops (Patrick
        /// 2026-11-16): 50 from the start, 100 at Nexus 10, 250 at
        /// Nexus 20, 500 at Nexus 30. The Nexus is the upper limit;
        /// the barracks allowance below decides how much of the
        /// ceiling is actually usable.</summary>
        public static int NexusTroopCeiling(int nexusLevel)
        {
            if (nexusLevel >= 30) return 500;
            if (nexusLevel >= 20) return 250;
            if (nexusLevel >= 10) return 100;
            return 50;
        }

        /// <summary>Troops the player currently owns (heroes not
        /// counted).</summary>
        public int TroopCount => _state.Army != null ? _state.Army.Count : 0;

        /// <summary>How many troops the player may own right now:
        /// 44 + 6 per combined level of the three military
        /// barracks, capped by the Nexus ceiling. A fresh base
        /// (one barracks at level 1) starts at the 50 ceiling; an
        /// army already over the limit (a save from before the
        /// limit existed) keeps its troops but cannot recruit
        /// more until it is back under.</summary>
        public int TroopLimit
        {
            get
            {
                int barracks = _state.GetLevel(BuildingId.MarineFoundry)
                             + _state.GetLevel(BuildingId.NovaArsenal)
                             + _state.GetLevel(BuildingId.AegisMotorPool);
                int allowance = 44 + 6 * barracks;
                return Math.Min(NexusTroopCeiling(_state.NexusLevel), allowance);
            }
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
                // The Medic (2026-11-15) is a late-game recruit:
                // his gate is the Hospital, not a barracks.
                if (def.Id == UnitId.HeroWard)
                {
                    int hospital = _state.GetLevel(BuildingId.Hospital);
                    if (hospital < MedicHospitalLevel)
                    {
                        reason = $"Requires Hospital level {MedicHospitalLevel} (currently {hospital}).";
                        return RecruitStatus.TierLocked;
                    }
                }
                if (CountHeroes() >= MaxHeroes)
                {
                    reason = $"Hero limit reached ({MaxHeroes} max).";
                    return RecruitStatus.TierLocked;
                }
            }
            // Troop limit (2026-11-16): barracks-driven, Nexus-
            // capped. Heroes are exempt (their own cap above).
            if (!def.IsHero && TroopCount >= TroopLimit)
            {
                reason = $"Army full ({TroopCount}/{TroopLimit} troops). Upgrade a military barracks or the Nexus to raise the limit.";
                return RecruitStatus.ArmyFull;
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
            var unit = new OwnedUnit(def.Id);
            // Standing armies (2026-11-12): every unit gets a
            // permanent serial at birth so rosters can name it.
            unit.Serial = _state.NextUnitSerial++;
            if (def.IsHero)
            {
                if (_state.Heroes == null) _state.Heroes = new List<OwnedUnit>();
                _state.Heroes.Add(unit);
            }
            else
            {
                if (_state.Army == null) _state.Army = new List<OwnedUnit>();
                _state.Army.Add(unit);
            }
            reason = $"{def.DisplayName} recruited at level 1.";
            return true;
        }

        /// <summary>Promotion (2026-11-23, Patrick): an existing
        /// troop can be promoted to the next tier of its own class
        /// (T1 up to T6) instead of recruiting the higher tier
        /// fresh. The unit keeps its level, XP, serial and army
        /// assignment — only its Id changes, so its stats rescale
        /// from the new tier's definition. The price is the
        /// DIFFERENCE between the two tiers' recruit costs (the
        /// lower tier was already paid for), so stepping tier by
        /// tier costs exactly the same in total as the direct
        /// difference. Gates mirror recruiting: the target tier
        /// must be unlocked by its barracks level. Heroes cannot
        /// be promoted — they are named characters, not tiers.
        /// The candidate is the player's best unit of the lower
        /// tier (highest level, then most XP), across the army
        /// and the hospital's injured list.</summary>
        public static ResourceCost PromotionCost(UnitDefinition fromDef, UnitDefinition toDef)
        {
            var from = fromDef.Cost;
            var to = toDef.Cost;
            return new ResourceCost(
                Math.Max(0, to.Biomass - from.Biomass),
                Math.Max(0, to.Alloy - from.Alloy),
                Math.Max(0, to.Helium3 - from.Helium3));
        }

        public OwnedUnit FindPromotionCandidate(UnitId fromId)
        {
            OwnedUnit best = null;
            ConsiderCandidates(_state.Army, fromId, ref best);
            ConsiderCandidates(_state.Injured, fromId, ref best);
            return best;
        }

        private static void ConsiderCandidates(List<OwnedUnit> list, UnitId fromId, ref OwnedUnit best)
        {
            if (list == null) return;
            foreach (var unit in list)
            {
                if (unit == null || unit.Id != fromId) continue;
                if (best == null || unit.Level > best.Level
                    || (unit.Level == best.Level && unit.XP > best.XP))
                {
                    best = unit;
                }
            }
        }

        public PromoteStatus CheckPromote(UnitDefinition fromDef, UnitDefinition toDef,
            out OwnedUnit candidate, out ResourceCost cost, out string reason)
        {
            candidate = null;
            cost = ResourceCost.Zero;
            reason = "";
            if (fromDef == null || toDef == null || fromDef.IsHero || toDef.IsHero)
            {
                reason = "Heroes cannot be promoted.";
                return PromoteStatus.NoCandidate;
            }
            int barracksLevel = _state.GetLevel(toDef.BarracksBuilding);
            if (barracksLevel == 0)
            {
                reason = $"Build {BarracksName(toDef.BarracksBuilding)} first.";
                return PromoteStatus.BarracksNotBuilt;
            }
            int required = RequiredBarracksLevel(toDef.Tier);
            if (barracksLevel < required)
            {
                reason = $"Requires {BarracksName(toDef.BarracksBuilding)} level {required} (currently {barracksLevel}).";
                return PromoteStatus.TierLocked;
            }
            candidate = FindPromotionCandidate(fromDef.Id);
            if (candidate == null)
            {
                reason = $"No {fromDef.DisplayName} to promote.";
                return PromoteStatus.NoCandidate;
            }
            cost = PromotionCost(fromDef, toDef);
            if (!_economy.CanAfford(cost))
            {
                reason = "Not enough resources.";
                return PromoteStatus.InsufficientResources;
            }
            reason = "Ready to promote.";
            return PromoteStatus.CanPromote;
        }

        public bool TryPromote(UnitDefinition fromDef, UnitDefinition toDef, out string reason)
        {
            var status = CheckPromote(fromDef, toDef, out var candidate, out var cost, out reason);
            if (status != PromoteStatus.CanPromote) return false;
            if (!_economy.Spend(cost)) { reason = "Not enough resources."; return false; }
            candidate.Id = toDef.Id;
            reason = $"{fromDef.DisplayName} promoted to {toDef.DisplayName} (level {candidate.Level} kept).";
            return true;
        }
    }
}
