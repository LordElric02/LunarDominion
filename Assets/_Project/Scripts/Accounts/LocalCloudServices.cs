using System.Collections.Generic;
using System.Threading.Tasks;

namespace LunarDominion.Accounts
{
    /// <summary>Stub cloud save + leaderboard sync (the pair to
    /// LocalAccountService): every call reports unavailable /
    /// empty, so sync attempts quietly no-op and the game keeps
    /// running purely on the local save — today's behaviour,
    /// preserved exactly until the Firebase SDK lights up the
    /// real implementations.</summary>
    public sealed class LocalCloudServices : ICloudSaveService, ILeaderboardSync
    {
        public bool IsAvailable => false;

        public Task<CloudSaveSnapshot> DownloadSaveAsync(string uid)
            => Task.FromResult<CloudSaveSnapshot>(null);

        public Task<bool> UploadSaveAsync(string uid, string saveJson, string clientSaveTimeUtc)
            => Task.FromResult(false);

        public Task<bool> PushProfileAsync(string uid, string username)
            => Task.FromResult(false);

        public Task<bool> PushLairDamageAsync(LeaderboardEntry entry)
            => Task.FromResult(false);

        public Task<List<LeaderboardEntry>> FetchTopLairDamageAsync(int count)
            => Task.FromResult(new List<LeaderboardEntry>());
    }
}
