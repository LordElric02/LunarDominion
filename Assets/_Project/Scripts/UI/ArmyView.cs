using System;
using System.Collections.Generic;
using LunarDominion.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Army screen (2026-11-12 redesign, Patrick): build
    /// the standing armies. Up to GameState.MaxArmies armies,
    /// unlocked by Nexus level (gates in GameState.ArmyNexusGates);
    /// each army is a hero pick plus assigned troops, capped at 8
    /// including the hero. A unit or hero serves in only one army.
    /// Rosters are the actual OwnedUnit instances in
    /// GameState.ArmyRosters, saved by serial. While an army is
    /// out (marching / fighting / farming) its roster is locked —
    /// the Bootstrap supplies IsArmyOut for that. Injured troops
    /// stay assigned but sit out until healed.</summary>
    public class ArmyView : MonoBehaviour
    {
        public Func<int, bool> IsArmyOut;

        private GameObject _root;
        private GameObject _rowsContent;
        private Text _summaryText;
        private readonly Button[] _armyTabs = new Button[GameState.MaxArmies];
        private readonly Text[] _armyTabLabels = new Text[GameState.MaxArmies];
        private int _selectedArmy;
        private GameController _controller;
        private readonly Dictionary<UnitId, UnitDefinition> _defs = new Dictionary<UnitId, UnitDefinition>();

        private GameState State => _controller != null ? _controller.State : null;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            if (controller != null && controller.UnitDefinitions != null)
            {
                foreach (var def in controller.UnitDefinitions)
                {
                    if (def != null && !_defs.ContainsKey(def.Id)) _defs[def.Id] = def;
                }
            }
            var safe = SafeArea.Get(canvas);
            _root = UIHelpers.Panel(canvas.gameObject, "ArmyPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 200 + safe.Bottom), new Vector2(-20, 920 - safe.Top));
            UIHelpers.LabelInRect(_root, "Title", "ARMIES", 36, Color.white,
                new Rect(0.04f, 0.90f, 0.96f, 0.98f), TextAnchor.MiddleCenter);

            for (int i = 0; i < GameState.MaxArmies; i++)
            {
                int army = i;
                var btn = UIHelpers.Button(_root, "ArmyTab" + i, "Army " + (i + 1), 26,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                float x0 = 0.04f + i * 0.313f;
                rt.anchorMin = new Vector2(x0, 0.845f);
                rt.anchorMax = new Vector2(x0 + 0.293f, 0.895f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => { _selectedArmy = army; Refresh(); });
                _armyTabs[i] = btn;
                _armyTabLabels[i] = btn.GetComponentInChildren<Text>();
            }

            _summaryText = UIHelpers.LabelInRect(_root, "Summary", "", 25, Color.white,
                new Rect(0.04f, 0.775f, 0.96f, 0.835f), TextAnchor.MiddleCenter);
            _summaryText.verticalOverflow = VerticalWrapMode.Overflow;

            UIHelpers.LabelInRect(_root, "Footer",
                "Tap a node or camp on the Map and choose which army to send. A unit can serve in only one army.",
                22, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.01f, 0.96f, 0.06f), TextAnchor.MiddleCenter);

            BuildRowsScroll();
            _root.SetActive(false);
        }

        private void BuildRowsScroll()
        {
            var scrollGo = new GameObject("RowsScroll");
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.075f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.765f);
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

        // ---- roster queries ----

        private int ArmyOfUnit(OwnedUnit unit)
        {
            var state = State;
            if (state == null || unit == null) return -1;
            for (int army = 0; army < GameState.MaxArmies; army++)
            {
                var roster = state.GetRoster(army);
                if (roster != null && roster.Contains(unit)) return army;
            }
            return -1;
        }

        private bool IsHero(OwnedUnit unit)
        {
            return unit != null && (unit.Id == UnitId.HeroReyes || unit.Id == UnitId.HeroVega || unit.Id == UnitId.HeroOkafor);
        }

        private OwnedUnit RosterHero(int army)
        {
            var roster = State != null ? State.GetRoster(army) : null;
            if (roster == null) return null;
            foreach (var unit in roster) if (IsHero(unit)) return unit;
            return null;
        }

        private bool ArmyOut(int army)
        {
            return IsArmyOut != null && IsArmyOut(army);
        }

        private bool Editable(int army)
        {
            var state = State;
            return state != null && state.ArmyUnlocked(army) && !ArmyOut(army);
        }

        private List<OwnedUnit> FreeUnitsOfType(UnitId id)
        {
            var result = new List<OwnedUnit>();
            var state = State;
            if (state == null || state.Army == null) return result;
            foreach (var unit in state.Army)
            {
                if (unit != null && unit.Id == id && ArmyOfUnit(unit) < 0) result.Add(unit);
            }
            // Strongest first: assigning takes your best spare.
            result.Sort((a, b) => a.Level != b.Level ? b.Level - a.Level : b.XP - a.XP);
            return result;
        }

        private List<OwnedUnit> AssignedOfType(int army, UnitId id)
        {
            var result = new List<OwnedUnit>();
            var roster = State != null ? State.GetRoster(army) : null;
            if (roster == null) return result;
            foreach (var unit in roster)
            {
                if (unit != null && unit.Id == id) result.Add(unit);
            }
            // Weakest first: unassigning returns your weakest.
            result.Sort((a, b) => a.Level != b.Level ? a.Level - b.Level : a.XP - b.XP);
            return result;
        }

        private string NameOf(UnitId id)
        {
            return _defs.TryGetValue(id, out var def) && def != null ? def.DisplayName : id.ToString();
        }

        // ---- refresh / rows ----

        public void Refresh()
        {
            if (_root == null || !_root.activeSelf || State == null) return;
            var state = State;
            int army = _selectedArmy;

            for (int i = 0; i < GameState.MaxArmies; i++)
            {
                var roster = state.GetRoster(i);
                int count = roster != null ? roster.Count : 0;
                _armyTabLabels[i].text = state.ArmyUnlocked(i)
                    ? $"Army {i + 1} ({count}/8)"
                    : $"Army {i + 1} (Nexus {GameState.ArmyNexusGates[i]})";
                var img = _armyTabs[i].GetComponent<Image>();
                if (img != null) img.color = i == army ? UIHelpers.Hex("#2E5E8C") : UIHelpers.Hex("#1A2436");
            }

            var myRoster = state.GetRoster(army);
            int myCount = myRoster != null ? myRoster.Count : 0;
            if (!state.ArmyUnlocked(army))
            {
                _summaryText.text = $"Army {army + 1} unlocks at Nexus {GameState.ArmyNexusGates[army]} (you are Nexus {state.NexusLevel}).";
            }
            else if (ArmyOut(army))
            {
                _summaryText.text = $"Army {army + 1} — {myCount}/8 — is out. Its roster is locked until it returns.";
            }
            else
            {
                _summaryText.text = $"Army {army + 1} — {myCount}/8 assigned. Tap + to assign, − to return a unit to the pool.";
            }

            RebuildRows();
        }

        private void RebuildRows()
        {
            for (int i = _rowsContent.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(_rowsContent.transform.GetChild(i).gameObject);
            }
            var state = State;
            int army = _selectedArmy;
            bool unlocked = state.ArmyUnlocked(army);
            bool editable = Editable(army);

            AddHeaderRow("HERO — one per army");
            if (state.Heroes == null || state.Heroes.Count == 0)
            {
                AddTextRow("No heroes recruited yet. Recruit heroes at the Marine Foundry, then assign one here.");
            }
            else
            {
                foreach (var hero in state.Heroes)
                {
                    if (hero != null) AddHeroRow(hero, army, editable);
                }
            }

            AddHeaderRow("TROOPS");
            bool anyTroopRow = false;
            foreach (var def in _defs.Values)
            {
                if (def == null || def.IsHero) continue;
                var assigned = AssignedOfType(army, def.Id);
                var free = FreeUnitsOfType(def.Id);
                if (assigned.Count == 0 && free.Count == 0) continue;
                anyTroopRow = true;
                AddTroopRow(def, army, assigned, free, editable && unlocked);
            }
            if (!anyTroopRow)
            {
                AddTextRow("No troops available. Recruit units at the barracks buildings, then assign them here.");
            }
        }

        private GameObject MakeRow(float height)
        {
            var row = UIHelpers.Panel(_rowsContent, "Row", UIHelpers.Hex("#1A2436"),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var layout = row.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            return row;
        }

        private void AddHeaderRow(string text)
        {
            var row = MakeRow(52);
            var label = UIHelpers.LabelInRect(row, "Header", text, 24, UIHelpers.Hex("#90CAF9"),
                new Rect(0.02f, 0.1f, 0.98f, 0.9f), TextAnchor.MiddleLeft);
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void AddTextRow(string text)
        {
            var row = MakeRow(64);
            var label = UIHelpers.LabelInRect(row, "Text", text, 23, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.02f, 0.05f, 0.98f, 0.95f), TextAnchor.MiddleLeft);
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void AddHeroRow(OwnedUnit hero, int army, bool editable)
        {
            var row = MakeRow(92);
            int home = ArmyOfUnit(hero);
            string detail = $"{NameOf(hero.Id)} — Lv {hero.Level}";
            var label = UIHelpers.LabelInRect(row, "Name", detail, 26, Color.white,
                new Rect(0.02f, 0.08f, 0.64f, 0.92f), TextAnchor.MiddleLeft);
            label.verticalOverflow = VerticalWrapMode.Overflow;

            if (home == army)
            {
                var btn = AddRowButton(row, "Remove", 0.68f, 0.97f);
                UIHelpers.SetButtonEnabled(btn, editable);
                if (editable) btn.onClick.AddListener(() => { State.GetRoster(army).Remove(hero); AfterEdit(); });
            }
            else if (home >= 0)
            {
                var tag = UIHelpers.LabelInRect(row, "Tag", $"Army {home + 1}", 24, UIHelpers.Hex("#90CAF9"),
                    new Rect(0.68f, 0.08f, 0.97f, 0.92f), TextAnchor.MiddleCenter);
                tag.verticalOverflow = VerticalWrapMode.Overflow;
            }
            else
            {
                var btn = AddRowButton(row, "Assign", 0.68f, 0.97f);
                bool canAssign = editable && RosterHero(army) == null && State.GetRoster(army).Count < 8;
                UIHelpers.SetButtonEnabled(btn, canAssign);
                if (canAssign) btn.onClick.AddListener(() => { State.GetRoster(army).Add(hero); AfterEdit(); });
            }
        }

        private void AddTroopRow(UnitDefinition def, int army, List<OwnedUnit> assigned, List<OwnedUnit> free, bool editable)
        {
            var row = MakeRow(92);
            int injured = 0;
            var levels = new List<string>();
            foreach (var unit in assigned)
            {
                levels.Add(unit.Level.ToString());
                if (State.Injured != null && State.Injured.Contains(unit)) injured++;
            }
            string line1 = $"{def.DisplayName} — {assigned.Count} in army · {free.Count} free";
            string line2 = assigned.Count > 0 ? "Levels: " + string.Join(", ", levels) : "Not assigned yet";
            if (injured > 0) line2 += $" · {injured} injured (sits out)";
            var label = UIHelpers.LabelInRect(row, "Info", line1 + "\n" + line2, 24, Color.white,
                new Rect(0.02f, 0.06f, 0.66f, 0.94f), TextAnchor.MiddleLeft);
            label.verticalOverflow = VerticalWrapMode.Overflow;

            var minus = AddRowButton(row, "−", 0.69f, 0.815f);
            bool canRemove = editable && assigned.Count > 0;
            UIHelpers.SetButtonEnabled(minus, canRemove);
            if (canRemove) minus.onClick.AddListener(() =>
            {
                var list = AssignedOfType(army, def.Id);
                if (list.Count > 0) State.GetRoster(army).Remove(list[0]);
                AfterEdit();
            });

            var plus = AddRowButton(row, "+", 0.845f, 0.97f);
            bool canAdd = editable && free.Count > 0 && State.GetRoster(army).Count < 8;
            UIHelpers.SetButtonEnabled(plus, canAdd);
            if (canAdd) plus.onClick.AddListener(() =>
            {
                var pool = FreeUnitsOfType(def.Id);
                if (pool.Count > 0) State.GetRoster(army).Add(pool[0]);
                AfterEdit();
            });
        }

        private Button AddRowButton(GameObject row, string text, float x0, float x1)
        {
            var btn = UIHelpers.Button(row, "Btn" + text, text, 28, UIHelpers.Hex("#2E7D32"), Color.white);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(x0, 0.18f);
            rt.anchorMax = new Vector2(x1, 0.82f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return btn;
        }

        private void AfterEdit()
        {
            _controller.SaveGame();
            _controller.NotifyChanged();
            Refresh();
        }
    }
}
