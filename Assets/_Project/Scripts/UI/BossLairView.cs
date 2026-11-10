using System;
using System.Collections;
using System.Collections.Generic;
using LunarDominion.Boss;
using LunarDominion.Core;
using LunarDominion.Expeditions;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Boss lair interior (2026-11-10): a full-screen view inside
    /// the War Robot's lair, separate from the exterior map. The War
    /// Robot stands large on screen (a big portion of the frame) and the
    /// player's heroes face it from the near side. The player picks 1-3
    /// recruited heroes, taps Fight, and the boss fight runs here (via
    /// ExpeditionService.TryStartHeroes) with a short fight animation,
    /// then the result shows. Tapping the lair on the map opens this
    /// view (the map march is not used for bosses); closing returns to
    /// the Map. The boss's vanish/respawn is owned by
    /// MapExcursionService — this view raises OnFightComplete so the
    /// Bootstrap can mark the boss defeated on victory.</summary>
    public class BossLairView : MonoBehaviour
    {
        // Raised after a fight resolves and the animation finishes,
        // with the battle result. The Bootstrap marks the boss defeated
        // on victory (and saves), then returns to the Map.
        public event Action<BattleResult> OnFightComplete;
        // Raised when the player taps Back to Map.
        public event Action OnCloseClicked;

        public bool IsVisible => _visible;

        private GameController _controller;
        private GameObject _sceneRoot;
        private GameObject _robotGo;
        private Vector3 _robotOrigPos;
        private Quaternion _robotOrigRot;
        private readonly List<GameObject> _heroFigures = new List<GameObject>();
        private readonly List<Vector3> _heroOrigPos = new List<Vector3>();
        private bool _visible;
        private bool _fighting;

        // Boss ladder (2026-11-10): 15 levels of War Robot. The player
        // fights the selected level (1 to the highest unlocked =
        // BossLevelDefeated + 1, capped at 15). Defeating a level
        // unlocks the next; the boss never vanishes.
        private int _selectedBossLevel = 1;
        private Button _prevLevelButton;
        private Button _nextLevelButton;

        // UI.
        private GameObject _root;
        private GameObject _headerGo;
        private GameObject _bottomGo;
        private RectTransform _bottomRt;
        private Text _titleText;
        private Text _bossInfoText;
        private Text _energyText;
        private Text _resultText;
        private Button _fightButton;
        private Button _closeButton;
        private GameObject _heroRowsContent;
        private readonly List<HeroRow> _heroRows = new List<HeroRow>();
        private readonly List<int> _selectedHeroIndices = new List<int>();

        // A.R.E.S.-09 real-time fight (2026-11-10): the lava tube
        // arena scene + the real-time controller. The old cave
        // close-up set (ground/rocks/War Robot) now lives under
        // _caveRoot so the two scenes can toggle. Realtime is used
        // per UseRealtime(); everything below stays null/inactive
        // when the arena models are not installed.
        private GameObject _caveRoot;
        private GameObject _arenaRoot;
        private bool _arenaAvailable;
        private GameObject _aresRoot;
        private GameObject _aresVisual;
        private Vector3 _aresVisualOrigPos;
        private Quaternion _aresVisualOrigRot;
        private AresBossController _aresController;
        private TelegraphRing _slamRing;
        private TelegraphRing _ventRing;
        private GameObject _droneTemplate;
        private LavaTubeAmbience _ambience;
        private Material _coreMaterial;
        private Color _coreBaseEmission = Color.black;
        private int _bossPhase = 1;
        private readonly List<Vector4> _coverSpheres = new List<Vector4>();
        private readonly List<AresHeroCombatant> _combatants = new List<AresHeroCombatant>();
        private EncounterDefinition _realtimeEncounter;
        private List<SquadCombatant> _realtimeSquad;
        private float _fightStartTime;
        private bool _realtimeActive;

        // Real-time fight HUD.
        private GameObject _hudRoot;
        private Image _bossHpFill;
        private Text _bossHpText;
        private Text _warningText;
        private float _warningUntil;
        private GameObject _chipsContent;
        private readonly List<HeroChip> _chips = new List<HeroChip>();
        private readonly List<Button> _droneButtons = new List<Button>();
        private Coroutine _hudRoutine;

        private class HeroChip
        {
            public AresHeroCombatant Combatant;
            public Image HpFill;
            public Image DashFill;
            public Text NameText;
        }

        private class HeroRow
        {
            public GameObject Root;
            public int HeroIndex;
            public Text NameText;
            public Text StatsText;
            public Button ToggleButton;
            public Image RowImage;
        }

        // Hero figure colours (match the picker rows): Reyes cyan-blue,
        // Vega orange, Okafor green.
        private static readonly Color[] HeroColors =
        {
            new Color(0.25f, 0.65f, 1.0f),
            new Color(1.0f, 0.55f, 0.15f),
            new Color(0.25f, 0.85f, 0.45f),
        };

        private static Color HeroColorFor(UnitId id)
        {
            if (id == UnitId.HeroReyes) return HeroColors[0];
            if (id == UnitId.HeroVega) return HeroColors[1];
            return HeroColors[2];
        }

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            BuildScene();
            BuildUI(canvas);
            _sceneRoot.SetActive(false);
            _root.SetActive(false);
        }

        // Dark cave interior: a dark ground plane, a few rock shapes
        // around the edges, the War Robot large centre-right, and hero
        // figures on the near-left facing it. The camera frames this
        // (see Show): the robot takes up a big portion of the screen.
        private void BuildScene()
        {
            _sceneRoot = new GameObject("BossLairScene");
            // The lair lives FAR from the base (x 500): the base's ground
            // (regolith + paved interior) and mountains are never hidden
            // by BaseView.SetVisible (it only hides buildings), so a lair
            // built at the origin would show the bright paved base floor
            // through/under it. At x 500 the camera sees only the lair —
            // dark cave floor, rocks, robot, heroes — against dark sky.
            _sceneRoot.transform.position = new Vector3(500f, 0f, 0f);
            // The original cave close-up set lives under _caveRoot so
            // it can toggle against the lava tube arena (_arenaRoot).
            _caveRoot = new GameObject("LairCave");
            _caveRoot.transform.SetParent(_sceneRoot.transform, false);
            // Dark ground (60x40, big enough that its edges sit far from
            // the camera and read as cave floor, not a visible rim).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "LairGround";
            ground.transform.SetParent(_caveRoot.transform, false);
            ground.transform.localPosition = new Vector3(0f, 0f, 0f);
            ground.transform.localScale = new Vector3(6f, 1f, 4f);
            SetColor(ground.GetComponent<Renderer>(), new Color(0.10f, 0.10f, 0.13f));
            Destroy(ground.GetComponent<Collider>());

            // Rock shapes around the edges (dark, so they read as cave
            // walls in the dim light). A few large spheres, squashed.
            AddRock(new Vector3(-9f, 0.5f, -6f), new Vector3(4f, 2.5f, 3f));
            AddRock(new Vector3(-9f, 0.5f, 6f), new Vector3(4f, 2.5f, 3f));
            AddRock(new Vector3(9f, 0.5f, -6f), new Vector3(4f, 2.5f, 3f));
            AddRock(new Vector3(9f, 0.5f, 6f), new Vector3(4f, 2.5f, 3f));
            AddRock(new Vector3(0f, 0.5f, -8f), new Vector3(6f, 2.5f, 3f));

            // The War Robot, large: the FBX at 2.5x, centre-right,
            // facing the heroes (-X). Falls back to a big dark primitive
            // if the FBX is not installed yet, so the view still works.
            _robotGo = null;
            GameObject prefab = null;
            try
            {
                prefab = Resources.Load<GameObject>("Models/WarRobot");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BossLairView] War Robot failed to load (Models/WarRobot): " + e.Message);
            }
            if (prefab != null)
            {
                _robotGo = Instantiate(prefab);
                _robotGo.name = "WarRobot";
                _robotGo.transform.SetParent(_caveRoot.transform, false);
                _robotGo.transform.localPosition = new Vector3(4f, 0f, 0f);
                _robotGo.transform.localScale = Vector3.one * 2.5f;
                // Face the heroes (-X): the FBX faces +X by default.
                _robotGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            else
            {
                Debug.LogWarning("[BossLairView] WarRobot.fbx is missing — the boss will be a dark placeholder. Copy WarRobot.fbx + its PNGs into Assets/_Project/Resources/Models/.");
                _robotGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _robotGo.name = "WarRobotPlaceholder";
                _robotGo.transform.SetParent(_caveRoot.transform, false);
                _robotGo.transform.localPosition = new Vector3(4f, 2.5f, 0f);
                _robotGo.transform.localScale = new Vector3(2f, 5f, 2f);
                SetColor(_robotGo.GetComponent<Renderer>(), new Color(0.25f, 0.25f, 0.30f));
                Destroy(_robotGo.GetComponent<Collider>());
            }
            _robotOrigPos = _robotGo.transform.localPosition;
            _robotOrigRot = _robotGo.transform.localRotation;
            foreach (var collider in _robotGo.GetComponentsInChildren<Collider>())
                Destroy(collider);

            BuildArenaScene();
        }

        // A.R.E.S.-09 real-time routing (2026-11-10): when true,
        // every ladder level is fought in real time against
        // A.R.E.S.-09 in the lava tube arena. Set false to make
        // A.R.E.S. the Level 15 capstone only (levels 1-14 stay
        // turn-based War Robot fights). Falls back to turn-based
        // whenever the arena models are not installed.
        private const bool AresOnWholeLadder = true;

        private bool UseRealtime()
        {
            if (!_arenaAvailable) return false;
            return AresOnWholeLadder || _selectedBossLevel >= EncounterCatalog.MaxBossLevel;
        }

        // The lava tube arena + A.R.E.S.-09, built once alongside
        // the cave set and toggled by PresentSceneForMode. Arena
        // layout (the LavaTube model's own): boss start at local
        // (0, 0, 10), heroes enter from local z -14.
        private void BuildArenaScene()
        {
            _arenaRoot = new GameObject("LairArena");
            _arenaRoot.transform.SetParent(_sceneRoot.transform, false);
            _arenaRoot.SetActive(false);

            GameObject tubePrefab = null;
            GameObject aresPrefab = null;
            try { tubePrefab = Resources.Load<GameObject>("Models/LavaTube"); }
            catch (System.Exception e) { Debug.LogWarning("[BossLairView] LavaTube failed to load (Models/LavaTube): " + e.Message); }
            try { aresPrefab = Resources.Load<GameObject>("Models/Ares09"); }
            catch (System.Exception e) { Debug.LogWarning("[BossLairView] Ares09 failed to load (Models/Ares09): " + e.Message); }
            if (tubePrefab == null || aresPrefab == null)
            {
                Debug.LogWarning("[BossLairView] LavaTube.fbx / Ares09.fbx missing — real-time boss fights unavailable; the lair stays turn-based. Copy both FBXs + their PNGs into Assets/_Project/Resources/Models/.");
                return;
            }

            var tube = Instantiate(tubePrefab);
            tube.name = "LavaTube";
            tube.transform.SetParent(_arenaRoot.transform, false);
            foreach (var collider in tube.GetComponentsInChildren<Collider>())
                Destroy(collider);

            // Cover boulders -> world-space spheres for the sweep
            // beam's cover test (segment-sphere; no physics layers).
            _coverSpheres.Clear();
            foreach (var t in tube.GetComponentsInChildren<Transform>())
            {
                if (!t.name.StartsWith("CoverBoulder_")) continue;
                var coverRenderer = t.GetComponent<Renderer>();
                if (coverRenderer == null) continue;
                var b = coverRenderer.bounds;
                _coverSpheres.Add(new Vector4(b.center.x, b.center.y, b.center.z,
                    Mathf.Max(b.extents.x, b.extents.z)));
            }

            // Fight root at the boss start; the controller rotates
            // this root to track heroes. Initial facing -Z (toward
            // the heroes' gate).
            _aresRoot = new GameObject("AresFightRoot");
            _aresRoot.transform.SetParent(_arenaRoot.transform, false);
            _aresRoot.transform.localPosition = new Vector3(0f, 0f, 10f);
            _aresRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _aresController = _aresRoot.AddComponent<AresBossController>();

            _aresVisual = new GameObject("AresVisual");
            _aresVisual.transform.SetParent(_aresRoot.transform, false);
            var aresModel = Instantiate(aresPrefab);
            aresModel.name = "Ares09";
            aresModel.transform.SetParent(_aresVisual.transform, false);
            // The FBX faces +X; the wrapper's -90 yaw maps that to
            // the fight root's forward. Scale 3x (~8.9 units tall).
            aresModel.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            aresModel.transform.localScale = Vector3.one * 3f;
            foreach (var collider in aresModel.GetComponentsInChildren<Collider>())
                Destroy(collider);
            _aresVisualOrigPos = _aresVisual.transform.localPosition;
            _aresVisualOrigRot = _aresVisual.transform.localRotation;

            // The chest core's material (instanced) so Phase 2 can
            // pulse it without touching the shared FBX asset.
            foreach (var t in aresModel.GetComponentsInChildren<Transform>())
            {
                if (t.name != "CoreChest") continue;
                var coreRenderer = t.GetComponent<Renderer>();
                if (coreRenderer == null) continue;
                _coreMaterial = coreRenderer.material;
                if (_coreMaterial != null && _coreMaterial.HasProperty("_EmissionColor"))
                    _coreBaseEmission = _coreMaterial.GetColor("_EmissionColor");
            }

            // Telegraph rings at the boss's feet.
            _slamRing = MakeRing(new Color(1f, 0.30f, 0.10f));
            _ventRing = MakeRing(new Color(1f, 0.55f, 0.10f));

            _droneTemplate = BuildDroneTemplate();
            _droneTemplate.transform.SetParent(_arenaRoot.transform, false);
            _droneTemplate.SetActive(false);

            var ambienceGo = new GameObject("LavaTubeAmbience");
            ambienceGo.transform.SetParent(_arenaRoot.transform, false);
            _ambience = ambienceGo.AddComponent<LavaTubeAmbience>();
            _ambience.Build(tube.transform);

            // Controller events -> HUD / ambience (subscribed once).
            _aresController.OnHealthChanged += (current, max) =>
            {
                if (_bossHpFill != null) _bossHpFill.fillAmount = max > 0f ? current / max : 0f;
                if (_bossHpText != null) _bossHpText.text = $"A.R.E.S.-09  {(int)current} / {(int)max}";
            };
            _aresController.OnTelegraph += (attackName) =>
            {
                ShowWarning(attackName);
                if (attackName == "Core Venting" && _ambience != null && _aresRoot != null)
                    _ambience.BurstSteam(_aresRoot.transform.position, 3f);
            };
            _aresController.OnPhaseChanged += (phase) =>
            {
                _bossPhase = phase;
                if (phase == 2)
                {
                    ShowWarning("PHASE 2 — CORE EXPOSED");
                    if (_ambience != null) _ambience.SetPhase2(true);
                }
            };
            _aresController.OnDefeated += () => { if (_realtimeActive) StartCoroutine(RealtimeEndRoutine(true)); };
            _aresController.OnSquadDefeated += () => { if (_realtimeActive) StartCoroutine(RealtimeEndRoutine(false)); };

            _arenaAvailable = true;
        }

        private TelegraphRing MakeRing(Color color)
        {
            var go = new GameObject("TelegraphRing");
            go.transform.SetParent(_arenaRoot.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 10f);
            var ring = go.AddComponent<TelegraphRing>();
            ring.Build(color);
            return ring;
        }

        private GameObject BuildDroneTemplate()
        {
            var root = new GameObject("SubDroneTemplate");
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "DroneBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(1.1f, 0.7f, 1.1f);
            SetColor(body.GetComponent<Renderer>(), new Color(0.16f, 0.17f, 0.20f));
            Destroy(body.GetComponent<Collider>());
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "DroneEye";
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            eye.transform.localScale = Vector3.one * 0.35f;
            SetGlow(eye.GetComponent<Renderer>(), new Color(1f, 0.2f, 0.1f));
            Destroy(eye.GetComponent<Collider>());
            for (int i = 0; i < 2; i++)
            {
                var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rotor.name = "DroneRotor";
                rotor.transform.SetParent(root.transform, false);
                rotor.transform.localPosition = new Vector3(i == 0 ? -0.55f : 0.55f, 0.42f, 0f);
                rotor.transform.localScale = new Vector3(0.7f, 0.05f, 0.7f);
                SetColor(rotor.GetComponent<Renderer>(), new Color(0.30f, 0.31f, 0.35f));
                Destroy(rotor.GetComponent<Collider>());
            }
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = 0.9f;
            return root;
        }

        // SetColor + a matching emissive, so the part glows under
        // the project's Bloom (threshold 0.9).
        private static void SetGlow(Renderer renderer, Color color)
        {
            SetColor(renderer, color);
            if (renderer != null && renderer.material != null)
            {
                renderer.material.SetColor("_EmissionColor", color * 2.5f);
                renderer.material.EnableKeyword("_EMISSION");
            }
        }

        // Show the scene for the selected level's fight mode: cave
        // close-up (turn-based) or lava tube arena (real-time),
        // with the matching camera framing and — for the arena —
        // the lair lighting/fog (restored on the way back, so the
        // base and map keep their own look).
        private void PresentSceneForMode()
        {
            bool realtime = UseRealtime();
            if (_caveRoot != null) _caveRoot.SetActive(!realtime);
            if (_arenaRoot != null) _arenaRoot.SetActive(realtime);
            var cam = Camera.main;
            if (cam != null)
            {
                if (realtime)
                {
                    // INSIDE the tube: the arena is an enclosed set
                    // (walls r~29.5, ceiling ~18), so the camera sits
                    // within it — south end, above the heroes,
                    // looking down the tube toward the boss.
                    cam.transform.position = new Vector3(500f, 9f, -24f);
                    cam.transform.LookAt(new Vector3(500f, 2.5f, 4f));
                }
                else
                {
                    cam.transform.position = new Vector3(498f, 7f, -16f);
                    cam.transform.LookAt(new Vector3(500f, 0.8f, 0f));
                }
            }
            if (_ambience != null)
            {
                if (realtime) _ambience.ApplyEnvironment();
                else _ambience.ReleaseEnvironment();
            }
        }

        private void AddRock(Vector3 pos, Vector3 size)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "LairRock";
            rock.transform.SetParent(_caveRoot.transform, false);
            rock.transform.localPosition = pos;
            rock.transform.localScale = size;
            SetColor(rock.GetComponent<Renderer>(), new Color(0.16f, 0.16f, 0.19f));
            Destroy(rock.GetComponent<Collider>());
        }

        // Explicit URP Lit material (same pattern as BaseView's
        // SetPrimitiveColor / the march marker): a runtime primitive's
        // default material renders magenta on the phone.
        private static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var material = new Material(shader);
                material.color = color;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                renderer.material = material;
            }
            else
            {
                renderer.material.color = color;
            }
        }

        private void BuildUI(Canvas canvas)
        {
            var safe = SafeArea.Get(canvas);
            // The root is a TRANSPARENT container (no opaque background):
            // the 3D lair scene (robot + heroes) shows through the middle
            // of the screen. The header (title/boss info/energy) sits at
            // the top and the hero picker + buttons at the bottom; only
            // those two bands are opaque, so the middle stays open.
            _root = new GameObject("BossLairRoot");
            _root.transform.SetParent(canvas.gameObject.transform, false);
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0, 0);
            rootRt.anchorMax = new Vector2(1, 0);
            rootRt.offsetMin = new Vector2(20, 200 + safe.Bottom);
            rootRt.offsetMax = new Vector2(-20, 920 - safe.Top);

            // Header band (top ~20%): title (with the boss-level
            // selector), boss info, energy. Opaque dark so the text
            // reads over the 3D scene. The < and > buttons pick any
            // unlocked boss level (1 to the highest unlocked).
            _headerGo = UIHelpers.Panel(_root, "Header", UIHelpers.Hex("#0D1420"),
                new Vector2(0, 0.80f), new Vector2(1, 1f), Vector2.zero, Vector2.zero);
            _titleText = UIHelpers.LabelInRect(_headerGo, "Title", "WAR ROBOT LAIR", 36, Color.white,
                new Rect(0.12f, 0.62f, 0.88f, 0.98f), TextAnchor.MiddleCenter);
            _prevLevelButton = UIHelpers.Button(_headerGo, "PrevLevelBtn", "<", 30,
                UIHelpers.Hex("#1A2436"), Color.white);
            var prevRt = _prevLevelButton.GetComponent<RectTransform>();
            prevRt.anchorMin = new Vector2(0.02f, 0.62f);
            prevRt.anchorMax = new Vector2(0.10f, 0.98f);
            prevRt.offsetMin = Vector2.zero;
            prevRt.offsetMax = Vector2.zero;
            _prevLevelButton.onClick.AddListener(() => ChangeBossLevel(-1));
            _nextLevelButton = UIHelpers.Button(_headerGo, "NextLevelBtn", ">", 30,
                UIHelpers.Hex("#1A2436"), Color.white);
            var nextRt = _nextLevelButton.GetComponent<RectTransform>();
            nextRt.anchorMin = new Vector2(0.90f, 0.62f);
            nextRt.anchorMax = new Vector2(0.98f, 0.98f);
            nextRt.offsetMin = Vector2.zero;
            nextRt.offsetMax = Vector2.zero;
            _nextLevelButton.onClick.AddListener(() => ChangeBossLevel(1));
            _bossInfoText = UIHelpers.LabelInRect(_headerGo, "BossInfo", "", 24, UIHelpers.Hex("#FFD166"),
                new Rect(0.04f, 0.30f, 0.96f, 0.62f), TextAnchor.MiddleCenter);
            _energyText = UIHelpers.LabelInRect(_headerGo, "Energy", "", 22, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.02f, 0.96f, 0.30f), TextAnchor.MiddleCenter);

            // Bottom band (bottom ~45%): result text, hero picker, and
            // the Fight / Back buttons. Opaque dark; the 3D scene shows
            // in the open middle band above this.
            _bottomGo = UIHelpers.Panel(_root, "BottomPanel", UIHelpers.Hex("#0D1420"),
                new Vector2(0, 0f), new Vector2(1, 0.45f), Vector2.zero, Vector2.zero);
            _bottomRt = _bottomGo.GetComponent<RectTransform>();
            _resultText = UIHelpers.LabelInRect(_bottomGo, "Result", "", 24, Color.white,
                new Rect(0.04f, 0.72f, 0.96f, 0.98f), TextAnchor.UpperLeft);
            BuildHeroRowsScroll(_bottomGo);
            _fightButton = UIHelpers.Button(_bottomGo, "FightButton", "FIGHT", 28,
                UIHelpers.Hex("#B71C1C"), Color.white);
            var fightRt = _fightButton.GetComponent<RectTransform>();
            fightRt.anchorMin = new Vector2(0.02f, 0.02f);
            fightRt.anchorMax = new Vector2(0.49f, 0.14f);
            fightRt.offsetMin = Vector2.zero;
            fightRt.offsetMax = Vector2.zero;
            _fightButton.onClick.AddListener(OnFightClicked);

            _closeButton = UIHelpers.Button(_bottomGo, "CloseButton", "Back to Map", 28,
                UIHelpers.Hex("#1A2436"), Color.white);
            var closeRt = _closeButton.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(0.51f, 0.02f);
            closeRt.anchorMax = new Vector2(0.98f, 0.14f);
            closeRt.offsetMin = Vector2.zero;
            closeRt.offsetMax = Vector2.zero;
            _closeButton.onClick.AddListener(() => OnCloseClicked?.Invoke());

            BuildFightHud();
        }

        private void BuildHeroRowsScroll(GameObject parent)
        {
            var scrollGo = new GameObject("HeroRowsScroll");
            scrollGo.transform.SetParent(parent.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.16f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.70f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = UIHelpers.Hex("#0F1724");
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = UIHelpers.Hex("#0F1724");
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = viewportRt;

            _heroRowsContent = new GameObject("Content");
            _heroRowsContent.transform.SetParent(viewportGo.transform, false);
            var contentRt = _heroRowsContent.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            // VerticalLayoutGroup (same as Army/SquadPicker): stacks the
            // hero rows vertically and forces them to the content width.
            // Without it the rows all overlap at the same spot and clip.
            var layout = _heroRowsContent.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _heroRowsContent.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.content = contentRt;
        }

        public void Show()
        {
            _visible = true;
            _fighting = false;
            _selectedHeroIndices.Clear();
            _resultText.text = "";
            // Open on the highest unlocked level (the frontier) — the
            // player can step back to an earlier level with < if they
            // want to re-fight it.
            _selectedBossLevel = HighestUnlockedBossLevel();
            _root.SetActive(true);
            // Hero selection: hide the 3D lair and expand the panel so
            // all heroes show without scrolling (Patrick 2026-11-10).
            SetSelectMode(true);
            // A fresh open always shows the header + bottom panel (they
            // are hidden during a fight — see OnFightClicked).
            if (_headerGo != null) _headerGo.SetActive(true);
            if (_bottomGo != null) _bottomGo.SetActive(true);
            RefreshHeroRows();
            RefreshHeroFigures();
            RefreshBossInfo();
            // Camera + scene set for the selected level's mode
            // (cave close-up or lava tube arena).
            PresentSceneForMode();
        }

        public void Hide()
        {
            _visible = false;
            // Leaving mid-fight abandons the real-time fight (energy
            // already spent is not refunded — same as a defeat).
            if (_realtimeActive) EndRealtimeFightImmediate();
            if (_ambience != null) _ambience.ReleaseEnvironment();
            if (_sceneRoot != null) _sceneRoot.SetActive(false);
            if (_root != null) _root.SetActive(false);
        }

        // Hero selection mode (Patrick 2026-11-10): while the player is
        // picking heroes, hide the 3D lair scene and expand the bottom
        // panel to fill the area below the header, so all heroes show
        // without scrolling. During the fight the 3D scene shows and
        // the panel returns to its normal size (and the UI hides).
        private void SetSelectMode(bool select)
        {
            if (_sceneRoot != null) _sceneRoot.SetActive(!select);
            if (_bottomRt != null)
            {
                _bottomRt.anchorMax = select ? new Vector2(1, 0.80f) : new Vector2(1, 0.45f);
            }
        }

        private void RefreshBossInfo()
        {
            var boss = BossEncounter();
            if (boss == null)
            {
                _bossInfoText.text = "Boss encounter not found.";
                _energyText.text = "";
                return;
            }
            int highest = HighestUnlockedBossLevel();
            bool realtime = UseRealtime();
            _titleText.text = realtime
                ? $"A.R.E.S.-09 — LEVEL {_selectedBossLevel} / {EncounterCatalog.MaxBossLevel}"
                : $"WAR ROBOT — LEVEL {_selectedBossLevel} / {EncounterCatalog.MaxBossLevel}";
            if (realtime && boss.Spawns.Count > 0)
            {
                var stats = AresStats.FromLadder(boss.Spawns[0].Health, boss.Spawns[0].Attack, boss.Spawns[0].Defense,
                    CombatService.EnemyScale(_controller.State.NexusLevel, boss.RequiredNexusLevel));
                _bossInfoText.text = $"REAL-TIME FIGHT  •  HP {(int)stats.MaxHealth}  ATK {boss.Spawns[0].Attack}  DEF {(int)stats.Defense}  •  Reward {boss.Reward.Biomass}B {boss.Reward.Alloy}A {boss.Reward.Helium3}H  •  {boss.XpPool} XP";
            }
            else
            {
                _bossInfoText.text = $"HP {(boss.Spawns.Count > 0 ? boss.Spawns[0].Health : 0)}  ATK {(boss.Spawns.Count > 0 ? boss.Spawns[0].Attack : 0)}  DEF {(boss.Spawns.Count > 0 ? boss.Spawns[0].Defense : 0)}  •  Reward {boss.Reward.Biomass}B {boss.Reward.Alloy}A {boss.Reward.Helium3}H  •  {boss.XpPool} XP";
            }
            _energyText.text = $"Energy: {_controller.State.Energy} / {boss.EnergyCost}  •  Unlocks at Nexus {boss.RequiredNexusLevel} (you are Nexus {_controller.State.NexusLevel})";
            UIHelpers.SetButtonEnabled(_prevLevelButton, _selectedBossLevel > 1);
            UIHelpers.SetButtonEnabled(_nextLevelButton, _selectedBossLevel < highest);
            RefreshFightButton();
        }

        // Highest boss level the player may fight: one past the highest
        // defeated, capped at 15. Defeating a level unlocks the next.
        private int HighestUnlockedBossLevel()
        {
            int defeated = _controller != null && _controller.State != null
                ? _controller.State.BossLevelDefeated : 0;
            return Math.Max(1, Math.Min(defeated + 1, EncounterCatalog.MaxBossLevel));
        }

        private void ChangeBossLevel(int delta)
        {
            int next = _selectedBossLevel + delta;
            int highest = HighestUnlockedBossLevel();
            if (next < 1 || next > highest) return;
            _selectedBossLevel = next;
            _resultText.text = "";
            RefreshBossInfo();
        }

        private EncounterDefinition BossEncounter()
        {
            if (_controller == null) return null;
            return EncounterCatalog.CreateBossEncounter(_selectedBossLevel);
        }

        private void RefreshHeroRows()
        {
            foreach (var row in _heroRows)
            {
                if (row.Root != null) Destroy(row.Root);
            }
            _heroRows.Clear();
            if (_controller == null || _controller.State == null || _heroRowsContent == null) return;
            var heroes = _controller.State.Heroes;
            if (heroes == null || heroes.Count == 0)
            {
                var emptyGo = new GameObject("NoHeroesRow");
                emptyGo.transform.SetParent(_heroRowsContent.transform, false);
                var emptyImg = emptyGo.AddComponent<Image>();
                emptyImg.color = UIHelpers.Hex("#1A2436");
                var layout = emptyGo.AddComponent<LayoutElement>();
                layout.preferredHeight = 88;
                var rt = emptyGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                UIHelpers.LabelInRect(emptyGo, "Empty", "No heroes recruited yet — recruit one from the Heroes tab.", 24, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.03f, 0.10f, 0.97f, 0.90f));
                RefreshFightButton();
                return;
            }
            for (int i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                if (hero == null) continue;
                var def = _controller.UnitDefinitions.Find(u => u.Id == hero.Id);
                if (def == null) continue;
                int heroIndex = i;
                var rowGo = new GameObject(def.DisplayName + "Row");
                rowGo.transform.SetParent(_heroRowsContent.transform, false);
                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = UIHelpers.Hex("#1A2436");
                var rowLayout = rowGo.AddComponent<LayoutElement>();
                // 88px rows: in hero-selection mode the panel expands so
                // all 3 heroes fit without scrolling (3x88 + spacing).
                rowLayout.preferredHeight = 88;
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.anchorMin = Vector2.zero;
                rowRt.anchorMax = Vector2.one;
                var color = HeroColorFor(hero.Id);
                var nameText = UIHelpers.LabelInRect(rowGo, "Name", $"{def.DisplayName}  Lv {hero.Level}", 26, Color.white,
                    new Rect(0.03f, 0.55f, 0.60f, 0.95f));
                var statsText = UIHelpers.LabelInRect(rowGo, "Stats", $"HP {def.BaseHealth}  ATK {def.BaseAttack}  DEF {def.BaseDefense}  •  {def.Class}", 22, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.03f, 0.08f, 0.60f, 0.50f));
                var toggle = UIHelpers.Button(rowGo, "ToggleBtn", "Select", 24,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var toggleRt = toggle.GetComponent<RectTransform>();
                toggleRt.anchorMin = new Vector2(0.68f, 0.15f);
                toggleRt.anchorMax = new Vector2(0.97f, 0.85f);
                toggleRt.offsetMin = Vector2.zero;
                toggleRt.offsetMax = Vector2.zero;
                // A small colour chip so the player can match the row
                // to the 3D figure (same colour as the figure).
                var chipGo = new GameObject("ColorChip");
                chipGo.transform.SetParent(rowGo.transform, false);
                var chipRt = chipGo.AddComponent<RectTransform>();
                chipRt.anchorMin = new Vector2(0.62f, 0.30f);
                chipRt.anchorMax = new Vector2(0.66f, 0.70f);
                chipRt.offsetMin = Vector2.zero;
                chipRt.offsetMax = Vector2.zero;
                var chipImg = chipGo.AddComponent<Image>();
                chipImg.color = color;
                var row = new HeroRow { Root = rowGo, HeroIndex = heroIndex, NameText = nameText, StatsText = statsText, ToggleButton = toggle, RowImage = rowImg };
                toggle.onClick.AddListener(() => OnHeroToggle(row));
                _heroRows.Add(row);
            }
            UpdateRowHighlights();
            RefreshFightButton();
        }

        private void OnHeroToggle(HeroRow row)
        {
            if (_fighting) return;
            if (_selectedHeroIndices.Contains(row.HeroIndex))
            {
                _selectedHeroIndices.Remove(row.HeroIndex);
            }
            else
            {
                if (_selectedHeroIndices.Count >= 3) return;
                _selectedHeroIndices.Add(row.HeroIndex);
            }
            UpdateRowHighlights();
            RefreshHeroFigures();
            RefreshFightButton();
        }

        private void UpdateRowHighlights()
        {
            foreach (var row in _heroRows)
            {
                bool selected = _selectedHeroIndices.Contains(row.HeroIndex);
                if (row.RowImage != null)
                    row.RowImage.color = selected ? UIHelpers.Hex("#2E7D32") : UIHelpers.Hex("#1A2436");
                var label = row.ToggleButton != null ? row.ToggleButton.GetComponentInChildren<Text>() : null;
                if (label != null) label.text = selected ? "Selected" : "Select";
            }
        }

        private void RefreshFightButton()
        {
            if (_fightButton == null) return;
            var boss = BossEncounter();
            bool canFight = !_fighting && boss != null
                && _selectedHeroIndices.Count >= 1
                && _selectedHeroIndices.Count <= 3
                && _controller != null && _controller.State != null
                && _controller.State.Energy >= boss.EnergyCost;
            UIHelpers.SetButtonEnabled(_fightButton, canFight);
        }

        // Hero figures in the 3D scene: one per SELECTED hero, so the
        // player sees who is fighting. A capsule body + sphere head,
        // colour-coded to the picker row. (Proper hero FBX models are a
        // later step — the figures read clearly at this scale, and the
        // boss is the focus.) Unselected heroes have no figure.
        private void RefreshHeroFigures()
        {
            foreach (var go in _heroFigures)
            {
                if (go != null) Destroy(go);
            }
            _heroFigures.Clear();
            _heroOrigPos.Clear();
            if (_controller == null || _controller.State == null || _controller.State.Heroes == null) return;
            // Real-time mode has no preview figures: the arena
            // combatants spawn when the fight starts.
            if (UseRealtime()) return;
            var zSpots = new float[] { -3f, 0f, 3f };
            for (int s = 0; s < _selectedHeroIndices.Count && s < 3; s++)
            {
                int heroIndex = _selectedHeroIndices[s];
                if (heroIndex < 0 || heroIndex >= _controller.State.Heroes.Count) continue;
                var hero = _controller.State.Heroes[heroIndex];
                if (hero == null) continue;
                var color = HeroColorFor(hero.Id);
                var fig = new GameObject("HeroFigure");
                fig.transform.SetParent(_sceneRoot.transform, false);
                // Closer to the camera and to centre (x -3.5, not -4) so
                // the heroes read clearly in the open middle band. The
                // figures are BIG (~3.5 tall, near-human scale next to
                // the 5+ tall robot) so the fight is actually visible.
                fig.transform.localPosition = new Vector3(-3.5f, 0f, zSpots[s]);
                // Body: capsule, ~2.6 tall. Head: sphere on top.
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "HeroBody";
                body.transform.SetParent(fig.transform, false);
                body.transform.localPosition = new Vector3(0f, 1.3f, 0f);
                body.transform.localScale = new Vector3(1.0f, 1.3f, 1.0f);
                SetColor(body.GetComponent<Renderer>(), color);
                Destroy(body.GetComponent<Collider>());
                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "HeroHead";
                head.transform.SetParent(fig.transform, false);
                head.transform.localPosition = new Vector3(0f, 2.9f, 0f);
                head.transform.localScale = Vector3.one * 0.8f;
                SetColor(head.GetComponent<Renderer>(), new Color(0.85f, 0.75f, 0.65f));
                Destroy(head.GetComponent<Collider>());
                // A flat coloured disc under the hero so it stands out
                // against the dark cave floor.
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "HeroDisc";
                disc.transform.SetParent(fig.transform, false);
                disc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                disc.transform.localScale = new Vector3(1.6f, 0.1f, 1.6f);
                SetColor(disc.GetComponent<Renderer>(), color);
                Destroy(disc.GetComponent<Collider>());
                _heroFigures.Add(fig);
                _heroOrigPos.Add(fig.transform.localPosition);
            }
        }

        private void OnFightClicked()
        {
            if (_fighting) return;
            var boss = BossEncounter();
            if (boss == null) return;
            if (_selectedHeroIndices.Count < 1 || _selectedHeroIndices.Count > 3) return;
            if (UseRealtime())
            {
                StartRealtimeFight(boss);
                return;
            }
            var heroIndices = new List<int>(_selectedHeroIndices);
            _fighting = true;
            RefreshFightButton();
            // Present the cave set (in case the level selection
            // crossed fight modes) and leave hero-selection mode:
            // show the 3D lair for the fight (the panel returns to
            // normal size; the UI then hides for the ~2s fight so
            // the full 3D fight is visible).
            PresentSceneForMode();
            SetSelectMode(false);
            if (_headerGo != null) _headerGo.SetActive(false);
            if (_bottomGo != null) _bottomGo.SetActive(false);
            // Resolve the fight immediately (combat is instant); the
            // animation below is purely visual, then the result shows.
            if (_controller.Expeditions.TryStartHeroes(boss, heroIndices, out var result, out var reason, allowLockedTier: true))
            {
                _controller.SaveGame();
                _controller.NotifyChanged();
                StartCoroutine(FightAnimationRoutine(result));
            }
            else
            {
                _fighting = false;
                // Fight could not start (e.g. not enough energy): back
                // to hero-selection mode (3D hidden, panel expanded).
                SetSelectMode(true);
                if (_headerGo != null) _headerGo.SetActive(true);
                if (_bottomGo != null) _bottomGo.SetActive(true);
                _resultText.text = reason;
                RefreshFightButton();
            }
        }

        // Fight animation (~2s, same shape as the map fight animation):
        // the robot lunges and shakes, the heroes lunge forward, then
        // victory = the robot tips over and sinks, the heroes bounce;
        // defeat = the heroes shake and shrink, the robot hops.
        private IEnumerator FightAnimationRoutine(BattleResult result)
        {
            float clashTime = 0.9f;
            float t = 0f;
            while (t < clashTime)
            {
                while (!_visible) yield return null;
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / clashTime);
                if (_robotGo != null)
                {
                    Vector3 jitter = new Vector3(
                        UnityEngine.Random.Range(-0.4f, 0.4f), 0f,
                        UnityEngine.Random.Range(-0.4f, 0.4f));
                    _robotGo.transform.localPosition = _robotOrigPos + jitter;
                    // Lunge toward the heroes.
                    _robotGo.transform.localPosition += new Vector3(-Mathf.Sin(p * Mathf.PI) * 1.2f, 0f, 0f);
                }
                for (int i = 0; i < _heroFigures.Count; i++)
                {
                    var fig = _heroFigures[i];
                    if (fig == null) continue;
                    Vector3 jitter = new Vector3(
                        UnityEngine.Random.Range(-0.2f, 0.2f), 0f,
                        UnityEngine.Random.Range(-0.2f, 0.2f));
                    fig.transform.localPosition = _heroOrigPos[i] + jitter;
                    // Heroes lunge toward the robot.
                    fig.transform.localPosition += new Vector3(Mathf.Sin(p * Mathf.PI) * 0.8f, 0f, 0f);
                }
                yield return null;
            }

            float outcomeTime = 1.1f;
            t = 0f;
            while (t < outcomeTime)
            {
                while (!_visible) yield return null;
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / outcomeTime);
                if (result.Victory)
                {
                    // Robot tips over and sinks; heroes bounce.
                    if (_robotGo != null)
                    {
                        _robotGo.transform.localRotation = _robotOrigRot * Quaternion.Euler(80f * p, 0f, 0f);
                        Vector3 pos = _robotOrigPos;
                        pos.y = _robotOrigPos.y - 2f * p;
                        _robotGo.transform.localPosition = pos;
                    }
                    for (int i = 0; i < _heroFigures.Count; i++)
                    {
                        var fig = _heroFigures[i];
                        if (fig == null) continue;
                        Vector3 pos = _heroOrigPos[i];
                        pos.y = _heroOrigPos[i].y + Mathf.Sin(p * Mathf.PI) * 0.6f;
                        fig.transform.localPosition = pos;
                    }
                }
                else
                {
                    // Heroes shake and shrink; robot hops (it won).
                    for (int i = 0; i < _heroFigures.Count; i++)
                    {
                        var fig = _heroFigures[i];
                        if (fig == null) continue;
                        Vector3 jitter = new Vector3(
                            UnityEngine.Random.Range(-0.4f, 0.4f), 0f,
                            UnityEngine.Random.Range(-0.4f, 0.4f));
                        fig.transform.localPosition = _heroOrigPos[i] + jitter;
                        fig.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, p);
                    }
                    if (_robotGo != null)
                    {
                        Vector3 pos = _robotOrigPos;
                        pos.y = _robotOrigPos.y + Mathf.Sin(p * Mathf.PI) * 0.8f;
                        _robotGo.transform.localPosition = pos;
                        _robotGo.transform.localRotation = _robotOrigRot;
                    }
                }
                yield return null;
            }

            // Restore transforms, then bring the UI back and show the
            // result. The header + bottom panel were hidden for the
            // fight; they return now with the result text.
            if (_robotGo != null)
            {
                _robotGo.transform.localPosition = _robotOrigPos;
                _robotGo.transform.localRotation = _robotOrigRot;
            }
            for (int i = 0; i < _heroFigures.Count; i++)
            {
                var fig = _heroFigures[i];
                if (fig == null) continue;
                fig.transform.localPosition = _heroOrigPos[i];
                fig.transform.localScale = Vector3.one;
            }
            if (_headerGo != null) _headerGo.SetActive(true);
            if (_bottomGo != null) _bottomGo.SetActive(true);
            // Back to hero-selection mode: hide the 3D lair and expand
            // the panel so all heroes show without scrolling, with the
            // result at the top.
            SetSelectMode(true);
            _fighting = false;
            // Boss ladder: a victory at the frontier unlocks the next
            // level. The boss never vanishes — it stays, and the player
            // can fight any unlocked level at will, energy permitting.
            if (result.Victory && _selectedBossLevel > _controller.State.BossLevelDefeated)
            {
                _controller.State.BossLevelDefeated = _selectedBossLevel;
                _controller.SaveGame();
                // Step up to the newly unlocked level for the next fight.
                _selectedBossLevel = HighestUnlockedBossLevel();
            }
            ShowResult(result);
            RefreshHeroRows();
            RefreshHeroFigures();
            RefreshBossInfo();
            OnFightComplete?.Invoke(result);
        }

        // ================= Real-time A.R.E.S.-09 fight =================

        private void StartRealtimeFight(EncounterDefinition boss)
        {
            var heroIndices = new List<int>(_selectedHeroIndices);
            _fighting = true;
            RefreshFightButton();
            // Leave hero-selection mode and present the arena (the
            // header + bottom panel hide, as in the turn-based flow;
            // the fight HUD takes over).
            SetSelectMode(false);
            if (_headerGo != null) _headerGo.SetActive(false);
            if (_bottomGo != null) _bottomGo.SetActive(false);
            PresentSceneForMode();

            if (!_controller.Expeditions.TryStartHeroesRealtime(boss, heroIndices, out var squad, out var reason, allowLockedTier: true))
            {
                _fighting = false;
                SetSelectMode(true);
                if (_headerGo != null) _headerGo.SetActive(true);
                if (_bottomGo != null) _bottomGo.SetActive(true);
                _resultText.text = reason;
                RefreshFightButton();
                return;
            }
            _controller.SaveGame();
            _controller.NotifyChanged();

            _realtimeEncounter = boss;
            _realtimeSquad = squad;
            _bossPhase = 1;
            BuildRealtimeCombatants(squad);

            var spawn = boss.Spawns[0];
            var stats = AresStats.FromLadder(spawn.Health, spawn.Attack, spawn.Defense,
                CombatService.EnemyScale(_controller.State.NexusLevel, boss.RequiredNexusLevel));
            var fighters = new List<IHeroCombatant>();
            foreach (var combatant in _combatants) fighters.Add(combatant);
            _aresController.Setup(stats, fighters, _droneTemplate, _aresVisual.transform,
                _slamRing, _ventRing, _coverSpheres);
            if (_bossHpText != null) _bossHpText.text = $"A.R.E.S.-09  {(int)stats.MaxHealth} / {(int)stats.MaxHealth}";
            if (_bossHpFill != null) _bossHpFill.fillAmount = 1f;
            _fightStartTime = Time.time;
            _realtimeActive = true;
            foreach (var combatant in _combatants) combatant.SetActive(true);
            _aresController.BeginFight();
            BuildFightChips();
            if (_hudRoot != null) _hudRoot.SetActive(true);
            if (_hudRoutine != null) StopCoroutine(_hudRoutine);
            _hudRoutine = StartCoroutine(FightHudRoutine());
        }

        // Spawn the hero combatants: the same colour-coded figures
        // as the cave view, entering from the arena's south gate
        // (local z -14, spread across x). Each gets an
        // AresHeroCombatant with its range band by class.
        private void BuildRealtimeCombatants(List<SquadCombatant> squad)
        {
            CleanupRealtimeCombatants();
            var xSpots = new float[] { -4f, 0f, 4f };
            Vector3 bossPos = _aresRoot.transform.position;
            for (int s = 0; s < squad.Count && s < 3; s++)
            {
                var member = squad[s];
                var color = HeroColorFor(member.Definition.Id);
                var fig = BuildCombatFigure(member.Definition, color);
                fig.transform.SetParent(_sceneRoot.transform, false);
                Vector3 spawnPos = new Vector3(500f + xSpots[s], 0f, -14f);
                fig.transform.position = spawnPos;
                var combatant = fig.AddComponent<AresHeroCombatant>();
                Vector3 dir = spawnPos - bossPos;
                dir.y = 0f;
                float bearing = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                float range = member.Definition.Class == UnitClass.Ranged ? 12f : 4.5f;
                float hpMult = 1f, atkMult = 1f, defMult = 1f;
                if (_controller.Research != null)
                    _controller.Research.ClassMultipliers(member.Definition.Class, out hpMult, out atkMult, out defMult);
                var spawn = _realtimeEncounter.Spawns[0];
                float bossDefense = spawn.Defense * CombatService.EnemyScale(
                    _controller.State.NexusLevel, _realtimeEncounter.RequiredNexusLevel);
                combatant.Setup(member.Unit, member.Definition, _aresRoot.transform,
                    bearing, range, bossDefense, _selectedBossLevel, hpMult, atkMult, defMult);
                _combatants.Add(combatant);
            }
        }

        // The hero figure build (same shapes as RefreshHeroFigures:
        // capsule body, sphere head, coloured ground disc) as a
        // standalone root the combatant can move and tip over.
        private GameObject BuildCombatFigure(UnitDefinition def, Color color)
        {
            var fig = new GameObject("HeroFigure_" + def.DisplayName);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "HeroBody";
            body.transform.SetParent(fig.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            body.transform.localScale = new Vector3(1.0f, 1.3f, 1.0f);
            SetColor(body.GetComponent<Renderer>(), color);
            Destroy(body.GetComponent<Collider>());
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "HeroHead";
            head.transform.SetParent(fig.transform, false);
            head.transform.localPosition = new Vector3(0f, 2.9f, 0f);
            head.transform.localScale = Vector3.one * 0.8f;
            SetColor(head.GetComponent<Renderer>(), new Color(0.85f, 0.75f, 0.65f));
            Destroy(head.GetComponent<Collider>());
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "HeroDisc";
            disc.transform.SetParent(fig.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            disc.transform.localScale = new Vector3(1.6f, 0.1f, 1.6f);
            SetColor(disc.GetComponent<Renderer>(), color);
            Destroy(disc.GetComponent<Collider>());
            return fig;
        }

        private void CleanupRealtimeCombatants()
        {
            foreach (var combatant in _combatants)
            {
                if (combatant != null) Destroy(combatant.gameObject);
            }
            _combatants.Clear();
            if (_aresController != null)
            {
                foreach (var drone in _aresController.Drones)
                {
                    if (drone != null) Destroy(drone.gameObject);
                }
            }
        }

        // Force-stop (the view is hiding mid-fight): no completion
        // is applied — the fight is abandoned.
        private void EndRealtimeFightImmediate()
        {
            _realtimeActive = false;
            _fighting = false;
            if (_hudRoutine != null) { StopCoroutine(_hudRoutine); _hudRoutine = null; }
            if (_hudRoot != null) _hudRoot.SetActive(false);
            if (_aresController != null) _aresController.EndFight();
            foreach (var combatant in _combatants) combatant.SetActive(false);
            CleanupRealtimeCombatants();
            ResetAresVisual();
            if (_ambience != null) _ambience.SetPhase2(false);
        }

        private void ResetAresVisual()
        {
            if (_aresVisual == null) return;
            _aresVisual.transform.localPosition = _aresVisualOrigPos;
            _aresVisual.transform.localRotation = _aresVisualOrigRot;
            if (_aresRoot != null)
                _aresRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (_coreMaterial != null && _coreMaterial.HasProperty("_EmissionColor"))
                _coreMaterial.SetColor("_EmissionColor", _coreBaseEmission);
        }

        // Outro + completion: victory tips the boss over (the
        // model's wrapper, same trick as the War Robot), then the
        // outcome is applied through ExpeditionService with the
        // usual boss rules (reward, hero XP at boss rate, ladder
        // unlock) and the result panel returns.
        private IEnumerator RealtimeEndRoutine(bool victory)
        {
            float outroTime = victory ? 1.6f : 1.2f;
            float t = 0f;
            while (t < outroTime)
            {
                while (!_visible) yield return null;
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / outroTime);
                if (victory && _aresVisual != null)
                {
                    _aresVisual.transform.localRotation = _aresVisualOrigRot * Quaternion.Euler(75f * p, 0f, 0f);
                    Vector3 pos = _aresVisualOrigPos;
                    pos.y = _aresVisualOrigPos.y - 1.6f * p;
                    _aresVisual.transform.localPosition = pos;
                }
                yield return null;
            }

            int fightSeconds = Mathf.Max(1, (int)(Time.time - _fightStartTime));
            var fallen = new List<int>();
            for (int i = 0; i < _combatants.Count; i++)
            {
                if (!_combatants[i].IsAlive) fallen.Add(_realtimeSquad[i].SquadIndex);
            }
            var result = _controller.Expeditions.CompleteHeroesRealtime(
                _realtimeEncounter, _realtimeSquad, fallen, victory, fightSeconds);

            _realtimeActive = false;
            if (_hudRoutine != null) { StopCoroutine(_hudRoutine); _hudRoutine = null; }
            if (_hudRoot != null) _hudRoot.SetActive(false);
            if (_aresController != null) _aresController.EndFight();
            foreach (var combatant in _combatants) combatant.SetActive(false);
            CleanupRealtimeCombatants();
            ResetAresVisual();
            if (_ambience != null) _ambience.SetPhase2(false);

            if (_headerGo != null) _headerGo.SetActive(true);
            if (_bottomGo != null) _bottomGo.SetActive(true);
            SetSelectMode(true);
            _fighting = false;
            // Boss ladder: a victory at the frontier unlocks the
            // next level (same rule as the turn-based path).
            if (result.Victory && _selectedBossLevel > _controller.State.BossLevelDefeated)
            {
                _controller.State.BossLevelDefeated = _selectedBossLevel;
                _selectedBossLevel = HighestUnlockedBossLevel();
            }
            _controller.SaveGame();
            _controller.NotifyChanged();
            ShowRealtimeResult(result);
            RefreshHeroRows();
            RefreshHeroFigures();
            RefreshBossInfo();
            OnFightComplete?.Invoke(result);
        }

        private void ShowRealtimeResult(BattleResult result)
        {
            var body = new System.Text.StringBuilder();
            body.AppendLine(result.Victory ? "VICTORY!" : "DEFEAT — SQUAD DOWN");
            body.AppendLine($"{result.EncounterName}  •  Fight time: {result.Rounds}s  •  Energy spent: {result.EnergySpent}");
            if (result.Victory)
            {
                body.AppendLine($"Reward: {CostText(result.RewardEarned)}");
                if (result.Flawless) body.AppendLine("Flawless victory: +25% XP bonus applied.");
            }
            else
            {
                body.AppendLine("No reward (defeat). Energy is not refunded.");
            }
            if (result.HeroCasualtyNames.Count == 0)
            {
                body.AppendLine("Heroes down: none");
            }
            else
            {
                body.AppendLine("Heroes down: " + string.Join(", ", result.HeroCasualtyNames) + " — heroes recover after the fight.");
            }
            body.AppendLine($"XP pool: {result.XpPool}");
            if (result.XpGains.Count == 0)
            {
                body.AppendLine("No XP gained.");
            }
            else
            {
                foreach (var gain in result.XpGains)
                {
                    string levelText = gain.NewLevel > gain.OldLevel
                        ? $"Lv {gain.OldLevel} → Lv {gain.NewLevel}"
                        : $"Lv {gain.OldLevel}";
                    body.AppendLine($"{gain.UnitName}: +{gain.XpGained} XP ({levelText})");
                }
            }
            _resultText.text = body.ToString();
        }

        // ================= Real-time fight HUD =================

        private void BuildFightHud()
        {
            _hudRoot = new GameObject("FightHud");
            _hudRoot.transform.SetParent(_root.transform, false);
            var hudRt = _hudRoot.AddComponent<RectTransform>();
            hudRt.anchorMin = Vector2.zero;
            hudRt.anchorMax = Vector2.one;
            hudRt.offsetMin = Vector2.zero;
            hudRt.offsetMax = Vector2.zero;

            // Boss HP bar across the top.
            var barBg = UIHelpers.Panel(_hudRoot, "BossHpBg", UIHelpers.Hex("#0D1420"),
                new Vector2(0.22f, 0.93f), new Vector2(0.78f, 0.985f), Vector2.zero, Vector2.zero);
            var fillGo = new GameObject("BossHpFill");
            fillGo.transform.SetParent(barBg.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0.01f, 0.45f);
            fillRt.anchorMax = new Vector2(0.99f, 0.95f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _bossHpFill = fillGo.AddComponent<Image>();
            _bossHpFill.color = UIHelpers.Hex("#C62828");
            _bossHpFill.type = Image.Type.Filled;
            _bossHpFill.fillMethod = Image.FillMethod.Horizontal;
            _bossHpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _bossHpFill.fillAmount = 1f;
            _bossHpText = UIHelpers.LabelInRect(barBg, "BossHpText", "A.R.E.S.-09", 22, Color.white,
                new Rect(0.02f, 0.02f, 0.98f, 0.45f), TextAnchor.MiddleCenter);

            // Telegraph warning, big and centre-top.
            _warningText = UIHelpers.LabelInRect(_hudRoot, "Warning", "", 44, UIHelpers.Hex("#FF7043"),
                new Rect(0.25f, 0.80f, 0.75f, 0.92f), TextAnchor.MiddleCenter);
            _warningText.gameObject.SetActive(false);

            // Hero chips along the bottom: tap a chip to dash that
            // hero. Built per fight by BuildFightChips.
            _chipsContent = new GameObject("HeroChips");
            _chipsContent.transform.SetParent(_hudRoot.transform, false);
            var chipsRt = _chipsContent.AddComponent<RectTransform>();
            chipsRt.anchorMin = new Vector2(0.24f, 0.015f);
            chipsRt.anchorMax = new Vector2(0.76f, 0.115f);
            chipsRt.offsetMin = Vector2.zero;
            chipsRt.offsetMax = Vector2.zero;
            var chipsLayout = _chipsContent.AddComponent<HorizontalLayoutGroup>();
            chipsLayout.spacing = 12;
            chipsLayout.childAlignment = TextAnchor.MiddleCenter;
            chipsLayout.childControlWidth = true;
            chipsLayout.childControlHeight = true;
            chipsLayout.childForceExpandWidth = true;
            chipsLayout.childForceExpandHeight = true;

            // Drone focus buttons (shown by the HUD tick while
            // drones live): tap to set squad focus fire.
            for (int i = 0; i < 3; i++)
            {
                int droneIndex = i;
                var btn = UIHelpers.Button(_hudRoot, "DroneBtn" + (i + 1), "DRONE " + (i + 1), 22,
                    UIHelpers.Hex("#4A1420"), Color.white);
                var btnRt = btn.GetComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(0.845f, 0.86f - i * 0.075f);
                btnRt.anchorMax = new Vector2(0.99f, 0.925f - i * 0.075f);
                btnRt.offsetMin = Vector2.zero;
                btnRt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() =>
                {
                    if (_aresController != null && droneIndex < _aresController.Drones.Count)
                        _aresController.SetFocusDrone(_aresController.Drones[droneIndex]);
                });
                btn.gameObject.SetActive(false);
                _droneButtons.Add(btn);
            }

            _hudRoot.SetActive(false);
        }

        private void BuildFightChips()
        {
            _chips.Clear();
            for (int i = _chipsContent.transform.childCount - 1; i >= 0; i--)
                Destroy(_chipsContent.transform.GetChild(i).gameObject);

            foreach (var combatant in _combatants)
            {
                var captured = combatant;
                var chipGo = new GameObject("Chip_" + combatant.DisplayName);
                chipGo.transform.SetParent(_chipsContent.transform, false);
                var chipImg = chipGo.AddComponent<Image>();
                chipImg.color = UIHelpers.Hex("#14202F");
                var btn = chipGo.AddComponent<Button>();
                btn.targetGraphic = chipImg;
                btn.onClick.AddListener(() => captured.TryDash());
                var nameText = UIHelpers.LabelInRect(chipGo, "ChipName",
                    combatant.DisplayName + " — TAP TO DASH", 20, Color.white,
                    new Rect(0.04f, 0.52f, 0.96f, 0.97f), TextAnchor.MiddleCenter);
                var hpBg = new GameObject("ChipHpBg");
                hpBg.transform.SetParent(chipGo.transform, false);
                var hpBgRt = hpBg.AddComponent<RectTransform>();
                hpBgRt.anchorMin = new Vector2(0.05f, 0.30f);
                hpBgRt.anchorMax = new Vector2(0.95f, 0.48f);
                hpBgRt.offsetMin = Vector2.zero;
                hpBgRt.offsetMax = Vector2.zero;
                var hpBgImg = hpBg.AddComponent<Image>();
                hpBgImg.color = UIHelpers.Hex("#0A0F16");
                var hpGo = new GameObject("ChipHpFill");
                hpGo.transform.SetParent(hpBg.transform, false);
                var hpRt = hpGo.AddComponent<RectTransform>();
                hpRt.anchorMin = Vector2.zero;
                hpRt.anchorMax = Vector2.one;
                hpRt.offsetMin = Vector2.zero;
                hpRt.offsetMax = Vector2.zero;
                var hpFill = hpGo.AddComponent<Image>();
                hpFill.color = UIHelpers.Hex("#43A047");
                hpFill.type = Image.Type.Filled;
                hpFill.fillMethod = Image.FillMethod.Horizontal;
                hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                hpFill.fillAmount = 1f;
                var dashGo = new GameObject("ChipDashFill");
                dashGo.transform.SetParent(chipGo.transform, false);
                var dashRt = dashGo.AddComponent<RectTransform>();
                dashRt.anchorMin = new Vector2(0.05f, 0.08f);
                dashRt.anchorMax = new Vector2(0.95f, 0.22f);
                dashRt.offsetMin = Vector2.zero;
                dashRt.offsetMax = Vector2.zero;
                var dashFill = dashGo.AddComponent<Image>();
                dashFill.color = UIHelpers.Hex("#29B6F6");
                dashFill.type = Image.Type.Filled;
                dashFill.fillMethod = Image.FillMethod.Horizontal;
                dashFill.fillOrigin = (int)Image.OriginHorizontal.Left;
                dashFill.fillAmount = 1f;
                _chips.Add(new HeroChip { Combatant = combatant, HpFill = hpFill, DashFill = dashFill, NameText = nameText });
            }
        }

        private void ShowWarning(string text)
        {
            if (_warningText == null) return;
            _warningText.text = text;
            _warningText.gameObject.SetActive(true);
            _warningUntil = Time.time + 1.4f;
        }

        private IEnumerator FightHudRoutine()
        {
            while (_realtimeActive)
            {
                RefreshFightHud();
                yield return new WaitForSeconds(0.1f);
            }
        }

        private void RefreshFightHud()
        {
            if (_warningText != null && _warningText.gameObject.activeSelf && Time.time > _warningUntil)
                _warningText.gameObject.SetActive(false);
            foreach (var chip in _chips)
            {
                if (chip.Combatant == null) continue;
                if (chip.HpFill != null) chip.HpFill.fillAmount = chip.Combatant.HealthFraction;
                if (chip.DashFill != null) chip.DashFill.fillAmount = chip.Combatant.DashCooldownFraction;
                if (chip.NameText != null)
                    chip.NameText.text = chip.Combatant.IsAlive
                        ? chip.Combatant.DisplayName + " — TAP TO DASH"
                        : chip.Combatant.DisplayName + " — DOWN";
            }
            // Drone focus buttons: visible while that drone lives.
            if (_aresController != null)
            {
                for (int i = 0; i < _droneButtons.Count; i++)
                {
                    var drone = i < _aresController.Drones.Count ? _aresController.Drones[i] : null;
                    bool show = drone != null && drone.IsAlive;
                    _droneButtons[i].gameObject.SetActive(show);
                    if (show)
                    {
                        var label = _droneButtons[i].GetComponentInChildren<Text>();
                        if (label != null)
                        {
                            string focus = _aresController.HeroTarget() == drone ? " ◄ FOCUS" : "";
                            label.text = $"DRONE {i + 1}  {(int)(drone.HealthFraction * 100f)}%{focus}";
                        }
                    }
                }
            }
            // Phase 2: the exposed core pulses.
            if (_bossPhase == 2 && _coreMaterial != null && _coreMaterial.HasProperty("_EmissionColor"))
            {
                float pulse = 1.6f + Mathf.Sin(Time.time * 6f) * 0.9f;
                _coreMaterial.SetColor("_EmissionColor", _coreBaseEmission * pulse);
            }
        }

        private void ShowResult(BattleResult result)
        {
            var body = new System.Text.StringBuilder();
            body.AppendLine(result.Victory ? "VICTORY!" : (result.Timeout ? "DEFEAT — TIMEOUT" : "DEFEAT"));
            body.AppendLine($"{result.EncounterName}  •  {result.Rounds} rounds  •  Energy spent: {result.EnergySpent}");
            if (result.Victory)
            {
                body.AppendLine($"Reward: {CostText(result.RewardEarned)}");
                if (result.Flawless) body.AppendLine("Flawless victory: +25% XP bonus applied.");
            }
            else
            {
                body.AppendLine("No reward (defeat). Energy is not refunded.");
            }
            if (result.CasualtyNames.Count == 0 && result.HeroCasualtyNames.Count == 0)
            {
                body.AppendLine("Casualties: none");
            }
            else
            {
                if (result.CasualtyNames.Count > 0)
                    body.AppendLine("Injured: " + string.Join(", ", result.CasualtyNames) + " — heal them at the Hospital.");
                if (result.HeroCasualtyNames.Count > 0)
                    body.AppendLine("Heroes down: " + string.Join(", ", result.HeroCasualtyNames) + " — heroes recover after the fight.");
            }
            body.AppendLine($"XP pool: {result.XpPool}");
            if (result.XpGains.Count == 0)
            {
                body.AppendLine("No XP gained.");
            }
            else
            {
                foreach (var gain in result.XpGains)
                {
                    string levelText = gain.NewLevel > gain.OldLevel
                        ? $"Lv {gain.OldLevel} → Lv {gain.NewLevel}"
                        : $"Lv {gain.OldLevel}";
                    body.AppendLine($"{gain.UnitName}: +{gain.XpGained} XP ({levelText})");
                }
            }
            _resultText.text = body.ToString();
        }

        private static string CostText(ResourceCost cost)
        {
            var parts = new List<string>();
            if (cost.Biomass > 0) parts.Add($"{cost.Biomass} Biomass");
            if (cost.Alloy > 0) parts.Add($"{cost.Alloy} Alloy");
            if (cost.Helium3 > 0) parts.Add($"{cost.Helium3} Helium-3");
            return parts.Count > 0 ? string.Join(", ", parts) : "None";
        }
    }
}
