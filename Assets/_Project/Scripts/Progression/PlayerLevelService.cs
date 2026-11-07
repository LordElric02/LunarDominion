using LunarDominion.Core;

namespace LunarDominion.Progression
{
    /// <summary>Player level (2026-11-07). Display only — it never
    /// affects combat, costs, or progression. Derived from the current
    /// GameState on every read (same pattern as PowerService), so it is
    /// never stored and never goes stale: an old save simply computes
    /// its level from what it already contains.
    ///
    /// Points = (Nexus-1)*100 + sum over other buildings (level-1)*20
    /// + sum over research node levels *15. Army is excluded, so the
    /// level never drops when units die in expeditions.
    /// Level = floor(Points / 200). Fresh base = Level 0, Nexus 15
    /// ≈ Level 15, Nexus 30 maxed ≈ Level 38.</summary>
    public class PlayerLevelService
    {
        public const int NexusPointsPerLevel = 100;
        public const int OtherBuildingPointsPerLevel = 20;
        public const int ResearchPointsPerLevel = 15;
        public const int PointsPerLevel = 200;

        private readonly GameState _state;

        public PlayerLevelService(GameState state)
        {
            _state = state;
        }

        public int Level()
        {
            return Points() / PointsPerLevel;
        }

        public int Points()
        {
            int total = 0;
            if (_state.BuildingLevels != null)
            {
                for (int i = 0; i < _state.BuildingLevels.Length; i++)
                {
                    int level = _state.BuildingLevels[i];
                    if (level <= 0) continue;
                    int perLevel = i == (int)BuildingId.CommandNexus
                        ? NexusPointsPerLevel
                        : OtherBuildingPointsPerLevel;
                    total += (level - 1) * perLevel;
                }
            }
            if (_state.ResearchLevels != null)
            {
                foreach (int level in _state.ResearchLevels)
                {
                    if (level > 0) total += level * ResearchPointsPerLevel;
                }
            }
            return total;
        }
    }
}
