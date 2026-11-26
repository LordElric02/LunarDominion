using System;
using UnityEngine;

namespace LunarDominion.Accounts
{
    /// <summary>Bridge to the Google Sign-In plugin: it owns
    /// the native account picker and hands back a Google ID
    /// token; FirebaseAccountService already turns tokens into
    /// sign-ins (LinkGoogleAsync / SignInGoogleAsync), so this
    /// file is the only plugin-dependent code in the game.
    ///
    /// The plugin is the maintained fork harshhks/
    /// google-signin-unity v1.0.0 (2026-11-25): the SAME C#
    /// API as the archived googlesamples plugin — this bridge
    /// needed no code changes — but Android sign-in runs on
    /// Google's current Credential Manager instead of the
    /// legacy GoogleSignIn API (removed in play-services-auth
    /// 22.0.0). API compatibility verified from the fork's
    /// package source before the swap. History: Google's
    /// first-party siwg plugin targets Unity 6.5 and failed
    /// the 6.3 build three ways; the archived 2018 plugin
    /// built but its ID-token step faulted DeveloperError
    /// with every console setting verified.
    ///
    /// Import notes (Patrick, one-time): delete the old
    /// Assets/GoogleSignIn folder FIRST (the fork's build
    /// check fails the build while it lingers), then import
    /// GoogleSignIn-1.0.0.unitypackage with the AuthManager
    /// folder UNCHECKED (its asmdef references the AppleAuth
    /// package, which this project does not use) and Samples
    /// unchecked; Force Resolve afterwards.
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
        // plugin accepts it in code — it could also read
        // Project Settings / google-services.json, but the
        // explicit const keeps it deterministic.
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
