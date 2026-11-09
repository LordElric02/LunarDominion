using System;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Units;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Squad picker for map sends (2026-11-09). When the player
    /// taps Send Excursion on the map, this panel appears instead of
    /// sending immediately, so they can choose which units go. Counts
    /// are picked per unit type (same pattern as the Expeditions menu
    /// squad picker), up to the squad cap. The picker opens pre-filled
    /// with the whole army (strongest first, capped) — the previous
    /// default — and the player trims or changes it. Confirm returns
    /// the chosen counts; the Bootstrap converts them to army indices
    /// (first N of each type, in army order) and sends that squad.
    /// This is also the hook multiple armies will plug into later: the
    /// picker will list armies instead of unit types.</summary>
    public class SquadPickerView : MonoBehaviour
    {
        private class SquadRow
        {
            public GameObject Root;
            public UnitId Type;
            public int Owned;
            public Text CountText;
            public Button AddButton;
            public Button RemoveButton;
        }

        // Counts chosen, by unit type. Raised on confirm.
        public event Action<Dictionary<UnitId, int>> OnConfirmClicked;
        public event Action OnCancelClicked;

        private GameObject _root;
        private GameObject _rowsContent;
        private Text _titleText;
        private Text _summaryText;
        private Button _sendButton;
        private Button _cancelButton;
        private GameController _controller;
        private string _targetTitle;
        private Dictionary<UnitId, int> _counts = new Dictionary<UnitId, int>();
        private List<SquadRow> _rows = new List<SquadRow>();

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            // Same tall panel as Army / Expeditions (bottom just above
            // the nav), so the squad rows have room.
            _root = UIHelpers.Panel(canvas.gameObject, "SquadPickerPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 200 + safe.Bottom), new Vector2(-20, 920 - safe.Top));
            _titleText = UIHelpers.LabelInRect(_root, "Title", "CHOOSE SQUAD", 36, Color.white,
                new Rect(0.04f, 0.90f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _summaryText = UIHelpers.LabelInRect(_root, "Summary", "", 26, Color.white,
                new Rect(0.04f, 0.83f, 0.96f, 0.90f), TextAnchor.MiddleCenter);
            BuildRowsScroll();

            _sendButton = UIHelpers.Button(_root, "SendButton", "Send Squad", 28,
                UIHelpers.Hex("#2E7D32"), Color.white);
            var sendRt = _sendButton.GetComponent<RectTransform>();
            sendRt.anchorMin = new Vector2(0.02f, 0.02f);
            sendRt.anchorMax = new Vector2(0.49f, 0.08f);
            sendRt.offsetMin = Vector2.zero;
            sendRt.offsetMax = Vector2.zero;
            _sendButton.onClick.AddListener(() => OnConfirmClicked?.Invoke(new Dictionary<UnitId, int>(_counts)));

            _cancelButton = UIHelpers.Button(_root, "CancelButton", "Cancel", 28,
                UIHelpers.Hex("#1A2436"), Color.white);
            var cancelRt = _cancelButton.GetComponent<RectTransform>();
            cancelRt.anchorMin = new Vector2(0.51f, 0.02f);
            cancelRt.anchorMax = new Vector2(0.98f, 0.08f);
            cancelRt.offsetMin = Vector2.zero;
            cancelRt.offsetMax = Vector2.zero;
            _cancelButton.onClick.AddListener(() => OnCancelClicked?.Invoke());

            _root.SetActive(false);
        }

        // Rows live in a vertical ScrollRect (same pattern as the Army
        // rows), so the list fits and scrolls if many types are owned.
        private void BuildRowsScroll()
        {
            var scrollGo = new GameObject("RowsScroll");
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.10f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.82f);
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

        public void Show(string targetTitle, Dictionary<UnitId, int> initialCounts)
        {
            _counts = initialCounts != null ? new Dictionary<UnitId, int>(initialCounts) : new Dictionary<UnitId, int>();
            _targetTitle = targetTitle;
            _titleText.text = "CHOOSE SQUAD";
            _root.SetActive(true);
            BuildRows();
            Refresh();
        }

        public void Hide()
        {
            _root.SetActive(false);
        }

        private void BuildRows()
        {
            foreach (var row in _rows)
            {
                if (row.Root != null) Destroy(row.Root);
            }
            _rows.Clear();
            if (_rowsContent == null || _controller == null) return;

            // Distinct owned types, in army order, with owned counts.
            var types = new List<UnitId>();
            var ownedCounts = new Dictionary<UnitId, int>();
            if (_controller.State.Army != null)
            {
                foreach (var unit in _controller.State.Army)
                {
                    if (unit == null) continue;
                    if (!ownedCounts.ContainsKey(unit.Id))
                    {
                        ownedCounts[unit.Id] = 0;
                        types.Add(unit.Id);
                    }
                    ownedCounts[unit.Id]++;
                }
            }
            // Drop picks for types no longer owned (e.g. casualties),
            // and clamp picks to what is owned.
            var keys = new List<UnitId>(_counts.Keys);
            foreach (var key in keys)
            {
                if (!ownedCounts.ContainsKey(key)) _counts.Remove(key);
                else if (_counts[key] > ownedCounts[key]) _counts[key] = ownedCounts[key];
            }

            if (types.Count == 0)
            {
                var emptyGo = new GameObject("EmptyRow");
                emptyGo.transform.SetParent(_rowsContent.transform, false);
                var emptyImg = emptyGo.AddComponent<Image>();
                emptyImg.color = UIHelpers.Hex("#1A2436");
                var layout = emptyGo.AddComponent<LayoutElement>();
                layout.preferredHeight = 100;
                UIHelpers.LabelInRect(emptyGo, "Empty", "No units yet — recruit in Army first.", 24, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.03f, 0.05f, 0.97f, 0.95f), TextAnchor.MiddleCenter);
                return;
            }

            foreach (var type in types)
            {
                var def = _controller.UnitDefinitions.Find(u => u.Id == type);
                string name = def != null ? def.DisplayName : type.ToString();
                var rowGo = new GameObject(name + "Row");
                rowGo.transform.SetParent(_rowsContent.transform, false);
                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = UIHelpers.Hex("#1A2436");
                var layout = rowGo.AddComponent<LayoutElement>();
                layout.preferredHeight = 100;
                var rowRt = rowGo.GetComponent<RectTransform>();
                rowRt.anchorMin = Vector2.zero;
                rowRt.anchorMax = Vector2.one;
                UIHelpers.LabelInRect(rowGo, "Name", name, 28, Color.white,
                    new Rect(0.03f, 0.50f, 0.60f, 0.97f));
                var countText = UIHelpers.LabelInRect(rowGo, "Count", "", 24, UIHelpers.Hex("#C0C8D0"),
                    new Rect(0.03f, 0.05f, 0.60f, 0.50f));
                var addBtn = UIHelpers.Button(rowGo, "AddBtn", "+", 30, UIHelpers.Hex("#2E7D32"), Color.white);
                var addRt = addBtn.GetComponent<RectTransform>();
                addRt.anchorMin = new Vector2(0.62f, 0.15f);
                addRt.anchorMax = new Vector2(0.76f, 0.85f);
                addRt.offsetMin = Vector2.zero;
                addRt.offsetMax = Vector2.zero;
                var removeBtn = UIHelpers.Button(rowGo, "RemoveBtn", "–", 30, UIHelpers.Hex("#7A2E2E"), Color.white);
                var removeRt = removeBtn.GetComponent<RectTransform>();
                removeRt.anchorMin = new Vector2(0.78f, 0.15f);
                removeRt.anchorMax = new Vector2(0.97f, 0.85f);
                removeRt.offsetMin = Vector2.zero;
                removeRt.offsetMax = Vector2.zero;
                var rowType = type;
                addBtn.onClick.AddListener(() => OnAdd(rowType));
                removeBtn.onClick.AddListener(() => OnRemove(rowType));
                _rows.Add(new SquadRow
                {
                    Root = rowGo,
                    Type = type,
                    Owned = ownedCounts[type],
                    CountText = countText,
                    AddButton = addBtn,
                    RemoveButton = removeBtn
                });
            }
        }

        private void OnAdd(UnitId type)
        {
            if (Total() >= _controller.Balance.MaxSquadSize) return;
            if (!_counts.ContainsKey(type)) _counts[type] = 0;
            // Clamp to owned.
            int owned = 0;
            foreach (var row in _rows) if (row.Type == type) owned = row.Owned;
            if (_counts[type] >= owned) return;
            _counts[type]++;
            Refresh();
        }

        private void OnRemove(UnitId type)
        {
            if (!_counts.ContainsKey(type) || _counts[type] <= 0) return;
            _counts[type]--;
            Refresh();
        }

        private int Total()
        {
            int total = 0;
            foreach (var count in _counts.Values) total += count;
            return total;
        }

        private void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            string target = string.IsNullOrEmpty(_targetTitle) ? "" : $"Send to: {_targetTitle}  •  ";
            _summaryText.text = $"{target}Squad: {Total()} / {_controller.Balance.MaxSquadSize}";
            foreach (var row in _rows)
            {
                int selected = _counts.ContainsKey(row.Type) ? _counts[row.Type] : 0;
                row.CountText.text = $"Squad: {selected} / {row.Owned}";
                UIHelpers.SetButtonEnabled(row.AddButton, Total() < _controller.Balance.MaxSquadSize && selected < row.Owned);
                UIHelpers.SetButtonEnabled(row.RemoveButton, selected > 0);
            }
            UIHelpers.SetButtonEnabled(_sendButton, Total() > 0);
        }
    }
}
