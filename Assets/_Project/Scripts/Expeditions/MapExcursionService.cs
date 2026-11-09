using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LunarDominion.Core;
using LunarDominion.Economy;
using LunarDominion.UI;
using LunarDominion.Units;
using UnityEngine;

namespace LunarDominion.Expeditions
{
    /// <summary>Map excursions (2026-11-09, Patrick's plan): tap a resource
    /// node or enemy camp on the exterior map, send the army, and resolve
    /// the outcome. The player picks the squad in a picker (2026-11-09):
    /// it opens pre-filled with the whole army (strongest first, capped
    /// at the squad size) and they trim it; the chosen squad marches
    /// visibly from the castle. Nodes are OCCUPIED, not collected
    /// instantly (2026-11-09): on arrival the army stays and farms the
    /// node for 120 min / troops sent (8 troops = 15 min), the node
    /// turns amber while it is being worked, and when the time is up
    /// the harvest lands, the army marches back, and the node vanishes
    /// until it regenerates in 8h. The occupation is saved, so it
    /// survives an app restart — if the time ends while the app is
    /// closed, the harvest is granted on the next load. Camps reuse
    /// ExpeditionService.TryStart exactly — same tier gate (bypassed on
    /// the map with a warning), energy cost, no-refund-on-defeat rule,
    /// rewards, XP, and permanent casualties — so a map fight and a menu
    /// fight are the same fight. A defeated camp vanishes (2026-11-09)
    /// with its creatures, and another of the same tier respawns at a
    /// different spot after CampRespawnHours; losing leaves the camp in
    /// place. Energy is spent and combat runs at ARRIVAL, not at send, so cancelling an outbound march (leaving
    /// the Map tab) before arrival costs nothing; an active occupation
    /// is never cancelled by leaving the tab — the army keeps farming.
    /// This service owns the rules; the march itself is visual and lives
    /// in ExteriorView. It owns no UI — results go out as events.</summary>
    public class MapExcursionService
    {
        // Node regen time after collection (Patrick-approved default).
        public const float NodeRegenHours = 8f;
        // Occupation: 120 min / troops sent (8 troops = 15 min).
        public const float OccupationMinutesPerTroop = 120f;
        // Camp respawn: a defeated camp vanishes, and another of the
        // same tier respawns elsewhere after this many real-time hours.
        // Shortened to 1h (2026-11-09, Patrick) so it is quick to see
        // in testing; raise it again for a slower live pace.
        public const float CampRespawnHours = 1f;

        // Raised when an excursion resolves (arrival outcome) or cannot
        // start — title + multi-line body for the info panel.
        public event Action<string, string> OnExcursionResult;
        // Raised when marching starts, finishes, or is cancelled, so the
        // panel can refresh its Send button.
        public event Action OnExcursionStateChanged;

        public bool IsMarching => _marching;
        // True while the army is out — marching or farming a node.
        // CanSend refuses while busy; the Expeditions menu is untouched.
        public bool IsBusy => _marching || _occupying;

        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly ExpeditionService _expeditions;
        private readonly BalanceData _balance;
        private readonly List<UnitDefinition> _units;
        private readonly List<EncounterDefinition> _encounters;
        private readonly ExteriorView _view;
        private readonly Action _saveGame;
        private readonly Action _notifyChanged;

        private MapPointData _activePoint;
        private List<int> _activeSquad;
        private bool _marching;
        private bool _occupying;

        public MapExcursionService(GameState state, EconomyService economy, ExpeditionService expeditions,
            BalanceData balance, List<UnitDefinition> units, List<EncounterDefinition> encounters,
            ExteriorView view, Action saveGame, Action notifyChanged)
        {
            _state = state;
            _economy = economy;
            _expeditions = expeditions;
            _balance = balance;
            _units = units;
            _encounters = encounters;
            _view = view;
            _saveGame = saveGame;
            _notifyChanged = notifyChanged;
            // Restore an occupation from the save (previous session):
            // the army is still farming, so the service is busy until
            // it completes (CheckOccupationDue runs on the next refresh).
            if (state.OccupiedNodeIndex >= 0 && state.OccupiedNodeIndex <= 8
                && !string.IsNullOrEmpty(state.OccupationEndUtc)
                && DateTime.TryParse(state.OccupationEndUtc, null, DateTimeStyles.RoundtripKind, out _))
            {
                _occupying = true;
                _activePoint = view != null ? view.NodeData(state.OccupiedNodeIndex) : null;
            }
            // Restore camp positions/visibility from the save: a camp
            // defeated in a previous session is still vanished (until
            // its respawn time), and a respawned camp sits at its saved
            // spot, not the original one.
            SyncCamps();
        }

        /// <summary>Can an excursion be sent to this point right now?
        /// Nodes need a non-empty army and must not be depleted; camps
        /// need a non-empty army and enough energy. The tier gate does
        /// NOT block a map excursion (2026-11-09): the player may attack
        /// a camp above their Nexus and the fight proceeds (and likely
        /// fails) — WarningFor carries the too-strong warning instead.</summary>
        public bool CanSend(MapPointData point, out string reason)
        {
            reason = "";
            if (point == null)
            {
                reason = "No target selected.";
                return false;
            }
            if (_marching || _occupying)
            {
                reason = _occupying
                    ? "Your army is busy farming a node."
                    : "An excursion is already underway.";
                return false;
            }
            if (_state.Army == null || _state.Army.Count == 0)
            {
                reason = "Recruit units first — your army is empty.";
                return false;
            }
            if (point.Kind != MapPointKind.Camp)
            {
                if (IsNodeDepleted(point.Index, out var remaining))
                {
                    reason = $"Depleted — regenerates in {FormatRemaining(remaining)}.";
                    return false;
                }
                reason = "Ready to send.";
                return true;
            }
            var encounter = EncounterForTier(point.Tier);
            if (encounter == null)
            {
                reason = "No encounter for this camp.";
                return false;
            }
            if (IsCampDefeated(point.Tier))
            {
                reason = "This camp was defeated — a new one will appear elsewhere.";
                return false;
            }
            if (_state.Energy < encounter.EnergyCost)
            {
                reason = $"Not enough energy ({_state.Energy} / {encounter.EnergyCost}).";
                return false;
            }
            reason = "Ready to send.";
            return true;
        }

        /// <summary>Warning shown in the panel for a camp above the
        /// player's Nexus (2026-11-09): the fight is allowed, but the
        /// enemy is far stronger. Null when there is nothing to warn
        /// about (nodes, or a camp at/below the player's Nexus).</summary>
        public string WarningFor(MapPointData point)
        {
            if (point == null || point.Kind != MapPointKind.Camp) return null;
            var encounter = EncounterForTier(point.Tier);
            if (encounter == null) return null;
            if (_state.NexusLevel >= encounter.RequiredNexusLevel) return null;
            return $"Warning: this enemy is far stronger than your army — Tier {point.Tier} unlocks at Nexus {encounter.RequiredNexusLevel} (you are Nexus {_state.NexusLevel}). Your squad is likely to be wiped out.";
        }

        public void StartExcursion(MapPointData point)
        {
            StartExcursion(point, null);
        }

        /// <summary>Send a chosen squad (army indices). Null or empty
        /// falls back to the whole army, strongest first, capped — the
        /// previous default. The squad is stored for arrival, so the
        /// farming duration and the fight both use the units the player
        /// picked, not the whole army.</summary>
        public void StartExcursion(MapPointData point, List<int> squadIndices)
        {
            if (!CanSend(point, out var reason))
            {
                OnExcursionResult?.Invoke(point != null ? point.Title : "Excursion", reason);
                return;
            }
            _activePoint = point;
            _activeSquad = squadIndices != null && squadIndices.Count > 0
                ? new List<int>(squadIndices)
                : BuildSquadIndices();
            _marching = true;
            OnExcursionStateChanged?.Invoke();
            // Outbound: castle (origin) to the point, 1 unit above ground.
            _view.StartMarch(new Vector3(0f, 1f, 0f), point.Position + new Vector3(0f, 1f, 0f), OnArrivedAtTarget);
        }

        /// <summary>Default squad for the picker: the whole army,
        /// strongest first, capped — as per-type counts. The picker
        /// opens pre-filled with this and the player trims it.</summary>
        public Dictionary<UnitId, int> DefaultSquadCounts()
        {
            var counts = new Dictionary<UnitId, int>();
            foreach (int i in BuildSquadIndices())
            {
                if (_state.Army == null || i < 0 || i >= _state.Army.Count) continue;
                var unit = _state.Army[i];
                if (unit == null) continue;
                if (!counts.ContainsKey(unit.Id)) counts[unit.Id] = 0;
                counts[unit.Id]++;
            }
            return counts;
        }

        /// <summary>Cancel an in-flight excursion (leaving the Map tab).
        /// An outbound march (before arrival) is cancelled with no
        /// outcome — energy is only spent and combat only runs at
        /// arrival, so cancelling before then costs nothing. An active
        /// OCCUPATION is never cancelled: the army keeps farming in
        /// real time, and only the marker hides (it reappears at the
        /// node when the Map reopens). A return march is cosmetic (the
        /// outcome already landed), so it just stops.</summary>
        public void Cancel()
        {
            if (_occupying)
            {
                _view.CancelMarch();
                return;
            }
            if (!_marching) return;
            _view.CancelMarch();
            _marching = false;
            _activePoint = null;
            _activeSquad = null;
            OnExcursionStateChanged?.Invoke();
        }

        private void OnArrivedAtTarget()
        {
            var point = _activePoint;
            if (point == null)
            {
                FinishMarch();
                return;
            }
            if (point.Kind != MapPointKind.Camp)
            {
                // Node: the army stays and farms it (occupation) instead
                // of collecting instantly. The return march starts when
                // the occupation completes.
                BeginOccupation(point);
                return;
            }
            FightCamp(point);
            // Nodes start an occupation (the return march waits until it
            // completes); camps fight — FightCamp plays the animation,
            // shows the result, and starts the return march itself.
        }

        private void FinishMarch()
        {
            _marching = false;
            _activePoint = null;
            _activeSquad = null;
            _view.CancelMarch();
            OnExcursionStateChanged?.Invoke();
        }

        // Node occupation (2026-11-09): on arrival the army stays and
        // farms the node for 120 min / troops sent (8 troops = 15 min).
        // The marker stays at the node, the node turns amber, and the
        // harvest lands when the time is up (CompleteOccupation). The
        // occupation is saved immediately, so it survives an app restart.
        private void BeginOccupation(MapPointData point)
        {
            var squad = ActiveSquad();
            int troops = Math.Max(1, squad.Count);
            float minutes = OccupationMinutesPerTroop / troops;
            _state.OccupiedNodeIndex = point.Index;
            _state.OccupationEndUtc = DateTime.UtcNow.AddMinutes(minutes).ToString("o");
            _state.OccupationAmount = point.Amount;
            _state.OccupationKind = (int)point.Kind;
            _state.OccupationTroops = troops;
            _saveGame?.Invoke();
            _marching = false;
            _occupying = true;
            _notifyChanged?.Invoke();
            OnExcursionStateChanged?.Invoke();
            string resourceName = ResourceName(point.Kind);
            OnExcursionResult?.Invoke(point.Title,
                $"Farming {resourceName} — {troops} troop{(troops == 1 ? "" : "s")}, about {FormatMinutes(minutes)}.\nThe army returns with the harvest when done.");
        }

        /// <summary>Called regularly (Bootstrap RefreshAll, ~once a
        /// second) and on load: if an occupation's end time has passed,
        /// complete it. Also completes an occupation that ended while
        /// the app was closed — the harvest is granted on the next load.</summary>
        public void CheckOccupationDue()
        {
            if (!_occupying && _state.OccupiedNodeIndex < 0) return;
            if (_state.OccupiedNodeIndex < 0 || _state.OccupiedNodeIndex > 8) return;
            if (string.IsNullOrEmpty(_state.OccupationEndUtc)) return;
            if (!DateTime.TryParse(_state.OccupationEndUtc, null, DateTimeStyles.RoundtripKind, out var endUtc)) return;
            if (DateTime.UtcNow < endUtc) return;
            CompleteOccupation();
        }

        private void CompleteOccupation()
        {
            int nodeIndex = _state.OccupiedNodeIndex;
            int amount = _state.OccupationAmount;
            var kind = (MapPointKind)Math.Max(0, Math.Min(_state.OccupationKind, 2));
            string resourceName = ResourceName(kind);
            var grant = new ResourceCost(
                kind == MapPointKind.Biomass ? amount : 0,
                kind == MapPointKind.Alloy ? amount : 0,
                kind == MapPointKind.Helium3 ? amount : 0);
            int beforeB = _state.Biomass, beforeA = _state.Alloy, beforeH = _state.Helium3;
            // Earn clamps to the storage cap; report what actually landed.
            _economy.Earn(grant);
            int gained = (_state.Biomass - beforeB) + (_state.Alloy - beforeA) + (_state.Helium3 - beforeH);
            // Deplete the node (UTC now + regen) and clear the occupation.
            if (_state.NodeRegenUtc == null || _state.NodeRegenUtc.Length != 9)
                _state.NodeRegenUtc = new string[9];
            _state.NodeRegenUtc[nodeIndex] = DateTime.UtcNow.AddHours(NodeRegenHours).ToString("o");
            ClearOccupation();
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            var body = new StringBuilder();
            body.AppendLine(gained > 0
                ? $"Collected +{gained} {resourceName}."
                : $"Storage full — no {resourceName} collected.");
            body.Append("Node depleted — it vanished and regenerates in 8h.");
            OnExcursionResult?.Invoke("Harvest complete", body.ToString());
            // Return march if the Map is open; otherwise the marker just
            // hides (the harvest already landed).
            _occupying = false;
            if (_view.IsVisible && _activePoint != null)
            {
                _view.StartMarch(_activePoint.Position + new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f), FinishMarch);
            }
            else
            {
                _activePoint = null;
                _view.CancelMarch();
                OnExcursionStateChanged?.Invoke();
            }
        }

        private void ClearOccupation()
        {
            _state.OccupiedNodeIndex = -1;
            _state.OccupationEndUtc = "";
            _state.OccupationAmount = 0;
            _state.OccupationKind = 0;
            _state.OccupationTroops = 0;
        }

        // Enemy camps (2026-11-09): a camp defeated in battle vanishes,
        // and another of the same tier respawns elsewhere after
        // CampRespawnHours. The camp and its creatures disappear
        // immediately; the respawn is a real-time timestamp in the save,
        // so it survives an app restart and can complete while closed.

        /// <summary>NodeDefs index for a camp tier (tier 1 → 9, 2 → 10,
        /// 3 → 11). Camps are indices 9-11 in ExteriorView.NodeDefs.</summary>
        private static int CampIndexForTier(int tier)
        {
            return 8 + tier;
        }

        /// <summary>True while this tier's camp is defeated and waiting
        /// to respawn (its CampRespawnUtc is set).</summary>
        public bool IsCampDefeated(int tier)
        {
            if (tier < 1 || tier > 3) return false;
            if (_state.CampRespawnUtc == null || _state.CampRespawnUtc.Length != 3) return false;
            return !string.IsNullOrEmpty(_state.CampRespawnUtc[tier - 1]);
        }

        // Vanish a defeated camp: record its respawn time, hide the
        // camp and its creatures, and save. The camp reappears at a
        // different spot when CheckCampRespawns runs after the time.
        private void VanishCamp(int tier)
        {
            if (tier < 1 || tier > 3) return;
            if (_state.CampRespawnUtc == null || _state.CampRespawnUtc.Length != 3)
                _state.CampRespawnUtc = new string[3];
            _state.CampRespawnUtc[tier - 1] = DateTime.UtcNow.AddHours(CampRespawnHours).ToString("o");
            _view.SetCampVisible(CampIndexForTier(tier), false);
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            OnExcursionStateChanged?.Invoke();
        }

        /// <summary>Called regularly (Bootstrap RefreshAll, ~once a
        /// second) and on load: if a defeated camp's respawn time has
        /// passed, bring another of the same tier back at a different
        /// spot. Also completes a respawn whose time passed while the
        /// app was closed — the new camp is there on the next load.</summary>
        public void CheckCampRespawns()
        {
            if (_state.CampRespawnUtc == null || _state.CampRespawnUtc.Length != 3) return;
            bool changed = false;
            for (int tier = 1; tier <= 3; tier++)
            {
                string s = _state.CampRespawnUtc[tier - 1];
                if (string.IsNullOrEmpty(s)) continue;
                if (!DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var respawnUtc)) continue;
                if (DateTime.UtcNow < respawnUtc) continue;
                // Pick a different spot than the one the camp just left.
                if (_state.CampSpotIndex == null || _state.CampSpotIndex.Length != 3)
                    _state.CampSpotIndex = new int[3];
                int current = _state.CampSpotIndex[tier - 1];
                int next = current;
                while (next == current) next = UnityEngine.Random.Range(0, 3);
                _state.CampSpotIndex[tier - 1] = next;
                _state.CampRespawnUtc[tier - 1] = "";
                _view.MoveCampToSpot(CampIndexForTier(tier), next);
                _view.SetCampVisible(CampIndexForTier(tier), true);
                changed = true;
            }
            if (changed)
            {
                _saveGame?.Invoke();
                _notifyChanged?.Invoke();
                OnExcursionStateChanged?.Invoke();
            }
        }

        /// <summary>Position active camps at their saved spots and hide
        /// defeated ones, from the save. Called on load (constructor)
        /// and whenever the Map is shown (the view's SetVisible re-shows
        /// every point, so a defeated camp must be re-hidden and a
        /// respawned camp re-placed). No-op for old saves (all active
        /// at spot 0, the original positions).</summary>
        public void SyncCamps()
        {
            if (_view == null) return;
            if (_state.CampSpotIndex == null || _state.CampSpotIndex.Length != 3)
                _state.CampSpotIndex = new int[3];
            for (int tier = 1; tier <= 3; tier++)
            {
                int campIndex = CampIndexForTier(tier);
                if (IsCampDefeated(tier))
                {
                    _view.SetCampVisible(campIndex, false);
                }
                else
                {
                    _view.MoveCampToSpot(campIndex, _state.CampSpotIndex[tier - 1]);
                    _view.SetCampVisible(campIndex, true);
                }
            }
        }

        /// <summary>True while this node is being farmed (occupation
        /// active, in this session or restored from the save).</summary>
        public bool IsNodeOccupied(int nodeIndex)
        {
            return _state.OccupiedNodeIndex == nodeIndex && !string.IsNullOrEmpty(_state.OccupationEndUtc);
        }

        /// <summary>Occupation detail for the panel: troops farming and
        /// time left. False when the node is not occupied.</summary>
        public bool OccupationInfo(int nodeIndex, out int troops, out TimeSpan remaining)
        {
            troops = 0;
            remaining = TimeSpan.Zero;
            if (!IsNodeOccupied(nodeIndex)) return false;
            if (!DateTime.TryParse(_state.OccupationEndUtc, null, DateTimeStyles.RoundtripKind, out var endUtc)) return false;
            troops = _state.OccupationTroops;
            remaining = endUtc - DateTime.UtcNow;
            return true;
        }

        /// <summary>Restore the marker at the occupied node when the Map
        /// reopens (or on a refresh) so the army is visibly farming
        /// there. No-op when nothing is occupied or the Map is hidden.</summary>
        public void RefreshMarkerVisual()
        {
            if (!_occupying && _state.OccupiedNodeIndex < 0) return;
            if (_state.OccupiedNodeIndex < 0 || _state.OccupiedNodeIndex > 8) return;
            if (!_view.IsVisible || _marching) return;
            // The occupied node is found by index; its position comes
            // from the active point when this session started it, or
            // from the saved index via the view's node positions.
            Vector3 pos;
            if (_activePoint != null && _activePoint.Index == _state.OccupiedNodeIndex)
            {
                pos = _activePoint.Position;
            }
            else if (!_view.NodePosition(_state.OccupiedNodeIndex, out pos))
            {
                return;
            }
            _view.ShowMarkerAt(pos + new Vector3(0f, 1f, 0f));
        }

        private static string ResourceName(MapPointKind kind)
        {
            return kind == MapPointKind.Biomass ? "Biomass"
                : kind == MapPointKind.Alloy ? "Alloy" : "Helium-3";
        }

        private static string FormatMinutes(float minutes)
        {
            int m = (int)Math.Round(minutes);
            if (m >= 60) return $"{m / 60}h {m % 60}m";
            return $"{Math.Max(1, m)} min";
        }

        private void FightCamp(MapPointData point)
        {
            var encounter = EncounterForTier(point.Tier);
            var squad = ActiveSquad();
            if (encounter == null || squad.Count == 0)
            {
                OnExcursionResult?.Invoke(point.Title, "Could not start the fight.");
                StartReturnMarch(point);
                return;
            }
            // Same fight as the Expeditions menu: energy spent at start,
            // no refund on defeat, rewards only on victory, casualties
            // permanent. TryStart applies all of it. allowLockedTier:
            // map excursions let the player attack above their Nexus —
            // the fight proceeds and (almost certainly) fails.
            if (_expeditions.TryStart(encounter, squad, out var result, out var reason, allowLockedTier: true))
            {
                _saveGame?.Invoke();
                _notifyChanged?.Invoke();
                // Play the fight animation first (~2s on the map), then
                // show the result, vanish the camp on victory, and march
                // back. The combat itself already resolved above — the
                // animation is purely visual.
                _view.PlayFightAnimation(point.Index, result.Victory, () =>
                {
                    OnExcursionResult?.Invoke(
                        result.Victory ? "VICTORY!" : (result.Timeout ? "DEFEAT — TIMEOUT" : "DEFEAT"),
                        FormatBattleResult(result));
                    // A defeated camp vanishes (2026-11-09): the camp and
                    // its creatures disappear, and another of the same tier
                    // respawns elsewhere after CampRespawnHours. Losing
                    // leaves the camp in place, so the player can retry.
                    if (result.Victory) VanishCamp(point.Tier);
                    StartReturnMarch(point);
                });
            }
            else
            {
                OnExcursionResult?.Invoke(point.Title, reason);
                StartReturnMarch(point);
            }
        }

        // Return march: back to the castle, then the excursion ends.
        private void StartReturnMarch(MapPointData point)
        {
            _view.StartMarch(point.Position + new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f), FinishMarch);
        }

        /// <summary>The squad chosen for this excursion, or the whole
        /// army (strongest first, capped) when none was chosen.</summary>
        private List<int> ActiveSquad()
        {
            if (_activeSquad != null && _activeSquad.Count > 0) return _activeSquad;
            return BuildSquadIndices();
        }

        /// <summary>Whole army, strongest first, capped at the squad size.
        /// Per-unit power is the same formula as PowerService: (HP +
        /// 8*ATK + 4*DEF) * (1 + 0.08*(level-1)).</summary>
        private List<int> BuildSquadIndices()
        {
            var scored = new List<(int index, long power)>();
            if (_state.Army == null) return new List<int>();
            for (int i = 0; i < _state.Army.Count; i++)
            {
                var unit = _state.Army[i];
                if (unit == null) continue;
                var def = _units != null ? _units.Find(u => u.Id == unit.Id) : null;
                if (def == null) continue;
                long basePower = def.BaseHealth + 8L * def.BaseAttack + 4L * def.BaseDefense;
                long power = (long)Math.Round(basePower * (1.0 + 0.08 * (Math.Max(1, unit.Level) - 1)));
                scored.Add((i, power));
            }
            scored.Sort((a, b) => b.power.CompareTo(a.power));
            var result = new List<int>();
            int count = Math.Min(scored.Count, _balance.MaxSquadSize);
            for (int i = 0; i < count; i++) result.Add(scored[i].index);
            return result;
        }

        private EncounterDefinition EncounterForTier(int tier)
        {
            if (_encounters == null) return null;
            return _encounters.Find(e => e != null && e.Tier == tier);
        }

        /// <summary>True while the node's regen time is still in the future.
        /// A missing or unparseable timestamp means available.</summary>
        public bool IsNodeDepleted(int nodeIndex)
        {
            return IsNodeDepleted(nodeIndex, out _);
        }

        public bool IsNodeDepleted(int nodeIndex, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (_state.NodeRegenUtc == null || nodeIndex < 0 || nodeIndex >= _state.NodeRegenUtc.Length) return false;
            string s = _state.NodeRegenUtc[nodeIndex];
            if (string.IsNullOrEmpty(s)) return false;
            if (!DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var regenUtc)) return false;
            remaining = regenUtc - DateTime.UtcNow;
            return remaining > TimeSpan.Zero;
        }

        public static string FormatRemaining(TimeSpan remaining)
        {
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            if (remaining.TotalMinutes >= 1)
                return $"{(int)remaining.TotalMinutes}m";
            return "under a minute";
        }

        // Same body as the Expeditions result panel, so a map fight and
        // a menu fight read identically.
        private static string FormatBattleResult(BattleResult result)
        {
            var body = new StringBuilder();
            body.AppendLine($"{result.EncounterName}  •  {result.Rounds} rounds  •  Energy spent: {result.EnergySpent}");
            if (result.Victory)
            {
                body.AppendLine($"Reward: {CostText(result.RewardEarned)}");
                if (result.Flawless) body.AppendLine("Flawless victory: +25% XP bonus applied.");
            }
            else
            {
                body.AppendLine("No reward (defeat). Energy is not refunded.");
            }
            body.AppendLine(result.CasualtyNames.Count == 0
                ? "Casualties: none"
                : "Casualties: " + string.Join(", ", result.CasualtyNames));
            body.AppendLine($"XP pool: {result.XpPool}");
            if (result.XpGains.Count == 0)
            {
                body.AppendLine("No XP gained.");
            }
            else
            {
                foreach (var gain in result.XpGains)
                {
                    string levelText = gain.NewLevel > gain.OldLevel
                        ? $"Lv {gain.OldLevel} → Lv {gain.NewLevel}"
                        : $"Lv {gain.OldLevel}";
                    body.AppendLine($"{gain.UnitName}: +{gain.XpGained} XP ({levelText})");
                }
            }
            return body.ToString();
        }

        private static string CostText(ResourceCost cost)
        {
            var parts = new List<string>();
            if (cost.Biomass > 0) parts.Add($"{cost.Biomass} Biomass");
            if (cost.Alloy > 0) parts.Add($"{cost.Alloy} Alloy");
            if (cost.Helium3 > 0) parts.Add($"{cost.Helium3} Helium-3");
            return parts.Count > 0 ? string.Join(", ", parts) : "None";
        }
    }
}
