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
        private GameController _controller;
        private GameObject _quickSelectRow;

        public void Build(GameController controller, Canvas canvas)
        {
            _controller = controller;
            BuildGroundAndCamera();
            BuildBuildings();
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
            light.intensity = 1.1f;
            lightGo.transform.position = new Vector3(5f, 10f, 0f);
            lightGo.transform.LookAt(Vector3.zero);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "LunarGround";
            ground.transform.localScale = new Vector3(2.2f, 1f, 2.2f);
            ground.GetComponent<Renderer>().material.color = new Color(0.23f, 0.25f, 0.29f);

            // A few crater placeholders.
            for (int i = 0; i < 5; i++)
            {
                var crater = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                crater.name = "Crater";
                crater.transform.position = new Vector3(-7f + i * 3.5f, 0.01f, -6f + (i % 2) * 9f);
                crater.transform.localScale = new Vector3(1.2f, 0.05f, 1.2f);
                crater.GetComponent<Renderer>().material.color = new Color(0.16f, 0.17f, 0.20f);
            }
        }

        private void BuildBuildings()
        {
            // Layout: Nexus centre, generators top, barracks bottom — readable
            // from the isometric camera and tappable on a phone screen.
            AddBuilding(BuildingId.CommandNexus, new Vector3(0f, 0f, 0f), PrimitiveType.Cylinder, new Vector3(2f, 3f, 2f), new Color(0.15f, 0.35f, 0.75f), "Nexus");
            AddBuilding(BuildingId.HydroponicsDome, new Vector3(-4f, 0f, 3.5f), PrimitiveType.Sphere, new Vector3(2f, 1.2f, 2f), new Color(0.25f, 0.75f, 0.35f), "Hydro");
            AddBuilding(BuildingId.AlloyExtractor, new Vector3(4f, 0f, 3.5f), PrimitiveType.Cube, new Vector3(1.8f, 1.6f, 1.8f), new Color(0.65f, 0.68f, 0.72f), "Alloy");
            AddBuilding(BuildingId.HeliumHarvester, new Vector3(0f, 0f, 5.5f), PrimitiveType.Cylinder, new Vector3(1.4f, 2.4f, 1.4f), new Color(0.35f, 0.82f, 0.90f), "He-3");
            AddBuilding(BuildingId.MarineFoundry, new Vector3(-4f, 0f, -3.5f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.35f, 0.65f, 0.25f), "Marine");
            AddBuilding(BuildingId.NovaArsenal, new Vector3(4f, 0f, -3.5f), PrimitiveType.Cube, new Vector3(2f, 1.4f, 2f), new Color(0.85f, 0.35f, 0.20f), "Nova");
            AddBuilding(BuildingId.AegisMotorPool, new Vector3(0f, 0f, -5.5f), PrimitiveType.Cube, new Vector3(2.4f, 1.2f, 2f), new Color(0.55f, 0.55f, 0.60f), "Aegis");
        }

        private void AddBuilding(BuildingId id, Vector3 pos, PrimitiveType shape, Vector3 scale, Color color, string shortName)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = shortName + "Building";
            go.transform.position = new Vector3(pos.x, scale.y / 2f, pos.z);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material.color = color;
            _buildingObjects[id] = go;
            _baseColors[id] = color;

            // A small marker on top so the building reads at phone size.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = shortName + "Marker";
            marker.transform.SetParent(go.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            marker.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
            marker.GetComponent<Renderer>().material.color = Color.white;
        }

        private void BuildQuickSelect(Canvas canvas)
        {
            // Accessibility fallback: tapping a 3D building can be fiddly on a
            // small screen, so a row of building buttons sits above the nav.
            _quickSelectRow = UIHelpers.Panel(canvas.gameObject, "QuickSelect", UIHelpers.Hex("#1A2436"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 220), new Vector2(0, 300));
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
                default: return id.ToString();
            }
        }

        private void Update()
        {
            bool tapped = Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
            if (!tapped) return;
            if (EventSystem.current != null)
            {
                if (Input.touchCount > 0 && EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)) return;
                if (Input.touchCount == 0 && EventSystem.current.IsPointerOverGameObject()) return;
            }
            var cam = Camera.main;
            if (cam == null) return;
            Vector2 screenPos = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
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
                var rend = pair.Value.GetComponent<Renderer>();
                if (rend == null) continue;
                if (pair.Key == selected)
                    rend.material.color = isLocked ? new Color(0.4f, 0.4f, 0.4f) : _baseColors[pair.Key] * 1.4f;
                else
                    rend.material.color = _baseColors[pair.Key];
            }
        }
    }
}
