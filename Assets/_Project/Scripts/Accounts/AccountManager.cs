using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using LunarDominion.Core;
using UnityEngine;

namespace LunarDominion.Accounts
{
    /// <summary>Owns the account/cloud layer for the running game
    /// (2026-11-24). Created once by LunarDominionBootstrap right
    /// after the GameController. Picks the Firebase
    /// implementations when FIREBASE_SDK is defined, the local
    /// stubs otherwise — with the stubs, Initialize does nothing
    /// but log its status and the game behaves exactly as it did
    /// before accounts existed.
    ///
    /// Sync policy (design doc): the local JSON save is the
    /// in-session source of truth and the offline cache. Uploads
    /// read the file SaveService just wrote, so the cloud payload
    /// is guaranteed to be the complete SaveData serialization.
    /// A newer cloud save is STAGED to the local file and takes
    /// effect on the next launch — a live session is never
    /// overwritten mid-play.</summary>
    public sealed class AccountManager
    {
        public IAccountService Accounts { get; }
        public ICloudSaveService CloudSaves { get; }
        public ILeaderboardSync Leaderboard { get; }

        public AccountStatus Status => Accounts.Status;

        /// <summary>True only when a real backend answered: signed
        /// in AND cloud services reachable. Every sync entry point
        /// checks this and fails soft.</summary>
        public bool CloudAvailable =>
            Accounts.IsAvailable && CloudSaves.IsAvailable && !string.IsNullOrEmpty(Accounts.CurrentUid);

        /// <summary>Raised when a newer cloud save was staged to
        /// the local file for next launch. The UI pass wires this
        /// to an Inbox message; today it is logged.</summary>
        public event Action OnCloudSaveStaged;

        private GameController _controller;
        private bool _initialized;

        private AccountManager(IAccountService accounts, ICloudSaveService cloudSaves, ILeaderboardSync leaderboard)
        {
            Accounts = accounts;
            CloudSaves = cloudSaves;
            Leaderboard = leaderboard;
        }

        public static AccountManager CreateDefault()
        {
#if FIREBASE_SDK
            var cloud = new FirebaseCloudServices();
            return new AccountManager(new FirebaseAccountService(), cloud, cloud);
#else
            var local = new LocalCloudServices();
            return new AccountManager(new LocalAccountService(), local, local);
#endif
        }

        /// <summary>Bootstrap hook. With the stub this is a log
        /// line and nothing else. With Firebase it kicks off the
        /// guest-first anonymous sign-in in the background — the
        /// game never waits on it.</summary>
        public void Initialize(GameController controller)
        {
            if (_initialized || controller == null) return;
            _initialized = true;
            _controller = controller;
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                await Accounts.InitializeAsync();
                if (Accounts.IsAvailable && Accounts.Status == AccountStatus.SignedOut)
                {
                    // Guest-first (design Decision 1): a UID with
                    // no login screen, progress untouched.
                    if (await Accounts.SignInGuestAsync())
                        await OnSignedInAsync();
                }
                else if (Accounts.IsAvailable && !string.IsNullOrEmpty(Accounts.CurrentUid))
                {
                    // Firebase restored a previous session.
                    await OnSignedInAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Accounts init failed, continuing local-only: {e.Message}");
            }
            Debug.Log($"[Lunar Dominion] Accounts: {Accounts.Status} (cloud available: {CloudAvailable})");
        }

        /// <summary>Post-sign-in housekeeping: username
        /// reconciliation, profile push, save migration/newest-wins,
        /// and a leaderboard push so the board entry exists.</summary>
        public async Task OnSignedInAsync()
        {
            if (!CloudAvailable || _controller == null) return;
            string uid = Accounts.CurrentUid;
            var state = _controller.State;
            try
            {
                // Username (design Decision 4): the local name
                // wins — unless it is the untouched default and
                // the account already carries a real name from
                // another device, which is adopted locally.
                string accountName = Accounts.DisplayName;
                if (string.IsNullOrWhiteSpace(state.Username)
                    && !string.IsNullOrEmpty(accountName)
                    && accountName != state.EffectiveUsername)
                {
                    state.Username = accountName;
                    _controller.SaveGame();
                    _controller.NotifyChanged();
                }
                else
                {
                    await Accounts.SetDisplayNameAsync(state.EffectiveUsername);
                }

                await CloudSaves.PushProfileAsync(uid, state.EffectiveUsername);
                await SyncSaveOnSignInAsync(uid);
                await PushLairDamageAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Post-sign-in sync failed (will retry at next trigger): {e.Message}");
            }
        }

        /// <summary>First-sign-in migration + newest-wins, per
        /// the design doc: no cloud save → upload local; local
        /// newer → upload local; cloud newer → stage for next
        /// launch, session untouched.</summary>
        private async Task SyncSaveOnSignInAsync(string uid)
        {
            var cloud = await CloudSaves.DownloadSaveAsync(uid);
            string localJson = ReadLocalSaveJson();
            string localTime = _controller.State.LastSaveTimeUtc ?? "";

            if (cloud == null)
            {
                if (localJson != null)
                    await CloudSaves.UploadSaveAsync(uid, localJson, localTime);
                return;
            }

            if (IsNewer(cloud.ClientSaveTimeUtc, localTime))
            {
                StageSaveForNextLaunch(cloud.SaveJson);
                Debug.Log("[Lunar Dominion] A newer cloud save was staged — it loads on next launch.");
                OnCloudSaveStaged?.Invoke();
            }
            else if (localJson != null)
            {
                await CloudSaves.UploadSaveAsync(uid, localJson, localTime);
            }
        }

        /// <summary>Upload trigger for after major events (boss
        /// results, building completions) and pause/quit: call
        /// AFTER GameController.SaveGame() so the file on disk is
        /// current. Fire-and-forget safe — failures are logged,
        /// never thrown into gameplay.</summary>
        public async Task<bool> UploadCurrentSaveAsync()
        {
            if (!CloudAvailable || _controller == null) return false;
            string localJson = ReadLocalSaveJson();
            if (localJson == null) return false;
            try
            {
                return await CloudSaves.UploadSaveAsync(
                    Accounts.CurrentUid, localJson, _controller.State.LastSaveTimeUtc ?? "");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Cloud save upload failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Push the Robot Lair Damage career totals from
        /// GameState. Called on sign-in and (by future wiring) at
        /// boss-fight end, where LeaderboardService banks them.</summary>
        public async Task<bool> PushLairDamageAsync()
        {
            if (!CloudAvailable || _controller == null) return false;
            var s = _controller.State;
            var entry = new LeaderboardEntry
            {
                Uid = Accounts.CurrentUid,
                Username = s.EffectiveUsername,
                Score = s.LairDamageScore,
                BossDamage = s.LairBossDamage,
                DroneDamage = s.LairDroneDamage,
                BossLevelDefeated = s.BossLevelDefeated,
                PlayerLevel = _controller.PlayerLevel != null ? _controller.PlayerLevel.Level() : 0
            };
            try
            {
                return await Leaderboard.PushLairDamageAsync(entry);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Leaderboard push failed: {e.Message}");
                return false;
            }
        }

        // ---- local file plumbing (SaveService is never modified) ----

        private string ReadLocalSaveJson()
        {
            try
            {
                string path = _controller.Save.SavePath;
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Could not read local save for cloud sync: {e.Message}");
                return null;
            }
        }

        /// <summary>Write a cloud save over the local file using
        /// the same temp-file + replace pattern as SaveService,
        /// so a crash mid-stage can never leave a torn file. The
        /// staged save is picked up by the normal load path on
        /// the next launch.</summary>
        private void StageSaveForNextLaunch(string saveJson)
        {
            try
            {
                string path = _controller.Save.SavePath;
                string temp = path + ".tmp";
                File.WriteAllText(temp, saveJson);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Could not stage cloud save: {e.Message}");
            }
        }

        /// <summary>Newest-wins clock: both sides are the
        /// round-trip UTC strings SaveService maintains
        /// (LastSaveTimeUtc). Parse properly — never compare
        /// local-time strings (the offline-production UTC pitfall
        /// in GameController). Unparseable = not newer.</summary>
        private static bool IsNewer(string candidateUtc, string currentUtc)
        {
            if (string.IsNullOrEmpty(candidateUtc)) return false;
            if (string.IsNullOrEmpty(currentUtc)) return true;
            if (!DateTime.TryParse(candidateUtc, null, DateTimeStyles.RoundtripKind, out var candidate)) return false;
            if (!DateTime.TryParse(currentUtc, null, DateTimeStyles.RoundtripKind, out var current)) return false;
            return candidate > current;
        }
    }
}
