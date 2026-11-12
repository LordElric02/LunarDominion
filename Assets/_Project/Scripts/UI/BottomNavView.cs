using System;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    public enum NavTab { Base, Heroes, Army, Research, Map }

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
            // Five tabs since the Army tab returned (2026-11-12,
            // standing armies): Base / Heroes / Army / Research /
            // Map. Each tab takes an equal share of the bar.
            string[] labels = { "Base", "Heroes", "Army", "Research", "Map" };
            NavTab[] tabs = { NavTab.Base, NavTab.Heroes, NavTab.Army, NavTab.Research, NavTab.Map };
            float step = 1f / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var tab = tabs[i];
                var btn = UIHelpers.Button(bar, labels[i] + "Tab", labels[i], 28,
                    UIHelpers.Hex("#1A2436"), Color.white);
                var rt = btn.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(i * step + 0.01f, 0.15f);
                rt.anchorMax = new Vector2((i + 1) * step - 0.01f, 0.85f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                btn.onClick.AddListener(() => OnTabSelected?.Invoke(tab));
            }
        }
    }
}
