using System;
using UnityEngine;

namespace LunarDominion.Accounts
{
    /// <summary>Bridge to Google's legacy sign-in plugin
    /// (googlesamples/google-signin-unity v1.0.4): the plugin
    /// owns the native account picker and hands back a Google
    /// ID token; FirebaseAccountService already turns tokens
    /// into sign-ins (LinkGoogleAsync / SignInGoogleAsync), so
    /// this file is the only plugin-dependent code in the game.
    ///
    /// Why the legacy plugin: Google's Nov-2026 Credential
    /// Manager plugin (siwg) targets Unity 6.5's Gradle
    /// pipeline — on 6.3 its Kotlin androidlib fails the build
    /// three different ways (manifest XML, missing kotlin()
    /// extension, KGP/AGP classloader clash, 2026-11-25). The
    /// legacy plugin ships a prebuilt Java library — no Kotlin
    /// compilation in the consuming project — and its C# API
    /// was verified from the v1.0.4 package source.
    ///
    /// Import notes (Patrick, one-time): import
    /// google-signin-plugin-1.0.4.unitypackage with the
    /// Assets/PlayServicesResolver and Assets/Parse folders
    /// UNCHECKED — the project already has Firebase's modern
    /// EDM resolver, and the plugin's code uses framework
    /// Tasks, not the 2018 Parse shims.
    ///
    /// Compiled ONLY with GOOGLE_SIGNIN defined. Without the
    /// define, this stub reports unavailable and the account
    /// panel never shows the Google buttons — the same
    /// stub/real pattern as the Local/Firebase service pairs.
    /// In the Editor the native flow cannot run; the plugin
    /// faults the sign-in task and the panel shows the error
    /// as its hint — the real test is on the phone.</summary>
    public static class GoogleSignInBridge
    {
#if GOOGLE_SIGNIN
        public static bool IsAvailable => true;

        // Web (OAuth 2.0) client ID for the Firebase project —
        // the client_type 3 entry in google-services.json. A
        // public identifier (it ships inside the app); the
        // legacy plugin takes it in code, not a settings asset.
        private const string WebClientId =
            "594856987727-b14kaq4r4lacralad0enqu3ula3bg8an.apps.googleusercontent.com";

        private static bool _configured;

        public static void SignIn(Action<string> onToken, Action<string> onError)
        {
            try
            {
                if (!_configured)
                {
                    Google.GoogleSignIn.Configuration =
                        new Google.GoogleSignInConfiguration
                        {
                            WebClientId = WebClientId,
                            RequestIdToken = true,
                            RequestEmail = true,
                            UseGameSignIn = false
                        };
                    _configured = true;
                }

                Google.GoogleSignIn.DefaultInstance.SignIn().ContinueWith(task =>
                {
                    if (task.IsCanceled)
                    {
                        if (onError != null) onError("Google sign-in was cancelled.");
                        return;
                    }
                    if (task.IsFaulted)
                    {
                        if (onError != null) onError(Describe(task.Exception));
                        return;
                    }
                    var user = task.Result;
                    if (user == null || string.IsNullOrEmpty(user.IdToken))
                    {
                        if (onError != null)
                            onError("Google sign-in returned no ID token.");
                        return;
                    }
                    if (onToken != null) onToken(user.IdToken);
                });
            }
            catch (Exception ex)
            {
                if (onError != null) onError(Describe(ex));
            }
        }

        private static string Describe(Exception ex)
        {
            // Task faults arrive wrapped in AggregateException;
            // the plugin's own failures are SignInException with
            // a GoogleSignInStatusCode worth showing the player.
            var agg = ex as AggregateException;
            if (agg != null && agg.InnerException != null)
                ex = agg.InnerException;
            var signInEx = ex as Google.GoogleSignIn.SignInException;
            if (signInEx != null)
                return "Google sign-in failed: " + signInEx.Status + ".";
            return "Google sign-in failed: " + ex.Message;
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
