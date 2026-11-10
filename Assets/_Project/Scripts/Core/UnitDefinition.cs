using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>Data for one unit, from GDD §5 (all values provisional there).
    /// Create assets via Assets > Create > Lunar Dominion > Unit Definition.
    /// If no assets are assigned, UnitCatalog creates these at runtime
    /// from the same GDD numbers.</summary>
    [CreateAssetMenu(fileName = "NewUnitDefinition", menuName = "Lunar Dominion/Unit Definition")]
    public class UnitDefinition : ScriptableObject
    {
        public UnitId Id;
        public string DisplayName = "Unit";
        public UnitClass Class;
        public int Tier = 1;

        // The barracks building that trains this unit.
        public BuildingId BarracksBuilding;

        public int BaseHealth;
        public int BaseAttack;
        public int BaseDefense;

        // Hero (2026-11-10): a named character, not a regular troop.
        // Heroes are recruited once each (one of each, max 3), live in
        // GameState.Heroes (not the Army), and fight bosses in the lair
        // view. Regular units have this false.
        public bool IsHero;

        public int BaseBiomassCost;
        public int BaseAlloyCost;
        public int BaseHelium3Cost;

        // Final-design recruitment time (seconds). In the prototype this
        // is 0 (instant, GDD C1) — the field still exists so a timed
        // queue can be restored later without changing the data shape.
        public int RecruitmentTimeSeconds;

        public ResourceCost Cost => new ResourceCost(BaseBiomassCost, BaseAlloyCost, BaseHelium3Cost);
    }
}
