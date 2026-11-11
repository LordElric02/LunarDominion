using LunarDominion.Core;

namespace LunarDominion.Boss
{
    /// <summary>Hero skills for the turn-based boss fight
    /// (2026-11-10 spec): each hero has three, all unlocked from
    /// the start, on round cooldowns — no mana in v1. Skills are a
    /// boss-fight system; map camp fights stay auto-resolved with
    /// basic attacks only.</summary>
    public enum HeroSkillId
    {
        Rally,
        Bulwark,
        PrecisionStrike,
        Deadeye,
        PiercingShot,
        SuppressingFire,
        AegisProtocol,
        GuardianTaunt,
        ArmorBreak,
    }

    public class SkillDefinition
    {
        public HeroSkillId Id;
        public UnitId Hero;
        public string Name;
        public int Cooldown;

        /// <summary>True for damage skills: while drones are out
        /// the player picks a target (boss or drone) for them, like
        /// a basic attack. Buffs, debuffs and control skills always
        /// resolve on their fixed targets.</summary>
        public bool IsAttack;

        public string Blurb;
    }

    public static class SkillCatalog
    {
        // Slot order per hero is the button order in the lair's
        // turn panel (ForHero preserves this array's order).
        private static readonly SkillDefinition[] All =
        {
            // Commander Elena Reyes — Infantry: the commander.
            new SkillDefinition
            {
                Id = HeroSkillId.Rally, Hero = UnitId.HeroReyes, Name = "Rally",
                Cooldown = 4, IsAttack = false,
                Blurb = "All heroes +15% ATK for 3 rounds.",
            },
            new SkillDefinition
            {
                Id = HeroSkillId.Bulwark, Hero = UnitId.HeroReyes, Name = "Bulwark",
                Cooldown = 4, IsAttack = false,
                Blurb = "All heroes +30% DEF for 2 rounds.",
            },
            new SkillDefinition
            {
                Id = HeroSkillId.PrecisionStrike, Hero = UnitId.HeroReyes, Name = "Precision Strike",
                Cooldown = 3, IsAttack = true,
                Blurb = "One hit at 180% damage; the target's DEF counts half.",
            },
            // Sergeant Marcus Vega — Ranged: the damage dealer.
            new SkillDefinition
            {
                Id = HeroSkillId.Deadeye, Hero = UnitId.HeroVega, Name = "Deadeye",
                Cooldown = 4, IsAttack = false,
                Blurb = "Vega's next 2 attacks deal x1.5 damage.",
            },
            new SkillDefinition
            {
                Id = HeroSkillId.PiercingShot, Hero = UnitId.HeroVega, Name = "Piercing Shot",
                Cooldown = 3, IsAttack = true,
                Blurb = "One hit at 150% damage, ignoring DEF entirely.",
            },
            new SkillDefinition
            {
                Id = HeroSkillId.SuppressingFire, Hero = UnitId.HeroVega, Name = "Suppressing Fire",
                Cooldown = 4, IsAttack = false,
                Blurb = "The boss's ATK -20% for 2 rounds.",
            },
            // Lieutenant Ada Okafor — Mechanized: the tank.
            new SkillDefinition
            {
                Id = HeroSkillId.AegisProtocol, Hero = UnitId.HeroOkafor, Name = "Aegis Protocol",
                Cooldown = 5, IsAttack = false,
                Blurb = "Every hero gains a shield worth 20% of Okafor's max HP (3 rounds).",
            },
            new SkillDefinition
            {
                Id = HeroSkillId.GuardianTaunt, Hero = UnitId.HeroOkafor, Name = "Guardian Taunt",
                Cooldown = 4, IsAttack = false,
                Blurb = "For 2 rounds the boss's basic attacks must target Okafor.",
            },
            new SkillDefinition
            {
                Id = HeroSkillId.ArmorBreak, Hero = UnitId.HeroOkafor, Name = "Armor Break",
                Cooldown = 3, IsAttack = true,
                Blurb = "One hit at 130% damage; the boss's DEF -20% for 2 rounds.",
            },
        };

        public static SkillDefinition Get(HeroSkillId id)
        {
            foreach (var skill in All)
            {
                if (skill.Id == id) return skill;
            }
            return null;
        }

        /// <summary>The hero's three skills in button (slot)
        /// order; the turn panel indexes cooldowns by this
        /// order.</summary>
        public static SkillDefinition[] ForHero(UnitId hero)
        {
            var result = new SkillDefinition[3];
            int slot = 0;
            foreach (var skill in All)
            {
                if (skill.Hero != hero) continue;
                if (slot < result.Length) result[slot++] = skill;
            }
            return result;
        }
    }
}
