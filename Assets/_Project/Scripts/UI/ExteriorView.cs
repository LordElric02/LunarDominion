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
            // the 280x240 map tiles (40,34). Wrap Mode Repeat, colour
            // white so the texture reads as-authored.
            var renderer = _terrain.GetComponentInChildren<Renderer>();
            var regolith = Resources.Load<Texture2D>("Textures/GroundRegolith");
            if (regolith != null && renderer != null)
            {
                regolith.wrapMode = TextureWrapMode.Repeat;
                renderer.material.mainTexture = regolith;
                renderer.material.mainTextureScale = new Vector2(40f, 34f);
                renderer.material.color = Color.white;
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
                // rotation alone (Bake Axis Conversion).
                _baseCastle.transform.localScale = Vector3.one;
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
                // rotation alone (Bake Axis Conversion).
                go.transform.localScale = Vector3.one;
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
            // Input System note: Unity throws InvalidOperationException if
            // the legacy UnityEngine.Input API is read while Player Settings
            // > Active Input Handler is set to Input System Package only.
            // Same pattern as BaseView.Update: read Mouse/Touchscreen when
            // the new Input System is enabled, legacy Input otherwise.
            bool tapped = false;
            Vector2 screenPos = Vector2.zero;
            int touchPointerId = -1;
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                tapped = true;
                screenPos = mouse.position.ReadValue();
            }
            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                tapped = true;
                screenPos = touchscreen.primaryTouch.position.ReadValue();
                touchPointerId = (int)touchscreen.primaryTouch.touchId.ReadValue();
            }
#else
            tapped = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
            if (tapped) screenPos = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
#endif
            if (!tapped) return;
            // UI taps never reach here — EventSystem filters them out.
            if (EventSystem.current != null)
            {
#if ENABLE_INPUT_SYSTEM
                if (touchPointerId >= 0 ? EventSystem.current.IsPointerOverGameObject(touchPointerId) : EventSystem.current.IsPointerOverGameObject()) return;
#else
                if (Input.touchCount > 0 && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)) return;
                if (Input.touchCount == 0 && EventSystem.current.IsPointerOverGameObject()) return;
#endif
            }
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
                cam.transform.position = ExteriorCameraPosition;
                cam.transform.LookAt(Vector3.zero);
            }
            else
            {
                cam.transform.position = BaseCameraPosition;
                cam.transform.LookAt(Vector3.zero);
            }
        }
    }
}
