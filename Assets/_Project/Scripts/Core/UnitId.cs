namespace LunarDominion.Core
{
    /// <summary>Stable unit IDs, in GDD §5 table order. Never renumber:
    /// save files store these values, and Phase 4 squads will too.
    /// Heroes (2026-11-10) are appended after the nine GDD units so
    /// the original values never shift.</summary>
    public enum UnitId
    {
        ScoutMarine,
        AssaultMarine,
        EliteVanguard,
        PulseTrooper,
        Railgunner,
        NovaSniper,
        ScoutRover,
        AssaultMech,
        TitanWalker,
        // Heroes (2026-11-10): named characters, one of each, max 3.
        // They fight bosses in the lair view, not regular squads.
        HeroReyes,
        HeroVega,
        HeroOkafor,
        // Tiers 4-6 (2026-11-10): appended after the heroes so the
        // original values never shift. Three per class, one per tier.
        BastionMarine,
        AstralKnight,
        EternalWarden,
        IonMarksman,
        QuasarCannoneer,
        VoidAnnihilator,
        JuggernautMech,
        ColossusWalker,
        LeviathanPrime
    }
}
