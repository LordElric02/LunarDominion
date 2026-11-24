using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Progression;
using LunarDominion.Units;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Building selection panel. Display only: it asks
    /// ProgressionService for status/cost/reason and shows the answer.
    /// The Upgrade button is disabled with a reason whenever it cannot be used.
    /// Hospital (2026-11-09): when the Hospital is selected and injured
    /// units are waiting, the info/status area is replaced by the injured
    /// list — one row per injured unit (name, level, heal cost) with a
    /// Heal button that restores it to the army via HospitalService.</summary>
    public class BuildingPanelView : MonoBehaviour
    {
        private class InjuredRow
        {
            public GameObject Root;
            public OwnedUnit Unit;
            public Text Label;
            public Button HealButton;
        }

        // Barracks recruitment (2026-11-10 redesign): one row per
        // regular unit this building recruits, with a Recruit button.
        // Heroes are NOT here — they live in the Heroes tab.
        private class RecruitRow
        {
            public GameObject Root;
            public UnitDefinition Def;
            public Text NameText;
            public Text StatsText;
            public Text OwnedText;
            public Text CostText;
            public Button RecruitButton;
            public Button PromoteButton;
        }

        private GameObject _root;
        private Text _nameText;
        private Text _levelText;
        private Text _infoText;
        private Text _statusText;
        private Button _upgradeButton;
        private Text _upgradeLabel;
        private GameController _controller;
        private BuildingDefinition _current;

        // Hospital injured list (hidden unless the Hospital is selected
        // and injured units are waiting). Replaces the info/status area.
        private GameObject _injuredRoot;
        private Text _injuredTitle;
        private GameObject _injuredContent;
        private readonly List<InjuredRow> _injuredRows = new List<InjuredRow>();
        private GameObject _healAllRow;
        private string _healFeedback = "";

        // Barracks recruitment list (hidden unless a barracks building
        // is selected). Replaces the info/status area, like the
        // Hospital list does — a building is one or the other, never
        // both, so the two sections never compete for the space.
        private GameObject _recruitRoot;
        private Text _recruitTitle;
        private GameObject _recruitContent;
        private readonly List<RecruitRow> _recruitRows = new List<RecruitRow>();

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            // 2026-11-06 landscape: right-side panel instead of a bottom
            // sheet, so it never covers the base. Anchored to the right
            // third of the screen (left edge at 0.68 sits at NDC x 0.36,
            // clear of the Nova building's on-screen right edge at
            // 0.33), from above the quick-select row to just below the
            // top bar. The name label stops at 0.53 so long names wrap
            // instead of running under the Upgrade button.
            _root = UIHelpers.Panel(canvas.gameObject, "BuildingPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0.68f, 0), new Vector2(1, 0), new Vector2(20, 320 + safe.Bottom), new Vector2(-20 - safe.Right, 920 - safe.Top));
            _nameText = UIHelpers.LabelInRect(_root, "Name", "Select a building", 36, Color.white,
                new Rect(0.04f, 0.72f, 0.53f, 0.98f));
            _levelText = UIHelpers.LabelInRect(_root, "Level", "", 30, Color.white,
                new Rect(0.04f, 0.50f, 0.96f, 0.72f));
            _infoText = UIHelpers.LabelInRect(_root, "Info", "", 26, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.28f, 0.96f, 0.50f));
            _statusText = UIHelpers.LabelInRect(_root, "Status", "", 24, UIHelpers.Hex("#FFD166"),
                new Rect(0.04f, 0.08f, 0.96f, 0.28f));

            _upgradeButton = UIHelpers.Button(_root, "UpgradeBtn", "Upgrade", 30,
                UIHelpers.Hex("#2E7D32"), Color.white);
            var rt = _upgradeButton.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.55f, 0.72f);
            rt.anchorMax = new Vector2(0.96f, 0.98f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _upgradeButton.onClick.AddListener(OnUpgradeClicked);

            BuildInjuredSection();
            BuildRecruitSection();
            _root.SetActive(false);
        }

        // Hospital injured list: a title + a vertical ScrollRect (same
        // pattern as the HeroesView rows), occupying the info/status area
        // (y 0.08-0.50) when the Hospital is selected with injured units.
        private void BuildInjuredSection()
        {
            _injuredRoot = new GameObject("InjuredSection");
            _injuredRoot.transform.SetParent(_root.transform, false);
            var rootRt = _injuredRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.04f, 0.08f);
            rootRt.anchorMax = new Vector2(0.96f, 0.50f);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            _injuredTitle = UIHelpers.LabelInRect(_injuredRoot, "InjuredTitle",
                "INJURED — tap Heal to restore to the army", 24, UIHelpers.Hex("#FF6B6B"),
                new Rect(0f, 0.82f, 1f, 1f), TextAnchor.MiddleLeft);

            var scrollGo = new GameObject("InjuredScroll");
            scrollGo.transform.SetParent(_injuredRoot.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 0.82f);
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

            _injuredContent = new GameObject("Content");
            _injuredContent.transform.SetParent(viewportGo.transform, false);
            var contentRt = _injuredContent.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            var layout = _injuredContent.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _injuredContent.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = contentRt;
            _injuredRoot.SetActive(false);
        }

        // Barracks recruitment list: a title + a vertical ScrollRect
        // (same pattern as the Hospital injured list), occupying the
        // info/status area (y 0.08-0.50) when a barracks building is
        // selected. One row per regular unit that building recruits.
        private void BuildRecruitSection()
        {
            _recruitRoot = new GameObject("RecruitSection");
            _recruitRoot.transform.SetParent(_root.transform, false);
            var rootRt = _recruitRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.04f, 0.08f);
            rootRt.anchorMax = new Vector2(0.96f, 0.50f);
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            _recruitTitle = UIHelpers.LabelInRect(_recruitRoot, "RecruitTitle",
                "RECRUIT", 24, UIHelpers.Hex("#FFD166"),
                new Rect(0f, 0.82f, 1f, 1f), TextAnchor.MiddleLeft);

            var scrollGo = new GameObject("RecruitScroll");
            scrollGo.transform.SetParent(_recruitRoot.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0f, 0f);
            scrollRt.anchorMax = new Vector2(1f, 0.82f);
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

            _recruitContent = new GameObject("Content");
            _recruitContent.transform.SetParent(viewportGo.transform, false);
            var contentRt = _recruitContent.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            var layout = _recruitContent.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _recruitContent.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.content = contentRt;
            _recruitRoot.SetActive(false);
        }

        public void Show(BuildingDefinition def)
        {
            _current = def;
            _healFeedback = "";
            _root.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _current = null;
        }

        public void Refresh()
        {
            if (_current == null || _root == null || !_root.activeSelf) return;
            int level = _controller.State.GetLevel(_current.Id);
            bool locked = level == 0;
            _nameText.text = _current.DisplayName;
            _infoText.text = _current.Description;

            var status = _controller.Progression.CheckUpgrade(_current, out var cost, out var reason);
            // Upgrade cost on the level line (2026-11-16, Patrick):
            // the status text below also carries the cost, but it
            // is hidden whenever the recruit or injured list is
            // showing — which is exactly the barracks and the
            // Hospital — so the cost was invisible for the
            // buildings he upgrades most. The level line is always
            // visible, for every building. The cost is read
            // straight from Economy (not CheckUpgrade's out cost,
            // which is unset on the Nexus-gate branch).
            if (locked)
            {
                _levelText.text = "Locked";
            }
            else if (status == UpgradeStatus.MaxLevel)
            {
                _levelText.text = $"Level {level} — max level";
            }
            else
            {
                var nextCost = _controller.Economy.UpgradeCost(_current, level + 1);
                _levelText.text = $"Level {level}  →  Level {level + 1}\nCost: {nextCost.Biomass} Biomass, {nextCost.Alloy} Alloy, {nextCost.Helium3} Helium-3";
            }
            if (locked)
            {
                _statusText.text = reason;
            }
            else if (status == UpgradeStatus.MaxLevel)
            {
                _statusText.text = "Max level reached.";
            }
            else
            {
                _statusText.text = $"{reason}  Cost: {cost.Biomass} Biomass, {cost.Alloy} Alloy, {cost.Helium3} Helium-3.";
                if (_current.BaseProductionPerHour > 0f)
                {
                    float rate = _controller.Economy.ProductionPerHour(_current, level);
                    _statusText.text += $"  Produces {rate:0} / hour at current level.";
                }
            }
            bool canUpgrade = status == UpgradeStatus.CanUpgrade;
            UIHelpers.SetButtonEnabled(_upgradeButton, canUpgrade);
            var label = _upgradeButton.GetComponentInChildren<Text>();
            if (label != null) label.text = locked ? "Build" : "Upgrade";

            RefreshInjuredSection();
            RefreshRecruitSection();
            // The info/status texts show only when neither the injured
            // list nor the recruit list occupies their area.
            bool listShowing = (_injuredRoot != null && _injuredRoot.activeSelf)
                || (_recruitRoot != null && _recruitRoot.activeSelf);
            _infoText.gameObject.SetActive(!listShowing);
            _statusText.gameObject.SetActive(!listShowing);
        }

        // Barracks only: show the recruitment list (one row per regular
        // unit this building recruits). Heroes are NOT listed here —
        // they live in the Heroes tab. The list occupies the info/status
        // area, like the Hospital injured list does.
        private void RefreshRecruitSection()
        {
            bool isBarracks = _current != null
                && (_current.Id == BuildingId.MarineFoundry
                    || _current.Id == BuildingId.NovaArsenal
                    || _current.Id == BuildingId.AegisMotorPool);
            _recruitRoot.SetActive(isBarracks);
            if (!isBarracks)
            {
                foreach (var row in _recruitRows) row.Root.SetActive(false);
                return;
            }

            _recruitTitle.text = $"RECRUIT — {_current.DisplayName}";

            // Rebuild the rows to match the current building. The list
            // is small (3 units per barracks), so a full rebuild is cheap.
            foreach (var row in _recruitRows) Object.Destroy(row.Root);
            _recruitRows.Clear();
            var units = new List<UnitDefinition>();
            foreach (var def in _controller.UnitDefinitions)
            {
                if (def != null && !def.IsHero && def.BarracksBuilding == _current.Id) units.Add(def);
            }
            units.Sort((a, b) => a.Tier.CompareTo(b.Tier));

            // Promotion target (2026-11-23, Patrick): the highest
            // tier this barracks has unlocked. Every lower tier's
            // row offers promotion straight to it — at Aegis 25-27
            // that is T4, so T1-T3 all promote to T4; at barracks
            // 30 it is T6, and T1-T5 all promote to T6.
            int barracksLevel = _controller.State.GetLevel(_current.Id);
            UnitDefinition maxDef = null;
            foreach (var u in units)
            {
                if (RecruitmentService.RequiredBarracksLevel(u.Tier) <= barracksLevel) maxDef = u;
            }

            foreach (var def in units)
            {
                var row = new RecruitRow { Def = def };
                row.Root = new GameObject(def.DisplayName + "Row");
                row.Root.transform.SetParent(_recruitContent.transform, false);
                var rowRt = row.Root.AddComponent<RectTransform>();
                rowRt.anchorMin = Vector2.zero;
                rowRt.anchorMax = Vector2.one;
                bool promotable = maxDef != null && def.Tier < maxDef.Tier;
                var le = row.Root.AddComponent<LayoutElement>();
                // Promotable rows are taller: they carry a Promote
                // button under the Recruit button (2026-11-23).
                le.preferredHeight = promotable ? 150 : 118;
                le.minHeight = le.preferredHeight;
                var rowBg = row.Root.AddComponent<Image>();
                rowBg.color = UIHelpers.Hex("#1A2436");

                row.NameText = UIHelpers.LabelInRect(row.Root, "Name",
                    $"{def.DisplayName} — Tier {def.Tier}", 26, Color.white,
                    new Rect(0.03f, 0.66f, 0.66f, 0.98f), TextAnchor.MiddleLeft);
                row.StatsText = UIHelpers.LabelInRect(row.Root, "Stats", "", 22, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.03f, 0.38f, 0.66f, 0.66f), TextAnchor.MiddleLeft);
                // Owned sits as a caption UNDER the Recruit button:
                // the one patch of row space no text line can grow
                // into. (LabelInRect's Rect is anchor corners —
                // minX, minY, maxX, maxY — not x/y/width/height.)
                row.OwnedText = UIHelpers.LabelInRect(row.Root, "Owned", "", 20, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.68f, 0.02f, 0.97f, 0.28f), TextAnchor.MiddleCenter);
                row.CostText = UIHelpers.LabelInRect(row.Root, "Cost", "", 20, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.03f, 0.03f, 0.66f, 0.38f), TextAnchor.MiddleLeft);

                row.RecruitButton = UIHelpers.Button(row.Root, "RecruitBtn", "Recruit", 24,
                    UIHelpers.Hex("#2E7D32"), Color.white);
                var btnRt = row.RecruitButton.GetComponent<RectTransform>();
                if (promotable)
                {
                    btnRt.anchorMin = new Vector2(0.68f, 0.56f);
                    btnRt.anchorMax = new Vector2(0.97f, 0.90f);
                }
                else
                {
                    btnRt.anchorMin = new Vector2(0.68f, 0.30f);
                    btnRt.anchorMax = new Vector2(0.97f, 0.88f);
                }
                btnRt.offsetMin = Vector2.zero;
                btnRt.offsetMax = Vector2.zero;
                var captured = def;
                row.RecruitButton.onClick.AddListener(() => OnRecruitClicked(captured));

                // Promotion (2026-11-23, Patrick): on every row
                // below the highest unlocked tier, a Promote button
                // under Recruit upgrades the best owned unit OF
                // THIS ROW'S TIER straight to that highest tier,
                // at the difference in recruit costs (stepping
                // tier by tier would total the same). The label
                // names the target tier and the price (or the
                // short blocker); the full reason logic lives in
                // RecruitmentService.CheckPromote, like Recruit.
                if (promotable)
                {
                    var fromDef = def;
                    var toDef = maxDef;
                    row.PromoteButton = UIHelpers.Button(row.Root, "PromoteBtn", "Promote", 20,
                        UIHelpers.Hex("#8D6E00"), Color.white);
                    var pRt = row.PromoteButton.GetComponent<RectTransform>();
                    pRt.anchorMin = new Vector2(0.68f, 0.30f);
                    pRt.anchorMax = new Vector2(0.97f, 0.54f);
                    pRt.offsetMin = Vector2.zero;
                    pRt.offsetMax = Vector2.zero;
                    var capturedFrom = fromDef;
                    var capturedTo = toDef;
                    row.PromoteButton.onClick.AddListener(() => OnPromoteClicked(capturedFrom, capturedTo));
                    var pStatus = _controller.Recruitment.CheckPromote(fromDef, toDef,
                        out _, out var pCost, out _);
                    string pLabel = $"Promote → T{toDef.Tier}";
                    pLabel += "\n" + (pStatus == PromoteStatus.CanPromote
                        ? CostText(pCost)
                        : pStatus == PromoteStatus.NoCandidate ? "None owned"
                        : pStatus == PromoteStatus.InsufficientResources ? "Can't afford"
                        : "Locked");
                    var pText = row.PromoteButton.GetComponentInChildren<Text>();
                    if (pText != null) pText.text = pLabel;
                    UIHelpers.SetButtonEnabled(row.PromoteButton, pStatus == PromoteStatus.CanPromote);
                }

                int owned = _controller.Recruitment.CountOwned(def.Id);
                // Owned gets its own label under the Recruit
                // button (2026-11-20, Patrick): appended to the
                // stats string it overflowed the label's width and
                // clipped off at the bullet — the same failure the
                // cost line had on 2026-11-16 — so the count never
                // showed even though it was always computed. The
                // stats string is single-spaced for width margin
                // (the top tiers' four-digit numbers are the
                // longest strings the label ever holds).
                row.StatsText.text = $"HP {def.BaseHealth} ATK {def.BaseAttack} DEF {def.BaseDefense}";
                row.OwnedText.text = $"Owned: {owned}";
                var status = _controller.Recruitment.CheckRecruit(def, out var cost, out var reason);
                // Cost on its own line (2026-11-16, Patrick): it
                // used to be appended to the name line, where the
                // label's width clipped it off — the name + tier +
                // bullet filled the line and the price never
                // showed. When the unit cannot be recruited, the
                // line carries the reason instead (amber), like
                // the Heroes rows do with their cost line.
                row.CostText.text = status == RecruitStatus.CanRecruit
                    ? $"Cost: {CostText(cost)}"
                    : reason;
                row.CostText.color = status == RecruitStatus.CanRecruit
                    ? UIHelpers.Hex("#C0C8D0")
                    : UIHelpers.Hex("#FFD166");
                UIHelpers.SetButtonEnabled(row.RecruitButton, status == RecruitStatus.CanRecruit);
                _recruitRows.Add(row);
            }
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

        private void OnPromoteClicked(UnitDefinition fromDef, UnitDefinition toDef)
        {
            if (_controller.Recruitment.TryPromote(fromDef, toDef, out _))
            {
                _controller.SaveGame();
            }
            _controller.NotifyChanged();
            Refresh();
        }

        // Hospital only: show the injured list when injured units are
        // waiting. The info/status texts are hidden while EITHER the
        // injured list or the recruit list is shown (handled in
        // Refresh, after both sections are refreshed).
        private void RefreshInjuredSection()
        {
            bool isHospital = _current != null && _current.Id == BuildingId.Hospital;
            bool hasInjured = isHospital && _controller.Hospital != null && _controller.Hospital.HasInjured;
            _injuredRoot.SetActive(hasInjured);
            if (!hasInjured)
            {
                foreach (var row in _injuredRows) row.Root.SetActive(false);
                return;
            }

            _injuredTitle.text = string.IsNullOrEmpty(_healFeedback)
                ? $"INJURED ({_controller.State.Injured.Count}) — tap Heal to restore to the army"
                : _healFeedback;

            // Rebuild the rows to match the current injured list. The
            // list can be long now (80-man armies), but a full rebuild
            // per refresh is still cheap.
            foreach (var row in _injuredRows) Object.Destroy(row.Root);
            _injuredRows.Clear();
            // Mass heal (Patrick 2026-11-16): a Heal All row on top of
            // the list — one tap heals everyone the current resources
            // cover, in list order, skipping any it cannot afford.
            if (_healAllRow != null) Object.Destroy(_healAllRow);
            _healAllRow = new GameObject("HealAllRow");
            _healAllRow.transform.SetParent(_injuredContent.transform, false);
            var haRt = _healAllRow.AddComponent<RectTransform>();
            haRt.anchorMin = Vector2.zero;
            haRt.anchorMax = Vector2.one;
            var haLe = _healAllRow.AddComponent<LayoutElement>();
            haLe.preferredHeight = 64;
            haLe.minHeight = 64;
            var haBg = _healAllRow.AddComponent<Image>();
            haBg.color = UIHelpers.Hex("#1A2436");
            var totalCost = _controller.Hospital.TotalHealCost();
            var healAllBtn = UIHelpers.Button(_healAllRow, "HealAllBtn",
                $"HEAL ALL ({_controller.State.Injured.Count}) — {CostText(totalCost)}", 26,
                UIHelpers.Hex("#2E7D32"), Color.white);
            var habRt = healAllBtn.GetComponent<RectTransform>();
            habRt.anchorMin = new Vector2(0.03f, 0.10f);
            habRt.anchorMax = new Vector2(0.97f, 0.90f);
            habRt.offsetMin = Vector2.zero;
            habRt.offsetMax = Vector2.zero;
            healAllBtn.onClick.AddListener(() => OnHealAllClicked());
            bool anyAffordable = false;
            foreach (var unit in _controller.State.Injured)
            {
                if (_controller.Economy.CanAfford(_controller.Hospital.HealCost(unit)))
                {
                    anyAffordable = true;
                    break;
                }
            }
            UIHelpers.SetButtonEnabled(healAllBtn,
                anyAffordable && _controller.State.GetLevel(BuildingId.Hospital) > 0);
            foreach (var unit in _controller.State.Injured)
            {
                var def = _controller.UnitDefinitions.Find(u => u.Id == unit.Id);
                string name = def != null ? def.DisplayName : unit.Id.ToString();
                var healCost = _controller.Hospital.HealCost(unit);
                string costText = CostText(healCost);
                var row = new InjuredRow { Unit = unit };
                row.Root = new GameObject("InjuredRow");
                row.Root.transform.SetParent(_injuredContent.transform, false);
                var rowRt = row.Root.AddComponent<RectTransform>();
                rowRt.anchorMin = Vector2.zero;
                rowRt.anchorMax = Vector2.one;
                var le = row.Root.AddComponent<LayoutElement>();
                le.preferredHeight = 64;
                le.minHeight = 64;
                var rowBg = row.Root.AddComponent<Image>();
                rowBg.color = UIHelpers.Hex("#1A2436");

                row.Label = UIHelpers.LabelInRect(row.Root, "Label",
                    $"{name}  Lv {unit.Level}  •  Heal: {costText}", 24, Color.white,
                    new Rect(0.03f, 0.05f, 0.68f, 0.95f), TextAnchor.MiddleLeft);

                row.HealButton = UIHelpers.Button(row.Root, "HealBtn", "Heal", 26,
                    UIHelpers.Hex("#2E7D32"), Color.white);
                var btnRt = row.HealButton.GetComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(0.70f, 0.10f);
                btnRt.anchorMax = new Vector2(0.97f, 0.90f);
                btnRt.offsetMin = Vector2.zero;
                btnRt.offsetMax = Vector2.zero;
                var captured = unit;
                row.HealButton.onClick.AddListener(() => OnHealClicked(captured));
                // Disable the Heal button when the player cannot afford it.
                UIHelpers.SetButtonEnabled(row.HealButton,
                    _controller.Economy.CanAfford(healCost) && _controller.State.GetLevel(BuildingId.Hospital) > 0);
                _injuredRows.Add(row);
            }
        }

        private static string CostText(ResourceCost cost)
        {
            var parts = new List<string>();
            if (cost.Biomass > 0) parts.Add($"{cost.Biomass}B");
            if (cost.Alloy > 0) parts.Add($"{cost.Alloy}A");
            if (cost.Helium3 > 0) parts.Add($"{cost.Helium3}H");
            return parts.Count == 0 ? "Free" : string.Join(" + ", parts);
        }

        private void OnHealClicked(OwnedUnit unit)
        {
            if (_controller.Hospital == null) return;
            if (_controller.Hospital.TryHeal(unit, out var reason))
            {
                _healFeedback = reason;
                _controller.SaveGame();
            }
            else
            {
                _healFeedback = reason;
            }
            _controller.NotifyChanged();
            Refresh();
        }

        private void OnHealAllClicked()
        {
            if (_controller.Hospital == null) return;
            int healed = _controller.Hospital.HealAllAffordable(out var summary);
            _healFeedback = summary;
            if (healed > 0) _controller.SaveGame();
            _controller.NotifyChanged();
            Refresh();
        }

        private void OnUpgradeClicked()
        {
            if (_current == null) return;
            if (_controller.Progression.TryUpgrade(_current, out var reason))
            {
                _controller.SaveGame();
            }
            _statusText.text = reason;
            _controller.NotifyChanged();
            Refresh();
        }
    }
}
