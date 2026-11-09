using System;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Info panel for a tapped map node or enemy camp (exterior
    /// Map view). Shows the title and detail lines, plus a Choose Squad
    /// button when the point can take an army (map excursions, 2026-11-09)
    /// — tapping it opens the squad picker so the player picks which
    /// units go, instead of sending immediately. Also shows excursion
    /// results (collection / battle outcome) in the same panel.
    /// 2026-11-09: compact panel (360 ref px) — the detail text lives in
    /// a ScrollRect with an auto-hiding vertical scrollbar, so short node
    /// info fits without scrolling and long battle results scroll. The
    /// content height is set from the text's preferred height (a layout
    /// group measured it as zero, so the text vanished).</summary>
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
        private ScrollRect _scroll;
        private RectTransform _contentRt;
        private bool _showingResult;

        public void Build(Canvas canvas)
        {
            var safe = SafeArea.Get(canvas);
            // Right-side panel, same spot as the building panel, so it
            // never covers the map. Compact height (360 ref px): top
            // just below the top bar, bottom well above the nav.
            _root = UIHelpers.Panel(canvas.gameObject, "MapInfoPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0.68f, 0), new Vector2(1, 0), new Vector2(20, 560 + safe.Bottom), new Vector2(-20 - safe.Right, 920 - safe.Top));
            _titleText = UIHelpers.LabelInRect(_root, "Title", "", 36, Color.white,
                new Rect(0.04f, 0.80f, 0.96f, 0.97f));
            BuildInfoScroll();
            // Send button in the bottom strip, below the info text.
            _sendButton = UIHelpers.Button(_root, "SendButton", "Choose Squad", 28,
                UIHelpers.Hex("#2E7D32"), Color.white);
            var rt = _sendButton.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.02f);
            rt.anchorMax = new Vector2(0.96f, 0.12f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _sendButton.onClick.AddListener(() => OnSendClicked?.Invoke());
            _sendButton.gameObject.SetActive(false);
            _root.SetActive(false);
        }

        // Scrollable detail area. The content height is set from the
        // text's preferred height whenever the text changes (see
        // ResetScroll) — a VerticalLayoutGroup + ContentSizeFitter
        // measured the single Text as zero height, so the text
        // vanished. The vertical scrollbar hides itself when the text
        // fits the viewport and appears when it overflows.
        private void BuildInfoScroll()
        {
            var scrollGo = new GameObject("InfoScroll");
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.04f, 0.14f);
            scrollRt.anchorMax = new Vector2(0.96f, 0.78f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = UIHelpers.Hex("#0F1724");
            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = new Vector2(-18, 0);
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = UIHelpers.Hex("#0F1724");
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            _scroll.viewport = viewportRt;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _contentRt = contentGo.AddComponent<RectTransform>();
            _contentRt.anchorMin = new Vector2(0, 1);
            _contentRt.anchorMax = new Vector2(1, 1);
            _contentRt.pivot = new Vector2(0.5f, 1);
            _contentRt.anchoredPosition = Vector2.zero;
            _contentRt.sizeDelta = Vector2.zero;
            _scroll.content = _contentRt;

            _infoText = UIHelpers.LabelInRect(contentGo, "Info", "", 26, UIHelpers.Hex("#C0C8D0"),
                new Rect(0, 0, 1, 1), TextAnchor.UpperLeft);
            var infoRt = _infoText.GetComponent<RectTransform>();
            infoRt.offsetMin = new Vector2(8, 6);
            infoRt.offsetMax = new Vector2(-8, -6);

            // Vertical scrollbar, auto-hidden when the text fits.
            var barGo = new GameObject("InfoScrollbar");
            barGo.transform.SetParent(scrollGo.transform, false);
            var barRt = barGo.AddComponent<RectTransform>();
            barRt.anchorMin = new Vector2(1, 0);
            barRt.anchorMax = new Vector2(1, 1);
            barRt.offsetMin = new Vector2(-16, 0);
            barRt.offsetMax = new Vector2(0, 0);
            var barBg = barGo.AddComponent<Image>();
            barBg.color = UIHelpers.Hex("#0F1724");
            var scrollbar = barGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(barGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = UIHelpers.Hex("#5A6B84");
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleImg;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        public void Show(string title, string info, bool showSend)
        {
            _showingResult = false;
            _titleText.text = title;
            _infoText.text = info;
            _sendButton.gameObject.SetActive(showSend);
            _root.SetActive(true);
            ResetScroll();
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
            ResetScroll();
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

        // After the text changes, size the content to the text's
        // preferred height (plus padding) so the ScrollRect knows the
        // true content size, then show the top of the text.
        private void ResetScroll()
        {
            if (_scroll == null || _contentRt == null || _infoText == null) return;
            Canvas.ForceUpdateCanvases();
            float height = _infoText.preferredHeight + 12f;
            _contentRt.sizeDelta = new Vector2(_contentRt.sizeDelta.x, height);
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
        }
    }
}
