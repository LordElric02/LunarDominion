using System;
using LunarDominion.Accounts;
using LunarDominion.Core;
using LunarDominion.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Top resource bar. Display only — it reads GameState, never
    /// computes costs, production, or energy rules.</summary>
    public class TopBarView : MonoBehaviour
    {
        private Text _nameText;
        private GameObject _editorRoot;
        private InputField _nameInput;
        private Text _editorHint;
        private Text _biomassText;
        private Text _alloyText;
        private Text _heliumText;
        private Text _energyText;
        private Button _inboxButton;
        private GameController _controller;

        // Inbox (2026-11-15): the button sits right of the energy
        // chip. The Bootstrap owns the Inbox service and wires both
        // members; the unread count is pulled at Refresh time (which
        // already runs on every state change).
        public event Action OnInboxClicked;
        public Func<int> UnreadCountProvider;

        // Account panel (2026-11-25): the Bootstrap hands over
        // its AccountManager after Build; the panel itself is
        // built lazily on first open from the name editor's
        // ACCOUNT button, so ordering never matters.
        public AccountManager AccountManager { get; set; }
        private Canvas _canvas;
        private AccountPanelView _accountPanel;

        public void Build(Canvas canvas, GameController controller)
        {
            _controller = controller;
            _canvas = canvas;
            var safe = SafeArea.Get(canvas);
            // Transparent background (Patrick 2026-11-14): the navy
            // band is gone — the level/resource text overlays the 3D
            // view directly. The panel object stays (the Map and the
            // boss fight find "TopBar" by name to hide it), but its
            // image is fully transparent and non-raycasting, so the
            // invisible strip can't eat taps meant for the scene.
            var bar = UIHelpers.Panel(canvas.gameObject, "TopBar", new Color(0f, 0f, 0f, 0f),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -140 - safe.Top), new Vector2(0, -safe.Top));
            var barImage = bar.GetComponent<Image>();
            if (barImage != null) barImage.raycastTarget = false;

            // Left block (2026-11-07; username 2026-11-24): one
            // line, "Username (Level)" — Patrick's format. The
            // level is derived from progression (see
            // PlayerLevelService). A transparent tap target under
            // the label opens the name editor; the label itself
            // doesn't raycast, so taps land on the button. The
            // name is the local half of the coming Firebase
            // account (its display name).
            var nameTap = UIHelpers.Panel(bar, "NameTap", new Color(0f, 0f, 0f, 0f),
                new Vector2(0.005f, 0.02f), new Vector2(0.245f, 0.98f),
                Vector2.zero, Vector2.zero);
            var nameBtn = nameTap.AddComponent<Button>();
            nameBtn.targetGraphic = nameTap.GetComponent<Image>();
            nameBtn.onClick.AddListener(OpenNameEditor);
            _nameText = UIHelpers.LabelInRect(bar, "Username", "", 28,
                Color.white, new Rect(0.01f, 0.05f, 0.24f, 0.95f), TextAnchor.MiddleCenter);
            _nameText.fontStyle = FontStyle.Bold;
            _nameText.raycastTarget = false;

            // Four chips share the remaining 75% in one row (18.75%
            // each, ~360px per chip — the longest chip, "ALLOY
            // 1,389,000", is ~250px at font 30). The PWR chip was
            // removed 2026-11-07 — power may be shown on a player
            // profile page later, not on this screen.
            _biomassText = UIHelpers.LabelInRect(bar, "Biomass", "", 30,
                UIHelpers.Hex("#7ED957"), new Rect(0.24f, 0.05f, 0.4275f, 0.95f), TextAnchor.MiddleCenter);
            _alloyText = UIHelpers.LabelInRect(bar, "Alloy", "", 30,
                UIHelpers.Hex("#C0C8D0"), new Rect(0.4275f, 0.05f, 0.615f, 0.95f), TextAnchor.MiddleCenter);
            _heliumText = UIHelpers.LabelInRect(bar, "Helium", "", 30,
                UIHelpers.Hex("#5AD1E6"), new Rect(0.615f, 0.05f, 0.8025f, 0.95f), TextAnchor.MiddleCenter);
            _energyText = UIHelpers.LabelInRect(bar, "Energy", "", 30,
                UIHelpers.Hex("#FFD166"), new Rect(0.8025f, 0.05f, 0.895f, 0.95f), TextAnchor.MiddleCenter);
            // Inbox button, right of the energy chip (Patrick
            // 2026-11-15). The label carries the unread count when
            // there is one; Refresh keeps it current.
            _inboxButton = UIHelpers.Button(bar, "Inbox", "INBOX", 26,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var inboxRt = _inboxButton.GetComponent<RectTransform>();
            inboxRt.anchorMin = new Vector2(0.90f, 0.14f);
            inboxRt.anchorMax = new Vector2(0.99f, 0.86f);
            inboxRt.offsetMin = Vector2.zero;
            inboxRt.offsetMax = Vector2.zero;
            _inboxButton.onClick.AddListener(() => OnInboxClicked?.Invoke());
            BuildNameEditor(canvas);
            Refresh();
        }

        // The name editor: a centred card over a dim backdrop,
        // built once and toggled. It lives on the canvas (not the
        // bar), so it overlays every tab the bar shows on.
        private void BuildNameEditor(Canvas canvas)
        {
            _editorRoot = UIHelpers.Panel(canvas.gameObject, "UsernameEditor",
                new Color(0f, 0f, 0f, 0.55f), new Vector2(0, 0), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            var card = UIHelpers.Panel(_editorRoot, "Card", UIHelpers.Hex("#1D2A3D"),
                new Vector2(0.32f, 0.36f), new Vector2(0.68f, 0.64f),
                Vector2.zero, Vector2.zero);
            UIHelpers.LabelInRect(card, "Title", "USERNAME", 30, Color.white,
                new Rect(0.05f, 0.70f, 0.95f, 0.96f), TextAnchor.MiddleCenter);

            var inputGo = new GameObject("NameInput");
            inputGo.transform.SetParent(card.transform, false);
            var inputImg = inputGo.AddComponent<Image>();
            inputImg.color = UIHelpers.Hex("#101A29");
            var inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchorMin = new Vector2(0.08f, 0.47f);
            inputRt.anchorMax = new Vector2(0.92f, 0.67f);
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = Vector2.zero;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(inputGo.transform, false);
            var inputText = textGo.AddComponent<Text>();
            inputText.font = UIHelpers.DefaultFont;
            inputText.fontSize = 28;
            inputText.color = Color.white;
            inputText.alignment = TextAnchor.MiddleLeft;
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(12, 0);
            textRt.offsetMax = new Vector2(-12, 0);

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(inputGo.transform, false);
            var phText = phGo.AddComponent<Text>();
            phText.font = UIHelpers.DefaultFont;
            phText.fontSize = 28;
            phText.color = new Color(1f, 1f, 1f, 0.35f);
            phText.alignment = TextAnchor.MiddleLeft;
            phText.text = "Commander";
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(12, 0);
            phRt.offsetMax = new Vector2(-12, 0);

            _nameInput = inputGo.AddComponent<InputField>();
            _nameInput.textComponent = inputText;
            _nameInput.placeholder = phText;
            _nameInput.targetGraphic = inputImg;
            _nameInput.characterLimit = 16;

            _editorHint = UIHelpers.LabelInRect(card, "Hint", "", 20,
                new Color(1f, 0.62f, 0.62f), new Rect(0.05f, 0.35f, 0.95f, 0.45f),
                TextAnchor.MiddleCenter);
            _editorHint.raycastTarget = false;

            var accountBtn = UIHelpers.Button(card, "Account", "ACCOUNT", 26,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var accountRt = accountBtn.GetComponent<RectTransform>();
            accountRt.anchorMin = new Vector2(0.05f, 0.21f);
            accountRt.anchorMax = new Vector2(0.95f, 0.33f);
            accountRt.offsetMin = Vector2.zero;
            accountRt.offsetMax = Vector2.zero;
            accountBtn.onClick.AddListener(OpenAccountPanel);

            var cancelBtn = UIHelpers.Button(card, "CancelName", "CANCEL", 26,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var cancelRt = cancelBtn.GetComponent<RectTransform>();
            cancelRt.anchorMin = new Vector2(0.05f, 0.04f);
            cancelRt.anchorMax = new Vector2(0.48f, 0.19f);
            cancelRt.offsetMin = Vector2.zero;
            cancelRt.offsetMax = Vector2.zero;
            cancelBtn.onClick.AddListener(CloseNameEditor);

            var saveBtn = UIHelpers.Button(card, "SaveName", "SAVE", 26,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var saveRt = saveBtn.GetComponent<RectTransform>();
            saveRt.anchorMin = new Vector2(0.52f, 0.04f);
            saveRt.anchorMax = new Vector2(0.95f, 0.19f);
            saveRt.offsetMin = Vector2.zero;
            saveRt.offsetMax = Vector2.zero;
            saveBtn.onClick.AddListener(SaveNameEditor);

            _editorRoot.SetActive(false);
        }

        private void OpenNameEditor()
        {
            if (_editorRoot == null || _controller == null) return;
            _editorHint.text = "";
            _nameInput.text = _controller.State.Username ?? "";
            _editorRoot.SetActive(true);
            _nameInput.ActivateInputField();
        }

        private void CloseNameEditor()
        {
            if (_editorRoot != null) _editorRoot.SetActive(false);
        }

        private void OpenAccountPanel()
        {
            if (AccountManager == null || _canvas == null) return;
            if (_accountPanel == null)
            {
                var go = new GameObject("AccountPanelView");
                _accountPanel = go.AddComponent<AccountPanelView>();
                _accountPanel.Build(_canvas, AccountManager);
            }
            CloseNameEditor();
            _accountPanel.Show();
        }

        private void SaveNameEditor()
        {
            var name = (_nameInput.text ?? "").Trim();
            if (name.Length == 0)
            {
                _editorHint.text = "Name can't be empty.";
                return;
            }
            _controller.State.Username = name;
            _controller.SaveGame();
            _controller.NotifyChanged();
            CloseNameEditor();
            Refresh();
        }

        public void Refresh()
        {
            if (_controller == null || _biomassText == null) return;
            var s = _controller.State;
            _biomassText.text = $"BIO {s.Biomass}";
            _alloyText.text = $"ALLOY {s.Alloy}";
            _heliumText.text = $"He-3 {s.Helium3}";
            _energyText.text = $"NRG {s.Energy}";
            // Refresh() already runs on every OnStateChanged (Bootstrap
            // RefreshAll, once a second plus every NotifyChanged), so
            // the level updates after upgrades and research
            // completions with no new event.
            if (_nameText != null)
            {
                int level = _controller.PlayerLevel != null ? _controller.PlayerLevel.Level() : 0;
                _nameText.text = $"{s.EffectiveUsername} ({level})";
            }
            if (_inboxButton != null)
            {
                int unread = UnreadCountProvider != null ? UnreadCountProvider() : 0;
                var t = _inboxButton.GetComponentInChildren<Text>();
                if (t != null) t.text = unread > 0 ? $"INBOX ({unread})" : "INBOX";
                var img = _inboxButton.GetComponent<Image>();
                if (img != null) img.color = unread > 0 ? UIHelpers.Hex("#8A6D1F") : UIHelpers.Hex("#2A3B52");
            }
        }
    }
}
