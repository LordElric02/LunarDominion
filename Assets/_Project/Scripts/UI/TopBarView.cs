using LunarDominion.Core;
using LunarDominion.Progression;
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
        private Text _powerText;
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            var bar = UIHelpers.Panel(canvas.gameObject, "TopBar", UIHelpers.Hex("#101826"),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -140 - safe.Top), new Vector2(0, -safe.Top));

            // 2026-11-06 full-bleed: single row like the reference HUD —
            // title in a left segment, the five resource chips sharing
            // the rest in one row (was title-above-chips). Same texts,
            // colours and font sizes; the title shrinks to 30 so it
            // fits its 23% segment (~262px of "LUNAR DOMINION" in a
            // ~442px segment at 1920 wide).
            var title = UIHelpers.LabelInRect(bar, "Title", "LUNAR DOMINION", 30,
                Color.white, new Rect(0.01f, 0.05f, 0.24f, 0.95f), TextAnchor.MiddleCenter);

            // Five chips share the remaining 75% in one row (15% each,
            // ~288px per chip — the longest chip, "ALLOY 1,389,000",
            // is ~250px at font 30).
            _biomassText = UIHelpers.LabelInRect(bar, "Biomass", "", 30,
                UIHelpers.Hex("#7ED957"), new Rect(0.24f, 0.05f, 0.39f, 0.95f), TextAnchor.MiddleCenter);
            _alloyText = UIHelpers.LabelInRect(bar, "Alloy", "", 30,
                UIHelpers.Hex("#C0C8D0"), new Rect(0.39f, 0.05f, 0.54f, 0.95f), TextAnchor.MiddleCenter);
            _heliumText = UIHelpers.LabelInRect(bar, "Helium", "", 30,
                UIHelpers.Hex("#5AD1E6"), new Rect(0.54f, 0.05f, 0.69f, 0.95f), TextAnchor.MiddleCenter);
            _energyText = UIHelpers.LabelInRect(bar, "Energy", "", 30,
                UIHelpers.Hex("#FFD166"), new Rect(0.69f, 0.05f, 0.84f, 0.95f), TextAnchor.MiddleCenter);
            _powerText = UIHelpers.LabelInRect(bar, "Power", "", 30,
                UIHelpers.Hex("#FFA94D"), new Rect(0.84f, 0.05f, 0.99f, 0.95f), TextAnchor.MiddleCenter);
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
            // Refresh() already runs on every OnStateChanged (Bootstrap
            // RefreshAll, once a second plus every NotifyChanged), so
            // power updates after upgrades, recruitment, level-ups,
            // casualties, and research completions with no new event.
            if (_powerText != null)
            {
                long power = _controller.Power != null ? _controller.Power.TotalPower() : 0;
                _powerText.text = $"PWR {PowerService.FormatPower(power)}";
            }
        }
    }
}
