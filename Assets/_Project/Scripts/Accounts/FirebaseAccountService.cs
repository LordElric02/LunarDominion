#if FIREBASE_SDK
using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

namespace LunarDominion.Accounts
{
    /// <summary>Firebase Auth implementation of IAccountService
    /// (Roadmap Phase 1). Compiled ONLY when the Firebase Unity
    /// SDK is imported and FIREBASE_SDK is in the scripting
    /// defines — without the define this whole file is inert and
    /// LocalAccountService runs instead.
    ///
    /// Written against the Firebase Unity SDK surface:
    /// FirebaseAuth.DefaultInstance, Task-based sign-in methods
    /// (SDK 12+ returns AuthResult — the user is .User),
    /// GetIdTokenResultAsync for custom-claim reads. Adjusted
    /// to SDK 14.2 on 2026-11-25 from Patrick's first compile.
    /// Session-restore race fixed the same day: InitializeAsync
    /// now waits for the first StateChanged (the persisted
    /// session verdict) before the manager may decide a guest
    /// sign-in is needed, and SignInGuestAsync refuses to mint
    /// a guest over an existing session.</summary>
    public sealed class FirebaseAccountService : IAccountService
    {
        private FirebaseAuth _auth;
        private bool _dependencyOk;

        public event Action OnStatusChanged;

        public AccountStatus Status { get; private set; } = AccountStatus.SignedOut;
        public bool IsAvailable => _dependencyOk && _auth != null;
        public string CurrentUid => _auth != null && _auth.CurrentUser != null ? _auth.CurrentUser.UserId : "";
        public bool IsAnonymous => _auth != null && _auth.CurrentUser != null && _auth.CurrentUser.IsAnonymous;
        public string DisplayName => _auth != null && _auth.CurrentUser != null ? _auth.CurrentUser.DisplayName ?? "" : "";
        public string Email => _auth != null && _auth.CurrentUser != null ? _auth.CurrentUser.Email ?? "" : "";
        public AccountRole Role { get; private set; } = AccountRole.Player;

        public async Task InitializeAsync()
        {
            var dependency = await FirebaseApp.CheckAndFixDependenciesAsync();
            _dependencyOk = dependency == DependencyStatus.Available;
            if (!_dependencyOk)
            {
                // Broken/missing config (e.g. google-services.json
                // not yet dropped in): degrade to local-only
                // rather than failing the game's launch.
                Status = AccountStatus.LocalOnly;
                Debug.LogWarning($"[Lunar Dominion] Firebase unavailable ({dependency}) — accounts run local-only.");
                OnStatusChanged?.Invoke();
                return;
            }
            _auth = FirebaseAuth.DefaultInstance;
            _auth.StateChanged += OnAuthStateChanged;
            // Wait for the SDK to deliver the persisted session
            // before returning (Patrick 2026-11-25: "it's saying
            // I'm a guest account when I'm not"). CurrentUser is
            // restored ASYNCHRONOUSLY on desktop/Editor — reading
            // it immediately can still show null, the manager then
            // reads SignedOut and mints a fresh guest OVER the
            // returning linked account. The first StateChanged is
            // the restore verdict (it fires even when there is no
            // persisted user). Bounded at 5s: a hung SDK must not
            // stall the game's launch.
            var restored = new TaskCompletionSource<bool>();
            _auth.StateChanged += (s, e) => restored.TrySetResult(true);
            await Task.WhenAny(restored.Task, Task.Delay(5000));
            UpdateFromCurrentUser();
        }

        private void OnAuthStateChanged(object sender, EventArgs e) => UpdateFromCurrentUser();

        private void UpdateFromCurrentUser()
        {
            var user = _auth != null ? _auth.CurrentUser : null;
            if (user == null)
            {
                Status = AccountStatus.SignedOut;
                Role = AccountRole.Player;
            }
            else
            {
                Status = user.IsAnonymous ? AccountStatus.SignedInGuest : AccountStatus.SignedInLinked;
                _ = RefreshRoleAsync();
            }
            OnStatusChanged?.Invoke();
        }

        private void SetSigningIn()
        {
            Status = AccountStatus.SigningIn;
            OnStatusChanged?.Invoke();
        }

        // ---- sign-in / linking -------------------------------------------

        public async Task<bool> SignInGuestAsync()
        {
            if (!IsAvailable) return false;
            if (_auth.CurrentUser != null)
            {
                // Never mint a fresh guest over an existing
                // session — a restored linked account must
                // survive this call regardless of caller timing.
                UpdateFromCurrentUser();
                return true;
            }
            try
            {
                SetSigningIn();
                var user = (await _auth.SignInAnonymouslyAsync())?.User;
                UpdateFromCurrentUser();
                return user != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Guest sign-in failed: {e.Message}");
                UpdateFromCurrentUser();
                return false;
            }
        }

        public async Task<bool> LinkEmailAsync(string email, string password)
        {
            if (!IsAvailable) return false;
            try
            {
                SetSigningIn();
                var credential = EmailAuthProvider.GetCredential(email, password);
                var current = _auth.CurrentUser;
                FirebaseUser user;
                if (current != null && current.IsAnonymous)
                    user = (await current.LinkWithCredentialAsync(credential))?.User;
                else if (current == null)
                    user = (await _auth.CreateUserWithEmailAndPasswordAsync(email, password))?.User;
                else
                    return true; // already linked/signed in — nothing to attach
                UpdateFromCurrentUser();
                return user != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Email link failed: {e.Message}");
                UpdateFromCurrentUser();
                return false;
            }
        }

        public async Task<bool> SignInEmailAsync(string email, string password)
        {
            if (!IsAvailable) return false;
            try
            {
                SetSigningIn();
                var user = (await _auth.SignInWithEmailAndPasswordAsync(email, password))?.User;
                UpdateFromCurrentUser();
                return user != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Email sign-in failed: {e.Message}");
                UpdateFromCurrentUser();
                return false;
            }
        }

        public Task<bool> LinkGoogleAsync(string idToken)
            => LinkWithCredential(GoogleAuthProvider.GetCredential(idToken, null), "Google");

        public Task<bool> SignInGoogleAsync(string idToken)
            => SignInWithCredential(GoogleAuthProvider.GetCredential(idToken, null), "Google");

        // Apple: Firebase's apple.com OAuth provider. The raw
        // nonce is the unhashed value that was sent to Apple in
        // the sign-in request.
        public Task<bool> LinkAppleAsync(string identityToken, string rawNonce)
            => LinkWithCredential(OAuthProvider.GetCredential("apple.com", identityToken, rawNonce), "Apple");

        public Task<bool> SignInAppleAsync(string identityToken, string rawNonce)
            => SignInWithCredential(OAuthProvider.GetCredential("apple.com", identityToken, rawNonce), "Apple");

        private async Task<bool> LinkWithCredential(Credential credential, string providerName)
        {
            if (!IsAvailable || credential == null) return false;
            try
            {
                SetSigningIn();
                var current = _auth.CurrentUser;
                FirebaseUser user;
                if (current != null && current.IsAnonymous)
                    user = (await current.LinkWithCredentialAsync(credential))?.User;
                else if (current == null)
                    user = await _auth.SignInWithCredentialAsync(credential);
                else
                    return true;
                UpdateFromCurrentUser();
                return user != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] {providerName} link failed: {e.Message}");
                UpdateFromCurrentUser();
                return false;
            }
        }

        private async Task<bool> SignInWithCredential(Credential credential, string providerName)
        {
            if (!IsAvailable || credential == null) return false;
            try
            {
                SetSigningIn();
                var user = await _auth.SignInWithCredentialAsync(credential);
                UpdateFromCurrentUser();
                return user != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] {providerName} sign-in failed: {e.Message}");
                UpdateFromCurrentUser();
                return false;
            }
        }

        public Task SignOutAsync()
        {
            if (_auth != null) _auth.SignOut();
            UpdateFromCurrentUser();
            return Task.CompletedTask;
        }

        // ---- profile & role ----------------------------------------------

        public async Task<bool> SetDisplayNameAsync(string displayName)
        {
            var user = _auth != null ? _auth.CurrentUser : null;
            if (user == null) return false;
            try
            {
                await user.UpdateUserProfileAsync(new UserProfile { DisplayName = displayName ?? "" });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Display name update failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Reads the `role` custom claim off a freshly
        /// forced token (Phase 3). No claim = Player. Display
        /// only — Firestore rules enforce.</summary>
        public async Task RefreshRoleAsync()
        {
            var user = _auth != null ? _auth.CurrentUser : null;
            if (user == null) { Role = AccountRole.Player; return; }
            try
            {
                var token = await user.GetIdTokenResultAsync(true);
                Role = AccountRole.Player;
                if (token.Claims != null
                    && token.Claims.TryGetValue("role", out var raw))
                {
                    if (raw is string roleName)
                    {
                        if (roleName == "admin") Role = AccountRole.Admin;
                        else if (roleName == "tester") Role = AccountRole.Tester;
                    }
                }
                OnStatusChanged?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Role refresh failed: {e.Message}");
            }
        }

        public async Task<bool> SendPasswordResetAsync(string email)
        {
            if (!IsAvailable) return false;
            try
            {
                await _auth.SendPasswordResetEmailAsync(email);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Password reset failed: {e.Message}");
                return false;
            }
        }
    }
}
#endif
