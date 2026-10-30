using System;
using LunarDominion.Core;

namespace LunarDominion.Units
{
    /// <summary>Level scaling and XP thresholds from GDD §5–§6.
    /// Applied at display / battle time, never stored as new base stats.</summary>
    public static class UnitStats
    {
        public static int ScaledHealth(UnitDefinition def, int level)
        {
            return (int)Math.Round(def.BaseHealth * (1f + 0.08f * (level - 1)));
        }

        public static int ScaledAttack(UnitDefinition def, int level)
        {
            return (int)Math.Round(def.BaseAttack * (1f + 0.08f * (level - 1)));
        }

        public static int ScaledDefense(UnitDefinition def, int level)
        {
            return (int)Math.Round(def.BaseDefense * (1f + 0.05f * (level - 1)));
        }

        /// <summary>XP to go from this level to the next (GDD §6).</summary>
        public static int XpToNextLevel(int level)
        {
            return (int)Math.Round(80f * Math.Pow(level, 1.4f));
        }
    }
}
