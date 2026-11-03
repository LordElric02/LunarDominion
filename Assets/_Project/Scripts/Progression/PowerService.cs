using System;
using System.Collections.Generic;
using LunarDominion.Core;

namespace LunarDominion.Progression
{
    /// <summary>User Power (2026-11-03, proposal
    /// Lunar-Dominion-User-Power-Proposal.md). Display only — it never
    /// affects combat, costs, or progression. Derived from the current
    /// GameState on every read, so it is never stored and never goes
    /// stale: an old save simply computes its power from what it
    /// already contains, and losing a unit lowers power.
    ///
    /// Total = Building + Army + Research.
    /// Buildings: Nexus 1,000 per level, every other building 300.
    /// Army: every recruited unit counts (not just the expedition
    /// squad), at (HP + 8*ATK + 4*DEF) * (1 + 0.08*(level-1)), rounded
    /// per unit. Research: 250 per node level.
    /// Stockpiled resources and energy are deliberately excluded, so
    /// power cannot be inflated by hoarding.</summary>
    public class PowerService
    {
        public const int NexusPowerPerLevel = 1000;
        public const int OtherBuildingPowerPerLevel = 300;
        public const int ResearchPowerPerLevel = 250;

        private readonly GameState _state;
        private readonly Dictionary<UnitId, UnitDefinition> _unitsById;

        public PowerService(GameState state, List<UnitDefinition> unitDefinitions)
        {
            _state = state;
            _unitsById = new Dictionary<UnitId, UnitDefinition>();
            if (unitDefinitions != null)
            {
                foreach (var def in unitDefinitions)
                {
                    if (def != null && !_unitsById.ContainsKey(def.Id))
                        _unitsById.Add(def.Id, def);
                }
            }
        }

        public long TotalPower()
        {
            return BuildingPower() + ArmyPower() + ResearchPower();
        }

        public long BuildingPower()
        {
            long total = 0;
            if (_state.BuildingLevels == null) return total;
            for (int i = 0; i < _state.BuildingLevels.Length; i++)
            {
                int level = _state.BuildingLevels[i];
                if (level <= 0) continue;
                total += level * (i == (int)BuildingId.CommandNexus
                    ? NexusPowerPerLevel
                    : OtherBuildingPowerPerLevel);
            }
            return total;
        }

        public long ArmyPower()
        {
            long total = 0;
            if (_state.Army == null) return total;
            foreach (var unit in _state.Army)
            {
                if (unit == null) continue;
                if (!_unitsById.TryGetValue(unit.Id, out var def) || def == null) continue;
                int level = Math.Max(1, unit.Level);
                long basePower = def.BaseHealth + 8L * def.BaseAttack + 4L * def.BaseDefense;
                total += (long)Math.Round(basePower * (1.0 + 0.08 * (level - 1)));
            }
            return total;
        }

        public long ResearchPower()
        {
            long total = 0;
            if (_state.ResearchLevels == null) return total;
            foreach (int level in _state.ResearchLevels)
            {
                if (level > 0) total += level * ResearchPowerPerLevel;
            }
            return total;
        }

        public static string FormatPower(long power)
        {
            if (power < 100000) return power.ToString("N0");
            return $"{power / 1000.0:F1}K";
        }
    }
}
