using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Reusable placeholder screen. Used for Army, Expeditions and
    /// Research in Phase 2 — those systems arrive in Phases 3–4, and this
    /// panel makes that explicit instead of faking functionality.</summary>
    public class PlaceholderView : MonoBehaviour
    {
        private GameObject _root;
        private Text _titleText;
        private Text _bodyText;

        public void Build(Canvas canvas)
        {
            _root = UIHelpers.Panel(canvas.gameObject, "PlaceholderPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 310), new Vector2(-20, 1200));
            _titleText = UIHelpers.LabelInRect(_root, "Title", "", 40, Color.white,
                new Rect(0.05f, 0.80f, 0.95f, 0.95f), TextAnchor.MiddleCenter);
            _bodyText = UIHelpers.LabelInRect(_root, "Body", "", 30, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.05f, 0.30f, 0.95f, 0.78f), TextAnchor.MiddleCenter);
            _root.SetActive(false);
        }

        public void Show(string title, string body)
        {
            _titleText.text = title;
            _bodyText.text = body;
            _root.SetActive(true);
        }

        public void Hide() { _root.SetActive(false); }
    }
}
