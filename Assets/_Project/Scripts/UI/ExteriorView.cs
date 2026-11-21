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
    /// indices 0-17 (in NodeDefs order); the boss lair is 21; enemy
    /// camps are the 36 camp slots at indices 100-135 (camp field,
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
    /// A large sculpted terrain (3360x2880 since the 2026-11-20
    /// terrain transplant — the 1120x960 landscape at x3, itself
    /// 16x the area of the original 280x240 map) with a sci-fi
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

        // Exterior camera (Round 3 prototype, Patrick 2026-11-15):
        // the camera rides a fixed 45-degree yaw around the pan
        // centre at a zoomable distance. The default distance
        // reproduces the original framing exactly (offset
        // (50, 80, 50), 48.5-degree pitch); zooming in also
        // flattens the pitch toward ~21 degrees, approaching the
        // bright-day mockup's low, cinematic framing. Zoomed all
        // the way out it steepens slightly for a wider command
        // view. The Base view's camera is separate and keeps its
        // locked framing (see PanLimitX/Z for the pan bounds).
        // Terrain transplant (2026-11-20): the world is x3, so the
        // camera distances scale x3 with it — the framing Patrick
        // tuned reads the same, the map is just three times bigger.
        private const float CamDistDefault = 320.31f; // 106.77 x3
        private const float CamDistMin = 102f;
        private const float CamDistMax = 450f;
        private const float CamPitchDefault = 48.54f;
        private const float CamPitchClose = 21f;
        private const float CamPitchFar = 53f;

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
        // Background marches (2026-11-15): whether each slot's marker
        // SHOULD be on screen whenever the Map is open (marching,
        // parked at a node, or fighting). SetVisible only toggles the
        // markers' active state — march state itself is never
        // destroyed by a tab switch anymore.
        private readonly bool[] _markerWanted = new bool[GameState.MaxArmies];
        private readonly Vector3[] _marchTargets = new Vector3[GameState.MaxArmies];
        private readonly Action[] _marchCallbacks = new Action[GameState.MaxArmies];
        // Road pathing (Round 3 Phase 2): the waypoint list each
        // slot is following along the road network, and which
        // waypoint it is currently heading for (_marchTargets
        // always holds that current waypoint, so the squad
        // figures' facing code keeps working unchanged).
        private readonly List<Vector3>[] _marchPaths = new List<Vector3>[GameState.MaxArmies];
        private readonly int[] _marchPathIndex = new int[GameState.MaxArmies];
        // March speed: 20 u/s when the map enlarged (positions
        // doubled, so trip times matched the old map's 10 u/s);
        // slowed to 14 u/s for Phase 2 (Patrick 2026-11-12) — real
        // marching figures read hurried at 20, and a far corner now
        // takes ~24s, which suits an army on the move. Raised to
        // 18 u/s with the road network (Round 3 Phase 2, Patrick
        // 2026-11-15): road routes run ~1.1x the straight line on
        // average, so trip times land about where they were. Halved
        // to 9 u/s (Patrick 2026-11-16): the march still read too
        // fast — a far-corner trip now takes ~1.5-2 minutes.
        // Doubled back to 18 u/s (Patrick 2026-11-21): in the x3
        // terrain world the 9 u/s trips ran 4-5 minutes, too long;
        // 18 restores roughly the old trip feel. "2X for now" — he
        // may tune it again after living with it.
        private static readonly float MarchSpeed = 18f; // units/second

        // Map Phase 2 (2026-11-12, Patrick's pick: real Blender mini
        // troops over code-built figures): each slot's marker root
        // carries a formation of mini troop figures — one per unit
        // TYPE in the squad since the stylized-march change
        // (2026-11-16), heroes leading on their full models — plus
        // an army banner with a floating count chip and a dotted
        // route line while marching. The root marches exactly as
        // the old capsule did; everything here is presentation.
        // Figure modes:
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
        // Phase 3 troop figures (Round 3): hero-quality class models
        // authored ~3.15 tall (mech ~4.5). Humanoids scale 1.15 so
        // heroes (3.24 x 1.35) stay the tallest in the squad; the
        // mech runs at its authored size. The minis remain as the
        // fallback if a troop FBX is missing.
        private const float TroopFigureScale = 1.15f;
        private const float MechFigureScale = 1.0f;
        private const float MiniFigureScale = 1.8f;
        private GameObject _troopInfantryPrefab;
        private GameObject _troopRangedPrefab;
        private GameObject _troopMechPrefab;
        private GameObject _miniInfantryPrefab;
        private GameObject _miniRangedPrefab;
        private GameObject _miniMechPrefab;
        private Dictionary<UnitId, UnitClass> _classById;
        private Dictionary<UnitId, int> _tierById;
        private Material[] _tierTrimMats;

        // Map camera pan (Patrick 2026-11-09): the Map view is 5x the
        // base, so the camera can be panned across it — arrow keys on
        // PC, touch/mouse drag on the phone (and in the Editor). The
        // pan offset moves both the camera position and its look-at
        // point. Clamped so the view stays on the 3360x2880 map.
        private Vector3 _panOffset = Vector3.zero;

        // Zoom state (Round 3 prototype): the current distance
        // eases toward the target the wheel/pinch sets. Pan speed
        // needs no matching code — ScreenDeltaToWorld already
        // derives world-per-pixel from the live camera distance.
        private float _camDist = CamDistDefault;
        private float _camDistTarget = CamDistDefault;
        private float _pinchPrevDist = -1f;

        // Alien Twilight ambience (Patrick 2026-11-15): while the
        // Map is up, the view gets a twilight sky dome with a
        // ringed planet, a warm low sun, teal haze (fog) and
        // drifting spore motes. Everything global it touches
        // (ambient, fog, the shared lights, the camera backdrop)
        // is captured on show and restored on hide — the same
        // discipline as the lair's environment pass — so the
        // Base and Lair views are never affected.
        private GameObject _ambienceRoot;
        private Transform _motes;
        private bool _ambApplied;
        private UnityEngine.Rendering.Volume _mapPostVolume;
        private UnityEngine.Rendering.AmbientMode _ambPrevMode;
        private Color _ambPrevSky, _ambPrevEquator, _ambPrevGround;
        private bool _ambPrevFog;
        private Color _ambPrevFogColor;
        private FogMode _ambPrevFogMode;
        private float _ambPrevFogDensity, _ambPrevFogNear, _ambPrevFogFar;
        private Color _ambPrevSunColor, _ambPrevFillColor;
        private float _ambPrevSunIntensity, _ambPrevFillIntensity;
        private Color _ambPrevCamBg;
        private Light _ambSun, _ambFill;
        private static readonly float PanSpeed = 120f; // units/second, arrow keys (x3 with the world)
        private static readonly float PanLimitX = 1440f;
        private static readonly float PanLimitZ = 1260f;
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

        // Layout: 18 resource nodes (indices 0-17 = the depletion
        // state indices, GameState.NodeRegenUtc), then the 3 legacy
        // camp records (18-20, layout record only - the camp field
        // below is the live system), then the boss lair (21).
        // Amounts grow with distance: 500/1000/2000 in the home
        // region, 3500/5000/8000 in the far field (2026-11-14: the
        // map quadrupled to 1120x960 for the higher-tier grind -
        // original positions doubled, far nodes appended so old
        // indices 0-8 keep their meaning in existing saves).
        // Transplant 2026-11-20: every position below scaled x3
        // again for the 3360x2880 terrain — indices unchanged, so
        // existing saves are unaffected.
        private struct NodeDef
        {
            public string ResourcePath; // Resources/Models/<name>
            public MapPointKind Kind;
            public int Tier; // camps only (1-3)
            public int Amount; // nodes only
            public string Title;
            public string Info;
            public Vector3 Position;
        }
        private static readonly NodeDef[] NodeDefs =
        {
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 500, Title = "Biomass Node", Info = "Biomass: 500", Position = new Vector3(420f, 0f, 300f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 1000, Title = "Biomass Node", Info = "Biomass: 1000", Position = new Vector3(-660f, 0f, 540f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 2000, Title = "Biomass Node", Info = "Biomass: 2000", Position = new Vector3(1140f, 0f, -660f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 500, Title = "Alloy Node", Info = "Alloy: 500", Position = new Vector3(-360f, 0f, -420f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 1000, Title = "Alloy Node", Info = "Alloy: 1000", Position = new Vector3(780f, 0f, 660f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 2000, Title = "Alloy Node", Info = "Alloy: 2000", Position = new Vector3(-1260f, 0f, -780f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 500, Title = "Helium-3 Node", Info = "Helium-3: 500", Position = new Vector3(540f, 0f, -480f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 1000, Title = "Helium-3 Node", Info = "Helium-3: 1000", Position = new Vector3(-840f, 0f, -240f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 2000, Title = "Helium-3 Node", Info = "Helium-3: 2000", Position = new Vector3(1320f, 0f, 900f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 3500, Title = "Biomass Node", Info = "Biomass: 3500", Position = new Vector3(-1140f, 0f, 960f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 5000, Title = "Biomass Node", Info = "Biomass: 5000", Position = new Vector3(900f, 0f, 1140f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 8000, Title = "Biomass Node", Info = "Biomass: 8000", Position = new Vector3(-540f, 0f, -1260f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 3500, Title = "Alloy Node", Info = "Alloy: 3500", Position = new Vector3(1440f, 0f, -180f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 5000, Title = "Alloy Node", Info = "Alloy: 5000", Position = new Vector3(-1440f, 0f, 180f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 8000, Title = "Alloy Node", Info = "Alloy: 8000", Position = new Vector3(360f, 0f, 1290f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 3500, Title = "Helium-3 Node", Info = "Helium-3: 3500", Position = new Vector3(-1380f, 0f, -1020f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 5000, Title = "Helium-3 Node", Info = "Helium-3: 5000", Position = new Vector3(1500f, 0f, 720f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 8000, Title = "Helium-3 Node", Info = "Helium-3: 8000", Position = new Vector3(180f, 0f, -1320f) },
            new NodeDef { ResourcePath = "Models/EnemyCampT1", Kind = MapPointKind.Camp, Tier = 1, Title = "Spore Crawler Camp", Info = "Tier 1 \u2014 Spore Crawlers\nExpedition enemy", Position = new Vector3(660f, 0f, 180f) },
            new NodeDef { ResourcePath = "Models/EnemyCampT2", Kind = MapPointKind.Camp, Tier = 2, Title = "Void Stalker Camp", Info = "Tier 2 \u2014 Void Stalkers\nExpedition enemy", Position = new Vector3(-1020f, 0f, 720f) },
            new NodeDef { ResourcePath = "Models/EnemyCampT3", Kind = MapPointKind.Camp, Tier = 3, Title = "Brood Tyrant Camp", Info = "Tier 3 \u2014 Brood Tyrant\nExpedition enemy", Position = new Vector3(1440f, 0f, -960f) },
            // Boss lair: the War Robot lives here, far southwest
            // corner of the map. Index 21. The lair does not move
            // on respawn (unlike camps) - it is a fixed place.
            new NodeDef { ResourcePath = "Models/BossLair", Kind = MapPointKind.BossLair, Title = "Morbius 3 Lair", Info = "BOSS \u2014 Morbius 3\nGiant Mechanized enemy\nUnlocks at Nexus 10", Position = new Vector3(-1440f, 0f, -1200f) },
        };

        // Enemy camp field (2026-11-10, replaces the 3 fixed camps and
        // their respawn spots): 36 camp slots spread across the map in
        // rings — near, mid, and far corners — all clear of the base,
        // the resource nodes, the boss lair, and each other. A slot's
        // camp tier is dynamic (rolled per spawn by MapExcursionService,
        // weighted by Nexus level), so a slot shows the one standout
        // structure for its CURRENT tier, swapped in on demand (the
        // creature figures that used to ring the camps were removed
        // 2026-11-14). Slot points carry Index = CampSlotIndexBase + slot
        // so they never collide with node (0-17) or lair (21) indices.
        public const int CampSlotCount = 36;
        public const int CampSlotIndexBase = 100;
        private static readonly Vector3[] CampSlotPositions =
        {
            new Vector3(660f, 0f, 180f), new Vector3(-480f, 0f, 960f), new Vector3(1200f, 0f, 480f),
            new Vector3(-1020f, 0f, 720f), new Vector3(240f, 0f, -1080f), new Vector3(-1440f, 0f, -120f),
            new Vector3(1440f, 0f, -960f), new Vector3(0f, 0f, 1200f), new Vector3(720f, 0f, -1200f),
            new Vector3(1020f, 0f, 120f), new Vector3(-1140f, 0f, 180f), new Vector3(180f, 0f, 720f),
            new Vector3(-180f, 0f, -1140f), new Vector3(1500f, 0f, 360f), new Vector3(-720f, 0f, -1020f),
            new Vector3(450f, 0f, -180f), new Vector3(-450f, 0f, -660f), new Vector3(-780f, 0f, 60f),
            new Vector3(270f, 0f, 900f), new Vector3(-960f, 0f, -540f), new Vector3(1260f, 0f, -1140f),
            new Vector3(-1320f, 0f, 1140f), new Vector3(1560f, 0f, 1020f), new Vector3(600f, 0f, -780f),
            new Vector3(-240f, 0f, 360f), new Vector3(-1200f, 0f, -1260f), new Vector3(-540f, 0f, 1290f),
            new Vector3(1500f, 0f, 1290f), new Vector3(840f, 0f, -240f), new Vector3(-1560f, 0f, -540f),
            new Vector3(120f, 0f, -480f), new Vector3(-120f, 0f, 780f), new Vector3(1290f, 0f, 180f),
            new Vector3(-1560f, 0f, 600f), new Vector3(510f, 0f, 570f), new Vector3(-900f, 0f, -1350f),
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
            // Tiers 4-6 (2026-11-16) reuse the three structures at
            // larger scales (CampStructureScales) until they get
            // models of their own: T4 = a grown Spore Nest,
            // T5 = taller Stalker Spires, T6 = a giant Tyrant Maw.
            "Models/EnemyCampT1",
            "Models/EnemyCampT2",
            "Models/EnemyCampT3",
        };
        private static readonly float[] CampStructureScales =
            { 0f, 1f, 1f, 1f, 1.45f, 1.4f, 1.45f };
        private readonly GameObject[] _campPrefabs = new GameObject[7];

        // Arena swap (Patrick 2026-11-16: the simulated camp
        // fight "needs more work" - he picked the full arena
        // version): for the duration of a camp fight the camp
        // structure sinks away and a dedicated creature model
        // takes its place at full scale, the army's stylized
        // figures grow to arena size, and the camera pushes in.
        // On victory the creatures collapse and the camp then
        // vanishes as usual; on defeat they burrow away and
        // the structure rises back untouched. One signature
        // creature per tier - a tier whose path is still null
        // (or whose FBX is not installed) falls back to the
        // simulated fight, so the feature lands tier by tier.
        private static readonly string[] ArenaCreaturePaths =
        {
            null, // tier 0 = no camp
            "Models/ArenaSporeCrawler",
            "Models/ArenaVoidStalker", // Tier 2 (added 2026-11-16, after the T1 slice closed)
            null, // Tier 3 Brood Tyrant - model pending
            null, // Tier 4 Ashen Ravager - model pending
            null, // Tier 5 Dread Stalker - model pending
            null, // Tier 6 Void Tyrant - model pending
        };
        private readonly GameObject[] _arenaPrefabs = new GameObject[7];
        // While an arena fight owns the camera, Update leaves
        // pan/zoom/tap input alone - the fight coroutine is
        // the only writer of _panOffset/_camDist.
        private bool _arenaCameraActive;
        private const float ArenaCamDist = 50f;
        private const float ArenaFigureScale = 2.3f;
        private const float ArenaRingRadius = 13.5f;
        private const float ArenaAnchorScale = 1.35f;
        private const float ArenaKinScale = 0.8f;

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
            { -380f, 300f, 3.9f, 88f },
            { 410f, -320f, 3.3f, 77f },
            { 300f, 340f, 2.7f, 67f },
            { -120f, -300f, 3.0f, 70f },
            { 60f, 180f, 2.4f, 60f },
        };
        // (cx, cz, radius, depth, rim height, type) — types mirror
        // BuildExteriorTerrainV7.py: 0 moss saucer (gentle dip),
        // 1 geode basin (flat floor, smooth wall, soft lip),
        // 2 rolling hollow (wide, very shallow), 3 geyser cone
        // (raised mound, vent dip on top). The old (40,-40)
        // crater is now a 5-pit secondary chain of saucers.
        private static readonly float[,] TerrainCraters =
        {
{ -120f, -240f, 27.5f, 5.0f, 3.2f, 1f },
            { 120f, 260f, 22.5f, 4.0f, 2.6f, 3f },
            { 300f, -120f, 17.5f, 3.2f, 2.1f, 0f },
            { -320f, -180f, 20.0f, 3.5f, 2.2f, 2f },
            { 460f, 240f, 25.0f, 4.5f, 2.9f, 1f },
            { -460f, 180f, 18.8f, 3.4f, 2.2f, 0f },
            { 180f, -300f, 15.0f, 2.7f, 1.8f, 2f },
            { -80f, 80f, 13.8f, 2.4f, 1.6f, 0f },
            { 360f, 380f, 20.0f, 3.5f, 2.2f, 3f },
            { -220f, -360f, 23.8f, 4.3f, 2.7f, 1f },
            { 56f, -104f, 5.2f, 1.1f, 0.7f, 0f },
            { 68f, -94f, 6.5f, 1.4f, 0.9f, 0f },
            { 80f, -84f, 5.8f, 1.2f, 0.8f, 0f },
            { 92f, -74f, 4.8f, 1.0f, 0.6f, 0f },
            { 104f, -64f, 4.0f, 0.8f, 0.5f, 0f },
            { 520f, -80f, 16.2f, 2.9f, 1.9f, 0f },
            { -520f, -280f, 21.2f, 3.8f, 2.4f, 2f },
            { 240f, 120f, 12.5f, 2.2f, 1.4f, 0f },
            { -380f, 380f, 17.5f, 3.2f, 2.1f, 0f },
            { 20f, -220f, 16.2f, 2.9f, 1.9f, 2f },
            { 5f, -312f, 9.4f, 1.9f, 1.3f, 3f },
            { 154f, 34f, 15.0f, 3.0f, 1.8f, 0f },
            { 345f, 351f, 14.9f, 3.4f, 2.2f, 2f },
            { -80f, 327f, 11.1f, 2.2f, 1.3f, 0f },
            { 425f, -302f, 21.1f, 4.2f, 2.8f, 0f },
            { 316f, -230f, 9.9f, 2.1f, 1.3f, 2f },
            { -147f, 221f, 20.8f, 5.0f, 3.2f, 1f },
            { -346f, -6f, 10.9f, 2.4f, 1.6f, 3f },
            { -246f, 90f, 16.4f, 3.9f, 2.5f, 2f },
            { 325f, 168f, 19.7f, 4.6f, 3.0f, 0f },
            { -243f, 265f, 14.6f, 2.8f, 1.8f, 3f },
            { -375f, -222f, 22.0f, 5.0f, 3.2f, 2f },
            { -471f, -113f, 15.7f, 3.0f, 2.0f, 0f },
            { -397f, -11f, 15.8f, 3.6f, 2.3f, 2f },
            { -126f, -382f, 13.5f, 3.0f, 2.0f, 2f },
            { -324f, -266f, 12.9f, 3.0f, 1.8f, 3f },
            { -462f, -278f, 9.8f, 2.0f, 1.2f, 0f },
            { -108f, -107f, 11.4f, 2.5f, 1.6f, 0f },
            { 391f, -64f, 15.2f, 3.0f, 2.0f, 0f },
            { -161f, -117f, 20.1f, 4.0f, 2.6f, 0f },
            { -378f, 110f, 12.9f, 2.7f, 1.6f, 3f },
            { -203f, 95f, 22.8f, 4.5f, 2.7f, 2f },
            { 342f, 323f, 10.3f, 2.1f, 1.4f, 0f },
            { 118f, 9f, 14.8f, 2.9f, 1.8f, 0f },
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

        // One landform's height contribution at q = dist / radius.
        // Mirrors crater_height in BuildExteriorTerrainV7.py —
        // keep the math identical (types listed at TerrainCraters).
        private static float TerrainCraterHeight(float q, float depth, float rim, int ctype)
        {
            if (ctype == 1) // geode basin: flat floor, smooth wall, soft lip
            {
                if (q < 0.45f) return -0.80f * depth;
                if (q < 1f)
                {
                    float t = (q - 0.45f) / 0.55f;
                    return -0.80f * depth * Mathf.Pow(1f - t, 1.2f);
                }
                if (q < 1.5f)
                {
                    float sn = Mathf.Sin(Mathf.PI * (q - 1f) / 0.5f);
                    return 0.30f * rim * sn * sn;
                }
                return 0f;
            }
            if (ctype == 2) // rolling hollow: wide, very shallow, no rim
            {
                if (q < 1.6f) return -0.20f * depth * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(Mathf.PI * q / 1.6f), 1.3f);
                return 0f;
            }
            if (ctype == 3) // geyser cone: raised mound, vent dip on top
            {
                if (q >= 2.2f) return 0f;
                float cone = 1.40f * depth / Mathf.Pow(1f + (q / 0.40f) * (q / 0.40f), 1.5f);
                if (q < 0.14f) cone -= 0.36f * depth * (1f - q / 0.14f);
                return cone;
            }
            // moss saucer (0): a gentle dip, no rim
            if (q < 1f) return -0.35f * depth * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(Mathf.PI * q), 1.2f);
            return 0f;
        }

        // ---- Terrain transplant (2026-11-20, Phase 3) ----
        // The world is the old 1120x960 landscape at WorldScale x3
        // footprint and HeightScaleFactor x6 height, baked by
        // TerrainTransplantBuilder into a Unity TerrainData
        // (Resources/Terrain/LunarTerrain). TerrainHeight samples
        // that data, so hand-sculpting the terrain later moves the
        // gameplay ground truth with it. If the data asset is
        // missing (e.g. a fresh clone), the analytic replica below
        // reproduces the identical shape mathematically.
        private const float WorldScale = 3f;
        private const float HeightScaleFactor = 6f;
        private const float WorldHalfX = 560f * WorldScale; // 1680
        private const float WorldHalfZ = 480f * WorldScale; // 1440
        private const float TerrainMinY = -5.2f * HeightScaleFactor; // -31.2
        private const string TerrainDataPath = "Terrain/LunarTerrain";
        private const string TerrainMaterialPath = "Terrain/LunarTerrainMat";
        private const string TerrainRocksPath = "Terrain/LunarRocks";
        private const string TerrainRocksMaterialPath = "Terrain/LunarRocksMat";

        private static TerrainData _terrainData;
        private static bool _terrainLoadTried;

        private static TerrainData LoadTerrainData()
        {
            if (!_terrainLoadTried)
            {
                _terrainLoadTried = true;
                try { _terrainData = Resources.Load<TerrainData>(TerrainDataPath); }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[ExteriorView] Terrain data failed to load (" + TerrainDataPath + "): " + e.Message);
                }
            }
            return _terrainData;
        }

        /// <summary>The terrain's height at (x, z) in world units:
        /// sampled from the transplant TerrainData when present,
        /// else the analytic replica of the same landscape.</summary>
        public static float TerrainHeight(float x, float z)
        {
            var data = LoadTerrainData();
            if (data != null)
            {
                float u = Mathf.Clamp01((x + WorldHalfX) / (WorldHalfX * 2f));
                float v = Mathf.Clamp01((z + WorldHalfZ) / (WorldHalfZ * 2f));
                return TerrainMinY + data.GetInterpolatedHeight(u, v);
            }
            return AnalyticHeight(x / WorldScale, z / WorldScale) * HeightScaleFactor;
        }

        /// <summary>The ORIGINAL pre-transplant analytic height, in
        /// OLD world coordinates — the TerrainTransplantBuilder bake
        /// sampled exactly this function. Kept as the fallback shape;
        /// mirrors BuildExteriorTerrainV7.py. The site tables now
        /// hold NEW coordinates, so the flatten discs below divide
        /// them back by WorldScale.</summary>
        private static float AnalyticHeight(float x, float z)
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
                float dx = x - def.Position.x / WorldScale;
                float dz = z - def.Position.z / WorldScale;
                m = Mathf.Min(m, TerrainSmoothstep(rIn, rOut, Mathf.Sqrt(dx * dx + dz * dz)));
            }
            // Camp slot discs.
            foreach (var pos in CampSlotPositions)
            {
                float dx = x - pos.x / WorldScale;
                float dz = z - pos.z / WorldScale;
                m = Mathf.Min(m, TerrainSmoothstep(21f, 30f, Mathf.Sqrt(dx * dx + dz * dz)));
            }
            m *= 1f - TerrainSmoothstep(516f, 560f, Mathf.Abs(x));
            m *= 1f - TerrainSmoothstep(436f, 480f, Mathf.Abs(z));
            return Mathf.Clamp(d * m, -5.2f, 8.0f);
        }

        public static string CampTitleForTier(int tier)
        {
            switch (tier)
            {
                case 2: return "Void Stalker Camp";
                case 3: return "Brood Tyrant Camp";
                case 4: return "Ashen Ravager Camp";
                case 5: return "Dread Stalker Camp";
                case 6: return "Void Tyrant Camp";
                default: return "Spore Crawler Camp";
            }
        }

        public static string CampInfoForTier(int tier)
        {
            switch (tier)
            {
                case 2: return "Tier 2 — Void Stalkers\nExpedition enemy";
                case 3: return "Tier 3 — Brood Tyrant\nExpedition enemy";
                case 4: return "Tier 4 — Ashen Ravagers\nExpedition enemy";
                case 5: return "Tier 5 — Dread Stalkers\nExpedition enemy";
                case 6: return "Tier 6 — Void Tyrant\nExpedition enemy";
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
                // Terrain-height Y, matching the slot root (see
                // BuildCampSlots): the raw table value is y=0,
                // which only ever described the old flat discs.
                Position = new Vector3(CampSlotPositions[slot].x, TerrainHeight(CampSlotPositions[slot].x, CampSlotPositions[slot].z), CampSlotPositions[slot].z),
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
            for (int tier = 1; tier <= 6; tier++)
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
                // Sit the root ON the terrain (fix 2026-11-21):
                // CampSlotPositions carries y=0 from the old
                // world, where every slot disc was flattened to
                // h=0 so y=0 WAS the ground. The baked Unity
                // Terrain keeps each disc at its local height x6,
                // so a y=0 root buried the far-out camps under
                // their own pads - invisible, untappable (their
                // tap box sat under the surface) and with their
                // fights staged inside the hill (Patrick's Phase
                // 3 Play checks: FIND landing on empty ground,
                // unsendable camps, fight location off).
                var slotPos = CampSlotPositions[slot];
                go.transform.position = new Vector3(slotPos.x, TerrainHeight(slotPos.x, slotPos.z), slotPos.z);
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
            if (tier < 0 || tier > 6) tier = 0;
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
        // (tiers 1-3 are authored at final size, so scale 1;
        // tiers 4-6 scale a reused model up — the imported
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
            float structureScale = CampStructureScales[tier];
            if (structureScale > 0f && structureScale != 1f)
                go.transform.localScale = new Vector3(structureScale, structureScale, structureScale);
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
            // The x3 transplant world pushes sightlines past the
            // old 3000 far plane when panned into a corner — raise
            // it so the far side of the world never clips.
            var mainCam = Camera.main;
            if (mainCam != null && mainCam.farClipPlane < 6000f) mainCam.farClipPlane = 6000f;
            BuildAmbience();

            // Terrain transplant (2026-11-20, Phase 3): the ground
            // is a Unity Terrain built from the baked TerrainData
            // (TerrainTransplantBuilder), replacing the
            // ExteriorTerrain FBX heightfield and its tint/regolith
            // material code — the TerrainData carries its own splat
            // painting (Grass_Moss base coat for now; roads and
            // clearings arrive with the Phase 4 bake). The terrain
            // sits with its corner at (-WorldHalfX, TerrainMinY,
            // -WorldHalfZ), the same placement the builder's probes
            // verified.
            var terrainData = LoadTerrainData();
            if (terrainData == null)
            {
                Debug.LogWarning("[ExteriorView] LunarTerrain data is missing — the Map tab will show the base ground only. Run 'Lunar Dominion/Terrain Transplant/1. Build Terrain Data' in the Editor (the asset lives in Assets/_Project/Resources/Terrain).");
                return;
            }

            var terrainGo = new GameObject("ExteriorGround");
            var terrain = terrainGo.AddComponent<Terrain>();
            terrain.terrainData = terrainData;
            terrain.drawTreesAndFoliage = true;
            terrain.heightmapPixelError = 5f;
            terrain.detailObjectDistance = 160f;
            // Phase 5 flora draw settings: trees/rocks (Terrain
            // tree instances scattered by builder item 5) draw to
            // 900, billboard from 120, and at most 250 render as
            // full meshes at once — the mobile-sane caps; Phase 6
            // measures the real cost on the S24 Ultra.
            terrain.treeDistance = 900f;
            terrain.treeBillboardDistance = 120f;
            terrain.treeMaximumFullLODCount = 250;
            var terrainMat = Resources.Load<Material>(TerrainMaterialPath);
            if (terrainMat != null) terrain.materialTemplate = terrainMat;
            var terrainCollider = terrainGo.AddComponent<TerrainCollider>();
            terrainCollider.terrainData = terrainData;
            terrainGo.transform.position = new Vector3(-WorldHalfX, TerrainMinY, -WorldHalfZ);
            _terrain = terrainGo;

            // Phase 5 rocks: builder item 5 bakes the kit rocks
            // into one combined mesh in terrain-local space (the
            // tree system refuses to instance those prefabs), so
            // it is drawn here as a child of the terrain and
            // follows the Map tab's visibility with it. Absent
            // until item 5 has run with rocks found.
            var rocksMesh = Resources.Load<Mesh>(TerrainRocksPath);
            if (rocksMesh != null)
            {
                var rocksGo = new GameObject("ExteriorRocks");
                rocksGo.transform.SetParent(terrainGo.transform, false);
                var rocksFilter = rocksGo.AddComponent<MeshFilter>();
                rocksFilter.sharedMesh = rocksMesh;
                var rocksRenderer = rocksGo.AddComponent<MeshRenderer>();
                var rocksMat = Resources.Load<Material>(TerrainRocksMaterialPath);
                if (rocksMat != null) rocksRenderer.sharedMaterial = rocksMat;
                rocksRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rocksRenderer.receiveShadow = false;
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
        // orange Army 2, violet Army 3. Hidden until a march starts.
        // Nothing in a squad keeps a collider: the tap raycast only
        // checks map points, and a figure standing in front of a
        // camp must never shadow its tap.
        // BUG FIX 2026-11-16: only slots 0 and 1 were built — a
        // leftover from the two-squad excursion era. When standing
        // armies grew to three, slot 2's marker stayed null, so
        // StartMarch(2) returned silently: Army 3 was marked out
        // by the service but never marched, and its arrival
        // callback never fired (Patrick: "Army 3 never actually
        // leaves the base"). Army 3 only unlocks at Nexus 20,
        // which is why it surfaced late.
        private void BuildSquads()
        {
            _marchMarkers[0] = MakeSquadRoot("ExcursionMarker");
            _marchMarkers[1] = MakeSquadRoot("ExcursionMarker2");
            _marchMarkers[2] = MakeSquadRoot("ExcursionMarker3");
            BuildBanner(0, _marchMarkers[0]);
            BuildBanner(1, _marchMarkers[1]);
            BuildBanner(2, _marchMarkers[2]);
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
            return id == UnitId.HeroReyes || id == UnitId.HeroVega || id == UnitId.HeroOkafor
                || id == UnitId.HeroWard;
        }

        private static string HeroModelPath(UnitId id)
        {
            switch (id)
            {
                case UnitId.HeroReyes: return "Models/HeroReyes";
                case UnitId.HeroVega: return "Models/HeroVega";
                case UnitId.HeroOkafor: return "Models/HeroOkafor";
                case UnitId.HeroWard: return "Models/HeroWard";
                default: return null;
            }
        }

        private UnitClass ClassOf(UnitId id)
        {
            if (_classById == null)
            {
                _classById = new Dictionary<UnitId, UnitClass>();
                _tierById = new Dictionary<UnitId, int>();
                foreach (var def in UnitCatalog.CreateDefaultDefinitions())
                    if (def != null)
                    {
                        _classById[def.Id] = def.Class;
                        _tierById[def.Id] = def.Tier;
                    }
            }
            UnitClass cls;
            return _classById.TryGetValue(id, out cls) ? cls : UnitClass.Infantry;
        }

        private int TierOf(UnitId id)
        {
            if (_tierById == null) ClassOf(id);
            int tier;
            return _tierById.TryGetValue(id, out tier) ? tier : 1;
        }

        private static string TroopModelPath(UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Ranged: return "Models/TroopRanged";
                case UnitClass.Mechanized: return "Models/TroopMech";
                case UnitClass.Support: return null;
                default: return "Models/TroopInfantry";
            }
        }

        // Tier trim colours (Phase 3): the troop models carry a
        // "TroopTrim" material on their pauldron rims, chest stripes
        // and knee plates; swapping it per tier makes a squad's tier
        // read at a glance. T1 dim steel, then amber, cyan, ice,
        // gold, violet.
        private static Color TierTrimColor(int tier)
        {
            switch (tier)
            {
                case 2: return new Color(1.00f, 0.62f, 0.20f);
                case 3: return new Color(0.30f, 0.85f, 1.00f);
                case 4: return new Color(0.80f, 0.92f, 1.00f);
                case 5: return new Color(1.00f, 0.78f, 0.25f);
                case 6: return new Color(0.85f, 0.55f, 1.00f);
                default: return new Color(0.45f, 0.55f, 0.70f);
            }
        }

        private Material TierTrimMaterial(int tier)
        {
            if (_tierTrimMats == null)
            {
                _tierTrimMats = new Material[7];
                for (int t = 1; t <= 6; t++)
                    _tierTrimMats[t] = MakeLitMaterial(TierTrimColor(t), 1.1f);
            }
            if (tier < 1) tier = 1;
            if (tier > 6) tier = 6;
            return _tierTrimMats[tier];
        }

        private void ApplyTierTrim(GameObject model, int tier)
        {
            var trimMat = TierTrimMaterial(tier);
            if (trimMat == null) return;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name.StartsWith("TroopTrim"))
                    {
                        mats[i] = trimMat;
                        changed = true;
                    }
                }
                if (changed) renderer.sharedMaterials = mats;
            }
        }

        private static Color ClassColor(UnitId id, UnitClass cls)
        {
            switch (cls)
            {
                case UnitClass.Ranged: return new Color(0.95f, 0.50f, 0.15f);
                case UnitClass.Mechanized: return new Color(0.40f, 0.60f, 0.35f);
                case UnitClass.Support: return new Color(0.45f, 0.92f, 0.85f);
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
            // Stylized march (Patrick 2026-11-16): ONE figure per
            // unit TYPE in the army, not one per soldier — at
            // 80-160-man armies a per-soldier column is unreadable
            // (and heavy). Each hero is its own type, so heroes
            // still lead as themselves. The chip above the banner
            // carries the true headcount; combat uses the full
            // roster regardless. (Supersedes the 24-figure cap from
            // the big-armies push earlier the same day.)
            var distinct = new List<UnitId>();
            foreach (var id in ordered) if (!distinct.Contains(id)) distinct.Add(id);
            string key = string.Join(",", distinct);
            if (key == _squadKeys[slot]) return;
            _squadKeys[slot] = key;
            var root = _marchMarkers[slot];
            foreach (var fig in _squadFigures[slot]) if (fig != null) Destroy(fig);
            _squadFigures[slot].Clear();
            if (root == null) return;
            int renderCount = distinct.Count;
            for (int i = 0; i < renderCount; i++)
            {
                var fig = BuildFigure(distinct[i]);
                fig.transform.SetParent(root.transform, false);
                fig.transform.localPosition = FormationOffset(slot, i, renderCount);
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
            bool troopModel = false;
            if (hero)
            {
                var path = HeroModelPath(id);
                if (path != null) prefab = Resources.Load<GameObject>(path);
                scale = 1.35f; // heroes ~3.2 tall x 1.35 — still the tallest in the squad
            }
            if (prefab == null && !hero)
            {
                // Phase 3 class models first; the minis stay as the
                // fallback if an FBX has not been installed.
                if (_troopInfantryPrefab == null) _troopInfantryPrefab = Resources.Load<GameObject>("Models/TroopInfantry");
                if (_troopRangedPrefab == null) _troopRangedPrefab = Resources.Load<GameObject>("Models/TroopRanged");
                if (_troopMechPrefab == null) _troopMechPrefab = Resources.Load<GameObject>("Models/TroopMech");
                var cls = ClassOf(id);
                switch (cls)
                {
                    case UnitClass.Ranged: prefab = _troopRangedPrefab; break;
                    case UnitClass.Mechanized: prefab = _troopMechPrefab; break;
                    case UnitClass.Support: prefab = null; break;
                    default: prefab = _troopInfantryPrefab; break;
                }
                if (prefab != null)
                {
                    troopModel = true;
                    scale = cls == UnitClass.Mechanized ? MechFigureScale : TroopFigureScale;
                }
            }
            if (prefab == null)
            {
                if (_miniInfantryPrefab == null) _miniInfantryPrefab = Resources.Load<GameObject>("Models/MiniInfantry");
                if (_miniRangedPrefab == null) _miniRangedPrefab = Resources.Load<GameObject>("Models/MiniRanged");
                if (_miniMechPrefab == null) _miniMechPrefab = Resources.Load<GameObject>("Models/MiniMech");
                scale = MiniFigureScale;
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
                if (troopModel) ApplyTierTrim(model, TierOf(id));
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
                float x = (i % 2 == 1) ? -2.6f : 2.6f;
                return new Vector3(x, 0f, -3.4f * row);
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
        private void BuildRouteDots(int slot, Vector3 from, List<Vector3> waypoints)
        {
            HideRouteDots(slot);
            // Dots trace the road path (Round 3 Phase 2): walk
            // the waypoint polyline at even arc-length intervals.
            var pts = new List<Vector3> { new Vector3(from.x, 0f, from.z) };
            foreach (var w in waypoints) pts.Add(new Vector3(w.x, 0f, w.z));
            float total = 0f;
            for (int i = 0; i + 1 < pts.Count; i++) total += Vector3.Distance(pts[i], pts[i + 1]);
            int count = Mathf.Clamp(Mathf.RoundToInt(total / 9f), 3, 48);
            if (_routeDotMats[slot] == null) _routeDotMats[slot] = MakeLitMaterial(ArmyColors[slot], 0.7f);
            int seg = 0;
            float segStart = 0f;
            for (int i = 1; i <= count; i++)
            {
                float want = total * i / (count + 1);
                while (seg + 1 < pts.Count - 1 && segStart + Vector3.Distance(pts[seg], pts[seg + 1]) < want)
                {
                    segStart += Vector3.Distance(pts[seg], pts[seg + 1]);
                    seg++;
                }
                float segLen = Vector3.Distance(pts[seg], pts[seg + 1]);
                float t = segLen > 0f ? (want - segStart) / segLen : 0f;
                Vector3 p = Vector3.Lerp(pts[seg], pts[seg + 1], t);
                var dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                dot.name = "RouteDot";
                dot.SetActive(_visible);
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
            _markerWanted[slot] = true;
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
            // Boss — War Robot at its lair (-1440, -1200), lair index 21.
            // Scale 4x: a GIANT war robot, twice the size of the Brood
            // Tyrant. Stands 36 units north of the lair structure so the
            // two do not overlap. Tapping it selects the lair.
            new CreatureDef { ResourcePath = "Models/WarRobot", CampIndex = 21, Position = new Vector3(-1440f, 0f, -1164f), Scale = 4f },
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
                    // Generous tap box (2026-11-20): at the x3
                    // map zoom the old 4-unit box covered a few
                    // screen pixels; sites sit hundreds of units
                    // apart now, so a fat box cannot mis-tap a
                    // neighbour.
                    box.center = new Vector3(0f, 3f, 0f);
                    box.size = new Vector3(12f, 6f, 12f);
                }
                go.SetActive(false);
                // The creature IS its camp for tapping: it carries the
                // camp's MapPointData, so the panel and the excursion
                // treat a creature tap exactly like a camp tap.
                _mapPoints.Add(new MapPoint { Go = go, Data = DataForNodeDef(def.CampIndex) });
            }
        }

        // Resource node levels (2026-11-16): the service owns the
        // levels (GameState.NodeLevel) and mirrors them here via
        // SetNodeLevel; DataForNodeDef prices the yield and labels
        // the point from the mirrored level.
        private readonly int[] _nodeLevel = new int[18];

        /// <summary>Yield multiplier for a node level: level 1 pays
        /// the node's base amount; each further level adds 30%
        /// (level 6 = x2.5).</summary>
        public static float NodeYieldMultiplier(int level)
        {
            if (level < 1) level = 1;
            if (level > 6) level = 6;
            return 1f + 0.3f * (level - 1);
        }

        /// <summary>Mirror one node's level from the service and
        /// refresh its cached point in place (never replace the
        /// data instance — the Bootstrap may hold it as the
        /// selected point, same rule as SetCampSlot).</summary>
        public void SetNodeLevel(int index, int level)
        {
            if (index < 0 || index >= _nodeLevel.Length) return;
            if (level < 1) level = 1;
            if (level > 6) level = 6;
            if (_nodeLevel[index] == level) return;
            _nodeLevel[index] = level;
            foreach (var point in _mapPoints)
            {
                if (point.Data == null || point.Data.Index != index) continue;
                if (point.Data.Kind != MapPointKind.Biomass
                    && point.Data.Kind != MapPointKind.Alloy
                    && point.Data.Kind != MapPointKind.Helium3) continue;
                NodePointFields(index, out var title, out var info, out var amount);
                point.Data.Title = title;
                point.Data.Info = info;
                point.Data.Amount = amount;
            }
        }

        private void NodePointFields(int index, out string title, out string info, out int amount)
        {
            var def = NodeDefs[index];
            title = def.Title;
            info = def.Info;
            amount = def.Amount;
            if (index < 0 || index >= 18 || def.Amount <= 0) return;
            int level = _nodeLevel[index];
            if (level < 1) level = 1;
            if (level > 6) level = 6;
            float mult = NodeYieldMultiplier(level);
            amount = Mathf.RoundToInt(def.Amount * mult);
            title = def.Title + " Lv " + level;
            if (level > 1)
            {
                string resName = def.Kind == MapPointKind.Biomass ? "Biomass"
                    : def.Kind == MapPointKind.Alloy ? "Alloy" : "Helium-3";
                info = resName + ": " + amount + " (Lv " + level + " node, x" + mult.ToString("0.0") + ")";
            }
        }

        // Structured data for a NodeDefs entry (nodes 0-17, camps 18-20, lair 21).
        private MapPointData DataForNodeDef(int index)
        {
            var def = NodeDefs[index];
            NodePointFields(index, out var title, out var info, out var amount);
            return new MapPointData
            {
                Index = index,
                Kind = def.Kind,
                Tier = def.Tier,
                Amount = amount,
                Position = def.Position,
                Title = title,
                Info = info
            };
        }

        private void BuildMapPoints()
        {
            for (int i = 0; i < NodeDefs.Length; i++)
            {
                var def = NodeDefs[i];
                // Camps (indices 18-20 in NodeDefs) are NOT built here:
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
                    // Generous tap box (2026-11-20): at the x3
                    // map zoom the old 4-unit box covered a few
                    // screen pixels; sites sit hundreds of units
                    // apart now, so a fat box cannot mis-tap a
                    // neighbour.
                    box.center = new Vector3(0f, 3f, 0f);
                    box.size = new Vector3(12f, 6f, 12f);
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

        // Excursion marches: each army slot's marker walks from its
        // start to its target at a steady speed, then fires the
        // arrival callback (the service resolves the outcome and
        // starts the return march). Runs whether or not the Map is
        // visible — see Update.
        private void TickMarches()
        {
            for (int slot = 0; slot < _marchMarkers.Length; slot++)
            {
                if (!_marching[slot] || _marchMarkers[slot] == null) continue;
                // Planar step at full MarchSpeed (2026-11-20 fix):
                // the waypoints carry Y=0, so a 3D MoveTowards
                // spent part of every step on the vertical leg
                // toward y=0 - at HeightScale 6 (heights to +/-48)
                // the ground speed collapsed near each waypoint,
                // reading as the army slowing down at every turn.
                // Move on the ground plane only; the ride height
                // below is presentation, as before.
                Vector3 curPos = _marchMarkers[slot].transform.position;
                Vector3 tgtPos = _marchTargets[slot];
                float stepX = tgtPos.x - curPos.x;
                float stepZ = tgtPos.z - curPos.z;
                float distSq = stepX * stepX + stepZ * stepZ;
                float stepLen = MarchSpeed * Time.deltaTime;
                Vector3 next;
                if (distSq <= stepLen * stepLen)
                {
                    next = new Vector3(tgtPos.x, curPos.y, tgtPos.z);
                }
                else
                {
                    float inv = stepLen / Mathf.Sqrt(distSq);
                    next = new Vector3(curPos.x + stepX * inv, curPos.y, curPos.z + stepZ * inv);
                }
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
                    var path = _marchPaths[slot];
                    if (path != null && _marchPathIndex[slot] < path.Count - 1)
                    {
                        // Waypoint reached: head for the next one.
                        // The march, its route dots and its arrival
                        // callback all carry on.
                        _marchPathIndex[slot]++;
                        _marchTargets[slot] = path[_marchPathIndex[slot]];
                    }
                    else
                    {
                        _marching[slot] = false;
                        _marchPaths[slot] = null;
                        HideRouteDots(slot);
                        var callback = _marchCallbacks[slot];
                        _marchCallbacks[slot] = null;
                        callback?.Invoke();
                    }
                }
            }
        }

        private void Update()
        {
            // Marches run in real time even while the Map is hidden
            // (Patrick 2026-11-15): the army keeps walking while the
            // player manages the base, arrivals resolve through the
            // service, and the results land in the Inbox. The markers
            // are inactive while the Map is closed, so the tick just
            // advances their transforms unseen.
            TickMarches();
            if (!_visible) return;
            // Spore motes drift around the player's current view.
            if (_motes != null) _motes.position = new Vector3(_panOffset.x, 16f, _panOffset.z);
            TickSquads(Time.deltaTime);
            TickSearch(Time.deltaTime);
            // Arena swap: while a camp fight's arena owns the
            // camera, pan/zoom/tap input is suspended - the
            // fight coroutine drives _panOffset/_camDist itself
            // and restores them before it completes.
            if (_arenaCameraActive) return;
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

            // Zoom (Round 3 prototype): mouse wheel in the Editor,
            // two-finger pinch on the phone. While a pinch is in
            // progress the one-finger press is cancelled, so the
            // gesture never pans the map or fires as a tap.
            bool pinching = false;
#if ENABLE_INPUT_SYSTEM
            var zoomMouse = UnityEngine.InputSystem.Mouse.current;
            if (zoomMouse != null)
            {
                // Wheel units vary by device and host (120 per notch
                // in a Windows build, ~1 per notch in the Editor), so
                // step by SIGN, not magnitude — the first version's
                // 0.06 factor assumed the big units and the Editor's
                // small ones moved the target by a hair (Patrick's
                // wheel "did nothing", 2026-11-15).
                float wheel = zoomMouse.scroll.ReadValue().y;
                if (wheel != 0f)
                    _camDistTarget = Mathf.Clamp(_camDistTarget - Mathf.Sign(wheel) * 9f, CamDistMin, CamDistMax);
            }
            var zoomTouch = UnityEngine.InputSystem.Touchscreen.current;
            if (zoomTouch != null)
            {
                Vector2 pinchA = Vector2.zero, pinchB = Vector2.zero;
                int pinchCount = 0;
                var allTouches = zoomTouch.touches;
                for (int i = 0; i < allTouches.Count && pinchCount < 2; i++)
                {
                    if (!allTouches[i].press.isPressed) continue;
                    if (pinchCount == 0) pinchA = allTouches[i].position.ReadValue();
                    else pinchB = allTouches[i].position.ReadValue();
                    pinchCount++;
                }
                if (pinchCount == 2)
                {
                    pinching = true;
                    float pinchDist = Vector2.Distance(pinchA, pinchB);
                    if (_pinchPrevDist > 0f)
                        _camDistTarget = Mathf.Clamp(_camDistTarget + (_pinchPrevDist - pinchDist) * 0.12f, CamDistMin, CamDistMax);
                    _pinchPrevDist = pinchDist;
                }
                else _pinchPrevDist = -1f;
            }
#else
            float wheelLegacy = Input.GetAxis("Mouse ScrollWheel");
            if (wheelLegacy != 0f)
                _camDistTarget = Mathf.Clamp(_camDistTarget - Mathf.Sign(wheelLegacy) * 9f, CamDistMin, CamDistMax);
            if (Input.touchCount == 2)
            {
                pinching = true;
                float pinchDist = Vector2.Distance(Input.GetTouch(0).position, Input.GetTouch(1).position);
                if (_pinchPrevDist > 0f)
                    _camDistTarget = Mathf.Clamp(_camDistTarget + (_pinchPrevDist - pinchDist) * 0.12f, CamDistMin, CamDistMax);
                _pinchPrevDist = pinchDist;
            }
            else _pinchPrevDist = -1f;
#endif
            // Keyboard zoom (PageUp / + and PageDown / - held):
            // a wheel-independent way to drive the same target, so
            // the zoom machinery can be verified even on a mouse
            // whose wheel the Editor is not passing through.
#if ENABLE_INPUT_SYSTEM
            var zoomKeys = UnityEngine.InputSystem.Keyboard.current;
            if (zoomKeys != null)
            {
                if (zoomKeys.pageUpKey.isPressed || zoomKeys.equalsKey.isPressed)
                    _camDistTarget = Mathf.Clamp(_camDistTarget - 55f * Time.deltaTime, CamDistMin, CamDistMax);
                if (zoomKeys.pageDownKey.isPressed || zoomKeys.minusKey.isPressed)
                    _camDistTarget = Mathf.Clamp(_camDistTarget + 55f * Time.deltaTime, CamDistMin, CamDistMax);
            }
#else
            if (Input.GetKey(KeyCode.PageUp) || Input.GetKey(KeyCode.Equals))
                _camDistTarget = Mathf.Clamp(_camDistTarget - 55f * Time.deltaTime, CamDistMin, CamDistMax);
            if (Input.GetKey(KeyCode.PageDown) || Input.GetKey(KeyCode.Minus))
                _camDistTarget = Mathf.Clamp(_camDistTarget + 55f * Time.deltaTime, CamDistMin, CamDistMax);
#endif
            if (pinching)
            {
                _pointerDown = false;
                _isDrag = true; // a pinch release must not fire as a tap
            }
            TickZoom(Time.deltaTime);

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
            // together, so this is just the current zoom distance).
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

        // Ease the camera distance toward its target so wheel and
        // pinch zooms glide instead of stepping.
        private void TickZoom(float dt)
        {
            if (Mathf.Abs(_camDistTarget - _camDist) < 0.05f)
            {
                if (_camDist != _camDistTarget)
                {
                    _camDist = _camDistTarget;
                    ApplyCamera();
                }
                return;
            }
            _camDist = Mathf.Lerp(_camDist, _camDistTarget, 1f - Mathf.Exp(-10f * dt));
            ApplyCamera();
        }

        private void ApplyCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // Pitch flattens as the camera closes in: today's
            // 48.5 degrees at the default distance, ~21 degrees at
            // the closest zoom, a touch steeper zoomed fully out.
            // Yaw stays at the fixed 45-degree diagonal.
            float pitchDeg = _camDist >= CamDistDefault
                ? Mathf.Lerp(CamPitchDefault, CamPitchFar, (_camDist - CamDistDefault) / (CamDistMax - CamDistDefault))
                : Mathf.Lerp(CamPitchClose, CamPitchDefault, (_camDist - CamDistMin) / (CamDistDefault - CamDistMin));
            float pitch = pitchDeg * Mathf.Deg2Rad;
            float horiz = _camDist * Mathf.Cos(pitch);
            // Ride the ground under the pan target (fix
            // 2026-11-21): the rig used to position and aim at
            // y=0, which was the ground everywhere in the old
            // world (sites flattened to h=0, nothing above 8).
            // At HeightScale 6 a camp pad can sit at +30 - the
            // arena camera at distance 50 ended up below the pad,
            // inside the hill, for the whole fight (Patrick's
            // Phase 3 retest video: ~3 s of full-screen grass at
            // the T2 fight). Position and LookAt now both take
            // the target's terrain height; at the base (h=0) and
            // in flat country this changes nothing.
            float groundY = TerrainHeight(_panOffset.x, _panOffset.z);
            Vector3 pos = new Vector3(horiz * 0.70710678f, _camDist * Mathf.Sin(pitch), horiz * 0.70710678f) + _panOffset;
            pos.y += groundY;
            // Never sink the camera into a highland or a geyser cone.
            float floorY = TerrainHeight(pos.x, pos.z) + 5f;
            if (pos.y < floorY) pos.y = floorY;
            cam.transform.position = pos;
            cam.transform.LookAt(new Vector3(_panOffset.x, groundY, _panOffset.z));
        }

        private void HandleTap(Vector2 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screenPos);
            // Tap on a node/camp shows its info; a tap anywhere else
            // on the map hides any info panel that is showing.
            bool hitPoint = false;
            // RaycastAll, not a single Raycast (fix 2026-11-20):
            // the Unity Terrain carries a TerrainCollider (the
            // old FBX ground had none), so a single first-hit
            // raycast stops at the ground whenever a slope or a
            // crater rim sits in front of a point's small
            // collider - some T1 nodes became untappable
            // (Patrick). Take the closest hit that belongs to a
            // point, with a small grace past the first hit overall
            // so a point sunk in its flattened disc still counts,
            // while a point hidden behind a ridge (a much longer
            // gap) does not. Range up from 900: at the x3 zoom-out
            // a far-edge tap can travel farther than that.
            var hits = Physics.RaycastAll(ray, 3000f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            float firstDist = hits.Length > 0 ? hits[0].distance : 0f;
            for (int h = 0; h < hits.Length && !hitPoint; h++)
            {
                if (hits[h].distance > firstDist + 15f) break;
                foreach (var point in _mapPoints)
                {
                    if (point.Go == null || point.Data == null) continue;
                    if (hits[h].transform == point.Go.transform || hits[h].transform.IsChildOf(point.Go.transform))
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

        // ---- Road network (Round 3 Phase 2, Patrick 2026-11-15) ----
        // The same tables BuildExteriorTerrainV8.py paints into
        // the terrain tint (both generated by gen_roads.py):
        // junction 0 is the base; 1..48 are a polar grid (16 rays
        // x 3 rings); 49..52 are the corner nodes. Junctions were
        // nudged clear of every site disc and landform.
        private static readonly Vector2[] RoadJunctions =
        {
            new Vector2(0.0f, 0.0f),
            new Vector2(480.0f, 0.0f),
            new Vector2(960.0f, 0.0f),
            new Vector2(1410.0f, 0.0f),
            new Vector2(443.4f, 183.6f),
            new Vector2(886.8f, 367.5f),
            new Vector2(1302.6f, 539.7f),
            new Vector2(339.3f, 339.3f),
            new Vector2(678.9f, 678.9f),
            new Vector2(961.5f, 1031.1f),
            new Vector2(183.6f, 443.4f),
            new Vector2(398.1f, 873.6f),
            new Vector2(513.9f, 1240.8f),
            new Vector2(0.0f, 480.0f),
            new Vector2(0.0f, 960.0f),
            new Vector2(-151.2f, 1231.5f),
            new Vector2(-168.0f, 449.7f),
            new Vector2(-367.5f, 886.8f),
            new Vector2(-426.3f, 1273.5f),
            new Vector2(-351.0f, 327.3f),
            new Vector2(-678.9f, 678.9f),
            new Vector2(-996.9f, 996.9f),
            new Vector2(-443.4f, 183.6f),
            new Vector2(-886.8f, 367.5f),
            new Vector2(-1283.1f, 584.7f),
            new Vector2(-480.0f, 0.0f),
            new Vector2(-960.0f, 0.0f),
            new Vector2(-1410.0f, 0.0f),
            new Vector2(-443.4f, -183.6f),
            new Vector2(-886.8f, -367.5f),
            new Vector2(-1302.6f, -539.7f),
            new Vector2(-268.5f, -397.8f),
            new Vector2(-678.9f, -678.9f),
            new Vector2(-996.9f, -996.9f),
            new Vector2(-183.6f, -443.4f),
            new Vector2(-367.5f, -886.8f),
            new Vector2(-426.3f, -1273.5f),
            new Vector2(-0.0f, -480.0f),
            new Vector2(-66.9f, -957.6f),
            new Vector2(-0.0f, -1240.8f),
            new Vector2(236.4f, -417.9f),
            new Vector2(367.5f, -886.8f),
            new Vector2(513.9f, -1240.8f),
            new Vector2(339.3f, -339.3f),
            new Vector2(678.9f, -678.9f),
            new Vector2(996.9f, -996.9f),
            new Vector2(477.0f, -54.3f),
            new Vector2(835.5f, -472.8f),
            new Vector2(1302.6f, -539.7f),
            new Vector2(1356.0f, 1137.6f),
            new Vector2(-1431.9f, 1040.4f),
            new Vector2(-1315.5f, -1184.4f),
            new Vector2(1394.7f, -1089.6f),
        };
        private static readonly int[,] RoadEdges =
        {
            { 0, 1 },
            { 0, 4 },
            { 0, 7 },
            { 0, 10 },
            { 0, 13 },
            { 0, 16 },
            { 0, 19 },
            { 0, 22 },
            { 0, 25 },
            { 0, 28 },
            { 0, 31 },
            { 0, 34 },
            { 0, 37 },
            { 0, 40 },
            { 0, 43 },
            { 0, 46 },
            { 1, 2 },
            { 1, 4 },
            { 1, 46 },
            { 2, 3 },
            { 2, 5 },
            { 2, 47 },
            { 3, 6 },
            { 3, 48 },
            { 4, 5 },
            { 4, 7 },
            { 5, 6 },
            { 5, 8 },
            { 6, 9 },
            { 6, 49 },
            { 7, 8 },
            { 7, 10 },
            { 8, 9 },
            { 8, 11 },
            { 9, 12 },
            { 9, 49 },
            { 10, 11 },
            { 10, 13 },
            { 11, 12 },
            { 11, 14 },
            { 12, 15 },
            { 13, 14 },
            { 13, 16 },
            { 14, 15 },
            { 14, 17 },
            { 15, 18 },
            { 16, 17 },
            { 16, 19 },
            { 17, 18 },
            { 17, 20 },
            { 18, 21 },
            { 19, 20 },
            { 19, 22 },
            { 20, 21 },
            { 20, 23 },
            { 21, 24 },
            { 21, 50 },
            { 22, 23 },
            { 22, 25 },
            { 23, 24 },
            { 23, 26 },
            { 24, 27 },
            { 24, 50 },
            { 25, 26 },
            { 25, 28 },
            { 26, 27 },
            { 26, 29 },
            { 27, 30 },
            { 28, 29 },
            { 28, 31 },
            { 29, 30 },
            { 29, 32 },
            { 30, 33 },
            { 30, 51 },
            { 31, 32 },
            { 31, 34 },
            { 32, 33 },
            { 32, 35 },
            { 33, 36 },
            { 33, 51 },
            { 34, 35 },
            { 34, 37 },
            { 35, 36 },
            { 35, 38 },
            { 36, 39 },
            { 37, 38 },
            { 37, 40 },
            { 38, 39 },
            { 38, 41 },
            { 39, 42 },
            { 40, 41 },
            { 40, 43 },
            { 41, 42 },
            { 41, 44 },
            { 42, 45 },
            { 43, 44 },
            { 43, 46 },
            { 44, 45 },
            { 44, 47 },
            { 45, 48 },
            { 45, 52 },
            { 46, 47 },
            { 47, 48 },
            { 48, 52 },
        };

        private static List<int>[] _roadAdj;

        private static int NearestRoadJunction(Vector2 p)
        {
            int best = 0;
            float bestSq = float.MaxValue;
            for (int i = 0; i < RoadJunctions.Length; i++)
            {
                float dx = p.x - RoadJunctions[i].x;
                float dy = p.y - RoadJunctions[i].y;
                float sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        // Dijkstra over the junction graph (53 nodes - a plain
        // O(n^2) scan is plenty and has no heap to get wrong).
        private static List<int> JunctionPath(int start, int goal)
        {
            if (_roadAdj == null)
            {
                _roadAdj = new List<int>[RoadJunctions.Length];
                for (int i = 0; i < _roadAdj.Length; i++) _roadAdj[i] = new List<int>();
                for (int e = 0; e < RoadEdges.GetLength(0); e++)
                {
                    int a = RoadEdges[e, 0], b = RoadEdges[e, 1];
                    _roadAdj[a].Add(b);
                    _roadAdj[b].Add(a);
                }
            }
            int n = RoadJunctions.Length;
            var dist = new float[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[start] = 0f;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < bestD) { bestD = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                if (u == goal) break;
                foreach (int v in _roadAdj[u])
                {
                    float nd = dist[u] + Vector2.Distance(RoadJunctions[u], RoadJunctions[v]);
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }
            var path = new List<int>();
            int cur = goal;
            while (cur != -1)
            {
                path.Add(cur);
                if (cur == start) break;
                cur = prev[cur];
            }
            path.Reverse();
            return path;
        }

        // The waypoint list for a march: from -> nearest junction
        // -> along the roads -> nearest junction -> to. Mirrors
        // gen_roads.py exactly, including its driveway rule: when
        // one end is the base and the road route would run more
        // than 1.45x the straight line, the site has its own
        // direct road (painted in the tint as a driveway) and the
        // march takes it. Waypoint Y is 0 - TickMarches rides the
        // terrain height itself.
        private static List<Vector3> RoadPath(Vector3 from, Vector3 to)
        {
            var a = new Vector2(from.x, from.z);
            var b = new Vector2(to.x, to.z);
            int ja = NearestRoadJunction(a);
            int jb = NearestRoadJunction(b);
            var nodes = JunctionPath(ja, jb);
            float roadLen = Vector2.Distance(a, RoadJunctions[ja]);
            for (int i = 0; i + 1 < nodes.Count; i++)
                roadLen += Vector2.Distance(RoadJunctions[nodes[i]], RoadJunctions[nodes[i + 1]]);
            roadLen += Vector2.Distance(RoadJunctions[jb], b);
            bool baseEnd = (ja == 0 && a == RoadJunctions[0]) || (jb == 0 && b == RoadJunctions[0]);
            var result = new List<Vector3>();
            if (baseEnd && roadLen > 1.45f * Vector2.Distance(a, b))
            {
                result.Add(to);
                return result;
            }
            Vector2 last = a;
            foreach (int node in nodes)
            {
                Vector2 p = RoadJunctions[node];
                if (Vector2.Distance(last, p) < 0.5f) continue;
                result.Add(new Vector3(p.x, 0f, p.y));
                last = p;
            }
            result.Add(to);
            return result;
        }

        // ---- Phase 4 paint access (terrain builder, Editor) ----
        // The builder paints the road network and site clearings
        // from these same tables, so the painted roads are the
        // routes the marches actually walk - the visual network
        // can never disagree with the pathing.
        public static Vector2[] RoadJunctionTable => RoadJunctions;
        public static int[,] RoadEdgeTable => RoadEdges;
        public static List<Vector3> RoadPathForPaint(Vector3 from, Vector3 to) => RoadPath(from, to);

        /// <summary>Every site that gets a painted clearing and a
        /// painted road leg: (world XZ, clearing radius). Radii are
        /// the baked flatten discs' outer radii (old radii x3):
        /// nodes 51, lair 84, camp slots 90. The base clearing
        /// (origin, r 180) is added by the builder itself.</summary>
        public static List<(Vector2 pos, float radius)> PaintSites()
        {
            var sites = new List<(Vector2, float)>();
            foreach (var def in NodeDefs)
            {
                if (def.Kind == MapPointKind.Camp) continue;
                float r = def.Kind == MapPointKind.BossLair ? 84f : 51f;
                sites.Add((new Vector2(def.Position.x, def.Position.z), r));
            }
            foreach (var pos in CampSlotPositions)
                sites.Add((new Vector2(pos.x, pos.z), 90f));
            return sites;
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
            if (marker == null)
            {
                // Never fail silently again (the Army 3 bug hid
                // behind this quiet return): a missing marker
                // means the march — and its arrival callback —
                // would never run.
                Debug.LogWarning($"StartMarch: no squad marker for army slot {slot}; march not started.");
                return;
            }
            // Sending an army out closes the search bar (Patrick
            // 2026-11-12): the find UI has done its job once the
            // squad is marching. Also clears the highlights.
            SetFindBarOpen(false);
            from.y = TerrainHeight(from.x, from.z) + MarkerRideHeight;
            marker.transform.position = from;
            // A march can start while the Map is hidden (a harvest
            // finished, an off-screen fight resolved): the marker
            // only appears when the Map is open.
            _markerWanted[slot] = true;
            marker.SetActive(_visible);
            _squadModes[slot] = SquadModeMarching;
            var path = RoadPath(from, to);
            _marchPaths[slot] = path;
            _marchPathIndex[slot] = 0;
            BuildRouteDots(slot, from, path);
            _marchTargets[slot] = path.Count > 0 ? path[0] : to;
            _marchCallbacks[slot] = onArrive;
            _marching[slot] = true;
        }

        // Stop one slot's march and hide its marker. Called when a
        // march completes (the service ends the excursion) or when
        // an off-screen occupation finishes and its cosmetic return
        // is skipped. Tab switches no longer cancel marches — they
        // only hide the markers (see SetMarkersVisible).
        public void CancelMarch(int slot)
        {
            if (slot < 0 || slot >= _marchMarkers.Length) return;
            _marching[slot] = false;
            _marchCallbacks[slot] = null;
            _marchPaths[slot] = null;
            _markerWanted[slot] = false;
            HideRouteDots(slot);
            if (_marchMarkers[slot] != null) _marchMarkers[slot].SetActive(false);
        }

        // Show or hide every slot's marker and route dots to match
        // the Map's visibility, WITHOUT touching march state: an
        // army mid-march keeps its position, target, and callback,
        // and resumes on screen when the Map reopens (it never
        // actually stopped — TickMarches runs while hidden too).
        private void SetMarkersVisible(bool visible)
        {
            for (int slot = 0; slot < _marchMarkers.Length; slot++)
            {
                if (_marchMarkers[slot] != null)
                    _marchMarkers[slot].SetActive(visible && _markerWanted[slot]);
                bool dotsVisible = visible && _marching[slot];
                foreach (var dot in _routeDots[slot])
                    if (dot != null) dot.SetActive(dotsVisible);
            }
        }

        // Camp fight presentation (2026-11-09; simulated version
        // rebuilt 2026-11-16; arena swap added the same day at
        // Patrick's direction): the combat has already resolved
        // in the service — this is presentation of the known
        // outcome. When the camp's tier has an arena creature
        // (see ArenaCreaturePaths), the fight plays as the ARENA
        // SWAP: the camp structure sinks, the creature and its
        // kin rise in its place, the army grows to arena size
        // on a wider ring, and the camera pushes in; the clash
        // and outcome beats run at that scale, then everything
        // swaps back before onComplete fires. Tiers without a
        // creature keep the SIMULATED fight: enemy figures (one
        // per enemy type in the encounter, rust-red class
        // models) rise inside the army's ring and clash there.
        // Either way the clash length scales with the fight's
        // real round count, figures on the losing side tip over
        // as it goes, and the army's fall count follows the
        // real casualty fraction. onComplete fires last — the
        // service shows the result, vanishes the camp on
        // victory, and starts the return march.
        public void PlayFightAnimation(int slot, int campIndex, int campTier, int campSlot, bool victory, int rounds, float playerCasualtyFrac, List<UnitClass> enemyTypes, Action onComplete)
        {
            StartCoroutine(FightAnimationRoutine(slot, campIndex, campTier, campSlot, victory, rounds, playerCasualtyFrac, enemyTypes, onComplete));
        }

        // One arena creature for the camp fight: the tier's
        // dedicated Blender model inside a wrapper + facing
        // pivot (the creatures are authored facing +X like the
        // troops, so the same 180-degree pivot puts the front
        // on the wrapper's +Z; the imported model's own
        // transform is never touched). Null when the tier has
        // no creature yet or its FBX is not installed — the
        // caller then falls back to the simulated fight.
        private GameObject BuildArenaCreature(int tier)
        {
            if (tier < 1 || tier > 6) return null;
            var path = ArenaCreaturePaths[tier];
            if (string.IsNullOrEmpty(path)) return null;
            if (_arenaPrefabs[tier] == null)
            {
                try
                {
                    _arenaPrefabs[tier] = Resources.Load<GameObject>(path);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[ExteriorView] Arena creature failed to load (" + path + "): " + e.Message);
                }
            }
            var prefab = _arenaPrefabs[tier];
            if (prefab == null) return null;
            var wrap = new GameObject("ArenaCreature");
            var facing = new GameObject("Facing");
            facing.transform.SetParent(wrap.transform, false);
            facing.transform.localRotation = Quaternion.Euler(0f, FigureFacingOffsetDeg, 0f);
            var model = Instantiate(prefab);
            model.transform.SetParent(facing.transform, false);
            foreach (var col in model.GetComponentsInChildren<Collider>(true)) Destroy(col);
            return wrap;
        }

        // One enemy figure for the camp fight: the class model in a
        // hostile rust-red tint. The camp creatures have no minis of
        // their own — stylized, matching the one-figure-per-type
        // march. The tint instances each renderer's material, so
        // the shared tier-trim materials stay untouched.
        private GameObject BuildEnemyFigure(UnitClass cls)
        {
            UnitId rep;
            switch (cls)
            {
                case UnitClass.Ranged: rep = UnitId.PulseTrooper; break;
                case UnitClass.Mechanized: rep = UnitId.AssaultMech; break;
                default: rep = UnitId.ScoutMarine; break;
            }
            var fig = BuildFigure(rep);
            var tint = new Color(0.60f, 0.20f, 0.16f, 1f);
            foreach (var r in fig.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var m = r.material;
                if (m != null && m.HasProperty("_Color")) m.color = m.color * tint;
            }
            return fig;
        }

        private static List<int> ShuffledIndices(int count)
        {
            var list = new List<int>();
            for (int i = 0; i < count; i++) list.Add(i);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
            return list;
        }

        private static float FallProgress(float fallStart, float t)
        {
            return fallStart < 0f ? 0f : Mathf.Clamp01((t - fallStart) / 0.35f);
        }

        private static int PickStanding(int count, float[] fallStarts, float t)
        {
            int standing = 0;
            for (int i = 0; i < count; i++)
                if (fallStarts[i] < 0f || fallStarts[i] > t) standing++;
            if (standing == 0) return -1;
            int pick = UnityEngine.Random.Range(0, standing);
            for (int i = 0; i < count; i++)
            {
                if (fallStarts[i] >= 0f && fallStarts[i] <= t) continue;
                if (pick-- == 0) return i;
            }
            return -1;
        }

        private IEnumerator FightAnimationRoutine(int slot, int campIndex, int campTier, int campSlot, bool victory, int rounds, float playerCasualtyFrac, List<UnitClass> enemyTypes, Action onComplete)
        {
            var figures = slot >= 0 && slot < _marchMarkers.Length ? _squadFigures[slot] : new List<GameObject>();
            var figOrig = new List<Vector3>();
            var figOrigRot = new List<Quaternion>();
            var figOrigScale = new List<Vector3>();
            foreach (var fig in figures)
            {
                figOrig.Add(fig != null ? fig.transform.localPosition : Vector3.zero);
                figOrigRot.Add(fig != null ? fig.transform.localRotation : Quaternion.identity);
                figOrigScale.Add(fig != null ? fig.transform.localScale : Vector3.one);
            }
            var root = slot >= 0 && slot < _marchMarkers.Length ? _marchMarkers[slot] : null;
            if (root != null)
            {
                _squadModes[slot] = SquadModeFighting;
                _squadAnimOwned[slot] = true;
            }

            // ---- Arena swap setup --------------------------------
            // The creatures stand on the camp slot's root (its y
            // is the flattened terrain), so they plant on the
            // ground instead of riding at the march markers'
            // height. The structure's transform is captured so
            // the swap can sink it and put it back exactly.
            Transform campRoot = null;
            GameObject structure = null;
            Vector3 structOrigPos = Vector3.zero;
            Vector3 structOrigScale = Vector3.one;
            if (campSlot >= 0 && campSlot < CampSlotCount && _campSlotMarkers[campSlot] != null && _campSlotMarkers[campSlot].Go != null)
            {
                campRoot = _campSlotMarkers[campSlot].Go.transform;
                structure = _campSlotStructures[campSlot];
                if (structure != null)
                {
                    structOrigPos = structure.transform.localPosition;
                    structOrigScale = structure.transform.localScale;
                }
            }

            var enemies = new List<GameObject>();
            var enemyBase = new List<Vector3>();
            var enemyBaseRot = new List<Quaternion>();
            var enemyBaseScale = new List<Vector3>();
            var enemyLungeDir = new List<Vector3>();
            bool arena = false;
            Vector3 arenaFocus = Vector3.zero;
            if (campRoot != null)
            {
                var anchor = BuildArenaCreature(campTier);
                if (anchor != null)
                {
                    arena = true;
                    // The pack faces the base - the direction
                    // the army marched in from.
                    Vector3 campPos = campRoot.position;
                    var toBase = new Vector3(-campPos.x, 0f, -campPos.z);
                    float faceYaw = toBase.sqrMagnitude > 0.01f
                        ? Mathf.Atan2(toBase.x, toBase.z) * Mathf.Rad2Deg
                        : 0f;
                    var faceDir = new Vector3(Mathf.Sin(faceYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(faceYaw * Mathf.Deg2Rad));
                    var perp = new Vector3(faceDir.z, 0f, -faceDir.x);
                    // Anchor at the centre; two kin behind it,
                    // flanking, at a smaller scale.
                    var spots = new[]
                    {
                        new KeyValuePair<Vector3, float>(Vector3.zero, ArenaAnchorScale),
                        new KeyValuePair<Vector3, float>(-faceDir * 4.5f + perp * 3.8f, ArenaKinScale),
                        new KeyValuePair<Vector3, float>(-faceDir * 4.5f - perp * 3.8f, ArenaKinScale),
                    };
                    for (int i = 0; i < spots.Length; i++)
                    {
                        var creature = i == 0 ? anchor : BuildArenaCreature(campTier);
                        if (creature == null) continue;
                        creature.transform.SetParent(campRoot, false);
                        creature.transform.localPosition = spots[i].Key;
                        creature.transform.localRotation = Quaternion.Euler(0f, faceYaw, 0f);
                        creature.transform.localScale = Vector3.one * spots[i].Value;
                        GroundSnap(creature);
                        enemies.Add(creature);
                        enemyBase.Add(creature.transform.localPosition);
                        enemyBaseRot.Add(creature.transform.localRotation);
                        enemyBaseScale.Add(creature.transform.localScale);
                        enemyLungeDir.Add(faceDir);
                        // Start sunk - the swap phase raises them.
                        var sunk = creature.transform.localPosition;
                        sunk.y -= 4.5f;
                        creature.transform.localPosition = sunk;
                    }
                    arenaFocus = new Vector3(
                        Mathf.Clamp(campPos.x, -PanLimitX, PanLimitX), 0f,
                        Mathf.Clamp(campPos.z, -PanLimitZ, PanLimitZ));
                }
            }

            // ---- Simulated-fight setup (the fallback) ------------
            if (!arena && root != null && enemyTypes != null)
            {
                float groundY = 0f;
                if (figOrig.Count > 0)
                {
                    float sum = 0f;
                    foreach (var p in figOrig) sum += p.y;
                    groundY = sum / figOrig.Count;
                }
                int n = enemyTypes.Count;
                for (int i = 0; i < n; i++)
                {
                    var fig = BuildEnemyFigure(enemyTypes[i]);
                    fig.transform.SetParent(root.transform, false);
                    float angle = i * (Mathf.PI * 2f / Mathf.Max(1, n)) + 0.6f;
                    var pos = new Vector3(Mathf.Sin(angle) * 4.2f, groundY, Mathf.Cos(angle) * 4.2f);
                    enemyBase.Add(pos);
                    enemyBaseRot.Add(Quaternion.Euler(0f, Mathf.Atan2(pos.x, pos.z) * Mathf.Rad2Deg, 0f));
                    enemyBaseScale.Add(Vector3.one);
                    var dir = new Vector3(pos.x, 0f, pos.z);
                    enemyLungeDir.Add(dir.sqrMagnitude > 0.01f ? dir.normalized : new Vector3(0f, 0f, 1f));
                    fig.transform.localPosition = pos + new Vector3(0f, -3f, 0f);
                    fig.transform.localRotation = enemyBaseRot[enemyBaseRot.Count - 1];
                    enemies.Add(fig);
                }
            }

            // Where the army stands for the clash: its current
            // formation normally; on the wider arena ring, grown
            // to arena scale and facing the centre, for the swap.
            var figBase = new List<Vector3>(figOrig);
            var figBaseRot = new List<Quaternion>(figOrigRot);
            var figBaseScale = new List<Vector3>(figOrigScale);
            if (arena)
            {
                for (int f = 0; f < figures.Count; f++)
                {
                    var o = figOrig[f];
                    float r = Mathf.Sqrt(o.x * o.x + o.z * o.z);
                    float angle = r > 0.5f
                        ? Mathf.Atan2(o.x, o.z)
                        : f * (Mathf.PI * 2f / Mathf.Max(1, figures.Count));
                    var target = new Vector3(Mathf.Sin(angle) * ArenaRingRadius, o.y, Mathf.Cos(angle) * ArenaRingRadius);
                    figBase[f] = target;
                    figBaseRot[f] = Quaternion.Euler(0f, Mathf.Atan2(-target.x, -target.z) * Mathf.Rad2Deg, 0f);
                    figBaseScale[f] = figOrigScale[f] * ArenaFigureScale;
                }
            }

            // Face the pack at the army's lead figure, not at the
            // base: the ring keeps each figure's march angle, so
            // the fixed base-facing above can leave the creatures
            // side-on to the soldier they are actually fighting
            // (Patrick, T1 Play check 2026-11-16: "the model was
            // turned to the side during the fight"). The camp
            // slot root carries no rotation, so a local yaw here
            // is a world yaw. Runs before the swap phase, while
            // the creatures are still sunk: positions are restaged
            // and re-sunk, rotations and lunge dirs follow.
            if (arena && campRoot != null && root != null && figures.Count > 0 && enemies.Count > 0)
            {
                Vector3 leadWorld = root.transform.TransformPoint(figBase[0]);
                Vector3 toLead = leadWorld - campRoot.position;
                toLead.y = 0f;
                if (toLead.sqrMagnitude > 0.01f)
                {
                    float leadYaw = Mathf.Atan2(toLead.x, toLead.z) * Mathf.Rad2Deg;
                    var leadDir = new Vector3(Mathf.Sin(leadYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(leadYaw * Mathf.Deg2Rad));
                    var leadPerp = new Vector3(leadDir.z, 0f, -leadDir.x);
                    var faceSpots = new[]
                    {
                        new KeyValuePair<Vector3, float>(Vector3.zero, ArenaAnchorScale),
                        new KeyValuePair<Vector3, float>(-leadDir * 4.5f + leadPerp * 3.8f, ArenaKinScale),
                        new KeyValuePair<Vector3, float>(-leadDir * 4.5f - leadPerp * 3.8f, ArenaKinScale),
                    };
                    for (int i = 0; i < enemies.Count && i < faceSpots.Length; i++)
                    {
                        var creature = enemies[i];
                        if (creature == null) continue;
                        var spotPos = faceSpots[i].Key;
                        spotPos.y = enemyBase[i].y;
                        enemyBase[i] = spotPos;
                        enemyBaseRot[i] = Quaternion.Euler(0f, leadYaw, 0f);
                        enemyLungeDir[i] = leadDir;
                        creature.transform.localRotation = enemyBaseRot[i];
                        spotPos.y -= 4.5f;
                        creature.transform.localPosition = spotPos;
                    }
                }
            }

            // Who falls, and when: falls spread across the clash.
            // The army's count follows the real casualty fraction
            // (a victory always leaves at least one figure standing,
            // as the real fight leaves a survivor); on victory every
            // enemy falls, on defeat none do.
            float clashTime = Mathf.Clamp(0.45f * Mathf.Max(1, rounds), 1.6f, 3.2f);
            int playerFalls = Mathf.RoundToInt(figures.Count * Mathf.Clamp01(playerCasualtyFrac));
            if (victory && figures.Count > 0) playerFalls = Mathf.Min(playerFalls, figures.Count - 1);
            int enemyFalls = victory ? enemies.Count : 0;
            var pFallStart = new float[figures.Count];
            for (int i = 0; i < pFallStart.Length; i++) pFallStart[i] = -1f;
            var playerOrder = ShuffledIndices(figures.Count);
            for (int k = 0; k < playerFalls && k < playerOrder.Count; k++)
                pFallStart[playerOrder[k]] = clashTime * (k + 0.5f) / Mathf.Max(1, playerFalls);
            var eFallStart = new float[enemies.Count];
            for (int i = 0; i < eFallStart.Length; i++) eFallStart[i] = -1f;
            var enemyOrder = ShuffledIndices(enemies.Count);
            for (int k = 0; k < enemyFalls && k < enemyOrder.Count; k++)
                eFallStart[enemyOrder[k]] = clashTime * (k + 0.5f) / Mathf.Max(1, enemyFalls);

            // Camera bookkeeping. The arena swap owns the camera;
            // since the x3 transplant the simulated fight (tiers
            // with no arena creature, e.g. T3) takes it too - at
            // map distances its 4.2-radius scuffle read as a few
            // pixels near the camp (Patrick, 2026-11-20: "the
            // fight was kind of small").
            Vector3 savedPan = _panOffset;
            float savedDist = _camDist;
            float savedTarget = _camDistTarget;
            Vector3 fightFocus = arenaFocus;
            if (!arena && root != null)
            {
                var rp = root.transform.position;
                fightFocus = new Vector3(
                    Mathf.Clamp(rp.x, -PanLimitX, PanLimitX), 0f,
                    Mathf.Clamp(rp.z, -PanLimitZ, PanLimitZ));
            }
            bool cameraOwned = arena || root != null;
            if (cameraOwned)
            {
                _gliding = false;
                _arenaCameraActive = true;
            }

            // Phase 0 - the swap (arena) or the face-off (simulated).
            if (arena)
            {
                float swapTime = 0.85f;
                float t = 0f;
                Vector3 panFrom = _panOffset;
                float distFrom = _camDist;
                while (t < swapTime)
                {
                    while (!_visible) yield return null;
                    t += Time.deltaTime;
                    float p = Mathf.Clamp01(t / swapTime);
                    float e = p * p * (3f - 2f * p);
                    if (structure != null)
                    {
                        var sp = structOrigPos;
                        sp.y -= 3.2f * e;
                        structure.transform.localPosition = sp;
                    }
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        var c = enemies[i];
                        if (c == null) continue;
                        var pos = enemyBase[i];
                        pos.y -= 4.5f * (1f - e);
                        c.transform.localPosition = pos;
                    }
                    for (int f = 0; f < figures.Count; f++)
                    {
                        var fig = figures[f];
                        if (fig == null) continue;
                        fig.transform.localPosition = Vector3.Lerp(figOrig[f], figBase[f], e);
                        fig.transform.localRotation = Quaternion.Slerp(figOrigRot[f], figBaseRot[f], e);
                        fig.transform.localScale = Vector3.Lerp(figOrigScale[f], figBaseScale[f], e);
                    }
                    _panOffset = Vector3.Lerp(panFrom, arenaFocus, e);
                    _camDist = Mathf.Lerp(distFrom, ArenaCamDist, e);
                    _camDistTarget = _camDist;
                    ApplyCamera();
                    yield return null;
                }
                if (structure != null) structure.SetActive(false);
            }
            else
            {
                // Face-off (~0.6s): the enemy figures rise while
                // the camera glides in (simulated fights only -
                // the arena swap does its own glide above).
                float faceTime = 0.6f;
                float t = 0f;
                Vector3 panFrom = _panOffset;
                float distFrom = _camDist;
                while (t < faceTime)
                {
                    while (!_visible) yield return null;
                    t += Time.deltaTime;
                    float p = Mathf.Clamp01(t / faceTime);
                    float rise = p * p * (3f - 2f * p);
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        var fig = enemies[i];
                        if (fig == null) continue;
                        var pos = enemyBase[i];
                        pos.y = enemyBase[i].y - 3f * (1f - rise);
                        fig.transform.localPosition = pos;
                    }
                    if (cameraOwned)
                    {
                        _panOffset = Vector3.Lerp(panFrom, fightFocus, rise);
                        _camDist = Mathf.Lerp(distFrom, ArenaCamDist, rise);
                        _camDistTarget = _camDist;
                        ApplyCamera();
                    }
                    yield return null;
                }
            }

            // Phase 1 - clash: lunge pulses, hit flashes, falls.
            float ct = 0f;
            int pulse = -1;
            float pulseStart = 0f;
            int flashPlayer = -1;
            int flashEnemy = -1;
            while (ct < clashTime)
            {
                while (!_visible) yield return null;
                ct += Time.deltaTime;
                int curPulse = Mathf.FloorToInt(ct / 0.45f);
                if (curPulse != pulse)
                {
                    pulse = curPulse;
                    pulseStart = ct;
                    flashPlayer = PickStanding(figures.Count, pFallStart, ct);
                    flashEnemy = PickStanding(enemies.Count, eFallStart, ct);
                }
                float lunge = Mathf.Sin(Mathf.Clamp01((ct % 0.45f) / 0.30f) * Mathf.PI);
                float flashP = Mathf.Clamp01(1f - (ct - pulseStart) / 0.18f);
                for (int f = 0; f < figures.Count; f++)
                {
                    var fig = figures[f];
                    if (fig == null) continue;
                    float fp = FallProgress(pFallStart[f], ct);
                    var basePos = figBase[f];
                    Vector3 pos = basePos;
                    Quaternion rot = figBaseRot[f];
                    if (fp > 0f)
                    {
                        pos.y = basePos.y - 0.6f * fp;
                        rot = figBaseRot[f] * Quaternion.Euler(80f * fp, 0f, 0f);
                    }
                    else
                    {
                        var dir = new Vector3(-basePos.x, 0f, -basePos.z);
                        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : new Vector3(0f, 0f, -1f);
                        pos += dir * (0.85f * lunge);
                        pos.x += UnityEngine.Random.Range(-0.12f, 0.12f);
                        pos.z += UnityEngine.Random.Range(-0.12f, 0.12f);
                    }
                    fig.transform.localPosition = pos;
                    fig.transform.localRotation = rot;
                    fig.transform.localScale = figBaseScale[f] * (f == flashPlayer ? 1f + 0.3f * flashP : 1f);
                }
                for (int i = 0; i < enemies.Count; i++)
                {
                    var fig = enemies[i];
                    if (fig == null) continue;
                    float fp = FallProgress(eFallStart[i], ct);
                    var basePos = enemyBase[i];
                    Vector3 pos = basePos;
                    Quaternion rot = enemyBaseRot[i];
                    if (fp > 0f)
                    {
                        pos.y = basePos.y - 0.6f * fp;
                        rot = enemyBaseRot[i] * Quaternion.Euler(80f * fp, 0f, 0f);
                    }
                    else
                    {
                        pos += enemyLungeDir[i] * (0.85f * lunge);
                        pos.x += UnityEngine.Random.Range(-0.12f, 0.12f);
                        pos.z += UnityEngine.Random.Range(-0.12f, 0.12f);
                    }
                    fig.transform.localPosition = pos;
                    fig.transform.localRotation = rot;
                    fig.transform.localScale = enemyBaseScale[i] * (i == flashEnemy ? 1f + 0.3f * flashP : 1f);
                }
                yield return null;
            }

            // Phase 2 - outcome (~1.1s).
            float outcomeTime = 1.1f;
            float ot = 0f;
            while (ot < outcomeTime)
            {
                while (!_visible) yield return null;
                ot += Time.deltaTime;
                float p = Mathf.Clamp01(ot / outcomeTime);
                for (int f = 0; f < figures.Count; f++)
                {
                    var fig = figures[f];
                    if (fig == null) continue;
                    float fp = FallProgress(pFallStart[f], clashTime);
                    var pos = figBase[f];
                    if (fp > 0f)
                    {
                        pos.y = figBase[f].y - 0.6f * fp;
                        fig.transform.localPosition = pos;
                        fig.transform.localRotation = figBaseRot[f] * Quaternion.Euler(80f * fp, 0f, 0f);
                    }
                    else if (victory)
                    {
                        pos.y = figBase[f].y + Mathf.Sin(p * Mathf.PI) * 1.1f;
                        fig.transform.localPosition = pos;
                        fig.transform.localRotation = figBaseRot[f];
                    }
                    else
                    {
                        pos.x += UnityEngine.Random.Range(-0.5f, 0.5f);
                        pos.z += UnityEngine.Random.Range(-0.5f, 0.5f);
                        pos.y = figBase[f].y - 0.6f * p;
                        fig.transform.localPosition = pos;
                        fig.transform.localRotation = figBaseRot[f];
                    }
                    fig.transform.localScale = figBaseScale[f];
                }
                for (int i = 0; i < enemies.Count; i++)
                {
                    var fig = enemies[i];
                    if (fig == null) continue;
                    var pos = enemyBase[i];
                    if (victory)
                    {
                        float fp = Mathf.Max(FallProgress(eFallStart[i], clashTime), p);
                        pos.y = enemyBase[i].y - 0.6f * fp - 1.4f * p;
                        fig.transform.localPosition = pos;
                        fig.transform.localRotation = enemyBaseRot[i] * Quaternion.Euler(80f * fp, 0f, 0f);
                    }
                    else
                    {
                        pos.y = enemyBase[i].y + Mathf.Sin(p * Mathf.PI) * 1.2f;
                        fig.transform.localPosition = pos;
                        fig.transform.localRotation = enemyBaseRot[i];
                    }
                    fig.transform.localScale = enemyBaseScale[i];
                }
                yield return null;
            }

            // Phase 3 - swap back (arena only): the army shrinks
            // to its march formation, the camera pulls back, and
            // on defeat the creatures burrow away while the camp
            // structure rises back into place.
            if (arena)
            {
                float backTime = 0.6f;
                float t = 0f;
                Vector3 panFrom = _panOffset;
                float distFrom = _camDist;
                if (!victory && structure != null)
                {
                    var sp = structOrigPos;
                    sp.y -= 3.2f;
                    structure.transform.localPosition = sp;
                    structure.SetActive(true);
                }
                while (t < backTime)
                {
                    while (!_visible) yield return null;
                    t += Time.deltaTime;
                    float p = Mathf.Clamp01(t / backTime);
                    float e = p * p * (3f - 2f * p);
                    if (!victory)
                    {
                        if (structure != null)
                        {
                            var sp = structOrigPos;
                            sp.y -= 3.2f * (1f - e);
                            structure.transform.localPosition = sp;
                        }
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            var c = enemies[i];
                            if (c == null) continue;
                            var pos = enemyBase[i];
                            pos.y -= 3.5f * e;
                            c.transform.localPosition = pos;
                        }
                    }
                    for (int f = 0; f < figures.Count; f++)
                    {
                        var fig = figures[f];
                        if (fig == null) continue;
                        fig.transform.localPosition = Vector3.Lerp(figBase[f], figOrig[f], e);
                        fig.transform.localRotation = Quaternion.Slerp(figBaseRot[f], figOrigRot[f], e);
                        fig.transform.localScale = Vector3.Lerp(figBaseScale[f], figOrigScale[f], e);
                    }
                    _panOffset = Vector3.Lerp(panFrom, savedPan, e);
                    _camDist = Mathf.Lerp(distFrom, savedDist, e);
                    _camDistTarget = _camDist;
                    ApplyCamera();
                    yield return null;
                }
                _camDistTarget = savedTarget;
                _arenaCameraActive = false;
            }
            else if (cameraOwned)
            {
                // Simulated fight: glide the camera back to the
                // pre-fight pose (the figures restore below).
                float backTime = 0.6f;
                float t = 0f;
                Vector3 panFrom = _panOffset;
                float distFrom = _camDist;
                while (t < backTime)
                {
                    while (!_visible) yield return null;
                    t += Time.deltaTime;
                    float p = Mathf.Clamp01(t / backTime);
                    float e = p * p * (3f - 2f * p);
                    _panOffset = Vector3.Lerp(panFrom, savedPan, e);
                    _camDist = Mathf.Lerp(distFrom, savedDist, e);
                    _camDistTarget = _camDist;
                    ApplyCamera();
                    yield return null;
                }
                _camDistTarget = savedTarget;
                _arenaCameraActive = false;
            }

            // Put the camp structure back exactly as it was. On
            // victory the service vanishes the slot inside
            // onComplete - the same call stack, before the next
            // frame renders - so the restored structure is never
            // seen; on defeat it is simply the camp again.
            if (structure != null)
            {
                structure.transform.localPosition = structOrigPos;
                structure.transform.localScale = structOrigScale;
                if (!structure.activeSelf) structure.SetActive(true);
            }

            // Restore the army figures, remove the enemies, finish.
            for (int f = 0; f < figures.Count; f++)
            {
                var fig = figures[f];
                if (fig == null) continue;
                fig.transform.localPosition = figOrig[f];
                fig.transform.localRotation = figOrigRot[f];
                fig.transform.localScale = figOrigScale[f];
            }
            foreach (var fig in enemies) if (fig != null) Destroy(fig);
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
            _markerWanted[slot] = true;
            _marchMarkers[slot].SetActive(_visible);
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
        // true. Hiding also hides it from taps. The lair (index 21)
        // and its robot both carry Data.Index == 21, so one pass hides
        // the whole lair.
        public void SetBossVisible(bool visible)
        {
            foreach (var point in _mapPoints)
            {
                if (point.Data == null || point.Data.Index != 21) continue;
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
        private BottomNavView _bottomNav;
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
        private readonly bool[] _nodeOccupied = new bool[18];
        private readonly bool[] _nodeHidden = new bool[18];

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
                    for (int i = 0; i < NodeDefs.Length; i++)
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
                    _searchMatches.Add(NodeDefs[21].Position);
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

        // ---- Alien Twilight ambience (2026-11-15) ----

        private void BuildAmbience()
        {
            _ambienceRoot = new GameObject("ExteriorAmbience");
            _ambienceRoot.SetActive(false);

            // Sky dome: a code-built sphere (radius 4050 — x3 with
            // the transplant world, was 1350, inside
            // the 3000 far plane) with its triangles wound so the
            // inside faces the camera, textured with a runtime
            // gradient — bright azure day sky since Round 3
            // Phase 4 (was the Alien Twilight gradient).
            var dome = new GameObject("TwilightSky");
            dome.transform.SetParent(_ambienceRoot.transform, false);
            dome.AddComponent<MeshFilter>().sharedMesh = BuildSkyDomeMesh(4050f, 32, 16);
            var domeRenderer = dome.AddComponent<MeshRenderer>();
            var skyShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (skyShader != null)
            {
                var skyMat = new Material(skyShader);
                skyMat.SetFloat("_Cull", 0f); // seen from inside; winding-proof
                skyMat.SetTexture("_BaseMap", MakeSkyTexture());
                skyMat.SetColor("_BaseColor", Color.white);
                domeRenderer.sharedMaterial = skyMat;
            }
            domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;

            BuildRingedPlanet(_ambienceRoot.transform);
            BuildMotes(_ambienceRoot.transform);
            BuildMapPostProcessing();
        }

        /// <summary>Bright-day post grade (Round 3 Phase 4): a
        /// global Volume that lives only while the Map is open —
        /// ApplyAmbience enables it, ReleaseAmbience disables it,
        /// so the base and lair keep their own looks. Priority 15
        /// sits between BaseView's always-on Bloom volume (10)
        /// and the lair's (20). ACES for the filmic highlight
        /// roll-off, a whisper of exposure / contrast /
        /// saturation, and gentler bloom than the lair's: the
        /// daylight scene is already bright, this just polishes
        /// it. Tune the numbers here, one line each.</summary>
        private void BuildMapPostProcessing()
        {
            if (_mapPostVolume != null) return;
            var postGo = new GameObject("Map Post Volume");
            _mapPostVolume = postGo.AddComponent<UnityEngine.Rendering.Volume>();
            _mapPostVolume.isGlobal = true;
            _mapPostVolume.priority = 15f;
            _mapPostVolume.enabled = false;
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();

            var tonemapping = profile.Add<UnityEngine.Rendering.Universal.Tonemapping>();
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = UnityEngine.Rendering.Universal.TonemappingMode.ACES;

            var bloom = profile.Add<UnityEngine.Rendering.Universal.Bloom>();
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.85f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.35f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.6f;

            var color = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
            color.active = true;
            color.postExposure.overrideState = true;
            color.postExposure.value = 0.05f;
            color.contrast.overrideState = true;
            color.contrast.value = 6f;
            color.saturation.overrideState = true;
            color.saturation.value = 10f;

            _mapPostVolume.profile = profile;
        }

        private static Mesh BuildSkyDomeMesh(float radius, int segments, int rings)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float v = (float)j / rings;                 // 0 = bottom pole, 1 = top pole
                float phi = (v - 0.5f) * Mathf.PI;           // latitude -90..+90 degrees
                float y = radius * Mathf.Sin(phi);
                float ringR = radius * Mathf.Cos(phi);
                for (int i = 0; i <= segments; i++)
                {
                    float u = (float)i / segments;
                    float a = u * 2f * Mathf.PI;
                    verts.Add(new Vector3(ringR * Mathf.Cos(a), y, ringR * Mathf.Sin(a)));
                    uvs.Add(new Vector2(u, v));
                }
            }
            int row = segments + 1;
            for (int j = 0; j < rings; j++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a0 = j * row + i, b0 = a0 + 1, c0 = a0 + row, d0 = c0 + 1;
                    // Reversed winding: the dome is seen from inside.
                    tris.Add(a0); tris.Add(d0); tris.Add(c0);
                    tris.Add(a0); tris.Add(b0); tris.Add(d0);
                }
            }
            var mesh = new Mesh { name = "TwilightSkyDome" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Texture2D MakeSkyTexture()
        {
            // BRIGHT DAY (Round 3 Phase 4, replacing the Alien
            // Twilight gradient). v: 0 bottom, 0.5 the horizon, 1
            // the zenith. The camera looks down 21-53 deg
            // depending on zoom, so the framed band runs from
            // below the horizon up to mid sky: pale haze below
            // the horizon line (it reads as hazy distance past
            // the terrain edge), a warm-white glare band at the
            // horizon, azure above, deepening to the zenith.
            // A sun glow sits at u = 0.625 — the camera's fixed
            // view azimuth (toward the NW corner, where the
            // ringed planet hangs) — and soft cloud bands drift
            // through the mid sky. No stars: it's daytime.
            // SKY FIX (2026-11-16, Patrick's Phase 4 note): the
            // glare band ran 0.44-0.64, so at close zoom the
            // eye-level sky read pale hazy white instead of
            // azure. The white is now a thin line at the horizon
            // itself (full at 0.505, held to 0.51) and the sun
            // glow falls off faster with height. FOLLOW-UP the
            // same day, from Patrick's check video: at close
            // zoom the frame tops out around v 0.55, so azure
            // arriving at 0.62 still left the framed band in
            // the pale transition - azure is now reached by
            // 0.56 and the haze is deeper blue.
            var tex = new Texture2D(1024, 512, TextureFormat.RGBA32, false);
            var haze = new Color(0.55f, 0.70f, 0.88f);
            var horizon = new Color(0.99f, 0.98f, 0.93f);
            var mid = new Color(0.36f, 0.63f, 0.95f);
            var zenith = new Color(0.14f, 0.42f, 0.86f);
            var sunCore = new Color(1.0f, 0.99f, 0.94f);
            for (int y = 0; y < 512; y++)
            {
                float v = y / 511f;
                Color c;
                if (v < 0.46f) c = haze;
                else if (v < 0.51f) c = Color.Lerp(haze, horizon, TerrainSmoothstep(0.46f, 0.505f, v));
                else if (v < 0.72f) c = Color.Lerp(horizon, mid, TerrainSmoothstep(0.51f, 0.56f, v));
                else c = Color.Lerp(mid, zenith, TerrainSmoothstep(0.72f, 0.95f, v));
                for (int x = 0; x < 1024; x++)
                {
                    float u = x / 1023f;
                    Color px = c;
                    // Sun glow: wrapped angular distance from
                    // the sun spot (u 0.625, v 0.50).
                    float du = Mathf.Abs(u - 0.625f);
                    if (du > 0.5f) du = 1f - du;
                    float dv = (v - 0.50f) * 2.6f;
                    float sd = Mathf.Sqrt(du * du * 4f + dv * dv);
                    float glow = Mathf.Clamp01(1f - sd / 0.55f);
                    px = Color.Lerp(px, sunCore, glow * glow * 0.85f);
                    // Cloud bands: two slow sine fields, only
                    // in the mid sky, whitening toward white.
                    if (v > 0.52f && v < 0.92f)
                    {
                        float cl = Mathf.Sin(u * 37.7f + 2.1f * Mathf.Sin(v * 21.0f))
                                 * Mathf.Sin(v * 34.0f + 1.3f);
                        float cloud = TerrainSmoothstep(0.55f, 0.95f, cl)
                                    * TerrainSmoothstep(0.52f, 0.60f, v)
                                    * (1f - TerrainSmoothstep(0.82f, 0.92f, v));
                        px = Color.Lerp(px, Color.white, cloud * 0.28f);
                    }
                    tex.SetPixel(x, y, px);
                }
            }
            tex.Apply();
            return tex;
        }

        private void BuildRingedPlanet(Transform parent)
        {
            var group = new GameObject("RingedPlanet");
            group.transform.SetParent(parent, false);
            // Low on the north-western horizon: the down-looking
            // camera only ever frames the dome band below the
            // geometric horizon, so the planet sits past the map's
            // NW corner (partly occluded by the terrain edge when
            // panned fully NW) instead of up in the unseen sky.
            group.transform.position = new Vector3(-2640f, -630f, -2460f);
            group.transform.LookAt(new Vector3(0f, 30f, 0f));
            group.transform.Rotate(0f, 0f, -14f, Space.Self);

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) return;

            // Disc with gas-giant bands.
            var disc = new GameObject("PlanetDisc");
            disc.transform.SetParent(group.transform, false);
            disc.AddComponent<MeshFilter>().sharedMesh = BuildDiscMesh(285f, 40);
            var discRenderer = disc.AddComponent<MeshRenderer>();
            var discMat = new Material(unlit);
            discMat.SetFloat("_Cull", 0f); // double-sided: visible however the group tilts
            discMat.SetTexture("_BaseMap", MakePlanetTexture());
            // Bright day (Phase 4): the disc goes translucent
            // like its ring, so the planet reads as a pale
            // daytime sky object instead of a solid twilight
            // billboard. The texture's limb falloff already
            // fades alpha toward the rim.
            discMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.55f));
            discMat.SetFloat("_Surface", 1f);
            discMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            discMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            discMat.SetInt("_ZWrite", 0);
            discMat.renderQueue = 3000;
            discRenderer.sharedMaterial = discMat;
            discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            discRenderer.receiveShadows = false;

            // Ring: a flat translucent annulus in the same plane.
            var ring = new GameObject("PlanetRing");
            ring.transform.SetParent(group.transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh = BuildRingMesh(384f, 624f, 48);
            var ringRenderer = ring.AddComponent<MeshRenderer>();
            var ringMat = new Material(unlit);
            ringMat.SetFloat("_Cull", 0f);
            ringMat.SetColor("_BaseColor", new Color(0.78f, 0.88f, 0.92f, 0.35f));
            ringMat.SetFloat("_Surface", 1f);
            ringMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ringMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ringMat.SetInt("_ZWrite", 0);
            ringMat.renderQueue = 3000;
            ringRenderer.sharedMaterial = ringMat;
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
        }

        private static Mesh BuildDiscMesh(float radius, int segments)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * 2f * Mathf.PI;
                float cx = Mathf.Cos(a), sy = Mathf.Sin(a);
                verts.Add(new Vector3(radius * cx, radius * sy, 0f));
                uvs.Add(new Vector2(0.5f + 0.5f * cx, 0.5f + 0.5f * sy));
            }
            for (int i = 1; i <= segments; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            var mesh = new Mesh { name = "PlanetDisc" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh BuildRingMesh(float inner, float outer, int segments)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = (float)i / segments * 2f * Mathf.PI;
                float cx = Mathf.Cos(a), sy = Mathf.Sin(a);
                verts.Add(new Vector3(inner * cx, inner * sy, 0f));
                verts.Add(new Vector3(outer * cx, outer * sy, 0f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a0 = i * 2, b0 = a0 + 1, c0 = a0 + 2, d0 = a0 + 3;
                tris.Add(a0); tris.Add(b0); tris.Add(c0);
                tris.Add(b0); tris.Add(d0); tris.Add(c0);
            }
            var mesh = new Mesh { name = "PlanetRing" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Texture2D MakePlanetTexture()
        {
            // Horizontal gas-giant bands (v runs across the disc),
            // with a soft darker limb toward the rim.
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var bands = new[]
            {
                new Color(0.92f, 0.78f, 0.62f), new Color(0.85f, 0.60f, 0.42f),
                new Color(0.95f, 0.85f, 0.70f), new Color(0.78f, 0.52f, 0.36f),
                new Color(0.90f, 0.72f, 0.55f), new Color(0.84f, 0.62f, 0.45f),
                new Color(0.93f, 0.80f, 0.64f),
            };
            for (int y = 0; y < 256; y++)
            {
                float v = y / 255f;
                float f = v * (bands.Length - 1);
                int bi = Mathf.Min((int)f, bands.Length - 2);
                Color c = Color.Lerp(bands[bi], bands[bi + 1], f - bi);
                for (int x = 0; x < 256; x++)
                {
                    float dx = (x - 127.5f) / 128f, dy = (y - 127.5f) / 128f;
                    float limb = Mathf.Clamp01(1.15f - 0.45f * (dx * dx + dy * dy));
                    tex.SetPixel(x, y, c * limb);
                }
            }
            tex.Apply();
            return tex;
        }

        private void BuildMotes(Transform parent)
        {
            // Mirrors LavaTubeAmbience.BuildDust, scaled up for
            // the open field: slow spore motes drifting around
            // the player's current view (Update re-centres the
            // system on the pan offset).
            var go = new GameObject("SporeMotes");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 16f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startColor = new Color(0.75f, 0.95f, 0.90f, 0.22f);
            main.maxParticles = 240;
            var emission = ps.emission;
            emission.rateOverTime = 14f; // Phase 4: subtler in daylight — drifting pollen, not spores
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(760f, 40f, 760f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null && renderer != null)
            {
                var mat = new Material(shader);
                mat.SetTexture("_BaseMap", MakeDotTexture());
                mat.SetColor("_BaseColor", new Color(0.75f, 0.95f, 0.90f, 0.38f));
                mat.SetFloat("_Surface", 1f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                renderer.material = mat;
            }
            _motes = go.transform;
        }

        private static Texture2D MakeDotTexture()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            tex.Apply();
            return tex;
        }

        private void ApplyAmbience()
        {
            if (_ambApplied) return;
            _ambPrevMode = RenderSettings.ambientMode;
            _ambPrevSky = RenderSettings.ambientSkyColor;
            _ambPrevEquator = RenderSettings.ambientEquatorColor;
            _ambPrevGround = RenderSettings.ambientGroundColor;
            _ambPrevFog = RenderSettings.fog;
            _ambPrevFogColor = RenderSettings.fogColor;
            _ambPrevFogMode = RenderSettings.fogMode;
            _ambPrevFogDensity = RenderSettings.fogDensity;
            _ambPrevFogNear = RenderSettings.fogStartDistance;
            _ambPrevFogFar = RenderSettings.fogEndDistance;
            if (_ambSun == null) { var go = GameObject.Find("Directional Light"); if (go != null) _ambSun = go.GetComponent<Light>(); }
            if (_ambFill == null) { var go = GameObject.Find("Fill Light"); if (go != null) _ambFill = go.GetComponent<Light>(); }
            if (_ambSun != null) { _ambPrevSunColor = _ambSun.color; _ambPrevSunIntensity = _ambSun.intensity; }
            if (_ambFill != null) { _ambPrevFillColor = _ambFill.color; _ambPrevFillIntensity = _ambFill.intensity; }
            var cam = Camera.main;
            if (cam != null) _ambPrevCamBg = cam.backgroundColor;
            _ambApplied = true;

            // BRIGHT DAY values (Round 3 Phase 4; these were
            // the Alien Twilight set before): bright blue bounce
            // from the sky, pale blue distance haze, a white
            // midday sun, and a cooler, weaker fill so the sun
            // does the modelling.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.68f, 0.85f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.52f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.20f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.62f, 0.76f, 0.88f);
            RenderSettings.fogStartDistance = 2100f;
            RenderSettings.fogEndDistance = 7800f;
            if (_ambSun != null) { _ambSun.color = new Color(1.0f, 0.98f, 0.94f); _ambSun.intensity = 1.6f; }
            if (_ambFill != null) { _ambFill.color = new Color(0.50f, 0.62f, 0.80f); _ambFill.intensity = 0.35f; }
            if (cam != null) cam.backgroundColor = new Color(0.35f, 0.58f, 0.85f);
            if (_mapPostVolume != null) _mapPostVolume.enabled = true;
        }

        private void ReleaseAmbience()
        {
            if (!_ambApplied) return;
            _ambApplied = false;
            RenderSettings.ambientMode = _ambPrevMode;
            RenderSettings.ambientSkyColor = _ambPrevSky;
            RenderSettings.ambientEquatorColor = _ambPrevEquator;
            RenderSettings.ambientGroundColor = _ambPrevGround;
            RenderSettings.fog = _ambPrevFog;
            RenderSettings.fogColor = _ambPrevFogColor;
            RenderSettings.fogMode = _ambPrevFogMode;
            RenderSettings.fogDensity = _ambPrevFogDensity;
            RenderSettings.fogStartDistance = _ambPrevFogNear;
            RenderSettings.fogEndDistance = _ambPrevFogFar;
            if (_ambSun != null) { _ambSun.color = _ambPrevSunColor; _ambSun.intensity = _ambPrevSunIntensity; }
            if (_ambFill != null) { _ambFill.color = _ambPrevFillColor; _ambFill.intensity = _ambPrevFillIntensity; }
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = _ambPrevCamBg;
            if (_mapPostVolume != null) _mapPostVolume.enabled = false;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_terrain != null) _terrain.SetActive(visible);
            if (_ambienceRoot != null) _ambienceRoot.SetActive(visible);
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
            // Marches are NOT stopped by tab switches anymore
            // (2026-11-15): the markers and route dots simply hide
            // with the Map and come back with it, while the march
            // itself keeps running in the background.
            SetMarkersVisible(visible);
            if (!visible) ReleaseAmbience();
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
            if (_topBarGo != null) _topBarGo.SetActive(!visible);
            // The BottomNav routes through its own view (2026-11-18):
            // the bar is player-toggled now, so the Map only
            // SUPPRESSES it (and its MENU button) while up — the
            // player's open/closed choice survives the round trip.
            if (_bottomNav == null)
            {
                var navHost = GameObject.Find("BottomNavView");
                if (navHost != null) _bottomNav = navHost.GetComponent<BottomNavView>();
            }
            if (_bottomNav != null) _bottomNav.SetSuppressed(visible);
            if (visible)
            {
                // Opening the Map always recentres on the base — the
                // pan starts from zero each time.
                _panOffset = Vector3.zero;
                _pointerDown = false;
                _isDrag = false;
                ApplyCamera();
                ApplyAmbience();
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
