using System.Collections.Generic;
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
            public Text StatsText;
            public Text CostText;
            public Button RecruitButton;
        }

        private GameObject _root;
        private GameObject _rowsContent;
        private Text _summaryText;
        private Text _footerText;
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
            scrollRt.anchorMin = new Vector2(0.02f, 0.08f);
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
                if (owned > 0)
                {
                    var ownedUnit = _controller.State.Heroes != null
                        ? _controller.State.Heroes.Find(u => u.Id == row.Def.Id)
                        : null;
                    if (ownedUnit != null) levelText = $"  •  Lv {ownedUnit.Level}";
                }
                row.StatsText.text = $"HP {row.Def.BaseHealth}  ATK {row.Def.BaseAttack}  DEF {row.Def.BaseDefense}{levelText}";
                var status = _controller.Recruitment.CheckRecruit(row.Def, out var cost, out var reason);
                string costText = CostText(cost);
                row.CostText.text = status == RecruitStatus.CanRecruit
                    ? $"Cost: {costText}"
                    : $"{reason}  Cost: {costText}";
                UIHelpers.SetButtonEnabled(row.RecruitButton, status == RecruitStatus.CanRecruit);
            }
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
                _rows.Add(new Row { Root = rowGo, Def = def, StatsText = stats, CostText = cost, RecruitButton = btn });
            }
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
