#if UNITY_EDITOR
using System;
using LunarDominion.UI;
using UnityEditor;
using UnityEngine;

namespace LunarDominion.Editor
{
    /// <summary>
    /// Terrain transplant (Patrick 2026-11-20, branch terrain-transplant):
    /// builds the Unity Terrain that will replace the Blender-mesh
    /// exterior. The heightmap is a STRETCHED REPLICA of today's
    /// landscape: every texel samples the existing analytic
    /// ExteriorView.TerrainHeight at (x / Scale, z / Scale), so the
    /// new world is the old world at 3x footprint — same pads, same
    /// road alignment, same heights relationships — and gameplay
    /// cannot break. Phase 2 of the transplant plan; the runtime
    /// code swap is Phase 3 and is NOT in this file.
    ///
    /// Menu:
    ///   Lunar Dominion/Terrain Transplant/1. Build Terrain Data
    ///   Lunar Dominion/Terrain Transplant/2. Create Terrain In Scene
    /// </summary>
    public static class TerrainTransplantBuilder
    {
        // World scale: old world 1120x960 -> new world 3360x2880.
        private const float Scale = 3f;
        private const float OldHalfX = 560f;
        private const float OldHalfZ = 480f;

        // Vertical scale for the replica's heights. Today's analytic
        // heights are clamped to [-5.2, 8.0]; at HeightScale 3 the
        // new terrain spans [-15.6, 24.0], keeping the landscape's
        // proportions at the bigger footprint.
        private const float HeightScale = 3f;
        private const float AnalyticMin = -5.2f;
        private const float AnalyticMax = 8.0f;

        private const int HeightmapResolution = 2049;
        private const string TerrainFolder = "Assets/_Project/Terrain";
        private const string TerrainAssetPath = TerrainFolder + "/LunarTerrain.asset";
        private const string MaterialPath = TerrainFolder + "/LunarTerrainMat.mat";
        private const string BaseLayerName = "Grass_Moss_TerrainLayer";

        private static float NewHalfX => OldHalfX * Scale;   // 1680
        private static float NewHalfZ => OldHalfZ * Scale;   // 1440
        private static float MinH => AnalyticMin * HeightScale;
        private static float MaxH => AnalyticMax * HeightScale;

        [MenuItem("Lunar Dominion/Terrain Transplant/1. Build Terrain Data")]
        public static void BuildTerrainData()
        {
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath) != null)
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Rebuild terrain data?",
                    "LunarTerrain.asset already exists. Rebuilding regenerates the heightmap " +
                    "from the analytic replica and WIPES any hand-sculpting done on it. Continue?",
                    "Rebuild", "Cancel");
                if (!ok) return;
                AssetDatabase.DeleteAsset(TerrainAssetPath);
            }

            if (!AssetDatabase.IsValidFolder(TerrainFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Project", "Terrain");
            }

            var data = new TerrainData();
            data.heightmapResolution = HeightmapResolution;
            data.size = new Vector3(NewHalfX * 2f, MaxH - MinH, NewHalfZ * 2f);
            data.alphamapResolution = 1024;

            int res = data.heightmapResolution;
            var heights = new float[res, res];
            try
            {
                for (int iz = 0; iz < res; iz++)
                {
                    if (iz % 128 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Building terrain replica",
                            "Sampling the analytic landscape...", iz / (float)res);
                    }
                    float wz = -NewHalfZ + (iz / (float)(res - 1)) * NewHalfZ * 2f;
                    for (int ix = 0; ix < res; ix++)
                    {
                        float wx = -NewHalfX + (ix / (float)(res - 1)) * NewHalfX * 2f;
                        float h = ExteriorView.TerrainHeight(wx / Scale, wz / Scale) * HeightScale;
                        heights[iz, ix] = Mathf.Clamp01((h - MinH) / (MaxH - MinH));
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            data.SetHeights(0, 0, heights);

            TryApplyBaseLayer(data);

            AssetDatabase.CreateAsset(data, TerrainAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[TerrainTransplant] Terrain data built: " + TerrainAssetPath +
                      " size " + data.size + " heightmap " + res + "x" + res +
                      " (analytic replica at " + Scale + "x footprint, " + HeightScale + "x height).");
            LogProbes("analytic", (wx, wz) => ExteriorView.TerrainHeight(wx / Scale, wz / Scale) * HeightScale);
            LogProbes("terrainData", (wx, wz) =>
            {
                float u = (wx + NewHalfX) / (NewHalfX * 2f);
                float v = (wz + NewHalfZ) / (NewHalfZ * 2f);
                return MinH + data.GetInterpolatedHeight(u, v);
            });
            Selection.activeObject = data;
        }

        [MenuItem("Lunar Dominion/Terrain Transplant/2. Create Terrain In Scene")]
        public static void CreateTerrainInScene()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                EditorUtility.DisplayDialog("No terrain data yet",
                    "Run 'Lunar Dominion/Terrain Transplant/1. Build Terrain Data' first.", "OK");
                return;
            }

            var existing = GameObject.Find("LunarTerrain (Transplant Test)");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            // Unity 6 removed Terrain.CreateGameObject — build it by hand.
            var go = new GameObject("LunarTerrain (Transplant Test)");
            var terrainComponent = go.AddComponent<Terrain>();
            terrainComponent.terrainData = data;
            terrainComponent.drawTreesAndFoliage = true;
            var terrainCollider = go.AddComponent<TerrainCollider>();
            terrainCollider.terrainData = data;

            // An explicit URP terrain material is required: with no
            // materialTemplate this project renders the terrain
            // magenta (the built-in fallback shader fails under URP).
            var mat = GetOrCreateTerrainMaterial();
            if (mat != null) terrainComponent.materialTemplate = mat;

            go.transform.position = new Vector3(-NewHalfX, MinH, -NewHalfZ);
            Selection.activeGameObject = go;

            var terrain = go.GetComponent<Terrain>();
            Debug.Log("[TerrainTransplant] Terrain placed in scene at " + go.transform.position +
                      ". Compare against the probes below (SampleHeight vs analytic):");
            LogProbes("sampleHeight", (wx, wz) => terrain.SampleHeight(new Vector3(wx, 0f, wz)) + MinH);
        }

        private static Material GetOrCreateTerrainMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null)
            {
                Debug.LogError("[TerrainTransplant] Shader 'Universal Render Pipeline/Terrain/Lit' not found — " +
                               "URP terrain shader unavailable in this project; terrain will render magenta.");
                return null;
            }
            var mat = new Material(shader) { name = "LunarTerrainMat" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTransplant] Terrain material created: " + MaterialPath + " (shader " + shader.name + ")");
            return mat;
        }

        // Grass base coat, if the Terrain Asset Pack has been imported
        // (it lives outside the repo; absence is fine, Phase 4 paints).
        private static void TryApplyBaseLayer(TerrainData data)
        {
            string[] guids = AssetDatabase.FindAssets(BaseLayerName + " t:TerrainLayer");
            if (guids.Length == 0)
            {
                Debug.Log("[TerrainTransplant] " + BaseLayerName + " not found — terrain left unpainted (Phase 4).");
                return;
            }
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (layer == null) return;

            data.terrainLayers = new[] { layer };
            int ares = data.alphamapResolution;
            var alpha = new float[ares, ares, 1];
            for (int y = 0; y < ares; y++)
                for (int x = 0; x < ares; x++)
                    alpha[y, x, 0] = 1f;
            data.SetAlphamaps(0, 0, alpha);
            Debug.Log("[TerrainTransplant] Base layer applied: " + layer.name);
        }

        // Probe points are given in NEW world coords; the expected
        // value comes from the same sampling the bake used.
        private static void LogProbes(string label, Func<float, float, float> sample)
        {
            var probes = new (string name, float x, float z)[]
            {
                ("base pad",        0f,     0f),
                ("highland NW",  -1140f,   900f),
                ("highland SE",   1230f,  -960f),
                ("geode basin",   -360f,  -720f),
                ("east edge",     1677f,     0f),
                ("open dunes",     300f,   150f),
            };
            foreach (var p in probes)
            {
                float expected = ExteriorView.TerrainHeight(p.x / Scale, p.z / Scale) * HeightScale;
                float got = sample(p.x, p.z);
                Debug.Log("[TerrainTransplant] probe " + p.name +
                          " (" + p.x + ", " + p.z + ") " + label + " = " + got.ToString("F2") +
                          " expected " + expected.ToString("F2") +
                          " diff " + (got - expected).ToString("F2"));
            }
        }
    }
}
#endif
