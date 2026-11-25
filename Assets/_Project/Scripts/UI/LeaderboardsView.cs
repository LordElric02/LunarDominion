using System.Text;
using LunarDominion.Accounts;
using LunarDominion.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Leaderboards: the ranked-stats screen, a
    /// bottom-nav tab. First category: Robot Lair Damage.
    /// CLOUD BOARD (2026-11-25): on Show the view pushes the
    /// player's current career totals, then fetches the public
    /// top-25 from Firestore (leaderboards_robotLairDamage,
    /// ordered by score) and renders it in two columns with the
    /// player's own row in gold and their true rank on the entry
    /// line. The local career entry paints instantly underneath,
    /// so offline / local-only builds show exactly what they
    /// showed before — the board area just stays empty and the
    /// status line says rankings are unavailable.</summary>
    public class LeaderboardsView : MonoBehaviour
    {
        private const int BoardSize = 25;
        private const int LeftColumnRows = 13;

        private GameController _controller;
        private GameObject _root;
        private Text _entryText;
        private Text _boardLeft;
        private Text _boardRight;
        private Text _statusText;
        private Text _detailText;

        // Null until a fetch lands: the entry line then carries
        // the true rank ("#3", or "#25+" when the board is full
        // and the player sits below it).
        private string _rankText;
        private int _fetchToken;

        /// <summary>Set by Bootstrap right after Build (the
        /// TopBarView pattern). Null-safe: without it the view
        /// is the local board it always was.</summary>
        public AccountManager AccountManager { get; set; }

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
                new Rect(0.06f, 0.79f, 0.94f, 0.87f), TextAnchor.MiddleLeft);
            _entryText = UIHelpers.LabelInRect(_root, "Entry", "", 30, Color.white,
                new Rect(0.06f, 0.70f, 0.94f, 0.78f), TextAnchor.MiddleLeft);
            _boardLeft = UIHelpers.LabelInRect(_root, "BoardLeft", "", 20, Color.white,
                new Rect(0.06f, 0.21f, 0.495f, 0.68f), TextAnchor.UpperLeft);
            _boardRight = UIHelpers.LabelInRect(_root, "BoardRight", "", 20, Color.white,
                new Rect(0.515f, 0.21f, 0.94f, 0.68f), TextAnchor.UpperLeft);
            _statusText = UIHelpers.LabelInRect(_root, "Status", "", 19, UIHelpers.Hex("#8A94A6"),
                new Rect(0.06f, 0.145f, 0.94f, 0.20f), TextAnchor.MiddleCenter);
            _detailText = UIHelpers.LabelInRect(_root, "Detail", "", 20, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.06f, 0.04f, 0.94f, 0.135f), TextAnchor.MiddleCenter);
            _root.SetActive(false);
        }

        public void Show()
        {
            _root.SetActive(true);
            Refresh();
            LoadBoard();
        }

        public void Hide() { _root.SetActive(false); }

        public void Refresh()
        {
            if (_controller == null || _entryText == null) return;
            var s = _controller.State;
            string rank = _rankText != null ? _rankText + "   " : "";
            _entryText.text = $"{rank}{s.EffectiveUsername}   —   {s.LairDamageScore:N0} pts";
            _detailText.text =
                $"Boss damage (career): {s.LairBossDamage:N0}   ·   Drone damage (career): {s.LairDroneDamage:N0}   ·   Highest boss level defeated: {s.BossLevelDefeated} / 15\n" +
                "Points per fight = (boss damage + drone damage) x boss level — counted on every finished fight, win or lose.";
        }

        /// <summary>Push-then-fetch: the push keeps the player's
        /// own row current (it otherwise refreshes at sign-in),
        /// the fetch paints the board. Both fail soft — any
        /// problem leaves the local entry standing alone.</summary>
        private async void LoadBoard()
        {
            int token = ++_fetchToken;
            if (AccountManager == null || !AccountManager.CloudAvailable)
            {
                _statusText.text = "Online rankings unavailable right now — showing your career totals.";
                return;
            }
            _statusText.text = "Loading online rankings…";
            try
            {
                await AccountManager.PushLairDamageAsync();
                var top = await AccountManager.Leaderboard.FetchTopLairDamageAsync(BoardSize);
                if (token != _fetchToken || top == null) return;
                if (top.Count == 0)
                {
                    _statusText.text = "Online rankings unavailable right now — showing your career totals.";
                    return;
                }
                RenderBoard(top);
                _statusText.text = "";
                Refresh();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Leaderboard load failed: {e.Message}");
                if (token == _fetchToken)
                    _statusText.text = "Online rankings unavailable right now — showing your career totals.";
            }
        }

        private void RenderBoard(System.Collections.Generic.List<LeaderboardEntry> top)
        {
            string myUid = AccountManager.Accounts.CurrentUid;
            int myScore = _controller.State.LairDamageScore;

            // True rank: the row's position when it made the
            // board, otherwise 1 + everyone fetched above the
            // local score.
            int rank = 0;
            for (int i = 0; i < top.Count; i++)
                if (top[i].Uid == myUid) { rank = i + 1; break; }
            if (rank == 0)
            {
                int above = 0;
                foreach (var e in top) if (e.Score > myScore) above++;
                rank = above + 1;
            }
            _rankText = rank <= BoardSize ? $"#{rank}" : $"#{BoardSize}+";

            var left = new StringBuilder();
            var right = new StringBuilder();
            for (int i = 0; i < top.Count; i++)
            {
                var e = top[i];
                string line = $"#{i + 1}  {CleanName(e.Username)} — {e.Score:N0}";
                if (e.Uid == myUid)
                    line = "<color=#FFD54A>" + line + "</color>";
                if (i < LeftColumnRows) left.AppendLine(line);
                else right.AppendLine(line);
            }
            _boardLeft.text = left.ToString();
            _boardRight.text = right.ToString();
        }

        /// <summary>Names are player-typed: strip the rich-text
        /// brackets so a name can never inject markup into the
        /// board.</summary>
        private static string CleanName(string name)
        {
            string clean = (name ?? "").Replace("<", "").Replace(">", "").Trim();
            return clean.Length == 0 ? "Commander" : clean;
        }
    }
}
