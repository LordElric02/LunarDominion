using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Progression;
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
        private string _healFeedback = "";

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
            _root.SetActive(false);
        }

        // Hospital injured list: a title + a vertical ScrollRect (same
        // pattern as ArmyView's rows), occupying the info/status area
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
            _levelText.text = locked ? "Locked" : $"Level {level}  →  Level {level + 1}";
            _infoText.text = _current.Description;

            var status = _controller.Progression.CheckUpgrade(_current, out var cost, out var reason);
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
        }

        // Hospital only: show the injured list when injured units are
        // waiting, and hide the info/status texts while it is shown (the
        // list occupies their area). Otherwise the list stays hidden and
        // the normal info/status show.
        private void RefreshInjuredSection()
        {
            bool isHospital = _current != null && _current.Id == BuildingId.Hospital;
            bool hasInjured = isHospital && _controller.Hospital != null && _controller.Hospital.HasInjured;
            _injuredRoot.SetActive(hasInjured);
            _infoText.gameObject.SetActive(!hasInjured);
            _statusText.gameObject.SetActive(!hasInjured);
            if (!hasInjured)
            {
                foreach (var row in _injuredRows) row.Root.SetActive(false);
                return;
            }

            _injuredTitle.text = string.IsNullOrEmpty(_healFeedback)
                ? $"INJURED ({_controller.State.Injured.Count}) — tap Heal to restore to the army"
                : _healFeedback;

            // Rebuild the rows to match the current injured list. The
            // list is small (squad cap 8), so a full rebuild is cheap.
            foreach (var row in _injuredRows) Object.Destroy(row.Root);
            _injuredRows.Clear();
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
