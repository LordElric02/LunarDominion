using LunarDominion.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Development HUD / test menu. Visible only when the DEV toggle
    /// is tapped, and the toggle itself is only created in the Editor or a
    /// development build — it never appears in a release build.</summary>
    public class DevHUDView : MonoBehaviour
    {
        private GameObject _root;
        private GameController _controller;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var safe = SafeArea.Get(canvas);
            float topFrac = Screen.height > 0 ? safe.TopPx / Screen.height : 0f;
            float rightFrac = Screen.width > 0 ? safe.RightPx / Screen.width : 0f;
            var toggle = UIHelpers.Button(canvas.gameObject, "DevToggle", "DEV", 24,
                UIHelpers.Hex("#7A3B00"), Color.white);
            var trt = toggle.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0.86f - rightFrac, 0.88f - topFrac);
            trt.anchorMax = new Vector2(0.98f - rightFrac, 0.93f - topFrac);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            toggle.onClick.AddListener(() => _root.SetActive(!_root.activeSelf));
#endif
            _root = UIHelpers.Panel(canvas.gameObject, "DevHUD", UIHelpers.Hex("#222222"),
                new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.88f), Vector2.zero, Vector2.zero);
            var title = UIHelpers.LabelInRect(_root, "Title", "DEV — Test menu", 32, Color.white,
                new Rect(0.05f, 0.85f, 0.95f, 0.98f), TextAnchor.MiddleCenter);
            AddButton("Grant +1000 Biomass", 0.72f, () => _controller.GrantResources(1000, 0, 0));
            AddButton("Grant +1000 Alloy", 0.60f, () => _controller.GrantResources(0, 1000, 0));
            AddButton("Grant +1000 Helium-3", 0.48f, () => _controller.GrantResources(0, 0, 1000));
            AddButton("Grant +1000 Energy", 0.36f, () => _controller.GrantEnergy(1000));
            AddButton("Reset save (new game)", 0.24f, () => { _controller.ResetGame(); _controller.SaveGame(); });
            AddButton("Save now", 0.12f, () => _controller.SaveGame());
            _root.SetActive(false);
        }

        private void AddButton(string label, float yMin, System.Action action)
        {
            var btn = UIHelpers.Button(_root, label, label, 26, UIHelpers.Hex("#3A3A3A"), Color.white);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, yMin);
            rt.anchorMax = new Vector2(0.95f, yMin + 0.10f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            btn.onClick.AddListener(() => { action(); _controller.NotifyChanged(); });
        }
    }
}
