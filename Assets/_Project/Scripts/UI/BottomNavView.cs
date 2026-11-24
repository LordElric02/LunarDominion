using System;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    public enum NavTab { Base, Heroes, Army, Research, Map, Leaderboards }

    /// <summary>Bottom navigation. One reusable tab component, six
    /// instances (Leaderboards added 2026-11-24). Tabs only switch
    /// views — they contain no gameplay logic.
    ///
    /// Player-toggled (Patrick 2026-11-18: the permanent bar "takes
    /// up too much space to be permanent"): the bar starts HIDDEN.
    /// A small MENU button at the bottom-left edge pops it up;
    /// the same button (HIDE while open, floated just above the
    /// bar) puts it away for a full view. The Map and boss fights
    /// SUPPRESS the bar and its button through SetSuppressed —
    /// suppression never touches the player's own open/closed
    /// choice, which survives both round trips.</summary>
    public class BottomNavView : MonoBehaviour
    {
        public event Action<NavTab> OnTabSelected;

        private GameObject _bar;
        private GameObject _toggleRoot;
        private RectTransform _toggleRt;
        private Text _toggleLabel;
        private float _safeBottom;
        private float _safeLeft;

        // The player's own choice (starts closed) and the external
        // suppression (Map up / fight running). The bar shows only
        // when the player opened it AND nothing suppresses it; the
        // MENU button shows whenever nothing suppresses it.
        private bool _userOpen;
        private bool _suppressed;

        public void Build(Canvas canvas)
        {
            var safe = SafeArea.Get(canvas);
            _safeBottom = safe.Bottom;
            _safeLeft = safe.Left;
            _bar = UIHelpers.Panel(canvas.gameObject, "BottomNav", UIHelpers.Hex("#101826"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, safe.Bottom), new Vector2(0, 180 + safe.Bottom));
            // Five tabs since the Army tab returned (2026-11-12,
            // standing armies): Base / Heroes / Army / Research /
            // Map. Each tab takes an equal share of the bar.
            string[] labels = { "Base", "Heroes", "Army", "Research", "Map", "Leaderboards" };
            NavTab[] tabs = { NavTab.Base, NavTab.Heroes, NavTab.Army, NavTab.Research, NavTab.Map, NavTab.Leaderboards };
            float step = 1f / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var tab = tabs[i];
                var btn = UIHelpers.Button(_bar, labels[i] + "Tab", labels[i], 28,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * step + 0.01f, 0.15f);
                rt.anchorMax = new Vector2((i + 1) * step - 0.01f, 0.85f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => OnTabSelected?.Invoke(tab));
            }

            // The summon button: same panel/button styling as the
            // Map's BASE/FIND corner cluster, parked at the bottom-
            // LEFT edge (Patrick 2026-11-18, moved from bottom-
            // centre after the first Play check). Created after
            // the bar so it draws above it; Refresh() parks it at
            // the bottom edge while the bar is closed and floats
            // it just above the bar while the bar is open.
            _toggleRoot = UIHelpers.Panel(canvas.gameObject, "NavToggle",
                new Color(0.06f, 0.09f, 0.15f, 0.94f),
                new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(safe.Left + 14f, safe.Bottom + 14f), new Vector2(safe.Left + 234f, safe.Bottom + 76f));
            _toggleRt = _toggleRoot.GetComponent<RectTransform>();
            var toggleBtn = UIHelpers.Button(_toggleRoot, "NavToggleButton", "MENU", 28,
                UIHelpers.Hex("#1A2436"), Color.white);
            var trt = toggleBtn.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            _toggleLabel = toggleBtn.GetComponentInChildren<Text>();
            toggleBtn.onClick.AddListener(Toggle);

            Refresh();
        }

        /// <summary>External suppression: the Map (ExteriorView)
        /// and boss fights (BossLairView) hide the whole bottom
        /// chrome while they are on top. Suppression is separate
        /// from the player's open/closed choice and never changes
        /// it. Safe to call before Build() — the flag is stored
        /// and applied when the UI exists.</summary>
        public void SetSuppressed(bool suppressed)
        {
            _suppressed = suppressed;
            Refresh();
        }

        private void Toggle()
        {
            _userOpen = !_userOpen;
            Refresh();
        }

        private void Refresh()
        {
            if (_bar != null)
                _bar.SetActive(_userOpen && !_suppressed);
            if (_toggleRoot != null)
            {
                _toggleRoot.SetActive(!_suppressed);
                // Closed: hug the bottom-left edge. Open: float just
                // above the 180-tall bar so the HIDE button never
                // covers a tab.
                float y0 = _userOpen ? _safeBottom + 192f : _safeBottom + 14f;
                float y1 = _userOpen ? _safeBottom + 254f : _safeBottom + 76f;
                _toggleRt.offsetMin = new Vector2(_safeLeft + 14f, y0);
                _toggleRt.offsetMax = new Vector2(_safeLeft + 234f, y1);
            }
            if (_toggleLabel != null)
                _toggleLabel.text = _userOpen ? "HIDE" : "MENU";
        }
    }
}
