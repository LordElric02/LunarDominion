using System.Collections.Generic;
using System.Text;
using LunarDominion.Core;
using LunarDominion.Expeditions;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Expeditions screen (Phase 4). Display only: it asks
    /// ExpeditionService for status/reason and shows the answer, then
    /// shows the battle result that service returns.
    /// Squad picking is by unit type, in army order: the picker stores
    /// how many of each type to take (up to 8 total), and the service
    /// takes the first that many of each type from the army, so XP
    /// and casualties still land on individual units. Squad order
    /// does not change a GDD §6 battle (player units all attack
    /// before enemies, targeting the lowest-HP enemy), so picking by
    /// type is equivalent to picking individuals here.</summary>
    public class ExpeditionsView : MonoBehaviour
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

        private GameObject _root;
        private GameObject _resultRoot;
        private Text _summaryText;
        private Text _detailText;
        private Text _statusText;
        private Text _resultTitleText;
        private Text _resultBodyText;
        private Button[] _tierButtons;
        private Button _startButton;
        private GameObject _squadContent;
        private List<SquadRow> _squadRows = new List<SquadRow>();
        private Dictionary<UnitId, int> _squadCounts = new Dictionary<UnitId, int>();
        private EncounterDefinition _selected;
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            _root = UIHelpers.Panel(canvas.gameObject, "ExpeditionsPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 310), new Vector2(-20, 1200));
            UIHelpers.LabelInRect(_root, "Title", "EXPEDITIONS", 36, Color.white,
                new Rect(0.04f, 0.90f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _summaryText = UIHelpers.LabelInRect(_root, "Summary", "", 26, Color.white,
                new Rect(0.04f, 0.83f, 0.96f, 0.90f), TextAnchor.MiddleCenter);

            _tierButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int tierIndex = i;
                var btn = UIHelpers.Button(_root, "Tier" + (i + 1) + "Tab", "Tier " + (i + 1), 26,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * 0.333f + 0.01f, 0.74f);
                rt.anchorMax = new Vector2((i + 1) * 0.333f - 0.01f, 0.82f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => SelectTier(tierIndex));
                _tierButtons[i] = btn;
            }

            _detailText = UIHelpers.LabelInRect(_root, "Detail", "", 24, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.63f, 0.96f, 0.74f), TextAnchor.MiddleLeft);
            UIHelpers.LabelInRect(_root, "SquadHeader", "Squad — tap + to add, – to remove (max 8):", 24, Color.white,
                new Rect(0.04f, 0.59f, 0.96f, 0.63f));

            BuildSquadScroll();

            _startButton = UIHelpers.Button(_root, "StartBtn", "Start Expedition", 28,
                UIHelpers.Hex("#2E7D32"), Color.white);
            var startRt = _startButton.GetComponent<RectTransform>();
            startRt.anchorMin = new Vector2(0.02f, 0.07f);
            startRt.anchorMax = new Vector2(0.98f, 0.16f);
            startRt.offsetMin = Vector2.zero;
            startRt.offsetMax = Vector2.zero;
            _startButton.onClick.AddListener(OnStartClicked);

            _statusText = UIHelpers.LabelInRect(_root, "Status", "", 24, UIHelpers.Hex("#FFD166"),
                new Rect(0.04f, 0.01f, 0.96f, 0.06f), TextAnchor.MiddleCenter);

            BuildResultPanel(canvas);
            if (_controller.EncounterDefinitions != null && _controller.EncounterDefinitions.Count > 0)
                _selected = _controller.EncounterDefinitions[0];
            BuildSquadRows();
            _root.SetActive(false);
        }

        public void Show()
        {
            _resultRoot.SetActive(false);
            _root.SetActive(true);
            BuildSquadRows();
            Refresh();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _resultRoot.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (visible) Show(); else Hide();
        }

        public void Refresh()
        {
            if (_root == null || !_root.activeSelf) return;
            int squadTotal = SquadTotal();
            _summaryText.text = $"Energy: {_controller.State.Energy} / {_controller.Balance.MaxEnergy}  •  Squad: {squadTotal} / {_controller.Balance.MaxSquadSize}";
            for (int i = 0; i < _tierButtons.Length; i++)
            {
                if (i >= _controller.EncounterDefinitions.Count) continue;
                var img = _tierButtons[i].GetComponent<Image>();
                if (img != null)
                {
                    var encounter = _controller.EncounterDefinitions[i];
                    img.color = encounter == _selected ? UIHelpers.Hex("#2E7D32") : UIHelpers.Hex("#1A2436");
                }
            }
            if (_selected != null)
            {
                var parts = new List<string>();
                foreach (var spawn in _selected.Spawns)
                {
                    parts.Add($"{spawn.Count} × {spawn.DisplayName} ({spawn.Class})");
                }
                _detailText.text = $"{_selected.DisplayName}\n{string.Join(", ", parts)}\nEnergy: {_selected.EnergyCost}  •  Reward: {CostText(_selected.Reward)}  •  XP pool: {_selected.XpPool}";
                var status = _controller.Expeditions.CheckExpedition(_selected, squadTotal, out var reason);
                _statusText.text = reason;
                UIHelpers.SetButtonEnabled(_startButton, status == ExpeditionStatus.CanStart);
            }
            foreach (var row in _squadRows)
            {
                int selected = _squadCounts.ContainsKey(row.Type) ? _squadCounts[row.Type] : 0;
                row.CountText.text = $"Squad: {selected} / {row.Owned}";
                UIHelpers.SetButtonEnabled(row.AddButton, squadTotal < _controller.Balance.MaxSquadSize && selected < row.Owned);
                UIHelpers.SetButtonEnabled(row.RemoveButton, selected > 0);
            }
        }

        private int SquadTotal()
        {
            int total = 0;
            foreach (var count in _squadCounts.Values) total += count;
            return total;
        }

        private static string CostText(ResourceCost cost)
        {
            var parts = new List<string>();
            if (cost.Biomass > 0) parts.Add($"{cost.Biomass} Biomass");
            if (cost.Alloy > 0) parts.Add($"{cost.Alloy} Alloy");
            if (cost.Helium3 > 0) parts.Add($"{cost.Helium3} Helium-3");
            return parts.Count > 0 ? string.Join(", ", parts) : "None";
        }

        private void SelectTier(int index)
        {
            if (index >= 0 && index < _controller.EncounterDefinitions.Count)
                _selected = _controller.EncounterDefinitions[index];
            Refresh();
        }

        private void BuildSquadScroll()
        {
            var scrollGo = new GameObject("SquadScroll");
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.18f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.58f);
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

            _squadContent = new GameObject("Content");
            _squadContent.transform.SetParent(viewportGo.transform, false);
            var contentRt = _squadContent.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            var layout = _squadContent.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _squadContent.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.content = contentRt;
        }

        private void BuildSquadRows()
        {
            foreach (var row in _squadRows)
            {
                if (row.Root != null) Destroy(row.Root);
            }
            _squadRows.Clear();
            if (_squadContent == null) return;

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
            // Drop squad picks for types no longer owned (e.g. casualties).
            var keys = new List<UnitId>(_squadCounts.Keys);
            foreach (var key in keys)
            {
                if (!ownedCounts.ContainsKey(key)) _squadCounts.Remove(key);
                else if (_squadCounts[key] > ownedCounts[key]) _squadCounts[key] = ownedCounts[key];
            }

            if (types.Count == 0)
            {
                var emptyGo = new GameObject("EmptyRow");
                emptyGo.transform.SetParent(_squadContent.transform, false);
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
                rowGo.transform.SetParent(_squadContent.transform, false);
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
                _squadRows.Add(new SquadRow
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
            if (SquadTotal() >= _controller.Balance.MaxSquadSize) return;
            if (!_squadCounts.ContainsKey(type)) _squadCounts[type] = 0;
            _squadCounts[type]++;
            Refresh();
        }

        private void OnRemove(UnitId type)
        {
            if (!_squadCounts.ContainsKey(type) || _squadCounts[type] <= 0) return;
            _squadCounts[type]--;
            Refresh();
        }

        private void OnStartClicked()
        {
            if (_selected == null) return;
            // Take the first N of each type from the army, in army order.
            var remaining = new Dictionary<UnitId, int>(_squadCounts);
            var squadIndices = new List<int>();
            for (int i = 0; i < _controller.State.Army.Count; i++)
            {
                var unit = _controller.State.Army[i];
                if (unit == null) continue;
                if (remaining.ContainsKey(unit.Id) && remaining[unit.Id] > 0)
                {
                    remaining[unit.Id]--;
                    squadIndices.Add(i);
                }
            }
            if (_controller.Expeditions.TryStart(_selected, squadIndices, out var result, out var reason))
            {
                _controller.SaveGame();
                _squadCounts.Clear();
                _controller.NotifyChanged();
                ShowResult(result);
            }
            else
            {
                _statusText.text = reason;
                _controller.NotifyChanged();
            }
        }

        private void BuildResultPanel(Canvas canvas)
        {
            _resultRoot = UIHelpers.Panel(canvas.gameObject, "ExpeditionResultPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 310), new Vector2(-20, 1200));
            _resultTitleText = UIHelpers.LabelInRect(_resultRoot, "ResultTitle", "", 36, Color.white,
                new Rect(0.04f, 0.88f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _resultBodyText = UIHelpers.LabelInRect(_resultRoot, "ResultBody", "", 24, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.18f, 0.96f, 0.88f), TextAnchor.UpperLeft);
            var backBtn = UIHelpers.Button(_resultRoot, "BackBtn", "Back to Expeditions", 28,
                UIHelpers.Hex("#1A2436"), Color.white);
            var backRt = backBtn.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0.02f, 0.07f);
            backRt.anchorMax = new Vector2(0.98f, 0.16f);
            backRt.offsetMin = Vector2.zero;
            backRt.offsetMax = Vector2.zero;
            backBtn.onClick.AddListener(() => Show());
            _resultRoot.SetActive(false);
        }

        private void ShowResult(BattleResult result)
        {
            _root.SetActive(false);
            _resultTitleText.text = result.Victory ? "VICTORY!" : (result.Timeout ? "DEFEAT — TIMEOUT" : "DEFEAT");
            var body = new StringBuilder();
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
                : "Casualties: " + string.Join(", ", result.CasualtyNames));
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
            _resultBodyText.text = body.ToString();
            _resultRoot.SetActive(true);
        }
    }
}
