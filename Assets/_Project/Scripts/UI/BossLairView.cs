using System;
using System.Collections;
using System.Collections.Generic;
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

        // UI.
        private GameObject _root;
        private GameObject _headerGo;
        private GameObject _bottomGo;
        private Text _titleText;
        private Text _bossInfoText;
        private Text _energyText;
        private Text _resultText;
        private Button _fightButton;
        private Button _closeButton;
        private GameObject _heroRowsContent;
        private readonly List<HeroRow> _heroRows = new List<HeroRow>();
        private readonly List<int> _selectedHeroIndices = new List<int>();

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
            // Dark ground (60x40, big enough that its edges sit far from
            // the camera and read as cave floor, not a visible rim).
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "LairGround";
            ground.transform.SetParent(_sceneRoot.transform, false);
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
                _robotGo.transform.SetParent(_sceneRoot.transform, false);
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
                _robotGo.transform.SetParent(_sceneRoot.transform, false);
                _robotGo.transform.localPosition = new Vector3(4f, 2.5f, 0f);
                _robotGo.transform.localScale = new Vector3(2f, 5f, 2f);
                SetColor(_robotGo.GetComponent<Renderer>(), new Color(0.25f, 0.25f, 0.30f));
                Destroy(_robotGo.GetComponent<Collider>());
            }
            _robotOrigPos = _robotGo.transform.localPosition;
            _robotOrigRot = _robotGo.transform.localRotation;
            foreach (var collider in _robotGo.GetComponentsInChildren<Collider>())
                Destroy(collider);
        }

        private void AddRock(Vector3 pos, Vector3 size)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "LairRock";
            rock.transform.SetParent(_sceneRoot.transform, false);
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

            // Header band (top ~20%): title, boss info, energy. Opaque
            // dark so the text reads over the 3D scene.
            _headerGo = UIHelpers.Panel(_root, "Header", UIHelpers.Hex("#0D1420"),
                new Vector2(0, 0.80f), new Vector2(1, 1f), Vector2.zero, Vector2.zero);
            _titleText = UIHelpers.LabelInRect(_headerGo, "Title", "WAR ROBOT LAIR", 36, Color.white,
                new Rect(0.04f, 0.62f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _bossInfoText = UIHelpers.LabelInRect(_headerGo, "BossInfo", "", 24, UIHelpers.Hex("#FFD166"),
                new Rect(0.04f, 0.30f, 0.96f, 0.62f), TextAnchor.MiddleCenter);
            _energyText = UIHelpers.LabelInRect(_headerGo, "Energy", "", 22, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.02f, 0.96f, 0.30f), TextAnchor.MiddleCenter);

            // Bottom band (bottom ~45%): result text, hero picker, and
            // the Fight / Back buttons. Opaque dark; the 3D scene shows
            // in the open middle band above this.
            _bottomGo = UIHelpers.Panel(_root, "BottomPanel", UIHelpers.Hex("#0D1420"),
                new Vector2(0, 0f), new Vector2(1, 0.45f), Vector2.zero, Vector2.zero);
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
            _sceneRoot.SetActive(true);
            _root.SetActive(true);
            // A fresh open always shows the header + bottom panel (they
            // are hidden during a fight — see OnFightClicked).
            if (_headerGo != null) _headerGo.SetActive(true);
            if (_bottomGo != null) _bottomGo.SetActive(true);
            RefreshHeroRows();
            RefreshHeroFigures();
            RefreshBossInfo();
            // Camera: frame the lair interior — the robot large on the
            // right, the heroes on the left. The lair lives at x 500
            // (far from the base, so the base floor never shows through),
            // so the camera goes there too. The look-at is LOW (y 0.8,
            // just below the heroes' centre) so the ground-level heroes
            // sit near screen centre, in the open middle band above the
            // bottom panel — not hidden behind it. Pulled back a touch
            // so the robot and the heroes both fit.
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(498f, 7f, -16f);
                cam.transform.LookAt(new Vector3(500f, 0.8f, 0f));
            }
        }

        public void Hide()
        {
            _visible = false;
            if (_sceneRoot != null) _sceneRoot.SetActive(false);
            if (_root != null) _root.SetActive(false);
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
            _bossInfoText.text = $"War Robot  •  HP {(boss.Spawns.Count > 0 ? boss.Spawns[0].Health : 0)}  ATK {(boss.Spawns.Count > 0 ? boss.Spawns[0].Attack : 0)}  DEF {(boss.Spawns.Count > 0 ? boss.Spawns[0].Defense : 0)}  •  Reward {boss.Reward.Biomass}B {boss.Reward.Alloy}A {boss.Reward.Helium3}H  •  {boss.XpPool} XP";
            _energyText.text = $"Energy: {_controller.State.Energy} / {boss.EnergyCost}  •  Unlocks at Nexus {boss.RequiredNexusLevel} (you are Nexus {_controller.State.NexusLevel})";
            RefreshFightButton();
        }

        private EncounterDefinition BossEncounter()
        {
            if (_controller == null || _controller.EncounterDefinitions == null) return null;
            return _controller.EncounterDefinitions.Find(e => e != null && e.Tier == 4);
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
                layout.preferredHeight = 100;
                var rt = emptyGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                UIHelpers.LabelInRect(emptyGo, "Empty", "No heroes recruited yet — recruit one from the Army tab (Marine Foundry).", 24, UIHelpers.Hex("#C0C8D0"),
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
                rowLayout.preferredHeight = 100;
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
            var heroIndices = new List<int>(_selectedHeroIndices);
            _fighting = true;
            RefreshFightButton();
            // Hide the UI during the fight (header + bottom panel: the
            // hero rows, FIGHT and Back buttons) so the player sees the
            // full 3D fight unobstructed. It comes back with the result
            // when the animation finishes (FightAnimationRoutine).
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
            _fighting = false;
            ShowResult(result);
            RefreshHeroRows();
            RefreshHeroFigures();
            RefreshBossInfo();
            OnFightComplete?.Invoke(result);
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
            body.AppendLine(result.CasualtyNames.Count == 0
                ? "Casualties: none"
                : "Injured: " + string.Join(", ", result.CasualtyNames) + " — heal them at the Hospital.");
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
