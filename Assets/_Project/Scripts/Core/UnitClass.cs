namespace LunarDominion.Core
{
    /// <summary>Fixed unit class. Used by the counter triangle in Phase 4
    /// combat; never changes for a unit definition. Support (2026-11-15,
    /// the Medic) stands outside the triangle: CombatService's counter
    /// table names no pair for it, so it deals and takes neutral x1.0
    /// damage against everything.</summary>
    public enum UnitClass
    {
        Infantry,
        Ranged,
        Mechanized,
        Support
    }
}
