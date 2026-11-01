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
    /// the list to four nodes at most, so it fits a portrait phone.
    /// The active timer counts down once a second via OnStateChanged,
    /// which GameController fires on its production tick.</summary>
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
            _root = UIHelpers.Panel(canvas.gameObject, "ResearchPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 310 + safe.Bottom), new Vector2(-20, 1200 + safe.Bottom));
            UIHelpers.LabelInRect(_root, "Title", "RESEARCH", 36, Color.white,
                new Rect(0.04f, 0.92f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _labText = UIHelpers.LabelInRect(_root, "LabStatus", "", 24, Color.white,
                new Rect(0.04f, 0.86f, 0.96f, 0.92f), TextAnchor.MiddleCenter);
            _activeText = UIHelpers.LabelInRect(_root, "Active", "", 24, UIHelpers.Hex("#FFD166"),
                new Rect(0.04f, 0.80f, 0.96f, 0.86f), TextAnchor.MiddleCenter);

            string[] labels = { "Base", "Combat", "Expeditions" };
            _branchButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                var branch = _branches[i];
                var btn = UIHelpers.Button(_root, labels[i] + "Tab", labels[i], 26,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * 0.333f + 0.01f, 0.71f);
                rt.anchorMax = new Vector2((i + 1) * 0.333f - 0.01f, 0.79f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => SelectBranch(branch));
                _branchButtons[i] = btn;
            }

            BuildRows();
            _root.SetActive(false);
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
            if (_root == null) return;

            var defs = new List<ResearchDefinition>();
            foreach (var def in _controller.ResearchDefinitions)
            {
                if (def != null && def.Branch == _selected) defs.Add(def);
            }
            defs.Sort((a, b) => ((int)a.Id).CompareTo((int)b.Id));

            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                float top = 0.68f - i * 0.16f;
                float bottom = top - 0.15f;
                var rowGo = UIHelpers.Panel(_root, def.DisplayName + "Row", UIHelpers.Hex("#1A2436"),
                    new Vector2(0.02f, bottom), new Vector2(0.98f, top),
                    Vector2.zero, Vector2.zero);
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
