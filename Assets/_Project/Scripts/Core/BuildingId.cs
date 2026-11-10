namespace LunarDominion.Core
{
    /// <summary>Stable IDs for the buildings in the Lunar Dominion GDD
    /// plus the Research Lab (Research, 2026-11-01). These IDs are
    /// what the save file stores, so never renumber them.</summary>
    public enum BuildingId
    {
        CommandNexus = 0,
        MarineFoundry = 1,
        NovaArsenal = 2,
        AegisMotorPool = 3,
        HydroponicsDome = 4,
        AlloyExtractor = 5,
        HeliumHarvester = 6,
        ResearchLab = 7,
        // Hospital (2026-11-09): injured units are healed here.
        // Index 8; old saves with 8 entries load with it at 0 (locked).
        Hospital = 8
    }
}
