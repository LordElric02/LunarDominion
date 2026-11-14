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
        private Text _levelText;
        private Text _biomassText;
        private Text _alloyText;
        private Text _heliumText;
        private Text _energyText;
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            // Transparent background (Patrick 2026-11-14): the navy
            // band is gone — the level/resource text overlays the 3D
            // view directly. The panel object stays (the Map and the
            // boss fight find "TopBar" by name to hide it), but its
            // image is fully transparent and non-raycasting, so the
            // invisible strip can't eat taps meant for the scene.
            var bar = UIHelpers.Panel(canvas.gameObject, "TopBar", new Color(0f, 0f, 0f, 0f),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -140 - safe.Top), new Vector2(0, -safe.Top));
            var barImage = bar.GetComponent<Image>();
            if (barImage != null) barImage.raycastTarget = false;

            // 2026-11-07: the title is the player level ("Level:XX"),
            // derived from progression — see PlayerLevelService. The
            // old "LUNAR DOMINION" title is gone.
            _levelText = UIHelpers.LabelInRect(bar, "Level", "Level:0", 30,
                Color.white, new Rect(0.01f, 0.05f, 0.24f, 0.95f), TextAnchor.MiddleCenter);

            // Four chips share the remaining 75% in one row (18.75%
            // each, ~360px per chip — the longest chip, "ALLOY
            // 1,389,000", is ~250px at font 30). The PWR chip was
            // removed 2026-11-07 — power may be shown on a player
            // profile page later, not on this screen.
            _biomassText = UIHelpers.LabelInRect(bar, "Biomass", "", 30,
                UIHelpers.Hex("#7ED957"), new Rect(0.24f, 0.05f, 0.4275f, 0.95f), TextAnchor.MiddleCenter);
            _alloyText = UIHelpers.LabelInRect(bar, "Alloy", "", 30,
                UIHelpers.Hex("#C0C8D0"), new Rect(0.4275f, 0.05f, 0.615f, 0.95f), TextAnchor.MiddleCenter);
            _heliumText = UIHelpers.LabelInRect(bar, "Helium", "", 30,
                UIHelpers.Hex("#5AD1E6"), new Rect(0.615f, 0.05f, 0.8025f, 0.95f), TextAnchor.MiddleCenter);
            _energyText = UIHelpers.LabelInRect(bar, "Energy", "", 30,
                UIHelpers.Hex("#FFD166"), new Rect(0.8025f, 0.05f, 0.99f, 0.95f), TextAnchor.MiddleCenter);
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
            // the level updates after upgrades and research
            // completions with no new event.
            if (_levelText != null)
            {
                int level = _controller.PlayerLevel != null ? _controller.PlayerLevel.Level() : 0;
                _levelText.text = $"Level:{level}";
            }
        }
    }
}
