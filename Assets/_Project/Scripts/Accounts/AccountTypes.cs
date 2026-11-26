using System;

namespace LunarDominion.Accounts
{
    /// <summary>Account layer shared types (2026-11-24, Firebase
    /// design — see files/Lunar-Dominion-Firebase-Design.md).
    /// Nothing in this folder references the Firebase SDK outside
    /// `#if FIREBASE_SDK` files, so the game compiles and plays
    /// identically with the SDK absent: the local stub is what
    /// runs until Patrick imports the SDK and adds the define.</summary>
    public enum AccountStatus
    {
        /// <summary>No account layer (stub, or Firebase reported a
        /// broken dependency). The game runs purely on the local
        /// save, exactly as before accounts existed.</summary>
        LocalOnly,
        SignedOut,
        SigningIn,
        /// <summary>Anonymous (guest-first) sign-in.</summary>
        SignedInGuest,
        /// <summary>A Google / Apple / email identity is linked.</summary>
        SignedInLinked
    }

    /// <summary>Which sign-in an account is BOUND to
    /// (one-to-one binding, Patrick 2026-11-25): an account
    /// takes exactly one sign-in when it is created — email,
    /// Google, or (iOS phase) Apple — and can never take
    /// another. Changing doors means deleting the account and
    /// starting over. None = not bound yet (guest / signed
    /// out / local-only).</summary>
    public enum AccountBinding
    {
        None,
        Email,
        Google,
        Apple
    }

    /// <summary>Role from the Firebase custom claim `role`
    /// (Roadmap Phase 3). No claim = Player. The client reads this
    /// only to decide what to display; Firestore security rules
    /// are the actual enforcement.</summary>
    public enum AccountRole
    {
        Player,
        Tester,
        Admin
    }

    /// <summary>A downloaded cloud save: the complete SaveData
    /// JSON plus the timestamps the newest-wins comparison uses.
    /// ClientSaveTimeUtc is the payload's own LastSaveTimeUtc,
    /// copied into the Firestore doc's metadata at upload.</summary>
    public sealed class CloudSaveSnapshot
    {
        public string SaveJson;
        public string ClientSaveTimeUtc = "";
        public DateTime CloudUpdatedAtUtc;
    }

    /// <summary>One row of the Robot Lair Damage board — the only
    /// data that ever leaves the private save for the public
    /// leaderboard collection (score fields + display name).</summary>
    public sealed class LeaderboardEntry
    {
        public string Uid;
        public string Username;
        public int Score;
        public int BossDamage;
        public int DroneDamage;
        public int BossLevelDefeated;
        public int PlayerLevel;
    }
}
