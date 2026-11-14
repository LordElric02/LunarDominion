using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LunarDominion.Core;

namespace LunarDominion.UI
{
    /// <summary>What a tappable map point is: a resource node (Biomass,
    /// Alloy, Helium-3), an enemy camp (one per Expedition tier), or the
    /// boss lair (War Robot, 2026-11-09).</summary>
    public enum MapPointKind
    {
        Biomass,
        Alloy,
        Helium3,
        Camp,
        BossLair
    }

    /// <summary>Structured data for one tappable map point — the display
    /// strings plus what the excursion rules need (kind, tier, amount,
    /// position, and the index into the node/camp lists). Nodes are
    /// indices 0-8 (in NodeDefs order); the boss lair is 12; enemy
    /// camps are the 15 camp slots at indices 100-114 (camp field,
    /// 2026-11-10) whose tier is set dynamically per spawn.</summary>
    public class MapPointData
    {
        public int Index;
        public MapPointKind Kind;
        public int Tier; // camps only (1-3); 0 for nodes
        public int Amount; // nodes only (500/1000/2000); 0 for camps
        public Vector3 Position;
        public string Title;
        public string Info;
    }

    /// <summary>Exterior map view (Phase 1 of the exterior base mode).
    /// A large sculpted terrain (560x480 — 4x the area of the original
    /// 280x240 map, doubled 2026-11-12) with a sci-fi
    /// castle standing in for the base (like the castle on the world
    /// map in Age of Empires Mobile), plus resource nodes and enemy
    /// camps scattered across it. The Map tab swaps the camera
    /// from the base position (10,16,10) to a pulled-back exterior
    /// position (50,80,50) — same 48.5-degree pitch, 5x further away,
    /// so the framing matches the base view exactly. In the Map view
    /// the detailed buildings are hidden (the Bootstrap hides them)
    /// and the bold BaseCastle represents the base; the ground,
    /// mountains and planet stay visible in both views.
    /// Tapping a node or camp shows its info (resource type + amount,
    /// or enemy tier); tapping empty map hides the info panel. Sending
    /// armies (map excursions, 2026-11-09) marches a marker from the
    /// castle to the point and resolves the outcome on arrival — node
    /// collection or camp combat via ExpeditionService. The march is
    /// visual only: switching away from the Map tab cancels it before
    /// any outcome is applied. No balance rules live here.</summary>
    public class ExteriorView : MonoBehaviour
    {
        // Raised when a tap lands on a map node or camp, with the
        // point's structured data (not just display strings).
        public event Action<MapPointData> OnMapPointSelected;
        // Raised when a tap lands on empty map ground (not a node/camp).
        public event Action OnEmptyMapTapped;
        // Raised by the BASE button beside FIND (Patrick
        // 2026-11-13): with the nav bar hidden on the Map, this
        // is the way back to the Base tab.
        public event Action OnBackToBaseClicked;

        // True while the Map tab is the active view.
        public bool IsVisible => _visible;

        // Exterior camera: 5x the base camera distance, same pitch.
        // Base camera (10,16,10) frames the 56x48 ground; the
        // exterior framing is unchanged since the original map —
        // the 2026-11-12 enlargement keeps this camera and the
        // player pans across the bigger map (see PanLimitX/Z).
        private static readonly Vector3 ExteriorCameraPosition = new Vector3(50f, 80f, 50f);

        private GameObject _terrain;
        private GameObject _baseCastle;
        private bool _visible;

        // A tappable point on the map: a resource node or an enemy camp.
        // Renderers + original colours are kept so a depleted node can
        // be dimmed and later restored without touching other points
        // that share the same material.
        private class MapPoint
        {
            public GameObject Go;
            public MapPointData Data;
            public Renderer[] Renderers;
            public Color[] OriginalColors;
        }
        private readonly List<MapPoint> _mapPoints = new List<MapPoint>();

        // Excursion march marker (2026-11-09): a bright capsule that
        // carries the army from the castle to the tapped point and back.
        // Pure visual — the rules live in MapExcursionService. The march
        // only runs while the Map is visible; leaving the Map tab cancels
        // it (the service calls CancelMarch before any outcome applies).
        // Excursion march markers, one per army slot (two armies,
        // 2026-11-11): slot 0 = Army 1 (cyan), slot 1 = Army 2
        // (orange). Each slot marches independently.
        // One marker set per standing army (3 since 2026-11-12;
        // sized from GameState.MaxArmies so a future army cap
        // raise only touches the data).
        private readonly GameObject[] _marchMarkers = new GameObject[GameState.MaxArmies];
        private readonly bool[] _marching = new bool[GameState.MaxArmies];
        private readonly Vector3[] _marchTargets = new Vector3[GameState.MaxArmies];
        private readonly Action[] _marchCallbacks = new Action[GameState.MaxArmies];
        // March speed: 20 u/s when the map enlarged (positions
        // doubled, so trip times matched the old map's 10 u/s);
        // slowed to 14 u/s for Phase 2 (Patrick 2026-11-12) — real
        // marching figures read hurried at 20, and a far corner now
        // takes ~24s, which suits an army on the move.
        private static readonly float MarchSpeed = 14f; // units/second

        // Map Phase 2 (2026-11-12, Patrick's pick: real Blender mini
        // troops over code-built figures): each slot's marker root
        // carries a formation of mini troop figures — one per squad
        // member, heroes leading on their full models — plus an army
        // banner with a floating count chip and a dotted route line
        // while marching. The root marches exactly as the old capsule
        // did; everything here is presentation. Figure modes:
        // 0 marching (two columns), 1 parked (ring around the node,
        // farming), 2 fighting (ring around the camp).
        private const int SquadModeMarching = 0;
        private const int SquadModeParked = 1;
        private const int SquadModeFighting = 2;
        private readonly List<GameObject>[] _squadFigures = CreateSlotLists<GameObject>();
        private readonly List<UnitId>[] _squadMembers = CreateSlotLists<UnitId>();
        private readonly string[] _squadKeys = new string[GameState.MaxArmies];
        private readonly int[] _squadModes = new int[GameState.MaxArmies];
        private readonly bool[] _squadAnimOwned = new bool[GameState.MaxArmies];
        private readonly TextMesh[] _squadChips = new TextMesh[GameState.MaxArmies];
        private readonly List<GameObject>[] _routeDots = CreateSlotLists<GameObject>();
        private readonly Material[] _routeDotMats = new Material[GameState.MaxArmies];

        private static List<T>[] CreateSlotLists<T>()
        {
            var lists = new List<T>[GameState.MaxArmies];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<T>();
            return lists;
        }
        private float _squadAnimTime;
        // Army colours: 1 cyan, 2 orange, 3 violet (2026-11-12).
        private static readonly Color[] ArmyColors = { new Color(0.2f, 0.9f, 1.0f), new Color(1.0f, 0.55f, 0.15f), new Color(0.62f, 0.38f, 1.0f) };
        // The mini models were authored facing +X in Blender; after
        // the FBX import the visual front sits at -Z, so the facing
        // pivot flips 180 degrees to put it on the wrapper's +Z.
        // If a Play check ever shows troops marching backwards, this
        // constant is the one-line fix.
        private const float FigureFacingOffsetDeg = 180f;
        private const float TroopFigureScale = 1.8f;
        private GameObject _miniInfantryPrefab;
        private GameObject _miniRangedPrefab;
        private GameObject _miniMechPrefab;
        private Dictionary<UnitId, UnitClass> _classById;

        // Map camera pan (Patrick 2026-11-09): the Map view is 5x the
        // base, so the camera can be panned across it — arrow keys on
        // PC, touch/mouse drag on the phone (and in the Editor). The
        // pan offset moves both the camera position and its look-at
        // point, so the 48.5-degree pitch never changes. Clamped so the
        // view stays on the 560x480 map.
        private Vector3 _panOffset = Vector3.zero;
        private static readonly float PanSpeed = 40f; // units/second, arrow keys
        private static readonly float PanLimitX = 200f;
        private static readonly float PanLimitZ = 180f;
        private static readonly float DragTapThresholdPx = 15f; // below this, a press is a tap, not a drag

        // Drag state: a press that moves past the threshold becomes a
        // drag (pan) and must NOT also fire as a tap on release.
        private bool _pointerDown;
        private bool _isDrag;
        private Vector2 _pointerStartPos;
        private Vector2 _pointerLastPos;

        // Camera ground-plane axes, derived from the exterior camera
        // (50,80,50) looking at the origin: screen-right is
        // (0.707,0,-0.707) and screen-up (toward the far edge) is
        // (-0.707,0,-0.707). Arrow keys and drags pan along these.
        private static readonly Vector3 CamRight = new Vector3(0.7071f, 0f, -0.7071f);
        private static readonly Vector3 CamGroundForward = new Vector3(-0.7071f, 0f, -0.7071f);

        // First-iteration layout (Patrick 2026-11-09): 3 nodes per
        // resource (one 500, one 1000, one 2000 — farther nodes hold
        // more), and 1 enemy camp per Expedition tier (Tier 1 near the
        // base, Tier 2 mid-range, Tier 3 far out). Positions are world
        // coords on the 560x480 map (x ±280, z ±240; doubled from the
        // original layout 2026-11-12), all outside the
        // central 56x48 base footprint. Node indices 0-8 are the
        // depletion state indices (GameState.NodeRegenUtc); camps are
        // 9-11 and never deplete.
        private struct NodeDef
        {
            public string ResourcePath; // Resources/Models/<name>
            public MapPointKind Kind;
            public int Tier; // camps only (1-3)
            public int Amount; // nodes only (500/1000/2000)
            public string Title;
            public string Info;
            public Vector3 Position;
        }
        private static readonly NodeDef[] NodeDefs =
        {
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 500, Title = "Biomass Node", Info = "Biomass: 500", Position = new Vector3(70f, 0f, 50f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 1000, Title = "Biomass Node", Info = "Biomass: 1000", Position = new Vector3(-110f, 0f, 90f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 2000, Title = "Biomass Node", Info = "Biomass: 2000", Position = new Vector3(190f, 0f, -110f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 500, Title = "Alloy Node", Info = "Alloy: 500", Position = new Vector3(-60f, 0f, -70f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 1000, Title = "Alloy Node", Info = "Alloy: 1000", Position = new Vector3(130f, 0f, 110f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 2000, Title = "Alloy Node", Info = "Alloy: 2000", Position = new Vector3(-210f, 0f, -130f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 500, Title = "Helium-3 Node", Info = "Helium-3: 500", Position = new Vector3(90f, 0f, -80f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 1000, Title = "Helium-3 Node", Info = "Helium-3: 1000", Position = new Vector3(-140f, 0f, -40f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 2000, Title = "Helium-3 Node", Info = "Helium-3: 2000", Position = new Vector3(220f, 0f, 150f) },
            new NodeDef { ResourcePath = "Models/EnemyCampT1", Kind = MapPointKind.Camp, Tier = 1, Title = "Spore Crawler Camp", Info = "Tier 1 — Spore Crawlers\nExpedition enemy", Position = new Vector3(110f, 0f, 30f) },
            new NodeDef { ResourcePath = "Models/EnemyCampT2", Kind = MapPointKind.Camp, Tier = 2, Title = "Void Stalker Camp", Info = "Tier 2 — Void Stalkers\nExpedition enemy", Position = new Vector3(-170f, 0f, 120f) },
            new NodeDef { ResourcePath = "Models/EnemyCampT3", Kind = MapPointKind.Camp, Tier = 3, Title = "Brood Tyrant Camp", Info = "Tier 3 — Brood Tyrant\nExpedition enemy", Position = new Vector3(240f, 0f, -160f) },
            // Boss lair (2026-11-09): the War Robot lives here, far
            // southwest corner of the map. Index 12. The lair does not
            // move on respawn (unlike camps) — it is a fixed place.
            new NodeDef { ResourcePath = "Models/BossLair", Kind = MapPointKind.BossLair, Title = "War Robot Lair", Info = "BOSS — War Robot\nGiant Mechanized enemy\nUnlocks at Nexus 10", Position = new Vector3(-240f, 0f, -200f) },
        };

        // Enemy camp field (2026-11-10, replaces the 3 fixed camps and
        // their respawn spots): 15 camp slots spread across the map in
        // rings — near, mid, and far corners — all clear of the base,
        // the resource nodes, the boss lair, and each other. A slot's
        // camp tier is dynamic (rolled per spawn by MapExcursionService,
        // weighted by Nexus level), so a slot shows the one standout
        // structure for its CURRENT tier, swapped in on demand (the
        // creature figures that used to ring the camps were removed
        // 2026-11-14). Slot points carry Index = CampSlotIndexBase + slot
        // so they never collide with node (0-8) or lair (12) indices.
        public const int CampSlotCount = 15;
        public const int CampSlotIndexBase = 100;
        private static readonly Vector3[] CampSlotPositions =
        {
            new Vector3(110f, 0f, 30f), new Vector3(-80f, 0f, 160f), new Vector3(200f, 0f, 80f),
            new Vector3(-170f, 0f, 120f), new Vector3(40f, 0f, -180f), new Vector3(-240f, 0f, -20f),
            new Vector3(240f, 0f, -160f), new Vector3(0f, 0f, 200f), new Vector3(120f, 0f, -200f),
            new Vector3(170f, 0f, 20f), new Vector3(-190f, 0f, 30f), new Vector3(30f, 0f, 120f),
            new Vector3(-30f, 0f, -190f), new Vector3(250f, 0f, 60f), new Vector3(-120f, 0f, -170f),
        };
        // What each slot currently shows: the tier built (0 = empty)
        // and whether it should be visible (the service owns the state;
        // the view only mirrors it, and visibility also respects
        // whether the Map itself is open).
        private readonly int[] _campSlotTier = new int[CampSlotCount];
        private readonly bool[] _campSlotVisible = new bool[CampSlotCount];
        private readonly MapPoint[] _campSlotMarkers = new MapPoint[CampSlotCount];
        private readonly GameObject[] _campSlotStructures = new GameObject[CampSlotCount];

        // One standout structure per camp tier (2026-11-14: the
        // creature figures that used to ring each camp are gone -
        // Patrick: "just one structure that stands out"). Loaded
        // once in BuildCampSlots; SetCampSlot swaps the slot's
        // child to the current tier's model.
        private static readonly string[] CampStructurePaths =
        {
            null, // tier 0 = empty slot, no structure
            "Models/EnemyCampT1",
            "Models/EnemyCampT2",
            "Models/EnemyCampT3",
        };
        private readonly GameObject[] _campPrefabs = new GameObject[4];

        // Sculpted terrain height (2026-11-12, map revisit Phase 1):
        // the ExteriorTerrainV2 mesh is an analytic heightfield
        // (dunes + broad highland rises + craters), flattened to
        // h=0 on discs at the base, every node, every camp slot
        // and the lair, so sites sit exactly as they always have.
        // This function mirrors BuildExteriorTerrainV2.py's
        // height_at() EXACTLY (same formulas, same constants) so
        // the marching markers can ride the terrain. The mesh
        // itself sits at y -0.05 (see Build()).
        private static readonly float[,] TerrainHighlands =
        {
            { -190f, 150f, 3.9f, 55f },
            { 205f, -160f, 3.3f, 48f },
            { 150f, 170f, 2.7f, 42f },
        };
        // (cx, cz, radius, depth, rim height, type) — types mirror
        // BuildExteriorTerrainV4.py: 0 simple (legacy profile),
        // 1 complex (flat floor, central peak, terraced walls),
        // 2 degraded (shallow, soft, wide worn rim), 3 fresh
        // (deeper, crisp rim; ejecta/rays are tint-only). The old
        // (40,-40) crater is now a 5-pit secondary chain.
        private static readonly float[,] TerrainCraters =
        {
            { -60f, -120f, 22f, 5.0f, 3.2f, 1f },
            { 60f, 130f, 18f, 4.0f, 2.6f, 3f },
            { 150f, -60f, 14f, 3.2f, 2.1f, 0f },
            { -160f, -90f, 16f, 3.5f, 2.2f, 2f },
            { 230f, 120f, 20f, 4.5f, 2.9f, 1f },
            { -230f, 90f, 15f, 3.4f, 2.2f, 0f },
            { 90f, -150f, 12f, 2.7f, 1.8f, 2f },
            { -40f, 40f, 11f, 2.4f, 1.6f, 0f },
            { 180f, 190f, 16f, 3.5f, 2.2f, 3f },
            { -110f, -180f, 19f, 4.3f, 2.7f, 1f },
            { 28f, -52f, 4.2f, 1.1f, 0.7f, 0f },
            { 34f, -47f, 5.2f, 1.4f, 0.9f, 0f },
            { 40f, -42f, 4.6f, 1.2f, 0.8f, 0f },
            { 46f, -37f, 3.8f, 1.0f, 0.6f, 0f },
            { 52f, -32f, 3.2f, 0.8f, 0.5f, 0f },
            { 260f, -40f, 13f, 2.9f, 1.9f, 0f },
            { -260f, -140f, 17f, 3.8f, 2.4f, 2f },
            { 120f, 60f, 10f, 2.2f, 1.4f, 0f },
            { -190f, 190f, 14f, 3.2f, 2.1f, 0f },
            { 10f, -110f, 13f, 2.9f, 1.9f, 2f },
        };
        // How high a march marker rides above the terrain surface:
        // the mesh sits at -0.05, and markers historically rode at
        // y 1 over flat ground — 0.95 keeps that at the sites.
        private static readonly float MarkerRideHeight = 0.95f;

        private static float TerrainSmoothstep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // One crater's height contribution at q = dist / radius.
        // Mirrors crater_height in BuildExteriorTerrainV4.py —
        // keep the math identical (types listed at TerrainCraters).
        private static float TerrainCraterHeight(float q, float depth, float rim, int ctype)
        {
            if (ctype == 1) // complex: flat floor, central peak, terraces
            {
                if (q < 1f)
                {
                    float peak = 0.38f * depth * Mathf.Exp(-((q / 0.16f) * (q / 0.16f)));
                    if (q < 0.42f) return -0.92f * depth + peak;
                    float t = (q - 0.42f) / 0.58f;
                    return -0.92f * depth * Mathf.Pow(1f - t, 1.35f)
                        + depth * 0.05f * Mathf.Sin(6f * Mathf.PI * t) * (4f * t * (1f - t))
                        + peak;
                }
                if (q < 1.7f)
                {
                    float sn = Mathf.Sin(Mathf.PI * (q - 1f) / 0.7f);
                    return 1.1f * rim * sn * sn;
                }
                return 0f;
            }
            if (ctype == 2) // degraded: shallow, softened, wide worn rim
            {
                if (q < 1f) return -0.45f * depth * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(Mathf.PI * q), 1.3f);
                if (q < 2.1f)
                {
                    float sn = Mathf.Sin(Mathf.PI * (q - 1f) / 1.1f);
                    return 0.4f * rim * sn * sn;
                }
                return 0f;
            }
            if (ctype == 3) // fresh: deeper bowl, crisp narrow rim
            {
                if (q < 1f) return -1.12f * depth * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * q));
                if (q < 1.5f)
                {
                    float sn = Mathf.Sin(Mathf.PI * (q - 1f) / 0.5f);
                    return 1.25f * rim * sn * sn;
                }
                return 0f;
            }
            // simple (0): the legacy profile, unchanged
            if (q < 1f) return -depth * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * q));
            if (q < 1.7f)
            {
                float sn = Mathf.Sin(Mathf.PI * (q - 1f) / 0.7f);
                return rim * sn * sn;
            }
            return 0f;
        }

        /// <summary>The sculpted terrain's height at (x, z), in
        /// world units (before the mesh's -0.05 offset). Mirrors
        /// BuildExteriorTerrainV4.py — change both together.</summary>
        public static float TerrainHeight(float x, float z)
        {
            float d = 2.4f * Mathf.Sin(0.021f * x + 1.7f) * Mathf.Sin(0.017f * z - 0.6f)
                    + 1.75f * Mathf.Sin(0.043f * x - 2.1f) * Mathf.Sin(0.052f * z + 1.2f)
                    + 0.95f * Mathf.Sin(0.110f * x + 0.3f) * Mathf.Sin(0.090f * z + 2.9f);
            for (int i = 0; i < TerrainHighlands.GetLength(0); i++)
            {
                float dx = x - TerrainHighlands[i, 0];
                float dz = z - TerrainHighlands[i, 1];
                float sigma = TerrainHighlands[i, 3];
                d += TerrainHighlands[i, 2] * Mathf.Exp(-(dx * dx + dz * dz) / (2f * sigma * sigma));
            }
            for (int i = 0; i < TerrainCraters.GetLength(0); i++)
            {
                float dist = Mathf.Sqrt((x - TerrainCraters[i, 0]) * (x - TerrainCraters[i, 0])
                                      + (z - TerrainCraters[i, 1]) * (z - TerrainCraters[i, 1]));
                d += TerrainCraterHeight(dist / TerrainCraters[i, 2], TerrainCraters[i, 3],
                    TerrainCraters[i, 4], (int)TerrainCraters[i, 5]);
            }
            float m = 1f;
            // Base disc.
            float baseDist = Mathf.Sqrt(x * x + z * z);
            m = Mathf.Min(m, TerrainSmoothstep(42f, 60f, baseDist));
            // Node + lair discs (positions from NodeDefs so the two
            // can never drift apart; camps are not in NodeDefs).
            foreach (var def in NodeDefs)
            {
                if (def.Kind == MapPointKind.Camp) continue;
                float rIn = def.Kind == MapPointKind.BossLair ? 19f : 10f;
                float rOut = def.Kind == MapPointKind.BossLair ? 28f : 17f;
                float dx = x - def.Position.x;
                float dz = z - def.Position.z;
                m = Mathf.Min(m, TerrainSmoothstep(rIn, rOut, Mathf.Sqrt(dx * dx + dz * dz)));
            }
            // Camp slot discs.
            foreach (var pos in CampSlotPositions)
            {
                float dx = x - pos.x;
                float dz = z - pos.z;
                m = Mathf.Min(m, TerrainSmoothstep(21f, 30f, Mathf.Sqrt(dx * dx + dz * dz)));
            }
            m *= 1f - TerrainSmoothstep(258f, 280f, Mathf.Abs(x));
            m *= 1f - TerrainSmoothstep(218f, 240f, Mathf.Abs(z));
            return Mathf.Clamp(d * m, -5.2f, 8.0f);
        }

        public static string CampTitleForTier(int tier)
        {
            switch (tier)
            {
                case 2: return "Void Stalker Camp";
                case 3: return "Brood Tyrant Camp";
                default: return "Spore Crawler Camp";
            }
        }

        public static string CampInfoForTier(int tier)
        {
            switch (tier)
            {
                case 2: return "Tier 2 — Void Stalkers\nExpedition enemy";
                case 3: return "Tier 3 — Brood Tyrant\nExpedition enemy";
                default: return "Tier 1 — Spore Crawlers\nExpedition enemy";
            }
        }

        private static MapPointData CampSlotData(int slot, int tier)
        {
            return new MapPointData
            {
                Index = CampSlotIndexBase + slot,
                Kind = MapPointKind.Camp,
                Tier = tier,
                Amount = 0,
                Position = CampSlotPositions[slot],
                Title = CampTitleForTier(tier),
                Info = CampInfoForTier(tier)
            };
        }

        // Build the 15 camp slot roots (empty GameObjects at the
        // slot positions, each with a generous tap collider). The
        // tier structures are NOT built here - they depend on the
        // slot's current tier and are swapped in by SetCampSlot.
        private void BuildCampSlots()
        {
            for (int tier = 1; tier <= 3; tier++)
            {
                try
                {
                    _campPrefabs[tier] = Resources.Load<GameObject>(CampStructurePaths[tier]);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[ExteriorView] Camp structure failed to load (" + CampStructurePaths[tier] + "): " + e.Message);
                }
                if (_campPrefabs[tier] == null)
                {
                    Debug.LogWarning("[ExteriorView] " + CampStructurePaths[tier] + ".fbx is missing - tier " + tier + " camps will show nothing. Copy the FBX into Assets/_Project/Resources/Models/ (do not rename it).");
                }
            }
            for (int slot = 0; slot < CampSlotCount; slot++)
            {
                _campSlotTier[slot] = 0;
                _campSlotVisible[slot] = false;
                var go = new GameObject("Empty Camp Slot");
                go.transform.position = CampSlotPositions[slot];
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 4f, 0f);
                box.size = new Vector3(14f, 9f, 14f);
                go.SetActive(false);
                var point = new MapPoint
                {
                    Go = go,
                    Data = CampSlotData(slot, 0),
                    Renderers = new Renderer[0],
                    OriginalColors = new Color[0]
                };
                _campSlotMarkers[slot] = point;
                _mapPoints.Add(point);
            }
        }

        /// <summary>Show a camp slot as the given tier (0 = empty):
        /// swaps the slot's structure for the tier and updates the
        /// marker's data in place (so a panel already showing this camp
        /// stays correct). No-op when the slot already shows that tier.
        /// Visibility is separate — see SetCampSlotVisible.</summary>
        public void SetCampSlot(int slot, int tier)
        {
            if (slot < 0 || slot >= CampSlotCount) return;
            if (tier < 0 || tier > 3) tier = 0;
            if (_campSlotTier[slot] == tier) return;
            DestroySlotStructure(slot);
            _campSlotTier[slot] = tier;
            var marker = _campSlotMarkers[slot];
            if (marker != null)
            {
                // Mutate the existing data (never replace it): the
                // Bootstrap may be holding this exact instance as the
                // selected point.
                marker.Data.Tier = tier;
                marker.Data.Title = CampTitleForTier(tier);
                marker.Data.Info = CampInfoForTier(tier);
                if (marker.Go != null) marker.Go.name = tier > 0 ? CampTitleForTier(tier) : "Empty Camp Slot";
            }
            if (tier > 0) BuildSlotStructure(slot, tier);
            ApplySlotVisibility(slot);
        }

        /// <summary>Show or hide a camp slot (root + structure). An
        /// empty slot is never shown. The actual active state also
        /// respects whether the Map itself is open, so a camp that
        /// respawns while the player is on another tab never pops into
        /// the Base view.</summary>
        public void SetCampSlotVisible(int slot, bool visible)
        {
            if (slot < 0 || slot >= CampSlotCount) return;
            _campSlotVisible[slot] = visible && _campSlotTier[slot] > 0;
            ApplySlotVisibility(slot);
        }

        private void ApplySlotVisibility(int slot)
        {
            bool active = _visible && _campSlotVisible[slot];
            var marker = _campSlotMarkers[slot];
            if (marker != null && marker.Go != null) marker.Go.SetActive(active);
        }

        // The creature FBXs are authored around their centres,
        // so placed at y=0 they sit sunk into the ground by half
        // their height (Patrick 2026-11-12: the crawlers and
        // stalkers looked buried in moon sand). Lift the
        // instance so its lowest rendered point rests exactly
        // on the ground. Measured from the real bounds, so it
        // is right for every creature model whatever its origin.
        private static void GroundSnap(GameObject go)
        {
            float minY = float.MaxValue;
            foreach (var rend in go.GetComponentsInChildren<Renderer>())
            {
                if (rend.bounds.min.y < minY) minY = rend.bounds.min.y;
            }
            if (minY < -0.001f && minY != float.MaxValue)
            {
                go.transform.position += new Vector3(0f, -minY, 0f);
            }
        }

        // Swap in the tier's structure as the slot root's child
        // (authored at final size, so scale 1; the imported
        // rotation is left alone, per the FBX rule) and refresh
        // the marker's renderer cache so the per-renderer tinting
        // in Update keeps working with the new model.
        private void BuildSlotStructure(int slot, int tier)
        {
            var marker = _campSlotMarkers[slot];
            if (marker == null || marker.Go == null) return;
            var prefab = _campPrefabs[tier];
            if (prefab == null) return;
            var go = Instantiate(prefab);
            go.name = CampTitleForTier(tier) + " Structure";
            go.transform.SetParent(marker.Go.transform, false);
            go.transform.localPosition = Vector3.zero;
            _campSlotStructures[slot] = go;
            var renderers = go.GetComponentsInChildren<Renderer>();
            var colors = new Color[renderers.Length];
            for (int r = 0; r < renderers.Length; r++) colors[r] = renderers[r].material.color;
            marker.Renderers = renderers;
            marker.OriginalColors = colors;
        }

        private void DestroySlotStructure(int slot)
        {
            var go = _campSlotStructures[slot];
            if (go != null) Destroy(go);
            _campSlotStructures[slot] = null;
            var marker = _campSlotMarkers[slot];
            if (marker != null)
            {
                marker.Renderers = new Renderer[0];
                marker.OriginalColors = new Color[0];
            }
        }

        public void Build()
        {
            GameObject terrainPrefab = null;
            try
            {
                terrainPrefab = Resources.Load<GameObject>("Models/ExteriorTerrain");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ExteriorView] Exterior terrain failed to load (Models/ExteriorTerrain): " + e.Message);
            }

            if (terrainPrefab == null)
            {
                Debug.LogWarning("[ExteriorView] ExteriorTerrain.fbx is missing — the Map tab will show the base ground only. Copy ExteriorTerrain.fbx into Assets/_Project/Resources/Models/ (do not rename it).");
                return;
            }

            _terrain = Instantiate(terrainPrefab);
            _terrain.name = "ExteriorGround";
            // Slightly below the base ground (y 0) so the two meshes
            // never z-fight where they overlap: the exterior mesh is
            // flat (h 0) across the central 56x48 base footprint, and
            // the base ground sits 0.05 above it. The step is invisible
            // from the exterior camera. Do NOT set the rotation here —
            // same Bake Axis Conversion lesson as the base terrain
            // (see BaseView.BuildGround): the FBX carries a -90 deg X
            // axis conversion, and forcing identity would render the
            // heightfield as a vertical slab.
            _terrain.transform.position = new Vector3(0f, -0.05f, 0f);
            _terrain.transform.localScale = Vector3.one;

            // Regolith texture, same density as the base ground: the
            // base tiles (8,7) across 56x48 (1 tile per 7 units), so
            // the 560x480 map tiles (80,68). Wrap Mode Repeat. The
            // colour is darkened to 0.7 (Patrick 2026-11-09: the map
            // terrain read almost white/snowy at (40,34) from the far
            // camera — the fine texture averages out to light grey, so
            // the material colour pulls it back toward lunar grey).
            var renderer = _terrain.GetComponentInChildren<Renderer>();
            // Terrain V3 (2026-11-12): when the baked tint texture
            // is installed (BuildExteriorTerrainV3.py writes
            // ExteriorTerrainTint.png into Resources/Textures), it
            // replaces the tiling regolith on the terrain — it
            // carries the soft crater shading and mottling the
            // mockup look needs, authored at the regolith palette,
            // so the material colour stays white and the UVs map
            // 1:1. Without it, the tiling regolith below applies
            // exactly as before.
            var tint = Resources.Load<Texture2D>("Textures/ExteriorTerrainTint");
            if (tint != null && renderer != null)
            {
                tint.wrapMode = TextureWrapMode.Clamp;
                renderer.material.mainTexture = tint;
                renderer.material.mainTextureScale = new Vector2(1f, 1f);
                renderer.material.color = Color.white;
            }
            else
            {
                var regolith = Resources.Load<Texture2D>("Textures/GroundRegolith");
                if (regolith != null && renderer != null)
                {
                    regolith.wrapMode = TextureWrapMode.Repeat;
                    renderer.material.mainTexture = regolith;
                    renderer.material.mainTextureScale = new Vector2(80f, 68f);
                    renderer.material.color = new Color(0.7f, 0.7f, 0.7f);
                }
            }

            // Start hidden — the Base tab is the default view.
            _terrain.SetActive(false);

            // Sci-fi castle (BaseCastle.fbx): one bold structure that
            // represents the whole base in the Map view, like the castle
            // on the world map in Age of Empires Mobile. Readable from
            // the far exterior camera where the detailed buildings are
            // tiny. Centred at its own origin, so it drops in at the
            // origin with no rotation. If it is missing, the Map view
            // still works — the detailed buildings just stay small (the
            // Bootstrap hides them in Map view, so a missing castle
            // means an empty base footprint; warn loudly).
            GameObject castlePrefab = null;
            try
            {
                castlePrefab = Resources.Load<GameObject>("Models/BaseCastle");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ExteriorView] Base castle failed to load (Models/BaseCastle): " + e.Message);
            }
            if (castlePrefab != null)
            {
                _baseCastle = Instantiate(castlePrefab);
                _baseCastle.name = "BaseCastle";
                _baseCastle.transform.position = Vector3.zero;
                // Same rotation rule as the terrain: leave the imported
                // rotation alone (Bake Axis Conversion). Scaled 2x
                // (Patrick 2026-11-09): at 1x the castle read as a tiny
                // white blob from the far camera; 2x makes it ~14 wide
                // and 15 high, a clear landmark that still fits well
                // inside the 56x48 base ground.
                _baseCastle.transform.localScale = Vector3.one * 2f;
                _baseCastle.SetActive(false);
            }
            else
            {
                Debug.LogWarning("[ExteriorView] BaseCastle.fbx is missing — the Map view will show an empty base footprint. Copy BaseCastle.fbx into Assets/_Project/Resources/Models/ (do not rename it).");
            }

            BuildMapPoints();
            BuildCreatures();
            BuildCampSlots();
            BuildSquads();
        }

        // Excursion squad roots (2026-11-09 as capsule markers; Map
        // Phase 2 2026-11-12: empty roots carrying the troop figures,
        // banner and count chip). One per army slot — cyan Army 1,
        // orange Army 2. Hidden until a march starts. Nothing in a
        // squad keeps a collider: the tap raycast only checks map
        // points, and a figure standing in front of a camp must never
        // shadow its tap.
        private void BuildSquads()
        {
            _marchMarkers[0] = MakeSquadRoot("ExcursionMarker");
            _marchMarkers[1] = MakeSquadRoot("ExcursionMarker2");
            BuildBanner(0, _marchMarkers[0]);
            BuildBanner(1, _marchMarkers[1]);
        }

        private GameObject MakeSquadRoot(string rootName)
        {
            var root = new GameObject(rootName);
            root.SetActive(false);
            return root;
        }

        // URP Lit material in a flat colour (the project's pipeline,
        // in Always Included Shaders) so runtime-built pieces never
        // render magenta. Optional emission for glows.
        private static Material MakeLitMaterial(Color color, float emission = 0f)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return null;
            var m = new Material(lit);
            m.color = color;
            if (emission > 0f)
            {
                m.SetColor("_EmissionColor", color * emission);
                m.EnableKeyword("_EMISSION");
            }
            return m;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // Army banner: a pole behind the leader, a flag in the army
        // colour, and a floating TextMesh count chip (built-in font,
        // like the boss fight's damage numbers — nothing new for the
        // Android build to strip). The chip is billboarded toward the
        // camera every frame in TickSquads.
        private void BuildBanner(int slot, GameObject root)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "SquadBannerPole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(-2.2f, 3.1f, -2.2f);
            pole.transform.localScale = new Vector3(0.11f, 3.1f, 0.11f);
            var poleRenderer = pole.GetComponent<Renderer>();
            if (poleRenderer != null) poleRenderer.material = MakeLitMaterial(new Color(0.12f, 0.12f, 0.14f));
            StripCollider(pole);

            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.name = "SquadBannerFlag";
            flag.transform.SetParent(root.transform, false);
            flag.transform.localPosition = new Vector3(-2.2f, 5.55f, -1.45f);
            flag.transform.localScale = new Vector3(0.07f, 0.95f, 1.65f);
            var flagRenderer = flag.GetComponent<Renderer>();
            if (flagRenderer != null) flagRenderer.material = MakeLitMaterial(ArmyColors[slot], 0.5f);
            StripCollider(flag);

            var chipGo = new GameObject("SquadCountChip");
            chipGo.transform.SetParent(root.transform, false);
            chipGo.transform.localPosition = new Vector3(0f, 6.9f, 0f);
            var chip = chipGo.AddComponent<TextMesh>();
            chip.text = "0";
            chip.fontSize = 64;
            chip.characterSize = 0.55f;
            chip.anchor = TextAnchor.MiddleCenter;
            chip.alignment = TextAlignment.Center;
            chip.color = Color.white;
            chip.fontStyle = FontStyle.Bold;
            _squadChips[slot] = chip;
        }

        private static bool IsHeroId(UnitId id)
        {
            return id == UnitId.HeroReyes || id == UnitId.HeroVega || id == UnitId.HeroOkafor;
        }

        private static string HeroModelPath(UnitId id)
        {
            switch (id)
            {
                case UnitId.HeroReyes: return "Models/HeroReyes";
                case UnitId.HeroVega: return "Models/HeroVega";
                case UnitId.HeroOkafor: return "Models/HeroOkafor";
                default: return null;
            }
        }

        private UnitClass ClassOf(UnitId id)
        {
            if (_classById == null)
            {
                _classById = new Dictionary<UnitId, UnitClass>();
                foreach (var def in UnitCatalog.CreateDefaultDefinitions())
                    if (def != null) _classById[def.Id] = def.Class;
            }
            UnitClass cls;
            return _classById.TryGetValue(id, out cls) ? cls : UnitClass.Infantry;
        }

        private static Color ClassColor(UnitId id, UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Ranged: return new Color(0.95f, 0.50f, 0.15f);
                case UnitClass.Mechanized: return new Color(0.40f, 0.60f, 0.35f);
                default: return new Color(0.40f, 0.55f, 0.80f);
            }
        }

        /// <summary>The service tells the view who is in a slot's
        /// squad (heroes first — they lead). Figures are rebuilt only
        /// when the membership actually changes, so refresh calls are
        /// cheap. Restored occupations pass generic infantry by count
        /// (the save keeps only the troop count).</summary>
        public void SetSquadComposition(int slot, List<UnitId> members)
        {
            if (slot < 0 || slot >= _marchMarkers.Length) return;
            if (members == null) members = new List<UnitId>();
            var ordered = new List<UnitId>();
            foreach (var id in members) if (IsHeroId(id)) ordered.Add(id);
            foreach (var id in members) if (!IsHeroId(id)) ordered.Add(id);
            _squadMembers[slot] = ordered;
            if (_squadChips[slot] != null) _squadChips[slot].text = ordered.Count.ToString();
            string key = string.Join(",", ordered);
            if (key == _squadKeys[slot]) return;
            _squadKeys[slot] = key;
            var root = _marchMarkers[slot];
            foreach (var fig in _squadFigures[slot]) if (fig != null) Destroy(fig);
            _squadFigures[slot].Clear();
            if (root == null) return;
            for (int i = 0; i < ordered.Count; i++)
            {
                var fig = BuildFigure(ordered[i]);
                fig.transform.SetParent(root.transform, false);
                fig.transform.localPosition = FormationOffset(slot, i, ordered.Count);
                _squadFigures[slot].Add(fig);
            }
        }

        // One squad member's figure: a wrapper (formation position +
        // yaw) holding a facing pivot holding the model. The model's
        // own transform is never touched — the FBX root carries
        // Blender's axis-conversion rotation (project rule).
        private GameObject BuildFigure(UnitId id)
        {
            bool hero = IsHeroId(id);
            var fig = new GameObject("SquadFigure");
            var pivot = new GameObject("Facing");
            pivot.transform.SetParent(fig.transform, false);
            pivot.transform.localRotation = Quaternion.Euler(0f, FigureFacingOffsetDeg, 0f);

            GameObject prefab = null;
            float scale = TroopFigureScale;
            if (hero)
            {
                var path = HeroModelPath(id);
                if (path != null) prefab = Resources.Load<GameObject>(path);
                scale = 1.35f; // heroes ~3.2 tall x 1.35 — still the tallest in the squad
            }
            if (prefab == null)
            {
                if (_miniInfantryPrefab == null) _miniInfantryPrefab = Resources.Load<GameObject>("Models/MiniInfantry");
                if (_miniRangedPrefab == null) _miniRangedPrefab = Resources.Load<GameObject>("Models/MiniRanged");
                if (_miniMechPrefab == null) _miniMechPrefab = Resources.Load<GameObject>("Models/MiniMech");
                switch (ClassOf(id))
                {
                    case UnitClass.Ranged: prefab = _miniRangedPrefab; break;
                    case UnitClass.Mechanized: prefab = _miniMechPrefab; break;
                    default: prefab = _miniInfantryPrefab; break;
                }
            }
            if (prefab != null)
            {
                var model = Instantiate(prefab);
                model.transform.SetParent(pivot.transform, false);
                model.transform.localScale = Vector3.one * scale;
                foreach (var col in model.GetComponentsInChildren<Collider>(true)) Destroy(col);
            }
            else
            {
                // Fallback if an FBX is missing: a class-coloured
                // capsule (the pre-Phase-2 marker look), so a march
                // still reads on the map.
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.transform.SetParent(pivot.transform, false);
                body.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                body.transform.localScale = new Vector3(0.9f, 1.1f, 0.9f);
                var r = body.GetComponent<Renderer>();
                if (r != null) r.material = MakeLitMaterial(ClassColor(id, ClassOf(id)));
                StripCollider(body);
            }
            if (hero)
            {
                // A gold ring marks the leader.
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "HeroRing";
                ring.transform.SetParent(fig.transform, false);
                ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                ring.transform.localScale = new Vector3(2.4f, 0.06f, 2.4f);
                var r = ring.GetComponent<Renderer>();
                if (r != null) r.material = MakeLitMaterial(new Color(1.0f, 0.78f, 0.25f), 0.8f);
                StripCollider(ring);
            }
            return fig;
        }

        // Where figure i stands in the root's local space for the
        // slot's current mode (y is set by the terrain ride in
        // TickSquads, not here).
        private Vector3 FormationOffset(int slot, int i, int n)
        {
            int mode = _squadModes[slot];
            if (mode == SquadModeMarching)
            {
                if (i == 0) return Vector3.zero;
                int row = (i + 1) / 2;
                float x = (i % 2 == 1) ? -2.3f : 2.3f;
                return new Vector3(x, 0f, -3.1f * row);
            }
            float radius = mode == SquadModeFighting ? 8.0f : 6.5f;
            if (n <= 1) return new Vector3(0f, 0f, radius);
            float angle = i * (Mathf.PI * 2f / n);
            return new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
        }

        // The yaw a figure should hold (local, degrees): marching
        // figures face the travel direction (the root's forward);
        // parked and fighting figures face the point they are
        // working on — the root's centre.
        private float FigureTargetYaw(int slot, Vector3 offset)
        {
            if (_squadModes[slot] == SquadModeMarching) return 0f;
            return Mathf.Atan2(-offset.x, -offset.z) * Mathf.Rad2Deg;
        }

        // Per-frame squad presentation: face the march direction,
        // ease figures into their formation, ride each figure on the
        // terrain at its own position, bob while marching, billboard
        // the count chips, pulse the route dots.
        private void TickSquads(float dt)
        {
            _squadAnimTime += dt;
            var cam = Camera.main;
            for (int slot = 0; slot < _marchMarkers.Length; slot++)
            {
                var root = _marchMarkers[slot];
                if (root == null || !root.activeSelf) continue;
                if (_squadChips[slot] != null && cam != null)
                    _squadChips[slot].transform.rotation = cam.transform.rotation;
                var dots = _routeDots[slot];
                for (int d = 0; d < dots.Count; d++)
                {
                    if (dots[d] == null) continue;
                    float pulse = 1f + 0.18f * Mathf.Sin(_squadAnimTime * 4f + d * 0.7f);
                    dots[d].transform.localScale = new Vector3(1.1f * pulse, 0.08f, 1.1f * pulse);
                }
                if (_squadAnimOwned[slot]) continue; // the fight animation owns the figures
                if (_marching[slot])
                {
                    Vector3 to = _marchTargets[slot] - root.transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.04f)
                    {
                        float targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                        float yaw = Mathf.LerpAngle(root.transform.eulerAngles.y, targetYaw, Mathf.Clamp01(dt * 8f));
                        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    }
                }
                var figs = _squadFigures[slot];
                int n = figs.Count;
                for (int i = 0; i < n; i++)
                {
                    var fig = figs[i];
                    if (fig == null) continue;
                    Vector3 target = FormationOffset(slot, i, n);
                    Vector3 lp = fig.transform.localPosition;
                    float step = Mathf.Clamp01(dt * 6f);
                    float nx = Mathf.Lerp(lp.x, target.x, step);
                    float nz = Mathf.Lerp(lp.z, target.z, step);
                    Vector3 world = root.transform.TransformPoint(new Vector3(nx, 0f, nz));
                    float bob = (_squadModes[slot] == SquadModeMarching && _marching[slot])
                        ? Mathf.Abs(Mathf.Sin(_squadAnimTime * 7f + i * 1.3f)) * 0.16f
                        : 0f;
                    float ny = TerrainHeight(world.x, world.z) - root.transform.position.y + bob;
                    fig.transform.localPosition = new Vector3(nx, ny, nz);
                    float targetYaw = FigureTargetYaw(slot, target);
                    float yaw = Mathf.LerpAngle(fig.transform.localEulerAngles.y, targetYaw, step);
                    fig.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
            }
        }

        // Dotted route line in the army colour from the march start
        // to its target, laid over the terrain. Rebuilt per march.
        private void BuildRouteDots(int slot, Vector3 from, Vector3 to)
        {
            HideRouteDots(slot);
            Vector3 a = new Vector3(from.x, 0f, from.z);
            Vector3 b = new Vector3(to.x, 0f, to.z);
            float dist = Vector3.Distance(a, b);
            int count = Mathf.Clamp(Mathf.RoundToInt(dist / 9f), 3, 18);
            if (_routeDotMats[slot] == null) _routeDotMats[slot] = MakeLitMaterial(ArmyColors[slot], 0.7f);
            for (int i = 1; i <= count; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)i / (count + 1));
                var dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dot.name = "RouteDot";
                dot.transform.position = new Vector3(p.x, TerrainHeight(p.x, p.z) + 0.32f, p.z);
                dot.transform.localScale = new Vector3(1.1f, 0.08f, 1.1f);
                var r = dot.GetComponent<Renderer>();
                if (r != null && _routeDotMats[slot] != null) r.material = _routeDotMats[slot];
                StripCollider(dot);
                _routeDots[slot].Add(dot);
            }
        }

        private void HideRouteDots(int slot)
        {
            foreach (var dot in _routeDots[slot]) if (dot != null) Destroy(dot);
            _routeDots[slot].Clear();
        }

        /// <summary>The slot's army has arrived at a node and is
        /// farming it: figures spread into a ring around the point.
        /// Called by the service from BeginOccupation.</summary>
        public void SetSquadParked(int slot)
        {
            if (slot < 0 || slot >= _marchMarkers.Length) return;
            _squadModes[slot] = SquadModeParked;
        }

        // Fixed creatures on the map: only the War Robot at the boss
        // lair now (2026-11-10). Camp slots show no creatures at all
        // since 2026-11-14 — each slot carries one tier structure
        // (see CampStructurePaths), swapped by SetCampSlot.
        private struct CreatureDef
        {
            public string ResourcePath; // Resources/Models/<name>
            public int CampIndex; // index into NodeDefs — tapping a
            // creature selects its point, not a separate point.
            public Vector3 Position;
            public float Scale;
        }
        private static readonly CreatureDef[] CreatureDefs =
        {
            // Boss — War Robot at its lair (-240, -200), lair index 12.
            // Scale 4x: a GIANT war robot, twice the size of the Brood
            // Tyrant. Stands 12 units north of the lair structure so the
            // two do not overlap. Tapping it selects the lair.
            new CreatureDef { ResourcePath = "Models/WarRobot", CampIndex = 12, Position = new Vector3(-240f, 0f, -188f), Scale = 4f },
        };

        private void BuildCreatures()
        {
            foreach (var def in CreatureDefs)
            {
                GameObject prefab = null;
                try
                {
                    prefab = Resources.Load<GameObject>(def.ResourcePath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[ExteriorView] Creature failed to load (" + def.ResourcePath + "): " + e.Message);
                }
                if (prefab == null)
                {
                    Debug.LogWarning("[ExteriorView] " + def.ResourcePath + " is missing — that creature will be an empty spot. Copy the FBX into Assets/_Project/Resources/Models/ (do not rename it).");
                    continue;
                }
                var go = Instantiate(prefab);
                go.name = NodeDefs[def.CampIndex].Title + " Creature";
                go.transform.position = def.Position;
                // Same rotation rule as the terrain: leave the imported
                // rotation alone (Bake Axis Conversion).
                go.transform.localScale = Vector3.one * def.Scale;
                GroundSnap(go);
                // Tappable like the nodes/camps: a generous BoxCollider
                // so the creature shows its camp's info when tapped.
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 2f, 0f);
                    box.size = new Vector3(4f, 4f, 4f);
                }
                go.SetActive(false);
                // The creature IS its camp for tapping: it carries the
                // camp's MapPointData, so the panel and the excursion
                // treat a creature tap exactly like a camp tap.
                _mapPoints.Add(new MapPoint { Go = go, Data = DataForNodeDef(def.CampIndex) });
            }
        }

        // Structured data for a NodeDefs entry (nodes 0-8, camps 9-11).
        private static MapPointData DataForNodeDef(int index)
        {
            var def = NodeDefs[index];
            return new MapPointData
            {
                Index = index,
                Kind = def.Kind,
                Tier = def.Tier,
                Amount = def.Amount,
                Position = def.Position,
                Title = def.Title,
                Info = def.Info
            };
        }

        private void BuildMapPoints()
        {
            for (int i = 0; i < NodeDefs.Length; i++)
            {
                var def = NodeDefs[i];
                // Camps (indices 9-11 in NodeDefs) are NOT built here:
                // the camp field (2026-11-10) builds 15 dynamic slots
                // instead (BuildCampSlots). The NodeDefs camp entries
                // remain only as the original layout record.
                if (def.Kind == MapPointKind.Camp) continue;
                GameObject prefab = null;
                try
                {
                    prefab = Resources.Load<GameObject>(def.ResourcePath);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[ExteriorView] Map point failed to load (" + def.ResourcePath + "): " + e.Message);
                }
                if (prefab == null)
                {
                    Debug.LogWarning("[ExteriorView] " + def.ResourcePath + " is missing — that map point will be an empty spot. Copy the FBX into Assets/_Project/Resources/Models/ (do not rename it).");
                    continue;
                }
                var go = Instantiate(prefab);
                go.name = def.Title;
                go.transform.position = def.Position;
                // Same rotation rule as the terrain: leave the imported
                // rotation alone (Bake Axis Conversion). Scaled 2x
                // (Patrick 2026-11-09): at 1x the nodes/camps read as
                // tiny dots from the far camera; 2x makes them ~4-8
                // units across, visible and tappable. The BoxCollider
                // below scales with the object, so the tap target grows
                // too.
                go.transform.localScale = Vector3.one * 2f;
                // The FBX has no collider, and tapping raycasts against
                // colliders — give each point a generous one (4x4x4,
                // centred at y 2) so it is easy to tap from the far
                // exterior camera. Same pattern as BaseView's buildings.
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 2f, 0f);
                    box.size = new Vector3(4f, 4f, 4f);
                }
                go.SetActive(false);
                // Keep every renderer + its original colour: a depleted
                // node is dimmed per-renderer, and accessing .material
                // here gives each point its own material instance so
                // dimming one never dims the others.
                var renderers = go.GetComponentsInChildren<Renderer>();
                var colors = new Color[renderers.Length];
                for (int r = 0; r < renderers.Length; r++)
                {
                    colors[r] = renderers[r].material.color;
                }
                _mapPoints.Add(new MapPoint { Go = go, Data = DataForNodeDef(i), Renderers = renderers, OriginalColors = colors });
            }
        }

        private void Update()
        {
            if (!_visible) return;
            // Excursion marches: each army slot's marker walks from its
            // start to its target at a steady speed, then fires the
            // arrival callback (the service resolves the outcome and
            // starts the return march). Only runs while the Map is
            // visible — leaving the Map tab cancels the march before
            // any outcome applies.
            for (int slot = 0; slot < _marchMarkers.Length; slot++)
            {
                if (!_marching[slot] || _marchMarkers[slot] == null) continue;
                Vector3 next = Vector3.MoveTowards(_marchMarkers[slot].transform.position, _marchTargets[slot], MarchSpeed * Time.deltaTime);
                // Ride the sculpted terrain (2026-11-12): the
                // marker follows the heightfield so it climbs the
                // dunes and dips through the craters instead of
                // clipping through them. Arrival is judged on the
                // ground plane — height is presentation only.
                next.y = TerrainHeight(next.x, next.z) + MarkerRideHeight;
                _marchMarkers[slot].transform.position = next;
                float dxz = next.x - _marchTargets[slot].x;
                float dzz = next.z - _marchTargets[slot].z;
                if (dxz * dxz + dzz * dzz < 0.01f)
                {
                    _marching[slot] = false;
                    HideRouteDots(slot);
                    var callback = _marchCallbacks[slot];
                    _marchCallbacks[slot] = null;
                    callback?.Invoke();
                }
            }
            TickSquads(Time.deltaTime);
            TickSearch(Time.deltaTime);
            // Arrow keys pan the map camera (PC). Same pattern as the
            // tap input below: Keyboard when the Input System is
            // enabled, legacy Input otherwise.
            Vector3 keyPan = Vector3.zero;
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rightArrowKey.isPressed) keyPan += CamRight;
                if (keyboard.leftArrowKey.isPressed) keyPan -= CamRight;
                if (keyboard.upArrowKey.isPressed) keyPan += CamGroundForward;
                if (keyboard.downArrowKey.isPressed) keyPan -= CamGroundForward;
            }
#else
            if (Input.GetKey(KeyCode.RightArrow)) keyPan += CamRight;
            if (Input.GetKey(KeyCode.LeftArrow)) keyPan -= CamRight;
            if (Input.GetKey(KeyCode.UpArrow)) keyPan += CamGroundForward;
            if (Input.GetKey(KeyCode.DownArrow)) keyPan -= CamGroundForward;
#endif
            if (keyPan != Vector3.zero)
            {
                keyPan.Normalize();
                AddPan(keyPan * PanSpeed * Time.deltaTime);
            }

            // Pointer input: a press that barely moves is a TAP (select
            // a node/camp or hide the panel); a press that moves past
            // the threshold is a DRAG (pan the camera) and must not
            // also fire as a tap on release. Input System note: Unity
            // throws InvalidOperationException if the legacy
            // UnityEngine.Input API is read while Player Settings >
            // Active Input Handler is set to Input System Package only.
            // Same pattern as BaseView.Update: read Mouse/Touchscreen
            // when the new Input System is enabled, legacy Input
            // otherwise.
            bool pressed = false;   // pointer went down this frame
            bool released = false;  // pointer went up this frame
            bool held = false;      // pointer is down right now
            Vector2 screenPos = Vector2.zero;
            int touchPointerId = -1;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            // Touch takes precedence on the phone; mouse in the Editor.
            if (touchscreen != null && (touchscreen.primaryTouch.press.isPressed || touchscreen.primaryTouch.press.wasPressedThisFrame || touchscreen.primaryTouch.press.wasReleasedThisFrame))
            {
                pressed = touchscreen.primaryTouch.press.wasPressedThisFrame;
                released = touchscreen.primaryTouch.press.wasReleasedThisFrame;
                held = touchscreen.primaryTouch.press.isPressed;
                screenPos = touchscreen.primaryTouch.position.ReadValue();
                touchPointerId = (int)touchscreen.primaryTouch.touchId.ReadValue();
            }
            else if (mouse != null)
            {
                pressed = mouse.leftButton.wasPressedThisFrame;
                released = mouse.leftButton.wasReleasedThisFrame;
                held = mouse.leftButton.isPressed;
                screenPos = mouse.position.ReadValue();
            }
#else
            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                pressed = touch.phase == TouchPhase.Began;
                released = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
                held = touch.phase == TouchPhase.Began || touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary;
                screenPos = touch.position;
            }
            else
            {
                pressed = Input.GetMouseButtonDown(0);
                released = Input.GetMouseButtonUp(0);
                held = Input.GetMouseButton(0);
                screenPos = Input.mousePosition;
            }
#endif
            // UI taps never reach here — EventSystem filters them out.
            if (pressed && EventSystem.current != null)
            {
#if ENABLE_INPUT_SYSTEM
                if (touchPointerId >= 0 ? EventSystem.current.IsPointerOverGameObject(touchPointerId) : EventSystem.current.IsPointerOverGameObject()) { _pointerDown = false; return; }
#else
                if (Input.touchCount > 0 && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)) { _pointerDown = false; return; }
                if (Input.touchCount == 0 && EventSystem.current.IsPointerOverGameObject()) { _pointerDown = false; return; }
#endif
            }

            if (pressed)
            {
                _pointerDown = true;
                _isDrag = false;
                _pointerStartPos = screenPos;
                _pointerLastPos = screenPos;
            }
            else if (held && _pointerDown)
            {
                // Dragging: once the pointer has moved past the tap
                // threshold, pan the camera with the finger/mouse so
                // the ground follows the pointer (like dragging a map).
                Vector2 delta = screenPos - _pointerLastPos;
                _pointerLastPos = screenPos;
                if (!_isDrag && Vector2.Distance(screenPos, _pointerStartPos) > DragTapThresholdPx)
                {
                    _isDrag = true;
                }
                if (_isDrag && delta != Vector2.zero)
                {
                    AddPan(ScreenDeltaToWorld(delta));
                }
            }
            else if (released && _pointerDown)
            {
                _pointerDown = false;
                // A drag never fires as a tap — only a press that stayed
                // within the threshold selects a node/camp or hides the
                // panel.
                if (_isDrag) return;
                HandleTap(screenPos);
            }
        }

        // Convert a screen-pixel drag delta to a world-space pan delta:
        // the ground follows the pointer, so the camera moves the
        // opposite way horizontally and the same way vertically (in
        // ground-plane axes). World-per-pixel comes from the camera's
        // distance and FOV so a drag feels the same at any zoom.
        private Vector3 ScreenDeltaToWorld(Vector2 pixelDelta)
        {
            var cam = Camera.main;
            if (cam == null) return Vector3.zero;
            // Distance from the camera to the point it looks at (the
            // pan offset cancels out — the camera and its look-at move
            // together, so the distance is constant at ~107).
            float distance = Vector3.Distance(cam.transform.position, Vector3.zero + _panOffset);
            float worldPerPixel = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, (float)Screen.height);
            return CamRight * (-pixelDelta.x * worldPerPixel) + CamGroundForward * (pixelDelta.y * worldPerPixel);
        }

        private void AddPan(Vector3 delta)
        {
            _gliding = false;
            _panOffset += delta;
            _panOffset.x = Mathf.Clamp(_panOffset.x, -PanLimitX, PanLimitX);
            _panOffset.z = Mathf.Clamp(_panOffset.z, -PanLimitZ, PanLimitZ);
            _panOffset.y = 0f;
            ApplyCamera();
        }

        private void ApplyCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = ExteriorCameraPosition + _panOffset;
            cam.transform.LookAt(Vector3.zero + _panOffset);
        }

        private void HandleTap(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screenPos);
            // Tap on a node/camp shows its info; a tap anywhere else
            // on the map hides any info panel that is showing.
            bool hitPoint = false;
            if (Physics.Raycast(ray, out var hit, 900f))
            {
                foreach (var point in _mapPoints)
                {
                    if (point.Go == null || point.Data == null) continue;
                    if (hit.transform == point.Go.transform || hit.transform.IsChildOf(point.Go.transform))
                    {
                        OnMapPointSelected?.Invoke(point.Data);
                        hitPoint = true;
                        break;
                    }
                }
            }
            // Tap-to-focus (Phase 2): a tap near one of your own
            // squads centres the camera on it instead of dismissing
            // the panel — on a map this size a marching army is easy
            // to lose.
            bool focused = false;
            if (!hitPoint)
            {
                for (int slot = 0; slot < _marchMarkers.Length && !focused; slot++)
                {
                    var root = _marchMarkers[slot];
                    if (root == null || !root.activeSelf) continue;
                    if (ray.direction.y > -0.001f) continue;
                    float tHit = (root.transform.position.y - ray.origin.y) / ray.direction.y;
                    if (tHit <= 0f) continue;
                    Vector3 p = ray.GetPoint(tHit);
                    float dx = p.x - root.transform.position.x;
                    float dz = p.z - root.transform.position.z;
                    if (dx * dx + dz * dz < 81f)
                    {
                        _gliding = false;
                        _panOffset.x = Mathf.Clamp(root.transform.position.x, -PanLimitX, PanLimitX);
                        _panOffset.z = Mathf.Clamp(root.transform.position.z, -PanLimitZ, PanLimitZ);
                        _panOffset.y = 0f;
                        ApplyCamera();
                        focused = true;
                    }
                }
            }
            if (!hitPoint && !focused) OnEmptyMapTapped?.Invoke();
        }

        // Start the march marker for an army slot at `from`, walking
        // to `to`, then fire `onArrive`. The marker rides 1 unit above
        // the ground so it never clips the terrain. The caller
        // (MapExcursionService) chains outbound and return marches
        // through the callback.
        public void StartMarch(int slot, Vector3 from, Vector3 to, Action onArrive)
        {
            if (slot < 0 || slot >= _marchMarkers.Length) return;
            var marker = _marchMarkers[slot];
            if (marker == null) return;
            // Sending an army out closes the search bar (Patrick
            // 2026-11-12): the find UI has done its job once the
            // squad is marching. Also clears the highlights.
            SetFindBarOpen(false);
            from.y = TerrainHeight(from.x, from.z) + MarkerRideHeight;
            marker.transform.position = from;
            marker.SetActive(true);
            _squadModes[slot] = SquadModeMarching;
            BuildRouteDots(slot, from, to);
            _marchTargets[slot] = to;
            _marchCallbacks[slot] = onArrive;
            _marching[slot] = true;
        }

        // Stop one slot's march and hide its marker. Called when the
        // player leaves the Map tab mid-excursion, or when a march
        // completes. Cancelling before arrival means no outcome was
        // applied.
        public void CancelMarch(int slot)
        {
            if (slot < 0 || slot >= _marchMarkers.Length) return;
            _marching[slot] = false;
            _marchCallbacks[slot] = null;
            HideRouteDots(slot);
            if (_marchMarkers[slot] != null) _marchMarkers[slot].SetActive(false);
        }

        // Stop every slot's march and hide every marker (the Map is
        // closing, so no marker may linger in another view).
        public void CancelAllMarches()
        {
            for (int slot = 0; slot < _marchMarkers.Length; slot++) CancelMarch(slot);
        }

        // Fight animation (2026-11-09, last feature of the phase;
        // Phase 2 2026-11-12: the squad figures do the fighting): a
        // short (~2s) clash on the map when the army reaches a camp.
        // Phase 1 (~0.9s) is the clash: the figures jitter in their
        // ring and the creatures shake. Phase 2 (~1.1s) is the
        // outcome: on victory the creatures tip over and sink away
        // while the figures bounce; on defeat the figures shake and
        // sink while the creatures hop. Transforms are restored at
        // the end (so a respawned camp is upright), then onComplete
        // fires — the service shows the result panel, vanishes the
        // camp on victory, and starts the return march. The restore +
        // hide happen in the same frame, so the player never sees
        // the creatures pop back upright.
        public void PlayFightAnimation(int slot, int campIndex, bool victory, Action onComplete)
        {
            StartCoroutine(FightAnimationRoutine(slot, campIndex, victory, onComplete));
        }

        private IEnumerator FightAnimationRoutine(int slot, int campIndex, bool victory, Action onComplete)
        {
            // The camp's creatures (not the camp marker itself — its
            // name is the camp Title, creatures are "<Title> Creature").
            // Boss lair (2026-11-09): the War Robot is a BossLair
            // creature, and it animates the same way as a camp creature.
            var creatures = new List<GameObject>();
            var origPos = new List<Vector3>();
            var origRot = new List<Quaternion>();
            foreach (var point in _mapPoints)
            {
                if (point.Data == null || point.Data.Index != campIndex) continue;
                if (point.Data.Kind != MapPointKind.Camp && point.Data.Kind != MapPointKind.BossLair) continue;
                if (point.Go == null) continue;
                if (!point.Go.name.EndsWith(" Creature")) continue;
                creatures.Add(point.Go);
                origPos.Add(point.Go.transform.position);
                origRot.Add(point.Go.transform.rotation);
            }
            // The squad figures ring the camp and fight it (Phase 2).
            // The per-frame squad tick hands them over for the length
            // of the animation and gets them back at the end.
            var figures = slot >= 0 && slot < _marchMarkers.Length ? _squadFigures[slot] : new List<GameObject>();
            var figOrig = new List<Vector3>();
            foreach (var fig in figures) figOrig.Add(fig != null ? fig.transform.localPosition : Vector3.zero);
            if (slot >= 0 && slot < _marchMarkers.Length)
            {
                _squadModes[slot] = SquadModeFighting;
                _squadAnimOwned[slot] = true;
            }

            // Phase 1 — clash (~0.9s): marker pulses, creatures shake.
            float clashTime = 0.9f;
            float t = 0f;
            while (t < clashTime)
            {
                // Pause while the Map is hidden (the player left the tab
                // mid-animation) — resume when they come back.
                while (!_visible) yield return null;
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / clashTime);
                for (int f = 0; f < figures.Count; f++)
                {
                    var fig = figures[f];
                    if (fig == null) continue;
                    Vector3 jitter = new Vector3(
                        UnityEngine.Random.Range(-0.35f, 0.35f), 0f,
                        UnityEngine.Random.Range(-0.35f, 0.35f));
                    fig.transform.localPosition = figOrig[f] + jitter;
                }
                for (int i = 0; i < creatures.Count; i++)
                {
                    var go = creatures[i];
                    if (go == null) continue;
                    Vector3 jitter = new Vector3(
                        UnityEngine.Random.Range(-0.7f, 0.7f), 0f,
                        UnityEngine.Random.Range(-0.7f, 0.7f));
                    go.transform.position = origPos[i] + jitter;
                }
                yield return null;
            }

            // Phase 2 — outcome (~1.1s).
            float outcomeTime = 1.1f;
            t = 0f;
            while (t < outcomeTime)
            {
                while (!_visible) yield return null;
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / outcomeTime);
                if (victory)
                {
                    // Creatures tip over and sink away.
                    for (int i = 0; i < creatures.Count; i++)
                    {
                        var go = creatures[i];
                        if (go == null) continue;
                        go.transform.rotation = origRot[i] * Quaternion.Euler(80f * p, 0f, 0f);
                        Vector3 pos = origPos[i];
                        pos.y = origPos[i].y - 2f * p;
                        go.transform.position = pos;
                    }
                    // Figures do a small victory bounce.
                    for (int f = 0; f < figures.Count; f++)
                    {
                        var fig = figures[f];
                        if (fig == null) continue;
                        Vector3 pos = figOrig[f];
                        pos.y = figOrig[f].y + Mathf.Sin(p * Mathf.PI) * 1.1f;
                        fig.transform.localPosition = pos;
                    }
                }
                else
                {
                    // Figures shake and sink (the army is broken);
                    // creatures hop once (they won).
                    for (int f = 0; f < figures.Count; f++)
                    {
                        var fig = figures[f];
                        if (fig == null) continue;
                        Vector3 jitter = new Vector3(
                            UnityEngine.Random.Range(-0.8f, 0.8f), 0f,
                            UnityEngine.Random.Range(-0.8f, 0.8f));
                        Vector3 pos = figOrig[f] + jitter;
                        pos.y = figOrig[f].y - 0.6f * p;
                        fig.transform.localPosition = pos;
                    }
                    for (int i = 0; i < creatures.Count; i++)
                    {
                        var go = creatures[i];
                        if (go == null) continue;
                        Vector3 pos = origPos[i];
                        pos.y = origPos[i].y + Mathf.Sin(p * Mathf.PI) * 1.2f;
                        go.transform.position = pos;
                        go.transform.rotation = origRot[i];
                    }
                }
                yield return null;
            }

            // Restore transforms (so a respawned camp is upright and the
            // marker is full-size for the return march), then finish.
            for (int i = 0; i < creatures.Count; i++)
            {
                var go = creatures[i];
                if (go == null) continue;
                go.transform.position = origPos[i];
                go.transform.rotation = origRot[i];
            }
            for (int f = 0; f < figures.Count; f++)
            {
                var fig = figures[f];
                if (fig == null) continue;
                fig.transform.localPosition = figOrig[f];
            }
            if (slot >= 0 && slot < _marchMarkers.Length) _squadAnimOwned[slot] = false;
            onComplete?.Invoke();
        }

        // Structured data for a NodeDefs point by index (nodes 0-8,
        // lair 12), for restoring an occupation from the save (the
        // service rebuilds its active point) and for the Bootstrap's
        // panel. Null when out of range. Camp slots are dynamic and
        // are not served here — their data lives on their MapPoints.
        public MapPointData NodeData(int index)
        {
            if (index < 0 || index >= NodeDefs.Length) return null;
            return DataForNodeDef(index);
        }

        // World position of a NodeDefs point by index, for the marker.
        public bool NodePosition(int index, out Vector3 position)
        {
            position = Vector3.zero;
            if (index < 0 || index >= NodeDefs.Length) return false;
            position = NodeDefs[index].Position;
            return true;
        }

        // Park a slot's marker at a position without marching
        // (2026-11-09): restores it at an occupied node when the Map
        // reopens, so the army is visibly farming there. Never
        // overrides that slot's active march.
        public void ShowMarkerAt(int slot, Vector3 position)
        {
            if (slot < 0 || slot >= _marchMarkers.Length) return;
            if (_marchMarkers[slot] == null || _marching[slot]) return;
            position.y = TerrainHeight(position.x, position.z) + MarkerRideHeight;
            _marchMarkers[slot].transform.position = position;
            _marchMarkers[slot].SetActive(true);
            _squadModes[slot] = SquadModeParked;
            HideRouteDots(slot);
        }

        // Mark a resource node as occupied (being farmed): its renderers
        // turn amber. Restoring clears the amber back to the original
        // colours. Camps and the boss lair never occupy, and creature
        // points carry camp/lair data (index 9-12), so the index check
        // alone keeps them out.
        public void SetNodeOccupied(int nodeIndex, bool occupied)
        {
            if (nodeIndex >= 0 && nodeIndex < _nodeOccupied.Length) _nodeOccupied[nodeIndex] = occupied;
            foreach (var point in _mapPoints)
            {
                if (point.Data == null || point.Data.Index != nodeIndex) continue;
                if (point.Data.Kind == MapPointKind.Camp || point.Data.Kind == MapPointKind.BossLair) continue;
                if (point.Renderers == null) continue;
                for (int i = 0; i < point.Renderers.Length; i++)
                {
                    if (point.Renderers[i] == null) continue;
                    point.Renderers[i].material.color = occupied
                        ? new Color(1.0f, 0.65f, 0.15f)
                        : point.OriginalColors[i];
                }
            }
        }

        // Show or hide a resource node. A depleted node vanishes
        // (2026-11-09, Patrick) until it regenerates, when it reappears.
        // Hiding also hides it from taps — a vanished node cannot be
        // selected, which is the point: it is gone until it comes back.
        public void SetNodeVisible(int nodeIndex, bool visible)
        {
            if (nodeIndex >= 0 && nodeIndex < _nodeHidden.Length) _nodeHidden[nodeIndex] = !visible;
            foreach (var point in _mapPoints)
            {
                if (point.Data == null || point.Data.Index != nodeIndex) continue;
                if (point.Data.Kind == MapPointKind.Camp || point.Data.Kind == MapPointKind.BossLair) continue;
                if (point.Go != null) point.Go.SetActive(visible);
            }
        }

        // Show or hide the boss lair (2026-11-09; ladder 2026-11-10):
        // the boss never vanishes now, so this is always called with
        // true. Hiding also hides it from taps. The lair (index 12)
        // and its robot both carry Data.Index == 12, so one pass hides
        // the whole lair.
        public void SetBossVisible(bool visible)
        {
            foreach (var point in _mapPoints)
            {
                if (point.Data == null || point.Data.Index != 12) continue;
                if (point.Data.Kind != MapPointKind.BossLair) continue;
                if (point.Go != null) point.Go.SetActive(visible);
            }
        }

        // ---- Map search (Phase 3, 2026-11-12, Patrick) ----
        // A FIND button on the Map view expands into category
        // chips (resources by type, camps by tier, the lair).
        // Tapping a chip rings and beams every match, glides the
        // camera to the nearest one, and tapping the same chip
        // again cycles to the next. Only usable places match:
        // nodes that are neither depleted (hidden) nor being
        // farmed, and camp slots currently holding creatures.
        private enum SearchCategory { None, Biomass, Alloy, Helium, Tier1, Tier2, Tier3, Lair }

        private static readonly SearchCategory[] ChipCategories =
        {
            SearchCategory.Biomass, SearchCategory.Alloy, SearchCategory.Helium,
            SearchCategory.Tier1, SearchCategory.Tier2, SearchCategory.Tier3,
            SearchCategory.Lair
        };

        private GameObject _findRoot;
        private GameObject _baseBtnRoot;
        private GameObject _topBarGo;
        private GameObject _bottomNavGo;
        private GameObject _findBar;
        private Text _findStatus;
        private readonly Button[] _findChips = new Button[ChipCategories.Length];
        private bool _findBarOpen;
        private SearchCategory _searchCategory = SearchCategory.None;
        private readonly List<Vector3> _searchMatches = new List<Vector3>();
        private int _searchIndex = -1;
        private GameObject _searchRoot;
        private readonly List<GameObject> _searchRings = new List<GameObject>();
        private readonly List<float> _searchRingBase = new List<float>();
        private readonly List<Material> _searchMats = new List<Material>();
        private readonly List<Material> _searchBeamMats = new List<Material>();
        private bool _gliding;
        private Vector3 _glideTarget;
        private float _searchTime;
        private readonly bool[] _nodeOccupied = new bool[9];
        private readonly bool[] _nodeHidden = new bool[9];

        private static Color SearchColor(SearchCategory cat)
        {
            switch (cat)
            {
                case SearchCategory.Biomass: return new Color(0.30f, 0.95f, 0.45f);
                case SearchCategory.Alloy: return new Color(0.65f, 0.82f, 1.00f);
                case SearchCategory.Helium: return new Color(0.30f, 0.92f, 1.00f);
                case SearchCategory.Lair: return new Color(1.00f, 0.68f, 0.18f);
                default: return new Color(1.00f, 0.38f, 0.22f); // camp tiers
            }
        }

        private static string SearchSingular(SearchCategory cat)
        {
            switch (cat)
            {
                case SearchCategory.Biomass: return "Biomass node";
                case SearchCategory.Alloy: return "Alloy node";
                case SearchCategory.Helium: return "Helium-3 node";
                case SearchCategory.Tier1: return "Tier 1 camp";
                case SearchCategory.Tier2: return "Tier 2 camp";
                case SearchCategory.Tier3: return "Tier 3 camp";
                case SearchCategory.Lair: return "Boss Lair";
                default: return "";
            }
        }

        private static string SearchEmptyText(SearchCategory cat)
        {
            switch (cat)
            {
                case SearchCategory.Biomass: return "No unfarmed Biomass nodes on the map";
                case SearchCategory.Alloy: return "No unfarmed Alloy nodes on the map";
                case SearchCategory.Helium: return "No unfarmed Helium-3 nodes on the map";
                case SearchCategory.Tier1: return "No Tier 1 camps on the map right now";
                case SearchCategory.Tier2: return "No Tier 2 camps on the map right now";
                case SearchCategory.Tier3: return "No Tier 3 camps on the map right now";
                default: return "Nothing found";
            }
        }

        /// <summary>Build the Find UI on the shared canvas. Called
        /// by the Bootstrap right after Build(); the bar itself is
        /// shown and hidden with the Map (see SetVisible).</summary>
        public void BuildSearchUi(Canvas canvas)
        {
            var safe = SafeArea.Get(canvas);
            float baseY = 196f + safe.Bottom;
            // The BASE+FIND cluster sits in the lower-left corner
            // (Patrick 2026-11-13, moved from bottom centre): it
            // can hug the corner now that the BottomNav it used
            // to clear is hidden on the Map. The expanded find
            // bar keeps its centred spot at baseY.
            float clusterX = 24f + safe.Left;
            float clusterY = 24f + safe.Bottom;
            var barColor = new Color(0.06f, 0.09f, 0.15f, 0.94f);

            _findRoot = UIHelpers.Panel(canvas.gameObject, "FindButton", barColor,
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(clusterX + 195f, clusterY), new Vector2(clusterX + 385f, clusterY + 62f));
            var findBtn = UIHelpers.Button(_findRoot, "Find", "FIND", 30,
                UIHelpers.Hex("#1A2436"), Color.white);
            var frt = findBtn.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            findBtn.onClick.AddListener(() => SetFindBarOpen(true));

            // BASE sits with FIND (Patrick 2026-11-13): with the
            // TopBar/BottomNav hidden on the Map, this button is
            // the way back to the Base tab. Same panel/button
            // styling as FIND, first in the corner cluster, and
            // it tucks away with FIND while the search bar is
            // open.
            _baseBtnRoot = UIHelpers.Panel(canvas.gameObject, "BaseButton", barColor,
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(clusterX, clusterY), new Vector2(clusterX + 185f, clusterY + 62f));
            var baseBtn = UIHelpers.Button(_baseBtnRoot, "Base", "BASE", 30,
                UIHelpers.Hex("#1A2436"), Color.white);
            var brt = baseBtn.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            baseBtn.onClick.AddListener(() => OnBackToBaseClicked?.Invoke());

            _findBar = UIHelpers.Panel(canvas.gameObject, "FindBar", barColor,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-660f, baseY), new Vector2(660f, baseY + 128f));
            _findStatus = UIHelpers.LabelInRect(_findBar, "FindStatus", "", 26, Color.white,
                new Rect(0.02f, 0.55f, 0.98f, 0.97f), TextAnchor.MiddleCenter);
            _findStatus.verticalOverflow = VerticalWrapMode.Overflow;

            string[] chipLabels = { "BIO", "ALLOY", "HE-3", "T1", "T2", "T3", "LAIR", "\u00D7" };
            for (int i = 0; i < chipLabels.Length; i++)
            {
                bool isClear = i == chipLabels.Length - 1;
                var chip = UIHelpers.Button(_findBar, "FindChip" + i, chipLabels[i], 26,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = chip.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.012f + i * 0.1235f, 0.07f);
                rt.anchorMax = new Vector2(0.012f + i * 0.1235f + 0.111f, 0.47f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                if (isClear)
                {
                    chip.onClick.AddListener(() => SetFindBarOpen(false));
                }
                else
                {
                    int idx = i;
                    chip.onClick.AddListener(() => OnSearchChip(ChipCategories[idx]));
                    _findChips[i] = chip;
                }
            }

            _searchRoot = new GameObject("SearchHighlights");
            _searchRoot.SetActive(false);
            _findBar.SetActive(false);
            _findRoot.SetActive(false);
            _baseBtnRoot.SetActive(false);
            UpdateSearchStatus();
        }

        private void SetFindBarOpen(bool open)
        {
            _findBarOpen = open;
            if (!open) ClearSearch();
            if (_findRoot != null) _findRoot.SetActive(_visible && !open);
            if (_baseBtnRoot != null) _baseBtnRoot.SetActive(_visible && !open);
            if (_findBar != null) _findBar.SetActive(_visible && open);
        }

        private void OnSearchChip(SearchCategory cat)
        {
            bool sameCategory = cat == _searchCategory;
            Vector3 previousFocus = Vector3.zero;
            bool hadFocus = sameCategory && _searchIndex >= 0 && _searchIndex < _searchMatches.Count;
            if (hadFocus) previousFocus = _searchMatches[_searchIndex];

            _searchCategory = cat;
            RebuildSearchMatches();

            if (_searchMatches.Count == 0)
            {
                _searchIndex = -1;
            }
            else if (hadFocus)
            {
                int found = _searchMatches.IndexOf(previousFocus);
                _searchIndex = found >= 0 ? (found + 1) % _searchMatches.Count : 0;
            }
            else
            {
                _searchIndex = 0;
            }

            RebuildSearchHighlights();
            RefreshChipColors();
            UpdateSearchStatus();
            if (_searchIndex >= 0) GlideTo(_searchMatches[_searchIndex]);
        }

        private void RebuildSearchMatches()
        {
            _searchMatches.Clear();
            switch (_searchCategory)
            {
                case SearchCategory.Biomass:
                case SearchCategory.Alloy:
                case SearchCategory.Helium:
                    var kind = _searchCategory == SearchCategory.Biomass ? MapPointKind.Biomass
                        : _searchCategory == SearchCategory.Alloy ? MapPointKind.Alloy
                        : MapPointKind.Helium3;
                    for (int i = 0; i < 9 && i < NodeDefs.Length; i++)
                    {
                        if (NodeDefs[i].Kind != kind) continue;
                        if (_nodeHidden[i] || _nodeOccupied[i]) continue;
                        _searchMatches.Add(NodeDefs[i].Position);
                    }
                    break;
                case SearchCategory.Tier1:
                case SearchCategory.Tier2:
                case SearchCategory.Tier3:
                    int tier = _searchCategory == SearchCategory.Tier1 ? 1
                        : _searchCategory == SearchCategory.Tier2 ? 2 : 3;
                    for (int slot = 0; slot < CampSlotCount; slot++)
                    {
                        if (_campSlotTier[slot] == tier) _searchMatches.Add(CampSlotPositions[slot]);
                    }
                    break;
                case SearchCategory.Lair:
                    _searchMatches.Add(NodeDefs[12].Position);
                    break;
            }
            // Nearest first, measured from where the camera is
            // currently centred.
            Vector2 focus = new Vector2(_panOffset.x, _panOffset.z);
            _searchMatches.Sort((a, b) =>
            {
                float da = (new Vector2(a.x, a.z) - focus).sqrMagnitude;
                float db = (new Vector2(b.x, b.z) - focus).sqrMagnitude;
                return da.CompareTo(db);
            });
        }

        private static Material MakeBeamMaterial(Color color)
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) return null;
            var m = new Material(unlit);
            m.color = new Color(color.r, color.g, color.b, 0.32f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;
            return m;
        }

        private void RebuildSearchHighlights()
        {
            ClearSearchHighlights();
            if (_searchRoot == null || _searchMatches.Count == 0) return;
            var color = SearchColor(_searchCategory);
            for (int i = 0; i < _searchMatches.Count; i++)
            {
                Vector3 p = _searchMatches[i];
                float ground = TerrainHeight(p.x, p.z);
                float baseScale = i == _searchIndex ? 20f : 15f;

                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ring.name = "SearchRing";
                ring.transform.SetParent(_searchRoot.transform, false);
                ring.transform.position = new Vector3(p.x, ground + 0.35f, p.z);
                ring.transform.localScale = new Vector3(baseScale, 0.3f, baseScale);
                var ringMat = MakeLitMaterial(color, 1.5f);
                var rr = ring.GetComponent<Renderer>();
                if (rr != null && ringMat != null) rr.material = ringMat;
                if (ringMat != null) _searchMats.Add(ringMat);
                StripCollider(ring);
                _searchRings.Add(ring);
                _searchRingBase.Add(baseScale);

                var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beam.name = "SearchBeam";
                beam.transform.SetParent(_searchRoot.transform, false);
                beam.transform.position = new Vector3(p.x, ground + 30f, p.z);
                beam.transform.localScale = new Vector3(1.7f, 60f, 1.7f);
                var beamMat = MakeBeamMaterial(color);
                var br = beam.GetComponent<Renderer>();
                if (br != null && beamMat != null) br.material = beamMat;
                if (beamMat != null) _searchBeamMats.Add(beamMat);
                StripCollider(beam);
            }
            _searchRoot.SetActive(_visible);
        }

        private void ClearSearchHighlights()
        {
            foreach (var ring in _searchRings) if (ring != null) Destroy(ring);
            _searchRings.Clear();
            _searchRingBase.Clear();
            if (_searchRoot != null)
            {
                // Beams are children of the root alongside the rings.
                var beams = new List<Transform>();
                foreach (Transform child in _searchRoot.transform)
                {
                    if (child.name == "SearchBeam") beams.Add(child);
                }
                foreach (var beam in beams) Destroy(beam.gameObject);
            }
            foreach (var m in _searchMats) if (m != null) Destroy(m);
            _searchMats.Clear();
            foreach (var m in _searchBeamMats) if (m != null) Destroy(m);
            _searchBeamMats.Clear();
        }

        private void ClearSearch()
        {
            _searchCategory = SearchCategory.None;
            _searchMatches.Clear();
            _searchIndex = -1;
            ClearSearchHighlights();
            RefreshChipColors();
            UpdateSearchStatus();
        }

        private void RefreshChipColors()
        {
            for (int i = 0; i < _findChips.Length; i++)
            {
                var chip = _findChips[i];
                if (chip == null) continue;
                var img = chip.GetComponent<Image>();
                if (img == null) continue;
                img.color = ChipCategories[i] == _searchCategory
                    ? SearchColor(_searchCategory)
                    : UIHelpers.Hex("#1A2436");
            }
        }

        private void UpdateSearchStatus()
        {
            if (_findStatus == null) return;
            if (_searchCategory == SearchCategory.None)
            {
                _findStatus.text = "Tap a category to find it on the map";
            }
            else if (_searchMatches.Count == 0)
            {
                _findStatus.text = SearchEmptyText(_searchCategory);
            }
            else
            {
                _findStatus.text = SearchSingular(_searchCategory) + " \u2014 "
                    + (_searchIndex + 1) + " of " + _searchMatches.Count;
            }
        }

        private void GlideTo(Vector3 target)
        {
            _glideTarget = new Vector3(
                Mathf.Clamp(target.x, -PanLimitX, PanLimitX), 0f,
                Mathf.Clamp(target.z, -PanLimitZ, PanLimitZ));
            _gliding = true;
        }

        private void TickSearch(float dt)
        {
            if (_gliding)
            {
                _panOffset = Vector3.MoveTowards(_panOffset, _glideTarget, 340f * dt);
                _panOffset.y = 0f;
                ApplyCamera();
                if ((_panOffset - _glideTarget).sqrMagnitude < 1f) _gliding = false;
            }
            if (_searchRings.Count == 0) return;
            _searchTime += dt;
            for (int i = 0; i < _searchRings.Count; i++)
            {
                var ring = _searchRings[i];
                if (ring == null) continue;
                float s = _searchRingBase[i] * (1f + 0.06f * Mathf.Sin(_searchTime * 3.2f + i * 0.7f));
                ring.transform.localScale = new Vector3(s, 0.3f, s);
            }
            for (int i = 0; i < _searchBeamMats.Count; i++)
            {
                var m = _searchBeamMats[i];
                if (m == null) continue;
                var c = m.color;
                c.a = 0.30f + 0.10f * Mathf.Sin(_searchTime * 3.2f + i * 0.7f);
                m.color = c;
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_terrain != null) _terrain.SetActive(visible);
            if (_baseCastle != null) _baseCastle.SetActive(visible);
            foreach (var point in _mapPoints)
            {
                // Camp slot points are visibility-managed per slot
                // (an empty slot must stay hidden) — handled below.
                if (point.Data != null && point.Data.Kind == MapPointKind.Camp
                    && point.Data.Index >= CampSlotIndexBase) continue;
                if (point.Go != null) point.Go.SetActive(visible);
            }
            for (int slot = 0; slot < CampSlotCount; slot++) ApplySlotVisibility(slot);
            // Hiding the Map also stops any march in progress — the
            // service cancels the excursion on tab switch, and this is
            // the backstop so the markers never linger in the Base view.
            if (!visible) CancelAllMarches();
            // Search (Phase 3): the Find UI lives and dies with the
            // Map, and leaving the Map clears any active search.
            if (!visible)
            {
                _gliding = false;
                ClearSearch();
                _findBarOpen = false;
            }
            if (_findRoot != null) _findRoot.SetActive(visible && !_findBarOpen);
            if (_findBar != null) _findBar.SetActive(visible && _findBarOpen);
            if (_searchRoot != null) _searchRoot.SetActive(visible);
            if (_baseBtnRoot != null) _baseBtnRoot.SetActive(visible && !_findBarOpen);
            // Full-screen Map (Patrick 2026-11-13): the TopBar
            // and BottomNav panels hide while the Map is up —
            // the same canvas panels the boss fight's chrome
            // hides (the *View objects are empty hosts; the
            // panels are canvas children found by name).
            // SetVisible is the single choke point — tab
            // switches, entering the lair and closing the lair
            // all pass through here — so the bars always match
            // the screen on top.
            if (_topBarGo == null) _topBarGo = GameObject.Find("TopBar");
            if (_bottomNavGo == null) _bottomNavGo = GameObject.Find("BottomNav");
            if (_topBarGo != null) _topBarGo.SetActive(!visible);
            if (_bottomNavGo != null) _bottomNavGo.SetActive(!visible);
            if (visible)
            {
                // Opening the Map always recentres on the base — the
                // pan starts from zero each time.
                _panOffset = Vector3.zero;
                _pointerDown = false;
                _isDrag = false;
                ApplyCamera();
            }
            // Hiding the Map does NOT touch the camera (2026-11-14
            // fix): this method used to reset it to a stale local
            // copy of the base camera position, which fought
            // BaseView's own camera — the startup view and tab
            // returns showed whichever pose wrote last, and the
            // first drag then snapped the zoom. BaseView owns the
            // base camera now; its SetVisible(true) re-applies it.
        }
    }
}
