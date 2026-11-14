using System;
using System.Collections.Generic;
using LunarDominion.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        // Horizon mountains (2026-11-09): the 13 HorizonA/B/C models
        // that frame the base view. Tracked so the Map tab can hide them
        // (they crowd the big map) while the Base tab keeps them.
        private readonly List<GameObject> _mountains = new List<GameObject>();
        private GameController _controller;
        // Rotated layout root (2026-11-06): buildings, wall and decor
        // are children of this object, so the whole base turns 45 degrees
        // while the ground plane and mountains stay unrotated.
        private GameObject _layoutRoot;
        // Base environment pieces the Map tab hides (see
        // SetEnvironmentVisible): the paved interior plane and
        // every wall piece. (The ground mesh is not among them —
        // since 2026-11-14 it never shows at all; see BuildGround.)
        private GameObject _pavedGo;
        private readonly System.Collections.Generic.List<GameObject> _wallPieces =
            new System.Collections.Generic.List<GameObject>();
        // Reactor animation (2026-11-06): the Nexus FBX keeps the reactor
        // sphere and each containment ring as separate named objects, so
        // they can move independently. Collected after the model loads;
        // empty when the model is missing or is the primitive fallback.
        private Transform _reactorCore;
        private Vector3 _reactorCoreBaseScale = Vector3.one;
        private readonly List<Transform> _reactorRings = new List<Transform>();
        private readonly List<float> _reactorRingSpeeds = new List<float>();
        private float _reactorAnimTime;

        // Hospital alert (2026-11-09): a red pulsing sphere above the
        // Hospital when injured units are waiting to be healed. Created
        // lazily (the Hospital may be the FBX model or the primitive
        // fallback, which have different scales) and hidden when there
        // are no injured units. The white marker stays as-is; the red
        // alert sits above it.
        private GameObject _hospitalAlert;
        private Vector3 _hospitalAlertBaseScale = new Vector3(0.5f, 0.5f, 0.5f);

        // Base camera pan (2026-11-09, expanded base): the base is now
        // wider than the screen, so the camera can be panned across it
        // — arrow keys on PC, touch/mouse drag on the phone (and in the
        // Editor). Same pattern as the Map view (ExteriorView): the pan
        // offset moves both the camera position and its look-at point,
        // so the 48.5-degree pitch never changes. Clamped so the camera
        // cannot leave the ground.
        private Vector3 _panOffset = Vector3.zero;
        private static readonly float PanSpeed = 40f; // units/second, arrow keys
        private static readonly float PanLimitX = 30f;
        private static readonly float PanLimitZ = 30f;
        private static readonly float DragTapThresholdPx = 15f; // below this, a press is a tap, not a drag
        // The Base camera sits at (23.2,43.5,23.2) on the x=z diagonal, so
        // screen-right is world (0.707,0,-0.707) and screen-up on the
        // ground is (-0.707,0,-0.707) — same as the Map camera.
        private static readonly Vector3 CamRight = new Vector3(0.7071f, 0f, -0.7071f);
        private static readonly Vector3 CamGroundForward = new Vector3(-0.7071f, 0f, -0.7071f);
        private static readonly Vector3 BaseCameraPosition = new Vector3(17f, 31.9f, 17f);
        // Drag state: a press that moves past the threshold becomes a
        // drag (pan) and must NOT also fire as a tap on release.
        // True while the Base tab is the view on screen. Set by
        // SetVisible; Update() ignores keyboard/pointer input while
        // false (see the gate in Update).
        private bool _visible = true;
        private bool _pointerDown;
        private bool _isDrag;
        private Vector2 _pointerStartPos;
        private Vector2 _pointerLastPos;

        public void Build(GameController controller)
        {
            _controller = controller;
            // 2026-11-06: rotate the base 45 degrees so the wall perimeter
            // reads parallel to the screen's horizontal borders. The camera
            // sits at (23.2,43.5,23.2), so screen-right is world (0.707,0,-0.707);
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
            // 2026-11-13 base redesign (Patrick's call: the concept
            // image's wider framing): camera (17, 31.9, 17), pitch
            // 53 degrees, over the 132x132 ground. Checked with real
            // camera math (60-degree vertical FOV, 16:9; the
            // projection was calibrated against the old layout,
            // reproducing Lab / Hospital at NDC x -0.947 / +0.947).
            // First cut (23.2, 43.5, 23.2) framed the whole 72x44
            // wall (corners at max |NDC x| 0.85); Patrick's Play
            // check read it as too far out, so the camera moved in
            // to distance 40 (from 54.5) at his direction — the wall
            // corners now crop out of frame (max |NDC x| ~1.16),
            // which he explicitly accepted ('don't worry about the
            // outside wall'); every building stays in frame
            // (max |NDC x| ~0.73). The
            // terrain and the mountain ring grew in the same pass so
            // every frame edge is still covered by ground or
            // mountains. The layout turns 45 degrees under BaseLayout.
            // (Supersedes the 2026-11-06 (10,16,10) framing and its
            // base-prominence rule, at Patrick's direction.)
            // 2026-11-09 expanded base: the camera position is now
            // ApplyCamera() (base position + pan offset), so the player
            // can scroll across the wider base.
            ApplyCamera();
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            // 2026-11-09 lighting upgrade: brighter, slightly warm sun
            // (was 1.25 white) so the base reads vivid and the shadows
            // have definition, and a stronger cool fill (was 0.35) so
            // the faces turned away from the sun stay readable.
            light.intensity = 1.5f;
            light.color = new Color(1.0f, 0.96f, 0.88f);
            lightGo.transform.position = new Vector3(5f, 10f, 0f);
            lightGo.transform.LookAt(Vector3.zero);

            // Phase 5 fill light: a soft cool light from the opposite
            // side, so buildings are readable from the isometric camera
            // instead of having one fully dark face. Primitives only —
            // no final art, no new models.
            var fillGo = new GameObject("Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.5f;
            fill.color = new Color(0.8f, 0.85f, 1f);
            fillGo.transform.position = new Vector3(-5f, 8f, -5f);
            fillGo.transform.LookAt(Vector3.zero);

            // 2026-11-09 lighting upgrade: ambient + bloom.
            // Ambient: brighten the scene's trilight ambient (sky /
            // equator / ground) to a cool moonlight/earthshine so the
            // shadowed faces are lifted without going flat. The scene
            // (Main.unity) already uses Trilight ambient; these values
            // replace its dark defaults. (AmbientMode.Trilight, not
            // "Gradient" — Gradient is not an AmbientMode value;
            // Trilight is the sky/equator/ground mode.)
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.33f, 0.40f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.24f, 0.29f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.13f, 0.16f);
            // Bloom: a global Volume with a Bloom override, so the
            // emissive materials (reactor rings, glowing windows,
            // hazard bars, bollard tips, snow caps) glow instead of
            // rendering as flat bright colour. The DefaultVolumeProfile
            // in the project already has a Bloom override but its
            // intensity is 0 (effectively off); this runtime Volume
            // (priority 10) overrides it. Threshold 0.9 keeps the
            // lit (non-emissive) surfaces from blooming — only the
            // emissive parts (strength 1.0-3.0 in HDR) exceed it.
            // Post-processing is enabled on the camera explicitly,
            // since the camera is created here in code.
            var camData = cam.GetUniversalAdditionalCameraData();
            if (camData != null) camData.renderPostProcessing = true;
            var bloomGo = new GameObject("Bloom Volume");
            var bloomVolume = bloomGo.AddComponent<Volume>();
            bloomVolume.isGlobal = true;
            bloomVolume.priority = 10f;
            var bloomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = bloomProfile.Add<Bloom>();
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.9f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.5f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.7f;
            bloomVolume.profile = bloomProfile;

            // Ground 132 wide by 132 deep (2026-11-13 redesign;
            // was 56x48), so it runs past all four frame edges from
            // the (23.2, 43.5, 23.2) camera — the whole screen is
            // terrain, with no black bars around it. The wall sits
            // at layout x +/-36, z +/-22 and the mountains ring the
            // far edges, all on this ground.
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
            // 2026-11-13 redesign: two rocks on the plaza aprons
            // inside the wall, eight in the outer band beyond the
            // new wall (layout x +/-36, z +/-22) — the outer eight
            // have matching flat pads in BuildTerrain.py (world
            // conversions of these layout spots).
            AddRock(new Vector3(-14f, 0f, 0f), 0.4f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(14f, 0f, 3f), 0.45f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(40f, 0f, 10f), 0.5f, new Color(0.38f, 0.4f, 0.44f));
            AddRock(new Vector3(20f, 0f, 26f), 0.45f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(27f, 0f, 24f), 0.5f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(38f, 0f, 20f), 0.4f, new Color(0.33f, 0.35f, 0.39f));
            AddRock(new Vector3(34f, 0f, 27f), 0.5f, new Color(0.38f, 0.4f, 0.44f));
            AddRock(new Vector3(13f, 0f, 30f), 0.45f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(-27f, 0f, 27f), 0.5f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(41f, 0f, 3f), 0.5f, new Color(0.33f, 0.35f, 0.39f));

            // 2026-11-07: Blender debris models (rubble / rocks /
            // shards), placed in world space like the ground plane.
            AddDebris();

            // 2026-11-07: Blender props, placed in world space
            // like the debris. The gate needs the wall gap (BuildWall).
            // (Roads removed 2026-11-09 — see AddProps.)
            AddProps();

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
            AddDebrisAt("Models/DebrisShards", new Vector3(-14f, 0f, 20f), 260f);
            AddDebrisAt("Models/DebrisRubble", new Vector3(20f, 0f, 8f), 310f);
            AddDebrisAt("Models/DebrisRubble", new Vector3(13f, 0f, -9f), 25f);
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

        // 2026-11-07: props — Blender prop models (PropSolarPanel /
        // PropCrates / PropStorageTank / PropRover / PropGate) in
        // Resources/Models/. Placed in world space (like the debris,
        // not under the rotated layout root) at fixed positions from
        // the layout plan. No colliders and no tap registration:
        // props are scenery. If an FBX is missing, that spot stays
        // empty. 2026-11-09 expanded base: props re-spread for the
        // 2.5x spread, clear of the buildings; the gate moves to the
        // new north wall gap (local (0,13.5) -> world (9.55,9.55)).
        // 2026-11-09: the ROADS are gone (removed at Patrick's
        // request — the black low-res road ring/spine/spur clashed
        // with the new paved interior, and the painted walkways in
        // the paving already connect the buildings; the outer wall
        // is the boundary now). Only the props remain.
        private void AddProps()
        {
            // Props, re-spread for the redesigned base (world
            // space, clear of the buildings and roads).
            // 2026-11-13 redesign: props re-spread for the new
            // layout (world conversions of layout spots on the
            // aprons, clear of the roads and buildings); the gate
            // moves to the new EAST wall gap (layout (36, 0) ->
            // world (25.46, -25.46)), rotated to span the gap.
            AddSceneryAt("Models/PropSolarPanel", new Vector3(-5.66f, 0f, 12.73f), 30f, "Prop");
            AddSceneryAt("Models/PropSolarPanel", new Vector3(5.66f, 0f, -12.73f), -20f, "Prop");
            AddSceneryAt("Models/PropSolarPanel", new Vector3(-12.73f, 0f, 5.66f), -30f, "Prop");
            AddSceneryAt("Models/PropCrates", new Vector3(20.51f, 0f, -13.44f), 15f, "Prop");
            AddSceneryAt("Models/PropCrates", new Vector3(-20.51f, 0f, 13.44f), 70f, "Prop");
            AddSceneryAt("Models/PropStorageTank", new Vector3(-13.44f, 0f, 20.51f), 0f, "Prop");
            AddSceneryAt("Models/PropStorageTank", new Vector3(13.44f, 0f, -20.51f), 0f, "Prop");
            AddSceneryAt("Models/PropRover", new Vector3(-8.49f, 0f, 4.24f), -45f, "Prop");
            AddSceneryAt("Models/PropGate", new Vector3(25.46f, 0f, -25.46f), -45f, "Prop");
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
                // Moon sand removed from the base (2026-11-14,
                // Patrick): the terrain never shows in the base
                // view — the compound sits on the black paving
                // over the dark backdrop. (The Map tab has its own
                // exterior terrain; this mesh was only ever the
                // base's own ground.)
                terrain.SetActive(false);
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
                ground.SetActive(false); // moon sand removed — see above
                ground.transform.localScale = new Vector3(13.2f, 1f, 13.2f);
                SetPrimitiveColor(ground.GetComponent<Renderer>(), new Color(0.27f, 0.29f, 0.33f));
                groundRenderer = ground.GetComponent<Renderer>();
            }

            // Regolith texture (GroundRegolith.png in
            // Resources/Textures/). If it loads, it replaces the flat
            // colour: the PNG is authored in sRGB at the ground's
            // colour, so the material colour goes to white — colour x
            // texture would double-darken it. Tiled (19,19) across
            // the 132x132 ground (2026-11-13; was (8,7) across
            // 56x48 — same texel density), Wrap Mode Repeat. If the
            // PNG is missing,
            // the flat colour above stays (plane) or the FBX material
            // keeps its own texture reference (terrain).
            var regolith = Resources.Load<Texture2D>("Textures/GroundRegolith");
            if (regolith != null && groundRenderer != null)
            {
                regolith.wrapMode = TextureWrapMode.Repeat;
                groundRenderer.material.mainTexture = regolith;
                groundRenderer.material.mainTextureScale = new Vector2(19f, 19f);
                groundRenderer.material.color = Color.white;
            }
            // Ground normal map (GroundRegolith_Normal.png in
            // Resources/Textures/, from BuildGroundTextureV2.py). Same
            // (19,19) tiling as the colour texture so the relief stays
            // in register with the pebbles/pocks. URP Lit reads it
            // from _BumpMap. The PNG must be imported as Texture Type
            // "Normal Map" in Unity or it unpacks wrong. If the PNG
            // is missing (v1 ground), the ground just stays flat —
            // the colour texture above still applies.
            var regolithNormal = Resources.Load<Texture2D>("Textures/GroundRegolith_Normal");
            if (regolithNormal != null && groundRenderer != null)
            {
                regolithNormal.wrapMode = TextureWrapMode.Repeat;
                groundRenderer.material.SetTexture("_BumpMap", regolithNormal);
                groundRenderer.material.SetTextureScale("_BumpMap", new Vector2(19f, 19f));
            }
            BuildPavedInterior();
        }

        // Paved interior: the ground inside the base walls is a
        // paved spaceport floor (concrete plates, Nexus plaza, a
        // painted road network, gate hazard stripes), while the
        // ground outside the walls stays the normal moon regolith.
        // A flat plane (48x27 originally; 69x42 since the 2026-11-13
        // redesign), child of the layout root (so it rotates with
        // the base), at local y 0.02 — just above the terrain's flat
        // interior, so it covers the regolith inside the walls without
        // z-fighting. The paved texture (GroundPaved.png, from
        // BuildGroundPaved.py) is one tile stretched across the whole
        // interior, with the markings positioned to line up with the
        // building spots. The normal map (GroundPaved_Normal.png)
        // gives the plate seams real relief. If either PNG is missing
        // the interior just stays regolith — no error, no crash.
        // No collider: the plane is visual only; taps go to buildings.
        private void BuildPavedInterior()
        {
            Texture2D paved = null;
            try
            {
                paved = Resources.Load<Texture2D>("Textures/GroundPaved");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Paved interior texture failed to load (Textures/GroundPaved): " + e.Message);
                return;
            }
            if (paved == null) return;
            // Clamp, don't wrap: the hexagonal paving mesh's UVs run
            // past the texture's z edges at the hexagon's north /
            // south points, and clamping stretches the black edge
            // rows there instead of wrapping road texels in.
            paved.wrapMode = TextureWrapMode.Clamp;
            try
            {
                var plane = new GameObject("PavedInterior");
                _pavedGo = plane;
                plane.transform.SetParent(_layoutRoot.transform, false);
                plane.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                // 2026-11-14: the paving is a hexagonal mesh now,
                // matching the hexagonal wall (BuildWall) — the old
                // rectangular plane's corners would poke outside the
                // wall's diagonal sides. The hexagon is the wall
                // hexagon scaled out by 1.04 (a small apron past the
                // wall's outer face). UVs keep the layout-space
                // mapping the scaled plane produced (u = (lx + 34.5)
                // / 69, v = (lz + 21) / 42), so the painted roads,
                // plaza and hazard panel land exactly where they
                // did. Normals are set to up by hand (the floor is
                // flat) and the material is set double-sided below,
                // so the fan's winding cannot hide or darken it.
                var corners = new[]
                {
                    new Vector2(36f, 13f), new Vector2(0f, 26f),
                    new Vector2(-36f, 13f), new Vector2(-36f, -13f),
                    new Vector2(0f, -26f), new Vector2(36f, -13f),
                };
                var verts = new Vector3[7];
                var uvs = new Vector2[7];
                var normals = new Vector3[7];
                verts[0] = Vector3.zero;
                uvs[0] = new Vector2(0.5f, 0.5f);
                normals[0] = Vector3.up;
                for (int i = 0; i < 6; i++)
                {
                    var c = corners[i] * 1.04f;
                    verts[i + 1] = new Vector3(c.x, 0f, c.y);
                    uvs[i + 1] = new Vector2((c.x + 34.5f) / 69f, (c.y + 21f) / 42f);
                    normals[i + 1] = Vector3.up;
                }
                var tris = new int[18];
                for (int i = 0; i < 6; i++)
                {
                    tris[i * 3] = 0;
                    tris[i * 3 + 1] = i + 1;
                    tris[i * 3 + 2] = (i + 1) % 6 + 1;
                }
                var mesh = new Mesh { name = "PavedInteriorMesh" };
                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.normals = normals;
                mesh.triangles = tris;
                plane.AddComponent<MeshFilter>().mesh = mesh;
                var rend = plane.AddComponent<MeshRenderer>();
                // Explicit URP Lit material (2026-11-10, phone magenta
                // fix): a fresh renderer's default material
                // uses the Standard shader, which renders magenta in a
                // URP build (the phone) even though it looks fine in
                // the Editor. SetPrimitiveColor creates the URP Lit
                // material (same as the ground plane above and the
                // march marker), then the texture goes on that material.
                // White so the paving shows at full brightness.
                SetPrimitiveColor(rend, Color.white);
                // URP Lit, texture as _BaseMap. The PNG is authored in
                // sRGB at the paving's colour, so the material colour
                // goes to white — colour x texture would double-darken.
                // One tile across the whole plane (no repeat): the
                // markings are positioned, not tiled.
                rend.material.mainTexture = paved;
                rend.material.mainTextureScale = Vector2.one;
                rend.material.color = Color.white;
                var pavedNormal = Resources.Load<Texture2D>("Textures/GroundPaved_Normal");
                if (pavedNormal != null)
                {
                    rend.material.SetTexture("_BumpMap", pavedNormal);
                    rend.material.SetTextureScale("_BumpMap", Vector2.one);
                }
                // Double-sided (URP Lit _Cull = Off): the
                // hand-built fan's winding is not relied on.
                rend.material.SetInt("_Cull", 0);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Paved interior failed to place: " + e.Message);
            }
        }

        // Perimeter wall palette (2026-11-14 hexagonal rebuild).
        private static readonly Color WallBaseColor = new Color(0.32f, 0.35f, 0.42f);
        private static readonly Color WallBandColor = new Color(0.27f, 0.30f, 0.37f);
        private static readonly Color WallTowerColor = new Color(0.25f, 0.28f, 0.36f);
        private static readonly Color WallCapColor = new Color(0.30f, 0.33f, 0.40f);
        private static readonly Color WallStripColor = new Color(0.38f, 0.85f, 1.0f);
        private static readonly Color LampHeadColor = new Color(1.0f, 0.93f, 0.80f);
        private static readonly Color LampPostColor = new Color(0.16f, 0.17f, 0.20f);

        // Perimeter wall around the building area.
        // 2026-11-14 (Patrick: "the square wall is not realistic in
        // this setting"): the perimeter is a HEXAGON — flat east /
        // west sides at x +/-36 spanning z -13..13 (so the gate and
        // the main street stay exactly where the 2026-11-13 redesign
        // put them) and points north / south at z +/-26, joined by
        // four diagonal sides. The diagonals clear the corner
        // buildings: at layout x +/-21 the diagonal line sits at
        // |z| 18.4, the buildings at (+/-21, +/-14). Each run is
        // built in three stepped layers for depth (see AddWallRun),
        // with a tower + glowing beacon at each of the six vertices
        // and lamp posts inside the wall (two flanking the gate).
        // Before this the wall was a rectangle x +/-36, z +/-22 of
        // single 0.35-thick boxes with a cyan strip.
        // No colliders, same as the decor, so taps are never swallowed.
        private void BuildWall()
        {
            // The east side is split into two runs for the 3.4-wide
            // gate gap at local z 0.
            AddWallRun(36f, -13f, 36f, -1.7f);
            AddWallRun(36f, 1.7f, 36f, 13f);
            AddWallRun(36f, 13f, 0f, 26f);
            AddWallRun(0f, 26f, -36f, 13f);
            AddWallRun(-36f, 13f, -36f, -13f);
            AddWallRun(-36f, -13f, 0f, -26f);
            AddWallRun(0f, -26f, 36f, -13f);

            // Vertex towers, each capped and topped with a glowing
            // cyan beacon.
            foreach (var v in new[]
            {
                new Vector2(36f, 13f), new Vector2(0f, 26f),
                new Vector2(-36f, 13f), new Vector2(-36f, -13f),
                new Vector2(0f, -26f), new Vector2(36f, -13f),
            })
            {
                AddWallBox(new Vector3(v.x, 1.3f, v.y), new Vector3(2.2f, 2.6f, 2.2f), WallTowerColor);
                AddWallBox(new Vector3(v.x, 2.75f, v.y), new Vector3(2.7f, 0.3f, 2.7f), WallCapColor);
                AddWallBox(new Vector3(v.x, 3.05f, v.y), new Vector3(0.5f, 0.3f, 0.5f), WallStripColor);
            }

            // Lamp posts inside the wall: one at each side's
            // midpoint, and one in each east half-run so two lamps
            // flank the gate.
            AddLampForRun(36f, -13f, 36f, -1.7f);
            AddLampForRun(36f, 1.7f, 36f, 13f);
            AddLampForRun(36f, 13f, 0f, 26f);
            AddLampForRun(0f, 26f, -36f, 13f);
            AddLampForRun(-36f, 13f, -36f, -13f);
            AddLampForRun(-36f, -13f, 0f, -26f);
            AddLampForRun(0f, -26f, 36f, -13f);
        }

        // One hexagonal wall run from (ax, az) to (bx, bz), in
        // three stepped layers for depth: a thick base (1.7 thick,
        // 0.85 high), a recessed upper band (1.05 thick, 0.75 high)
        // and an overhanging cap (2.0 thick, 0.22 high — total wall
        // height 1.82), plus the cyan strip light along the crest
        // and buttresses standing proud of the outer face.
        private void AddWallRun(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            // Box local +X must align with the run direction; a
            // Unity Y-rotation maps +X to (cos, 0, -sin).
            float yaw = Mathf.Atan2(-dz, dx) * Mathf.Rad2Deg;
            var mid = new Vector3((ax + bx) * 0.5f, 0f, (az + bz) * 0.5f);
            AddWallBox(mid + new Vector3(0f, 0.425f, 0f), new Vector3(len, 0.85f, 1.7f), WallBaseColor, yaw);
            AddWallBox(mid + new Vector3(0f, 1.225f, 0f), new Vector3(len, 0.75f, 1.05f), WallBandColor, yaw);
            AddWallBox(mid + new Vector3(0f, 1.71f, 0f), new Vector3(len, 0.22f, 2.0f), WallCapColor, yaw);
            AddWallBox(mid + new Vector3(0f, 1.85f, 0f), new Vector3(len - 0.4f, 0.06f, 0.18f), WallStripColor, yaw);
            // Buttresses on the outer face, evenly spaced.
            var outward = new Vector3(-dz, 0f, dx).normalized;
            if (Vector3.Dot(outward, mid) < 0f) outward = -outward;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 12f));
            for (int i = 1; i <= n; i++)
            {
                float t = i / (n + 1f);
                var c = new Vector3(ax + dx * t, 0f, az + dz * t) + outward * 1.3f;
                AddWallBox(c + new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 1.1f, 1.5f), WallTowerColor, yaw);
            }
        }

        // A lamp post just inside a wall run, at the run's
        // midpoint: a slim dark post with a warm-white head — the
        // light structures on the wall (2026-11-14). It stands
        // 1.65 inside the wall centreline, clear of the roads (the
        // ring road's outer edge is 2.5+ from every wall line).
        private void AddLampForRun(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            var mid = new Vector3((ax + bx) * 0.5f, 0f, (az + bz) * 0.5f);
            var inward = new Vector3(-dz, 0f, dx).normalized;
            if (Vector3.Dot(inward, mid) > 0f) inward = -inward;
            var p = mid + inward * 1.65f;
            AddWallBox(new Vector3(p.x, 1.15f, p.z), new Vector3(0.16f, 2.3f, 0.16f), LampPostColor);
            AddWallBox(new Vector3(p.x, 2.38f, p.z), new Vector3(0.46f, 0.16f, 0.46f), LampHeadColor);
        }

        // One wall piece: a plain box, coloured, collider stripped.
        // Cubes are 1x1x1, so localScale is the world size.
        private void AddWallBox(Vector3 pos, Vector3 size, Color color)
        {
            AddWallBox(pos, size, color, 0f);
        }

        private void AddWallBox(Vector3 pos, Vector3 size, Color color, float yawDeg)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Wall";
            _wallPieces.Add(box);
            ParentToLayout(box);
            box.transform.localPosition = pos;
            box.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
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
            // 2026-11-08 refine (PLANET-REFINE.md): spots 1 and 3
            // are lowered (Y 1.00 -> 0.50 / 0.60) because their
            // peaks landed on the planet disc's visible cap and
            // split it — lowering them makes the arc continuous.
            // All other spots unchanged.
            AddMountain("Models/HorizonB", new Vector3(-60f, 0f, 26f), 20f, new Vector3(3.1f, 0.7f, 3.1f));
            AddMountain("Models/HorizonC", new Vector3(-55f, 0f, 39f), 310f, new Vector3(2.9f, 1.4f, 2.9f));
            AddMountain("Models/HorizonA", new Vector3(-52f, 0f, 13f), 140f, new Vector3(3.1f, 0.8f, 3.1f));
            AddMountain("Models/HorizonB", new Vector3(-55f, 0f, -10f), 260f, new Vector3(3.1f, 1.4f, 3.1f));
            AddMountain("Models/HorizonC", new Vector3(-49f, 0f, -34f), 90f, new Vector3(3.1f, 1.3f, 3.1f));
            AddMountain("Models/HorizonA", new Vector3(-36f, 0f, -52f), 200f, new Vector3(2.9f, 1.3f, 2.9f));
            AddMountain("Models/HorizonB", new Vector3(-13f, 0f, -60f), 170f, new Vector3(2.9f, 1.3f, 2.9f));
            AddMountain("Models/HorizonC", new Vector3(13f, 0f, -60f), 15f, new Vector3(2.9f, 1.3f, 2.9f));
            AddMountain("Models/HorizonA", new Vector3(34f, 0f, -52f), 230f, new Vector3(2.8f, 1.2f, 2.8f));
            AddMountain("Models/HorizonB", new Vector3(49f, 0f, -42f), 120f, new Vector3(2.8f, 1.2f, 2.8f));
            AddMountain("Models/HorizonC", new Vector3(60f, 0f, -21f), 45f, new Vector3(2.6f, 1.1f, 2.6f));
            AddMountain("Models/HorizonA", new Vector3(62f, 0f, 3f), 280f, new Vector3(2.6f, 1.1f, 2.6f));
            AddMountain("Models/HorizonB", new Vector3(-44f, 0f, 42f), 180f, new Vector3(2.8f, 1.3f, 2.8f));
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
                _mountains.Add(go);
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
                // 2026-11-08 refine (PLANET-REFINE.md): V3 showed
                // only ~6% of the disc (a thin sliver). Refined:
                // planet moved further out and scaled DOWN to 0.70
                // (narrower disc, 15.6% wide) so more of it fits
                // the thin sky band — 21.5% visible, a broad
                // continuous banded arc, +53% visible pixels vs V3.
                // Camera then unchanged (10,16,10). NOTE 2026-11-13:
                // the base camera has since moved to (23.2,43.5,23.2)
                // for the layout redesign — this planet position was
                // tuned for the old framing and needs a Play re-check
                // against the new one (position left as-is for now).
                // Top edge y 15.7% — only ~5 px below the top bar,
                // check it clears in Play.
                go.transform.position = new Vector3(-170.6f, -70.0f, 7.9f);
                go.transform.localScale = Vector3.one * 0.7f;
                // 2026-11-07: assign the texture at runtime. The FBX
                // import never linked AlienPlanet.png to the material's
                // Base Map (the slot stays empty, and the embedded
                // material reads as read-only in the Inspector, so the
                // texture cannot be assigned by hand). Resources.Load
                // finds the PNG under Resources/Models/, and setting
                // _BaseMap on the instance material bypasses the
                // import entirely.
                // 2026-11-10 (phone magenta fix): the FBX's embedded
                // material can come in with a shader that renders
                // magenta in a URP build (the phone) even though it
                // looks fine in the Editor — same class as the
                // CreatePrimitive default-material bug (see
                // SetPrimitiveColor). Assign a fresh URP Lit material
                // first (the march-marker pattern), then the texture
                // on that material, so the planet uses the same
                // shader the buildings use.
                var planetRenderer = go.GetComponentInChildren<MeshRenderer>();
                if (planetRenderer != null)
                {
                    var lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit != null) planetRenderer.material = new Material(lit);
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
            // Layout (2026-11-13 redesign): Nexus centre on its
            // plaza, the four production buildings at the ring-road
            // corners (+/-21, +/-14), He-3 / Aegis on the north /
            // south spines (0, +/-16), Lab / Hospital out on the
            // main street (+/-30, 0) — every building fronts the
            // road network painted in GroundPaved. Before that:
            // Nexus centre, generators top, barracks bottom — readable
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
            // 2026-11-09 expanded base: positions x2.5 (Nexus 1.75x, so
            // it dominates) for room for realistic roads. The base is
            // now wider than the screen — the Base view pans (arrow
            // keys / drag), like the Map view. Ground stays 56x48; the
            // spread buildings still fit inside it.
            // Command Nexus (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old blue cylinder so the
            // base still builds when the model file is missing.
            if (!TryAddModelNexus())
                AddBuilding(BuildingId.CommandNexus, new Vector3(0f, 0f, 0f), PrimitiveType.Cylinder, new Vector3(2f, 3f, 2f), new Color(0.15f, 0.35f, 0.75f), "Nexus");
            // Hydroponics Dome (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old green sphere so the
            // base still builds when the model file is missing.
            if (!TryAddModelHydroponicsDome())
                AddBuilding(BuildingId.HydroponicsDome, new Vector3(-21f, 0f, 14f), PrimitiveType.Sphere, new Vector3(2f, 1.2f, 2f), new Color(0.25f, 0.75f, 0.35f), "Hydro");
            // Alloy Extractor (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old grey cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelAlloyExtractor())
                AddBuilding(BuildingId.AlloyExtractor, new Vector3(21f, 0f, 14f), PrimitiveType.Cube, new Vector3(1.8f, 1.6f, 1.8f), new Color(0.65f, 0.68f, 0.72f), "Alloy");
            // Helium-3 Harvester (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old cyan cylinder so the
            // base still builds when the model file is missing.
            if (!TryAddModelHeliumHarvester())
                AddBuilding(BuildingId.HeliumHarvester, new Vector3(0f, 0f, 16f), PrimitiveType.Cylinder, new Vector3(1.4f, 2.4f, 1.4f), new Color(0.35f, 0.82f, 0.90f), "He-3");
            // Marine Foundry (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old green-grey cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelMarineFoundry())
                AddBuilding(BuildingId.MarineFoundry, new Vector3(-21f, 0f, -14f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.35f, 0.65f, 0.25f), "Marine");
            // Nova Arsenal (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old red cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelNovaArsenal())
                AddBuilding(BuildingId.NovaArsenal, new Vector3(21f, 0f, -14f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.85f, 0.35f, 0.20f), "Nova");
            // Aegis Motor Pool (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old grey cube so the base
            // still builds when the model file is missing.
            if (!TryAddModelAegisMotorPool())
                AddBuilding(BuildingId.AegisMotorPool, new Vector3(0f, 0f, -16f), PrimitiveType.Cube, new Vector3(2.4f, 1.2f, 2f), new Color(0.55f, 0.55f, 0.60f), "Aegis");
            // Research Lab (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old purple cylinder so
            // the base still builds when the model file is missing.
            // (2026-11-01 note: west side, clear of the other buildings,
            // so it stays tappable at phone size.)
            if (!TryAddModelResearchLab())
                AddBuilding(BuildingId.ResearchLab, new Vector3(-30f, 0f, 0f), PrimitiveType.Cylinder, new Vector3(1.8f, 2.2f, 1.8f), new Color(0.55f, 0.30f, 0.85f), "Lab");
            // Hospital (2026-11-09): east side at (30, 0) since the
            // 2026-11-13 redesign (was (20.75, 0)), mirroring
            // the Research Lab on the west, clear of the other buildings
            // so it stays tappable at phone size. Use the FBX model if
            // it loads, otherwise fall back to a white cube so the base
            // still builds when the model file is missing (the Blender
            // model is built separately, like the other v5 buildings).
            if (!TryAddModelHospital())
                AddBuilding(BuildingId.Hospital, new Vector3(30f, 0f, 0f), PrimitiveType.Cube, new Vector3(2f, 1.8f, 2f), new Color(0.95f, 0.95f, 0.95f), "Hospital");
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
                // 2026-11-09: the Nexus is the centrepiece — scaled 1.75x
                // so it towers over the other buildings (footprint 2.0 ->
                // 3.5, height 2.90 -> ~5.08). The model is built at 1.0 in
                // Blender; Unity scales it. The reactor rings animate by
                // rotating the ring objects, so scaling the parent does
                // not affect the animation.
                go.transform.localScale = Vector3.one * 1.75f;

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
                // colliders — give the model one matching the scaled
                // footprint (1.75x: 3.5 wide, 5.25 tall, centred at
                // y = 2.625, in WORLD space). NOTE: the collider lives
                // on the 1.75x-scaled Nexus, so its local centre/size
                // are divided by 1.75 — otherwise the collider renders
                // 6.13 wide, 9.19 tall, centred at 4.59.
                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 2.625f / 1.75f, 0f);
                    box.size = new Vector3(3.5f / 1.75f, 5.25f / 1.75f, 3.5f / 1.75f);
                }

                // Same white marker as the primitives, just above the
                // antenna tip (model top ~2.9 x 1.75 = ~5.08, so 5.51
                // sits on it, in WORLD space). NOTE: the marker is a
                // child of the 1.75x-scaled Nexus, so its local
                // position/scale are divided by 1.75 — otherwise it
                // floats at 9.64 and renders 1.84 wide (the "floating
                // globe" Patrick reported 2026-11-09). Local 3.15 ->
                // world 5.51; local 0.6 -> world 1.05 (proportionally
                // bigger than the 0.6 markers on unscaled buildings).
                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "NexusMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 5.51f / 1.75f, 0f);
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

        /// <summary>Hospital alert (2026-11-09): when injured units are
        /// waiting to be healed, a red sphere above the Hospital pulses
        /// (the hospital "lights up"); when none are injured it is
        /// hidden. Created lazily as a child of the Hospital building
        /// object, so it sits above the building whether the building is
        /// the FBX model (scale 1, top ~2.24) or the primitive fallback
        /// (scale 2/1.8/2, top 1.8). Runs every frame, like AnimateReactor.</summary>
        private void UpdateHospitalAlert()
        {
            if (_controller == null || _controller.Hospital == null) return;
            bool hasInjured = _controller.Hospital.HasInjured;
            if (_hospitalAlert == null)
            {
                if (!_buildingObjects.TryGetValue(BuildingId.Hospital, out var hospital) || hospital == null) return;
                _hospitalAlert = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _hospitalAlert.name = "HospitalAlert";
                _hospitalAlert.transform.SetParent(hospital.transform, false);
                // Remove the collider the primitive comes with — the alert
                // is visual only and must never block a building tap.
                var col = _hospitalAlert.GetComponent<Collider>();
                if (col != null) Destroy(col);
                bool isModel = _modelRenderers.ContainsKey(BuildingId.Hospital);
                if (isModel)
                {
                    _hospitalAlert.transform.localPosition = new Vector3(0f, 3.3f, 0f);
                    _hospitalAlertBaseScale = new Vector3(0.5f, 0.5f, 0.5f);
                }
                else
                {
                    // Primitive fallback: the parent is scaled (2, 1.8, 2),
                    // so the local position/scale are divided to land at the
                    // same world spot/size as the model case (y 2.97, 0.5 wide).
                    _hospitalAlert.transform.localPosition = new Vector3(0f, 1.15f, 0f);
                    _hospitalAlertBaseScale = new Vector3(0.25f, 0.28f, 0.25f);
                }
                _hospitalAlert.transform.localScale = _hospitalAlertBaseScale;
                var rend = _hospitalAlert.GetComponent<Renderer>();
                SetPrimitiveColor(rend, new Color(1f, 0.15f, 0.15f));
                // Bright red that blooms (the lighting pass gives emissive
                // materials a glow); the material colour is set above, and
                // the pulse below scales it. Keep it simple: colour only.
                var mat = rend != null ? rend.material : null;
                if (mat != null && mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.1f));
            }
            _hospitalAlert.SetActive(hasInjured);
            if (hasInjured)
            {
                float pulse = 1f + 0.35f * Mathf.Sin(Time.time * 4f);
                _hospitalAlert.transform.localScale = _hospitalAlertBaseScale * pulse;
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
                go.transform.localPosition = new Vector3(-8.4f, 0f, 5.6f);
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
                go.transform.localPosition = new Vector3(8.4f, 0f, 5.6f);
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
                go.transform.localPosition = new Vector3(0f, 0f, 6.4f);
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
                go.transform.localPosition = new Vector3(-8.4f, 0f, -5.6f);
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
                go.transform.localPosition = new Vector3(8.4f, 0f, -5.6f);
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
                go.transform.localPosition = new Vector3(0f, 0f, -6.4f);
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
                go.transform.localPosition = new Vector3(-12f, 0f, 0f);
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

        // Hospital (2026-11-09): same pattern as the Research Lab.
        // The model position (12, 0, 0) is the layout position
        // (30, 0) / 2.5, matching the other buildings' model
        // positions (see TryAddModelResearchLab). The marker is white
        // like the other buildings; the hospital's injured alert is a
        // separate pulsing red marker (see UpdateHospitalAlert), so the
        // two never conflict.
        private bool TryAddModelHospital()
        {
            var prefab = LoadHospitalModelPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "HospitalBuilding";
                go.transform.SetParent(_layoutRoot.transform, false);
                go.transform.localPosition = new Vector3(12f, 0f, 0f);
                go.transform.localScale = Vector3.one;

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
                    partColors[rend] = rend.material.color;
                }

                if (go.GetComponentInChildren<Collider>() == null)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 1f, 0f);
                    box.size = new Vector3(2.2f, 2.1f, 2.2f);
                }

                var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "HospitalMarker";
                marker.transform.SetParent(go.transform, false);
                marker.transform.localPosition = new Vector3(0f, 2.5f, 0f);
                marker.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                SetPrimitiveColor(marker.GetComponent<Renderer>(), Color.white);

                _modelRenderers[BuildingId.Hospital] = list;
                foreach (var pair in partColors)
                    _modelBaseColors[pair.Key] = pair.Value;
                _buildingObjects[BuildingId.Hospital] = go;
                _baseColors[BuildingId.Hospital] = new Color(0.95f, 0.95f, 0.95f);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Hospital model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the Hospital FBX as a prefab. Same Resources /
        /// Editor pattern as the other buildings: in a built player the
        /// FBX lives at Assets/_Project/Resources/Models/Hospital.fbx.</summary>
        private static GameObject LoadHospitalModelPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/Hospital");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/Hospital.fbx");
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
            // Hospital alert (2026-11-09): the red pulsing sphere above
            // the Hospital when injured units are waiting to be healed.
            UpdateHospitalAlert();
            // Input gate (2026-11-13): the base's keyboard/pointer
            // handling must only run while the Base tab is showing.
            // This component lives on an always-active host, so
            // without the gate it kept answering drags on other
            // screens — in the boss lair, a drag on empty screen space
            // panned the hidden base camera, snapping the fight
            // camera back to the base. Same pattern as
            // ExteriorView._visible.
            if (!_visible) { _pointerDown = false; return; }
            // Arrow keys pan the base camera (PC). Same pattern as the
            // Map view: Keyboard when the Input System is enabled,
            // legacy Input otherwise.
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
            // a building or hide the panel); a press that moves past
            // the threshold is a DRAG (pan the camera) and must not
            // also fire as a tap on release. Input System note: Unity
            // throws InvalidOperationException if the legacy
            // UnityEngine.Input API is read while Player Settings >
            // Active Input Handler is set to Input System Package only.
            // Same pattern as the Map view: read Mouse/Touchscreen
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
                // within the threshold selects a building or hides the
                // panel.
                if (_isDrag) return;
                HandleTap(screenPos);
            }
        }

        private Vector3 ScreenDeltaToWorld(Vector2 pixelDelta)
        {
            var cam = Camera.main;
            if (cam == null) return Vector3.zero;
            // Distance from the camera to the point it looks at (the
            // pan offset cancels out — the camera and its look-at move
            // together, so the distance is constant).
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
            cam.transform.position = BaseCameraPosition + _panOffset;
            cam.transform.LookAt(Vector3.zero + _panOffset);
        }

        private void HandleTap(Vector2 screenPos)
        {
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
            _visible = visible;
            foreach (var go in _buildingObjects.Values) go.SetActive(visible);
        }

        /// <summary>Show or hide the base environment: the paved
        /// interior and the perimeter wall. The Map tab hides them
        /// (2026-11-13, Patrick: the Map view should stay as it was
        /// before the base redesign): on the Map the base is
        /// represented by ExteriorView's BaseMarker castle, which
        /// sits on the exterior terrain's flat base disc (r 42 — the
        /// whole compound footprint fits inside it); the base's wall
        /// and road paving would otherwise show around the castle.
        /// The ground mesh is not toggled here — since 2026-11-14
        /// it never shows at all (moon sand removed, BuildGround).
        /// Buildings are toggled separately by SetVisible; rocks,
        /// props, debris and the planet stay as they were.</summary>
        public void SetEnvironmentVisible(bool visible)
        {
            if (_pavedGo != null) _pavedGo.SetActive(visible);
            foreach (var go in _wallPieces)
            {
                if (go != null) go.SetActive(visible);
            }
        }

        /// <summary>Show or hide the 13 horizon mountains. The Map tab
        /// hides them (they crowd the big map and take space the nodes,
        /// camps and future bases need); the Base tab shows them again
        /// (they frame the base view). The planet is untouched — it
        /// stays in the sky in both views.</summary>
        public void SetMountainsVisible(bool visible)
        {
            foreach (var go in _mountains)
            {
                if (go != null) go.SetActive(visible);
            }
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
