using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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
    /// A large sculpted terrain (5x the base: 280x240) with a sci-fi
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

        // True while the Map tab is the active view.
        public bool IsVisible => _visible;

        // Exterior camera: 5x the base camera distance, same pitch.
        // Base camera (10,16,10) frames the 56x48 ground; scaling
        // both by 5 frames the 280x240 map identically.
        private static readonly Vector3 BaseCameraPosition = new Vector3(10f, 16f, 10f);
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
        private readonly GameObject[] _marchMarkers = new GameObject[2];
        private readonly bool[] _marching = new bool[2];
        private readonly Vector3[] _marchTargets = new Vector3[2];
        private readonly Action[] _marchCallbacks = new Action[2];
        // Slow, deliberate march (2026-11-09, Patrick): 10 u/s, so a
        // far node takes ~13s to reach instead of ~5s at the old 25.
        private static readonly float MarchSpeed = 10f; // units/second

        // Map camera pan (Patrick 2026-11-09): the Map view is 5x the
        // base, so the camera can be panned across it — arrow keys on
        // PC, touch/mouse drag on the phone (and in the Editor). The
        // pan offset moves both the camera position and its look-at
        // point, so the 48.5-degree pitch never changes. Clamped so the
        // view stays on the 280x240 map.
        private Vector3 _panOffset = Vector3.zero;
        private static readonly float PanSpeed = 40f; // units/second, arrow keys
        private static readonly float PanLimitX = 100f;
        private static readonly float PanLimitZ = 90f;
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
        // coords on the 280x240 map (x ±140, z ±120), all outside the
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
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 500, Title = "Biomass Node", Info = "Biomass: 500", Position = new Vector3(35f, 0f, 25f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 1000, Title = "Biomass Node", Info = "Biomass: 1000", Position = new Vector3(-55f, 0f, 45f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Kind = MapPointKind.Biomass, Amount = 2000, Title = "Biomass Node", Info = "Biomass: 2000", Position = new Vector3(95f, 0f, -55f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 500, Title = "Alloy Node", Info = "Alloy: 500", Position = new Vector3(-30f, 0f, -35f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 1000, Title = "Alloy Node", Info = "Alloy: 1000", Position = new Vector3(65f, 0f, 55f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Kind = MapPointKind.Alloy, Amount = 2000, Title = "Alloy Node", Info = "Alloy: 2000", Position = new Vector3(-105f, 0f, -65f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 500, Title = "Helium-3 Node", Info = "Helium-3: 500", Position = new Vector3(45f, 0f, -40f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 1000, Title = "Helium-3 Node", Info = "Helium-3: 1000", Position = new Vector3(-70f, 0f, -20f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Kind = MapPointKind.Helium3, Amount = 2000, Title = "Helium-3 Node", Info = "Helium-3: 2000", Position = new Vector3(110f, 0f, 75f) },
            new NodeDef { ResourcePath = "Models/EnemyCamp", Kind = MapPointKind.Camp, Tier = 1, Title = "Spore Crawler Camp", Info = "Tier 1 — Spore Crawlers\nExpedition enemy", Position = new Vector3(55f, 0f, 15f) },
            new NodeDef { ResourcePath = "Models/EnemyCamp", Kind = MapPointKind.Camp, Tier = 2, Title = "Void Stalker Camp", Info = "Tier 2 — Void Stalkers\nExpedition enemy", Position = new Vector3(-85f, 0f, 60f) },
            new NodeDef { ResourcePath = "Models/EnemyCamp", Kind = MapPointKind.Camp, Tier = 3, Title = "Brood Tyrant Camp", Info = "Tier 3 — Brood Tyrant\nExpedition enemy", Position = new Vector3(120f, 0f, -80f) },
            // Boss lair (2026-11-09): the War Robot lives here, far
            // southwest corner of the map. Index 12. The lair does not
            // move on respawn (unlike camps) — it is a fixed place.
            new NodeDef { ResourcePath = "Models/BossLair", Kind = MapPointKind.BossLair, Title = "War Robot Lair", Info = "BOSS — War Robot\nGiant Mechanized enemy\nUnlocks at Nexus 10", Position = new Vector3(-120f, 0f, -100f) },
        };

        // Enemy camp field (2026-11-10, replaces the 3 fixed camps and
        // their respawn spots): 15 camp slots spread across the map in
        // rings — near, mid, and far corners — all clear of the base,
        // the resource nodes, the boss lair, and each other. A slot's
        // camp tier is dynamic (rolled per spawn by MapExcursionService,
        // weighted by Nexus level), so a slot shows the EnemyCamp
        // marker plus the creature group for its CURRENT tier, built
        // on demand. Slot points carry Index = CampSlotIndexBase + slot
        // so they never collide with node (0-8) or lair (12) indices.
        public const int CampSlotCount = 15;
        public const int CampSlotIndexBase = 100;
        private static readonly Vector3[] CampSlotPositions =
        {
            new Vector3(55f, 0f, 15f), new Vector3(-40f, 0f, 80f), new Vector3(100f, 0f, 40f),
            new Vector3(-85f, 0f, 60f), new Vector3(20f, 0f, -90f), new Vector3(-120f, 0f, -10f),
            new Vector3(120f, 0f, -80f), new Vector3(0f, 0f, 100f), new Vector3(60f, 0f, -100f),
            new Vector3(85f, 0f, 10f), new Vector3(-95f, 0f, 15f), new Vector3(15f, 0f, 60f),
            new Vector3(-15f, 0f, -95f), new Vector3(125f, 0f, 30f), new Vector3(-60f, 0f, -85f),
        };
        // What each slot currently shows: the tier built (0 = empty)
        // and whether it should be visible (the service owns the state;
        // the view only mirrors it, and visibility also respects
        // whether the Map itself is open).
        private readonly int[] _campSlotTier = new int[CampSlotCount];
        private readonly bool[] _campSlotVisible = new bool[CampSlotCount];
        private readonly MapPoint[] _campSlotMarkers = new MapPoint[CampSlotCount];
        private readonly List<MapPoint>[] _campSlotCreatures = new List<MapPoint>[CampSlotCount];

        // Creature layout per camp tier, as offsets from the slot
        // position (the same relative layouts the 3 fixed camps used):
        // Tier 1 = 4 Spore Crawlers, Tier 2 = 5 Void Stalkers,
        // Tier 3 = the Brood Tyrant (2x) + 3 brood (1x).
        private struct SlotCreatureDef
        {
            public string ResourcePath;
            public Vector3 Offset;
            public float Scale;
        }
        private static readonly SlotCreatureDef[][] CampCreatureTemplates =
        {
            new SlotCreatureDef[]
            {
                new SlotCreatureDef { ResourcePath = "Models/SporeCrawler", Offset = new Vector3(6f, 0f, 4f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/SporeCrawler", Offset = new Vector3(-6f, 0f, 5f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/SporeCrawler", Offset = new Vector3(5f, 0f, -6f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/SporeCrawler", Offset = new Vector3(-5f, 0f, -5f), Scale = 2f },
            },
            new SlotCreatureDef[]
            {
                new SlotCreatureDef { ResourcePath = "Models/VoidStalker", Offset = new Vector3(7f, 0f, 5f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/VoidStalker", Offset = new Vector3(-7f, 0f, 4f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/VoidStalker", Offset = new Vector3(6f, 0f, -6f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/VoidStalker", Offset = new Vector3(-6f, 0f, -5f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/VoidStalker", Offset = new Vector3(0f, 0f, 8f), Scale = 2f },
            },
            new SlotCreatureDef[]
            {
                new SlotCreatureDef { ResourcePath = "Models/BroodTyrant", Offset = new Vector3(8f, 0f, 0f), Scale = 2f },
                new SlotCreatureDef { ResourcePath = "Models/BroodTyrant", Offset = new Vector3(-7f, 0f, 6f), Scale = 1f },
                new SlotCreatureDef { ResourcePath = "Models/BroodTyrant", Offset = new Vector3(-7f, 0f, -6f), Scale = 1f },
                new SlotCreatureDef { ResourcePath = "Models/BroodTyrant", Offset = new Vector3(2f, 0f, 9f), Scale = 1f },
            },
        };

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

        // Build the 15 camp slot markers (the EnemyCamp model at each
        // slot position). Creatures are NOT built here — they depend on
        // the slot's current tier and are built by SetCampSlot.
        private void BuildCampSlots()
        {
            GameObject prefab = null;
            try
            {
                prefab = Resources.Load<GameObject>("Models/EnemyCamp");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ExteriorView] Camp marker failed to load (Models/EnemyCamp): " + e.Message);
            }
            for (int slot = 0; slot < CampSlotCount; slot++)
            {
                _campSlotCreatures[slot] = new List<MapPoint>();
                _campSlotTier[slot] = 0;
                _campSlotVisible[slot] = false;
                if (prefab == null) continue;
                var go = Instantiate(prefab);
                go.name = "Empty Camp Slot";
                go.transform.position = CampSlotPositions[slot];
                go.transform.localScale = Vector3.one * 2f;
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 2f, 0f);
                    box.size = new Vector3(4f, 4f, 4f);
                }
                go.SetActive(false);
                var renderers = go.GetComponentsInChildren<Renderer>();
                var colors = new Color[renderers.Length];
                for (int r = 0; r < renderers.Length; r++) colors[r] = renderers[r].material.color;
                var point = new MapPoint { Go = go, Data = CampSlotData(slot, 0), Renderers = renderers, OriginalColors = colors };
                _campSlotMarkers[slot] = point;
                _mapPoints.Add(point);
            }
            if (prefab == null)
            {
                Debug.LogWarning("[ExteriorView] EnemyCamp.fbx is missing — camp slots will be empty. Copy EnemyCamp.fbx into Assets/_Project/Resources/Models/ (do not rename it).");
            }
        }

        /// <summary>Show a camp slot as the given tier (0 = empty):
        /// rebuilds the slot's creatures for the tier and updates the
        /// marker's data in place (so a panel already showing this camp
        /// stays correct). No-op when the slot already shows that tier.
        /// Visibility is separate — see SetCampSlotVisible.</summary>
        public void SetCampSlot(int slot, int tier)
        {
            if (slot < 0 || slot >= CampSlotCount) return;
            if (tier < 0 || tier > 3) tier = 0;
            if (_campSlotTier[slot] == tier) return;
            DestroySlotCreatures(slot);
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
            if (tier > 0) BuildSlotCreatures(slot, tier);
            ApplySlotVisibility(slot);
        }

        /// <summary>Show or hide a camp slot (marker + creatures). An
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
            var creatures = _campSlotCreatures[slot];
            if (creatures == null) return;
            foreach (var creature in creatures)
            {
                if (creature.Go != null) creature.Go.SetActive(active);
            }
        }

        private void BuildSlotCreatures(int slot, int tier)
        {
            var template = CampCreatureTemplates[tier - 1];
            string title = CampTitleForTier(tier);
            foreach (var def in template)
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
                go.name = title + " Creature";
                go.transform.position = CampSlotPositions[slot] + def.Offset;
                go.transform.localScale = Vector3.one * def.Scale;
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 2f, 0f);
                    box.size = new Vector3(4f, 4f, 4f);
                }
                go.SetActive(false);
                // The creature IS its camp for tapping: it carries the
                // slot's data (with the CAMP position, so the march
                // target is the camp, not the creature's offset spot).
                var point = new MapPoint { Go = go, Data = CampSlotData(slot, tier) };
                _campSlotCreatures[slot].Add(point);
                _mapPoints.Add(point);
            }
        }

        private void DestroySlotCreatures(int slot)
        {
            var creatures = _campSlotCreatures[slot];
            if (creatures == null) return;
            foreach (var creature in creatures)
            {
                _mapPoints.Remove(creature);
                if (creature.Go != null) Destroy(creature.Go);
            }
            creatures.Clear();
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
            // the 280x240 map tiles (40,34). Wrap Mode Repeat. The
            // colour is darkened to 0.7 (Patrick 2026-11-09: the map
            // terrain read almost white/snowy at (40,34) from the far
            // camera — the fine texture averages out to light grey, so
            // the material colour pulls it back toward lunar grey).
            var renderer = _terrain.GetComponentInChildren<Renderer>();
            var regolith = Resources.Load<Texture2D>("Textures/GroundRegolith");
            if (regolith != null && renderer != null)
            {
                regolith.wrapMode = TextureWrapMode.Repeat;
                renderer.material.mainTexture = regolith;
                renderer.material.mainTextureScale = new Vector2(40f, 34f);
                renderer.material.color = new Color(0.7f, 0.7f, 0.7f);
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
            BuildMarchMarker();
        }

        // Excursion march markers (2026-11-09; two armies 2026-11-11):
        // a bright capsule per army slot, big enough to read from the
        // far exterior camera — cyan for Army 1, orange for Army 2.
        // No collider — the tap raycast only checks map points, so a
        // marker can never be tapped by mistake. Hidden until a march
        // starts.
        private void BuildMarchMarker()
        {
            _marchMarkers[0] = MakeMarchMarker("ExcursionMarker", new Color(0.2f, 0.9f, 1.0f));
            _marchMarkers[1] = MakeMarchMarker("ExcursionMarker2", new Color(1.0f, 0.55f, 0.15f));
        }

        private GameObject MakeMarchMarker(string markerName, Color color)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            marker.name = markerName;
            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                // URP Lit (the project's pipeline) so the marker never
                // renders magenta; fall back to the primitive's own
                // material if the shader lookup fails.
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) renderer.material = new Material(lit);
                renderer.material.color = color;
            }
            marker.transform.localScale = Vector3.one * 2f;
            marker.SetActive(false);
            return marker;
        }

        // Fixed creatures on the map: only the War Robot at the boss
        // lair now (2026-11-10). Camp creatures are built per camp
        // slot from CampCreatureTemplates, since a slot's tier (and
        // therefore its creatures) is dynamic.
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
            // Boss — War Robot at its lair (-120, -100), lair index 12.
            // Scale 4x: a GIANT war robot, twice the size of the Brood
            // Tyrant. Stands 6 units north of the lair structure so the
            // two do not overlap. Tapping it selects the lair.
            new CreatureDef { ResourcePath = "Models/WarRobot", CampIndex = 12, Position = new Vector3(-120f, 0f, -94f), Scale = 4f },
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
                _marchMarkers[slot].transform.position = next;
                if (Vector3.Distance(next, _marchTargets[slot]) < 0.1f)
                {
                    _marching[slot] = false;
                    var callback = _marchCallbacks[slot];
                    _marchCallbacks[slot] = null;
                    callback?.Invoke();
                }
            }
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
            if (Physics.Raycast(ray, out var hit, 500f))
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
            if (!hitPoint) OnEmptyMapTapped?.Invoke();
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
            marker.transform.position = from;
            marker.SetActive(true);
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
            if (_marchMarkers[slot] != null) _marchMarkers[slot].SetActive(false);
        }

        // Stop every slot's march and hide every marker (the Map is
        // closing, so no marker may linger in another view).
        public void CancelAllMarches()
        {
            for (int slot = 0; slot < _marchMarkers.Length; slot++) CancelMarch(slot);
        }

        // Fight animation (2026-11-09, last feature of the phase): a
        // short (~2s) clash on the map when the army reaches a camp.
        // No new FBX — the existing creature models and the march marker
        // are animated in code. Phase 1 (~0.9s) is the clash: the marker
        // pulses and the creatures shake. Phase 2 (~1.1s) is the
        // outcome: on victory the creatures tip over and sink away; on
        // defeat the marker shakes and shrinks while the creatures hop.
        // Transforms are restored at the end (so a respawned camp is
        // upright), then onComplete fires — the service shows the result
        // panel, vanishes the camp on victory, and starts the return
        // march. The restore + hide happen in the same frame, so the
        // player never sees the creatures pop back upright.
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
            var marker = slot >= 0 && slot < _marchMarkers.Length ? _marchMarkers[slot] : null;
            Vector3 markerPos = marker != null ? marker.transform.position : Vector3.zero;
            Vector3 markerScale = marker != null ? marker.transform.localScale : Vector3.one;

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
                if (marker != null)
                {
                    float pulse = 1f + 0.18f * Mathf.Abs(Mathf.Sin(t * 18f));
                    marker.transform.localScale = markerScale * pulse;
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
                    // Marker does a small victory bounce.
                    if (marker != null)
                    {
                        marker.transform.localScale = markerScale;
                        Vector3 pos = markerPos;
                        pos.y = markerPos.y + Mathf.Sin(p * Mathf.PI) * 1.5f;
                        marker.transform.position = pos;
                    }
                }
                else
                {
                    // Marker shakes hard and shrinks (the army is broken);
                    // creatures hop once (they won).
                    if (marker != null)
                    {
                        Vector3 jitter = new Vector3(
                            UnityEngine.Random.Range(-1.2f, 1.2f), 0f,
                            UnityEngine.Random.Range(-1.2f, 1.2f));
                        marker.transform.position = markerPos + jitter;
                        marker.transform.localScale = markerScale * Mathf.Lerp(1f, 0.55f, p);
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
            if (marker != null)
            {
                marker.transform.position = markerPos;
                marker.transform.localScale = markerScale;
            }
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
            _marchMarkers[slot].transform.position = position;
            _marchMarkers[slot].SetActive(true);
        }

        // Mark a resource node as occupied (being farmed): its renderers
        // turn amber. Restoring clears the amber back to the original
        // colours. Camps and the boss lair never occupy, and creature
        // points carry camp/lair data (index 9-12), so the index check
        // alone keeps them out.
        public void SetNodeOccupied(int nodeIndex, bool occupied)
        {
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
            var cam = Camera.main;
            if (cam == null) return;
            if (visible)
            {
                // Opening the Map always recentres on the base — the
                // pan starts from zero each time.
                _panOffset = Vector3.zero;
                _pointerDown = false;
                _isDrag = false;
                ApplyCamera();
            }
            else
            {
                cam.transform.position = BaseCameraPosition;
                cam.transform.LookAt(Vector3.zero);
            }
        }
    }
}
