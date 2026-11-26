using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LunarDominion.Accounts
{
    /// <summary>The account service the game actually uses until
    /// the Firebase SDK is imported and FIREBASE_SDK is defined:
    /// no network, no sign-in, status permanently LocalOnly. It
    /// exists so every caller (AccountManager today, account UI
    /// later) is written against the real interface from day one
    /// and never needs a null check or a behaviour branch.</summary>
    public sealed class LocalAccountService : IAccountService
    {
        public event Action OnStatusChanged;

        public AccountStatus Status => AccountStatus.LocalOnly;
        public bool IsAvailable => false;
        public string CurrentUid => "";
        public bool IsAnonymous => false;
        public string DisplayName => "";
        public string Email => "";
        public AccountRole Role => AccountRole.Player;
        public AccountBinding Binding => AccountBinding.None;

        public Task InitializeAsync() => Task.CompletedTask;

        public Task<bool> SignInGuestAsync() => NotAvailable("guest sign-in");
        public Task<bool> LinkEmailAsync(string email, string password) => NotAvailable("email link");
        public Task<bool> SignInEmailAsync(string email, string password) => NotAvailable("email sign-in");
        public Task<bool> LinkGoogleAsync(string idToken) => NotAvailable("Google link");
        public Task<bool> SignInGoogleAsync(string idToken) => NotAvailable("Google sign-in");
        public Task<bool> LinkAppleAsync(string identityToken, string rawNonce) => NotAvailable("Apple link");
        public Task<bool> SignInAppleAsync(string identityToken, string rawNonce) => NotAvailable("Apple sign-in");
        public Task<bool> DeleteAccountAsync() => NotAvailable("account deletion");
        public Task<bool> SetDisplayNameAsync(string displayName) => NotAvailable("display name");
        public Task<bool> SendPasswordResetAsync(string email) => NotAvailable("password reset");

        public Task SignOutAsync() => Task.CompletedTask;
        public Task RefreshRoleAsync() => Task.CompletedTask;

        private Task<bool> NotAvailable(string what)
        {
            Debug.Log($"[Lunar Dominion] Accounts: {what} unavailable — local stub (no Firebase SDK).");
            OnStatusChanged?.Invoke();
            return Task.FromResult(false);
        }
    }
}
