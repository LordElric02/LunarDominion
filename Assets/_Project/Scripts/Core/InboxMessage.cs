using System;

namespace LunarDominion.Core
{
    /// <summary>One Inbox message (2026-11-15, Patrick): a game status
    /// delivered to the player's Inbox instead of a pop-up — excursion
    /// battle results, harvests, boss fight reports, the welcome
    /// message. Saved in the game save (see SaveService); Utc is a
    /// round-trip ("o") timestamp string, displayed in local time by
    /// the Inbox view. Plain data, serialized by JsonUtility as part
    /// of a flat List (never a jagged array).</summary>
    [Serializable]
    public class InboxMessage
    {
        public long Id;
        public string Utc = "";
        public string Title = "";
        public string Body = "";
        public bool Read;
    }
}
