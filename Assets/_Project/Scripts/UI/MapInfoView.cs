using System;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Info panel for a tapped map node or enemy camp (exterior
    /// Map view). Shows the title and detail lines, plus a Send Excursion
    /// button when the point can take an army (map excursions, 2026-11-09).
    /// Also shows excursion results (collection / battle outcome) in the
    /// same panel. Tapping empty map ground hides it (the Bootstrap wires
    /// ExteriorView.OnEmptyMapTapped to Hide).</summary>
    public class MapInfoView : MonoBehaviour
    {
        // Raised when the Send Excursion button is tapped.
        public event Action OnSendClicked;

        // True while the panel is showing an excursion result rather
        // than a point's info — the Bootstrap does not overwrite a
        // result with a point refresh until the player taps again.
        public bool ShowingResult => _showingResult;

        private GameObject _root;
        private Text _titleText;
        private Text _infoText;
        private Button _sendButton;
        private bool _showingResult;

        public void Build(Canvas canvas)
        {
            var safe = SafeArea.Get(canvas);
            // Right-side panel, same spot as the building panel, so it
            // never covers the map.
            _root = UIHelpers.Panel(canvas.gameObject, "MapInfoPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0.68f, 0), new Vector2(1, 0), new Vector2(20, 320 + safe.Bottom), new Vector2(-20 - safe.Right, 920 - safe.Top));
            _titleText = UIHelpers.LabelInRect(_root, "Title", "", 36, Color.white,
                new Rect(0.04f, 0.72f, 0.96f, 0.98f));
            _infoText = UIHelpers.LabelInRect(_root, "Info", "", 26, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.10f, 0.96f, 0.72f));
            // Send button in the bottom strip, below the info text.
            _sendButton = UIHelpers.Button(_root, "SendButton", "Send Excursion", 28,
                UIHelpers.Hex("#2E7D32"), Color.white);
            var rt = _sendButton.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.01f);
            rt.anchorMax = new Vector2(0.96f, 0.09f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _sendButton.onClick.AddListener(() => OnSendClicked?.Invoke());
            _sendButton.gameObject.SetActive(false);
            _root.SetActive(false);
        }

        public void Show(string title, string info, bool showSend)
        {
            _showingResult = false;
            _titleText.text = title;
            _infoText.text = info;
            _sendButton.gameObject.SetActive(showSend);
            _root.SetActive(true);
        }

        // Show an excursion outcome (collection / battle) in the same
        // panel. The Send button hides — the player taps the point again
        // for its current state.
        public void ShowResult(string title, string body)
        {
            _showingResult = true;
            _titleText.text = title;
            _infoText.text = body;
            _sendButton.gameObject.SetActive(false);
            _root.SetActive(true);
        }

        public void SetSendEnabled(bool enabled)
        {
            if (_sendButton != null) UIHelpers.SetButtonEnabled(_sendButton, enabled);
        }

        public void Hide()
        {
            _showingResult = false;
            _root.SetActive(false);
        }
    }
}
