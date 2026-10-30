using System;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Units;

namespace LunarDominion.Expeditions
{
    /// <summary>Input for one player combatant: an owned unit, with its
    /// position in the squad so results map back to the right unit.</summary>
    public class SquadCombatant
    {
        public OwnedUnit Unit;
        public UnitDefinition Definition;
        public int SquadIndex;
    }

    public class CombatOutcome
    {
        public bool Victory;
        public bool Timeout;
        public int Rounds;
        // SquadIndex values of player units killed in this battle.
        public List<int> DeadSquadIndices = new List<int>();
        // SquadIndex values of player units still alive at the end.
        public List<int> SurvivingSquadIndices = new List<int>();
    }

    /// <summary>Auto-resolved combat only (GDD §6, C8): no UI, no state
    /// changes here — it reports an outcome and ExpeditionService
    /// applies rewards, XP, and casualties. Enemies are level 1 in
    /// this prototype: the GDD tables give no enemy level, so the
    /// level modifier is computed against 1 for both sides' enemies.</summary>
    public static class CombatService
    {
        private class Fighter
        {
            public string Name;
            public UnitClass Class;
            public int Level;
            public int Attack;
            public int Defense;
            public int Health;
            public int SquadIndex = -1;
            public bool IsPlayer;
            public bool Alive => Health > 0;
        }

        public static float CounterMultiplier(UnitClass attacker, UnitClass defender)
        {
            // Ranged beats Infantry, Infantry beats Mechanized,
            // Mechanized beats Ranged (GDD §5, C10).
            if (attacker == UnitClass.Ranged && defender == UnitClass.Infantry) return 1.5f;
            if (attacker == UnitClass.Infantry && defender == UnitClass.Mechanized) return 1.5f;
            if (attacker == UnitClass.Mechanized && defender == UnitClass.Ranged) return 1.5f;
            if (attacker == UnitClass.Infantry && defender == UnitClass.Ranged) return 0.75f;
            if (attacker == UnitClass.Mechanized && defender == UnitClass.Infantry) return 0.75f;
            if (attacker == UnitClass.Ranged && defender == UnitClass.Mechanized) return 0.75f;
            return 1.0f;
        }

        public static int Damage(int attack, int defense, UnitClass attackerClass, UnitClass defenderClass,
            int attackerLevel, int defenderLevel)
        {
            float levelModifier = 1f + 0.04f * (attackerLevel - defenderLevel);
            levelModifier = Math.Max(0.80f, Math.Min(1.25f, levelModifier));
            int damage = (int)Math.Round(attack * CounterMultiplier(attackerClass, defenderClass) * levelModifier - defense * 0.5f);
            return Math.Max(5, damage);
        }

        public static CombatOutcome Resolve(List<SquadCombatant> squad, EncounterDefinition encounter)
        {
            var players = new List<Fighter>();
            foreach (var member in squad)
            {
                int level = Math.Max(1, member.Unit.Level);
                players.Add(new Fighter
                {
                    Name = member.Definition.DisplayName,
                    Class = member.Definition.Class,
                    Level = level,
                    Attack = UnitStats.ScaledAttack(member.Definition, level),
                    Defense = UnitStats.ScaledDefense(member.Definition, level),
                    Health = UnitStats.ScaledHealth(member.Definition, level),
                    SquadIndex = member.SquadIndex,
                    IsPlayer = true
                });
            }

            var enemies = new List<Fighter>();
            foreach (var spawn in encounter.Spawns)
            {
                for (int i = 0; i < spawn.Count; i++)
                {
                    enemies.Add(new Fighter
                    {
                        Name = spawn.DisplayName,
                        Class = spawn.Class,
                        Level = 1,
                        Attack = spawn.Attack,
                        Defense = spawn.Defense,
                        Health = spawn.Health,
                        IsPlayer = false
                    });
                }
            }

            var outcome = new CombatOutcome();
            for (int round = 1; round <= 20; round++)
            {
                outcome.Rounds = round;
                // Player attacks first, each alive unit hitting the alive
                // enemy with the lowest current HP (GDD §6).
                foreach (var attacker in players)
                {
                    if (!attacker.Alive) continue;
                    var target = LowestHealth(enemies);
                    if (target == null) break;
                    target.Health -= Damage(attacker.Attack, target.Defense, attacker.Class, target.Class, attacker.Level, target.Level);
                }
                if (AllDead(enemies))
                {
                    outcome.Victory = true;
                    break;
                }
                foreach (var attacker in enemies)
                {
                    if (!attacker.Alive) continue;
                    var target = LowestHealth(players);
                    if (target == null) break;
                    target.Health -= Damage(attacker.Attack, target.Defense, attacker.Class, target.Class, attacker.Level, target.Level);
                }
                if (AllDead(players)) break;
            }

            if (!outcome.Victory && !AllDead(players) && !AllDead(enemies))
            {
                outcome.Timeout = true;
            }
            foreach (var player in players)
            {
                if (player.Alive) outcome.SurvivingSquadIndices.Add(player.SquadIndex);
                else outcome.DeadSquadIndices.Add(player.SquadIndex);
            }
            return outcome;
        }

        private static Fighter LowestHealth(List<Fighter> fighters)
        {
            Fighter lowest = null;
            foreach (var fighter in fighters)
            {
                if (!fighter.Alive) continue;
                if (lowest == null || fighter.Health < lowest.Health) lowest = fighter;
            }
            return lowest;
        }

        private static bool AllDead(List<Fighter> fighters)
        {
            foreach (var fighter in fighters)
            {
                if (fighter.Alive) return false;
            }
            return true;
        }
    }
}
