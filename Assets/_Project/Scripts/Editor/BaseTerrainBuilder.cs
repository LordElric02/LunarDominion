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
    /// PHASES 1-2: build the TerrainData, a scene preview, and
    /// the splat paint. No runtime change — BaseView still builds
    /// the old ground (TerrainMesh FBX + paved hex) until the
    /// later phases swap it over, so the base is never broken
    /// mid-flight.
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
    ///   Lunar Dominion/Base Terrain/3. Paint Splat Map (Phase 2)
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

        // ---- Phase 2 surface paint ----
        // The interior in the exterior's language: pad dirt as
        // the fill, the road network as worn dirt TRACKS (a
        // second earth layer — no painted markings, no dashes),
        // worked-earth discs under the building plots, and
        // outside the walls the transplant's rules: grass base,
        // rock by slope. The road network is transcribed from
        // BuildGroundPavedSand.py's ROAD_SEGMENTS (layout space,
        // half-width 1.5) so the tracks land exactly where the
        // painted roads — and the rover — run today.
        private static readonly Vector4[] RoadSegments =
        {
            new Vector4(-31.5f, 14.25f, 31.5f, 14.25f),     // ring north
            new Vector4(-31.5f, -14.25f, 31.5f, -14.25f),   // ring south
            new Vector4(-31.5f, -17.25f, -31.5f, 17.25f),   // ring west
            new Vector4(31.5f, -17.25f, 31.5f, 17.25f),     // ring east
            new Vector4(-41.25f, 0f, 54f, 0f),              // main street: Lab to the gate
            new Vector4(0f, 9f, 0f, 20.25f),                // north spine to Helium-3
            new Vector4(0f, -24.25f, 0f, -9f),              // south spine to Aegis
        };
        private const float RoadCoreHalfWidth = 1.5f;   // the painted road's half-width
        private const float RoadFeather = 3.4f;         // track weight reaches 0 here
        private const float PlazaRadius = 6.0f;         // the paved texture's plaza disc
        private const float PlazaFeather = 7.6f;

        // Building plots (layout space) with worn-disc radii
        // matched to each building's footprint class.
        private static readonly (string name, float x, float z, float r)[] PlotDiscs =
        {
            ("Nexus",     0f,     0f,   15f),
            ("Hydro",    -31.5f,  21f,  11f),
            ("Alloy",     31.5f,  21f,  11f),
            ("Helium",    0f,     24f,  11f),
            ("Marine",   -28.5f, -22f,  11f),
            ("Nova",      28.5f, -22f,  11f),
            ("Aegis",     0f,    -28f,  11f),
            ("Lab",      -45f,    0f,   12f),
            ("Hospital",  45f,    0f,   12f),
        };
        private const float PlotDiscStrength = 0.5f;

        [MenuItem("Lunar Dominion/Base Terrain/3. Paint Splat Map (Phase 2)")]
        public static void PaintSplatMap()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                EditorUtility.DisplayDialog("No base terrain data yet",
                    "Run 'Lunar Dominion/Base Terrain/1. Build Base Terrain Data' first.", "OK");
                return;
            }

            // Layer picks. The pad dirt is the Phase 1 layer; the
            // track wants a SECOND earth layer so the roads read
            // against the fill — searched by Path/Trail/Sand/
            // Ground, excluding the pad pick. Every TerrainLayer
            // in the project is logged, so if the track pick is
            // wrong the Console names what the pack actually has.
            var padDirt = FindTerrainLayerByKeywords("Dirt", "Path", "Ground", "Sand", "Soil");
            var track = FindTrackLayer(padDirt);
            var grass = FindTerrainLayer("Grass_Moss_TerrainLayer");
            var rock = FindTerrainLayerByKeywords("Rock");
            LogAllTerrainLayers();
            if (padDirt == null)
            {
                EditorUtility.DisplayDialog("No dirt layer found",
                    "No TerrainLayer matching Dirt/Path/Ground/Sand/Soil exists in the project — " +
                    "the paint cannot run. The Console lists every layer found.", "OK");
                return;
            }
            if (track == null)
            {
                Debug.LogWarning("[BaseTerrain] No second earth layer for the road tracks — " +
                                 "roads will paint in the pad dirt (no contrast). Layer inventory is logged above.");
                track = padDirt;
            }

            var layers = new System.Collections.Generic.List<TerrainLayer> { padDirt };
            int dirtCh = 0;
            int trackCh = -1, grassCh = -1, rockCh = -1;
            if (track != padDirt) { trackCh = layers.Count; layers.Add(track); }
            else trackCh = dirtCh;
            if (grass != null) { grassCh = layers.Count; layers.Add(grass); }
            if (rock != null) { rockCh = layers.Count; layers.Add(rock); }
            data.terrainLayers = layers.ToArray();
            int channels = layers.Count;

            int ares = data.alphamapResolution;
            var alpha = new float[ares, ares, channels];
            try
            {
                for (int y = 0; y < ares; y++)
                {
                    if (y % 128 == 0)
                    {
                        EditorUtility.DisplayProgressBar("Painting base terrain",
                            "Compositing dirt, tracks, grass and rock...", y / (float)ares);
                    }
                    float wz = -TerrainHalf + (y / (float)(ares - 1)) * TerrainSize;
                    for (int x = 0; x < ares; x++)
                    {
                        float wx = -TerrainHalf + (x / (float)(ares - 1)) * TerrainSize;
                        var lp = LayoutYawInverse * new Vector3(wx, 0f, wz);
                        float lx = lp.x, lz = lp.z;
                        float dHex = SignedOutsideDistance(lx, lz);

                        // Track weight: road segments + plaza.
                        float trackW = 0f;
                        for (int s = 0; s < RoadSegments.Length; s++)
                        {
                            var seg = RoadSegments[s];
                            float dist = SegmentDistance(lx, lz, seg.x, seg.y, seg.z, seg.w);
                            float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RoadCoreHalfWidth, RoadFeather, dist));
                            if (w > trackW) trackW = w;
                        }
                        float pr = Mathf.Sqrt(lx * lx + lz * lz);
                        float plazaW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlazaRadius, PlazaFeather, pr));
                        if (plazaW > trackW) trackW = plazaW;
                        // Worked earth under the buildings.
                        for (int p = 0; p < PlotDiscs.Length; p++)
                        {
                            var plot = PlotDiscs[p];
                            float dx = lx - plot.x, dz = lz - plot.z;
                            float dist = Mathf.Sqrt(dx * dx + dz * dz);
                            float w = PlotDiscStrength * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(plot.r * 0.55f, plot.r, dist)));
                            if (w > trackW) trackW = w;
                        }

                        // Base fill: dirt inside the pad, grass
                        // fading in outside the wall apron.
                        float grassBase = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 9f, dHex));
                        float dirtBase = 1f - grassBase;

                        // Rock by slope, on the grass side only.
                        float rockW = 0f;
                        if (rockCh >= 0 && grassBase > 0f)
                        {
                            float u = (wx + TerrainHalf) / TerrainSize;
                            float v = (wz + TerrainHalf) / TerrainSize;
                            rockW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 24f, data.GetSteepness(u, v)));
                        }

                        float tW = trackW;
                        float dW = dirtBase * (1f - tW);
                        float gW = grassBase * (1f - rockW) * (1f - tW);
                        float rW = grassBase * rockW * (1f - tW);
                        // Missing layers fold into the dirt.
                        if (grassCh < 0) { dW += gW; gW = 0f; }
                        if (rockCh < 0) { dW += rW; rW = 0f; }
                        float sum = dW + tW + gW + rW;
                        if (sum <= 0f) { dW = 1f; sum = 1f; }
                        if (trackCh == dirtCh)
                        {
                            // Fallback (single earth layer): the
                            // track weight folds into the dirt.
                            alpha[y, x, dirtCh] = (dW + tW) / sum;
                        }
                        else
                        {
                            alpha[y, x, dirtCh] = dW / sum;
                            alpha[y, x, trackCh] = tW / sum;
                        }
                        if (grassCh >= 0) alpha[y, x, grassCh] = gW / sum;
                        if (rockCh >= 0) alpha[y, x, rockCh] = rW / sum;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[BaseTerrain] Splat painted at " + ares + " with " + channels + " layers: " +
                      "dirt=" + padDirt.name + " (ch " + dirtCh + "), track=" + track.name + " (ch " + trackCh + "), " +
                      "grass=" + (grass != null ? grass.name : "MISSING") + ", rock=" + (rock != null ? rock.name : "MISSING") + ". " +
                      "Re-run item 2 to refresh the preview, or check it in Play after Phase 3.");
        }

        // Distance from a point to a segment (the Python
        // generator's dist_and_arclen without the arc length).
        private static float SegmentDistance(float px, float pz, float ax, float az, float bx, float bz)
        {
            float abx = bx - ax, abz = bz - az;
            float ab2 = abx * abx + abz * abz;
            float t = ab2 > 0f ? Mathf.Clamp01(((px - ax) * abx + (pz - az) * abz) / ab2) : 0f;
            float cx = ax + t * abx, cz = az + t * abz;
            float dx = px - cx, dz = pz - cz;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // The track layer: a second earth layer, distinct from
        // the pad dirt. Path/Trail first (a path layer is made
        // for tracks), then Sand/Ground.
        private static TerrainLayer FindTrackLayer(TerrainLayer exclude)
        {
            string[] guids = AssetDatabase.FindAssets("t:TerrainLayer");
            foreach (var kw in new[] { "Path", "Trail", "Sand", "Ground" })
            {
                foreach (var g in guids)
                {
                    var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g));
                    if (layer != null && layer != exclude &&
                        layer.name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                        return layer;
                }
            }
            return null;
        }

        private static TerrainLayer FindTerrainLayer(string exactName)
        {
            string[] guids = AssetDatabase.FindAssets(exactName + " t:TerrainLayer");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static void LogAllTerrainLayers()
        {
            string[] guids = AssetDatabase.FindAssets("t:TerrainLayer");
            var names = new System.Collections.Generic.List<string>();
            foreach (var g in guids)
            {
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GUIDToAssetPath(g));
                if (layer != null) names.Add(layer.name);
            }
            Debug.Log("[BaseTerrain] TerrainLayers in project (" + names.Count + "): " + string.Join(", ", names));
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
