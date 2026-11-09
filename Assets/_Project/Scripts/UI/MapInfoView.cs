using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Info panel for a tapped map node or enemy camp (exterior
    /// Map view). Display only: shows the title and a few detail lines
    /// (e.g. a resource node's type and amount, or a camp's enemy tier).
    /// No actions yet — sending armies and fighting come in a later
    /// phase. Tapping empty map ground hides it (the Bootstrap wires
    /// ExteriorView.OnEmptyMapTapped to Hide).</summary>
    public class MapInfoView : MonoBehaviour
    {
        private GameObject _root;
        private Text _titleText;
        private Text _infoText;

        public void Build(Canvas canvas)
        {
            var safe = SafeArea.Get(canvas);
            // Right-side panel, same spot as the building panel, so it
            // never covers the map. Simpler than the building panel:
            // title + info lines only, no buttons.
            _root = UIHelpers.Panel(canvas.gameObject, "MapInfoPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0.68f, 0), new Vector2(1, 0), new Vector2(20, 320 + safe.Bottom), new Vector2(-20 - safe.Right, 920 - safe.Top));
            _titleText = UIHelpers.LabelInRect(_root, "Title", "", 36, Color.white,
                new Rect(0.04f, 0.72f, 0.96f, 0.98f));
            _infoText = UIHelpers.LabelInRect(_root, "Info", "", 26, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.10f, 0.96f, 0.72f));
            _root.SetActive(false);
        }

        public void Show(string title, string info)
        {
            _titleText.text = title;
            _infoText.text = info;
            _root.SetActive(true);
        }

        public void Hide()
        {
            _root.SetActive(false);
        }
    }
}
