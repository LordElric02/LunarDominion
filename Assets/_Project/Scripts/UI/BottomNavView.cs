using System;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    public enum NavTab { Base, Heroes, Research, Map }

    /// <summary>Bottom navigation. One reusable tab component, four instances.
    /// Tabs only switch views — they contain no gameplay logic.</summary>
    public class BottomNavView : MonoBehaviour
    {
        public event Action<NavTab> OnTabSelected;

        public void Build(Canvas canvas)
        {
            var safe = SafeArea.Get(canvas);
            var bar = UIHelpers.Panel(canvas.gameObject, "BottomNav", UIHelpers.Hex("#101826"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, safe.Bottom), new Vector2(0, 180 + safe.Bottom));
            // 2026-11-10 redesign: the Army tab is gone (regular troops
            // are recruited from their barracks building panel now) and
            // the tab is Heroes — heroes only, no regular army mixed in.
            // Four tabs (Expeditions removed 2026-11-09). Each tab takes
            // a quarter of the bar.
            string[] labels = { "Base", "Heroes", "Research", "Map" };
            NavTab[] tabs = { NavTab.Base, NavTab.Heroes, NavTab.Research, NavTab.Map };
            for (int i = 0; i < labels.Length; i++)
            {
                var tab = tabs[i];
                var btn = UIHelpers.Button(bar, labels[i] + "Tab", labels[i], 28,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * 0.25f + 0.01f, 0.15f);
                rt.anchorMax = new Vector2((i + 1) * 0.25f - 0.01f, 0.85f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => OnTabSelected?.Invoke(tab));
            }
        }
    }
}
