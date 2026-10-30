using System;
using LunarDominion.Core;

namespace LunarDominion.Expeditions
{
    /// <summary>One enemy type inside an encounter, and how many of it
    /// appear. A single spawn with Count > 1 is a pack, not one unit
    /// with multiplied stats — each copy fights and dies individually.</summary>
    [Serializable]
    public struct EnemySpawn
    {
        public string DisplayName;
        public UnitClass Class;
        public int Health;
        public int Attack;
        public int Defense;
        public int Count;
    }
}
