using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LunarDominion.UI
{
    /// <summary>What a tappable map point is: a resource node (Biomass,
    /// Alloy, Helium-3) or an enemy camp (one per Expedition tier).</summary>
    public enum MapPointKind
    {
        Biomass,
        Alloy,
        Helium3,
        Camp
    }

    /// <summary>Structured data for one tappable map point — the display
    /// strings plus what the excursion rules need (kind, tier, amount,
    /// position, and the index into the node/camp lists). Nodes are
    /// indices 0-8 (in NodeDefs order); camps are 9-11.</summary>
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
        private GameObject _marchMarker;
        private bool _marching;
        private Vector3 _marchTarget;
        private Action _marchCallback;
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
        };

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
            BuildMarchMarker();
        }

        // Excursion march marker (2026-11-09): a bright cyan capsule,
        // big enough to read from the far exterior camera. No collider —
        // the tap raycast only checks map points, so the marker can never
        // be tapped by mistake. Hidden until a march starts.
        private void BuildMarchMarker()
        {
            _marchMarker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _marchMarker.name = "ExcursionMarker";
            var renderer = _marchMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                // URP Lit (the project's pipeline) so the marker never
                // renders magenta; fall back to the primitive's own
                // material if the shader lookup fails.
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) renderer.material = new Material(lit);
                renderer.material.color = new Color(0.2f, 0.9f, 1.0f);
            }
            _marchMarker.transform.localScale = Vector3.one * 2f;
            _marchMarker.SetActive(false);
        }

        // Enemy creatures on the map (Patrick 2026-11-09): the actual
        // Expedition enemies, not just the camp markers — 4 Spore
        // Crawlers around the Tier 1 camp, 5 Void Stalkers around the
        // Tier 2 camp, and the Brood Tyrant + 3 brood (mini-Tyrants)
        // around the Tier 3 camp. Each creature is tappable and shows
        // its camp's info (same Title/Info as the camp). Scale is
        // 2x like the nodes/camps, except the brood at 1x so they read
        // clearly smaller than the boss.
        private struct CreatureDef
        {
            public string ResourcePath; // Resources/Models/<name>
            public int CampIndex; // index into NodeDefs (9-11) — tapping
            // a creature selects its camp, not a separate point.
            public Vector3 Position;
            public float Scale;
        }
        private static readonly CreatureDef[] CreatureDefs =
        {
            // Tier 1 — Spore Crawlers around (55, 15), camp index 9.
            new CreatureDef { ResourcePath = "Models/SporeCrawler", CampIndex = 9, Position = new Vector3(61f, 0f, 19f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/SporeCrawler", CampIndex = 9, Position = new Vector3(49f, 0f, 20f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/SporeCrawler", CampIndex = 9, Position = new Vector3(60f, 0f, 9f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/SporeCrawler", CampIndex = 9, Position = new Vector3(50f, 0f, 10f), Scale = 2f },
            // Tier 2 — Void Stalkers around (-85, 60), camp index 10.
            new CreatureDef { ResourcePath = "Models/VoidStalker", CampIndex = 10, Position = new Vector3(-78f, 0f, 65f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/VoidStalker", CampIndex = 10, Position = new Vector3(-92f, 0f, 64f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/VoidStalker", CampIndex = 10, Position = new Vector3(-79f, 0f, 54f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/VoidStalker", CampIndex = 10, Position = new Vector3(-91f, 0f, 55f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/VoidStalker", CampIndex = 10, Position = new Vector3(-85f, 0f, 68f), Scale = 2f },
            // Tier 3 — Brood Tyrant + 3 brood around (120, -80), camp index 11.
            new CreatureDef { ResourcePath = "Models/BroodTyrant", CampIndex = 11, Position = new Vector3(128f, 0f, -80f), Scale = 2f },
            new CreatureDef { ResourcePath = "Models/BroodTyrant", CampIndex = 11, Position = new Vector3(113f, 0f, -74f), Scale = 1f },
            new CreatureDef { ResourcePath = "Models/BroodTyrant", CampIndex = 11, Position = new Vector3(113f, 0f, -86f), Scale = 1f },
            new CreatureDef { ResourcePath = "Models/BroodTyrant", CampIndex = 11, Position = new Vector3(122f, 0f, -71f), Scale = 1f },
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
            // Excursion march: the marker walks from its start to the
            // target at a steady speed, then fires the arrival callback
            // (the service resolves the outcome and starts the return
            // march). Only runs while the Map is visible — leaving the
            // Map tab cancels the march before any outcome applies.
            if (_marching && _marchMarker != null)
            {
                Vector3 next = Vector3.MoveTowards(_marchMarker.transform.position, _marchTarget, MarchSpeed * Time.deltaTime);
                _marchMarker.transform.position = next;
                if (Vector3.Distance(next, _marchTarget) < 0.1f)
                {
                    _marching = false;
                    var callback = _marchCallback;
                    _marchCallback = null;
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

        // Start the march marker at `from`, walking to `to`, then fire
        // `onArrive`. The marker rides 1 unit above the ground so it
        // never clips the terrain. The caller (MapExcursionService)
        // chains outbound and return marches through the callback.
        public void StartMarch(Vector3 from, Vector3 to, Action onArrive)
        {
            if (_marchMarker == null) return;
            _marchMarker.transform.position = from;
            _marchMarker.SetActive(true);
            _marchTarget = to;
            _marchCallback = onArrive;
            _marching = true;
        }

        // Stop the march and hide the marker. Called when the player
        // leaves the Map tab mid-excursion, or when a march completes.
        // Cancelling before arrival means no outcome was applied.
        public void CancelMarch()
        {
            _marching = false;
            _marchCallback = null;
            if (_marchMarker != null) _marchMarker.SetActive(false);
        }

        // Structured data for a node/camp by index (0-11), for restoring
        // an occupation from the save (the service rebuilds its active
        // point) and for the Bootstrap's panel. Null when out of range.
        public MapPointData NodeData(int index)
        {
            if (index < 0 || index >= NodeDefs.Length) return null;
            return DataForNodeDef(index);
        }

        // World position of a node/camp by index, for the marker.
        public bool NodePosition(int index, out Vector3 position)
        {
            position = Vector3.zero;
            if (index < 0 || index >= NodeDefs.Length) return false;
            position = NodeDefs[index].Position;
            return true;
        }

        // Park the marker at a position without marching (2026-11-09):
        // restores it at an occupied node when the Map reopens, so the
        // army is visibly farming there. Never overrides an active march.
        public void ShowMarkerAt(Vector3 position)
        {
            if (_marchMarker == null || _marching) return;
            _marchMarker.transform.position = position;
            _marchMarker.SetActive(true);
        }

        // Mark a resource node as occupied (being farmed): its renderers
        // turn amber. Restoring clears the amber back to the original
        // colours. Camps never occupy, and creature points carry camp
        // data (index 9-11), so the index check alone keeps them out.
        public void SetNodeOccupied(int nodeIndex, bool occupied)
        {
            foreach (var point in _mapPoints)
            {
                if (point.Data == null || point.Data.Index != nodeIndex) continue;
                if (point.Data.Kind == MapPointKind.Camp) continue;
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
                if (point.Data.Kind == MapPointKind.Camp) continue;
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
                if (point.Go != null) point.Go.SetActive(visible);
            }
            // Hiding the Map also stops any march in progress — the
            // service cancels the excursion on tab switch, and this is
            // the backstop so the marker never lingers in the Base view.
            if (!visible) CancelMarch();
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
