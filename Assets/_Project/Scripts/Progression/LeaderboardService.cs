using System;
using LunarDominion.Core;

namespace LunarDominion.Progression
{
    /// <summary>Leaderboard scoring (2026-11-24). One entry
    /// point per category; the first is Robot Lair Damage.
    /// Points are banked on GameState at fight end, so the
    /// local board works today and the same totals upload
    /// when Firebase accounts arrive.</summary>
    public static class LeaderboardService
    {
        /// <summary>Robot Lair Damage for one fight: the damage
        /// actually dealt (HP removed — overkill does not count)
        /// to the boss and his drones, weighted by the boss
        /// level fought. Counted on any finished fight, win or
        /// lose.</summary>
        public static int LairDamagePoints(int bossLevel, int bossDamage, int droneDamage)
        {
            int dmg = Math.Max(0, bossDamage) + Math.Max(0, droneDamage);
            return dmg * Math.Max(1, bossLevel);
        }

        /// <summary>Banks a finished lair fight's damage on the
        /// career totals and returns the points earned.</summary>
        public static int AwardLairDamage(GameState state, int bossLevel, int bossDamage, int droneDamage)
        {
            int points = LairDamagePoints(bossLevel, bossDamage, droneDamage);
            state.LairBossDamage += Math.Max(0, bossDamage);
            state.LairDroneDamage += Math.Max(0, droneDamage);
            state.LairDamageScore += points;
            return points;
        }
    }
}
