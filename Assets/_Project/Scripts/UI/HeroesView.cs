using System.Collections.Generic;
using LunarDominion.Boss;
using LunarDominion.Core;
using LunarDominion.Units;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Heroes screen (2026-11-10 redesign). Heroes ONLY —
    /// no regular army units (those are recruited from their barracks
    /// building panel now). Display only: it asks RecruitmentService
    /// for status/cost/reason and shows the answer. A single scrollable
    /// list, so it scales as more heroes become available over time
    /// (Patrick 2026-11-10) — no class tabs, since heroes span classes
    /// and the roster will grow. Each hero is unique (one of each);
    /// the cap is RecruitmentService.MaxHeroes (3 for the current 3 —
    /// raise it when new heroes are added). Heroes fight the War Robot
    /// in the lair and level up from boss-fight XP.</summary>
    public class HeroesView : MonoBehaviour
    {
        private class Row
        {
            public GameObject Root;
            public UnitDefinition Def;
            public Image Bg;
            public Text StatsText;
            public Text CostText;
            public Button RecruitButton;
        }

        private GameObject _root;
        private GameObject _rowsContent;
        private Text _summaryText;
        private Text _footerText;
        private UnitDefinition _selectedDef;
        private Text _detailTitle;
        private Text _detailStats;
        private Text _detailSkills;
        private List<Row> _rows = new List<Row>();
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            // Same panel geometry as the old ArmyView (full-width panel
            // above the bottom nav), so the layout the player knows
            // stays put — only the content changes.
            _root = UIHelpers.Panel(canvas.gameObject, "HeroesPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 200 + safe.Bottom), new Vector2(-20, 920 - safe.Top));
            UIHelpers.LabelInRect(_root, "Title", "HEROES", 36, Color.white,
                new Rect(0.04f, 0.90f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _summaryText = UIHelpers.LabelInRect(_root, "Summary", "", 26, Color.white,
                new Rect(0.04f, 0.83f, 0.96f, 0.90f), TextAnchor.MiddleCenter);

            _footerText = UIHelpers.LabelInRect(_root, "Footer",
                "Heroes fight the War Robot in the lair. One of each — each hero can only be recruited once. Heroes level up from boss-fight XP.",
                22, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.01f, 0.96f, 0.06f), TextAnchor.MiddleCenter);

            // Hero detail panel (Patrick 2026-11-11): tapping a
            // hero row selects it and the hero's full information
            // — level-scaled HP/ATK/DEF/SPD, XP, and skills — is
            // displayed here. This is where the information from
            // the removed fight-HUD chips lives now.
            var detail = UIHelpers.Panel(_root, "HeroDetail", UIHelpers.Hex("#101826"),
                new Vector2(0.04f, 0.075f), new Vector2(0.96f, 0.295f), Vector2.zero, Vector2.zero);
            _detailTitle = UIHelpers.LabelInRect(detail, "DetailName", "Tap a hero to see their details", 26, Color.white,
                new Rect(0.03f, 0.64f, 0.97f, 0.97f));
            _detailStats = UIHelpers.LabelInRect(detail, "DetailStats", "", 24, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.03f, 0.34f, 0.97f, 0.64f));
            _detailSkills = UIHelpers.LabelInRect(detail, "DetailSkills", "", 22, UIHelpers.Hex("#90CAF9"),
                new Rect(0.03f, 0.04f, 0.97f, 0.34f));

            BuildRowsScroll();
            BuildRows();
            _root.SetActive(false);
        }

        // Single vertical ScrollRect (same pattern as the old ArmyView's
        // rows), so the list fits and scrolls as the hero roster grows.
        private void BuildRowsScroll()
        {
            var scrollGo = new GameObject("RowsScroll");
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.31f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.80f);
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

            _rowsContent = new GameObject("Content");
            _rowsContent.transform.SetParent(viewportGo.transform, false);
            var contentRt = _rowsContent.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            var layout = _rowsContent.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _rowsContent.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.content = contentRt;
        }

        public void Show()
        {
            _root.SetActive(true);
            Refresh();
        }

        public void Hide() { _root.SetActive(false); }

        public void SetVisible(bool visible)
        {
            if (visible) Show(); else Hide();
        }

        public void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            int recruited = _controller.Recruitment != null ? _controller.Recruitment.CountHeroes() : 0;
            _summaryText.text = $"Heroes: {recruited} / {RecruitmentService.MaxHeroes} recruited";

            foreach (var row in _rows)
            {
                int owned = _controller.Recruitment.CountOwned(row.Def.Id);
                string levelText = "";
                int heroLevel = 1;
                if (owned > 0)
                {
                    var ownedUnit = _controller.State.Heroes != null
                        ? _controller.State.Heroes.Find(u => u.Id == row.Def.Id)
                        : null;
                    if (ownedUnit != null)
                    {
                        levelText = $"  •  Lv {ownedUnit.Level}";
                        heroLevel = ownedUnit.Level;
                    }
                }
                if (row.Bg != null)
                    row.Bg.color = row.Def == _selectedDef ? UIHelpers.Hex("#24466B") : UIHelpers.Hex("#1A2436");
                // Real level-scaled stats including SPD — the
                // numbers the fight HUD chips used to show
                // (Patrick 2026-11-11: the chips were removed
                // from the fight and this info lives here now).
                // Unrecruited heroes preview at level 1.
                row.StatsText.text = $"HP {Mathf.RoundToInt(UnitStats.ScaledHealth(row.Def, heroLevel))}  ATK {Mathf.RoundToInt(UnitStats.ScaledAttack(row.Def, heroLevel))}  DEF {Mathf.RoundToInt(UnitStats.ScaledDefense(row.Def, heroLevel))}  SPD {UnitStats.ScaledSpeed(row.Def, heroLevel)}{levelText}";
                var status = _controller.Recruitment.CheckRecruit(row.Def, out var cost, out var reason);
                string costText = CostText(cost);
                row.CostText.text = status == RecruitStatus.CanRecruit
                    ? $"Cost: {costText}"
                    : $"{reason}  Cost: {costText}";
                UIHelpers.SetButtonEnabled(row.RecruitButton, status == RecruitStatus.CanRecruit);
            }

            // Selected hero's detail panel.
            if (_detailTitle != null)
            {
                if (_selectedDef == null)
                {
                    _detailTitle.text = "Tap a hero to see their details";
                    if (_detailStats != null) _detailStats.text = "";
                    if (_detailSkills != null) _detailSkills.text = "";
                }
                else
                {
                    var def = _selectedDef;
                    var ownedUnit = _controller.State.Heroes != null
                        ? _controller.State.Heroes.Find(u => u.Id == def.Id)
                        : null;
                    int lvl = ownedUnit != null ? ownedUnit.Level : 1;
                    _detailTitle.text = ownedUnit != null
                        ? $"{def.DisplayName} — {def.Class} ★ HERO  •  Lv {lvl}"
                        : $"{def.DisplayName} — {def.Class} ★ HERO  •  Not recruited";
                    string statsLine = $"HP {Mathf.RoundToInt(UnitStats.ScaledHealth(def, lvl))}  ATK {Mathf.RoundToInt(UnitStats.ScaledAttack(def, lvl))}  DEF {Mathf.RoundToInt(UnitStats.ScaledDefense(def, lvl))}  SPD {UnitStats.ScaledSpeed(def, lvl)}";
                    if (ownedUnit != null)
                        statsLine += lvl >= 20 ? "  •  MAX LEVEL" : $"  •  XP {ownedUnit.XP} / {UnitStats.XpToNextLevel(lvl)}";
                    else
                        statsLine += "  •  Lv 1 stats";
                    if (_detailStats != null) _detailStats.text = statsLine;
                    if (_detailSkills != null)
                    {
                        var names = new List<string>();
                        var skills = SkillCatalog.ForHero(def.Id);
                        if (skills != null)
                        {
                            foreach (var s in skills) names.Add(s.Name);
                        }
                        _detailSkills.text = names.Count > 0 ? "Skills: " + string.Join("  •  ", names) : "";
                    }
                }
            }
        }

        private void SelectHero(UnitDefinition def)
        {
            _selectedDef = def;
            Refresh();
        }

        private static string CostText(ResourceCost cost)
        {
            var parts = new List<string>();
            if (cost.Biomass > 0) parts.Add($"{cost.Biomass} Biomass");
            if (cost.Alloy > 0) parts.Add($"{cost.Alloy} Alloy");
            if (cost.Helium3 > 0) parts.Add($"{cost.Helium3} Helium-3");
            return parts.Count > 0 ? string.Join(", ", parts) : "Free";
        }

        private void BuildRows()
        {
            foreach (var row in _rows)
            {
                if (row.Root != null) Destroy(row.Root);
            }
            _rows.Clear();
            if (_root == null || _rowsContent == null) return;

            // Heroes only (IsHero), sorted by class then name. The list
            // grows as new heroes are added — no class tabs, it scrolls.
            var heroes = new List<UnitDefinition>();
            foreach (var def in _controller.UnitDefinitions)
            {
                if (def != null && def.IsHero) heroes.Add(def);
            }
            heroes.Sort((a, b) =>
            {
                int c = a.Class.CompareTo(b.Class);
                return c != 0 ? c : string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.Ordinal);
            });

            for (int i = 0; i < heroes.Count; i++)
            {
                var def = heroes[i];
                var rowGo = new GameObject(def.DisplayName + "Row");
                rowGo.transform.SetParent(_rowsContent.transform, false);
                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = UIHelpers.Hex("#1A2436");
                var layout = rowGo.AddComponent<LayoutElement>();
                layout.preferredHeight = 120;
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.anchorMin = Vector2.zero;
                rowRt.anchorMax = Vector2.one;
                // Hero portrait (rendered by the hero's Blender
                // build script) when installed; without one the
                // text spans from the left edge as before.
                float textLeft = 0.03f;
                var iconTex = HeroIconTexture(def.Id);
                if (iconTex != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(rowGo.transform, false);
                    var raw = iconGo.AddComponent<RawImage>();
                    raw.texture = iconTex;
                    raw.raycastTarget = false;
                    var irt = iconGo.GetComponent<RectTransform>();
                    irt.anchorMin = new Vector2(0.015f, 0.08f);
                    irt.anchorMax = new Vector2(0.135f, 0.92f);
                    irt.offsetMin = Vector2.zero;
                    irt.offsetMax = Vector2.zero;
                    textLeft = 0.15f;
                }
                UIHelpers.LabelInRect(rowGo, "Name", $"{def.DisplayName}  ★ HERO  •  {def.Class}", 28, Color.white,
                    new Rect(textLeft, 0.62f, 0.72f, 0.97f));
                var stats = UIHelpers.LabelInRect(rowGo, "Stats", "", 24, UIHelpers.Hex("#C0C8D0"),
                    new Rect(textLeft, 0.32f, 0.72f, 0.62f));
                var cost = UIHelpers.LabelInRect(rowGo, "Cost", "", 24, UIHelpers.Hex("#FFD166"),
                    new Rect(textLeft, 0.03f, 0.72f, 0.32f));
                var btn = UIHelpers.Button(rowGo, "RecruitBtn", "Recruit", 26,
                    UIHelpers.Hex("#2E7D32"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.74f, 0.15f);
                rt.anchorMax = new Vector2(0.97f, 0.85f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var heroDef = def;
                btn.onClick.AddListener(() => OnRecruitClicked(heroDef));
                // Tapping the row (anywhere but the Recruit
                // button) selects the hero for the detail panel.
                var rowBtn = rowGo.AddComponent<Button>();
                rowBtn.targetGraphic = rowImg;
                rowBtn.onClick.AddListener(() => SelectHero(heroDef));
                _rows.Add(new Row { Root = rowGo, Def = def, Bg = rowImg, StatsText = stats, CostText = cost, RecruitButton = btn });
            }
            if (_selectedDef == null && heroes.Count > 0) _selectedDef = heroes[0];
        }

        // The hero's menu portrait (rendered by BuildHero<Name>.py
        // into Resources/Textures), or null when not installed.
        private static Texture2D HeroIconTexture(UnitId id)
        {
            string path = null;
            switch (id)
            {
                case UnitId.HeroReyes: path = "Textures/HeroIcon_Reyes"; break;
                case UnitId.HeroVega: path = "Textures/HeroIcon_Vega"; break;
                case UnitId.HeroOkafor: path = "Textures/HeroIcon_Okafor"; break;
            }
            return path != null ? Resources.Load<Texture2D>(path) : null;
        }

        private void OnRecruitClicked(UnitDefinition def)
        {
            if (_controller.Recruitment.TryRecruit(def, out _))
            {
                _controller.SaveGame();
            }
            _controller.NotifyChanged();
            Refresh();
        }
    }
}
