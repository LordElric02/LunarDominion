using LunarDominion.Core;
using LunarDominion.Economy;

namespace LunarDominion.Progression
{
    public enum UpgradeStatus
    {
        CanUpgrade,
        MaxLevel,
        NexusGate,
        Locked,
        InsufficientResources
    }

    /// <summary>Upgrade rules only: Nexus cap, unlock thresholds, max level,
    /// affordability. UI displays the status/reason — it never recomputes it.</summary>
    public class ProgressionService
    {
        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly BalanceData _balance;

        public ProgressionService(GameState state, EconomyService economy, BalanceData balance)
        {
            _state = state;
            _economy = economy;
            _balance = balance;
        }

        public UpgradeStatus CheckUpgrade(BuildingDefinition def, out ResourceCost cost, out string reason)
        {
            int level = _state.GetLevel(def.Id);
            cost = ResourceCost.Zero;
            reason = "";

            if (level >= _balance.MaxBuildingLevel)
            {
                reason = "Max level reached.";
                return UpgradeStatus.MaxLevel;
            }
            if (!def.IsCommandNexus && level == 0)
            {
                // Building not yet unlocked at all.
                if (_state.NexusLevel < def.UnlockNexusLevel)
                {
                    reason = $"Unlocks at Command Nexus level {def.UnlockNexusLevel}.";
                    return UpgradeStatus.Locked;
                }
                // Level 0 -> 1 is the unlock itself; it still costs the level-2 base
                // in the GDD tables only after level 1, so treat the first build as
                // free in the prototype to avoid a soft-lock, then normal costs apply.
                reason = "Ready to build.";
                return UpgradeStatus.CanUpgrade;
            }
            if (!def.IsCommandNexus && level >= _state.NexusLevel)
            {
                reason = "Command Nexus level caps this building. Upgrade the Nexus first.";
                return UpgradeStatus.NexusGate;
            }

            int target = level + 1;
            cost = _economy.UpgradeCost(def, target);
            if (!_economy.CanAfford(cost))
            {
                reason = "Not enough resources.";
                return UpgradeStatus.InsufficientResources;
            }
            reason = "Ready to upgrade.";
            return UpgradeStatus.CanUpgrade;
        }

        public bool TryUpgrade(BuildingDefinition def, out string reason)
        {
            var status = CheckUpgrade(def, out var cost, out reason);
            if (status != UpgradeStatus.CanUpgrade) return false;

            int level = _state.GetLevel(def.Id);
            if (level == 0)
            {
                _state.SetLevel(def.Id, 1);
                reason = $"{def.DisplayName} built at level 1.";
                return true;
            }
            if (!_economy.Spend(cost)) { reason = "Not enough resources."; return false; }
            _state.SetLevel(def.Id, level + 1);
            reason = $"{def.DisplayName} upgraded to level {level + 1}.";
            return true;
        }
    }
}
