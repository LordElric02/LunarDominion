using System;
using System.Collections.Generic;
using System.Globalization;
using LunarDominion.Core;
using LunarDominion.Economy;

namespace LunarDominion.Research
{
    public enum ResearchStatus
    {
        CanResearch,
        MaxLevel,
        AlreadyResearching,
        LabNotBuilt,
        LabLevelGate,
        NexusGate,
        PrerequisiteGate,
        InsufficientResources
    }

    /// <summary>Research rules only: Lab and Nexus gates, prerequisite
    /// chain, one-at-a-time timers, costs, and bonus multipliers.
    /// UI displays the status/reason — it never recomputes it.
    /// Timers are real time: a level applies when its finish time
    /// passes, whether the game was open or closed, and TryComplete
    /// is what turns a finished timer into a level. This is the
    /// prototype's first timed system on purpose: PrototypeInstantBuild
    /// stays true for buildings and recruitment, but does NOT
    /// shortcut research.</summary>
    public class ResearchService
    {
        // Approved defaults (2026-11-01): minutes per target level.
        public static readonly int[] TimerMinutesByLevel = { 2, 5, 10, 20, 30 };

        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly BalanceData _balance;
        private readonly List<ResearchDefinition> _definitions;

        public ResearchService(GameState state, EconomyService economy, BalanceData balance,
            List<ResearchDefinition> definitions)
        {
            _state = state;
            _economy = economy;
            _balance = balance;
            _definitions = definitions ?? new List<ResearchDefinition>();
        }

        public int GetLevel(ResearchId id)
        {
            int i = (int)id;
            if (_state.ResearchLevels == null || i < 0 || i >= _state.ResearchLevels.Length) return 0;
            return _state.ResearchLevels[i];
        }

        public ResearchDefinition Find(ResearchId id)
        {
            return _definitions.Find(d => d.Id == id);
        }

        public bool IsResearching => _state.ActiveResearchId >= 0;

        public ResearchDefinition ActiveDefinition =>
            IsResearching ? Find((ResearchId)_state.ActiveResearchId) : null;

        public int ActiveTargetLevel => _state.ActiveResearchTargetLevel;

        public static int TimerMinutesForLevel(int targetLevel)
        {
            if (targetLevel < 1 || targetLevel > TimerMinutesByLevel.Length) return 0;
            return TimerMinutesByLevel[targetLevel - 1];
        }

        // BUG FIX (2026-11-01, Alloy Refining 2 completed instantly):
        // Plain DateTime.TryParse converts a UTC "o" timestamp to local
        // time. In Chicago that shifts the finish 5-6 hours earlier, so
        // UtcNow already looks past it and TryComplete fires on the
        // next 1-second tick. RoundtripKind preserves the UTC instant.
        public static bool TryParseFinishUtc(string text, out DateTime finish)
        {
            return DateTime.TryParse(text, null, DateTimeStyles.RoundtripKind, out finish);
        }

        public double RemainingSeconds(DateTime nowUtc)
        {
            if (!IsResearching) return 0;
            if (!TryParseFinishUtc(_state.ActiveResearchFinishUtc, out var finish)) return 0;
            return Math.Max(0, (finish - nowUtc).TotalSeconds);
        }

        public string RemainingText(DateTime nowUtc)
        {
            int seconds = (int)Math.Ceiling(RemainingSeconds(nowUtc));
            return $"{seconds / 60:D2}:{seconds % 60:D2}";
        }

        public ResourceCost CostToLevel(ResearchDefinition def, int targetLevel)
        {
            if (targetLevel <= 0) return ResourceCost.Zero;
            float factor = (float)Math.Pow(_balance.CostGrowth, targetLevel - 1);
            return new ResourceCost(
                (int)Math.Round(def.BaseBiomassCost * factor),
                (int)Math.Round(def.BaseAlloyCost * factor),
                (int)Math.Round(def.BaseHelium3Cost * factor));
        }

        public ResearchStatus CheckResearch(ResearchDefinition def, out ResourceCost cost, out string reason)
        {
            int level = GetLevel(def.Id);
            int target = level + 1;
            cost = ResourceCost.Zero;
            reason = "";

            if (level >= ResearchCatalog.MaxResearchLevel)
            {
                reason = "Max level reached.";
                return ResearchStatus.MaxLevel;
            }
            if (IsResearching)
            {
                var active = ActiveDefinition;
                reason = active != null
                    ? $"Research already in progress: {active.DisplayName} ({RemainingText(DateTime.UtcNow)} left)."
                    : "Research already in progress.";
                return ResearchStatus.AlreadyResearching;
            }
            int labLevel = _state.GetLevel(BuildingId.ResearchLab);
            if (labLevel == 0)
            {
                reason = "Build the Research Lab on the Base tab first.";
                return ResearchStatus.LabNotBuilt;
            }
            if (labLevel < target)
            {
                reason = $"Research Lab level {target} required (currently {labLevel}).";
                return ResearchStatus.LabLevelGate;
            }
            if (_state.NexusLevel < def.UnlockNexusLevel)
            {
                reason = $"Unlocks at Command Nexus level {def.UnlockNexusLevel}.";
                return ResearchStatus.NexusGate;
            }
            if (def.PrerequisiteId != ResearchId.None)
            {
                int prereqLevel = GetLevel(def.PrerequisiteId);
                if (prereqLevel < def.PrerequisiteLevel)
                {
                    var prereq = Find(def.PrerequisiteId);
                    string name = prereq != null ? prereq.DisplayName : def.PrerequisiteId.ToString();
                    reason = $"Requires {name} level {def.PrerequisiteLevel}.";
                    return ResearchStatus.PrerequisiteGate;
                }
            }
            cost = CostToLevel(def, target);
            if (!_economy.CanAfford(cost))
            {
                reason = "Not enough resources.";
                return ResearchStatus.InsufficientResources;
            }
            int minutes = TimerMinutesForLevel(target);
            reason = $"Ready — {minutes} min.";
            return ResearchStatus.CanResearch;
        }

        /// <summary>Pay now, start the timer. The level is NOT applied
        /// until TryComplete sees the finish time pass.</summary>
        public bool StartResearch(ResearchDefinition def, out string reason)
        {
            var status = CheckResearch(def, out var cost, out reason);
            if (status != ResearchStatus.CanResearch) return false;
            if (!_economy.Spend(cost)) { reason = "Not enough resources."; return false; }
            int target = GetLevel(def.Id) + 1;
            _state.ActiveResearchId = (int)def.Id;
            _state.ActiveResearchTargetLevel = target;
            _state.ActiveResearchFinishUtc = DateTime.UtcNow
                .AddMinutes(TimerMinutesForLevel(target)).ToString("o");
            reason = $"{def.DisplayName} level {target} researching — {TimerMinutesForLevel(target)} min.";
            return true;
        }

        /// <summary>Apply a finished timer. Returns true only when a
        /// level was actually granted by this call.</summary>
        public bool TryComplete(DateTime nowUtc, out ResearchDefinition completed)
        {
            completed = null;
            if (!IsResearching) return false;
            if (!TryParseFinishUtc(_state.ActiveResearchFinishUtc, out var finish))
            {
                // Corrupt finish time: drop the in-progress research
                // rather than leaving the player blocked forever.
                ClearActive();
                return false;
            }
            if (nowUtc < finish) return false;
            var def = ActiveDefinition;
            int target = _state.ActiveResearchTargetLevel;
            ClearActive();
            if (def == null || target < 1 || target > ResearchCatalog.MaxResearchLevel) return false;
            SetLevel(def.Id, target);
            completed = def;
            return true;
        }

        private void ClearActive()
        {
            _state.ActiveResearchId = -1;
            _state.ActiveResearchTargetLevel = 0;
            _state.ActiveResearchFinishUtc = "";
        }

        private void SetLevel(ResearchId id, int level)
        {
            int i = (int)id;
            if (_state.ResearchLevels == null) _state.ResearchLevels = new int[10];
            if (i >= 0 && i < _state.ResearchLevels.Length) _state.ResearchLevels[i] = level;
        }

        // --- Bonus multipliers. All are 1.0 with no research. ---

        public float ProductionMultiplier(int producedResource)
        {
            foreach (var def in _definitions)
            {
                int level = GetLevel(def.Id);
                if (level <= 0) continue;
                if (def.Effect == ResearchEffect.ProductionBiomass && producedResource == 1)
                    return 1f + def.EffectPerLevel * level;
                if (def.Effect == ResearchEffect.ProductionAlloy && producedResource == 2)
                    return 1f + def.EffectPerLevel * level;
                if (def.Effect == ResearchEffect.ProductionHelium && producedResource == 3)
                    return 1f + def.EffectPerLevel * level;
            }
            return 1f;
        }

        public float StorageMultiplier()
        {
            foreach (var def in _definitions)
            {
                if (def.Effect != ResearchEffect.StorageCap) continue;
                int level = GetLevel(def.Id);
                if (level > 0) return 1f + def.EffectPerLevel * level;
            }
            return 1f;
        }

        public void ClassMultipliers(UnitClass cls, out float hpMult, out float atkMult, out float defMult)
        {
            hpMult = 1f; atkMult = 1f; defMult = 1f;
            foreach (var def in _definitions)
            {
                int level = GetLevel(def.Id);
                if (level <= 0) continue;
                float bonus = def.EffectPerLevel * level;
                if (cls == UnitClass.Infantry && def.Effect == ResearchEffect.InfantryStats)
                {
                    hpMult += bonus; atkMult += bonus;
                }
                else if (cls == UnitClass.Ranged && def.Effect == ResearchEffect.RangedStats)
                {
                    hpMult += bonus; atkMult += bonus;
                }
                else if (cls == UnitClass.Mechanized && def.Effect == ResearchEffect.MechanizedStats)
                {
                    hpMult += bonus; defMult += bonus;
                }
            }
        }

        public float RewardMultiplier()
        {
            foreach (var def in _definitions)
            {
                if (def.Effect != ResearchEffect.ExpeditionReward) continue;
                int level = GetLevel(def.Id);
                if (level > 0) return 1f + def.EffectPerLevel * level;
            }
            return 1f;
        }

        public float XpMultiplier()
        {
            foreach (var def in _definitions)
            {
                if (def.Effect != ResearchEffect.ExpeditionXp) continue;
                int level = GetLevel(def.Id);
                if (level > 0) return 1f + def.EffectPerLevel * level;
            }
            return 1f;
        }

        /// <summary>Flawless XP bonus as a fraction: base 0.25, +0.05
        /// per Flawless Drills level, up to 0.50.</summary>
        public float FlawlessBonus()
        {
            foreach (var def in _definitions)
            {
                if (def.Effect != ResearchEffect.FlawlessBonus) continue;
                int level = GetLevel(def.Id);
                if (level > 0) return 0.25f + def.EffectPerLevel * level;
            }
            return 0.25f;
        }
    }
}
