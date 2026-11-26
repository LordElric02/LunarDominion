using System.Collections.Generic;
using System.Threading.Tasks;

namespace LunarDominion.Accounts
{
    /// <summary>Cloud save surface (Roadmap Phase 2). The payload
    /// is the COMPLETE SaveData JSON — byte-for-byte what
    /// SaveService writes to the local file (Patrick's scope
    /// confirmation, 2026-11-24: most of the app data lives in
    /// Firebase too, not a subset). SaveService itself is never
    /// modified: uploads read the save file after a local save,
    /// downloads are staged to the file for the next load.
    /// Implementations: LocalCloudServices (stub),
    /// FirebaseCloudServices (behind `#if FIREBASE_SDK`).</summary>
    public interface ICloudSaveService
    {
        /// <summary>False for the stub / unavailable backend.
        /// Callers fail soft: the local save is always enough
        /// to play.</summary>
        bool IsAvailable { get; }

        /// <summary>The cloud save for this uid, or null when the
        /// account has none yet (first-sign-in migration uploads
        /// the local save in that case).</summary>
        Task<CloudSaveSnapshot> DownloadSaveAsync(string uid);

        /// <summary>Upload the full save JSON. clientSaveTimeUtc
        /// is the payload's LastSaveTimeUtc, stored as metadata
        /// so newest-wins can be decided without parsing the
        /// payload.</summary>
        Task<bool> UploadSaveAsync(string uid, string saveJson, string clientSaveTimeUtc);

        /// <summary>Write/refresh the public profile doc
        /// (users/{uid} — username only, the name other players
        /// see next to scores).</summary>
        Task<bool> PushProfileAsync(string uid, string username);

        /// <summary>Delete every cloud doc owned by this uid
        /// (save, profile, leaderboard entry) — the cloud side
        /// of deleting an account under the one-to-one binding
        /// model. Best effort: returns false if any delete
        /// failed, and the caller reports what it can.</summary>
        Task<bool> DeleteAccountDataAsync(string uid);
    }

    /// <summary>Leaderboard surface: push the local Robot Lair
    /// Damage career totals, fetch the public top-N board. Only
    /// score fields + the display name ever travel this path —
    /// never save data.</summary>
    public interface ILeaderboardSync
    {
        bool IsAvailable { get; }

        Task<bool> PushLairDamageAsync(LeaderboardEntry entry);

        /// <summary>Top entries by score, descending. Empty list
        /// when unavailable/offline — the view falls back to the
        /// local entry it already shows.</summary>
        Task<List<LeaderboardEntry>> FetchTopLairDamageAsync(int count);
    }
}
