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
        // heights are clamped to [-5.2, 8.0]. HeightScale 3 spanned
        // [-15.6, 24.0] and read too flat at the x3 footprint
        // (Patrick 2026-11-20), so 6: the terrain spans
        // [-31.2, 48.0] with double the slope drama.
        private const float HeightScale = 6f;
        private const float AnalyticMin = -5.2f;
        private const float AnalyticMax = 8.0f;

        private const int HeightmapResolution = 2049;
        private const string TerrainFolder = "Assets/_Project/Terrain";
        private const string TerrainAssetPath = TerrainFolder + "/LunarTerrain.asset";
        private const string MaterialPath = TerrainFolder + "/LunarTerrainMat.mat";
        private const string GrassTexturePath = TerrainFolder + "/GrassBlades.png";
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

        // ---- Grass / wind test (Patrick 2026-11-20) ----
        // Billboard grass details driven by the TerrainData wind
        // settings (the Speed/Size/Bending Patrick saw in the
        // Inspector). Test-grade: grass goes everywhere the slope is
        // gentle; the Phase 4 bake will keep it off roads and pads.
        // NOTE: rebuilding the data (item 1) wipes this — re-run 3.
        [MenuItem("Lunar Dominion/Terrain Transplant/3. Add Grass Details (Test)")]
        public static void AddGrassDetails()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                EditorUtility.DisplayDialog("No terrain data yet",
                    "Run 'Lunar Dominion/Terrain Transplant/1. Build Terrain Data' first.", "OK");
                return;
            }

            var grassTex = GetOrCreateGrassTexture();
            if (grassTex == null) return;

            var proto = new DetailPrototype
            {
                prototypeTexture = grassTex,
                usePrototypeMesh = false,
                renderMode = DetailRenderMode.GrassBillboard,
                minWidth = 1.4f,
                maxWidth = 2.2f,
                minHeight = 1.0f,
                maxHeight = 2.2f,
                noiseSpread = 0.35f,
                healthyColor = new Color(0.42f, 0.58f, 0.22f),
                dryColor = new Color(0.55f, 0.50f, 0.26f),
            };
            data.detailPrototypes = new[] { proto };
            data.RefreshPrototypes();

            const int dres = 1024;
            data.SetDetailResolution(dres, 32);
            var map = new int[dres, dres];
            try
            {
                for (int y = 0; y < dres; y++)
                {
                    if (y % 128 == 0)
                        EditorUtility.DisplayProgressBar("Terrain Transplant",
                            "Scattering grass details...", y / (float)dres);
                    float v = y / (float)(dres - 1);
                    for (int x = 0; x < dres; x++)
                    {
                        float u = x / (float)(dres - 1);
                        if (data.GetSteepness(u, v) > 25f) continue; // no grass on rock
                        float wx = -NewHalfX + u * NewHalfX * 2f;
                        float wz = -NewHalfZ + v * NewHalfZ * 2f;
                        float n = Mathf.PerlinNoise(wx * 0.006f + 40f, wz * 0.006f + 40f);
                        map[y, x] = Mathf.Clamp(Mathf.RoundToInt((0.30f + 0.70f * n) * 12f), 0, 15);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            data.SetDetailLayer(0, 0, 0, map);

            // The wind Patrick saw in the Inspector lives on the data.
            data.wavingGrassSpeed = 0.6f;
            data.wavingGrassStrength = 0.4f;
            data.wavingGrassAmount = 0.5f;
            data.wavingGrassTint = new Color(0.75f, 0.80f, 0.60f);

            // Make sure a test terrain already in the scene shows the
            // details from a reasonable zoom-in distance.
            var go = GameObject.Find("LunarTerrain (Transplant Test)");
            if (go != null)
            {
                var t = go.GetComponent<Terrain>();
                if (t != null) t.detailObjectDistance = 160f;
            }

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTransplant] Grass details added (billboard, detail res " + dres +
                      ", wind speed 0.6 / size 0.4 / bending 0.5). Zoom low or press Play to see it wave.");
        }

        // Code-drawn grass-blade sprite (the image tools available to
        // Ember cannot emit transparency, and billboard grass needs a
        // real alpha channel). ~90 tapered blades, transparent bg.
        private static Texture2D GetOrCreateGrassTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath);
            if (existing != null) return existing;

            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            var rng = new System.Random(12345);
            var dark = new Color(0.15f, 0.30f, 0.07f);
            var light = new Color(0.46f, 0.63f, 0.22f);
            for (int b = 0; b < 90; b++)
            {
                float baseX = (float)rng.NextDouble() * (S - 8) + 4f;
                float h = S * (0.55f + (float)rng.NextDouble() * 0.45f);
                float lean = (float)(rng.NextDouble() * 24.0 - 12.0);
                float w0 = 3.5f + (float)rng.NextDouble() * 2.5f;
                Color col = Color.Lerp(dark, light, (float)rng.NextDouble());
                for (int y = 0; y < h; y++)
                {
                    float t = y / h;
                    float cx = baseX + lean * t * t;
                    float w = Mathf.Lerp(w0, 0.5f, t);
                    Color c = Color.Lerp(col * 0.7f, col * 1.15f, t);
                    c.a = 1f;
                    Color32 c32 = c;
                    for (int x = Mathf.CeilToInt(cx - w); x <= Mathf.FloorToInt(cx + w); x++)
                    {
                        if (x < 0 || x >= S || y < 0 || y >= S) continue;
                        px[y * S + x] = c32;
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply();

            string fullPath = System.IO.Path.Combine(Application.dataPath, "_Project/Terrain/GrassBlades.png");
            System.IO.File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(GrassTexturePath);
            var importer = AssetImporter.GetAtPath(GrassTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            Debug.Log("[TerrainTransplant] Grass blade texture generated: " + GrassTexturePath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath);
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
