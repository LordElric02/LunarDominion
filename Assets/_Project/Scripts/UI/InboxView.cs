using System;
using System.Collections.Generic;
using System.Globalization;
using LunarDominion.Core;
using LunarDominion.Inbox;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>The Inbox panel (2026-11-15, Patrick): game statuses
    /// delivered as messages instead of pop-ups. Two modes: a
    /// scrollable list (newest first, unread highlighted with a dot)
    /// filling the panel, and a reading mode — tapping a message
    /// marks it read, hides the list, and gives the message the
    /// whole content area, with a BACK TO LIST button to return.
    /// Opened from the top bar's Inbox button; the badge count
    /// lives in TopBarView.</summary>
    public class InboxView : MonoBehaviour
    {
        private GameObject _root;
        private Text _summaryText;
        private GameObject _listScrollGo;
        private GameObject _rowsContent;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private Text _detailTitle;
        private Text _detailText;
        private ScrollRect _detailScroll;
        private GameObject _detailScrollGo;
        private RectTransform _detailContentRt;
        private Button _markAllButton;
        private Button _backButton;
        private InboxService _inbox;
        private long _selectedId = -1;
        // Two modes (Patrick 2026-11-15): the list fills the panel;
        // opening a message hides the list and the message takes
        // over the whole content area, with a BACK button to return.
        private bool _reading;

        public void Build(Canvas canvas, InboxService inbox)
        {
            _inbox = inbox;
            var safe = SafeArea.Get(canvas);
            // Same panel geometry as the Heroes panel, so the
            // management screens share one shape.
            _root = UIHelpers.Panel(canvas.gameObject, "InboxPanel", UIHelpers.Hex("#16202F"),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(20, 200 + safe.Bottom), new Vector2(-20, 920 - safe.Top));
            UIHelpers.LabelInRect(_root, "Title", "INBOX", 36, Color.white,
                new Rect(0.04f, 0.90f, 0.96f, 0.98f), TextAnchor.MiddleCenter);
            _summaryText = UIHelpers.LabelInRect(_root, "Summary", "", 26, UIHelpers.Hex("#C0C8D0"),
                new Rect(0.04f, 0.83f, 0.96f, 0.90f), TextAnchor.MiddleCenter);

            BuildRowsScroll();
            BuildDetail();
            ApplyMode();

            _markAllButton = UIHelpers.Button(_root, "MarkAllRead", "MARK ALL READ", 24,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var markRt = _markAllButton.GetComponent<RectTransform>();
            markRt.anchorMin = new Vector2(0.04f, 0.012f);
            markRt.anchorMax = new Vector2(0.49f, 0.062f);
            markRt.offsetMin = Vector2.zero;
            markRt.offsetMax = Vector2.zero;
            _markAllButton.onClick.AddListener(() => _inbox?.MarkAllRead());

            // BACK shares the MARK ALL READ slot — only one of them
            // is visible at a time (list mode vs reading mode).
            _backButton = UIHelpers.Button(_root, "BackToList", "BACK TO LIST", 24,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var backRt = _backButton.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0.04f, 0.012f);
            backRt.anchorMax = new Vector2(0.49f, 0.062f);
            backRt.offsetMin = Vector2.zero;
            backRt.offsetMax = Vector2.zero;
            _backButton.onClick.AddListener(BackToList);

            var close = UIHelpers.Button(_root, "CloseInbox", "CLOSE", 24,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var closeRt = close.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(0.51f, 0.012f);
            closeRt.anchorMax = new Vector2(0.96f, 0.062f);
            closeRt.offsetMin = Vector2.zero;
            closeRt.offsetMax = Vector2.zero;
            close.onClick.AddListener(Hide);

            ApplyMode();
            _root.SetActive(false);
        }

        // Message list (HeroesView scroll pattern): a vertical
        // ScrollRect whose content is a layout group; rows are
        // rebuilt on every Refresh.
        private void BuildRowsScroll()
        {
            var scrollGo = new GameObject("RowsScroll");
            _listScrollGo = scrollGo;
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            // The list owns the whole content area; reading a
            // message hides it entirely (ApplyMode).
            scrollRt.anchorMin = new Vector2(0.02f, 0.075f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.82f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = UIHelpers.Hex("#0F1724");
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = UIHelpers.Hex("#0F1724");
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = viewportRt;

            _rowsContent = new GameObject("Content");
            _rowsContent.transform.SetParent(viewportGo.transform, false);
            var contentRt = _rowsContent.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = Vector2.zero;
            var layout = _rowsContent.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _rowsContent.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            scroll.content = contentRt;
        }

        // Detail reader (MapInfoView scroll pattern): one wrapping
        // Text in a ScrollRect; the content height is set from the
        // text's preferred height whenever the selection changes.
        private void BuildDetail()
        {
            // Reading mode: the message title sits under the summary
            // and the body scroll owns the rest of the panel.
            _detailTitle = UIHelpers.LabelInRect(_root, "DetailTitle", "Select a message", 30, Color.white,
                new Rect(0.04f, 0.735f, 0.96f, 0.815f), TextAnchor.MiddleLeft);

            var scrollGo = new GameObject("DetailScroll");
            _detailScrollGo = scrollGo;
            scrollGo.transform.SetParent(_root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.02f, 0.075f);
            scrollRt.anchorMax = new Vector2(0.98f, 0.72f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            var scrollBg = scrollGo.AddComponent<Image>();
            scrollBg.color = UIHelpers.Hex("#0F1724");
            _detailScroll = scrollGo.AddComponent<ScrollRect>();
            _detailScroll.horizontal = false;
            _detailScroll.vertical = true;
            _detailScroll.movementType = ScrollRect.MovementType.Clamped;

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
            _detailScroll.viewport = viewportRt;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _detailContentRt = contentGo.AddComponent<RectTransform>();
            _detailContentRt.anchorMin = new Vector2(0, 1);
            _detailContentRt.anchorMax = new Vector2(1, 1);
            _detailContentRt.pivot = new Vector2(0.5f, 1);
            _detailContentRt.anchoredPosition = Vector2.zero;
            _detailContentRt.sizeDelta = Vector2.zero;
            _detailScroll.content = _detailContentRt;

            _detailText = UIHelpers.LabelInRect(contentGo, "Detail", "", 25, UIHelpers.Hex("#C0C8D0"),
                new Rect(0, 0, 1, 1), TextAnchor.UpperLeft);
            var textRt = _detailText.GetComponent<RectTransform>();
            textRt.offsetMin = new Vector2(8, 6);
            textRt.offsetMax = new Vector2(-8, -6);

            var barGo = new GameObject("DetailScrollbar");
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
            _detailScroll.verticalScrollbar = scrollbar;
            _detailScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        public void Show()
        {
            // Always open on the list.
            _reading = false;
            ApplyMode();
            _root.SetActive(true);
            Refresh();
        }

        private void BackToList()
        {
            _reading = false;
            ApplyMode();
            Refresh();
        }

        // Toggle the list vs the message reader. The list scroll
        // keeps its full-area anchors either way — in reading mode
        // it is simply inactive.
        private void ApplyMode()
        {
            if (_listScrollGo != null) _listScrollGo.SetActive(!_reading);
            if (_detailTitle != null) _detailTitle.gameObject.SetActive(_reading);
            if (_detailScrollGo != null) _detailScrollGo.SetActive(_reading);
            if (_markAllButton != null) _markAllButton.gameObject.SetActive(!_reading);
            if (_backButton != null) _backButton.gameObject.SetActive(_reading);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public bool IsShowing => _root != null && _root.activeSelf;

        public void Refresh()
        {
            if (_root == null || !_root.activeSelf || _inbox == null) return;
            var messages = _inbox.Messages;
            int total = messages != null ? messages.Count : 0;
            int unread = _inbox.UnreadCount;
            _summaryText.text = total == 0
                ? "No messages yet."
                : unread > 0 ? $"{total} messages — {unread} unread" : $"{total} messages — all read";

            foreach (var row in _rows) if (row != null) Destroy(row);
            _rows.Clear();
            if (messages != null)
            {
                // Newest first; the stored list is chronological.
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    var m = messages[i];
                    if (m != null) _rows.Add(BuildRow(m));
                }
            }
            RenderDetail();
        }

        private GameObject BuildRow(InboxMessage m)
        {
            var row = new GameObject("MessageRow");
            row.transform.SetParent(_rowsContent.transform, false);
            var img = row.AddComponent<Image>();
            img.color = m.Id == _selectedId ? UIHelpers.Hex("#24344E")
                : !m.Read ? UIHelpers.Hex("#1D2F47") : UIHelpers.Hex("#141E2E");
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = img;
            var layoutEl = row.AddComponent<LayoutElement>();
            layoutEl.preferredHeight = 96;
            long id = m.Id;
            btn.onClick.AddListener(() => Select(id));

            if (!m.Read)
            {
                var dot = UIHelpers.Panel(row, "UnreadDot", UIHelpers.Hex("#FFD166"),
                    new Vector2(0.012f, 0.40f), new Vector2(0.038f, 0.66f), Vector2.zero, Vector2.zero);
                dot.GetComponent<Image>().raycastTarget = false;
            }
            var title = UIHelpers.LabelInRect(row, "Title", m.Title, 26,
                !m.Read ? Color.white : UIHelpers.Hex("#9FB0C3"),
                new Rect(0.055f, 0.34f, 0.76f, 0.95f), TextAnchor.MiddleLeft);
            title.raycastTarget = false;
            var time = UIHelpers.LabelInRect(row, "Time", FormatTime(m.Utc), 22,
                UIHelpers.Hex("#7E93AB"), new Rect(0.77f, 0.34f, 0.985f, 0.95f), TextAnchor.MiddleRight);
            time.raycastTarget = false;
            var preview = UIHelpers.LabelInRect(row, "Preview", FirstLine(m.Body), 22,
                UIHelpers.Hex("#7E93AB"), new Rect(0.055f, 0.05f, 0.985f, 0.34f), TextAnchor.MiddleLeft);
            preview.raycastTarget = false;
            return row;
        }

        private void Select(long id)
        {
            _selectedId = id;
            // Opening a message switches to reading mode: the list
            // hides and the message takes over the panel.
            _reading = true;
            ApplyMode();
            // Marking read raises OnChanged, which refreshes this
            // view (and the top bar badge) through the Bootstrap.
            _inbox?.MarkRead(id);
            RenderDetail();
            Refresh();
        }

        private void RenderDetail()
        {
            var messages = _inbox != null ? _inbox.Messages : null;
            InboxMessage found = null;
            if (messages != null)
            {
                foreach (var m in messages)
                    if (m != null && m.Id == _selectedId) { found = m; break; }
                if (found == null && messages.Count > 0)
                {
                    // Nothing selected yet (or it was trimmed): show
                    // the newest message without selecting it.
                    found = messages[messages.Count - 1];
                }
            }
            if (found == null)
            {
                _detailTitle.text = "Select a message";
                _detailText.text = "";
            }
            else
            {
                _detailTitle.text = found.Title;
                _detailText.text = found.Body;
            }
            ResetDetailScroll();
        }

        private void ResetDetailScroll()
        {
            if (_detailText == null || _detailScroll == null) return;
            float viewportH = _detailScroll.viewport != null ? _detailScroll.viewport.rect.height : 0f;
            float h = Mathf.Max(viewportH, _detailText.preferredHeight + 14f);
            _detailContentRt.sizeDelta = new Vector2(0f, h);
            _detailScroll.verticalNormalizedPosition = 1f;
        }

        private static string FirstLine(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            int cut = body.IndexOf('\n');
            return cut >= 0 ? body.Substring(0, cut) : body;
        }

        private static string FormatTime(string utc)
        {
            if (string.IsNullOrEmpty(utc)) return "";
            if (!DateTime.TryParse(utc, null, DateTimeStyles.RoundtripKind, out var t)) return "";
            var local = t.ToLocalTime();
            return local.Date == DateTime.Now.Date
                ? local.ToString("HH:mm")
                : local.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);
        }
    }
}
