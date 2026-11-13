using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LunarDominion.Boss
{
    /// <summary>The lava tube arena's atmosphere (design doc Part
    /// 2.3), built at runtime under the lair's arena root and toggled
    /// with the lair view: deep-blue Trilight ambient + exponential
    /// fog (captured on apply, restored on release — the lair shares
    /// the scene with the base and map, so nothing may leak), three
    /// white light shafts through the ceiling cracks (spotlights +
    /// fake volumetric cones; only one casts shadows), pulsing
    /// amber beacons (2 real point lights; the domes' shared
    /// emissive does the rest), drifting micro-dust, and steam at
    /// the vent grates (idle wisps; BurstSteam answers the
    /// boss's core venting). All materials are explicit URP shaders
    /// (project magenta rule).</summary>
    public class LavaTubeAmbience : MonoBehaviour
    {
        // Ceiling cracks in arena-local coords (Unity x, z) — the
        // LavaTube model's ceiling slabs keep these points open.
        private static readonly Vector2[] CrackXZ =
        {
            new Vector2(-8f, 4f), new Vector2(10f, -8f), new Vector2(0f, 16f),
        };

        private readonly List<Light> _beaconLights = new List<Light>();
        private readonly List<Material> _beaconMaterials = new List<Material>();
        private readonly List<Color> _beaconBaseEmission = new List<Color>();
        private readonly List<ParticleSystem> _steamSystems = new List<ParticleSystem>();
        private readonly List<float> _steamBurstTimers = new List<float>();
        private bool _phase2;
        private float _time;

        // Captured RenderSettings (restored in ReleaseEnvironment).
        private bool _captured;
        private AmbientMode _prevAmbientMode;
        private Color _prevSky, _prevEquator, _prevGround;
        private bool _prevFog;
        private Color _prevFogColor;
        private FogMode _prevFogMode;
        private float _prevFogDensity;

        /// <summary>Build the rig under the arena root. tubeInstance
        /// is the instantiated LavaTube model (beacon domes and vent
        /// grates are found by name).</summary>
        public void Build(Transform tubeInstance)
        {
            BuildShafts();
            BuildBeacons(tubeInstance);
            BuildCrystalLights(tubeInstance);
            BuildDust();
            BuildHeroDust();
            BuildSteam(tubeInstance);
        }

        private void BuildShafts()
        {
            var gradient = MakeGradientTexture();
            for (int i = 0; i < CrackXZ.Length; i++)
            {
                // Cinematic C4: the ceiling rose to ~40, so the
                // shafts lengthen with it. Shaft 3 (the crack
                // behind the boss start) becomes the HERO shaft:
                // wider, harsher, and angled onto the boss.
                bool hero = i == 2;
                Vector3 top = new Vector3(CrackXZ[i].x, 37.0f, CrackXZ[i].y);
                // Spotlight at the crack, aimed down the shaft
                // (the hero light tilts ~9 degrees to land on the
                // boss at arena (0, 10) instead of straight down).
                var lightGo = new GameObject("ShaftLight_" + (i + 1));
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = top;
                var spot = lightGo.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.color = new Color(0.96f, 0.97f, 1.0f);
                spot.intensity = hero ? 4.6f : 3.2f;
                spot.range = 62f;
                spot.spotAngle = hero ? 23f : 17f;
                spot.innerSpotAngle = hero ? 12f : 9f;
                // A spotlight shines along its transform's +Z —
                // rotate the light to point down the shaft.
                lightGo.transform.localRotation =
                    Quaternion.Euler(hero ? 99.2f : 90f, 0f, 0f);
                if (hero)
                {
                    // One shadow-casting light only (mobile budget).
                    spot.shadows = LightShadows.Soft;
                }
                // Fake volumetric cone: additive, gradient alpha.
                var cone = MakeCone(hero ? 9.5f : 6.5f, 35.0f, 12);
                cone.name = "ShaftCone_" + (i + 1);
                cone.transform.SetParent(transform, false);
                cone.transform.localPosition = hero
                    ? new Vector3(CrackXZ[i].x, 18.6f, CrackXZ[i].y - 1.5f)
                    : new Vector3(CrackXZ[i].x, 18.6f, CrackXZ[i].y);
                if (hero)
                    cone.transform.localRotation = Quaternion.Euler(9f, 0f, 0f);
                var renderer = cone.GetComponent<MeshRenderer>();
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit != null && renderer != null)
                {
                    var mat = new Material(unlit);
                    mat.SetTexture("_BaseMap", gradient);
                    // Alpha 0.09 (was 0.16, 2026-11-11): at 0.16 the
                    // cones read as solid floating objects; lighter
                    // plus the stronger gradient fade below makes
                    // them read as light falling through the cracks.
                    // The hero shaft runs hotter (0.14) — it is the
                    // one shaft meant to read as a spotlight.
                    mat.SetColor("_BaseColor", new Color(0.85f, 0.90f, 1.0f, hero ? 0.14f : 0.09f));
                    mat.SetFloat("_Surface", 1f);          // transparent
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;
                    renderer.material = mat;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            // Cool rim light so the boss silhouette never merges
            // with the black walls + a warm key over the hero side.
            var rimGo = new GameObject("BossRimLight");
            rimGo.transform.SetParent(transform, false);
            rimGo.transform.localPosition = new Vector3(0f, 17f, 26f);
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(0.22f, 0.33f, 0.55f);
            rim.intensity = 0.9f;
            rim.range = 44f;
            var heroGo = new GameObject("HeroKeyLight");
            heroGo.transform.SetParent(transform, false);
            heroGo.transform.localPosition = new Vector3(0f, 6f, -12f);
            var heroLight = heroGo.AddComponent<Light>();
            heroLight.type = LightType.Point;
            heroLight.color = new Color(1.0f, 0.85f, 0.63f);
            heroLight.intensity = 0.8f;
            heroLight.range = 18f;
        }

        private void BuildBeacons(Transform tubeInstance)
        {
            if (tubeInstance == null) return;
            var beaconPositions = new List<Vector3>();
            foreach (var t in tubeInstance.GetComponentsInChildren<Transform>())
            {
                if (t.name.Length == 9 && t.name.StartsWith("Beacon_"))
                {
                    beaconPositions.Add(t.position);
                    var renderer = t.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        // Instance the dome material so pulsing it
                        // does not touch the shared FBX asset.
                        var mat = renderer.material;
                        _beaconMaterials.Add(mat);
                        _beaconBaseEmission.Add(mat != null && mat.HasProperty("_EmissionColor")
                            ? mat.GetColor("_EmissionColor") : new Color(1f, 0.62f, 0.12f));
                    }
                }
            }
            // Two real point lights on two of the domes; the other
            // two read via their emissive pulse alone.
            for (int i = 0; i < beaconPositions.Count && i < 2; i++)
            {
                var go = new GameObject("BeaconLight_" + (i + 1));
                go.transform.SetParent(transform, false);
                go.transform.position = beaconPositions[i] + Vector3.up * 0.4f;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1.0f, 0.58f, 0.12f);
                light.range = 12f;
                light.intensity = 1.2f;
                _beaconLights.Add(light);
            }
        }

        /// <summary>Cyan point lights at the crystal clusters
        /// (2026-11-11, concept-art pass): the shards glow via
        /// their emissive material but cast no light; a real
        /// light at each big cluster throws the cool rim light
        /// across the nearby rocks. Shards are grouped greedily
        /// (within 6 units = one cluster), the six largest
        /// clusters get lights — a bounded count for mobile.</summary>
        private void BuildCrystalLights(Transform tubeInstance)
        {
            if (tubeInstance == null) return;
            var points = new List<Vector3>();
            foreach (var t in tubeInstance.GetComponentsInChildren<Transform>())
            {
                if (t.name.StartsWith("Crystal_")) points.Add(t.position);
            }
            var clusters = new List<List<Vector3>>();
            foreach (var p in points)
            {
                List<Vector3> home = null;
                foreach (var c in clusters)
                {
                    if (Vector3.Distance(p, c[0]) < 6f) { home = c; break; }
                }
                if (home == null) { home = new List<Vector3>(); clusters.Add(home); }
                home.Add(p);
            }
            clusters.Sort((a, b) => b.Count.CompareTo(a.Count));
            int made = 0;
            foreach (var c in clusters)
            {
                if (made >= 6) break;
                Vector3 centroid = Vector3.zero;
                foreach (var p in c) centroid += p;
                centroid /= c.Count;
                var go = new GameObject("CrystalLight_" + (made + 1));
                go.transform.SetParent(transform, false);
                go.transform.position = centroid + Vector3.up * 0.6f;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.25f, 0.85f, 1.0f);
                light.range = 11f;
                light.intensity = 2.4f;
                made++;
            }
        }

        private void BuildDust()
        {
            var go = new GameObject("LairDust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 10f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.startColor = new Color(0.8f, 0.85f, 1.0f, 0.16f);
            main.maxParticles = 220;
            var emission = ps.emission;
            emission.rateOverTime = 24f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(50f, 20f, 50f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null && renderer != null)
            {
                var mat = new Material(shader);
                mat.SetTexture("_BaseMap", MakeDotTexture());
                mat.SetColor("_BaseColor", new Color(0.8f, 0.85f, 1.0f, 0.5f));
                // Transparent setup (2026-11-11 fix): without it
                // the material stays OPAQUE, the dot texture's
                // alpha is ignored, and every particle renders as
                // a solid pale SQUARE (the texture's RGB is white
                // everywhere; all the softness lives in alpha).
                // Mirrors the shaft-cone recipe that works.
                mat.SetFloat("_Surface", 1f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                renderer.material = mat;
            }
        }

        /// <summary>Cinematic C4: a denser dust drift inside the
        /// hero shaft only — the falling light gets visible motes,
        /// the rest of the cavern keeps the sparse global dust.
        /// Particle budget: 220 global + 80 here + 5 vents x 60
        /// steam = 600 alive max in the lair.</summary>
        private void BuildHeroDust()
        {
            var go = new GameObject("HeroShaftDust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 18f, 13f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = new Color(0.85f, 0.9f, 1.0f, 0.22f);
            main.maxParticles = 80;
            main.gravityModifier = 0.02f;
            var emission = ps.emission;
            emission.rateOverTime = 16f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(11f, 32f, 11f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader != null && renderer != null)
            {
                var mat = new Material(shader);
                mat.SetTexture("_BaseMap", MakeDotTexture());
                mat.SetColor("_BaseColor", new Color(0.85f, 0.9f, 1.0f, 0.5f));
                mat.SetFloat("_Surface", 1f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                renderer.material = mat;
            }
        }

        private void BuildSteam(Transform tubeInstance)
        {
            if (tubeInstance == null) return;
            foreach (var t in tubeInstance.GetComponentsInChildren<Transform>())
            {
                if (!(t.name.Length == 7 && t.name.StartsWith("Vent_"))) continue;
                var go = new GameObject("Steam_" + t.name);
                go.transform.SetParent(transform, false);
                go.transform.position = t.position + Vector3.up * 0.15f;
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
                main.startColor = new Color(0.75f, 0.8f, 0.85f, 0.20f);
                // Cinematic C4: 60 per vent (was 90) — with five
                // vents in the V6 set the lair's total alive
                // particles stay at the ~600 cap.
                main.maxParticles = 60;
                var emission = ps.emission;
                emission.rateOverTime = 6f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.9f;
                var sizeOver = ps.sizeOverLifetime;
                sizeOver.enabled = true;
                sizeOver.size = new ParticleSystem.MinMaxCurve(1f,
                    AnimationCurve.Linear(0f, 0.6f, 1f, 2.4f));
                var renderer = go.GetComponent<ParticleSystemRenderer>();
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader != null && renderer != null)
                {
                    var mat = new Material(shader);
                    mat.SetTexture("_BaseMap", MakeDotTexture());
                    mat.SetColor("_BaseColor", new Color(0.75f, 0.8f, 0.85f, 1.0f));
                    // Transparent setup (2026-11-11 fix, same as
                    // the dust): normal alpha blending for steam,
                    // so the soft dot texture reads as mist puffs
                    // instead of solid squares.
                    mat.SetFloat("_Surface", 1f);
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;
                    renderer.material = mat;
                }
                _steamSystems.Add(ps);
                _steamBurstTimers.Add(0f);
            }
        }

        /// <summary>The boss is venting: the nearest room vent
        /// bursts in sympathy for the duration.</summary>
        public void BurstSteam(Vector3 worldPos, float duration)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < _steamSystems.Count; i++)
            {
                float d = Vector3.Distance(worldPos, _steamSystems[i].transform.position);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best >= 0) _steamBurstTimers[best] = duration;
        }

        public void SetPhase2(bool phase2) { _phase2 = phase2; }

        /// <summary>Apply the lair's ambient + fog, capturing the
        /// scene's current values first (Hide restores them).</summary>
        public void ApplyEnvironment()
        {
            if (!_captured)
            {
                _prevAmbientMode = RenderSettings.ambientMode;
                _prevSky = RenderSettings.ambientSkyColor;
                _prevEquator = RenderSettings.ambientEquatorColor;
                _prevGround = RenderSettings.ambientGroundColor;
                _prevFog = RenderSettings.fog;
                _prevFogColor = RenderSettings.fogColor;
                _prevFogMode = RenderSettings.fogMode;
                _prevFogDensity = RenderSettings.fogDensity;
                _captured = true;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // Colour grade (2026-11-11, toward the lair concept
            // art): space-blue sky, obsidian-purple ground and
            // fog, and slightly denser fog so the far walls fall
            // off into haze. The fight area stays readable via
            // the shaft spots, the hero key light and the rim
            // light, which are unchanged. Cinematic C4 deepens
            // the shadows further toward space-blue now that the
            // cavern is taller — the hero shaft carries the boss.
            RenderSettings.ambientSkyColor = new Color(0.11f, 0.11f, 0.31f);
            RenderSettings.ambientEquatorColor = new Color(0.038f, 0.048f, 0.125f);
            RenderSettings.ambientGroundColor = new Color(0.014f, 0.02f, 0.048f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.038f, 0.042f, 0.115f);
            RenderSettings.fogDensity = 0.0135f;
        }

        public void ReleaseEnvironment()
        {
            if (!_captured) return;
            RenderSettings.ambientMode = _prevAmbientMode;
            RenderSettings.ambientSkyColor = _prevSky;
            RenderSettings.ambientEquatorColor = _prevEquator;
            RenderSettings.ambientGroundColor = _prevGround;
            RenderSettings.fog = _prevFog;
            RenderSettings.fogColor = _prevFogColor;
            RenderSettings.fogMode = _prevFogMode;
            RenderSettings.fogDensity = _prevFogDensity;
            _captured = false;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            float speed = _phase2 ? 2.6f : 1.0f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(_time * speed * Mathf.PI);
            for (int i = 0; i < _beaconLights.Count; i++)
            {
                float phase = i * 1.7f;
                float p = 0.5f + 0.5f * Mathf.Sin(_time * speed * Mathf.PI + phase);
                _beaconLights[i].intensity = Mathf.Lerp(0.6f, 2.2f, p);
                if (_phase2) _beaconLights[i].color = new Color(1.0f, 0.25f, 0.10f);
            }
            for (int i = 0; i < _beaconMaterials.Count; i++)
            {
                var mat = _beaconMaterials[i];
                if (mat == null) continue;
                float p = 0.5f + 0.5f * Mathf.Sin(_time * speed * Mathf.PI + i * 1.3f);
                Color baseColor = _phase2 ? new Color(1f, 0.2f, 0.08f) : _beaconBaseEmission[i];
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", baseColor * Mathf.Lerp(0.8f, 2.4f, p));
            }
            for (int i = 0; i < _steamSystems.Count; i++)
            {
                if (_steamBurstTimers[i] <= 0f) continue;
                _steamBurstTimers[i] -= Time.deltaTime;
                var emission = _steamSystems[i].emission;
                emission.rateOverTime = _steamBurstTimers[i] > 0f ? 42f : 6f;
            }
        }

        // A cone mesh (open, no caps) with v running 0 at the wide
        // base to 1 at the apex, for the gradient texture.
        private static GameObject MakeCone(float baseRadius, float height, int segments)
        {
            var mesh = new Mesh();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = (i % segments) / (float)segments * Mathf.PI * 2f;
                // Base ring (v=0) then apex ring collapsed (v=1).
                verts.Add(new Vector3(Mathf.Cos(a) * baseRadius, -height * 0.5f, Mathf.Sin(a) * baseRadius));
                uvs.Add(new Vector2(0f, 0f));
                verts.Add(new Vector3(Mathf.Cos(a) * 0.6f, height * 0.5f, Mathf.Sin(a) * 0.6f));
                uvs.Add(new Vector2(0f, 1f));
            }
            for (int i = 0; i < segments; i++)
            {
                int b0 = i * 2, t0 = i * 2 + 1, b1 = i * 2 + 2, t1 = i * 2 + 3;
                tris.Add(b0); tris.Add(t0); tris.Add(b1);
                tris.Add(t0); tris.Add(t1); tris.Add(b1);
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var go = new GameObject("Cone");
            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }

        // Vertical gradient: bright at the top (v=1, the crack),
        // fading to clear at the floor — the fake light shaft.
        private static Texture2D MakeGradientTexture()
        {
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++)
            {
                float t = y / 63f;
                float a = Mathf.Pow(t, 2.4f);
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return tex;
        }

        // Soft round sprite for particles.
        private static Texture2D MakeDotTexture()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            }
            tex.Apply();
            return tex;
        }
    }
}
