using System;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Economy;
using LunarDominion.Research;
using LunarDominion.Units;

namespace LunarDominion.Expeditions
{
    public enum ExpeditionStatus
    {
        CanStart,
        TierLocked,
        EmptySquad,
        SquadTooLarge,
        InsufficientEnergy
    }

    public class XpGain
    {
        public string UnitName;
        public int XpGained;
        public int OldLevel;
        public int NewLevel;
    }

    public class BattleResult
    {
        public string EncounterName;
        public bool Victory;
        public bool Timeout;
        public bool Flawless;
        public int Rounds;
        public int EnergySpent;
        public ResourceCost RewardEarned;
        public int XpPool;
        public List<string> CasualtyNames = new List<string>();
        public List<XpGain> XpGains = new List<XpGain>();
    }

    /// <summary>Expedition rules only: tier gates, squad size, energy
    /// spending, and applying a combat outcome (rewards, XP, level-ups,
    /// casualties). Energy is spent at expedition start and is not
    /// refunded on defeat (GDD §6, provisional). Rewards are paid only
    /// on victory; timeout survivors get half XP, all-dead defeats
    /// get no XP. This service owns no UI.</summary>
    public class ExpeditionService
    {
        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly EnergyService _energy;
        private readonly BalanceData _balance;
        private readonly List<UnitDefinition> _units;
        private readonly ResearchService _research;

        public ExpeditionService(GameState state, EconomyService economy, EnergyService energy,
            BalanceData balance, List<UnitDefinition> units, ResearchService research = null)
        {
            _state = state;
            _economy = economy;
            _energy = energy;
            _balance = balance;
            _units = units;
            _research = research;
        }

        public ExpeditionStatus CheckExpedition(EncounterDefinition encounter, int squadSize, out string reason)
        {
            reason = "";
            if (_state.NexusLevel < encounter.RequiredNexusLevel)
            {
                reason = $"Unlocks at Command Nexus level {encounter.RequiredNexusLevel} (currently {_state.NexusLevel}).";
                return ExpeditionStatus.TierLocked;
            }
            if (squadSize == 0)
            {
                reason = "Select at least one unit for your squad.";
                return ExpeditionStatus.EmptySquad;
            }
            if (squadSize > _balance.MaxSquadSize)
            {
                reason = $"Squad is capped at {_balance.MaxSquadSize} units.";
                return ExpeditionStatus.SquadTooLarge;
            }
            if (_state.Energy < encounter.EnergyCost)
            {
                reason = $"Not enough energy ({_state.Energy} / {encounter.EnergyCost}).";
                return ExpeditionStatus.InsufficientEnergy;
            }
            reason = "Ready to launch.";
            return ExpeditionStatus.CanStart;
        }

        /// <summary>squadArmyIndices are positions in State.Army, in the
        /// order the player chose them. Returns false without spending
        /// energy or changing the army if the expedition cannot start.</summary>
        public bool TryStart(EncounterDefinition encounter, List<int> squadArmyIndices,
            out BattleResult result, out string reason)
        {
            result = null;
            var status = CheckExpedition(encounter, squadArmyIndices.Count, out reason);
            if (status != ExpeditionStatus.CanStart) return false;

            var squad = new List<SquadCombatant>();
            for (int i = 0; i < squadArmyIndices.Count; i++)
            {
                int armyIndex = squadArmyIndices[i];
                if (armyIndex < 0 || armyIndex >= _state.Army.Count) continue;
                var owned = _state.Army[armyIndex];
                var def = _units.Find(u => u.Id == owned.Id);
                if (def == null) continue;
                squad.Add(new SquadCombatant { Unit = owned, Definition = def, SquadIndex = i });
            }
            if (squad.Count == 0)
            {
                reason = "Select at least one unit for your squad.";
                return false;
            }
            if (!_energy.Spend(encounter.EnergyCost))
            {
                reason = $"Not enough energy ({_state.Energy} / {encounter.EnergyCost}).";
                return false;
            }

            // Phase 6: pass the player's Nexus level so enemies scale
            // above their gate (1.0 at the gate itself).
            // Research: pass research so class bonuses apply to players.
            var outcome = CombatService.Resolve(squad, encounter, _state.NexusLevel, _research);

            result = new BattleResult
            {
                EncounterName = encounter.DisplayName,
                Victory = outcome.Victory,
                Timeout = outcome.Timeout,
                Rounds = outcome.Rounds,
                EnergySpent = encounter.EnergyCost
            };

            // Casualty names, in squad order, before any removal.
            foreach (int deadIndex in outcome.DeadSquadIndices)
            {
                result.CasualtyNames.Add(squad[deadIndex].Definition.DisplayName);
            }
            result.Flawless = outcome.Victory && outcome.DeadSquadIndices.Count == 0;

            // XP pool: full on victory (+flawless bonus if flawless),
            // half on a timeout defeat with survivors, none on an
            // all-dead defeat. Research (2026-11-01): Combat Training
            // multiplies the pool, Flawless Drills raises the flawless
            // bonus from its 25% base. No research means 1.25x / 1.0x
            // exactly as before.
            float xpMult = _research != null ? _research.XpMultiplier() : 1f;
            float flawlessBonus = _research != null ? _research.FlawlessBonus() : 0.25f;
            int pool = 0;
            if (outcome.Victory)
            {
                pool = result.Flawless
                    ? (int)Math.Round(encounter.XpPool * (1f + flawlessBonus) * xpMult)
                    : (int)Math.Round(encounter.XpPool * xpMult);
            }
            else if (outcome.Timeout && outcome.SurvivingSquadIndices.Count > 0)
            {
                pool = (int)Math.Round(encounter.XpPool / 2f * xpMult);
            }
            result.XpPool = pool;

            // Apply XP to survivors, split equally (remainder to the
            // first survivor), with level-ups. At the unit level cap,
            // XP is retained but grants no further level.
            if (pool > 0 && outcome.SurvivingSquadIndices.Count > 0)
            {
                int share = pool / outcome.SurvivingSquadIndices.Count;
                int remainder = pool % outcome.SurvivingSquadIndices.Count;
                for (int i = 0; i < outcome.SurvivingSquadIndices.Count; i++)
                {
                    var member = squad[outcome.SurvivingSquadIndices[i]];
                    int gained = share + (i == 0 ? remainder : 0);
                    int oldLevel = member.Unit.Level;
                    member.Unit.XP += gained;
                    while (member.Unit.Level < _balance.MaxUnitLevel
                        && member.Unit.XP >= UnitStats.XpToNextLevel(member.Unit.Level))
                    {
                        member.Unit.XP -= UnitStats.XpToNextLevel(member.Unit.Level);
                        member.Unit.Level++;
                    }
                    result.XpGains.Add(new XpGain
                    {
                        UnitName = member.Definition.DisplayName,
                        XpGained = gained,
                        OldLevel = oldLevel,
                        NewLevel = member.Unit.Level
                    });
                }
            }

            // Victory reward only. Report what actually landed after
            // the storage cap, not the table value, if the cap clipped it.
            // Research (2026-11-01): Salvage Protocols multiplies the
            // reward before the cap is applied.
            if (outcome.Victory)
            {
                float rewardMult = _research != null ? _research.RewardMultiplier() : 1f;
                var scaledReward = new ResourceCost(
                    (int)Math.Round(encounter.Reward.Biomass * rewardMult),
                    (int)Math.Round(encounter.Reward.Alloy * rewardMult),
                    (int)Math.Round(encounter.Reward.Helium3 * rewardMult));
                int beforeB = _state.Biomass, beforeA = _state.Alloy, beforeH = _state.Helium3;
                _economy.Earn(scaledReward);
                result.RewardEarned = new ResourceCost(
                    _state.Biomass - beforeB, _state.Alloy - beforeA, _state.Helium3 - beforeH);
            }

            // Remove casualties permanently, highest army index first
            // so earlier indices stay valid while removing.
            var deadArmyIndices = new List<int>();
            foreach (int deadIndex in outcome.DeadSquadIndices)
            {
                deadArmyIndices.Add(squadArmyIndices[deadIndex]);
            }
            deadArmyIndices.Sort((a, b) => b.CompareTo(a));
            foreach (int armyIndex in deadArmyIndices)
            {
                if (armyIndex >= 0 && armyIndex < _state.Army.Count) _state.Army.RemoveAt(armyIndex);
            }

            reason = outcome.Victory ? "Victory!" : (outcome.Timeout ? "Defeat — 20-round timeout." : "Defeat.");
            return true;
        }
    }
}
