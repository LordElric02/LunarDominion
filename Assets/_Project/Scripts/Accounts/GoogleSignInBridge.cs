using System;
using UnityEngine;

namespace LunarDominion.Accounts
{
    /// <summary>Bridge to Google's Sign in with Google plugin
    /// (googlesamples/siwg-mobile-unity-plugin, Nov 2026): the
    /// plugin owns the native account picker (Android Credential
    /// Manager) and hands back a Google ID token;
    /// FirebaseAccountService already turns tokens into
    /// sign-ins (LinkGoogleAsync / SignInGoogleAsync), so this
    /// file is the only plugin-dependent code in the game.
    ///
    /// Compiled ONLY with GOOGLE_SIGNIN defined — add that
    /// Scripting Define Symbol AFTER importing
    /// siwg_unity_mobile_plugin.unitypackage. Without the
    /// define, this stub reports unavailable and the account
    /// panel never shows the Google buttons — the same
    /// stub/real pattern as the Local/Firebase service pairs.
    ///
    /// The plugin's GoogleSignInManager loads its settings from
    /// Resources in Awake, so the bridge creates it lazily at
    /// runtime on the first sign-in attempt — no scene wiring.
    /// In the Editor the plugin itself fails soft with a
    /// "not supported in Editor" error, which the panel shows
    /// as its hint.</summary>
    public static class GoogleSignInBridge
    {
#if GOOGLE_SIGNIN
        public static bool IsAvailable => true;

        private static Action<string> _onToken;
        private static Action<string> _onError;
        private static bool _hooked;

        public static void SignIn(Action<string> onToken, Action<string> onError)
        {
            _onToken = onToken;
            _onError = onError;
            if (!_hooked)
            {
                GoogleSignInManager.OnSignInSuccess += HandleSuccess;
                GoogleSignInManager.OnSignInError += HandleError;
                _hooked = true;
            }
            if (GoogleSignInManager.Instance == null)
            {
                var go = new GameObject("GoogleSignInManager");
                go.AddComponent<GoogleSignInManager>();
            }
            GoogleSignInManager.Instance.StartSignIn();
        }

        private static void HandleSuccess(string idToken)
        {
            var cb = _onToken;
            _onToken = null;
            _onError = null;
            if (cb != null) cb(idToken);
        }

        private static void HandleError(string error)
        {
            var cb = _onError;
            _onToken = null;
            _onError = null;
            if (cb != null) cb(error);
        }
#else
        public static bool IsAvailable => false;

        public static void SignIn(Action<string> onToken, Action<string> onError)
        {
            if (onError != null) onError("Google sign-in is not in this build.");
        }
#endif
    }
}
