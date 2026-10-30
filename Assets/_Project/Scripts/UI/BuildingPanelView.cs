using LunarDominion.Core;
using LunarDominion.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Building selection panel. Display only: it asks
    /// ProgressionService for status/cost/reason and shows the answer.
    /// The Upgrade button is disabled with a reason whenever it cannot be used.</summary>
    public class BuildingPanelView : MonoBehaviour
    {
        private GameObject _root;
        private Text _nameText;
        private Text _levelText;
        private Text _infoText;
        private Text _statusText;
        private Button _upgradeButton;
        private Text _upgradeLabel;
        private GameController _controller;
        private BuildingDefinition _current;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            _root = UIHelpers.Panel(canvas.gameObject, "BuildingPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 310 + safe.Bottom), new Vector2(-20, 620 + safe.Bottom));
            _nameText = UIHelpers.LabelInRect(_root, "Name", "Select a building", 36, Color.white,
                new Rect(0.04f, 0.72f, 0.96f, 0.98f));
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
            _root.SetActive(false);
        }

        public void Show(BuildingDefinition def)
        {
            _current = def;
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
