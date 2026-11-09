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
    /// the outcome on arrival. The whole army goes (strongest first,
    /// capped at the squad size), the marker marches visibly from the
    /// castle, and the army marches back. Nodes pay their amount on
    /// arrival (storage cap applies) and then deplete for 8 hours.
    /// Camps reuse ExpeditionService.TryStart exactly — same tier gate,
    /// energy cost, no-refund-on-defeat rule, rewards, XP, and permanent
    /// casualties — so a map fight and a menu fight are the same fight.
    /// Energy is spent and combat runs at ARRIVAL, not at send, so
    /// cancelling (leaving the Map tab) before arrival costs nothing.
    /// This service owns the rules; the march itself is visual and lives
    /// in ExteriorView. It owns no UI — results go out as events.</summary>
    public class MapExcursionService
    {
        // Node regen time after collection (Patrick-approved default).
        public const float NodeRegenHours = 8f;

        // Raised when an excursion resolves (arrival outcome) or cannot
        // start — title + multi-line body for the info panel.
        public event Action<string, string> OnExcursionResult;
        // Raised when marching starts, finishes, or is cancelled, so the
        // panel can refresh its Send button.
        public event Action OnExcursionStateChanged;

        public bool IsMarching => _marching;

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
        private bool _marching;

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
            if (_marching)
            {
                reason = "An excursion is already underway.";
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
            if (!CanSend(point, out var reason))
            {
                OnExcursionResult?.Invoke(point != null ? point.Title : "Excursion", reason);
                return;
            }
            _activePoint = point;
            _marching = true;
            OnExcursionStateChanged?.Invoke();
            // Outbound: castle (origin) to the point, 1 unit above ground.
            _view.StartMarch(new Vector3(0f, 1f, 0f), point.Position + new Vector3(0f, 1f, 0f), OnArrivedAtTarget);
        }

        /// <summary>Cancel an in-flight excursion (leaving the Map tab).
        /// No outcome is applied — energy is only spent and combat only
        /// runs at arrival, so cancelling before then costs nothing.</summary>
        public void Cancel()
        {
            if (!_marching) return;
            _view.CancelMarch();
            _marching = false;
            _activePoint = null;
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
            if (point.Kind != MapPointKind.Camp) CollectNode(point);
            else FightCamp(point);
            // Return march: back to the castle, then the excursion ends.
            _view.StartMarch(point.Position + new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f), FinishMarch);
        }

        private void FinishMarch()
        {
            _marching = false;
            _activePoint = null;
            _view.CancelMarch();
            OnExcursionStateChanged?.Invoke();
        }

        private void CollectNode(MapPointData point)
        {
            var amount = new ResourceCost(
                point.Kind == MapPointKind.Biomass ? point.Amount : 0,
                point.Kind == MapPointKind.Alloy ? point.Amount : 0,
                point.Kind == MapPointKind.Helium3 ? point.Amount : 0);
            string resourceName = point.Kind == MapPointKind.Biomass ? "Biomass"
                : point.Kind == MapPointKind.Alloy ? "Alloy" : "Helium-3";
            int beforeB = _state.Biomass, beforeA = _state.Alloy, beforeH = _state.Helium3;
            // Earn clamps to the storage cap; report what actually landed.
            _economy.Earn(amount);
            int gained = (_state.Biomass - beforeB) + (_state.Alloy - beforeA) + (_state.Helium3 - beforeH);
            // Deplete the node (UTC now + regen) and save immediately so
            // the depletion survives an app restart mid-excursion.
            if (_state.NodeRegenUtc == null || _state.NodeRegenUtc.Length != 9)
                _state.NodeRegenUtc = new string[9];
            _state.NodeRegenUtc[point.Index] = DateTime.UtcNow.AddHours(NodeRegenHours).ToString("o");
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            var body = new StringBuilder();
            body.AppendLine(gained > 0
                ? $"Collected +{gained} {resourceName}."
                : $"Storage full — no {resourceName} collected.");
            body.Append($"Node depleted — regenerates in {NodeRegenHours}h.");
            OnExcursionResult?.Invoke(point.Title, body.ToString());
        }

        private void FightCamp(MapPointData point)
        {
            var encounter = EncounterForTier(point.Tier);
            var squad = BuildSquadIndices();
            if (encounter == null || squad.Count == 0)
            {
                OnExcursionResult?.Invoke(point.Title, "Could not start the fight.");
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
                OnExcursionResult?.Invoke(
                    result.Victory ? "VICTORY!" : (result.Timeout ? "DEFEAT — TIMEOUT" : "DEFEAT"),
                    FormatBattleResult(result));
            }
            else
            {
                OnExcursionResult?.Invoke(point.Title, reason);
            }
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
