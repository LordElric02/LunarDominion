using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Units;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Army screen (Phase 3). Display only: it asks
    /// RecruitmentService for status/cost/reason and shows the answer.
    /// Class tabs keep the list to three units at a time, so it fits
    /// a portrait phone without scrolling. Units are grouped for
    /// display by type, but the army itself stores individual units,
    /// so Phase 4 XP can differ per unit.</summary>
    public class ArmyView : MonoBehaviour
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
        private Text _summaryText;
        private Text _footerText;
        private Button[] _classButtons;
        private UnitClass[] _classes = { UnitClass.Infantry, UnitClass.Ranged, UnitClass.Mechanized };
        private UnitClass _selected = UnitClass.Infantry;
        private List<Row> _rows = new List<Row>();
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            _root = UIHelpers.Panel(canvas.gameObject, "ArmyPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 310), new Vector2(-20, 1200));
            UIHelpers.LabelInRect(_root, "Title", "ARMY", 36, Color.white,
                new Rect(0.04f, 0.90f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _summaryText = UIHelpers.LabelInRect(_root, "Summary", "", 26, Color.white,
                new Rect(0.04f, 0.83f, 0.96f, 0.90f), TextAnchor.MiddleCenter);

            string[] labels = { "Infantry", "Ranged", "Mechanized" };
            _classButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                var cls = _classes[i];
                var btn = UIHelpers.Button(_root, labels[i] + "Tab", labels[i], 26,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * 0.333f + 0.01f, 0.74f);
                rt.anchorMax = new Vector2((i + 1) * 0.333f - 0.01f, 0.82f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => SelectClass(cls));
                _classButtons[i] = btn;
            }

            _footerText = UIHelpers.LabelInRect(_root, "Footer",
                "Instant recruitment in this prototype. New units start at Level 1, 0 XP.",
                22, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.01f, 0.96f, 0.06f), TextAnchor.MiddleCenter);

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
            int total = _controller.State.Army != null ? _controller.State.Army.Count : 0;
            int inClass = 0;
            if (_controller.State.Army != null)
            {
                foreach (var unit in _controller.State.Army)
                {
                    if (unit == null) continue;
                    var def = _controller.UnitDefinitions.Find(u => u.Id == unit.Id);
                    if (def != null && def.Class == _selected) inClass++;
                }
            }
            _summaryText.text = $"Army: {total} units  •  {_selected}: {inClass}";

            for (int i = 0; i < _classButtons.Length; i++)
            {
                var img = _classButtons[i].GetComponent<Image>();
                if (img != null) img.color = _classes[i] == _selected
                    ? UIHelpers.Hex("#2E7D32") : UIHelpers.Hex("#1A2436");
            }

            foreach (var row in _rows)
            {
                int owned = _controller.Recruitment.CountOwned(row.Def.Id);
                row.StatsText.text = $"HP {row.Def.BaseHealth}  ATK {row.Def.BaseAttack}  DEF {row.Def.BaseDefense}  •  Owned: {owned}";
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

        private void SelectClass(UnitClass cls)
        {
            _selected = cls;
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

            var units = new List<UnitDefinition>();
            foreach (var def in _controller.UnitDefinitions)
            {
                if (def != null && def.Class == _selected) units.Add(def);
            }
            units.Sort((a, b) => a.Tier.CompareTo(b.Tier));

            for (int i = 0; i < units.Count; i++)
            {
                var def = units[i];
                float top = 0.71f - i * 0.22f;
                float bottom = top - 0.20f;
                var rowGo = UIHelpers.Panel(_root, def.DisplayName + "Row", UIHelpers.Hex("#1A2436"),
                    new Vector2(0.02f, bottom), new Vector2(0.98f, top),
                    Vector2.zero, Vector2.zero);
                UIHelpers.LabelInRect(rowGo, "Name", $"{def.DisplayName} — Tier {def.Tier}", 28, Color.white,
                    new Rect(0.03f, 0.62f, 0.72f, 0.97f));
                var stats = UIHelpers.LabelInRect(rowGo, "Stats", "", 24, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.03f, 0.32f, 0.72f, 0.62f));
                var cost = UIHelpers.LabelInRect(rowGo, "Cost", "", 24, UIHelpers.Hex("#FFD166"),
                    new Rect(0.03f, 0.03f, 0.72f, 0.32f));
                var btn = UIHelpers.Button(rowGo, "RecruitBtn", "Recruit", 26,
                    UIHelpers.Hex("#2E7D32"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.74f, 0.15f);
                rt.anchorMax = new Vector2(0.97f, 0.85f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                var unitDef = def;
                btn.onClick.AddListener(() => OnRecruitClicked(unitDef));
                _rows.Add(new Row { Root = rowGo, Def = def, StatsText = stats, CostText = cost, RecruitButton = btn });
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
    }
}
