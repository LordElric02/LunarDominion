using System;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Research;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Research screen. Display only: it asks ResearchService
    /// for status/cost/reason and shows the answer. Branch tabs keep
    /// the list to four nodes at most. In landscape the rows live in a
    /// vertical ScrollRect inside the shorter panel, so the list fits
    /// and scrolls. The active timer counts down once a second via
    /// OnStateChanged, which GameController fires on its production tick.</summary>
    public class ResearchView : MonoBehaviour
    {
        private class Row
        {
            public GameObject Root;
            public ResearchDefinition Def;
            public Text EffectText;
            public Text CostText;
            public Button ResearchButton;
        }

        private GameObject _root;
        private GameObject _rowsContent;
        private Text _labText;
        private Text _activeText;
        private Button[] _branchButtons;
        private ResearchBranch[] _branches = { ResearchBranch.Base, ResearchBranch.Combat, ResearchBranch.Expeditions };
        private ResearchBranch _selected = ResearchBranch.Base;
        private List<Row> _rows = new List<Row>();
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            // 2026-11-06 landscape: shorter panel — top sits just below
            // the top bar (920 ref px above the bottom, less the top safe
            // inset), bottom stays above the quick-select row. Height is
            // 610 minus the safe insets in the Editor, so the rows scroll.
            _root = UIHelpers.Panel(canvas.gameObject, "ResearchPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 310 + safe.Bottom), new Vector2(-20, 920 - safe.Top));
            UIHelpers.LabelInRect(_root, "Title", "RESEARCH", 36, Color.white,
                new Rect(0.04f, 0.90f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _labText = UIHelpers.LabelInRect(_root, "LabStatus", "", 24, Color.white,
                new Rect(0.04f, 0.84f, 0.96f, 0.90f), TextAnchor.MiddleCenter);
            _activeText = UIHelpers.LabelInRect(_root, "Active", "", 24, UIHelpers.Hex("#FFD166"),
                new Rect(0.04f, 0.78f, 0.96f, 0.84f), TextAnchor.MiddleCenter);

            string[] labels = { "Base", "Combat", "Expeditions" };
            _branchButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                var branch = _branches[i];
                var btn = UIHelpers.Button(_root, labels[i] + "Tab", labels[i], 26,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * 0.333f + 0.01f, 0.69f);
                rt.anchorMax = new Vector2((i + 1) * 0.333f - 0.01f, 0.77f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => SelectBranch(branch));
                _branchButtons[i] = btn;
            }

            BuildRowsScroll();
            BuildRows();
            _root.SetActive(false);
        }

        // 2026-11-06 landscape: the node rows live in a vertical
        // ScrollRect (same pattern as ExpeditionsView's squad list), so
        // the list fits the shorter panel and scrolls. Rows are
        // fixed-height LayoutElements.
        private void BuildRowsScroll()
        {
            var scrollGo = new GameObject("RowsScroll");
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.02f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.67f);
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
            int labLevel = _controller.State.GetLevel(BuildingId.ResearchLab);
            _labText.text = labLevel == 0
                ? "Research Lab not built — build it on the Base tab first."
                : $"Research Lab level {labLevel}  •  Nexus {_controller.State.NexusLevel}";

            if (_controller.Research != null && _controller.Research.IsResearching)
            {
                var active = _controller.Research.ActiveDefinition;
                string name = active != null ? active.DisplayName : "Research";
                _activeText.text = $"Researching: {name} level {_controller.Research.ActiveTargetLevel} — {_controller.Research.RemainingText(DateTime.UtcNow)} left";
            }
            else
            {
                _activeText.text = "One research at a time. Cost is paid at start; the level applies when the timer finishes.";
            }

            for (int i = 0; i < _branchButtons.Length; i++)
            {
                var img = _branchButtons[i].GetComponent<Image>();
                if (img != null) img.color = _branches[i] == _selected
                    ? UIHelpers.Hex("#2E7D32") : UIHelpers.Hex("#1A2436");
            }

            foreach (var row in _rows)
            {
                int level = _controller.Research.GetLevel(row.Def.Id);
                row.EffectText.text = $"{row.Def.DisplayName} — Lv {level}/5\n{row.Def.Description}";
                var status = _controller.Research.CheckResearch(row.Def, out var cost, out var reason);
                string costText = CostText(cost);
                if (status == ResearchStatus.CanResearch)
                {
                    int minutes = ResearchService.TimerMinutesForLevel(level + 1);
                    row.CostText.text = $"Cost: {costText}  •  {minutes} min";
                }
                else
                {
                    row.CostText.text = $"{reason}  Cost: {costText}";
                }
                UIHelpers.SetButtonEnabled(row.ResearchButton, status == ResearchStatus.CanResearch);
                var btnText = row.ResearchButton.GetComponentInChildren<Text>();
                if (btnText != null) btnText.text = level >= ResearchCatalog.MaxResearchLevel ? "Max" : "Research";
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

        private void SelectBranch(ResearchBranch branch)
        {
            _selected = branch;
            BuildRows();
            Refresh();
        }

        private void BuildRows()
        {
            foreach (var row in _rows)
            {
                if (row.Root != null) Destroy(row.Root);
            }
            _rows.Clear();
            if (_root == null || _rowsContent == null) return;

            var defs = new List<ResearchDefinition>();
            foreach (var def in _controller.ResearchDefinitions)
            {
                if (def != null && def.Branch == _selected) defs.Add(def);
            }
            defs.Sort((a, b) => ((int)a.Id).CompareTo((int)b.Id));

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                var rowGo = new GameObject(def.DisplayName + "Row");
                rowGo.transform.SetParent(_rowsContent.transform, false);
                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = UIHelpers.Hex("#1A2436");
                var layout = rowGo.AddComponent<LayoutElement>();
                layout.preferredHeight = 110;
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.anchorMin = Vector2.zero;
                rowRt.anchorMax = Vector2.one;
                var effect = UIHelpers.LabelInRect(rowGo, "Effect", "", 24, Color.white,
                    new Rect(0.03f, 0.35f, 0.72f, 0.97f));
                var cost = UIHelpers.LabelInRect(rowGo, "Cost", "", 22, UIHelpers.Hex("#FFD166"),
                    new Rect(0.03f, 0.03f, 0.72f, 0.35f));
                var btn = UIHelpers.Button(rowGo, "ResearchBtn", "Research", 24,
                    UIHelpers.Hex("#2E7D32"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.74f, 0.15f);
                rt.anchorMax = new Vector2(0.97f, 0.85f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var nodeDef = def;
                btn.onClick.AddListener(() => OnResearchClicked(nodeDef));
                _rows.Add(new Row { Root = rowGo, Def = def, EffectText = effect, CostText = cost, ResearchButton = btn });
            }
        }

        private void OnResearchClicked(ResearchDefinition def)
        {
            if (_controller.Research.StartResearch(def, out _))
            {
                _controller.SaveGame();
            }
            _controller.NotifyChanged();
            Refresh();
        }
    }
}
