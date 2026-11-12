using System.Collections.Generic;
using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>Builds the nine GDD §5 unit definitions plus the three
    /// named heroes (2026-11-10) at runtime when the project has no
    /// UnitDefinition assets yet. Replace with assigned ScriptableObject
    /// assets later without changing any service code.</summary>
    public static class UnitCatalog
    {
        public static List<UnitDefinition> CreateDefaultDefinitions()
        {
            var list = new List<UnitDefinition>();
            // Infantry — Marine Foundry
            list.Add(Make(UnitId.ScoutMarine, "Scout Marine", UnitClass.Infantry, 1, BuildingId.MarineFoundry, 100, 18, 8, 40, 0, 0, 30));
            list.Add(Make(UnitId.AssaultMarine, "Assault Marine", UnitClass.Infantry, 2, BuildingId.MarineFoundry, 180, 32, 14, 120, 20, 0, 120));
            list.Add(Make(UnitId.EliteVanguard, "Elite Vanguard", UnitClass.Infantry, 3, BuildingId.MarineFoundry, 320, 55, 24, 350, 80, 0, 300));
            // Ranged — Nova Arsenal
            list.Add(Make(UnitId.PulseTrooper, "Pulse Trooper", UnitClass.Ranged, 1, BuildingId.NovaArsenal, 70, 28, 4, 0, 60, 20, 45));
            list.Add(Make(UnitId.Railgunner, "Railgunner", UnitClass.Ranged, 2, BuildingId.NovaArsenal, 120, 50, 7, 0, 150, 60, 180));
            list.Add(Make(UnitId.NovaSniper, "Nova Sniper", UnitClass.Ranged, 3, BuildingId.NovaArsenal, 200, 90, 12, 0, 400, 180, 480));
            // Mechanized — Aegis Motor Pool
            list.Add(Make(UnitId.ScoutRover, "Scout Rover", UnitClass.Mechanized, 1, BuildingId.AegisMotorPool, 200, 22, 18, 0, 100, 30, 60));
            list.Add(Make(UnitId.AssaultMech, "Assault Mech", UnitClass.Mechanized, 2, BuildingId.AegisMotorPool, 360, 38, 30, 0, 300, 100, 240));
            list.Add(Make(UnitId.TitanWalker, "Titan Walker", UnitClass.Mechanized, 3, BuildingId.AegisMotorPool, 650, 65, 48, 0, 900, 300, 600));
            // Heroes (2026-11-10): named characters, one of each, max 3.
            // All recruited from the Marine Foundry (the hero hall for
            // v1). They fight bosses in the lair view, not regular
            // squads. Reyes (Infantry) counters the Mechanized War
            // Robot; Vega (Ranged) is high damage / fragile; Okafor
            // (Mechanized) is the tank.
            list.Add(MakeHero(UnitId.HeroReyes, "Commander Elena Reyes", UnitClass.Infantry, 800, 120, 50, 50, 500, 500, 200));
            list.Add(MakeHero(UnitId.HeroVega, "Sergeant Marcus Vega", UnitClass.Ranged, 550, 180, 30, 65, 500, 500, 200));
            list.Add(MakeHero(UnitId.HeroOkafor, "Lieutenant Obinna Okafor", UnitClass.Mechanized, 1200, 80, 80, 40, 500, 500, 200));
            // Tiers 4-6 (2026-11-10): the endgame tiers. Tiers 1-3 grow
            // ~x1.75 per tier; tiers 4-6 grow ~x2.2 per tier — the jump
            // is deliberate: a maxed (Lv 20) full Tier 6 squad is the
            // first army in the game whose damage output reaches the
            // War Robot boss Level 15 (HP 196,869), and Tier 5 falls
            // short of it (Patrick: "you need a Tier 6 army to beat the
            // robot boss on level 15"). Unlocks: barracks 25 / 28 / 30
            // (barracks cannot exceed the Nexus, so Tier 6 is a Nexus
            // 30 capstone). Costs stay under the 8,000 storage cap.
            // Infantry — Marine Foundry (Biomass + Alloy)
            list.Add(Make(UnitId.BastionMarine, "Bastion Marine", UnitClass.Infantry, 4, BuildingId.MarineFoundry, 700, 121, 53, 900, 220, 0, 900));
            list.Add(Make(UnitId.AstralKnight, "Astral Knight", UnitClass.Infantry, 5, BuildingId.MarineFoundry, 1540, 266, 117, 2400, 600, 0, 2100));
            list.Add(Make(UnitId.EternalWarden, "Eternal Warden", UnitClass.Infantry, 6, BuildingId.MarineFoundry, 3390, 640, 257, 5500, 1500, 0, 4800));
            // Ranged — Nova Arsenal (Alloy + Helium-3)
            list.Add(Make(UnitId.IonMarksman, "Ion Marksman", UnitClass.Ranged, 4, BuildingId.NovaArsenal, 440, 198, 26, 0, 1000, 450, 1200));
            list.Add(Make(UnitId.QuasarCannoneer, "Quasar Cannoneer", UnitClass.Ranged, 5, BuildingId.NovaArsenal, 970, 436, 58, 0, 2600, 1100, 2700));
            list.Add(Make(UnitId.VoidAnnihilator, "Void Annihilator", UnitClass.Ranged, 6, BuildingId.NovaArsenal, 2130, 1050, 128, 0, 6000, 2600, 6000));
            // Mechanized — Aegis Motor Pool (Alloy + Helium-3)
            list.Add(Make(UnitId.JuggernautMech, "Juggernaut Mech", UnitClass.Mechanized, 4, BuildingId.AegisMotorPool, 1430, 143, 106, 0, 2200, 750, 1500));
            list.Add(Make(UnitId.ColossusWalker, "Colossus Walker", UnitClass.Mechanized, 5, BuildingId.AegisMotorPool, 3150, 315, 233, 0, 4800, 1700, 3400));
            list.Add(Make(UnitId.LeviathanPrime, "Leviathan Prime", UnitClass.Mechanized, 6, BuildingId.AegisMotorPool, 6930, 760, 513, 0, 7000, 3000, 7500));
            return list;
        }

        private static UnitDefinition MakeHero(UnitId id, string name, UnitClass cls,
            int hp, int atk, int def, int speed, int costB, int costA, int costH)
        {
            var unit = ScriptableObject.CreateInstance<UnitDefinition>();
            unit.Id = id;
            unit.DisplayName = name;
            unit.Class = cls;
            unit.Tier = 1;
            unit.BarracksBuilding = BuildingId.MarineFoundry;
            unit.BaseHealth = hp;
            unit.BaseAttack = atk;
            unit.BaseDefense = def;
            unit.BaseSpeed = speed;
            unit.BaseBiomassCost = costB;
            unit.BaseAlloyCost = costA;
            unit.BaseHelium3Cost = costH;
            unit.RecruitmentTimeSeconds = 0;
            unit.IsHero = true;
            return unit;
        }

        private static UnitDefinition Make(UnitId id, string name, UnitClass cls, int tier,
            BuildingId barracks, int hp, int atk, int def,
            int costB, int costA, int costH, int timeSeconds)
        {
            var unit = ScriptableObject.CreateInstance<UnitDefinition>();
            unit.Id = id;
            unit.DisplayName = name;
            unit.Class = cls;
            unit.Tier = tier;
            unit.BarracksBuilding = barracks;
            unit.BaseHealth = hp;
            unit.BaseAttack = atk;
            unit.BaseDefense = def;
            unit.BaseBiomassCost = costB;
            unit.BaseAlloyCost = costA;
            unit.BaseHelium3Cost = costH;
            unit.RecruitmentTimeSeconds = timeSeconds;
            return unit;
        }
    }
}
