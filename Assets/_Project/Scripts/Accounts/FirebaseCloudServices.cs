#if FIREBASE_SDK
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

namespace LunarDominion.Accounts
{
    /// <summary>Firestore implementation of the cloud save +
    /// leaderboard surfaces (Roadmap Phase 2). Compiled ONLY
    /// with the Firebase Unity SDK imported and FIREBASE_SDK
    /// defined; otherwise LocalCloudServices runs.
    ///
    /// Collections (see the design doc for the full model and
    /// the security rules that guard them):
    ///   users/{uid}                          public profile
    ///   saves/{uid}                          full save, owner-only
    ///   leaderboards_robotLairDamage/{uid}   score fields only
    ///
    /// Firestore returns integers as Int64 — the Int() helper
    /// below converts rather than casts.</summary>
    public sealed class FirebaseCloudServices : ICloudSaveService, ILeaderboardSync
    {
        // Mirrors SaveData.SaveVersion (SaveService rejects any
        // other version on load). Bump alongside it, never alone.
        private const int CurrentSaveVersion = 1;

        private const string SavesCollection = "saves";
        private const string UsersCollection = "users";
        private const string LairBoardCollection = "leaderboards_robotLairDamage";

        private FirebaseFirestore Db => FirebaseFirestore.DefaultInstance;

        public bool IsAvailable
        {
            get
            {
                try { return Db != null; }
                catch { return false; }
            }
        }

        // ---- cloud save ---------------------------------------------------

        public async Task<CloudSaveSnapshot> DownloadSaveAsync(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            try
            {
                var snap = await Db.Collection(SavesCollection).Document(uid).GetSnapshotAsync();
                if (!snap.Exists) return null;
                var data = snap.ToDictionary();
                string json = Str(data, "saveJson");
                if (string.IsNullOrEmpty(json)) return null;
                var result = new CloudSaveSnapshot
                {
                    SaveJson = json,
                    ClientSaveTimeUtc = Str(data, "clientSaveTimeUtc") ?? ""
                };
                if (data.TryGetValue("updatedAt", out var raw) && raw is Timestamp ts)
                    result.CloudUpdatedAtUtc = ts.ToDateTime();
                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Cloud save download failed: {e.Message}");
                return null;
            }
        }

        public async Task<bool> UploadSaveAsync(string uid, string saveJson, string clientSaveTimeUtc)
        {
            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(saveJson)) return false;
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "saveJson", saveJson },
                    { "saveVersion", CurrentSaveVersion },
                    { "clientSaveTimeUtc", clientSaveTimeUtc ?? "" },
                    { "updatedAt", FieldValue.ServerTimestamp }
                };
                await Db.Collection(SavesCollection).Document(uid).SetAsync(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Cloud save upload failed: {e.Message}");
                return false;
            }
        }

        public async Task<bool> PushProfileAsync(string uid, string username)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            try
            {
                var doc = Db.Collection(UsersCollection).Document(uid);
                var existing = await doc.GetSnapshotAsync();
                if (!existing.Exists)
                {
                    var create = new Dictionary<string, object>
                    {
                        { "username", username ?? "" },
                        { "createdAt", FieldValue.ServerTimestamp },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    };
                    await doc.SetAsync(create);
                }
                else
                {
                    // Merge: only username + updatedAt ever change
                    // after creation (the rules enforce exactly this).
                    var update = new Dictionary<string, object>
                    {
                        { "username", username ?? "" },
                        { "updatedAt", FieldValue.ServerTimestamp }
                    };
                    await doc.SetAsync(update, SetOptions.MergeAll);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Profile push failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Account deletion (one-to-one binding's
        /// "start over" path): remove the save, the public
        /// profile, and the leaderboard entry for this uid.
        /// Each delete is attempted even if another fails, so
        /// a retry starts from the least leftover state.</summary>
        public async Task<bool> DeleteAccountDataAsync(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            bool ok = true;
            try { await Db.Collection(SavesCollection).Document(uid).DeleteAsync(); }
            catch (Exception e) { ok = false; Debug.LogWarning($"[Lunar Dominion] Cloud save delete failed: {e.Message}"); }
            try { await Db.Collection(UsersCollection).Document(uid).DeleteAsync(); }
            catch (Exception e) { ok = false; Debug.LogWarning($"[Lunar Dominion] Profile delete failed: {e.Message}"); }
            try { await Db.Collection(LairBoardCollection).Document(uid).DeleteAsync(); }
            catch (Exception e) { ok = false; Debug.LogWarning($"[Lunar Dominion] Leaderboard entry delete failed: {e.Message}"); }
            return ok;
        }

        // ---- leaderboard ----------------------------------------------------

        public async Task<bool> PushLairDamageAsync(LeaderboardEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Uid)) return false;
            try
            {
                var data = new Dictionary<string, object>
                {
                    { "username", entry.Username ?? "" },
                    { "score", entry.Score },
                    { "bossDamage", entry.BossDamage },
                    { "droneDamage", entry.DroneDamage },
                    { "bossLevelDefeated", entry.BossLevelDefeated },
                    { "playerLevel", entry.PlayerLevel },
                    { "updatedAt", FieldValue.ServerTimestamp }
                };
                await Db.Collection(LairBoardCollection).Document(entry.Uid).SetAsync(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Leaderboard push failed: {e.Message}");
                return false;
            }
        }

        public async Task<List<LeaderboardEntry>> FetchTopLairDamageAsync(int count)
        {
            var result = new List<LeaderboardEntry>();
            try
            {
                var query = Db.Collection(LairBoardCollection)
                    .OrderByDescending("score")
                    .Limit(Math.Max(1, count));
                var snap = await query.GetSnapshotAsync();
                foreach (var doc in snap.Documents)
                {
                    var data = doc.ToDictionary();
                    result.Add(new LeaderboardEntry
                    {
                        Uid = doc.Id,
                        Username = Str(data, "username") ?? "",
                        Score = Int(data, "score"),
                        BossDamage = Int(data, "bossDamage"),
                        DroneDamage = Int(data, "droneDamage"),
                        BossLevelDefeated = Int(data, "bossLevelDefeated"),
                        PlayerLevel = Int(data, "playerLevel")
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lunar Dominion] Leaderboard fetch failed: {e.Message}");
            }
            return result;
        }

        // ---- field helpers --------------------------------------------------

        private static string Str(Dictionary<string, object> data, string key)
        {
            return data != null && data.TryGetValue(key, out var raw) ? raw as string : null;
        }

        private static int Int(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.TryGetValue(key, out var raw) || raw == null) return 0;
            try { return Convert.ToInt32(raw); }
            catch { return 0; }
        }
    }
}
#endif
