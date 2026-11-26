using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using LunarDominion.Core;
using LunarDominion.Save;
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
        // Raised when the local game has been replaced and the
        // running session must reload into the new one: the
        // player chose the account save in a conflict, or
        // deleted the account for a fresh start. The Bootstrap
        // answers by reloading the scene in place — the player
        // never has to close and reopen the app to switch.
        public event Action OnSessionReload;

        /// <summary>One side of a save conflict, in player
        /// terms: whose game it is and how far it got.</summary>
        public sealed class SaveSummary
        {
            public string Name;
            public int NexusLevel;
            public string SavedTimeUtc;
            public string SaveId;
        }

        /// <summary>MERGE SAFEGUARD (Patrick 2026-11-25): when
        /// the device save and the account save are different
        /// games, the newest-wins timestamp rule must NOT pick
        /// between them — a fresh or passively re-saved game
        /// always carries the newest stamp (a fresh phone save
        /// overwrote this account's progressed save twice in
        /// one day). The sync then stops: nothing is uploaded,
        /// staged, or pushed until the player chooses in the
        /// account panel. These hold the pending choice.
        /// "Different games" is decided by the save lineage ID
        /// since 2026-11-26 (see SyncSaveOnSignInAsync); the
        /// original name comparison remains as the fallback
        /// for saves written before IDs existed.</summary>
        public SaveSummary PendingLocalSave { get; private set; }
        public SaveSummary PendingCloudSave { get; private set; }
        public bool HasPendingConflict => PendingLocalSave != null && PendingCloudSave != null;
        public event Action OnSaveConflict;
        private string _pendingCloudJson;

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

                await SyncSaveOnSignInAsync(uid);
                // While a save conflict is pending the local
                // state is not the account's game, so its name
                // and scores must not be pushed over the
                // account's profile/leaderboard entries.
                if (!HasPendingConflict)
                {
                    await CloudSaves.PushProfileAsync(uid, state.EffectiveUsername);
                    await PushLairDamageAsync();
                }
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
                ClearConflict();
                if (localJson != null)
                    await CloudSaves.UploadSaveAsync(uid, localJson, localTime);
                return;
            }

            // Merge safeguard: different games never merge
            // silently. Lineage is decided by the save ID
            // (2026-11-26): when both saves carry one, equal
            // IDs are the same game — a rename included — and
            // sync automatically, while different IDs are
            // different games even when the names match (two
            // fresh "Commander" starts). When either save
            // predates IDs, the original name rule decides;
            // and if the names match and only the cloud save
            // carries an ID, a local win below adopts that ID
            // first so the lineage converges on the ID rule.
            var localSummary = Summarize(localJson, localTime);
            var cloudSummary = Summarize(cloud.SaveJson, cloud.ClientSaveTimeUtc);
            if (localSummary != null && cloudSummary != null)
            {
                bool bothHaveIds = !string.IsNullOrEmpty(localSummary.SaveId)
                    && !string.IsNullOrEmpty(cloudSummary.SaveId);
                bool sameGame = bothHaveIds
                    ? string.Equals(localSummary.SaveId, cloudSummary.SaveId, StringComparison.Ordinal)
                    : string.Equals(localSummary.Name, cloudSummary.Name, StringComparison.OrdinalIgnoreCase);
                if (!sameGame)
                {
                    PendingLocalSave = localSummary;
                    PendingCloudSave = cloudSummary;
                    _pendingCloudJson = cloud.SaveJson;
                    Debug.Log($"[Lunar Dominion] Save conflict: this device has '{localSummary.Name}' (Nexus {localSummary.NexusLevel}), the account has '{cloudSummary.Name}' (Nexus {cloudSummary.NexusLevel}) — waiting for the player's choice in the account panel.");
                    OnSaveConflict?.Invoke();
                    return;
                }
                if (!bothHaveIds
                    && string.IsNullOrEmpty(localSummary.SaveId)
                    && !string.IsNullOrEmpty(cloudSummary.SaveId)
                    && !IsNewer(cloud.ClientSaveTimeUtc, localTime))
                {
                    // Legacy local save, ID-carrying cloud save,
                    // same game, and the local save is the one
                    // about to be uploaded: stamp it with the
                    // account's lineage ID first. (When the
                    // cloud save is newer it is staged instead,
                    // and its ID arrives with it.)
                    _controller.State.SaveId = cloudSummary.SaveId;
                    _controller.SaveGame();
                    localJson = ReadLocalSaveJson();
                    localTime = _controller.State.LastSaveTimeUtc ?? "";
                }
            }

            ClearConflict();
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

        /// <summary>Summarize a save JSON for the conflict
        /// check and the player's choice: the name it plays
        /// under (the untouched default reads "Commander") and
        /// its Nexus level. Unparseable = null, and null never
        /// raises a conflict — the sync falls back to the plain
        /// newest-wins rule rather than blocking on a bad read.</summary>
        private static SaveSummary Summarize(string saveJson, string savedTimeUtc)
        {
            if (string.IsNullOrEmpty(saveJson)) return null;
            SaveData data;
            try { data = JsonUtility.FromJson<SaveData>(saveJson); }
            catch { return null; }
            if (data == null) return null;
            string name = (data.Username ?? "").Trim();
            return new SaveSummary
            {
                Name = name.Length == 0 ? "Commander" : name,
                NexusLevel = data.BuildingLevels != null && data.BuildingLevels.Length > 0
                    ? data.BuildingLevels[0] : 1,
                SavedTimeUtc = savedTimeUtc ?? "",
                SaveId = data.SaveId ?? ""
            };
        }

        private void ClearConflict()
        {
            PendingLocalSave = null;
            PendingCloudSave = null;
            _pendingCloudJson = null;
        }

        /// <summary>Player chose the account save: stage it
        /// and clear the conflict, then ask for a session
        /// reload — the Bootstrap swaps the staged save in and
        /// reloads the scene, so the account's game loads
        /// right away instead of on a manual relaunch (the
        /// staged file remains the fallback if the app dies
        /// before the reload lands). This explicit choice is
        /// the one case where a live session is replaced on
        /// the spot; automatic sync staging still never yanks
        /// one. From here the device plays the account's
        /// game, so later syncs match by save ID and run
        /// automatically again.</summary>
        public void ResolveConflictUseCloud()
        {
            if (!HasPendingConflict || _pendingCloudJson == null) return;
            StageSaveForNextLaunch(_pendingCloudJson);
            ClearConflict();
            OnCloudSaveStaged?.Invoke();
            OnSessionReload?.Invoke();
        }

        /// <summary>Player chose this device's save: upload it
        /// as the account save. This explicit choice is the only
        /// path allowed to replace a different-named cloud
        /// save.</summary>
        public async Task<bool> ResolveConflictKeepLocalAsync()
        {
            if (!HasPendingConflict) return false;
            bool ok = await UploadCurrentSaveAsync();
            if (ok) ClearConflict();
            return ok;
        }

        /// <summary>Delete this account and reset the device
        /// to a fresh guest — the "start over with a different
        /// door" path of the one-to-one binding model (Patrick
        /// 2026-11-25). Order matters: the cloud docs go FIRST,
        /// while the uid can still authenticate against the
        /// owner-only security rules (after the auth identity
        /// is deleted, nobody can sign in as that uid to clean
        /// them up); then the auth identity; then the local
        /// save file, so the deleted game cannot re-upload
        /// under the new guest. The fresh start takes effect
        /// on next launch — the live session keeps its
        /// in-memory state until then, the same staging rule
        /// as save conflicts — so the panel tells the player
        /// to restart right away. The new guest is signed in
        /// WITHOUT the post-sign-in sync on purpose: syncing
        /// now would upload this session's (deleted) game
        /// under the new guest UID. Next launch starts clean
        /// and syncs then.</summary>
        public async Task<bool> DeleteAccountAndResetAsync()
        {
            if (!CloudAvailable || _controller == null) return false;
            string uid = Accounts.CurrentUid;
            try { await CloudSaves.DeleteAccountDataAsync(uid); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Cloud data delete failed (continuing): {e.Message}");
            }
            bool ok = false;
            try { ok = await Accounts.DeleteAccountAsync(); }
            catch { ok = false; }
            if (!ok) return false;
            ClearConflict();
            try { _controller.Save.DeleteSave(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Local save delete failed: {e.Message}");
            }
            try { await Accounts.SignInGuestAsync(); }
            catch { /* the services log their own failures */ }
            // The local save is gone and a fresh guest is
            // signed in, but this session still holds the
            // deleted account's game in memory — reload the
            // scene so the fresh start begins now, not on a
            // manual relaunch.
            OnSessionReload?.Invoke();
            return true;
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

        /// <summary>Stage a cloud save for next launch. The
        /// JSON waits in SaveService's pending file, NOT over
        /// the live save file: this session keeps saving its
        /// own state (pause/quit autosaves included) and would
        /// overwrite a directly replaced file before the next
        /// launch ever read it. Bootstrap applies the pending
        /// file before the controller loads.</summary>
        private void StageSaveForNextLaunch(string saveJson)
        {
            try
            {
                SaveService.StagePendingSave(saveJson);
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
