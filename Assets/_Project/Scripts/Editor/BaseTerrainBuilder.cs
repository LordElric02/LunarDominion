#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace LunarDominion.Editor
{
    /// <summary>
    /// Base terrain (Patrick 2026-11-27, Route 2 — full terrain
    /// interior; plan: files/Lunar-Dominion-Base-Terrain-Plan-2026-11-27.md).
    /// The exterior map's Unity Terrain reads "pretty and sharp"
    /// while the base interior — one stretched texture over a flat
    /// pad — reads "a bit cartoonish", so the base ground gets the
    /// same treatment as the transplant (TerrainTransplantBuilder
    /// is the template for everything here).
    ///
    /// PHASE 1 ONLY: build the TerrainData and a scene preview.
    /// No runtime change — BaseView still builds the old ground
    /// (TerrainMesh FBX + paved hex) until the later phases swap
    /// it over, so the base is never broken mid-flight.
    ///
    /// Geometry facts this bake depends on (all read from
    /// BaseView.cs, 2026-11-27):
    ///  - The layout root sits at the world origin with a
    ///    45-degree yaw; every building plot, the wall and the
    ///    roads are placed in that rotated layout frame.
    ///  - A Terrain cannot be rotated, so this terrain is
    ///    axis-aligned in WORLD space and the bake converts each
    ///    sample through the inverse yaw into layout space, where
    ///    the wall hexagon lives.
    ///  - The old ground square is 132x132 in layout space; its
    ///    rotated corners reach 66 * sqrt(2) = 93.3 in world, so a
    ///    192x192 terrain centred on the origin covers it.
    ///  - The pad is the wall hexagon scaled by 1.04 (the same
    ///    apron the paved floor uses), held at exactly y = 0 so
    ///    every building, wall piece and the rover keep their
    ///    current numbers when Phase 3 grounds them on it.
    ///
    /// Menu:
    ///   Lunar Dominion/Base Terrain/1. Build Base Terrain Data
    ///   Lunar Dominion/Base Terrain/2. Create Base Terrain In Scene
    /// </summary>
    public static class BaseTerrainBuilder
    {
        // Terrain footprint: 192x192 world units, centred on the
        // origin; the object is placed at (-96, 0, -96).
        private const float TerrainSize = 192f;
        private const float TerrainHalf = TerrainSize / 2f;   // 96
        // Height span of the TerrainData: heights are stored
        // normalised, so this is the ceiling the relief is
        // normalised against. The pad is height 0.
        private const float HeightSpan = 16f;
        // Gentle relief outside the pad: full rise by
        // ReliefRun metres past the pad edge, capped at
        // ReliefMax, modulated by rolling Perlin variation so the
        // rim is not a uniform cone.
        private const float ReliefMax = 6.5f;
        private const float ReliefRun = 38f;

        private const int HeightmapResolution = 513;
        private const int AlphamapResolution = 1024;

        // Resources/Terrain/ alongside the exterior's assets, so
        // the Phase 3 runtime can load them the same way.
        private const string TerrainFolder = "Assets/_Project/Resources/Terrain";
        private const string TerrainAssetPath = TerrainFolder + "/BaseTerrain.asset";
        private const string MaterialPath = TerrainFolder + "/BaseTerrainMat.mat";

        // The dirt mood from the old ground materials (tint
        // (0.60, 0.56, 0.52), main e57be603). Recorded as the
        // target, NOT applied here: TerrainLayer has no tint
        // field and URP Terrain/Lit has no global colour, so
        // there is nothing honest to write it into in Phase 1.
        // The base grade (postExposure 0.10) currently carries
        // the mood; Phase 3 settles the mechanism.
        private static readonly Color DirtMoodTint = new Color(0.60f, 0.56f, 0.52f);

        // The wall hexagon in layout space (BaseView's corners,
        // including the 2026-11-26 south expansion), scaled by
        // the paved floor's 1.04 apron when tested.
        private static readonly Vector2[] WallHex =
        {
            new Vector2(54f, 19.5f), new Vector2(0f, 39f),
            new Vector2(-54f, 19.5f), new Vector2(-54f, -23f),
            new Vector2(0f, -46f), new Vector2(54f, -23f),
        };
        private const float PadApron = 1.04f;

        // Layout <-> world: the layout root's yaw, and its
        // inverse for world -> layout.
        private static readonly Quaternion LayoutYaw = Quaternion.Euler(0f, 45f, 0f);
        private static readonly Quaternion LayoutYawInverse = Quaternion.Inverse(LayoutYaw);

        [MenuItem("Lunar Dominion/Base Terrain/1. Build Base Terrain Data")]
        public static void BuildBaseTerrainData()
        {
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath) != null)
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Rebuild base terrain data?",
                    "BaseTerrain.asset already exists. Rebuilding regenerates the heightmap " +
                    "and WIPES any hand-sculpting and paint done on it. Continue?",
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
            data.size = new Vector3(TerrainSize, HeightSpan, TerrainSize);
            data.alphamapResolution = AlphamapResolution;

            int res = data.heightmapResolution;
            var heights = new float[res, res];
            try
            {
                for (int iz = 0; iz < res; iz++)
                {
                    if (iz % 64 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Building base terrain",
                            "Baking pad and relief...", iz / (float)res);
                    }
                    float wz = -TerrainHalf + (iz / (float)(res - 1)) * TerrainSize;
                    for (int ix = 0; ix < res; ix++)
                    {
                        float wx = -TerrainHalf + (ix / (float)(res - 1)) * TerrainSize;
                        heights[iz, ix] = Mathf.Clamp01(BaseGroundHeight(wx, wz) / HeightSpan);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            data.SetHeights(0, 0, heights);

            TryApplyDirtLayer(data);

            AssetDatabase.CreateAsset(data, TerrainAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BaseTerrain] Terrain data built: " + TerrainAssetPath +
                      " size " + data.size + " heightmap " + res + "x" + res +
                      " (flat pad inside the wall hexagon, relief outside; dirt mood tint " +
                      DirtMoodTint + " recorded, carried by the grade in Phase 1).");
            LogProbes(data);
            Selection.activeObject = data;
        }

        [MenuItem("Lunar Dominion/Base Terrain/2. Create Base Terrain In Scene")]
        public static void CreateBaseTerrainInScene()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                EditorUtility.DisplayDialog("No base terrain data yet",
                    "Run 'Lunar Dominion/Base Terrain/1. Build Base Terrain Data' first.", "OK");
                return;
            }

            var existing = GameObject.Find("BaseTerrain (Preview)");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            // Unity 6 removed Terrain.CreateGameObject — build it by hand.
            var go = new GameObject("BaseTerrain (Preview)");
            var terrainComponent = go.AddComponent<Terrain>();
            terrainComponent.terrainData = data;
            terrainComponent.drawTreesAndFoliage = false;
            var terrainCollider = go.AddComponent<TerrainCollider>();
            terrainCollider.terrainData = data;

            // An explicit URP terrain material is required: with no
            // materialTemplate this project renders the terrain
            // magenta (the built-in fallback shader fails under URP).
            var mat = GetOrCreateTerrainMaterial();
            if (mat != null) terrainComponent.materialTemplate = mat;

            go.transform.position = new Vector3(-TerrainHalf, 0f, -TerrainHalf);
            Selection.activeGameObject = go;

            Debug.Log("[BaseTerrain] Preview terrain placed at " + go.transform.position +
                      ". It is a PREVIEW: BaseView still renders the old ground, which sits " +
                      "in the same place — hide or delete the old ground objects (or this " +
                      "preview) to compare, and expect z-fighting where the two pads overlap.");
            LogProbes(data);
        }

        // The Phase 1 height field, in WORLD space. Inside the
        // (apron-scaled) wall hexagon: exactly 0. Outside: a
        // smooth rise over ReliefRun metres to ReliefMax, rolled
        // by two Perlin octaves so the rim reads as ground, not
        // as a cone. Deterministic — same bake every run.
        private static float BaseGroundHeight(float wx, float wz)
        {
            // World -> layout (the frame the hexagon lives in).
            var lp = LayoutYawInverse * new Vector3(wx, 0f, wz);
            float d = SignedOutsideDistance(lp.x, lp.z);
            if (d <= 0f) return 0f;

            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / ReliefRun));
            float rolling = 0.65f + 0.35f * Mathf.PerlinNoise(wx * 0.021f + 13.7f, wz * 0.021f + 4.2f);
            float swell = 1.2f * Mathf.PerlinNoise(wx * 0.006f + 3.1f, wz * 0.006f + 8.4f);
            return ReliefMax * t * rolling + swell * t;
        }

        // Signed distance from a layout-space point to the
        // apron-scaled wall hexagon: <= 0 inside, > 0 outside
        // (the worst edge's outward distance). Convex polygon, so
        // the max over edges is the distance to the boundary.
        private static float SignedOutsideDistance(float px, float pz)
        {
            // Polygon orientation from the shoelace sum, so the
            // test does not depend on the corners' winding.
            float area2 = 0f;
            for (int i = 0; i < WallHex.Length; i++)
            {
                var a = WallHex[i] * PadApron;
                var b = WallHex[(i + 1) % WallHex.Length] * PadApron;
                area2 += a.x * b.y - b.x * a.y;
            }
            float orient = area2 >= 0f ? 1f : -1f;

            float d = float.NegativeInfinity;
            for (int i = 0; i < WallHex.Length; i++)
            {
                var a = WallHex[i] * PadApron;
                var b = WallHex[(i + 1) % WallHex.Length] * PadApron;
                float ex = b.x - a.x, ez = b.y - a.y;
                float len = Mathf.Sqrt(ex * ex + ez * ez);
                if (len <= 0f) continue;
                // 2D cross of edge x (p - a): positive when p is
                // left of the directed edge.
                float cross = ex * (pz - a.y) - ez * (px - a.x);
                float outward = -orient * cross / len;
                if (outward > d) d = outward;
            }
            return d;
        }

        // Phase 1 paint baseline: the exterior's dirt layer at
        // full strength everywhere. Phase 2 paints properly
        // (roads, worn pads, grass and rock outside). If no dirt
        // layer is found the terrain is left unpainted, like the
        // transplant builder leaves it.
        private static void TryApplyDirtLayer(TerrainData data)
        {
            var dirt = FindTerrainLayerByKeywords("Dirt", "Path", "Ground", "Sand", "Soil");
            if (dirt == null)
            {
                Debug.LogWarning("[BaseTerrain] No dirt TerrainLayer found in the project — " +
                                 "terrain left unpainted (Phase 2).");
                return;
            }
            data.terrainLayers = new[] { dirt };
            int ares = data.alphamapResolution;
            var alpha = new float[ares, ares, 1];
            for (int y = 0; y < ares; y++)
                for (int x = 0; x < ares; x++)
                    alpha[y, x, 0] = 1f;
            data.SetAlphamaps(0, 0, alpha);
            Debug.Log("[BaseTerrain] Dirt layer applied: " + dirt.name);
        }

        private static Material GetOrCreateTerrainMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader == null)
            {
                Debug.LogError("[BaseTerrain] Shader 'Universal Render Pipeline/Terrain/Lit' not found — " +
                               "URP terrain shader unavailable in this project; terrain will render magenta.");
                return null;
            }
            var mat = new Material(shader) { name = "BaseTerrainMat" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[BaseTerrain] Terrain material created: " + MaterialPath + " (shader " + shader.name + ")");
            return mat;
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

        // Probes are given in LAYOUT space (the frame Patrick
        // thinks in): the building plots must read exactly 0, the
        // outside probes must read relief. Heights come from the
        // baked data, converted layout -> world -> terrain UV.
        private static void LogProbes(TerrainData data)
        {
            var probes = new (string name, float lx, float lz, bool pad)[]
            {
                ("Nexus plot",      0f,     0f,    true),
                ("Marine plot",    -28.5f, -22f,   true),
                ("Aegis plot",      0f,    -28f,   true),
                ("Hospital plot",   45f,     0f,   true),
                ("Lab plot",       -45f,     0f,   true),
                ("south outside",   0f,    -70f,  false),
                ("north-east out",  90f,    60f,  false),
            };
            foreach (var p in probes)
            {
                var wp = LayoutYaw * new Vector3(p.lx, 0f, p.lz);
                float u = (wp.x + TerrainHalf) / TerrainSize;
                float v = (wp.z + TerrainHalf) / TerrainSize;
                // GetInterpolatedHeight returns terrain-local
                // height (normalised x size.y), i.e. world
                // height here since the span bake divides by it.
                float h = data.GetInterpolatedHeight(u, v);
                string verdict = p.pad
                    ? (Mathf.Abs(h) < 0.01f ? "OK (flat)" : "FAIL — pad not flat")
                    : (h > 0.05f ? "OK (relief)" : "CHECK — no relief");
                Debug.Log("[BaseTerrain] probe " + p.name +
                          " layout (" + p.lx + ", " + p.lz + ") height = " + h.ToString("F2") +
                          " — " + verdict);
            }
        }
    }
}
#endif
