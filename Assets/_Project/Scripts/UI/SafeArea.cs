using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Safe-area insets (Phase 5). Screen.safeArea is in screen
    /// pixels; panels in this project use CanvasScaler reference units,
    /// so pixel insets are divided by the scaler's scale factor.
    /// On a device without a notch, or in the Editor Game view where
    /// safeArea is the full screen, every inset is 0 and nothing moves.
    /// Insets are clamped so an extreme device cannot push panels
    /// off-screen; a slightly under-shifted panel is better than a
    /// broken layout.</summary>
    public struct SafeAreaInsets
    {
        public float LeftPx;
        public float RightPx;
        public float TopPx;
        public float BottomPx;
        public float Left;
        public float Right;
        public float Top;
        public float Bottom;
    }

    public static class SafeArea
    {
        public static SafeAreaInsets Get(Canvas canvas)
        {
            var insets = new SafeAreaInsets();
            if (Screen.width <= 0 || Screen.height <= 0) return insets;
            var safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0) return insets;

            insets.LeftPx = safe.xMin;
            insets.BottomPx = safe.yMin;
            insets.RightPx = Screen.width - safe.xMax;
            insets.TopPx = Screen.height - safe.yMax;

            float scale = 1f;
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            if (scaler != null && scaler.referenceResolution.x > 0 && scaler.referenceResolution.y > 0)
            {
                float widthRatio = Screen.width / scaler.referenceResolution.x;
                float heightRatio = Screen.height / scaler.referenceResolution.y;
                float match = Mathf.Clamp01(scaler.matchWidthOrHeight);
                scale = Mathf.Pow(widthRatio, 1f - match) * Mathf.Pow(heightRatio, match);
                if (scale <= 0f) scale = 1f;
            }

            insets.Left = Mathf.Clamp(insets.LeftPx / scale, 0f, 60f);
            insets.Right = Mathf.Clamp(insets.RightPx / scale, 0f, 60f);
            insets.Top = Mathf.Clamp(insets.TopPx / scale, 0f, 120f);
            insets.Bottom = Mathf.Clamp(insets.BottomPx / scale, 0f, 80f);
            return insets;
        }
    }
}
