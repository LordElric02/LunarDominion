#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
    ///   Lunar Dominion/Terrain Transplant/3. Paint Splat Map (Phase 4)
    ///   Lunar Dominion/Terrain Transplant/4. Add Grass Details (Phase 4)
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
        // Resources/ so the runtime (ExteriorView) can load the
        // TerrainData + material with Resources.Load — Phase 3.
        private const string TerrainFolder = "Assets/_Project/Resources/Terrain";
        private const string TerrainAssetPath = TerrainFolder + "/LunarTerrain.asset";
        private const string MaterialPath = TerrainFolder + "/LunarTerrainMat.mat";
        private const string GrassTexturePath = TerrainFolder + "/GrassBlades.png";
        private const string GrassTuftPath = TerrainFolder + "/GrassTuft.png";
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
                AssetDatabase.CreateFolder("Assets/_Project/Resources", "Terrain");
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

        // ---- Phase 4 surface paint ----
        // Splat rules (transplant plan, Phase 4): Grass_Moss
        // everywhere (the lush look is a hard requirement), Rock
        // by slope, and a dirt layer along the road network plus a
        // clearing at every site - all painted from ExteriorView's
        // own tables, so the painted roads are exactly the routes
        // the marches walk. Re-running item 1 wipes the paint and
        // the grass: re-run 3, then 4.
        private const int SplatResolution = 2048;

        [MenuItem("Lunar Dominion/Terrain Transplant/3. Paint Splat Map (Phase 4)")]
        public static void PaintSplatMap()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                EditorUtility.DisplayDialog("No terrain data yet",
                    "Run 'Lunar Dominion/Terrain Transplant/1. Build Terrain Data' first.", "OK");
                return;
            }
            var grassLayer = FindTerrainLayer(BaseLayerName);
            if (grassLayer == null)
            {
                EditorUtility.DisplayDialog("Grass_Moss layer missing",
                    "The Terrain Asset Pack does not appear to be imported (no " + BaseLayerName + " found). Import it, then re-run.", "OK");
                return;
            }
            var rockLayer = FindTerrainLayerByKeywords("Rock");
            var dirtLayer = FindTerrainLayerByKeywords("Dirt", "Path", "Ground", "Sand", "Soil");
            var layers = new List<TerrainLayer> { grassLayer };
            int rockCh = -1, dirtCh = -1;
            if (rockLayer != null) { rockCh = layers.Count; layers.Add(rockLayer); }
            if (dirtLayer != null) { dirtCh = layers.Count; layers.Add(dirtLayer); }
            Debug.Log("[TerrainTransplant] Paint layers: grass=" + grassLayer.name +
                      ", rock=" + (rockLayer != null ? rockLayer.name : "MISSING") +
                      ", dirt=" + (dirtLayer != null ? dirtLayer.name : "MISSING"));

            int ares = SplatResolution;
            var dirt = new float[ares, ares];
            try
            {
                // Roads: every edge, plus every site's march route
                // from the base (which adds the driveway-rule
                // direct roads and the junction-to-site legs -
                // neither is an edge).
                var junctions = ExteriorView.RoadJunctionTable;
                var edges = ExteriorView.RoadEdgeTable;
                for (int e = 0; e < edges.GetLength(0); e++)
                {
                    var a = junctions[edges[e, 0]];
                    var b = junctions[edges[e, 1]];
                    StampSegment(dirt, ares, a.x, a.y, b.x, b.y, 4.5f, 9.5f, 1f);
                }
                foreach (var (pos, radius) in ExteriorView.PaintSites())
                {
                    var path = ExteriorView.RoadPathForPaint(Vector3.zero, new Vector3(pos.x, 0f, pos.y));
                    var prev = Vector2.zero;
                    foreach (var wp in path)
                    {
                        StampSegment(dirt, ares, prev.x, prev.y, wp.x, wp.z, 4.5f, 9.5f, 1f);
                        prev = new Vector2(wp.x, wp.z);
                    }
                    StampDisc(dirt, ares, pos.x, pos.y, radius, 0.6f);
                }
                StampDisc(dirt, ares, 0f, 0f, 180f, 0.6f); // the base clearing

                data.terrainLayers = layers.ToArray();
                data.alphamapResolution = ares;
                var alpha = new float[ares, ares, layers.Count];
                for (int y = 0; y < ares; y++)
                {
                    if (y % 128 == 0)
                        EditorUtility.DisplayProgressBar("Terrain Transplant",
                            "Painting splat map...", y / (float)ares);
                    float v = y / (float)(ares - 1);
                    for (int x = 0; x < ares; x++)
                    {
                        float u = x / (float)(ares - 1);
                        float rockW = rockCh >= 0
                            ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(24f, 42f, data.GetSteepness(u, v)))
                            : 0f;
                        float dirtW = dirtCh >= 0 ? dirt[y, x] : 0f;
                        float g = (1f - rockW) * (1f - dirtW);
                        float r = rockW * (1f - dirtW * 0.65f);
                        float d = dirtW;
                        float sum = g + r + d;
                        if (sum <= 0f) { g = 1f; sum = 1f; }
                        alpha[y, x, 0] = g / sum;
                        if (rockCh >= 0) alpha[y, x, rockCh] = r / sum;
                        if (dirtCh >= 0) alpha[y, x, dirtCh] = d / sum;
                    }
                }
                data.SetAlphamaps(0, 0, alpha);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTransplant] Splat painted at " + ares + " with " + layers.Count +
                      " layers. Run item 4 next for the grass.");
        }

        private static TerrainLayer FindTerrainLayer(string exactName)
        {
            string[] guids = AssetDatabase.FindAssets(exactName + " t:TerrainLayer");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static TerrainLayer FindTerrainLayerByKeywords(params string[] keywords)
        {
            string[] guids = AssetDatabase.FindAssets("t:TerrainLayer");
            foreach (var kw in keywords)
            {
                foreach (var g in guids)
                {
                    var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g));
                    if (layer != null && layer.name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        return layer;
                }
            }
            return null;
        }

        private static bool LayerNameMatches(string name, params string[] keywords)
        {
            foreach (var kw in keywords)
                if (name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // World <-> splat texel mapping (texel [y, x] = [z, x]).
        private static int WorldToTexelX(float wx, int ares) => Mathf.Clamp(Mathf.RoundToInt((wx + NewHalfX) / (NewHalfX * 2f) * (ares - 1)), 0, ares - 1);
        private static int WorldToTexelY(float wz, int ares) => Mathf.Clamp(Mathf.RoundToInt((wz + NewHalfZ) / (NewHalfZ * 2f) * (ares - 1)), 0, ares - 1);
        private static float TexelToWorldX(int x, int ares) => -NewHalfX + x / (float)(ares - 1) * NewHalfX * 2f;
        private static float TexelToWorldZ(int y, int ares) => -NewHalfZ + y / (float)(ares - 1) * NewHalfZ * 2f;

        // Max-blend a dirt band along a segment: full strength
        // inside the core half-width, feathering to zero.
        private static void StampSegment(float[,] field, int ares, float ax, float az, float bx, float bz, float core, float feather, float strength)
        {
            int x0 = WorldToTexelX(Mathf.Min(ax, bx) - feather, ares), x1 = WorldToTexelX(Mathf.Max(ax, bx) + feather, ares);
            int y0 = WorldToTexelY(Mathf.Min(az, bz) - feather, ares), y1 = WorldToTexelY(Mathf.Max(az, bz) + feather, ares);
            float dx = bx - ax, dz = bz - az;
            float lenSq = dx * dx + dz * dz;
            for (int y = y0; y <= y1; y++)
            {
                float wz = TexelToWorldZ(y, ares);
                for (int x = x0; x <= x1; x++)
                {
                    float wx = TexelToWorldX(x, ares);
                    float t = lenSq > 0f ? Mathf.Clamp01(((wx - ax) * dx + (wz - az) * dz) / lenSq) : 0f;
                    float px = ax + dx * t, pz = az + dz * t;
                    float dist = Mathf.Sqrt((wx - px) * (wx - px) + (wz - pz) * (wz - pz));
                    if (dist >= feather) continue;
                    float w = strength * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core, feather, dist)));
                    if (w > field[y, x]) field[y, x] = w;
                }
            }
        }

        // Max-blend a dirt disc: full strength to 55% of the
        // radius, feathering out at the rim.
        private static void StampDisc(float[,] field, int ares, float cx, float cz, float radius, float strength)
        {
            int x0 = WorldToTexelX(cx - radius, ares), x1 = WorldToTexelX(cx + radius, ares);
            int y0 = WorldToTexelY(cz - radius, ares), y1 = WorldToTexelY(cz + radius, ares);
            for (int y = y0; y <= y1; y++)
            {
                float wz = TexelToWorldZ(y, ares);
                for (int x = x0; x <= x1; x++)
                {
                    float wx = TexelToWorldX(x, ares);
                    float dist = Mathf.Sqrt((wx - cx) * (wx - cx) + (wz - cz) * (wz - cz));
                    if (dist >= radius) continue;
                    float w = strength * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius * 0.55f, radius, dist)));
                    if (w > field[y, x]) field[y, x] = w;
                }
            }
        }

        // ---- Grass details (Phase 4 production version) ----
        // Billboard grass as a ZOOM-IN detail only (Patrick
        // 2026-11-20): Detail Distance 160 culls it at map zoom.
        // Versus the item-3 test this replaces: bigger tufts, a
        // denser blade sprite, clumped meadow scatter, wind tuned
        // UP (the test's 0.6/0.4/0.5 read too subtle), and hard
        // exclusions from the painted splat - no grass on roads,
        // clearings or rock. Run AFTER item 3 (the paint); a
        // rebuild (item 1) wipes it.
        [MenuItem("Lunar Dominion/Terrain Transplant/4. Add Grass Details (Phase 4)")]
        public static void AddGrassDetails()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                EditorUtility.DisplayDialog("No terrain data yet",
                    "Run 'Lunar Dominion/Terrain Transplant/1. Build Terrain Data' first.", "OK");
                return;
            }
            int dirtCh = -1, rockCh = -1;
            var tls = data.terrainLayers;
            for (int i = 0; i < tls.Length; i++)
            {
                if (tls[i] == null) continue;
                if (dirtCh < 0 && LayerNameMatches(tls[i].name, "Dirt", "Path", "Ground", "Sand", "Soil")) dirtCh = i;
                if (rockCh < 0 && tls[i].name.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0) rockCh = i;
            }
            if (dirtCh < 0)
            {
                EditorUtility.DisplayDialog("Paint first",
                    "No dirt layer on the terrain yet - run '3. Paint Splat Map (Phase 4)' first, so the grass keeps off the roads and pads.", "OK");
                return;
            }
            int ares = data.alphamapResolution;
            var alphas = data.GetAlphamaps(0, 0, ares, ares);

            var grassTex = GetOrCreateGrassTuftTexture();
            if (grassTex == null) return;

            var proto = new DetailPrototype
            {
                prototypeTexture = grassTex,
                usePrototypeMesh = false,
                renderMode = DetailRenderMode.GrassBillboard,
                minWidth = 1.8f,
                maxWidth = 3.2f,
                minHeight = 1.6f,
                maxHeight = 3.4f,
                noiseSpread = 0.3f,
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
                        if (data.GetSteepness(u, v) > 25f) continue; // no grass on rock faces
                        int ax = Mathf.Min(ares - 1, Mathf.RoundToInt(u * (ares - 1)));
                        int ay = Mathf.Min(ares - 1, Mathf.RoundToInt(v * (ares - 1)));
                        float dirtW = alphas[ay, ax, dirtCh];
                        if (dirtW > 0.30f) continue; // roads + clearings stay bare
                        float rockW = rockCh >= 0 ? alphas[ay, ax, rockCh] : 0f;
                        float wx = -NewHalfX + u * NewHalfX * 2f;
                        float wz = -NewHalfZ + v * NewHalfZ * 2f;
                        float clump = Mathf.PerlinNoise(wx * 0.004f + 40f, wz * 0.004f + 40f);
                        float fine = Mathf.PerlinNoise(wx * 0.03f + 90f, wz * 0.03f + 90f);
                        float density = Mathf.Clamp01(clump * 1.25f - 0.18f) * (0.55f + 0.45f * fine);
                        density *= (1f - dirtW * 1.6f) * (1f - rockW);
                        map[y, x] = Mathf.Clamp(Mathf.RoundToInt(density * 15f), 0, 15);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            data.SetDetailLayer(0, 0, 0, map);

            // Wind tuned up from the test (Patrick: the test's
            // sway was "not waving a lot").
            data.wavingGrassSpeed = 0.85f;
            data.wavingGrassStrength = 0.65f;
            data.wavingGrassAmount = 0.8f;
            data.wavingGrassTint = new Color(0.75f, 0.80f, 0.60f);

            var go = GameObject.Find("LunarTerrain (Transplant Test)");
            if (go != null)
            {
                var t = go.GetComponent<Terrain>();
                if (t != null) t.detailObjectDistance = 160f;
            }

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTransplant] Grass details added (Phase 4: tuft billboards, road/pad/rock exclusions, " +
                      "wind speed 0.85 / size 0.65 / bending 0.8, detail distance 160). Zoom in close in Play to judge the sway.");
        }

        // Phase 5 flora targets: the old Blender exterior carried
        // its trees and rocks modelled into the mesh, so the
        // transplant starts bare — these counts restore roughly
        // the old map's dressing density across the x9 area.
        // Tune here after a Play look.
        private const int FloraTreeTarget = 7000;
        private const int FloraRockTarget = 650;

        [MenuItem("Lunar Dominion/Terrain Transplant/5. Scatter Flora (Phase 5)")]
        public static void ScatterFlora()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                EditorUtility.DisplayDialog("No terrain data yet",
                    "Run 'Lunar Dominion/Terrain Transplant/1. Build Terrain Data' first.", "OK");
                return;
            }
            int dirtCh = -1, rockCh = -1;
            var tls = data.terrainLayers;
            for (int i = 0; i < tls.Length; i++)
            {
                if (tls[i] == null) continue;
                if (dirtCh < 0 && LayerNameMatches(tls[i].name, "Dirt", "Path", "Ground", "Sand", "Soil")) dirtCh = i;
                if (rockCh < 0 && tls[i].name.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0) rockCh = i;
            }
            if (dirtCh < 0)
            {
                EditorUtility.DisplayDialog("Paint first",
                    "No dirt layer on the terrain yet - run '3. Paint Splat Map (Phase 4)' first, so the flora keeps off the roads and pads.", "OK");
                return;
            }

            // Discover the pack's vegetation and rock prefabs by
            // name (the paint item's approach): the Terrain Asset
            // Pack's trees Patrick used in the playground were
            // Heather_A and Plant_D. Picks are logged, so a wrong
            // pick is visible in the Console before any Play check.
            // The pack (Assets/TerrainSampleAssets/Prefabs, surveyed
            // from Patrick's Project window 2026-11-21) has NO
            // trees — it is all ground cover: bushes, ferns,
            // heather, plants, every one ~0.5-1.5 m. The first
            // scatter placed 7,000 of them at true scale and they
            // were invisible from map height — the scatter worked,
            // the plants are just tiny. So the flora runs in two
            // tiers at alien scale: the rounded bushes and heather
            // at 4-6.5x read as canopy shrubs from the map camera
            // (the old map's trees were mushroom-canopy blobs — the
            // same read), and the plants and ferns at 1.8-3x make
            // the mid layer. Exact names, no keyword fishing; the
            // keyword search remains only as a fallback.
            var canopyPrefabs = FindPrefabsExact("Bush_A", "Bush_B", "Heather_A", "Heather_B");
            var shrubPrefabs = FindPrefabsExact("Plant_A", "Plant_B", "Plant_C", "Plant_D", "Fern_A", "Fern_B", "Fern_C");
            if (canopyPrefabs.Count == 0 && shrubPrefabs.Count == 0)
            {
                shrubPrefabs = FindPrefabs(6,
                    new[] { "Heather", "Plant", "Tree", "Bush", "Shrub", "Fern" },
                    new[] { "Rock" });
            }
            var rockPrefabs = FindPrefabs(3,
                new[] { "Rock" },
                new[] { "Heather", "Plant", "Tree", "Bush", "Shrub", "Fern" });
            if (canopyPrefabs.Count == 0 && shrubPrefabs.Count == 0)
            {
                EditorUtility.DisplayDialog("No vegetation prefabs found",
                    "The TerrainSampleAssets prefabs (Bush_A, Heather_A, Plant_A, Fern_A, ...) were not found in the project. " +
                    "Check the Terrain Asset Pack import, then run item 5 again.", "OK");
                return;
            }
            Debug.Log("[TerrainTransplant] Flora prefabs: canopy=" + string.Join(", ", canopyPrefabs.ConvertAll(p => p.name)) +
                      " | shrubs=" + string.Join(", ", shrubPrefabs.ConvertAll(p => p.name)) +
                      " | rocks=" + (rockPrefabs.Count > 0 ? string.Join(", ", rockPrefabs.ConvertAll(p => p.name)) : "NONE"));

            var protos = new List<TreePrototype>();
            foreach (var p in canopyPrefabs) protos.Add(new TreePrototype { prefab = p });
            int canopyProtoCount = protos.Count;
            foreach (var p in shrubPrefabs) protos.Add(new TreePrototype { prefab = p });
            int vegProtoCount = protos.Count;
            foreach (var p in rockPrefabs) protos.Add(new TreePrototype { prefab = p });
            data.treePrototypes = protos.ToArray();
            data.RefreshPrototypes();

            int ares = data.alphamapResolution;
            var alphas = data.GetAlphamaps(0, 0, ares, ares);
            var rng = new System.Random(20261121);
            var instances = new List<TreeInstance>();
            int trees = 0, rocks = 0;
            const int maxAttempts = 300000;
            try
            {
                for (int attempt = 0; attempt < maxAttempts && (trees < FloraTreeTarget || rocks < FloraRockTarget); attempt++)
                {
                    if (attempt % 20000 == 0)
                        EditorUtility.DisplayProgressBar("Terrain Transplant",
                            "Scattering flora...", attempt / (float)maxAttempts);
                    float u = (float)rng.NextDouble();
                    float v = (float)rng.NextDouble();
                    float wx = -NewHalfX + u * NewHalfX * 2f;
                    float wz = -NewHalfZ + v * NewHalfZ * 2f;
                    // The base compound stays clear (its dirt disc
                    // already excludes most of this; make it certain).
                    if (wx * wx + wz * wz < 215f * 215f) continue;
                    int ax = Mathf.Min(ares - 1, Mathf.RoundToInt(u * (ares - 1)));
                    int ay = Mathf.Min(ares - 1, Mathf.RoundToInt(v * (ares - 1)));
                    float dirtW = alphas[ay, ax, dirtCh];
                    if (dirtW > 0.25f) continue; // roads + clearings stay bare
                    float rockW = rockCh >= 0 ? alphas[ay, ax, rockCh] : 0f;
                    float steep = data.GetSteepness(u, v);

                    if (trees < FloraTreeTarget && steep <= 22f && rockW <= 0.55f)
                    {
                        // Copses, not a uniform lawn: the same
                        // two-scale clumping the grass uses.
                        float clump = Mathf.PerlinNoise(wx * 0.004f + 40f, wz * 0.004f + 40f);
                        float keep = Mathf.Clamp01(clump * 1.7f - 0.55f) * (1f - rockW);
                        if (rng.NextDouble() < keep)
                        {
                            // Tier pick: about a third canopy
                            // (4-6.5x), the rest mid shrubs
                            // (1.8-3x) — see the discovery note.
                            bool canopy = canopyProtoCount > 0 && rng.NextDouble() < 0.34;
                            float s;
                            int protoIndex;
                            if (canopy || vegProtoCount <= canopyProtoCount)
                            {
                                s = 4.0f + (float)rng.NextDouble() * 2.5f;
                                protoIndex = rng.Next(canopyProtoCount);
                            }
                            else
                            {
                                s = 1.8f + (float)rng.NextDouble() * 1.2f;
                                protoIndex = canopyProtoCount + rng.Next(vegProtoCount - canopyProtoCount);
                            }
                            instances.Add(new TreeInstance
                            {
                                position = new Vector3(u, 0f, v),
                                widthScale = s,
                                heightScale = s * (0.85f + (float)rng.NextDouble() * 0.3f),
                                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                                prototypeIndex = protoIndex,
                                color = new Color32(255, 255, 255, 255),
                            });
                            trees++;
                            continue;
                        }
                    }
                    if (rocks < FloraRockTarget && rockPrefabs.Count > 0 && steep <= 48f)
                    {
                        // Rocks favour the slopes and the rocky
                        // ground; a scatter also lives on the flats.
                        float keep = 0.18f + steep / 48f * 0.5f + rockW * 0.3f;
                        if (rng.NextDouble() < keep)
                        {
                            float s = 0.7f + (float)rng.NextDouble() * 1.3f;
                            instances.Add(new TreeInstance
                            {
                                position = new Vector3(u, 0f, v),
                                widthScale = s,
                                heightScale = s * (0.75f + (float)rng.NextDouble() * 0.4f),
                                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                                prototypeIndex = vegProtoCount + rng.Next(rockPrefabs.Count),
                                color = new Color32(255, 255, 255, 255),
                            });
                            rocks++;
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            data.treeInstances = instances.ToArray();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTransplant] Flora scattered: " + trees + " flora (canopy + shrubs), " + rocks + " rocks " +
                      "(targets " + FloraTreeTarget + " / " + FloraRockTarget + "). Run item 2 to preview, or Play.");
        }

        // Find prefabs whose names match the keywords (earlier
        // keyword = stronger pick), preferring paths that look
        // like a terrain/nature pack. Names in `exclude` are
        // skipped. At most `max` distinct prefabs, best first.
        private static List<GameObject> FindPrefabs(int max, string[] keywords, string[] exclude)
        {
            var scored = new List<(int score, string name, string path)>();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                int slash = path.LastIndexOf('/');
                string name = slash >= 0 ? path.Substring(slash + 1) : path;
                if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - ".prefab".Length);
                if (exclude != null && LayerNameMatches(name, exclude)) continue;
                int kw = -1;
                for (int i = 0; i < keywords.Length; i++)
                {
                    if (name.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0) { kw = i; break; }
                }
                if (kw < 0) continue;
                int score = kw * 10;
                if (path.IndexOf("Terrain", StringComparison.OrdinalIgnoreCase) >= 0) score -= 2;
                if (path.IndexOf("Nature", StringComparison.OrdinalIgnoreCase) >= 0) score -= 1;
                scored.Add((score, name, path));
            }
            scored.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : string.Compare(a.name, b.name, StringComparison.Ordinal));
            var results = new List<GameObject>();
            var seen = new HashSet<string>();
            foreach (var s in scored)
            {
                if (results.Count >= max) break;
                if (!seen.Add(s.name)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(s.path);
                if (go != null) results.Add(go);
            }
            return results;
        }

        // Resolve prefabs by exact file name (first exact match
        // wins). FindAssets matches substrings, so the file name
        // is verified before loading.
        private static List<GameObject> FindPrefabsExact(params string[] names)
        {
            var results = new List<GameObject>();
            foreach (var name in names)
            {
                foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(g);
                    int slash = path.LastIndexOf('/');
                    string file = slash >= 0 ? path.Substring(slash + 1) : path;
                    if (!file.Equals(name + ".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) { results.Add(go); break; }
                }
            }
            return results;
        }

        // Code-drawn grass tuft sprite (the image tools available
        // to Ember cannot emit transparency, and billboard grass
        // needs a real alpha channel). ~150 tapered blades with
        // per-blade colour variation, transparent background.
        // Phase 4 version of the test sprite: denser and taller,
        // and written to the CURRENT terrain folder (the test
        // version wrote to the pre-Phase-3 folder path).
        private static Texture2D GetOrCreateGrassTuftTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTuftPath);
            if (existing != null) return existing;

            const int S = 256;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            var rng = new System.Random(777);
            var dark = new Color(0.13f, 0.28f, 0.06f);
            var light = new Color(0.50f, 0.66f, 0.24f);
            for (int b = 0; b < 150; b++)
            {
                float baseX = (float)rng.NextDouble() * (S - 8) + 4f;
                float h = S * (0.50f + (float)rng.NextDouble() * 0.52f);
                float lean = (float)(rng.NextDouble() * 30.0 - 15.0);
                float w0 = 3.0f + (float)rng.NextDouble() * 3.0f;
                Color col = Color.Lerp(dark, light, (float)rng.NextDouble());
                for (int y = 0; y < h; y++)
                {
                    float t = y / h;
                    float cx = baseX + lean * t * t;
                    float w = Mathf.Lerp(w0, 0.5f, t);
                    Color c = Color.Lerp(col * 0.65f, col * 1.2f, t);
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

            string fullPath = Application.dataPath + GrassTuftPath.Substring("Assets".Length);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));
            System.IO.File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(GrassTuftPath);
            var importer = AssetImporter.GetAtPath(GrassTuftPath) as TextureImporter;
            if (importer != null)
            {
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            Debug.Log("[TerrainTransplant] Grass tuft texture generated: " + GrassTuftPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTuftPath);
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
