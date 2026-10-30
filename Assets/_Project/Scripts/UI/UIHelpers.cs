using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Reusable UI builders. Every view uses these, so no screen
    /// duplicates panel/button/text setup. Presentation only — no rules.</summary>
    public static class UIHelpers
    {
        public static Font DefaultFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static GameObject Panel(GameObject parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        public static Text Label(GameObject parent, string name, string text, int size,
            Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var t = go.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = alignment;
            // Phase 5: on a narrower portrait screen (e.g. 360 dp wide)
            // long labels must wrap inside their rect, never run off it.
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return t;
        }

        // Note: callers pass Rect(left, bottom, right, top) — i.e. the third
        // and fourth values are the right/top anchors, not width/height.
        // This helper reads them as anchors, so Rect(0.02, 0.05, 0.26, 0.5)
        // means left 0.02, bottom 0.05, right 0.26, top 0.5.
        public static Text LabelInRect(GameObject parent, string name, string text, int size,
            Color color, Rect rect, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var t = Label(parent, name, text, size, color, alignment);
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(rect.x, rect.y);
            rt.anchorMax = new Vector2(rect.width, rect.height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return t;
        }

        public static Button Button(GameObject parent, string name, string label,
            int fontSize, Color bg, Color fg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            var txt = new GameObject("Text");
            txt.transform.SetParent(go.transform, false);
            var t = txt.AddComponent<Text>();
            t.font = DefaultFont;
            t.text = label;
            t.fontSize = fontSize;
            t.color = fg;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = txt.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return btn;
        }

        public static void SetButtonEnabled(Button button, bool enabled)
        {
            button.interactable = enabled;
            var img = button.GetComponent<Image>();
            if (img != null)
            {
                var c = img.color;
                c.a = enabled ? 1f : 0.45f;
                img.color = c;
            }
        }
    }
}
