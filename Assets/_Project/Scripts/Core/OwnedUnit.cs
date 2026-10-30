using System;

namespace LunarDominion.Core
{
    /// <summary>One recruited unit instance in the player's army.
    /// Units are tracked individually, not just as counts: two units of
    /// the same type can end at different levels once Phase 4 XP exists.
    /// New units always start at level 1 with 0 XP (GDD C5).</summary>
    [Serializable]
    public class OwnedUnit
    {
        public UnitId Id;
        public int Level = 1;
        public int XP;

        public OwnedUnit() { }

        public OwnedUnit(UnitId id)
        {
            Id = id;
            Level = 1;
            XP = 0;
        }
    }
}
