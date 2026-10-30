using System.Collections.Generic;
using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>Builds the nine GDD §5 unit definitions at runtime when the
    /// project has no UnitDefinition assets yet. Replace with assigned
    /// ScriptableObject assets later without changing any service code.</summary>
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
            return list;
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
