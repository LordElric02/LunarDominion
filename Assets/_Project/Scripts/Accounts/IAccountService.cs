using System;
using System.Threading.Tasks;

namespace LunarDominion.Accounts
{
    /// <summary>Sign-in surface (Roadmap Phase 1). Guest-first:
    /// SignInGuestAsync is the background first-run path; the
    /// Link* methods attach a real identity to that same guest
    /// account so the UID — and with it the save and scores —
    /// never changes. Google and Apple arrive as tokens produced
    /// by their native plugins (Google Sign-In Unity plugin /
    /// Sign in with Apple), so this layer never depends on them.
    /// Implementations: LocalAccountService (stub, runs today),
    /// FirebaseAccountService (behind `#if FIREBASE_SDK`).</summary>
    public interface IAccountService
    {
        event Action OnStatusChanged;

        AccountStatus Status { get; }

        /// <summary>False when no real account backend exists
        /// (the local stub, or Firebase failed its dependency
        /// check). Every caller must tolerate false and keep
        /// playing locally.</summary>
        bool IsAvailable { get; }

        /// <summary>Firebase UID, "" when signed out / local.</summary>
        string CurrentUid { get; }

        bool IsAnonymous { get; }

        /// <summary>The account's display name ("" if unset).</summary>
        string DisplayName { get; }

        /// <summary>Role claim from the current token.</summary>
        AccountRole Role { get; }

        Task InitializeAsync();

        /// <summary>Anonymous sign-in — the guest-first entry.</summary>
        Task<bool> SignInGuestAsync();

        /// <summary>Attach email+password to the current guest
        /// account (falls back to creating a fresh account when
        /// signed out).</summary>
        Task<bool> LinkEmailAsync(string email, string password);

        Task<bool> SignInEmailAsync(string email, string password);

        /// <summary>idToken comes from the Google Sign-In plugin.</summary>
        Task<bool> LinkGoogleAsync(string idToken);
        Task<bool> SignInGoogleAsync(string idToken);

        /// <summary>identityToken + rawNonce come from Sign in
        /// with Apple (the nonce is the one sent to Apple,
        /// unhashed — Firebase hashes it for the exchange).</summary>
        Task<bool> LinkAppleAsync(string identityToken, string rawNonce);
        Task<bool> SignInAppleAsync(string identityToken, string rawNonce);

        Task SignOutAsync();

        /// <summary>Force a token refresh so a newly granted
        /// role claim shows up without waiting for expiry.</summary>
        Task RefreshRoleAsync();

        /// <summary>Push a name up as the account displayName.
        /// The game calls this with GameState.EffectiveUsername —
        /// the local name wins (design Decision 4).</summary>
        Task<bool> SetDisplayNameAsync(string displayName);

        Task<bool> SendPasswordResetAsync(string email);
    }
}
