using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>In-game splash screen. Shows the Lunar Dominion artwork
    /// full-screen at startup, holds for HoldSeconds, then fades out to
    /// reveal the base behind it. Tap anywhere to skip. The artwork is
    /// loaded from Resources (Assets/_Project/Resources/Textures/
    /// LunarDominionSplash.png) — the same pattern BaseView uses for the
    /// building models — so it works in a built player. If the texture
    /// is missing, a plain title on the near-black background is shown
    /// instead, so the game is never blocked by a missing file.</summary>
    public class SplashView : MonoBehaviour
    {
        // Tunable: how long the artwork holds before fading.
        private const float HoldSeconds = 2.0f;
        private const float FadeSeconds = 0.5f;
        private const string TexturePath = "Textures/LunarDominionSplash";

        private GameObject _root;
        private CanvasGroup _group;

        public void Build()
        {
            var canvas = UIHelpers.CreateCanvas("SplashCanvas");
            canvas.sortingOrder = 100;
            _root = canvas.gameObject;

            _group = _root.AddComponent<CanvasGroup>();

            // Near-black background (#06050a): the artwork's edge colour,
            // so any letterbox bars blend invisibly into the image.
            UIHelpers.Panel(_root, "Background", UIHelpers.Hex("#06050a"),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var texture = Resources.Load<Texture2D>(TexturePath);
            if (texture != null)
            {
                var sprite = Sprite.Create(texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                var art = new GameObject("Artwork");
                art.transform.SetParent(_root.transform, false);
                var img = art.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                var rt = art.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            else
            {
                Debug.LogWarning("[Lunar Dominion] Splash texture not found at Resources/" + TexturePath + ". Showing title fallback — move LunarDominionSplash.png into Assets/_Project/Resources/Textures/.");
                UIHelpers.LabelInRect(_root, "Title", "LUNAR DOMINION", 72, Color.white,
                    new Rect(0.05f, 0.45f, 0.95f, 0.60f), TextAnchor.MiddleCenter);
            }

            // Full-screen invisible button: any tap skips the hold.
            var tap = new GameObject("Skip");
            tap.transform.SetParent(_root.transform, false);
            var tapImg = tap.AddComponent<Image>();
            tapImg.color = new Color(0, 0, 0, 0);
            var btn = tap.AddComponent<Button>();
            btn.targetGraphic = tapImg;
            btn.onClick.AddListener(Skip);
            var tapRt = tap.GetComponent<RectTransform>();
            tapRt.anchorMin = Vector2.zero;
            tapRt.anchorMax = Vector2.one;
            tapRt.offsetMin = Vector2.zero;
            tapRt.offsetMax = Vector2.zero;

            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return new WaitForSecondsRealtime(HoldSeconds);
            var t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                if (_group != null)
                    _group.alpha = 1f - Mathf.Clamp01(t / FadeSeconds);
                yield return null;
            }
            Finish();
        }

        private void Skip()
        {
            StopAllCoroutines();
            Finish();
        }

        private void Finish()
        {
            if (_root != null) Destroy(_root);
            Destroy(gameObject);
        }
    }
}
