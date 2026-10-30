using System.Collections.Generic;
using UnityEngine;

namespace LunarDominion.Core
{
    /// <summary>Builds the seven GDD building definitions at runtime when the
    /// project has no BuildingDefinition assets yet. Bases match GDD §4
    /// (unified formula: base x 1.22^(level-2)). Replace with assigned
    /// ScriptableObject assets later without changing any service code.</summary>
    public static class BuildingCatalog
    {
        public static List<BuildingDefinition> CreateDefaultDefinitions()
        {
            var list = new List<BuildingDefinition>();
            list.Add(Make(BuildingId.CommandNexus, "Command Nexus",
                "Main base. Gates every other building: no building may exceed Nexus level.",
                1, 200, 160, 0, 0, 0f));
            list.Add(Make(BuildingId.MarineFoundry, "Marine Foundry",
                "Infantry barracks. Trains Scout Marine, Assault Marine, Elite Vanguard.",
                1, 150, 100, 0, 0, 0f));
            list.Add(Make(BuildingId.NovaArsenal, "Nova Arsenal",
                "Ranged-unit barracks. Trains Pulse Trooper, Railgunner, Nova Sniper.",
                5, 0, 150, 50, 0, 0f));
            list.Add(Make(BuildingId.AegisMotorPool, "Aegis Motor Pool",
                "Mechanized-unit barracks. Trains Scout Rover, Assault Mech, Titan Walker.",
                8, 0, 250, 100, 0, 0f));
            list.Add(Make(BuildingId.HydroponicsDome, "Hydroponics Dome",
                "Generates Biomass.",
                1, 0, 150, 0, 1, 24f));
            list.Add(Make(BuildingId.AlloyExtractor, "Alloy Extractor",
                "Generates Alloy.",
                1, 150, 0, 0, 2, 18f));
            list.Add(Make(BuildingId.HeliumHarvester, "Helium-3 Harvester",
                "Generates Helium-3.",
                4, 200, 150, 0, 3, 6f));
            return list;
        }

        private static BuildingDefinition Make(BuildingId id, string name, string desc,
            int unlock, int baseB, int baseA, int baseH, int produced, float prodPerHour)
        {
            var def = ScriptableObject.CreateInstance<BuildingDefinition>();
            def.Id = id;
            def.DisplayName = name;
            def.Description = desc;
            def.UnlockNexusLevel = unlock;
            def.BaseBiomassCost = baseB;
            def.BaseAlloyCost = baseA;
            def.BaseHelium3Cost = baseH;
            def.ProducedResource = produced;
            def.BaseProductionPerHour = prodPerHour;
            return def;
        }
    }
}
