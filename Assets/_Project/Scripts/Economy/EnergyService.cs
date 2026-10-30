using System;
using LunarDominion.Core;

namespace LunarDominion.Economy
{
    /// <summary>Daily energy budget (GDD §6, contradiction C2):
    /// refills to full at local midnight, not continuous regen.</summary>
    public class EnergyService
    {
        private readonly GameState _state;
        private readonly BalanceData _balance;

        public EnergyService(GameState state, BalanceData balance)
        {
            _state = state;
            _balance = balance;
        }

        public void RefillIfNewDay(DateTime todayLocal)
        {
            string today = todayLocal.ToString("yyyy-MM-dd");
            if (_state.LastEnergyDate != today)
            {
                _state.Energy = _balance.MaxEnergy;
                _state.LastEnergyDate = today;
            }
        }

        public bool Spend(int amount)
        {
            if (_state.Energy < amount) return false;
            _state.Energy -= amount;
            return true;
        }
    }
}
