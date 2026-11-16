using System;
using LunarDominion.Core;

namespace LunarDominion.Inbox
{
    /// <summary>The Inbox (2026-11-15, Patrick): every game status —
    /// excursion battle results, harvests, boss fight reports — is
    /// delivered here instead of a pop-up panel, so a march that
    /// completes while the player manages the base still reports in.
    /// Messages live in GameState (saved); this service is the only
    /// writer. The state is read through a provider so a DEV reset
    /// (which replaces the GameState) can never strand the inbox on
    /// the old state.</summary>
    public class InboxService
    {
        public const string WelcomeTitle = "Welcome to Lunar Dominion";
        public const string WelcomeBody =
            "Welcome, Commander.\n" +
            "Build up your Nexus, raise your armies, gather Biomass, Alloy and Helium-3, " +
            "and take the fight to A.R.E.S.-09 in its lair.\n" +
            "Field reports — battles, harvests, and boss fights — arrive here in your Inbox, " +
            "even while you are managing the base. The badge by the Inbox button shows how " +
            "many are unread.";

        private readonly Func<GameState> _stateOf;
        private readonly Action _saveGame;
        private readonly Action _notifyChanged;
        private long _nextId = 1;

        /// <summary>Raised after any change (add / read / seed), so
        /// the Inbox view can refresh while it is open. The top bar
        /// badge refreshes through the normal state-changed flow.</summary>
        public event Action OnChanged;

        public InboxService(Func<GameState> stateOf, Action saveGame, Action notifyChanged)
        {
            _stateOf = stateOf;
            _saveGame = saveGame;
            _notifyChanged = notifyChanged;
            var s = State;
            if (s != null && s.Inbox != null)
            {
                foreach (var m in s.Inbox)
                    if (m != null && m.Id >= _nextId) _nextId = m.Id + 1;
            }
        }

        private GameState State => _stateOf != null ? _stateOf() : null;

        /// <summary>All messages, oldest first (the view reverses
        /// for display). Null when there is no state.</summary>
        public System.Collections.Generic.List<InboxMessage> Messages
            => State != null ? State.Inbox : null;

        public int UnreadCount
        {
            get
            {
                var s = State;
                if (s == null || s.Inbox == null) return 0;
                int n = 0;
                foreach (var m in s.Inbox) if (m != null && !m.Read) n++;
                return n;
            }
        }

        /// <summary>Deliver the one-time welcome message. Called on
        /// startup; old saves (which load unseeded) receive it too.
        /// Idempotent — the seeded flag lives in the save.</summary>
        public void EnsureWelcome()
        {
            var s = State;
            if (s == null || s.InboxSeeded) return;
            s.InboxSeeded = true;
            AddInternal(s, WelcomeTitle, WelcomeBody);
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            OnChanged?.Invoke();
        }

        /// <summary>Deliver a status message (unread). Trims the
        /// inbox to the cap, saves, and notifies.</summary>
        public void Add(string title, string body)
        {
            var s = State;
            if (s == null) return;
            AddInternal(s, title ?? "", body ?? "");
            Trim(s);
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            OnChanged?.Invoke();
        }

        public void MarkRead(long id)
        {
            var s = State;
            if (s == null || s.Inbox == null) return;
            foreach (var m in s.Inbox)
            {
                if (m == null || m.Id != id || m.Read) continue;
                m.Read = true;
                _saveGame?.Invoke();
                _notifyChanged?.Invoke();
                OnChanged?.Invoke();
                return;
            }
        }

        public void MarkAllRead()
        {
            var s = State;
            if (s == null || s.Inbox == null) return;
            bool changed = false;
            foreach (var m in s.Inbox)
            {
                if (m != null && !m.Read) { m.Read = true; changed = true; }
            }
            if (!changed) return;
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            OnChanged?.Invoke();
        }

        private void AddInternal(GameState s, string title, string body)
        {
            if (s.Inbox == null) s.Inbox = new System.Collections.Generic.List<InboxMessage>();
            s.Inbox.Add(new InboxMessage
            {
                Id = _nextId++,
                Utc = DateTime.UtcNow.ToString("o"),
                Title = title,
                Body = body,
                Read = false
            });
            Trim(s);
        }

        // Keep at most MaxInboxMessages: drop the oldest READ message
        // first; only drop an unread one when nothing read remains.
        private static void Trim(GameState s)
        {
            if (s.Inbox == null) return;
            while (s.Inbox.Count > GameState.MaxInboxMessages)
            {
                int drop = 0;
                for (int i = 0; i < s.Inbox.Count; i++)
                {
                    if (s.Inbox[i] != null && s.Inbox[i].Read) { drop = i; break; }
                }
                s.Inbox.RemoveAt(drop);
            }
        }
    }
}
