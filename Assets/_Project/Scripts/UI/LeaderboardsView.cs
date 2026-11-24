using LunarDominion.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Leaderboards (2026-11-24): the ranked-stats
    /// screen, a bottom-nav tab. First category: Robot Lair
    /// Damage. Local for now — the player's own entry, banked
    /// on GameState after every lair fight; online rankings
    /// join it when Firebase accounts land, and the same
    /// career totals upload as-is.</summary>
    public class LeaderboardsView : MonoBehaviour
    {
        private GameController _controller;
        private GameObject _root;
        private Text _entryText;
        private Text _detailText;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            var safe = SafeArea.Get(canvas);
            // Same panel geometry as the Heroes tab, so the
            // screen sits where the player expects a tab to sit.
            _root = UIHelpers.Panel(canvas.gameObject, "LeaderboardsPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 200 + safe.Bottom), new Vector2(-20, 920 - safe.Top));
            UIHelpers.LabelInRect(_root, "Title", "LEADERBOARDS", 40, Color.white,
                new Rect(0.04f, 0.88f, 0.96f, 0.97f), TextAnchor.MiddleCenter);
            UIHelpers.LabelInRect(_root, "Category", "ROBOT LAIR DAMAGE", 32, UIHelpers.Hex("#7ED957"),
                new Rect(0.06f, 0.76f, 0.94f, 0.86f), TextAnchor.MiddleLeft);
            _entryText = UIHelpers.LabelInRect(_root, "Entry", "", 34, Color.white,
                new Rect(0.06f, 0.63f, 0.94f, 0.75f), TextAnchor.MiddleLeft);
            _detailText = UIHelpers.LabelInRect(_root, "Detail", "", 26, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.06f, 0.22f, 0.94f, 0.61f), TextAnchor.UpperLeft);
            UIHelpers.LabelInRect(_root, "Note", "Online rankings arrive with player accounts.", 22,
                UIHelpers.Hex("#8A94A6"), new Rect(0.06f, 0.05f, 0.94f, 0.13f), TextAnchor.MiddleCenter);
            _root.SetActive(false);
        }

        public void Show()
        {
            _root.SetActive(true);
            Refresh();
        }

        public void Hide() { _root.SetActive(false); }

        public void Refresh()
        {
            if (_controller == null || _entryText == null) return;
            var s = _controller.State;
            _entryText.text = $"#1   {s.EffectiveUsername}   —   {s.LairDamageScore:N0} pts";
            _detailText.text =
                $"Boss damage (career): {s.LairBossDamage:N0}\n" +
                $"Drone damage (career): {s.LairDroneDamage:N0}\n" +
                $"Highest boss level defeated: {s.BossLevelDefeated} / 15\n\n" +
                "Points per fight = (boss damage + drone damage) x boss level.\n" +
                "Damage counts on every finished fight, win or lose.";
        }
    }
}
