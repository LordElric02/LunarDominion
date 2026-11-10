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
        // Fallen heroes are listed separately (2026-11-10): heroes
        // recover instantly after the fight and are never injured, so
        // they are not Hospital casualties like regular troops.
        public List<string> HeroCasualtyNames = new List<string>();
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
        // Heroes gain XP at half rate (2026-11-10): a solo hero takes
        // the whole boss XP pool (1000, +25% flawless = 1250), which
        // levelled Reyes Lv 1 -> Lv 10 in a few fights. At 0.5x a solo
        // flawless win gives ~625 XP, so Lv 10 takes ~12 boss wins —
        // a real progression, not a few fights. Regular units are
        // unaffected (their squads split the pool 8 ways anyway).
        public const float HeroXpRate = 0.5f;

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

        /// <summary>allowLockedTier (2026-11-09): map excursions let the
        /// player attack a camp above their Nexus — the fight proceeds
        /// and (almost certainly) fails, with a warning shown first.
        /// The Expeditions menu keeps the gate (false). Enemy scaling
        /// clamps to 1.0 below the gate, so the fight is at base stats.</summary>
        public ExpeditionStatus CheckExpedition(EncounterDefinition encounter, int squadSize, out string reason, bool allowLockedTier = false)
        {
            reason = "";
            if (!allowLockedTier && _state.NexusLevel < encounter.RequiredNexusLevel)
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
        /// energy or changing the army if the expedition cannot start.
        /// allowLockedTier: map excursions only — see CheckExpedition.</summary>
        public bool TryStart(EncounterDefinition encounter, List<int> squadArmyIndices,
            out BattleResult result, out string reason, bool allowLockedTier = false)
        {
            result = null;
            var status = CheckExpedition(encounter, squadArmyIndices.Count, out reason, allowLockedTier);
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
            if (!ResolveFight(encounter, squad, out result, out var outcome, out reason)) return false;

            // Hospital (2026-11-09): casualties are INJURED, not dead.
            // Move the fallen units from the Army to the Injured list
            // (highest army index first so earlier indices stay valid
            // while moving). They keep their level and XP and cannot
            // fight until healed in the Hospital (HospitalService).
            // Future: a permadeath encounter type (e.g. attacking a
            // base) will remove its casualties permanently instead —
            // that branch goes here, gated on an encounter flag.
            var deadArmyIndices = new List<int>();
            foreach (int deadIndex in outcome.DeadSquadIndices)
            {
                deadArmyIndices.Add(squadArmyIndices[deadIndex]);
            }
            deadArmyIndices.Sort((a, b) => b.CompareTo(a));
            foreach (int armyIndex in deadArmyIndices)
            {
                if (armyIndex >= 0 && armyIndex < _state.Army.Count)
                {
                    _state.Injured.Add(_state.Army[armyIndex]);
                    _state.Army.RemoveAt(armyIndex);
                }
            }

            reason = outcome.Victory ? "Victory!" : (outcome.Timeout ? "Defeat — 20-round timeout." : "Defeat.");
            return true;
        }

        /// <summary>Hero boss fight (2026-11-10): same fight as TryStart,
        /// but the squad is heroes (positions in State.Heroes, 1-3) and
        /// the encounter is the boss. Fallen heroes RECOVER INSTANTLY
        /// after the fight (Patrick 2026-11-10): they stay in
        /// State.Heroes, are never injured and never go to the Hospital
        /// — healing applies to troops only. A fallen hero's cost is
        /// the lost fight and the missed XP (XP splits among survivors).
        /// allowLockedTier: the lair view lets the player attack above
        /// their Nexus, like a map camp — the fight proceeds and
        /// (almost certainly) fails, with a warning shown first.</summary>
        public bool TryStartHeroes(EncounterDefinition encounter, List<int> squadHeroIndices,
            out BattleResult result, out string reason, bool allowLockedTier = false)
        {
            result = null;
            var status = CheckExpedition(encounter, squadHeroIndices.Count, out reason, allowLockedTier);
            if (status != ExpeditionStatus.CanStart) return false;

            var squad = new List<SquadCombatant>();
            for (int i = 0; i < squadHeroIndices.Count; i++)
            {
                int heroIndex = squadHeroIndices[i];
                if (_state.Heroes == null || heroIndex < 0 || heroIndex >= _state.Heroes.Count) continue;
                var owned = _state.Heroes[heroIndex];
                var def = _units.Find(u => u.Id == owned.Id);
                if (def == null) continue;
                squad.Add(new SquadCombatant { Unit = owned, Definition = def, SquadIndex = i });
            }
            if (squad.Count == 0)
            {
                reason = "Select at least one hero for the fight.";
                return false;
            }
            if (!ResolveFight(encounter, squad, out result, out var outcome, out reason)) return false;

            // Heroes recover instantly (Patrick 2026-11-10): fallen
            // heroes stay in State.Heroes — no Injured move, no Hospital.
            // They keep their level and XP (and gained none from this
            // fight: XP splits among survivors only).

            reason = outcome.Victory ? "Victory!" : (outcome.Timeout ? "Defeat — 20-round timeout." : "Defeat.");
            return true;
        }

        /// <summary>Mixed map squad (2026-11-10): heroes go along with
        /// the army when attacking enemy camps on the map, and gain XP
        /// from those fights. squadArmyIndices are positions in
        /// State.Army, squadHeroIndices positions in State.Heroes; the
        /// COMBINED squad is capped at MaxSquadSize (8). Fallen army
        /// units move Army -> Injured; fallen heroes recover instantly
        /// and stay in Heroes (healing applies to troops only).
        /// Same combat, energy and reward as TryStart.</summary>
        public bool TryStartMixed(EncounterDefinition encounter, List<int> squadArmyIndices, List<int> squadHeroIndices,
            out BattleResult result, out string reason, bool allowLockedTier = false)
        {
            result = null;
            int total = (squadArmyIndices != null ? squadArmyIndices.Count : 0)
                + (squadHeroIndices != null ? squadHeroIndices.Count : 0);
            var status = CheckExpedition(encounter, total, out reason, allowLockedTier);
            if (status != ExpeditionStatus.CanStart) return false;

            var squad = new List<SquadCombatant>();
            int squadIndex = 0;
            int armyInSquad = 0;
            if (squadArmyIndices != null)
            {
                for (int i = 0; i < squadArmyIndices.Count; i++)
                {
                    int armyIndex = squadArmyIndices[i];
                    if (armyIndex < 0 || armyIndex >= _state.Army.Count) continue;
                    var owned = _state.Army[armyIndex];
                    var def = _units.Find(u => u.Id == owned.Id);
                    if (def == null) continue;
                    squad.Add(new SquadCombatant { Unit = owned, Definition = def, SquadIndex = squadIndex++ });
                    armyInSquad++;
                }
            }
            if (squadHeroIndices != null)
            {
                for (int i = 0; i < squadHeroIndices.Count; i++)
                {
                    int heroIndex = squadHeroIndices[i];
                    if (_state.Heroes == null || heroIndex < 0 || heroIndex >= _state.Heroes.Count) continue;
                    var owned = _state.Heroes[heroIndex];
                    var def = _units.Find(u => u.Id == owned.Id);
                    if (def == null) continue;
                    squad.Add(new SquadCombatant { Unit = owned, Definition = def, SquadIndex = squadIndex++ });
                }
            }
            if (squad.Count == 0)
            {
                reason = "Select at least one unit or hero for your squad.";
                return false;
            }
            if (!ResolveFight(encounter, squad, out result, out var outcome, out reason)) return false;

            // Troop casualties are INJURED, not dead (Hospital): army
            // units move Army -> Injured. Fallen HEROES recover
            // instantly and stay in Heroes (Patrick 2026-11-10 —
            // healing applies to troops only), so only the army dead
            // are mapped and moved here, highest-first so earlier
            // indices stay valid.
            var deadArmyIndices = new List<int>();
            foreach (int deadIndex in outcome.DeadSquadIndices)
            {
                if (deadIndex < armyInSquad) deadArmyIndices.Add(squadArmyIndices[deadIndex]);
            }
            deadArmyIndices.Sort((a, b) => b.CompareTo(a));
            foreach (int armyIndex in deadArmyIndices)
            {
                if (armyIndex >= 0 && armyIndex < _state.Army.Count)
                {
                    _state.Injured.Add(_state.Army[armyIndex]);
                    _state.Army.RemoveAt(armyIndex);
                }
            }

            reason = outcome.Victory ? "Victory!" : (outcome.Timeout ? "Defeat — 20-round timeout." : "Defeat.");
            return true;
        }

        /// <summary>Shared fight resolution (2026-11-10): spend energy,
        /// resolve combat, build the result, apply XP to survivors and
        /// the victory reward. TryStart (army) and TryStartHeroes
        /// (heroes) both call this; each moves its own casualties after.
        /// Returns false without spending energy if energy is short.</summary>
        private bool ResolveFight(EncounterDefinition encounter, List<SquadCombatant> squad,
            out BattleResult result, out CombatOutcome outcome, out string reason)
        {
            result = null;
            outcome = null;
            reason = "";
            if (!_energy.Spend(encounter.EnergyCost))
            {
                reason = $"Not enough energy ({_state.Energy} / {encounter.EnergyCost}).";
                return false;
            }

            // Phase 6: pass the player's Nexus level so enemies scale
            // above their gate (1.0 at the gate itself).
            // Research: pass research so class bonuses apply to players.
            outcome = CombatService.Resolve(squad, encounter, _state.NexusLevel, _research);

            result = new BattleResult
            {
                EncounterName = encounter.DisplayName,
                Victory = outcome.Victory,
                Timeout = outcome.Timeout,
                Rounds = outcome.Rounds,
                EnergySpent = encounter.EnergyCost
            };

            // Casualty names, in squad order, before any removal.
            // Heroes are tracked separately: they recover instantly
            // after the fight (never injured), troops go to the Hospital.
            foreach (int deadIndex in outcome.DeadSquadIndices)
            {
                if (squad[deadIndex].Definition.IsHero) result.HeroCasualtyNames.Add(squad[deadIndex].Definition.DisplayName);
                else result.CasualtyNames.Add(squad[deadIndex].Definition.DisplayName);
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
                    // Heroes level at half rate from BOSS fights only
                    // (HeroXpRate, Tier 4): a solo hero would otherwise
                    // take the whole 1000+ XP boss pool and rocket to
                    // Lv 10 in a few fights. Map camp fights (Tier 1-3)
                    // give full XP — that is the heroes' steady XP path
                    // now that they march with the army (2026-11-10).
                    if (member.Definition.IsHero && encounter.Tier == 4)
                    {
                        gained = (int)Math.Round(gained * HeroXpRate);
                    }
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

            return true;
        }

        /// <summary>Real-time boss fight, start (2026-11-10): the
        /// A.R.E.S.-09 fight in the lair resolves in real time via
        /// AresBossController instead of CombatService, but the
        /// economy around it is identical to TryStartHeroes — same
        /// gate checks, energy spent up front (never refunded on
        /// defeat) — and CompleteHeroesRealtime applies the same XP
        /// pool / reward rules afterwards. Squad is heroes only
        /// (positions in State.Heroes, 1-3); the returned
        /// SquadCombatants carry the OwnedUnit + definition the
        /// real-time combatants are built from.</summary>
        public bool TryStartHeroesRealtime(EncounterDefinition encounter, List<int> squadHeroIndices,
            out List<SquadCombatant> squad, out string reason, bool allowLockedTier = false)
        {
            squad = null;
            var status = CheckExpedition(encounter, squadHeroIndices.Count, out reason, allowLockedTier);
            if (status != ExpeditionStatus.CanStart) return false;

            squad = new List<SquadCombatant>();
            for (int i = 0; i < squadHeroIndices.Count; i++)
            {
                int heroIndex = squadHeroIndices[i];
                if (_state.Heroes == null || heroIndex < 0 || heroIndex >= _state.Heroes.Count) continue;
                var owned = _state.Heroes[heroIndex];
                var def = _units.Find(u => u.Id == owned.Id);
                if (def == null) continue;
                squad.Add(new SquadCombatant { Unit = owned, Definition = def, SquadIndex = i });
            }
            if (squad.Count == 0)
            {
                reason = "Select at least one hero for the fight.";
                squad = null;
                return false;
            }
            if (!_energy.Spend(encounter.EnergyCost))
            {
                reason = $"Not enough energy ({_state.Energy} / {encounter.EnergyCost}).";
                squad = null;
                return false;
            }
            reason = "Fight started.";
            return true;
        }

        /// <summary>Real-time boss fight, completion (2026-11-10):
        /// applies the outcome of an AresBossController fight with
        /// the same rules as ResolveFight's second half — victory
        /// reward (research multiplier, storage-cap aware), XP pool
        /// split among survivors at the hero boss rate (boss
        /// encounters are Tier 4), flawless = victory with no fallen
        /// hero. Fallen heroes recover instantly: they stay in
        /// State.Heroes and never go to the Hospital (healing is
        /// troops only). fallenSquadIndices are SquadIndex values
        /// within the squad from TryStartHeroesRealtime;
        /// fightSeconds is carried in the result's Rounds field
        /// (the lair view formats it as the fight time).</summary>
        public BattleResult CompleteHeroesRealtime(EncounterDefinition encounter, List<SquadCombatant> squad,
            List<int> fallenSquadIndices, bool victory, int fightSeconds)
        {
            var result = new BattleResult
            {
                EncounterName = encounter.DisplayName,
                Victory = victory,
                Timeout = false,
                Rounds = fightSeconds,
                EnergySpent = encounter.EnergyCost
            };
            var fallen = new HashSet<int>(fallenSquadIndices ?? new List<int>());
            foreach (int idx in fallen)
            {
                if (idx >= 0 && idx < squad.Count)
                    result.HeroCasualtyNames.Add(squad[idx].Definition.DisplayName);
            }
            result.Flawless = victory && fallen.Count == 0;

            // XP pool: full on victory (+flawless bonus), none on
            // defeat (a real-time defeat means the whole squad fell,
            // so there are no survivors to pay out anyway).
            float xpMult = _research != null ? _research.XpMultiplier() : 1f;
            float flawlessBonus = _research != null ? _research.FlawlessBonus() : 0.25f;
            int pool = 0;
            if (victory)
            {
                pool = result.Flawless
                    ? (int)Math.Round(encounter.XpPool * (1f + flawlessBonus) * xpMult)
                    : (int)Math.Round(encounter.XpPool * xpMult);
            }
            result.XpPool = pool;

            var survivors = new List<SquadCombatant>();
            for (int i = 0; i < squad.Count; i++)
            {
                if (!fallen.Contains(squad[i].SquadIndex)) survivors.Add(squad[i]);
            }
            if (pool > 0 && survivors.Count > 0)
            {
                int share = pool / survivors.Count;
                int remainder = pool % survivors.Count;
                for (int i = 0; i < survivors.Count; i++)
                {
                    var member = survivors[i];
                    int gained = share + (i == 0 ? remainder : 0);
                    // Heroes level at half rate from boss fights
                    // (HeroXpRate — see ResolveFight).
                    if (member.Definition.IsHero && encounter.Tier == 4)
                    {
                        gained = (int)Math.Round(gained * HeroXpRate);
                    }
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

            // Victory reward only, reported after the storage cap.
            if (victory)
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
            return result;
        }
    }
}
