using System;
using System.Collections.Generic;
using LunarDominion.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LunarDominion.UI
{
    /// <summary>Central lunar base scene, built from primitives only.
    /// Isometric-style camera, lunar ground, and one placeholder building
    /// per GDD building. Tapping a building opens the building panel.
    /// No rules live here.</summary>
    public class BaseView : MonoBehaviour
    {
        public event Action<BuildingDefinition> OnBuildingSelected;
        // Raised when a tap lands on the base but not on a building —
        // empty ground, or a ray that hits nothing at all.
        public event Action OnEmptyBaseTapped;

        private readonly Dictionary<BuildingId, GameObject> _buildingObjects = new Dictionary<BuildingId, GameObject>();
        private readonly Dictionary<BuildingId, Color> _baseColors = new Dictionary<BuildingId, Color>();
        // Blender-model buildings (Command Nexus, Hydroponics Dome, Alloy
        // Extractor, Helium-3 Harvester, Marine Foundry, Nova Arsenal,
        // Aegis Motor Pool and Research Lab — all eight buildings): the
        // renderers and each part's original colour, so Highlight() can
        // brighten/dim each part from its own colour instead of flattening
        // the whole model to one colour. Primitives keep the old path.
        private readonly Dictionary<BuildingId, List<Renderer>> _modelRenderers = new Dictionary<BuildingId, List<Renderer>>();
        private readonly Dictionary<Renderer, Color> _modelBaseColors = new Dictionary<Renderer, Color>();
        private GameController _controller;
        // Rotated layout root (2026-11-06): buildings, wall and decor
        // are children of this object, so the whole base turns 45 degrees
        // while the ground plane and mountains stay unrotated.
        private GameObject _layoutRoot;
        // Reactor animation (2026-11-06): the Nexus FBX keeps the reactor
        // sphere and each containment ring as separate named objects, so
        // they can move independently. Collected after the model loads;
        // empty when the model is missing or is the primitive fallback.
        private Transform _reactorCore;
        private Vector3 _reactorCoreBaseScale = Vector3.one;
        private readonly List<Transform> _reactorRings = new List<Transform>();
        private readonly List<float> _reactorRingSpeeds = new List<float>();
        private float _reactorAnimTime;

        public void Build(GameController controller)
        {
            _controller = controller;
            // 2026-11-06: rotate the base 45 degrees so the wall perimeter
            // reads parallel to the screen's horizontal borders. The camera
            // sits at (10,16,10), so screen-right is world (0.707,0,-0.707);
            // a +45-degree Y rotation maps the layout's local X axis onto
            // screen-right and its local Z axis onto screen-depth, so the
            // wall's north/south edges run horizontal on screen and its
            // east/west edges run vertical. Buildings, wall and decor are
            // parented under this root (see ParentToLayout); the ground
            // plane and mountains are NOT rotated — the ground is a flat
            // plane that still covers the frame, and the mountains are the
            // horizon fixed to the far edges.
            _layoutRoot = new GameObject("BaseLayout");
            _layoutRoot.transform.position = Vector3.zero;
            _layoutRoot.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            BuildGroundAndCamera();
            BuildBuildings();
        }

        private void BuildGroundAndCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }
            // 2026-11-06 full-bleed: camera (10,16,10), pitch 48.5
            // degrees, over the 56x48 ground. Checked with real camera
            // math (60-degree vertical FOV, 16:9): every frame edge is
            // covered by ground or mountains (0 uncovered boundary
            // samples). 2026-11-06 rotation: the layout turns 45 degrees
            // under BaseLayout, so buildings now span NDC x -0.42..0.28
            // and y -0.29..0.35 — inside the frame, clear of the top bar
            // (bottom edge NDC y 0.74) and the quick-select row (top
            // edge NDC y -0.50). The wall's near edge is horizontal at
            // NDC y -0.46 (top), its base at -0.495, just 0.009 above
            // the quick-select row top (-0.504) — the tightest margin in
            // the layout; moving the camera to widen it breaks the
            // mountain coverage, so the camera stays at (10,16,10). The
            // old (11,13,11) camera left the far ground corner at NDC x
            // 0.83, so dark background showed around the ground.
            cam.transform.position = new Vector3(10f, 16f, 10f);
            cam.transform.LookAt(Vector3.zero);
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            lightGo.transform.position = new Vector3(5f, 10f, 0f);
            lightGo.transform.LookAt(Vector3.zero);

            // Phase 5 fill light: a soft cool light from the opposite
            // side, so buildings are readable from the isometric camera
            // instead of having one fully dark face. Primitives only —
            // no final art, no new models.
            var fillGo = new GameObject("Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.35f;
            fill.color = new Color(0.8f, 0.85f, 1f);
            fillGo.transform.position = new Vector3(-5f, 8f, -5f);
            fillGo.transform.LookAt(Vector3.zero);

            // 2026-11-06 full-bleed: ground 56 wide by 48 deep, so it
            // runs past all four frame edges from the (10,16,10)
            // camera — the whole screen is terrain, with no black bars
            // around it. The wall sits at x +/-10.5, z +/-6.5 and the
            // mountains at the far edges, all on this ground.
            // 2026-11-07: the ground is the sculpted terrain mesh
            // (TerrainMesh.fbx), flat plane as fallback — see
            // BuildGround() below.
            BuildGround();

            // 2026-11-05: moon-like surface. The ground now has scattered
            // boulders. All decor is low, fixed-position (no randomness at
            // runtime), clear of every building, and has its collider
            // stripped so a tap ray can never be swallowed by a rock in
            // front of a building.
            // 2026-11-06 full-bleed: decor re-spread onto the 56x48
            // ground — two rocks inside the compound, eight more rocks
            // in the outer band between the wall and the ground edge, so
            // the wider ground does not read empty. Every item is clear
            // of every building (>= 0.6 edge gap, checked), clear of the
            // wall, and inside the ground edges. Positions are fixed (no
            // randomness at runtime), colliders stripped so a tap ray can
            // never be swallowed by a rock in front of a building.
            // 2026-11-06 rotation: the decor turns 45 degrees with the
            // layout root, so three outer rocks that would have landed
            // outside the ground or inside a mountain after the rotation
            // now sit in the +X/+Z outer band, clear of the wall,
            // buildings and mountains.
            // 2026-11-07: the flat tonal patch discs are removed — the
            // regolith texture now gives the ground its tonal variation,
            // and the flat circles read as stains on top of it. The
            // darker crater discs are removed too — the regolith texture
            // and the debris carry the ground detail now, and the flat
            // dark circles read the same way. The rocks stay.
            AddRock(new Vector3(-2f, 0f, 5.5f), 0.4f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(2.5f, 0f, -5.5f), 0.45f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(-16f, 0f, 16f), 0.5f, new Color(0.38f, 0.4f, 0.44f));
            AddRock(new Vector3(24f, 0f, 8f), 0.5f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(12f, 0f, 16f), 0.45f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(24f, 0f, 2f), 0.5f, new Color(0.33f, 0.35f, 0.39f));
            AddRock(new Vector3(8f, 0f, 18f), 0.4f, new Color(0.38f, 0.4f, 0.44f));
            AddRock(new Vector3(16f, 0f, 14f), 0.5f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(22f, 0f, 12f), 0.45f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(20f, 0f, 16f), 0.5f, new Color(0.33f, 0.35f, 0.39f));

            // 2026-11-07: Blender debris models (rubble / rocks /
            // shards), placed in world space like the ground plane.
            AddDebris();

            // 2026-11-07: Blender roads + props, placed in world space
            // like the debris. The gate needs the wall gap (BuildWall).
            AddRoadsAndProps();

            BuildWall();
            BuildMountains();
            BuildAlienPlanet();
        }

        // 2026-11-06: planet-and-stars sky removed from the base view —
        // it is reserved for a future exterior view. The code lives in
        // git history (commits 69011168, 899f63a5).

        // Parent an object under the rotated layout root. SetParent with
        // worldPositionStays=false keeps the local transform, so callers
        // must set localPosition (not position) afterwards — setting
        // position would place the object in world space and undo the
        // rotation. The ground plane and mountains never call this.
        private void ParentToLayout(GameObject go)
        {
            go.transform.SetParent(_layoutRoot.transform, false);
        }

        // A single boulder, squashed slightly so it reads as a rock.
        private void AddRock(Vector3 pos, float size, Color color)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock";
            ParentToLayout(rock);
            rock.transform.localPosition = new Vector3(pos.x, size * 0.25f, pos.z);
            rock.transform.localScale = new Vector3(size, size * 0.6f, size * 0.85f);
            SetPrimitiveColor(rock.GetComponent<Renderer>(), color);
            StripCollider(rock);
        }

        // 2026-11-07: ground debris — Blender rubble/rock/shard models
        // (DebrisRubble / DebrisRocks / DebrisShards in Resources/Models/).
        // Placed in world space (like the ground plane, not under the
        // rotated layout root) at fixed positions checked against the
        // rotated building/wall/decor layout. No colliders and no tap
        // registration: debris is scenery, like the rocks. If an FBX
        // is missing, that spot stays empty — the ground texture and
        // the existing decor still show.
        private void AddDebris()
        {
            AddDebrisAt("Models/DebrisRubble", new Vector3(0f, 0f, -7.5f), 0f);
            AddDebrisAt("Models/DebrisRocks", new Vector3(-7.5f, 0f, 0f), 40f);
            AddDebrisAt("Models/DebrisShards", new Vector3(7.5f, 0f, 0f), 80f);
            AddDebrisAt("Models/DebrisRubble", new Vector3(-0.5f, 0f, 7.5f), 130f);
            AddDebrisAt("Models/DebrisRocks", new Vector3(14f, 0f, -10f), 200f);
            AddDebrisAt("Models/DebrisShards", new Vector3(-14f, 0f, 18f), 260f);
            AddDebrisAt("Models/DebrisRubble", new Vector3(20f, 0f, 8f), 310f);
            AddDebrisAt("Models/DebrisRubble", new Vector3(16f, 0f, -12f), 25f);
        }

        private void AddDebrisAt(string resourcePath, Vector3 position, float rotationY)
        {
            GameObject prefab = null;
            try
            {
                prefab = Resources.Load<GameObject>(resourcePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Debris model failed to load (" + resourcePath + "): " + e.Message);
                return;
            }
            if (prefab == null) return;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "Debris";
                go.transform.position = position;
                go.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
                go.transform.localScale = Vector3.one;
                foreach (var collider in go.GetComponentsInChildren<Collider>())
                    Destroy(collider);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Debris model failed to place (" + resourcePath + "): " + e.Message);
                if (go != null) Destroy(go);
            }
        }

        // 2026-11-07: roads + props — Blender road pieces (RoadStraight /
        // RoadJunction / RoadTee) and prop models (PropSolarPanel /
        // PropCrates / PropStorageTank / PropRover / PropGate) in
        // Resources/Models/. Placed in world space (like the debris, not
        // under the rotated layout root) at fixed positions from the
        // layout plan. Roads sit at y 0.015 so the slab base clears the
        // ground plane. No colliders and no tap registration: roads and
        // props are scenery. If an FBX is missing, that spot stays empty.
        // The spine (E1/S1/J1/S2) and the NW street (N1/N2) are two
        // separate systems — the link between them would run through the
        // Nexus footprint. The tee is built but unused.
        private void AddRoadsAndProps()
        {
            AddSceneryAt("Models/RoadStraight", new Vector3(5.45f, 0.015f, 3.75f), 45f, "Road");
            // 2026-11-07: E1 extended NW along the near wall toward
            // Hydroponics — butts E1's NW end, enabled by moving the
            // solar panel (below) out of its path.
            AddSceneryAt("Models/RoadStraight", new Vector3(2.63f, 0.015f, 6.57f), 45f, "Road");
            // 2026-11-07: extended one more piece NW along the near wall
            // toward the Hydroponics corner — butts the previous piece's
            // NW end, enabled by moving the solar panel (below) right.
            AddSceneryAt("Models/RoadStraight", new Vector3(-0.19f, 0.015f, 9.39f), 45f, "Road");
            // 2026-11-07: E1 extended SE along the near wall from the gate
            // to the right corner — butts E1's SE end, then butts the
            // previous piece. The solar panel at (8.5, -2.0) is NOT in
            // the way: it sits 1.90 units off this road line, ~0.55 clear
            // of the road edge, so it stays put.
            AddSceneryAt("Models/RoadStraight", new Vector3(8.27f, 0.015f, 0.93f), 45f, "Road");
            AddSceneryAt("Models/RoadStraight", new Vector3(11.10f, 0.015f, -1.90f), 45f, "Road");
            AddSceneryAt("Models/RoadStraight", new Vector3(4.46f, 0.015f, 1.07f), -45f, "Road");
            AddSceneryAt("Models/RoadJunction", new Vector3(2.20f, 0.015f, -1.19f), -45f, "Road");
            AddSceneryAt("Models/RoadStraight", new Vector3(-0.06f, 0.015f, -3.45f), -45f, "Road");
            AddSceneryAt("Models/RoadStraight", new Vector3(-1.42f, 0.015f, 2.58f), -45f, "Road");
            AddSceneryAt("Models/RoadStraight", new Vector3(-4.24f, 0.015f, -0.24f), -45f, "Road");

            // 2026-11-07: moved right (toward Hydroponics) from (1.5, 6.5),
            // then again to (0.1, 6.2) so the wall road can extend past it
            // toward the Hydroponics corner.
            AddSceneryAt("Models/PropSolarPanel", new Vector3(0.1f, 0f, 6.2f), 30f, "Prop");
            AddSceneryAt("Models/PropSolarPanel", new Vector3(-5.0f, 0f, 8.0f), -20f, "Prop");
            AddSceneryAt("Models/PropSolarPanel", new Vector3(8.5f, 0f, -2.0f), -30f, "Prop");
            AddSceneryAt("Models/PropCrates", new Vector3(-6.8f, 0f, 3.8f), 15f, "Prop");
            AddSceneryAt("Models/PropCrates", new Vector3(3.5f, 0f, -4.0f), 70f, "Prop");
            AddSceneryAt("Models/PropStorageTank", new Vector3(-3.5f, 0f, 6.5f), 0f, "Prop");
            AddSceneryAt("Models/PropStorageTank", new Vector3(3.5f, 0f, -6.5f), 0f, "Prop");
            AddSceneryAt("Models/PropRover", new Vector3(-1.5f, 0f, -1.5f), -45f, "Prop");
            AddSceneryAt("Models/PropGate", new Vector3(4.6f, 0f, 4.6f), 45f, "Prop");
        }

        private void AddSceneryAt(string resourcePath, Vector3 position, float rotationY, string name)
        {
            GameObject prefab = null;
            try
            {
                prefab = Resources.Load<GameObject>(resourcePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Scenery model failed to load (" + resourcePath + "): " + e.Message);
                return;
            }
            if (prefab == null) return;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = name;
                go.transform.position = position;
                go.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
                go.transform.localScale = Vector3.one;
                foreach (var collider in go.GetComponentsInChildren<Collider>())
                    Destroy(collider);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Scenery model failed to place (" + resourcePath + "): " + e.Message);
                if (go != null) Destroy(go);
            }
        }

        // 2026-11-07: sculpted terrain — a Blender heightfield mesh
        // (TerrainMesh.fbx in Resources/Models/) that replaces the
        // flat ground plane. Flat (height 0) inside the wall, gentle
        // dunes outside, rising toward the mountains. Its planar UVs
        // span 0-1 across the 56x48, so the regolith texture tiles
        // exactly as on the flat plane (8,7) — but the FBX material's
        // own tiling is (1,1), so the code must set (8,7) here or the
        // texture would stretch across the whole ground instead of
        // tiling. If the FBX is missing, the flat plane below is the
        // fallback, so the game still works. The mesh has no collider
        // (like the debris and roads) — taps on it hit nothing, which
        // the tap logic already treats as an empty tap.
        private void BuildGround()
        {
            GameObject terrainPrefab = null;
            try
            {
                terrainPrefab = Resources.Load<GameObject>("Models/TerrainMesh");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Terrain mesh failed to load (Models/TerrainMesh): " + e.Message);
            }

            Renderer groundRenderer;
            if (terrainPrefab != null)
            {
                var terrain = Instantiate(terrainPrefab);
                terrain.name = "LunarGround";
                terrain.transform.position = Vector3.zero;
                // Do NOT set the rotation here. The FBX carries a -90 deg X
                // axis conversion (Blender Z-up to Unity Y-up). When the
                // import's "Bake Axis Conversion" is off, that conversion
                // lives on this object's transform; forcing identity here
                // strips it and the flat heightfield renders as a 48-tall
                // vertical slab that blocks the camera's view of the base.
                // Leaving the imported rotation alone is correct whether
                // Bake is on (rotation already baked into the vertices,
                // transform is identity) or off (transform carries -90 X).
                terrain.transform.localScale = Vector3.one;
                groundRenderer = terrain.GetComponentInChildren<Renderer>();
            }
            else
            {
                // Flat-plane fallback (the pre-terrain ground).
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "LunarGround";
                ground.transform.localScale = new Vector3(5.6f, 1f, 4.8f);
                SetPrimitiveColor(ground.GetComponent<Renderer>(), new Color(0.27f, 0.29f, 0.33f));
                groundRenderer = ground.GetComponent<Renderer>();
            }

            // Regolith texture (GroundRegolith.png in
            // Resources/Textures/). If it loads, it replaces the flat
            // colour: the PNG is authored in sRGB at the ground's
            // colour, so the material colour goes to white — colour x
            // texture would double-darken it. Tiled (8,7) across the
            // 56x48 ground, Wrap Mode Repeat. If the PNG is missing,
            // the flat colour above stays (plane) or the FBX material
            // keeps its own texture reference (terrain).
            var regolith = Resources.Load<Texture2D>("Textures/GroundRegolith");
            if (regolith != null && groundRenderer != null)
            {
                regolith.wrapMode = TextureWrapMode.Repeat;
                groundRenderer.material.mainTexture = regolith;
                groundRenderer.material.mainTextureScale = new Vector2(8f, 7f);
                groundRenderer.material.color = Color.white;
            }
        }

        // 2026-11-06 full-bleed: perimeter wall around the building
        // area, like the walled compound in the reference layout.
        // Rectangle x +/-10.5, z +/-6.5 — every building sits inside
        // with margin (worst case Lab at x -8.3, edge -9.4, is 1.1
        // from the west wall). Four segments 0.8 high, 0.35 thick,
        // gunmetal grey-blue, with a thin cyan strip light along the
        // top of each segment and a 1.4-high corner tower (with cap)
        // at each corner. 2026-11-06: the whole layout rotates 45
        // degrees under BaseLayout, so the north/south edges read
        // horizontal on screen and the east/west edges vertical. No
        // gate yet: the near edge now sits just above the quick-select
        // row, so a gate gap there would land cleanly — a follow-up
        // if Patrick wants one.
        // No colliders, same as the decor, so taps are never swallowed.
        private void BuildWall()
        {
            var wallColor = new Color(0.32f, 0.35f, 0.42f);
            var towerColor = new Color(0.25f, 0.28f, 0.36f);
            var capColor = new Color(0.30f, 0.33f, 0.40f);
            var stripColor = new Color(0.38f, 0.85f, 1.0f);

            // North (+Z) and south (-Z) segments, corner to corner.
            // 2026-11-07: the north segment is split for the gate — a
            // 2.4-wide gap centred at local x 0 (world (4.6, 4.6), the
            // near wall edge's midpoint), where the gate model sits and
            // the E1 road passes through. The gate posts overlap the
            // wall ends by 0.13 on each side.
            AddWallBox(new Vector3(-5.9375f, 0.4f, 6.5f), new Vector3(9.475f, 0.8f, 0.35f), wallColor);
            AddWallBox(new Vector3(5.9375f, 0.4f, 6.5f), new Vector3(9.475f, 0.8f, 0.35f), wallColor);
            AddWallBox(new Vector3(0f, 0.4f, -6.5f), new Vector3(21.35f, 0.8f, 0.35f), wallColor);
            // East (+X) and west (-X) segments, corner to corner.
            AddWallBox(new Vector3(10.5f, 0.4f, 0f), new Vector3(0.35f, 0.8f, 13.35f), wallColor);
            AddWallBox(new Vector3(-10.5f, 0.4f, 0f), new Vector3(0.35f, 0.8f, 13.35f), wallColor);

            // Cyan strip lights along the top of each segment. The north
            // strip is split for the gate gap, like the wall below it.
            AddWallBox(new Vector3(-5.85f, 0.83f, 6.5f), new Vector3(9.3f, 0.06f, 0.12f), stripColor);
            AddWallBox(new Vector3(5.85f, 0.83f, 6.5f), new Vector3(9.3f, 0.06f, 0.12f), stripColor);
            AddWallBox(new Vector3(0f, 0.83f, -6.5f), new Vector3(21f, 0.06f, 0.12f), stripColor);
            AddWallBox(new Vector3(10.5f, 0.83f, 0f), new Vector3(0.12f, 0.06f, 13f), stripColor);
            AddWallBox(new Vector3(-10.5f, 0.83f, 0f), new Vector3(0.12f, 0.06f, 13f), stripColor);

            // Corner towers with caps.
            foreach (float x in new[] { -10.5f, 10.5f })
            {
                foreach (float z in new[] { -6.5f, 6.5f })
                {
                    AddWallBox(new Vector3(x, 0.7f, z), new Vector3(1f, 1.4f, 1f), towerColor);
                    AddWallBox(new Vector3(x, 1.525f, z), new Vector3(1.3f, 0.25f, 1.3f), capColor);
                }
            }
        }

        // One wall piece: a plain box, coloured, collider stripped.
        // Cubes are 1x1x1, so localScale is the world size.
        private void AddWallBox(Vector3 pos, Vector3 size, Color color)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Wall";
            ParentToLayout(box);
            box.transform.localPosition = pos;
            box.transform.localScale = size;
            SetPrimitiveColor(box.GetComponent<Renderer>(), color);
            StripCollider(box);
        }

        // 2026-11-06 full-bleed: low-poly mountain ridge around the
        // far (-X and -Z) edges of the ground, outside the wall — the
        // lunar horizon that fills the top and side edges of the
        // frame. No colliders. The +X and +Z edges need no mountains:
        // the ground plane itself covers those frame edges from this
        // camera (checked: 0 uncovered boundary samples). Mountain
        // tops project to NDC y 0.85..1.18, so they rise behind the
        // top bar, as in the reference.
        // 2026-11-07: the stacked-cylinder primitives are replaced by
        // three detailed Blender mountains (MountainA "Broad Twin",
        // MountainB "Sharp Spire", MountainC "Craggy Dome" in
        // Resources/Models/), cycled A/B/C so no two adjacent spots
        // share a variant, each with its own Y rotation and a scale
        // that maps the nominal r3.5 / h7.0 model to the spot's
        // radius/height. Same 13 spots as before. If an FBX is
        // missing, that spot stays empty.
        private void BuildMountains()
        {
            // 2026-11-08 horizon models (hidden_files/mountains-horizon/):
            // the old MountainA/B/C were tall peaks (h 5.5-8.0) that
            // read as a ridgeline even in a ring. The new HorizonA/B/C
            // are LOW rugged ridges by design — 3-4 sub-peaks, wide,
            // h 2.80-3.60 at scale 1, wider than tall (ratio ~1.8).
            // Same 13-spot ring/bowl positions and rotations as the
            // redesign (MOUNTAINS-PLANET-REDESIGN.md), but wired to
            // the Horizon models. Scale divisors are the Horizon
            // nominal envelope (radius 2.5, height 4.0) — do NOT reuse
            // the old tall-mountain divisors (3.5 / 7.0). Y scale
            // tapers 1.00 (top-left) to 0.80 (right edge) so the
            // horizon stays low and the planet clears it. 13-spot
            // ring = 4,252 tris total.
            AddMountain("Models/HorizonB", new Vector3(-23f, 0f, 10f), 20f, new Vector3(1.4f, 1f, 1.4f));
            AddMountain("Models/HorizonC", new Vector3(-21f, 0f, 15f), 310f, new Vector3(1.33f, 1f, 1.33f));
            AddMountain("Models/HorizonA", new Vector3(-20f, 0f, 5f), 140f, new Vector3(1.4f, 1f, 1.4f));
            AddMountain("Models/HorizonB", new Vector3(-21f, 0f, -4f), 260f, new Vector3(1.4f, 1f, 1.4f));
            AddMountain("Models/HorizonC", new Vector3(-19f, 0f, -13f), 90f, new Vector3(1.4f, 0.95f, 1.4f));
            AddMountain("Models/HorizonA", new Vector3(-14f, 0f, -20f), 200f, new Vector3(1.33f, 0.9f, 1.33f));
            AddMountain("Models/HorizonB", new Vector3(-5f, 0f, -23f), 170f, new Vector3(1.33f, 0.9f, 1.33f));
            AddMountain("Models/HorizonC", new Vector3(5f, 0f, -23f), 15f, new Vector3(1.33f, 0.9f, 1.33f));
            AddMountain("Models/HorizonA", new Vector3(13f, 0f, -20f), 230f, new Vector3(1.26f, 0.85f, 1.26f));
            AddMountain("Models/HorizonB", new Vector3(19f, 0f, -16f), 120f, new Vector3(1.26f, 0.85f, 1.26f));
            AddMountain("Models/HorizonC", new Vector3(23f, 0f, -8f), 45f, new Vector3(1.19f, 0.8f, 1.19f));
            AddMountain("Models/HorizonA", new Vector3(24f, 0f, 1f), 280f, new Vector3(1.19f, 0.8f, 1.19f));
            AddMountain("Models/HorizonB", new Vector3(-17f, 0f, 16f), 180f, new Vector3(1.26f, 0.9f, 1.26f));
        }

        private void AddMountain(string resourcePath, Vector3 position, float rotationY, Vector3 scale)
        {
            GameObject prefab = null;
            try
            {
                prefab = Resources.Load<GameObject>(resourcePath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Mountain model failed to load (" + resourcePath + "): " + e.Message);
                return;
            }
            if (prefab == null) return;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "Mountain";
                go.transform.position = position;
                // Y rotation only, like the buildings/debris/roads: the
                // FBX import has Bake Axis Conversion on, so the -90 deg
                // X axis conversion is baked into the vertices and a Y
                // rotation here is safe. Do not force the rotation to
                // identity (terrain lesson, commit 4186124e).
                go.transform.rotation = Quaternion.Euler(0f, rotationY, 0f);
                go.transform.localScale = scale;
                foreach (var collider in go.GetComponentsInChildren<Collider>())
                    Destroy(collider);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Mountain model failed to place (" + resourcePath + "): " + e.Message);
                if (go != null) Destroy(go);
            }
        }

        // 2026-11-07: alien planet on the horizon — a large banded
        // world (AlienPlanet.fbx in Resources/Models/), low on the
        // -X/-Z horizon so only its top arc shows above the mountains.
        // Centre (-47, -20, -47), radius 26: the centre sits below
        // ground level on purpose — the camera looks down 48.5 deg,
        // so a planet centred on the true horizon would be out of
        // frame. No collider, untappable, no animation. If the FBX is
        // missing, no planet shows.
        // Do NOT set the rotation here: the FBX carries a -90 deg X
        // axis conversion, and the texture's poles are not rotation-
        // invariant — forcing identity while Bake Axis Conversion is
        // off would turn the bands vertical. Leaving the imported
        // rotation alone keeps the bands horizontal (terrain lesson,
        // commit 4186124e).
        private void BuildAlienPlanet()
        {
            GameObject prefab = null;
            try
            {
                prefab = Resources.Load<GameObject>("Models/AlienPlanet");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Alien planet failed to load (Models/AlienPlanet): " + e.Message);
                return;
            }
            if (prefab == null) return;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "AlienPlanet";
                // 2026-11-08 V3 (PLACEMENT-FIX-V3.md): R3 (0,-54,-90)
                // put the planet in the UPPER LEFT in Unity, not the
                // upper right — the Blender conversion mirrored
                // left/right (Unity is left-handed), and a half-size
                // ground hid the occlusion. V3, computed with a
                // camera calibrated against screenshot image-105,
                // puts the disc upper right: bbox x 75.4-99.3%,
                // y 16.1-50.7%, ~24% of frame width, fully below the
                // top bar, fully in frame. Honest caveat: only a thin
                // banded crescent (~7% of the disc) is visible at the
                // top-right edge — the rest is behind the ground,
                // because the 48.5° camera pitch puts the horizon
                // above the frame. Patrick chose this over the 30°
                // camera reframe (CAMERA-PITCH-PREVIEW.md), which
                // gave a full disc but shrank the base to ~35% of
                // the frame — he wants the base prominent. Same FBX
                // + texture; rotation untouched.
                go.transform.position = new Vector3(-137.2f, -68.8f, 4.1f);
                go.transform.localScale = Vector3.one * 1.0f;
                // 2026-11-07: assign the texture at runtime. The FBX
                // import never linked AlienPlanet.png to the material's
                // Base Map (the slot stays empty, and the embedded
                // material reads as read-only in the Inspector, so the
                // texture cannot be assigned by hand). Resources.Load
                // finds the PNG under Resources/Models/, and setting
                // _BaseMap on the instance material bypasses the
                // import entirely.
                var planetRenderer = go.GetComponentInChildren<MeshRenderer>();
                if (planetRenderer != null)
                {
                    Texture2D planetTex = Resources.Load<Texture2D>("Models/AlienPlanet");
                    if (planetTex != null)
                        planetRenderer.material.SetTexture("_BaseMap", planetTex);
                    else
                        Debug.LogWarning("[BaseView] Alien planet texture not found (Models/AlienPlanet).");
                }
                foreach (var collider in go.GetComponentsInChildren<Collider>())
                    Destroy(collider);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Alien planet failed to place (Models/AlienPlanet): " + e.Message);
                if (go != null) Destroy(go);
            }
        }

        // Ground decor must never block a building tap: the tap raycast
        // takes the first hit, so decor colliders are removed on creation.
        // (The ground plane itself keeps its original collider.)
        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void BuildBuildings()
        {
            // Layout: Nexus centre, generators top, barracks bottom — readable
            // from the isometric camera and tappable on a phone screen.
            // 2026-11-05: positions tightened to 0.75x toward the centre so
            // the base fills the portrait frame better (was spread over 13
            // units wide with a lot of empty ground); ground plane and
            // craters shrunk to match. Building sizes unchanged.
            // 2026-11-06 landscape: spread horizontally again — X positions
            // x1.7 (Hydro/Alloy/Marine/Nova at +/-5.1, Lab at -8.3), Z
            // unchanged — so the wider base fills a 16:9 frame the way
            // high-quality games in the genre lay out. Worst
            // building-to-building edge gap is 1.7 (Nexus-Aegis); Lab
            // stays clear of Hydro/Marine (2.0). Ground widened to
            // 56x48 and the camera moved to (10,16,10) to match.
            // Command Nexus (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old blue cylinder so the
            // base still builds when the model file is missing.
            if (!TryAddModelNexus())
                AddBuilding(BuildingId.CommandNexus, new Vector3(0f, 0f, 0f), PrimitiveType.Cylinder, new Vector3(2f, 3f, 2f), new Color(0.15f, 0.35f, 0.75f), "Nexus");
            // Hydroponics Dome (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old green sphere so the
            // base still builds when the model file is missing.
            if (!TryAddModelHydroponicsDome())
                AddBuilding(BuildingId.HydroponicsDome, new Vector3(-5.1f, 0f, 2.6f), PrimitiveType.Sphere, new Vector3(2f, 1.2f, 2f), new Color(0.25f, 0.75f, 0.35f), "Hydro");
            // Alloy Extractor (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old grey cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelAlloyExtractor())
                AddBuilding(BuildingId.AlloyExtractor, new Vector3(5.1f, 0f, 2.6f), PrimitiveType.Cube, new Vector3(1.8f, 1.6f, 1.8f), new Color(0.65f, 0.68f, 0.72f), "Alloy");
            // Helium-3 Harvester (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old cyan cylinder so the
            // base still builds when the model file is missing.
            if (!TryAddModelHeliumHarvester())
                AddBuilding(BuildingId.HeliumHarvester, new Vector3(0f, 0f, 4.1f), PrimitiveType.Cylinder, new Vector3(1.4f, 2.4f, 1.4f), new Color(0.35f, 0.82f, 0.90f), "He-3");
            // Marine Foundry (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old green-grey cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelMarineFoundry())
                AddBuilding(BuildingId.MarineFoundry, new Vector3(-5.1f, 0f, -2.6f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.35f, 0.65f, 0.25f), "Marine");
            // Nova Arsenal (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old red cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelNovaArsenal())
                AddBuilding(BuildingId.NovaArsenal, new Vector3(5.1f, 0f, -2.6f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.85f, 0.35f, 0.20f), "Nova");
            // Aegis Motor Pool (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old grey cube so the base
            // still builds when the model file is missing.
            if (!TryAddModelAegisMotorPool())
                AddBuilding(BuildingId.AegisMotorPool, new Vector3(0f, 0f, -4.1f), PrimitiveType.Cube, new Vector3(2.4f, 1.2f, 2f), new Color(0.55f, 0.55f, 0.60f), "Aegis");
            // Research Lab (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old purple cylinder so
            // the base still builds when the model file is missing.
            // (2026-11-01 note: west side, clear of the other buildings,
            // so it stays tappable at phone size.)
            if (!TryAddModelResearchLab())
                AddBuilding(BuildingId.ResearchLab, new Vector3(-8.3f, 0f, 0f), PrimitiveType.Cylinder, new Vector3(1.8f, 2.2f, 1.8f), new Color(0.55f, 0.30f, 0.85f), "Lab");
        }

        private void AddBuilding(BuildingId id, Vector3 pos, PrimitiveType shape, Vector3 scale, Color color, string shortName)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = shortName + "Building";
            ParentToLayout(go);
            go.transform.localPosition = new Vector3(pos.x, scale.y / 2f, pos.z);
            go.transform.localScale = scale;
            SetPrimitiveColor(go.GetComponent<Renderer>(), color);
            _buildingObjects[id] = go;
            _baseColors[id] = color;

            // A small marker on top so the building reads at phone size.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = shortName + "Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            marker.transform.localScale = new Vector3(0.30f, 0.30f, 0.30f);
            SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);
        }

        /// <summary>Instantiate the Blender Command Nexus FBX at (0,0,0) if it
        /// can be loaded. Returns false (caller falls back to the primitive)
        /// when the model is missing, has no meshes, or fails to instantiate.
        /// The model is built at ground level, ~2 units wide and ~2.9 tall,
        /// so it drops into the exact spot and scale of the old cylinder.
        /// Its imported materials are used as-is (URP Lit on import); only
        /// the white marker sphere still gets the runtime URP Lit material.</summary>
        private bool TryAddModelNexus()
        {
            var prefab = LoadNexusModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "NexusBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one matching the old primitive's
                // footprint (2 wide, 3 tall, centred at y = 1.5).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 1.5f, 0f);
                    box.size = new Vector3(2f, 3f, 2f);
                }

                // Same white marker as the primitives, just above the antenna
                // tip (model top is ~2.9, so 3.15 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "NexusMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 3.15f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.CommandNexus] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.CommandNexus] = go;
                CollectReactorAnimation(go);
                _baseColors[BuildingId.CommandNexus] = new Color(0.15f, 0.35f, 0.75f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Command Nexus model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Find the reactor sphere and containment rings inside
        /// the loaded Nexus model, for the idle animation. Names match the
        /// v3/v4 Blender scripts (Reactor_Core, Containment_Ring_01..04).
        /// Missing parts are simply skipped, so the primitive fallback and
        /// older models animate nothing instead of erroring.</summary>
        private void CollectReactorAnimation(GameObject nexusGo)
        {
            _reactorCore = FindDeep(nexusGo.transform, "Reactor_Core");
            if (_reactorCore != null) _reactorCoreBaseScale = _reactorCore.localScale;
            // Ring speeds in degrees/second: alternating directions, slow.
            float[] speeds = { 12f, -10f, 8f, -14f };
            _reactorRings.Clear();
            _reactorRingSpeeds.Clear();
            for (int i = 0; i < speeds.Length; i++)
            {
                var ring = FindDeep(nexusGo.transform, "Containment_Ring_0" + (i + 1));
                if (ring != null)
                {
                    _reactorRings.Add(ring);
                    _reactorRingSpeeds.Add(speeds[i]);
                }
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Idle reactor motion: the containment rings sweep slowly
        /// around the reactor (gyroscopic — each tilted ring turns about the
        /// world vertical through the reactor centre, alternating
        /// directions), and the core breathes subtly. Runs every frame; a
        /// few transform rotations, and a no-op when the model has no
        /// reactor parts.</summary>
        private void AnimateReactor()
        {
            if (_reactorRings.Count == 0 && _reactorCore == null) return;
            _reactorAnimTime += Time.deltaTime;
            for (int i = 0; i < _reactorRings.Count; i++)
                _reactorRings[i].Rotate(Vector3.up, _reactorRingSpeeds[i] * Time.deltaTime, Space.World);
            if (_reactorCore != null)
            {
                float s = 1f + 0.03f * Mathf.Sin(_reactorAnimTime * 1.5f);
                _reactorCore.localScale = _reactorCoreBaseScale * s;
            }
        }

        /// <summary>Load the Nexus FBX as a prefab. In a built player only
        /// assets under a Resources folder can be loaded by path, so for
        /// Android/iOS builds the FBX must also live at
        /// Assets/_Project/Resources/Models/CommandNexus.fbx. In the Editor
        /// it loads straight from Assets/_Project/Models/, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadNexusModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/CommandNexus");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/CommandNexus.fbx");
#else
            return null;
#endif
        }

        /// <summary>Instantiate the Blender Hydroponics Dome FBX at
        /// (-5.1,0,2.6) if it can be loaded. Returns false (caller falls back
        /// to the primitive) when the model is missing, has no meshes, or
        /// fails to instantiate. The model is built at ground level, ~2
        /// units wide and ~1.24 tall, so it drops into the exact spot and
        /// scale of the old sphere. Its imported materials are used as-is
        /// (URP Lit on import); only the white marker sphere still gets
        /// the runtime URP Lit material.</summary>
        private bool TryAddModelHydroponicsDome()
        {
            var prefab = LoadHydroponicsDomeModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "HydroBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(-5.1f, 0f, 2.6f);
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one matching the old primitive's
                // footprint (2 wide, 1.2 tall, centred at y = 0.6).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.6f, 0f);
                    box.size = new Vector3(2f, 1.2f, 2f);
                }

                // Same white marker as the primitives, just above the vent
                // (model top is ~1.24, so 1.5 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "HydroMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.HydroponicsDome] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.HydroponicsDome] = go;
                _baseColors[BuildingId.HydroponicsDome] = new Color(0.25f, 0.75f, 0.35f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Hydroponics Dome model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Hydroponics Dome FBX as a prefab. In a built
        /// player only assets under a Resources folder can be loaded by
        /// path, so for Android/iOS builds the FBX lives at
        /// Assets/_Project/Resources/Models/HydroponicsDome.fbx. In the
        /// Editor it loads straight from that same path, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadHydroponicsDomeModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/HydroponicsDome");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/HydroponicsDome.fbx");
#else
            return null;
#endif
        }

        /// <summary>Instantiate the Blender Alloy Extractor FBX at
        /// (5.1,0,2.6) if it can be loaded. Returns false (caller falls back
        /// to the primitive) when the model is missing, has no meshes, or
        /// fails to instantiate. The model is built at ground level, ~1.8
        /// units wide and ~1.6 tall, so it drops into the exact spot and
        /// scale of the old cube. Its imported materials are used as-is
        /// (URP Lit on import); only the white marker sphere still gets
        /// the runtime URP Lit material.</summary>
        private bool TryAddModelAlloyExtractor()
        {
            var prefab = LoadAlloyExtractorModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "AlloyBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(5.1f, 0f, 2.6f);
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one matching the old primitive's
                // footprint (1.8 wide, 1.6 tall, centred at y = 0.8).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.8f, 0f);
                    box.size = new Vector3(1.8f, 1.6f, 1.8f);
                }

                // Same white marker as the primitives, just above the stack
                // tip (model top is ~1.6, so 1.9 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "AlloyMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 1.9f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.AlloyExtractor] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.AlloyExtractor] = go;
                _baseColors[BuildingId.AlloyExtractor] = new Color(0.65f, 0.68f, 0.72f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Alloy Extractor model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Alloy Extractor FBX as a prefab. In a built
        /// player only assets under a Resources folder can be loaded by
        /// path, so for Android/iOS builds the FBX lives at
        /// Assets/_Project/Resources/Models/AlloyExtractor.fbx. In the
        /// Editor it loads straight from that same path, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadAlloyExtractorModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/AlloyExtractor");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/AlloyExtractor.fbx");
#else
            return null;
#endif
        }

        /// <summary>Instantiate the Blender Helium-3 Harvester FBX at
        /// (0,0,4.1) if it can be loaded. Returns false (caller falls back
        /// to the primitive) when the model is missing, has no meshes, or
        /// fails to instantiate. The model is built at ground level, ~1.4
        /// units wide and ~2.4 tall, so it drops into the exact spot and
        /// scale of the old cylinder. Its imported materials are used as-is
        /// (URP Lit on import); only the white marker sphere still gets
        /// the runtime URP Lit material.</summary>
        private bool TryAddModelHeliumHarvester()
        {
            var prefab = LoadHeliumHarvesterModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "He3Building";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 4.1f);
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one matching the old primitive's
                // footprint (1.4 wide, 2.4 tall, centred at y = 1.2).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 1.2f, 0f);
                    box.size = new Vector3(1.4f, 2.4f, 1.4f);
                }

                // Same white marker as the primitives, just above the glowing
                // orb (model top is ~2.42, so 2.7 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "He3Marker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 2.7f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.HeliumHarvester] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.HeliumHarvester] = go;
                _baseColors[BuildingId.HeliumHarvester] = new Color(0.35f, 0.82f, 0.90f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Helium-3 Harvester model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Helium-3 Harvester FBX as a prefab. In a built
        /// player only assets under a Resources folder can be loaded by
        /// path, so for Android/iOS builds the FBX lives at
        /// Assets/_Project/Resources/Models/HeliumHarvester.fbx. In the
        /// Editor it loads straight from that same path, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadHeliumHarvesterModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/HeliumHarvester");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/HeliumHarvester.fbx");
#else
            return null;
#endif
        }

        /// <summary>Instantiate the Blender Marine Foundry FBX at (-5.1,0,-2.6)
        /// if it can be loaded. Returns false (caller falls back to the
        /// primitive) when the model is missing, has no meshes, or fails to
        /// instantiate. The model is built at ground level, ~2 units wide
        /// and ~1.47 tall, so it drops into the exact spot and scale of the
        /// old cube. Its imported materials are used as-is (URP Lit on
        /// import); only the white marker sphere still gets the runtime URP
        /// Lit material.</summary>
        private bool TryAddModelMarineFoundry()
        {
            var prefab = LoadMarineFoundryModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "MarineBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(-5.1f, 0f, -2.6f);
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one matching the old primitive's
                // footprint (2 wide, 1.4 tall, centred at y = 0.7).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.7f, 0f);
                    box.size = new Vector3(2f, 1.4f, 2f);
                }

                // Same white marker as the primitives, just above the flag
                // (model top is ~1.47, so 1.7 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "MarineMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 1.7f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.MarineFoundry] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.MarineFoundry] = go;
                _baseColors[BuildingId.MarineFoundry] = new Color(0.35f, 0.65f, 0.25f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Marine Foundry model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Marine Foundry FBX as a prefab. In a built
        /// player only assets under a Resources folder can be loaded by
        /// path, so for Android/iOS builds the FBX lives at
        /// Assets/_Project/Resources/Models/MarineFoundry.fbx. In the
        /// Editor it loads straight from that same path, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadMarineFoundryModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/MarineFoundry");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/MarineFoundry.fbx");
#else
            return null;
#endif
        }

        /// <summary>Instantiate the Blender Nova Arsenal FBX at (5.1,0,-2.6)
        /// if it can be loaded. Returns false (caller falls back to the
        /// primitive) when the model is missing, has no meshes, or fails to
        /// instantiate. The model is built at ground level, ~2 units wide
        /// and ~1.86 tall (spire tip and barrel muzzle clear the 1.4 block),
        /// so it drops into the exact spot and scale of the old cube. Its
        /// imported materials are used as-is (URP Lit on import); only the
        /// white marker sphere still gets the runtime URP Lit material.</summary>
        private bool TryAddModelNovaArsenal()
        {
            var prefab = LoadNovaArsenalModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "NovaBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(5.1f, 0f, -2.6f);
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one covering the block and the
                // spire/barrel above it (2 wide, 1.9 tall, centred at
                // y = 0.95).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.95f, 0f);
                    box.size = new Vector3(2f, 1.9f, 2f);
                }

                // Same white marker as the primitives, just above the spire
                // tip (model top is ~1.86, so 2.1 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "NovaMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 2.1f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.NovaArsenal] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.NovaArsenal] = go;
                _baseColors[BuildingId.NovaArsenal] = new Color(0.85f, 0.35f, 0.20f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Nova Arsenal model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Nova Arsenal FBX as a prefab. In a built
        /// player only assets under a Resources folder can be loaded by
        /// path, so for Android/iOS builds the FBX lives at
        /// Assets/_Project/Resources/Models/NovaArsenal.fbx. In the
        /// Editor it loads straight from that same path, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadNovaArsenalModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/NovaArsenal");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/NovaArsenal.fbx");
#else
            return null;
#endif
        }

        private bool TryAddModelAegisMotorPool()
        {
            var prefab = LoadAegisMotorPoolModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "AegisBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, -4.1f);
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one covering the garage block,
                // the antenna above it, and the rover parked out front on
                // the camera-facing side (2.4 wide, 1.7 tall, 2.8 deep,
                // centred at y = 0.85, z = 0.2 toward the rover).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.85f, 0.2f);
                    box.size = new Vector3(2.4f, 1.7f, 2.8f);
                }

                // Same white marker as the primitives, just above the antenna
                // tip (model top is ~1.67, so 1.9 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "AegisMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 1.9f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.AegisMotorPool] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.AegisMotorPool] = go;
                _baseColors[BuildingId.AegisMotorPool] = new Color(0.55f, 0.55f, 0.60f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Aegis Motor Pool model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Aegis Motor Pool FBX as a prefab. In a built
        /// player only assets under a Resources folder can be loaded by
        /// path, so for Android/iOS builds the FBX lives at
        /// Assets/_Project/Resources/Models/AegisMotorPool.fbx. In the
        /// Editor it loads straight from that same path, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadAegisMotorPoolModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/AegisMotorPool");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/AegisMotorPool.fbx");
#else
            return null;
#endif
        }

        private bool TryAddModelResearchLab()
        {
            var prefab = LoadResearchLabModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "LabBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(-8.3f, 0f, 0f);
                go.transform.localScale = Vector3.one;

                // Capture the model's part renderers BEFORE adding the marker,
                // so Highlight() recolours the model parts but not the marker.
                // Held in locals until the end: if anything below fails, the
                // fallback primitive must not inherit a half-built model entry.
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                var list = new List<Renderer>();
                var partColors = new Dictionary<Renderer, Color>();
                foreach (var rend in renderers)
                {
                    list.Add(rend);
                    // Accessing .material makes a per-renderer copy, so
                    // Highlight() never touches the shared imported material.
                    partColors[rend] = rend.material.color;
                }

                // The FBX has no collider, and tapping raycasts against
                // colliders — give the model one covering the tower, dome
                // and antenna above it (2.2 wide, 2.3 tall, 2.2 deep,
                // centred at y = 1.15).
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 1.15f, 0f);
                    box.size = new Vector3(2.2f, 2.3f, 2.2f);
                }

                // Same white marker as the primitives, just above the
                // antenna tip (model top is ~2.24, so 2.5 sits on it).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "LabMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 2.5f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.ResearchLab] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.ResearchLab] = go;
                _baseColors[BuildingId.ResearchLab] = new Color(0.55f, 0.30f, 0.85f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Research Lab model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Research Lab FBX as a prefab. In a built
        /// player only assets under a Resources folder can be loaded by
        /// path, so for Android/iOS builds the FBX lives at
        /// Assets/_Project/Resources/Models/ResearchLab.fbx. In the
        /// Editor it loads straight from that same path, so Editor Play
        /// shows the model without moving the file.</summary>
        private static GameObject LoadResearchLabModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/ResearchLab");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/ResearchLab.fbx");
#else
            return null;
#endif
        }

        // Android magenta fix (2026-11-02): CreatePrimitive's default
        // material rendered as Unity's missing-shader magenta on the
        // phone, even though it looked coloured in the Editor. Assign an
        // explicit URP Lit material to every runtime primitive instead,
        // so the intended colour is carried by a shader that is present
        // in the URP Android build. Fall back to the primitive's own
        // material if URP Lit cannot be found (e.g. a Built-in project).
        private static void SetPrimitiveColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var material = new Material(shader);
                SetMaterialColor(material, color);
                renderer.material = material;
            }
            else
            {
                SetMaterialColor(renderer.material, color);
            }
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        private void Update()
        {
            // The reactor animation runs every frame, unlike the tap
            // handling below, which returns early when nothing is tapped.
            AnimateReactor();
            // Input System note: Unity throws InvalidOperationException if
            // the legacy UnityEngine.Input API is read while Player Settings
            // > Active Input Handler is set to Input System Package only.
            // When the new Input System is enabled, read Mouse/Touchscreen
            // instead; otherwise keep the legacy path. This keeps the same
            // script working in Input Manager, Both, and Input System only.
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
            // Tap on a building opens its panel; a tap anywhere else on
            // the base (empty ground, or a ray that hits nothing) hides
            // any building panel that is showing. UI taps never reach
            // here — EventSystem filters them out above.
            bool hitBuilding = false;
            if (Physics.Raycast(ray, out var hit, 100f))
            {
                foreach (var pair in _buildingObjects)
                {
                    if (hit.transform == pair.Value.transform || hit.transform.IsChildOf(pair.Value.transform))
                    {
                        var def = _controller.BuildingDefinitions.Find(d => d.Id == pair.Key);
                        if (def != null) OnBuildingSelected?.Invoke(def);
                        hitBuilding = true;
                        break;
                    }
                }
            }
            if (!hitBuilding) OnEmptyBaseTapped?.Invoke();
        }

        public void SetVisible(bool visible)
        {
            foreach (var go in _buildingObjects.Values) go.SetActive(visible);
        }

        /// <summary>Highlight the selected building; locked buildings stay dim.</summary>
        public void Highlight(BuildingId selected, bool isLocked)
        {
            foreach (var pair in _buildingObjects)
            {
                // Blender-model building: recolour each part from its own
                // stored colour, so the dome, ring and tower keep their own
                // colours instead of all flattening to the Nexus blue.
                if (_modelRenderers.TryGetValue(pair.Key, out var modelRends))
                {
                    foreach (var rend in modelRends)
                    {
                        if (rend == null) continue;
                        if (!_modelBaseColors.TryGetValue(rend, out var partColor)) continue;
                        if (pair.Key == selected)
                            SetMaterialColor(rend.material, isLocked ? new Color(0.4f, 0.4f, 0.4f) : partColor * 1.4f);
                        else
                            SetMaterialColor(rend.material, partColor);
                    }
                    continue;
                }

                var rend0 = pair.Value.GetComponent<Renderer>();
                if (rend0 == null) continue;
                if (pair.Key == selected)
                    SetMaterialColor(rend0.material, isLocked ? new Color(0.4f, 0.4f, 0.4f) : _baseColors[pair.Key] * 1.4f);
                else
                    SetMaterialColor(rend0.material, _baseColors[pair.Key]);
            }
        }
    }
}
