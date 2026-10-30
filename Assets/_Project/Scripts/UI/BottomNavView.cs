using System;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    public enum NavTab { Base, Army, Expeditions, Research }

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
            string[] labels = { "Base", "Army", "Expeditions", "Research" };
            NavTab[] tabs = { NavTab.Base, NavTab.Army, NavTab.Expeditions, NavTab.Research };
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
