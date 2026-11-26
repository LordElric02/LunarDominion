using LunarDominion.Accounts;
using UnityEngine;
using UnityEngine.UI;

namespace LunarDominion.UI
{
    /// <summary>Account panel (2026-11-25): the in-game face of
    /// the account layer. Opened from the TopBar name editor.
    /// Guest-first, per the Firebase design: the player is
    /// already signed in anonymously, so this panel is never a
    /// wall.
    ///
    /// ONE-TO-ONE BINDING (Patrick 2026-11-25, supersedes the
    /// linking design): an account takes exactly ONE sign-in —
    /// an email password, Google, or (iOS phase) Apple — chosen
    /// when the guest binds, and it can never take another.
    /// The panel therefore offers the bind choices ONLY to an
    /// unbound guest (BIND EMAIL, or CONTINUE WITH GOOGLE which
    /// binds Google to the guest — or signs into the existing
    /// Google-bound account if that identity already owns one,
    /// with the save safeguard arbitrating). A bound account
    /// shows its door ("Sign-in: …") and gets SIGN OUT and
    /// DELETE ACCOUNT — deleting and starting over is the only
    /// way to a different door. Sign-in runs the
    /// AccountManager's post-sign-in sync, so a newer cloud
    /// save stages in for next launch (newest-wins). The
    /// Google button rides on GoogleSignInBridge and only
    /// exists in GOOGLE_SIGNIN builds. The Apple button
    /// arrives with the iOS phase.
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
        private Button _bindButton;
        private Button _signInButton;
        private Button _googleButton;
        private Button _deleteButton;
        private Button _forgotButton;
        private Button _signOutButton;
        private Button _useCloudButton;
        private bool _busy;
        private bool _stagedSaveSeen;
        private bool _deleteArmed;

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
                new Vector2(0.08f, 0.565f), new Vector2(0.92f, 0.655f),
                InputField.ContentType.EmailAddress);
            _passwordInput = BuildInput(card, "PasswordInput", "Password (6+ characters)",
                new Vector2(0.08f, 0.455f), new Vector2(0.92f, 0.545f),
                InputField.ContentType.Password);

            _hintText = UIHelpers.LabelInRect(card, "Hint", "", 19,
                HintBad, new Rect(0.06f, 0.365f, 0.94f, 0.445f),
                TextAnchor.MiddleCenter);
            _hintText.raycastTarget = false;

            _googleButton = AddButton(card, "Google", "CONTINUE WITH GOOGLE",
                new Vector2(0.05f, 0.255f), new Vector2(0.95f, 0.355f), OnGoogleClicked);
            // Same slot as the Google button: the two never
            // coexist (Google is a guest's bind choice, Delete
            // belongs to a bound account).
            _deleteButton = AddButton(card, "DeleteAccount", "DELETE ACCOUNT",
                new Vector2(0.05f, 0.255f), new Vector2(0.95f, 0.355f), OnDeleteClicked);
            _bindButton = AddButton(card, "BindEmail", "BIND EMAIL",
                new Vector2(0.05f, 0.135f), new Vector2(0.485f, 0.235f), OnBindEmailClicked);
            _signInButton = AddButton(card, "SignIn", "SIGN IN",
                new Vector2(0.515f, 0.135f), new Vector2(0.95f, 0.235f), OnSignInClicked);
            _signOutButton = AddButton(card, "SignOut", "SIGN OUT",
                new Vector2(0.05f, 0.135f), new Vector2(0.95f, 0.235f), OnSignOutClicked);
            _useCloudButton = AddButton(card, "UseAccountSave", "CONFIRM USER SWITCH",
                new Vector2(0.05f, 0.135f), new Vector2(0.95f, 0.235f), OnUseCloudClicked);
            _forgotButton = AddButton(card, "Forgot", "FORGOT PASSWORD",
                new Vector2(0.05f, 0.03f), new Vector2(0.485f, 0.115f), OnForgotClicked);
            AddButton(card, "Close", "CLOSE",
                new Vector2(0.515f, 0.03f), new Vector2(0.95f, 0.115f), Hide);

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
            _deleteArmed = false;
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
            SetVisible(_bindButton.gameObject, guestOrOut && !conflict);
            SetVisible(_signInButton.gameObject, guestOrOut && !conflict);
            SetVisible(_signOutButton.gameObject, linked && !conflict);
            // Password reset only makes sense for guests (who
            // may be about to sign in) and email-bound accounts
            // — a Google-bound account has no password here.
            SetVisible(_forgotButton.gameObject,
                (guestOrOut || (linked && accounts.Binding == AccountBinding.Email)) && !conflict);
            SetVisible(_useCloudButton.gameObject, conflict);
            // One-to-one binding: Google is offered ONLY to an
            // unbound guest — as its bind choice or as the door
            // of an existing Google-bound account. A bound
            // account never sees a second door; it sees Delete.
            SetVisible(_googleButton.gameObject,
                guestOrOut && !conflict && GoogleSignInBridge.IsAvailable);
            SetVisible(_deleteButton.gameObject, linked && !conflict);
            if (_deleteButton != null && !_deleteArmed)
            {
                var deleteLabel = _deleteButton.GetComponentInChildren<Text>();
                if (deleteLabel != null)
                    deleteLabel.text = "DELETE ACCOUNT";
            }

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
                    _statusText.text = "Signed out. Sign in with your account's sign-in — or just close this and keep playing as a guest.";
                    break;
                case AccountStatus.SignedInGuest:
                    _statusText.text = "Guest account — not bound to a sign-in yet.\nBind ONE sign-in to this account: an email, or Google.\nChoose carefully — it can't be changed later.";
                    break;
                case AccountStatus.SignedInLinked:
                    string email = accounts.Email;
                    string line = string.IsNullOrEmpty(email) ? "Signed in." : $"Signed in: {email}";
                    line += $"\nSign-in: {BindingName(accounts.Binding)}";
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

        private static string BindingName(AccountBinding binding)
        {
            switch (binding)
            {
                case AccountBinding.Email: return "Email and password";
                case AccountBinding.Google: return "Google";
                case AccountBinding.Apple: return "Apple";
                default: return "Guest (not bound)";
            }
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
            if (_bindButton != null) _bindButton.interactable = enabled;
            if (_signInButton != null) _signInButton.interactable = enabled;
            if (_googleButton != null) _googleButton.interactable = enabled;
            if (_deleteButton != null) _deleteButton.interactable = enabled;
            if (_forgotButton != null) _forgotButton.interactable = enabled;
            if (_signOutButton != null) _signOutButton.interactable = enabled;
            if (_useCloudButton != null) _useCloudButton.interactable = enabled;
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

        private async void OnBindEmailClicked()
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
                SetHint("Email bound — this account now signs in with that email.", true);
                _passwordInput.text = "";
            }
            else
            {
                SetHint("That didn't work — the email may already belong to another account. Accounts keep the one sign-in they started with; try signing in instead.", false);
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

        private void OnGoogleClicked()
        {
            if (_busy || _manager == null) return;
            SetBusy(true);
            SetHint("Opening Google sign-in…", true);
            GoogleSignInBridge.SignIn(OnGoogleToken, OnGoogleError);
        }

        /// <summary>Google returned an ID token (guest state
        /// only — a bound account never sees the Google button).
        /// Bind first: Google attaches to the guest account,
        /// same UID, save untouched — that bind IS this
        /// account's one sign-in choice. If that Google identity
        /// already owns an account, the bind refuses and we fall
        /// back to signing into it (the save safeguard then
        /// arbitrates, exactly like email sign-in). If the
        /// Google email belongs to an email-password account,
        /// both attempts fail by design — under one-to-one
        /// binding that account keeps its own door — and the
        /// hint below says so.</summary>
        private async void OnGoogleToken(string idToken)
        {
            bool ok = false;
            try
            {
                var accounts = _manager.Accounts;
                if (accounts.Status == AccountStatus.SignedInLinked)
                {
                    // Unreachable through the panel (the button
                    // hides for bound accounts); kept as a guard
                    // so a stray token can never stack a door.
                    SetHint("This account already has its sign-in — another one can't be added.", false);
                }
                else
                {
                    ok = await accounts.LinkGoogleAsync(idToken);
                    if (!ok) ok = await accounts.SignInGoogleAsync(idToken);
                    if (ok)
                    {
                        _stagedSaveSeen = false;
                        try { await _manager.OnSignedInAsync(); }
                        catch { /* the manager logs its own failures */ }
                        if (_manager.HasPendingConflict)
                            SetHint("Signed in with Google — now choose which save your account keeps.", true);
                        else
                            SetHint(_stagedSaveSeen
                                ? "Signed in with Google. A newer cloud save from your account loads on next launch."
                                : "Signed in with Google — welcome.", true);
                    }
                    else
                    {
                        SetHint("Google sign-in didn't complete. If that Gmail belongs to an email-password account, that account keeps its own sign-in — sign in with email instead, or pick a different Google account.", false);
                    }
                }
            }
            catch { SetHint("Google sign-in didn't complete — try again.", false); }
            SetBusy(false);
            RefreshPanel();
        }

        private void OnGoogleError(string error)
        {
            SetBusy(false);
            SetHint(string.IsNullOrEmpty(error) ? "Google sign-in was cancelled." : error, false);
            RefreshPanel();
        }

        private void OnUseCloudClicked()
        {
            if (_busy || _manager == null) return;
            _manager.ResolveConflictUseCloud();
            SetHint("Loading your account save…", true);
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

        /// <summary>Delete account — the one-to-one model's
        /// only route to a different sign-in. Two-tap arm, like
        /// the boss-fight surrender: the first tap explains and
        /// relabels the button, the second executes. The
        /// manager deletes cloud data, the auth identity, and
        /// the local save, then signs in a fresh guest; the
        /// manager then asks for a session reload, so the
        /// fresh start begins immediately — no manual relaunch.</summary>
        private async void OnDeleteClicked()
        {
            if (_busy || _manager == null) return;
            if (!_deleteArmed)
            {
                _deleteArmed = true;
                var label = _deleteButton.GetComponentInChildren<Text>();
                if (label != null) label.text = "TAP AGAIN TO DELETE PERMANENTLY";
                SetHint("This permanently deletes the account, its cloud save, and this device's save. Tap again to confirm.", false);
                return;
            }
            _deleteArmed = false;
            SetBusy(true);
            bool ok = false;
            try { ok = await _manager.DeleteAccountAndResetAsync(); }
            catch { ok = false; }
            SetBusy(false);
            SetHint(ok
                ? "Account deleted — starting fresh…"
                : "Couldn't delete the account — sign in again first (deletion needs a recent sign-in), then retry.", ok);
            RefreshPanel();
        }
    }
}
