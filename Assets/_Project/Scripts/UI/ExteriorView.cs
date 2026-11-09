using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LunarDominion.UI
{
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
    /// armies, node timeout/regeneration, and arrival/combat come in
    /// later phases. No rules live here.</summary>
    public class ExteriorView : MonoBehaviour
    {
        // Raised when a tap lands on a map node or camp — the title
        // and info lines for the info panel.
        public event Action<string, string> OnMapPointSelected;
        // Raised when a tap lands on empty map ground (not a node/camp).
        public event Action OnEmptyMapTapped;

        // Exterior camera: 5x the base camera distance, same pitch.
        // Base camera (10,16,10) frames the 56x48 ground; scaling
        // both by 5 frames the 280x240 map identically.
        private static readonly Vector3 BaseCameraPosition = new Vector3(10f, 16f, 10f);
        private static readonly Vector3 ExteriorCameraPosition = new Vector3(50f, 80f, 50f);

        private GameObject _terrain;
        private GameObject _baseCastle;
        private bool _visible;

        // A tappable point on the map: a resource node or an enemy camp.
        private class MapPoint
        {
            public GameObject Go;
            public string Title;
            public string Info;
        }
        private readonly List<MapPoint> _mapPoints = new List<MapPoint>();

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
        // base, Tier 2 mid-range, Tier 3 far out). Amounts are display
        // values for now; node timeout/regeneration is a later phase.
        // Positions are world coords on the 280x240 map (x ±140,
        // z ±120), all outside the central 56x48 base footprint.
        private struct NodeDef
        {
            public string ResourcePath; // Resources/Models/<name>
            public string Title;
            public string Info;
            public Vector3 Position;
        }
        private static readonly NodeDef[] NodeDefs =
        {
            new NodeDef { ResourcePath = "Models/BiomassNode", Title = "Biomass Node", Info = "Biomass: 500", Position = new Vector3(35f, 0f, 25f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Title = "Biomass Node", Info = "Biomass: 1000", Position = new Vector3(-55f, 0f, 45f) },
            new NodeDef { ResourcePath = "Models/BiomassNode", Title = "Biomass Node", Info = "Biomass: 2000", Position = new Vector3(95f, 0f, -55f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Title = "Alloy Node", Info = "Alloy: 500", Position = new Vector3(-30f, 0f, -35f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Title = "Alloy Node", Info = "Alloy: 1000", Position = new Vector3(65f, 0f, 55f) },
            new NodeDef { ResourcePath = "Models/AlloyNode", Title = "Alloy Node", Info = "Alloy: 2000", Position = new Vector3(-105f, 0f, -65f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Title = "Helium-3 Node", Info = "Helium-3: 500", Position = new Vector3(45f, 0f, -40f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Title = "Helium-3 Node", Info = "Helium-3: 1000", Position = new Vector3(-70f, 0f, -20f) },
            new NodeDef { ResourcePath = "Models/HeliumNode", Title = "Helium-3 Node", Info = "Helium-3: 2000", Position = new Vector3(110f, 0f, 75f) },
            new NodeDef { ResourcePath = "Models/EnemyCamp", Title = "Spore Crawler Camp", Info = "Tier 1 — Spore Crawlers\nExpedition enemy", Position = new Vector3(55f, 0f, 15f) },
            new NodeDef { ResourcePath = "Models/EnemyCamp", Title = "Void Stalker Camp", Info = "Tier 2 — Void Stalkers\nExpedition enemy", Position = new Vector3(-85f, 0f, 60f) },
            new NodeDef { ResourcePath = "Models/EnemyCamp", Title = "Brood Tyrant Camp", Info = "Tier 3 — Brood Tyrant\nExpedition enemy", Position = new Vector3(120f, 0f, -80f) },
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
        }

        private void BuildMapPoints()
        {
            foreach (var def in NodeDefs)
            {
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
                _mapPoints.Add(new MapPoint { Go = go, Title = def.Title, Info = def.Info });
            }
        }

        private void Update()
        {
            if (!_visible) return;
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
                    if (point.Go == null) continue;
                    if (hit.transform == point.Go.transform || hit.transform.IsChildOf(point.Go.transform))
                    {
                        OnMapPointSelected?.Invoke(point.Title, point.Info);
                        hitPoint = true;
                        break;
                    }
                }
            }
            if (!hitPoint) OnEmptyMapTapped?.Invoke();
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
