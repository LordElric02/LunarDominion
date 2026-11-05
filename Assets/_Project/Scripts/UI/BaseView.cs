using System;
using System.Collections.Generic;
using LunarDominion.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LunarDominion.UI
{
    /// <summary>Central lunar base scene, built from primitives only.
    /// Isometric-style camera, lunar ground, and one placeholder building
    /// per GDD building. Tapping a building (or its quick-select button)
    /// opens the building panel. No rules live here.</summary>
    public class BaseView : MonoBehaviour
    {
        public event Action<BuildingDefinition> OnBuildingSelected;

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
        private GameObject _quickSelectRow;

        public void Build(GameController controller, Canvas canvas)
        {
            _controller = controller;
            BuildGroundAndCamera();
            BuildBuildings();
            BuildDerelictShipScene();
            BuildQuickSelect(canvas);
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
            cam.transform.position = new Vector3(9f, 11f, 9f);
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

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "LunarGround";
            // 2026-11-05: extended from 17 to 36 units wide so the
            // derelict spaceship in the distance sits on moon surface
            // instead of black void. Buildings, craters, patches and
            // rocks are unchanged and still sit near the centre.
            ground.transform.localScale = new Vector3(3.6f, 1f, 3.6f);
            SetPrimitiveColor(ground.GetComponent<Renderer>(), new Color(0.27f, 0.29f, 0.33f));

            // 2026-11-05: moon-like surface. The old flat dark discs read as
            // stains, so the ground now has tonal patches, two proper 3D
            // craters (dark bowl + raised rim of small rocks) and scattered
            // boulders. All decor is low, fixed-position (no randomness at
            // runtime), clear of every building, and has its collider
            // stripped so a tap ray can never be swallowed by a rock in
            // front of a building. Building positions are unchanged.
            AddGroundPatch(new Vector3(-5f, 0.005f, 5.5f), 3.2f, new Color(0.22f, 0.24f, 0.28f));
            AddGroundPatch(new Vector3(5.8f, 0.005f, -1.5f), 2.8f, new Color(0.32f, 0.34f, 0.38f));
            AddGroundPatch(new Vector3(-2.5f, 0.005f, -6.8f), 3f, new Color(0.23f, 0.25f, 0.29f));
            AddGroundPatch(new Vector3(7f, 0.005f, -5.5f), 2.6f, new Color(0.31f, 0.33f, 0.37f));
            AddCrater(new Vector3(-6.3f, 0f, -3.2f), 1.6f);
            AddCrater(new Vector3(6.2f, 0f, 3f), 1.3f);
            AddRock(new Vector3(-7f, 0f, 4f), 0.5f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(-1f, 0f, 7f), 0.4f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(7.5f, 0f, 0.5f), 0.45f, new Color(0.38f, 0.4f, 0.44f));
            AddRock(new Vector3(2f, 0f, -7f), 0.55f, new Color(0.3f, 0.32f, 0.36f));
            AddRock(new Vector3(-7.5f, 0f, -5.5f), 0.4f, new Color(0.35f, 0.37f, 0.41f));
            AddRock(new Vector3(4.5f, 0f, 6.5f), 0.5f, new Color(0.33f, 0.35f, 0.39f));
            AddRock(new Vector3(-3.5f, 0f, 6.8f), 0.35f, new Color(0.38f, 0.4f, 0.44f));
            AddRock(new Vector3(7f, 0f, -6.8f), 0.45f, new Color(0.3f, 0.32f, 0.36f));
        }


        // A large flat tonal patch, to break up the single grey ground.
        private void AddGroundPatch(Vector3 pos, float diameter, Color color)
        {
            var patch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            patch.name = "GroundPatch";
            patch.transform.position = pos;
            patch.transform.localScale = new Vector3(diameter, 0.02f, diameter);
            SetPrimitiveColor(patch.GetComponent<Renderer>(), color);
            StripCollider(patch);
        }

        // A lunar crater: dark bowl sunk into the ground, a raised rim of
        // small rocks around the edge, and a low mound in the middle.
        private void AddCrater(Vector3 pos, float radius)
        {
            var bowl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bowl.name = "CraterBowl";
            bowl.transform.position = new Vector3(pos.x, 0.01f, pos.z);
            bowl.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
            SetPrimitiveColor(bowl.GetComponent<Renderer>(), new Color(0.13f, 0.14f, 0.17f));
            StripCollider(bowl);

            int rimStones = 9;
            for (int i = 0; i < rimStones; i++)
            {
                float angle = i * Mathf.PI * 2f / rimStones;
                var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stone.name = "CraterRim";
                stone.transform.position = new Vector3(
                    pos.x + Mathf.Cos(angle) * radius,
                    0.06f,
                    pos.z + Mathf.Sin(angle) * radius);
                stone.transform.localScale = new Vector3(0.32f, 0.18f, 0.32f);
                SetPrimitiveColor(stone.GetComponent<Renderer>(), new Color(0.38f, 0.4f, 0.44f));
                StripCollider(stone);
            }

            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mound.name = "CraterMound";
            mound.transform.position = new Vector3(pos.x, 0.04f, pos.z);
            mound.transform.localScale = new Vector3(radius * 0.5f, 0.12f, radius * 0.5f);
            SetPrimitiveColor(mound.GetComponent<Renderer>(), new Color(0.2f, 0.21f, 0.24f));
            StripCollider(mound);
        }

        // A single boulder, squashed slightly so it reads as a rock.
        private void AddRock(Vector3 pos, float size, Color color)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock";
            rock.transform.position = new Vector3(pos.x, size * 0.25f, pos.z);
            rock.transform.localScale = new Vector3(size, size * 0.6f, size * 0.85f);
            SetPrimitiveColor(rock.GetComponent<Renderer>(), color);
            StripCollider(rock);
        }

        // Ground decor must never block a building tap: the tap raycast
        // takes the first hit, so decor colliders are removed on creation.
        // (The ground plane itself keeps its original collider.)
        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        // Derelict spaceship scene (2026-11-05): a crashed ship in the
        // distance on the far side of the base from the camera, at
        // (-13, 0, -11), with debris scattered around it. Framing check:
        // with the camera at (9,11,9) looking at the origin at 60 degrees
        // vertical FOV in portrait, the ship centre lands at about
        // NDC (0.15, +0.71) — upper middle of the frame, in view with
        // margin — and the extended 36-wide ground reaches it. The ship
        // is scenery, not a building: it is never registered in
        // _buildingObjects/_baseColors, has no quick-select button, no
        // marker, and no colliders, so the tap raycast is unaffected.
        private void BuildDerelictShipScene()
        {
            if (!TryAddDerelictShip())
                AddDerelictShipPrimitive();

            // Debris within ~4 units of the ship, some half-buried. All
            // fixed positions, all colliders stripped, all ~13+ units
            // from the nearest building (the Lab at (-4.9, 0)).
            var rust = new Color(0.75f, 0.38f, 0.12f);
            var dark = new Color(0.15f, 0.15f, 0.18f);
            AddDebris(new Vector3(-11.2f, 0f, -9.3f), PrimitiveType.Cube, new Vector3(1.2f, 0.3f, 0.8f), rust, 20f);
            AddDebris(new Vector3(-15f, 0f, -12.8f), PrimitiveType.Cylinder, new Vector3(0.6f, 1.4f, 0.6f), dark, 0f);
            AddDebris(new Vector3(-12f, 0f, -13.6f), PrimitiveType.Cube, new Vector3(0.9f, 0.4f, 0.7f), rust, 65f);
            AddDebris(new Vector3(-14.6f, 0f, -9.4f), PrimitiveType.Cube, new Vector3(0.8f, 0.5f, 0.6f), dark, 110f);
            AddDebris(new Vector3(-10.6f, 0f, -11.6f), PrimitiveType.Sphere, new Vector3(0.7f, 0.7f, 0.7f), dark, 0f);
            AddDebris(new Vector3(-13.2f, 0f, -14.2f), PrimitiveType.Cube, new Vector3(1f, 0.3f, 0.6f), dark, 40f);
            AddDebris(new Vector3(-16.2f, 0f, -11f), PrimitiveType.Cylinder, new Vector3(0.7f, 1.2f, 0.7f), rust, 0f);
            AddDebris(new Vector3(-11.6f, 0f, -12.6f), PrimitiveType.Cube, new Vector3(0.7f, 0.4f, 0.5f), dark, 80f);
            AddDebris(new Vector3(-14.2f, 0f, -8.6f), PrimitiveType.Cube, new Vector3(0.9f, 0.35f, 0.7f), rust, 15f);
            AddDebris(new Vector3(-12.6f, 0f, -10.2f), PrimitiveType.Sphere, new Vector3(0.5f, 0.5f, 0.5f), rust, 0f);
        }

        // One piece of wreck debris: a panel, pipe section or tank, sunk
        // a quarter of its height into the ground so it reads half-buried.
        private void AddDebris(Vector3 pos, PrimitiveType shape, Vector3 scale, Color color, float yRot)
        {
            var piece = GameObject.CreatePrimitive(shape);
            piece.name = "Debris";
            piece.transform.position = new Vector3(pos.x, scale.y * 0.25f, pos.z);
            piece.transform.localScale = scale;
            piece.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
            SetPrimitiveColor(piece.GetComponent<Renderer>(), color);
            StripCollider(piece);
        }

        private void BuildBuildings()
        {
            // Layout: Nexus centre, generators top, barracks bottom — readable
            // from the isometric camera and tappable on a phone screen.
            // 2026-11-05: positions tightened to 0.75x toward the centre so
            // the base fills the portrait frame better (was spread over 13
            // units wide with a lot of empty ground); ground plane and
            // craters shrunk to match. Building sizes unchanged.
            // Command Nexus (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old blue cylinder so the
            // base still builds when the model file is missing.
            if (!TryAddModelNexus())
                AddBuilding(BuildingId.CommandNexus, new Vector3(0f, 0f, 0f), PrimitiveType.Cylinder, new Vector3(2f, 3f, 2f), new Color(0.15f, 0.35f, 0.75f), "Nexus");
            // Hydroponics Dome (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old green sphere so the
            // base still builds when the model file is missing.
            if (!TryAddModelHydroponicsDome())
                AddBuilding(BuildingId.HydroponicsDome, new Vector3(-3f, 0f, 2.6f), PrimitiveType.Sphere, new Vector3(2f, 1.2f, 2f), new Color(0.25f, 0.75f, 0.35f), "Hydro");
            // Alloy Extractor (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old grey cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelAlloyExtractor())
                AddBuilding(BuildingId.AlloyExtractor, new Vector3(3f, 0f, 2.6f), PrimitiveType.Cube, new Vector3(1.8f, 1.6f, 1.8f), new Color(0.65f, 0.68f, 0.72f), "Alloy");
            // Helium-3 Harvester (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old cyan cylinder so the
            // base still builds when the model file is missing.
            if (!TryAddModelHeliumHarvester())
                AddBuilding(BuildingId.HeliumHarvester, new Vector3(0f, 0f, 4.1f), PrimitiveType.Cylinder, new Vector3(1.4f, 2.4f, 1.4f), new Color(0.35f, 0.82f, 0.90f), "He-3");
            // Marine Foundry (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old green-grey cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelMarineFoundry())
                AddBuilding(BuildingId.MarineFoundry, new Vector3(-3f, 0f, -2.6f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.35f, 0.65f, 0.25f), "Marine");
            // Nova Arsenal (Blender model, 2026-11-05): use the FBX model if
            // it loads, otherwise fall back to the old red cube so the
            // base still builds when the model file is missing.
            if (!TryAddModelNovaArsenal())
                AddBuilding(BuildingId.NovaArsenal, new Vector3(3f, 0f, -2.6f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.85f, 0.35f, 0.20f), "Nova");
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
                AddBuilding(BuildingId.ResearchLab, new Vector3(-4.9f, 0f, 0f), PrimitiveType.Cylinder, new Vector3(1.8f, 2.2f, 1.8f), new Color(0.55f, 0.30f, 0.85f), "Lab");
        }

        private void AddBuilding(BuildingId id, Vector3 pos, PrimitiveType shape, Vector3 scale, Color color, string shortName)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = shortName + "Building";
            go.transform.position = new Vector3(pos.x, scale.y / 2f, pos.z);
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
                go.transform.position = Vector3.zero;
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
        /// (-3,0,2.6) if it can be loaded. Returns false (caller falls back
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
                go.transform.position = new Vector3(-3f, 0f, 2.6f);
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
        /// (3,0,2.6) if it can be loaded. Returns false (caller falls back
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
                go.transform.position = new Vector3(3f, 0f, 2.6f);
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
                go.transform.position = new Vector3(0f, 0f, 4.1f);
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

        /// <summary>Instantiate the Blender Marine Foundry FBX at (-3,0,-2.6)
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
                go.transform.position = new Vector3(-3f, 0f, -2.6f);
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

        /// <summary>Instantiate the Blender Nova Arsenal FBX at (3,0,-2.6)
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
                go.transform.position = new Vector3(3f, 0f, -2.6f);
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
                go.transform.position = new Vector3(0f, 0f, -4.1f);
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
                go.transform.position = new Vector3(-4.9f, 0f, 0f);
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

        /// <summary>Instantiate the Blender derelict spaceship FBX at
        /// (-13, 0, -11) if it can be loaded. Returns false (caller falls
        /// back to the primitive wreck) when the model is missing, has no
        /// meshes, or fails to instantiate. The model is built ~5.5 units
        /// long with its nose-down crash tilt baked into the geometry, so
        /// only the -45 degree yaw is applied here, pointing the nose
        /// toward the camera side. Scenery only: no building registration,
        /// no marker, and all colliders are stripped so taps are unaffected.
        /// Its imported materials are used as-is (URP Lit on import).</summary>
        private bool TryAddDerelictShip()
        {
            var prefab = LoadDerelictShipPrefab();
            if (prefab == null) return false;

            GameObject go = null;
            try
            {
                go = Instantiate(prefab);
                go.name = "DerelictShip";
                go.transform.position = new Vector3(-13f, 0f, -11f);
                go.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
                go.transform.localScale = Vector3.one;

                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Destroy(go);
                    return false;
                }
                foreach (var collider in go.GetComponentsInChildren<Collider>())
                    Destroy(collider);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BaseView] Derelict ship model failed to load, using primitive: " + e.Message);
                if (go != null) Destroy(go);
                return false;
            }
        }

        /// <summary>Load the derelict spaceship FBX as a prefab. Same
        /// Resources/ + Editor AssetDatabase pattern as the buildings:
        /// in a built player only assets under a Resources folder can be
        /// loaded by path, so the FBX lives at
        /// Assets/_Project/Resources/Models/DerelictShip.fbx.</summary>
        private static GameObject LoadDerelictShipPrefab()
        {
            var fromResources = Resources.Load<GameObject>("Models/DerelictShip");
            if (fromResources != null) return fromResources;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Project/Resources/Models/DerelictShip.fbx");
#else
            return null;
#endif
        }

        /// <summary>Primitive fallback wreck, used only when the FBX is
        /// missing: a grey cylinder hull lying along the ship's yawed axis
        /// with a wing and a dark cockpit. Same position and yaw as the
        /// model; the nose-down tilt is model-only and not reproduced here.
        /// Colliders are stripped on every part, as with the model.</summary>
        private void AddDerelictShipPrimitive()
        {
            var go = new GameObject("DerelictShip");
            go.transform.position = new Vector3(-13f, 0f, -11f);
            go.transform.rotation = Quaternion.Euler(0f, -45f, 0f);

            var hull = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hull.name = "DerelictHull";
            hull.transform.SetParent(go.transform, false);
            hull.transform.localPosition = new Vector3(0f, 1f, 0f);
            hull.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            hull.transform.localScale = new Vector3(1.4f, 2.5f, 1.4f);
            SetPrimitiveColor(hull.GetComponent<Renderer>(), new Color(0.32f, 0.33f, 0.36f));
            StripCollider(hull);

            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = "DerelictWing";
            wing.transform.SetParent(go.transform, false);
            wing.transform.localPosition = new Vector3(0.2f, 0.9f, 1f);
            wing.transform.localScale = new Vector3(1.6f, 0.12f, 1.4f);
            SetPrimitiveColor(wing.GetComponent<Renderer>(), new Color(0.15f, 0.15f, 0.18f));
            StripCollider(wing);

            var cockpit = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cockpit.name = "DerelictCockpit";
            cockpit.transform.SetParent(go.transform, false);
            cockpit.transform.localPosition = new Vector3(1.8f, 1.3f, 0f);
            cockpit.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            SetPrimitiveColor(cockpit.GetComponent<Renderer>(), new Color(0.08f, 0.10f, 0.16f));
            StripCollider(cockpit);
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

        private void BuildQuickSelect(Canvas canvas)
        {
            // Accessibility fallback: tapping a 3D building can be fiddly on a
            // small screen, so a row of building buttons sits above the nav.
            // It shifts up with the bottom safe area, in step with the nav.
            var safe = SafeArea.Get(canvas);
            _quickSelectRow = UIHelpers.Panel(canvas.gameObject, "QuickSelect", UIHelpers.Hex("#1A2436"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 220 + safe.Bottom), new Vector2(0, 300 + safe.Bottom));
            var defs = _controller.BuildingDefinitions;
            float step = 1f / defs.Count;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var btn = UIHelpers.Button(_quickSelectRow, def.DisplayName + "Btn", ShortName(def.Id), 22,
                    UIHelpers.Hex("#2A3A55"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * step + 0.01f, 0.15f);
                rt.anchorMax = new Vector2((i + 1) * step - 0.01f, 0.85f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => OnBuildingSelected?.Invoke(def));
            }
        }

        private static string ShortName(BuildingId id)
        {
            switch (id)
            {
                case BuildingId.CommandNexus: return "Nexus";
                case BuildingId.MarineFoundry: return "Marine";
                case BuildingId.NovaArsenal: return "Nova";
                case BuildingId.AegisMotorPool: return "Aegis";
                case BuildingId.HydroponicsDome: return "Hydro";
                case BuildingId.AlloyExtractor: return "Alloy";
                case BuildingId.HeliumHarvester: return "He-3";
                case BuildingId.ResearchLab: return "Lab";
                default: return id.ToString();
            }
        }

        private void Update()
        {
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
            if (!Physics.Raycast(ray, out var hit, 100f)) return;
            foreach (var pair in _buildingObjects)
            {
                if (hit.transform == pair.Value.transform || hit.transform.IsChildOf(pair.Value.transform))
                {
                    var def = _controller.BuildingDefinitions.Find(d => d.Id == pair.Key);
                    if (def != null) OnBuildingSelected?.Invoke(def);
                    break;
                }
            }
        }

        public void SetVisible(bool visible)
        {
            foreach (var go in _buildingObjects.Values) go.SetActive(visible);
            if (_quickSelectRow != null) _quickSelectRow.SetActive(visible);
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
