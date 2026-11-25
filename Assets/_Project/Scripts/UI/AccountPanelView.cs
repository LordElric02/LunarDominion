using LunarDominion.Accounts;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Account panel (2026-11-25): the in-game face of
    /// the account layer. Opened from the TopBar name editor.
    /// Guest-first, per the Firebase design: the player is
    /// already signed in anonymously, so this panel is about
    /// ATTACHING an identity (link email) or returning to one
    /// (sign in) — never a wall. Linking keeps the same UID, so
    /// the save and leaderboard totals are untouched. Sign-in
    /// runs the AccountManager's post-sign-in sync, so a newer
    /// cloud save stages in for next launch (newest-wins).
    /// Google/Apple buttons arrive with their native plugins;
    /// the service already accepts their tokens.
    ///
    /// Built lazily by TopBarView on first open, following the
    /// name editor's construction pattern (dim backdrop + card,
    /// code-built InputFields on UIHelpers.DefaultFont).</summary>
    public class AccountPanelView : MonoBehaviour
    {
        private AccountManager _manager;
        private GameObject _root;
        private Text _statusText;
        private Text _hintText;
        private InputField _emailInput;
        private InputField _passwordInput;
        private Button _linkButton;
        private Button _signInButton;
        private Button _forgotButton;
        private Button _signOutButton;
        private Button _useCloudButton;
        private Button _keepLocalButton;
        private bool _busy;
        private bool _stagedSaveSeen;

        private static readonly Color HintOk = new Color(0.55f, 0.95f, 0.55f);
        private static readonly Color HintBad = new Color(1f, 0.62f, 0.62f);

        public void Build(Canvas canvas, AccountManager manager)
        {
            _manager = manager;

            _root = UIHelpers.Panel(canvas.gameObject, "AccountPanel",
                new Color(0f, 0f, 0f, 0.55f), new Vector2(0, 0), new Vector2(1, 1),
                Vector2.zero, Vector2.zero);
            var card = UIHelpers.Panel(_root, "Card", UIHelpers.Hex("#1D2A3D"),
                new Vector2(0.30f, 0.20f), new Vector2(0.70f, 0.80f),
                Vector2.zero, Vector2.zero);

            UIHelpers.LabelInRect(card, "Title", "ACCOUNT", 30, Color.white,
                new Rect(0.05f, 0.86f, 0.95f, 0.97f), TextAnchor.MiddleCenter);

            _statusText = UIHelpers.LabelInRect(card, "Status", "", 22, Color.white,
                new Rect(0.06f, 0.665f, 0.94f, 0.855f), TextAnchor.MiddleCenter);
            _statusText.raycastTarget = false;

            _emailInput = BuildInput(card, "EmailInput", "Email address",
                new Vector2(0.08f, 0.545f), new Vector2(0.92f, 0.645f),
                InputField.ContentType.EmailAddress);
            _passwordInput = BuildInput(card, "PasswordInput", "Password (6+ characters)",
                new Vector2(0.08f, 0.425f), new Vector2(0.92f, 0.525f),
                InputField.ContentType.Password);

            _hintText = UIHelpers.LabelInRect(card, "Hint", "", 20,
                HintBad, new Rect(0.06f, 0.315f, 0.94f, 0.415f),
                TextAnchor.MiddleCenter);
            _hintText.raycastTarget = false;

            _linkButton = AddButton(card, "LinkEmail", "LINK EMAIL",
                new Vector2(0.05f, 0.19f), new Vector2(0.485f, 0.30f), OnLinkClicked);
            _signInButton = AddButton(card, "SignIn", "SIGN IN",
                new Vector2(0.515f, 0.19f), new Vector2(0.95f, 0.30f), OnSignInClicked);
            _signOutButton = AddButton(card, "SignOut", "SIGN OUT",
                new Vector2(0.05f, 0.19f), new Vector2(0.95f, 0.30f), OnSignOutClicked);
            _useCloudButton = AddButton(card, "UseAccountSave", "USE ACCOUNT SAVE",
                new Vector2(0.05f, 0.19f), new Vector2(0.485f, 0.30f), OnUseCloudClicked);
            _keepLocalButton = AddButton(card, "KeepThisSave", "KEEP THIS SAVE",
                new Vector2(0.515f, 0.19f), new Vector2(0.95f, 0.30f), OnKeepLocalClicked);
            _forgotButton = AddButton(card, "Forgot", "FORGOT PASSWORD",
                new Vector2(0.05f, 0.05f), new Vector2(0.485f, 0.16f), OnForgotClicked);
            AddButton(card, "Close", "CLOSE",
                new Vector2(0.515f, 0.05f), new Vector2(0.95f, 0.16f), Hide);

            if (_manager != null && _manager.Accounts != null)
                _manager.Accounts.OnStatusChanged += RefreshPanel;
            if (_manager != null)
            {
                _manager.OnCloudSaveStaged += () => { _stagedSaveSeen = true; };
                _manager.OnSaveConflict += RefreshPanel;
            }

            _root.SetActive(false);
        }

        private static InputField BuildInput(GameObject parent, string name, string placeholder,
            Vector2 anchorMin, Vector2 anchorMax, InputField.ContentType contentType)
        {
            var inputGo = new GameObject(name);
            inputGo.transform.SetParent(parent.transform, false);
            var inputImg = inputGo.AddComponent<Image>();
            inputImg.color = UIHelpers.Hex("#101A29");
            var inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchorMin = anchorMin;
            inputRt.anchorMax = anchorMax;
            inputRt.offsetMin = Vector2.zero;
            inputRt.offsetMax = Vector2.zero;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(inputGo.transform, false);
            var inputText = textGo.AddComponent<Text>();
            inputText.font = UIHelpers.DefaultFont;
            inputText.fontSize = 26;
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
            phText.fontSize = 26;
            phText.color = new Color(1f, 1f, 1f, 0.35f);
            phText.alignment = TextAnchor.MiddleLeft;
            phText.text = placeholder;
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(12, 0);
            phRt.offsetMax = new Vector2(-12, 0);

            var field = inputGo.AddComponent<InputField>();
            field.textComponent = inputText;
            field.placeholder = phText;
            field.targetGraphic = inputImg;
            field.contentType = contentType;
            field.characterLimit = 64;
            return field;
        }

        private static Button AddButton(GameObject parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick)
        {
            var btn = UIHelpers.Button(parent, name, label, 24,
                UIHelpers.Hex("#2A3B52"), Color.white);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            btn.onClick.AddListener(onClick);
            return btn;
        }

        public void Show()
        {
            if (_root == null) return;
            _hintText.text = "";
            _passwordInput.text = "";
            _stagedSaveSeen = false;
            RefreshPanel();
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void RefreshPanel()
        {
            if (_statusText == null || _manager == null || _manager.Accounts == null) return;
            var accounts = _manager.Accounts;
            var status = accounts.Status;

            bool guestOrOut = status == AccountStatus.SignedInGuest || status == AccountStatus.SignedOut;
            bool linked = status == AccountStatus.SignedInLinked;
            // Merge safeguard: while a save conflict is pending,
            // the panel is the choice screen — the credential
            // and sign-out controls step aside until the player
            // picks which save the account keeps.
            bool conflict = _manager.HasPendingConflict;
            SetVisible(_emailInput.gameObject, guestOrOut && !conflict);
            SetVisible(_passwordInput.gameObject, guestOrOut && !conflict);
            SetVisible(_linkButton.gameObject, guestOrOut && !conflict);
            SetVisible(_signInButton.gameObject, guestOrOut && !conflict);
            SetVisible(_signOutButton.gameObject, linked && !conflict);
            SetVisible(_forgotButton.gameObject, (guestOrOut || linked) && !conflict);
            SetVisible(_useCloudButton.gameObject, conflict);
            SetVisible(_keepLocalButton.gameObject, conflict);

            if (conflict)
            {
                var local = _manager.PendingLocalSave;
                var cloud = _manager.PendingCloudSave;
                _statusText.text =
                    "This device and your account have different saves.\n" +
                    $"This device: {local.Name} — Nexus {local.NexusLevel}\n" +
                    $"Account: {cloud.Name} — Nexus {cloud.NexusLevel}\n" +
                    "Which save should your account keep?";
                return;
            }

            switch (status)
            {
                case AccountStatus.LocalOnly:
                    _statusText.text = "Playing locally — accounts are unavailable in this build.";
                    break;
                case AccountStatus.SigningIn:
                    _statusText.text = "Signing in…";
                    break;
                case AccountStatus.SignedOut:
                    _statusText.text = "Signed out. Sign in with your email — or just close this and keep playing as a guest.";
                    break;
                case AccountStatus.SignedInGuest:
                    _statusText.text = "Guest account\nYour progress saves to the cloud on this device.\nLink an email to use the same account everywhere.";
                    break;
                case AccountStatus.SignedInLinked:
                    string email = accounts.Email;
                    string line = string.IsNullOrEmpty(email) ? "Signed in." : $"Signed in: {email}";
                    if (accounts.Role != AccountRole.Player)
                        line += $"\nRole: {accounts.Role}";
                    _statusText.text = line;
                    break;
            }
        }

        private static void SetVisible(GameObject go, bool visible)
        {
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
        }

        private void SetHint(string message, bool ok)
        {
            _hintText.color = ok ? HintOk : HintBad;
            _hintText.text = message;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            bool enabled = !busy;
            if (_linkButton != null) _linkButton.interactable = enabled;
            if (_signInButton != null) _signInButton.interactable = enabled;
            if (_forgotButton != null) _forgotButton.interactable = enabled;
            if (_signOutButton != null) _signOutButton.interactable = enabled;
            if (_useCloudButton != null) _useCloudButton.interactable = enabled;
            if (_keepLocalButton != null) _keepLocalButton.interactable = enabled;
        }

        private bool ValidateCredentials(out string email, out string password, bool requirePasswordLength)
        {
            email = (_emailInput.text ?? "").Trim();
            password = _passwordInput.text ?? "";
            if (email.Length == 0 || !email.Contains("@"))
            {
                SetHint("Enter a valid email address.", false);
                return false;
            }
            if (password.Length == 0 || (requirePasswordLength && password.Length < 6))
            {
                SetHint("Password must be at least 6 characters.", false);
                return false;
            }
            return true;
        }

        private async void OnLinkClicked()
        {
            if (_busy || _manager == null) return;
            if (!ValidateCredentials(out var email, out var password, true)) return;
            SetBusy(true);
            bool ok = false;
            try { ok = await _manager.Accounts.LinkEmailAsync(email, password); }
            catch { ok = false; }
            SetBusy(false);
            if (ok)
            {
                SetHint("Email linked — this account now signs in with that email.", true);
                _passwordInput.text = "";
            }
            else
            {
                SetHint("That didn't work — the email may already be registered. Try signing in instead.", false);
            }
            RefreshPanel();
        }

        private async void OnSignInClicked()
        {
            if (_busy || _manager == null) return;
            if (!ValidateCredentials(out var email, out var password, false)) return;
            SetBusy(true);
            bool ok = false;
            try { ok = await _manager.Accounts.SignInEmailAsync(email, password); }
            catch { ok = false; }
            if (ok)
            {
                // Post-sign-in sync: profile push + newest-wins
                // save staging, exactly as at launch.
                _stagedSaveSeen = false;
                try { await _manager.OnSignedInAsync(); }
                catch { /* the manager logs its own failures */ }
            }
            SetBusy(false);
            if (ok)
            {
                SetHint(_stagedSaveSeen
                    ? "Signed in. A newer cloud save from your account loads on next launch."
                    : "Signed in — welcome back.", true);
                _passwordInput.text = "";
            }
            else
            {
                SetHint("Sign-in failed — check the email and password.", false);
            }
            RefreshPanel();
        }

        private void OnUseCloudClicked()
        {
            if (_busy || _manager == null) return;
            _manager.ResolveConflictUseCloud();
            SetHint("Account save chosen — restart the app and it loads.", true);
            RefreshPanel();
        }

        private async void OnKeepLocalClicked()
        {
            if (_busy || _manager == null) return;
            SetBusy(true);
            bool ok = false;
            try { ok = await _manager.ResolveConflictKeepLocalAsync(); }
            catch { ok = false; }
            SetBusy(false);
            SetHint(ok
                ? "Done — this device's save is now your account save."
                : "Couldn't reach your account — check your connection and try again.", ok);
            RefreshPanel();
        }

        private async void OnForgotClicked()
        {
            if (_busy || _manager == null) return;
            string email = (_emailInput.text ?? "").Trim();
            if (email.Length == 0 || !email.Contains("@"))
            {
                SetHint("Enter your email address first.", false);
                return;
            }
            SetBusy(true);
            bool ok = false;
            try { ok = await _manager.Accounts.SendPasswordResetAsync(email); }
            catch { ok = false; }
            SetBusy(false);
            SetHint(ok
                ? "Password reset email sent — check your inbox."
                : "Couldn't send the reset email — check the address.", ok);
        }

        private async void OnSignOutClicked()
        {
            if (_busy || _manager == null) return;
            SetBusy(true);
            try
            {
                await _manager.Accounts.SignOutAsync();
                // Guest-first: never leave the player account-less.
                // The fresh guest adopts the local save via the
                // manager's normal migration path.
                if (await _manager.Accounts.SignInGuestAsync())
                    await _manager.OnSignedInAsync();
            }
            catch { /* the services log their own failures */ }
            SetBusy(false);
            SetHint("Signed out — you're now playing as a guest on this device.", true);
            RefreshPanel();
        }
    }
}
