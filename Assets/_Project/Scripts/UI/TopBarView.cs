using LunarDominion.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Top resource bar. Display only — it reads GameState, never
    /// computes costs, production, or energy rules.</summary>
    public class TopBarView : MonoBehaviour
    {
        private Text _biomassText;
        private Text _alloyText;
        private Text _heliumText;
        private Text _energyText;
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var bar = UIHelpers.Panel(canvas.gameObject, "TopBar", UIHelpers.Hex("#101826"),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -140), new Vector2(0, 0));

            var title = UIHelpers.LabelInRect(bar, "Title", "LUNAR DOMINION", 36,
                Color.white, new Rect(0.02f, 0.55f, 0.98f, 1f), TextAnchor.MiddleCenter);

            _biomassText = UIHelpers.LabelInRect(bar, "Biomass", "", 30,
                UIHelpers.Hex("#7ED957"), new Rect(0.02f, 0.05f, 0.26f, 0.5f), TextAnchor.MiddleCenter);
            _alloyText = UIHelpers.LabelInRect(bar, "Alloy", "", 30,
                UIHelpers.Hex("#C0C8D0"), new Rect(0.26f, 0.05f, 0.50f, 0.5f), TextAnchor.MiddleCenter);
            _heliumText = UIHelpers.LabelInRect(bar, "Helium", "", 30,
                UIHelpers.Hex("#5AD1E6"), new Rect(0.50f, 0.05f, 0.74f, 0.5f), TextAnchor.MiddleCenter);
            _energyText = UIHelpers.LabelInRect(bar, "Energy", "", 30,
                UIHelpers.Hex("#FFD166"), new Rect(0.74f, 0.05f, 0.98f, 0.5f), TextAnchor.MiddleCenter);
            Refresh();
        }

        public void Refresh()
        {
            if (_controller == null || _biomassText == null) return;
            var s = _controller.State;
            _biomassText.text = $"BIO {s.Biomass}";
            _alloyText.text = $"ALLOY {s.Alloy}";
            _heliumText.text = $"He-3 {s.Helium3}";
            _energyText.text = $"NRG {s.Energy}";
        }
    }
}
