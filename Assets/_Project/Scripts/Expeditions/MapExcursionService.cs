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
    /// node, enemy camp, or the boss lair on the exterior map, send the
    /// army, and resolve the outcome. The player picks the squad in a
    /// picker (2026-11-09):
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
    /// fight are the same fight. A defeated camp vanishes (2026-11-09;
    /// camp field 2026-11-10) with its creatures, and its slot refills
    /// after CampRespawnMinutes with a fresh tier rolled by Nexus
    /// level — 15 slots across the map, so there is (energy aside)
    /// always something to attack; losing leaves the camp in
    /// place. The boss lair (2026-11-09; ladder 2026-11-10) is a single
    /// Tier 4 encounter (War Robot) that never vanishes — the player
    /// fights it in the lair view (heroes, 15-level ladder) at will,
    /// energy permitting. Energy is spent and combat runs at ARRIVAL,
    /// not at send, so cancelling an outbound march (leaving the Map
    /// tab) before arrival costs nothing; an active occupation is never
    /// cancelled by leaving the tab — the army keeps farming.
    /// This service owns the rules; the march itself is visual and lives
    /// in ExteriorView. It owns no UI — results go out as events.
    /// Two armies (2026-11-11, Patrick; the second army is gated at
    /// Nexus SecondArmyNexusGate): the service runs TWO excursion
    /// slots — slot 0 is Army 1, slot 1 is Army 2 — and each slot
    /// marches, fights, and farms independently with its own marker,
    /// with both occupations persisted per slot. A slot snapshots its
    /// squad as unit REFERENCES at send time, so the other army's
    /// fight (which injures units and shifts Army indices) can never
    /// corrupt this slot's squad; indices are re-derived at arrival.
    /// Units out with either army are deployed and cannot be picked
    /// for the other. Known simplification: an occupation restored
    /// from a save knows only its troop count, not which units, so a
    /// restored farm does not exclude units from the other army's
    /// picker (farming itself touches no per-unit state, so nothing
    /// can corrupt — the harvest lands regardless).</summary>
    public class MapExcursionService
    {
        // Node regen time after collection (Patrick-approved default).
        public const float NodeRegenHours = 8f;
        // Occupation: 120 min / troops sent (8 troops = 15 min).
        public const float OccupationMinutesPerTroop = 120f;
        // Camp respawn (camp field, 2026-11-10, Patrick): a defeated
        // camp's slot refills after this many real-time minutes, with
        // a fresh tier rolled by Nexus level (RollCampTier). With 15
        // slots the field stays populated; energy is the only gate.
        public const float CampRespawnMinutes = 10f;

        // Two armies (2026-11-11): excursion slots. Slot 0 = Army 1,
        // slot 1 = Army 2. The second army is gated at this Nexus
        // level (Patrick's pick) — below it the game plays exactly
        // as the single-army version.
        // Standing armies (2026-11-12): one slot per standing
        // army, sized from GameState so raising the army cap
        // later is a data change, not a rewrite.
        public const int ArmySlotCount = GameState.MaxArmies;
        public const int SecondArmyNexusGate = 10;

        /// <summary>One army's excursion: its target, its squad
        /// (unit references snapshotted at send time), and whether it
        /// is marching or farming. A slot is busy while either flag
        /// is set.</summary>
        private class ExcursionSlot
        {
            public MapPointData Point;
            public List<OwnedUnit> Squad;
            public List<OwnedUnit> HeroSquad;
            public bool Marching;
            public bool Occupying;
            public bool Busy => Marching || Occupying;
        }

        // Raised when an excursion resolves (arrival outcome) or cannot
        // start — title + multi-line body for the info panel.
        public event Action<string, string> OnExcursionResult;
        // Raised when marching starts, finishes, or is cancelled, so the
        // panel can refresh its Send button.
        public event Action OnExcursionStateChanged;

        public bool IsMarching
        {
            get { foreach (var s in _slots) if (s.Marching) return true; return false; }
        }
        // True while any army is out — marching or farming a node.
        // CanSend refuses a send with no free slot; the Expeditions
        // menu is untouched.
        public bool IsBusy
        {
            get { foreach (var s in _slots) if (s.Busy) return true; return false; }
        }

        /// <summary>True when the given standing army can be
        /// unlocked at the current Nexus level (Army 1 at Nexus
        /// 1, Army 2 at 10, Army 3 at 20 — GameState gates).</summary>
        public bool ArmyUnlocked(int army) => _state.ArmyUnlocked(army);

        /// <summary>True while the given army is out — marching,
        /// fighting, or farming a node.</summary>
        public bool IsArmyOut(int army)
        {
            return army >= 0 && army < ArmySlotCount && _slots[army].Busy;
        }

        /// <summary>The standing army's roster members that can
        /// march right now: assigned and not injured. Heroes are
        /// never injured, so they always count when assigned.</summary>
        public List<OwnedUnit> HealthyRoster(int army)
        {
            var result = new List<OwnedUnit>();
            var roster = _state.GetRoster(army);
            if (roster == null) return result;
            foreach (var unit in roster)
            {
                if (unit == null) continue;
                if (_state.Injured != null && _state.Injured.Contains(unit)) continue;
                result.Add(unit);
            }
            return result;
        }

        /// <summary>True once the second army is unlocked (the Nexus
        /// gate, Patrick 2026-11-11).</summary>
        public bool SecondArmyUnlocked => _state.NexusLevel >= SecondArmyNexusGate;

        private readonly GameState _state;
        private readonly EconomyService _economy;
        private readonly ExpeditionService _expeditions;
        private readonly BalanceData _balance;
        private readonly List<UnitDefinition> _units;
        private readonly List<EncounterDefinition> _encounters;
        private readonly ExteriorView _view;
        private readonly Action _saveGame;
        private readonly Action _notifyChanged;

        private readonly ExcursionSlot[] _slots = CreateSlots();

        private static ExcursionSlot[] CreateSlots()
        {
            var slots = new ExcursionSlot[ArmySlotCount];
            for (int i = 0; i < slots.Length; i++) slots[i] = new ExcursionSlot();
            return slots;
        }

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
            // Restore occupations from the save (previous session):
            // those armies are still farming, so their slots are busy
            // until they complete (CheckOccupationDue runs on the
            // next refresh).
            EnsureOccupationArrays();
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                if (state.OccupiedNodeIndex[slot] >= 0 && state.OccupiedNodeIndex[slot] <= 8
                    && !string.IsNullOrEmpty(state.OccupationEndUtc[slot])
                    && DateTime.TryParse(state.OccupationEndUtc[slot], null, DateTimeStyles.RoundtripKind, out _))
                {
                    _slots[slot].Occupying = true;
                    _slots[slot].Point = view != null ? view.NodeData(state.OccupiedNodeIndex[slot]) : null;
                }
            }
            // Restore camp positions/visibility from the save: a camp
            // defeated in a previous session is still vanished (until
            // its respawn time), and a respawned camp sits at its saved
            // spot, not the original one. Same for the boss lair.
            SyncCamps();
            SyncBoss();
        }

        // The occupation arrays are per army slot; normalize their
        // lengths defensively (a state object carrying another length
        // would break every per-slot access below).
        private void EnsureOccupationArrays()
        {
            _state.OccupiedNodeIndex = NormalizeIntSlots(_state.OccupiedNodeIndex, -1);
            _state.OccupationEndUtc = NormalizeStringSlots(_state.OccupationEndUtc);
            _state.OccupationAmount = NormalizeIntSlots(_state.OccupationAmount, 0);
            _state.OccupationKind = NormalizeIntSlots(_state.OccupationKind, 0);
            _state.OccupationTroops = NormalizeIntSlots(_state.OccupationTroops, 0);
        }

        private static int[] NormalizeIntSlots(int[] source, int emptyValue)
        {
            if (source != null && source.Length == ArmySlotCount) return source;
            var result = new int[ArmySlotCount];
            for (int i = 0; i < ArmySlotCount; i++)
                result[i] = source != null && i < source.Length ? source[i] : emptyValue;
            return result;
        }

        private static string[] NormalizeStringSlots(string[] source)
        {
            if (source != null && source.Length == ArmySlotCount) return source;
            var result = new string[ArmySlotCount];
            for (int i = 0; i < ArmySlotCount; i++)
                result[i] = source != null && i < source.Length && source[i] != null ? source[i] : "";
            return result;
        }

        /// <summary>Can an excursion be sent to this point right now?
        /// Nodes need a non-empty army and must not be depleted; camps
        /// and the boss lair need a non-empty army and enough energy.
        /// The tier gate does NOT block a map excursion (2026-11-09):
        /// the player may attack a camp or the boss above their Nexus
        /// and the fight proceeds (and likely fails) — WarningFor
        /// carries the too-strong warning instead.</summary>
        public bool CanSendToPoint(MapPointData point, out string reason)
        {
            reason = "";
            if (point == null)
            {
                reason = "No target selected.";
                return false;
            }
            // Target conflicts (two armies): a node being farmed, or a
            // point an army is already marching to, cannot be
            // double-booked by the other army.
            if (point.Kind != MapPointKind.Camp && point.Kind != MapPointKind.BossLair
                && IsNodeOccupied(point.Index))
            {
                reason = "That node is already being farmed.";
                return false;
            }
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                var s = _slots[slot];
                if (s.Marching && s.Point != null && s.Point.Kind == point.Kind && s.Point.Index == point.Index)
                {
                    reason = $"Army {slot + 1} is already headed there.";
                    return false;
                }
            }
            if (point.Kind != MapPointKind.Camp && point.Kind != MapPointKind.BossLair)
            {
                if (IsNodeDepleted(point.Index, out var remaining))
                {
                    reason = $"Depleted — regenerates in {FormatRemaining(remaining)}.";
                    return false;
                }
                reason = "Ready to send.";
                return true;
            }
            // Boss lair (2026-11-09): same rules as a camp, but the
            // encounter is the Tier 4 War Robot and the defeated check
            // is the boss respawn timer, not a camp tier.
            if (point.Kind == MapPointKind.BossLair)
            {
                var boss = EncounterForBoss();
                if (boss == null)
                {
                    reason = "No boss encounter.";
                    return false;
                }
                if (_state.Energy < boss.EnergyCost)
                {
                    reason = $"Not enough energy ({_state.Energy} / {boss.EnergyCost}).";
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
            if (IsCampSlotEmpty(point))
            {
                reason = "This camp was defeated — a new one will appear soon.";
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

        /// <summary>Can this standing army be sent to this point
        /// right now? Combines the target rules (CanSendToPoint)
        /// with the army's own state: unlocked, home, and with at
        /// least one healthy unit in its roster.</summary>
        public bool CanSendArmy(MapPointData point, int army, out string reason)
        {
            if (!CanSendToPoint(point, out reason)) return false;
            if (army < 0 || army >= ArmySlotCount)
            {
                reason = "No such army.";
                return false;
            }
            if (!ArmyUnlocked(army))
            {
                reason = $"Army {army + 1} unlocks at Nexus {GameState.ArmyNexusGates[army]} (you are Nexus {_state.NexusLevel}).";
                return false;
            }
            if (_slots[army].Busy)
            {
                reason = $"Army {army + 1} is already out.";
                return false;
            }
            var roster = _state.GetRoster(army);
            if (roster == null || roster.Count == 0)
            {
                reason = $"Army {army + 1} has no units — build it in the Army tab.";
                return false;
            }
            if (HealthyRoster(army).Count == 0)
            {
                reason = $"Every unit in Army {army + 1} is injured — heal them at the Hospital.";
                return false;
            }
            reason = "Ready to send.";
            return true;
        }

        /// <summary>Legacy any-army check, kept for callers that
        /// only need to know whether a send is possible at all:
        /// the target must be valid and at least one unlocked,
        /// home army must have a healthy unit.</summary>
        public bool CanSend(MapPointData point, out string reason)
        {
            if (!CanSendToPoint(point, out reason)) return false;
            string firstReason = null;
            for (int army = 0; army < ArmySlotCount; army++)
            {
                if (CanSendArmy(point, army, out var armyReason)) return true;
                if (firstReason == null) firstReason = armyReason;
            }
            reason = firstReason ?? "No army can be sent.";
            return false;
        }

        /// <summary>Warning shown in the panel for a camp or the boss
        /// above the player's Nexus (2026-11-09): the fight is allowed,
        /// but the enemy is far stronger. Null when there is nothing to
        /// warn about (nodes, or a camp/boss at/below the player's
        /// Nexus).</summary>
        public string WarningFor(MapPointData point)
        {
            if (point == null) return null;
            if (point.Kind == MapPointKind.BossLair)
            {
                var boss = EncounterForBoss();
                if (boss == null) return null;
                if (_state.NexusLevel >= boss.RequiredNexusLevel) return null;
                return $"Warning: the War Robot is far stronger than your army — it unlocks at Nexus {boss.RequiredNexusLevel} (you are Nexus {_state.NexusLevel}). Your squad is likely to be wiped out.";
            }
            if (point.Kind != MapPointKind.Camp) return null;
            var encounter = EncounterForTier(point.Tier);
            if (encounter == null) return null;
            if (_state.NexusLevel >= encounter.RequiredNexusLevel) return null;
            return $"Warning: this enemy is far stronger than your army — Tier {point.Tier} unlocks at Nexus {encounter.RequiredNexusLevel} (you are Nexus {_state.NexusLevel}). Your squad is likely to be wiped out.";
        }


        /// <summary>Send a standing army (2026-11-12): the
        /// army's healthy roster, built in the Army tab, marches
        /// as one. The roster is snapshotted as unit references
        /// for arrival, so the farming duration and the fight use
        /// exactly those units — and another army's fight cannot
        /// shift this squad while it is out.</summary>
        public void StartExcursion(MapPointData point, int armyIndex)
        {
            if (!CanSendArmy(point, armyIndex, out var reason))
            {
                OnExcursionResult?.Invoke(point != null ? point.Title : "Excursion", reason);
                return;
            }
            int slotIndex = armyIndex;
            var slot = _slots[slotIndex];
            slot.Point = point;
            // The standing army marches as built in the Army tab:
            // its healthy roster members, snapshotted as unit
            // references so the fight and the farming duration
            // use exactly those units. Injured members stay
            // assigned but sit this one out.
            slot.Squad = new List<OwnedUnit>();
            slot.HeroSquad = new List<OwnedUnit>();
            foreach (var unit in HealthyRoster(armyIndex))
            {
                if (unit.Id == UnitId.HeroReyes || unit.Id == UnitId.HeroVega || unit.Id == UnitId.HeroOkafor)
                    slot.HeroSquad.Add(unit);
                else
                    slot.Squad.Add(unit);
            }
            // Phase 2: the view builds the marching figures from the
            // squad (heroes first — they lead the formation).
            _view.SetSquadComposition(slotIndex, CompositionOf(slot));
            slot.Marching = true;
            OnExcursionStateChanged?.Invoke();
            // Outbound: castle (origin) to the point, 1 unit above ground.
            _view.StartMarch(slotIndex, new Vector3(0f, 1f, 0f), point.Position + new Vector3(0f, 1f, 0f), () => OnArrivedAtTarget(slotIndex));
        }

        /// <summary>The slot's squad as unit ids for the view's
        /// marching figures (Phase 2): heroes first, then troops.
        /// A slot restored from a save has no squad snapshot — the
        /// occupation kept only its troop count — so it marches as
        /// that many generic infantry figures.</summary>
        private List<UnitId> CompositionOf(ExcursionSlot slot, int slotIndex = -1)
        {
            var members = new List<UnitId>();
            if (slot != null && (slot.Squad != null || slot.HeroSquad != null))
            {
                if (slot.HeroSquad != null)
                    foreach (var u in slot.HeroSquad) if (u != null) members.Add(u.Id);
                if (slot.Squad != null)
                    foreach (var u in slot.Squad) if (u != null) members.Add(u.Id);
                return members;
            }
            int troops = 4;
            if (slotIndex >= 0)
            {
                EnsureOccupationArrays();
                if (_state.OccupationTroops != null && slotIndex < _state.OccupationTroops.Length)
                    troops = _state.OccupationTroops[slotIndex];
            }
            troops = Math.Max(1, Math.Min(8, troops));
            for (int i = 0; i < troops; i++) members.Add(UnitId.ScoutMarine);
            return members;
        }

        /// <summary>The first slot that can take an excursion, or -1.
        /// Slot 1 (Army 2) only counts once the Nexus gate is met.</summary>
        private int FreeSlot()
        {
            if (!_slots[0].Busy) return 0;
            if (SecondArmyUnlocked && !_slots[1].Busy) return 1;
            return -1;
        }

        /// <summary>True while this unit is out with either army
        /// (marching, fighting, or farming an occupation started this
        /// session). Deployed units cannot be picked for the other
        /// army. An occupation restored from a save knows only its
        /// troop count, so its units are not excluded — see the class
        /// comment.</summary>
        public bool IsDeployed(OwnedUnit unit)
        {
            if (unit == null) return false;
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                var s = _slots[slot];
                if (!s.Busy) continue;
                if (s.Squad != null && s.Squad.Contains(unit)) return true;
                if (s.HeroSquad != null && s.HeroSquad.Contains(unit)) return true;
            }
            return false;
        }

        /// <summary>Every unit currently out with an army (for the
        /// squad picker's availability counts).</summary>
        public HashSet<OwnedUnit> DeployedUnits()
        {
            var set = new HashSet<OwnedUnit>();
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                var s = _slots[slot];
                if (!s.Busy) continue;
                if (s.Squad != null) foreach (var u in s.Squad) if (u != null) set.Add(u);
                if (s.HeroSquad != null) foreach (var u in s.HeroSquad) if (u != null) set.Add(u);
            }
            return set;
        }

        private bool HasAvailableUnits()
        {
            if (_state.Army != null)
                foreach (var u in _state.Army) if (u != null && !IsDeployed(u)) return true;
            if (_state.Heroes != null)
                foreach (var u in _state.Heroes) if (u != null && !IsDeployed(u)) return true;
            return false;
        }

        // Snapshot unit references for a list of indices into a unit
        // list (invalid indices and nulls are skipped).
        private static List<OwnedUnit> UnitsAt(List<OwnedUnit> units, List<int> indices)
        {
            var result = new List<OwnedUnit>();
            if (units == null || indices == null) return result;
            foreach (int i in indices)
            {
                if (i >= 0 && i < units.Count && units[i] != null) result.Add(units[i]);
            }
            return result;
        }

        // Current indices of a slot's snapshotted units, re-derived
        // at arrival (the other army's fight may have shifted them).
        private static List<int> IndicesOf(List<OwnedUnit> units, List<OwnedUnit> squad)
        {
            var result = new List<int>();
            if (units == null || squad == null) return result;
            foreach (var unit in squad)
            {
                int i = units.IndexOf(unit);
                if (i >= 0) result.Add(i);
            }
            return result;
        }

        /// <summary>Default squad for the picker: the whole army +
        /// strongest hero, strongest first, capped — as per-type
        /// counts. At most one hero is included (one hero per
        /// excursion); the picker opens pre-filled with this and the
        /// player trims it.</summary>
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
            foreach (int i in BuildSquadHeroIndices())
            {
                if (_state.Heroes == null || i < 0 || i >= _state.Heroes.Count) continue;
                var hero = _state.Heroes[i];
                if (hero == null) continue;
                if (!counts.ContainsKey(hero.Id)) counts[hero.Id] = 0;
                counts[hero.Id]++;
            }
            return counts;
        }

        /// <summary>Cancel in-flight excursions (leaving the Map tab).
        /// An outbound march (before arrival) is cancelled with no
        /// outcome — energy is only spent and combat only runs at
        /// arrival, so cancelling before then costs nothing. An active
        /// OCCUPATION is never cancelled: the army keeps farming in
        /// real time, and only its marker hides (it reappears at the
        /// node when the Map reopens). A return march is cosmetic (the
        /// outcome already landed), so it just stops. Applies to both
        /// army slots.</summary>
        public void Cancel()
        {
            bool changed = false;
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                var s = _slots[slot];
                if (s.Occupying)
                {
                    _view.CancelMarch(slot);
                    continue;
                }
                if (!s.Marching) continue;
                _view.CancelMarch(slot);
                s.Marching = false;
                s.Point = null;
                s.Squad = null;
                s.HeroSquad = null;
                changed = true;
            }
            if (changed) OnExcursionStateChanged?.Invoke();
        }

        private void OnArrivedAtTarget(int slotIndex)
        {
            var slot = _slots[slotIndex];
            var point = slot.Point;
            if (point == null)
            {
                FinishMarch(slotIndex);
                return;
            }
            if (point.Kind == MapPointKind.BossLair)
            {
                FightBoss(slotIndex, point);
                return;
            }
            if (point.Kind != MapPointKind.Camp)
            {
                // Node: the army stays and farms it (occupation) instead
                // of collecting instantly. The return march starts when
                // the occupation completes.
                BeginOccupation(slotIndex, point);
                return;
            }
            FightCamp(slotIndex, point);
            // Nodes start an occupation (the return march waits until it
            // completes); camps and the boss fight — FightCamp/FightBoss
            // play the animation, show the result, and start the return
            // march themselves.
        }

        private void FinishMarch(int slotIndex)
        {
            var slot = _slots[slotIndex];
            slot.Marching = false;
            slot.Point = null;
            slot.Squad = null;
            slot.HeroSquad = null;
            _view.CancelMarch(slotIndex);
            OnExcursionStateChanged?.Invoke();
        }

        // Node occupation (2026-11-09; per army slot 2026-11-11): on
        // arrival the army stays and farms the node for 120 min /
        // troops sent (8 troops = 15 min). The slot's marker stays at
        // the node, the node turns amber, and the harvest lands when
        // the time is up (CompleteOccupation). The occupation is saved
        // immediately, so it survives an app restart.
        private void BeginOccupation(int slotIndex, MapPointData point)
        {
            var slot = _slots[slotIndex];
            int troops = Math.Max(1, (slot.Squad != null ? slot.Squad.Count : 0) + (slot.HeroSquad != null ? slot.HeroSquad.Count : 0));
            float minutes = OccupationMinutesPerTroop / troops;
            EnsureOccupationArrays();
            _state.OccupiedNodeIndex[slotIndex] = point.Index;
            _state.OccupationEndUtc[slotIndex] = DateTime.UtcNow.AddMinutes(minutes).ToString("o");
            _state.OccupationAmount[slotIndex] = point.Amount;
            _state.OccupationKind[slotIndex] = (int)point.Kind;
            _state.OccupationTroops[slotIndex] = troops;
            _saveGame?.Invoke();
            slot.Marching = false;
            slot.Occupying = true;
            _view.SetSquadParked(slotIndex);
            _notifyChanged?.Invoke();
            OnExcursionStateChanged?.Invoke();
            string resourceName = ResourceName(point.Kind);
            OnExcursionResult?.Invoke(point.Title,
                $"Army {slotIndex + 1} — farming {resourceName}: {troops} troop{(troops == 1 ? "" : "s")}, about {FormatMinutes(minutes)}.\nThe army returns with the harvest when done.");
        }

        /// <summary>Called regularly (Bootstrap RefreshAll, ~once a
        /// second) and on load: if an occupation's end time has passed,
        /// complete it (per army slot). Also completes an occupation
        /// that ended while the app was closed — the harvest is
        /// granted on the next load.</summary>
        public void CheckOccupationDue()
        {
            EnsureOccupationArrays();
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                if (!_slots[slot].Occupying && _state.OccupiedNodeIndex[slot] < 0) continue;
                if (_state.OccupiedNodeIndex[slot] < 0 || _state.OccupiedNodeIndex[slot] > 8) continue;
                if (string.IsNullOrEmpty(_state.OccupationEndUtc[slot])) continue;
                if (!DateTime.TryParse(_state.OccupationEndUtc[slot], null, DateTimeStyles.RoundtripKind, out var endUtc)) continue;
                if (DateTime.UtcNow < endUtc) continue;
                CompleteOccupation(slot);
            }
        }

        private void CompleteOccupation(int slotIndex)
        {
            var slot = _slots[slotIndex];
            int nodeIndex = _state.OccupiedNodeIndex[slotIndex];
            int amount = _state.OccupationAmount[slotIndex];
            var kind = (MapPointKind)Math.Max(0, Math.Min(_state.OccupationKind[slotIndex], 2));
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
            ClearOccupation(slotIndex);
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            var body = new StringBuilder();
            body.AppendLine(gained > 0
                ? $"Collected +{gained} {resourceName}."
                : $"Storage full — no {resourceName} collected.");
            body.Append("Node depleted — it vanished and regenerates in 8h.");
            OnExcursionResult?.Invoke($"Harvest complete — Army {slotIndex + 1}", body.ToString());
            // The farming army is home: release its units, then the
            // return march (if the Map is open) is purely cosmetic.
            slot.Occupying = false;
            slot.Squad = null;
            slot.HeroSquad = null;
            if (_view.IsVisible && slot.Point != null)
            {
                var from = slot.Point;
                slot.Point = null;
                _view.StartMarch(slotIndex, from.Position + new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f), () => FinishMarch(slotIndex));
            }
            else
            {
                slot.Point = null;
                _view.CancelMarch(slotIndex);
                OnExcursionStateChanged?.Invoke();
            }
        }

        private void ClearOccupation(int slotIndex)
        {
            EnsureOccupationArrays();
            _state.OccupiedNodeIndex[slotIndex] = -1;
            _state.OccupationEndUtc[slotIndex] = "";
            _state.OccupationAmount[slotIndex] = 0;
            _state.OccupationKind[slotIndex] = 0;
            _state.OccupationTroops[slotIndex] = 0;
        }

        // Enemy camp field (2026-11-10, replaces the 3 fixed camps):
        // 15 slots across the map (ExteriorView.CampSlotPositions). A
        // defeated camp's slot empties and refills after
        // CampRespawnMinutes with a tier rolled fresh from the Nexus-
        // weighted table (RollCampTier), so the field grows with the
        // player and there is always something to attack. Slot state
        // (tier + respawn time per slot) lives in the save, so timers
        // survive an app restart and complete while the app is closed.

        /// <summary>The camp slot (0-14) a map point belongs to, or
        /// -1 when the point is not a camp slot point.</summary>
        private static int SlotForPoint(MapPointData point)
        {
            if (point == null || point.Kind != MapPointKind.Camp) return -1;
            int slot = point.Index - ExteriorView.CampSlotIndexBase;
            return slot >= 0 && slot < ExteriorView.CampSlotCount ? slot : -1;
        }

        /// <summary>True while this point's camp slot is empty (its
        /// camp was defeated and the slot is waiting to refill).</summary>
        public bool IsCampSlotEmpty(MapPointData point)
        {
            int slot = SlotForPoint(point);
            if (slot < 0) return false;
            if (_state.CampSlotTier == null || _state.CampSlotTier.Length != ExteriorView.CampSlotCount) return false;
            return _state.CampSlotTier[slot] == 0;
        }

        /// <summary>Tier for a newly spawning camp, weighted by Nexus
        /// level (Patrick's design, 2026-11-10): Tier 2 camps join
        /// once a Tier 2 army is possible (Nexus 10), Tier 3 at
        /// Nexus 15, and Tier 1 is always sprinkled in — never gone.</summary>
        private int RollCampTier()
        {
            int nexus = _state.NexusLevel;
            if (nexus < 10) return 1;
            float roll = UnityEngine.Random.value;
            if (nexus < 15) return roll < 0.65f ? 1 : 2;
            if (nexus < 20) return roll < 0.45f ? 1 : (roll < 0.75f ? 2 : 3);
            return roll < 0.30f ? 1 : (roll < 0.65f ? 2 : 3);
        }

        // Normalize the slot arrays, and populate a FRESH field: when
        // every slot is empty AND no respawn is pending, this save has
        // never had the camp field (new game, or a save from before
        // it) — fill all 15 slots now. A field with every camp beaten
        // at once is NOT fresh: all its respawn timers are set.
        private void EnsureCampSlots()
        {
            if (_state.CampSlotTier == null || _state.CampSlotTier.Length != ExteriorView.CampSlotCount)
                _state.CampSlotTier = new int[ExteriorView.CampSlotCount];
            if (_state.CampSlotRespawnUtc == null || _state.CampSlotRespawnUtc.Length != ExteriorView.CampSlotCount)
                _state.CampSlotRespawnUtc = new string[ExteriorView.CampSlotCount];
            bool anyTier = false;
            bool anyTimer = false;
            for (int i = 0; i < ExteriorView.CampSlotCount; i++)
            {
                if (_state.CampSlotTier[i] != 0) anyTier = true;
                if (!string.IsNullOrEmpty(_state.CampSlotRespawnUtc[i])) anyTimer = true;
            }
            if (anyTier || anyTimer) return;
            for (int i = 0; i < ExteriorView.CampSlotCount; i++)
            {
                _state.CampSlotTier[i] = RollCampTier();
            }
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
        }

        // Vanish a defeated camp's slot: record the refill time, clear
        // the slot in the view, and save. The slot refills when
        // CheckCampRespawns runs after the time.
        private void VanishCampSlot(int slot)
        {
            if (slot < 0 || slot >= ExteriorView.CampSlotCount) return;
            EnsureCampSlots();
            _state.CampSlotTier[slot] = 0;
            _state.CampSlotRespawnUtc[slot] = DateTime.UtcNow.AddMinutes(CampRespawnMinutes).ToString("o");
            if (_view != null)
            {
                _view.SetCampSlot(slot, 0);
                _view.SetCampSlotVisible(slot, false);
            }
            _saveGame?.Invoke();
            _notifyChanged?.Invoke();
            OnExcursionStateChanged?.Invoke();
        }

        /// <summary>Called regularly (Bootstrap RefreshAll, ~once a
        /// second) and on load: refill every empty slot whose respawn
        /// time has passed, with a freshly rolled tier. Also completes
        /// respawns whose time passed while the app was closed — the
        /// new camps are there on the next load.</summary>
        public void CheckCampRespawns()
        {
            EnsureCampSlots();
            bool changed = false;
            for (int slot = 0; slot < ExteriorView.CampSlotCount; slot++)
            {
                if (_state.CampSlotTier[slot] != 0) continue;
                string s = _state.CampSlotRespawnUtc[slot];
                if (string.IsNullOrEmpty(s)) continue;
                if (!DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var respawnUtc)) continue;
                if (DateTime.UtcNow < respawnUtc) continue;
                _state.CampSlotTier[slot] = RollCampTier();
                _state.CampSlotRespawnUtc[slot] = "";
                if (_view != null)
                {
                    _view.SetCampSlot(slot, _state.CampSlotTier[slot]);
                    _view.SetCampSlotVisible(slot, true);
                }
                changed = true;
            }
            if (changed)
            {
                _saveGame?.Invoke();
                _notifyChanged?.Invoke();
                OnExcursionStateChanged?.Invoke();
            }
        }

        /// <summary>Mirror the saved slot state into the view: each
        /// slot shows its saved tier (SetCampSlot is a no-op when the
        /// slot already shows it) and empty slots stay hidden. Called
        /// on load and whenever the Map is shown (the view's
        /// SetVisible re-shows static points, so slot visibility is
        /// re-applied here).</summary>
        public void SyncCamps()
        {
            if (_view == null) return;
            EnsureCampSlots();
            for (int slot = 0; slot < ExteriorView.CampSlotCount; slot++)
            {
                int tier = _state.CampSlotTier[slot];
                _view.SetCampSlot(slot, tier);
                _view.SetCampSlotVisible(slot, tier > 0);
            }
        }

        // Boss lair (2026-11-09, redesigned 2026-11-10): the War Robot.
        // The boss NEVER vanishes now — the player can fight any
        // unlocked level at will, energy permitting (15-level ladder).
        // IsBossDefeated always returns false; VanishBoss and the
        // respawn timer are retired (BossRespawnUtc stays "" and is
        // ignored). The lair is always visible at the same spot.

        /// <summary>The boss never vanishes now (2026-11-10 ladder
        /// redesign), so this is always false. Kept so callers that
        /// used to gate on a defeated boss keep compiling.</summary>
        public bool IsBossDefeated()
        {
            return false;
        }

        /// <summary>Hero boss fight (2026-11-10 ladder): the lair view
        /// runs the fight itself (TryStartHeroes) and reports the
        /// victory here. The boss does NOT vanish — it stays, and the
        /// lair view unlocks the next level. This just refreshes the
        /// map so the lair display stays current.</summary>
        public void OnBossDefeatedInLair()
        {
            OnExcursionStateChanged?.Invoke();
        }

        /// <summary>Retired (2026-11-10): the boss no longer respawns —
        /// it never leaves. This only clears a stale BossRespawnUtc
        /// left by an old save and makes sure the lair shows.</summary>
        public void CheckBossRespawn()
        {
            if (!string.IsNullOrEmpty(_state.BossRespawnUtc))
            {
                _state.BossRespawnUtc = "";
                _saveGame?.Invoke();
                _notifyChanged?.Invoke();
            }
            _view?.SetBossVisible(true);
        }

        /// <summary>The boss lair is always visible now (2026-11-10
        /// ladder redesign — the boss never vanishes). Called on load
        /// and whenever the Map is shown.</summary>
        public void SyncBoss()
        {
            if (_view == null) return;
            _view.SetBossVisible(true);
        }

        /// <summary>The War Robot encounter (Tier 4). Null when the
        /// catalog has no Tier 4 encounter.</summary>
        private EncounterDefinition EncounterForBoss()
        {
            if (_encounters == null) return null;
            return _encounters.Find(e => e != null && e.Tier == 4);
        }

        /// <summary>True while this node is being farmed by either
        /// army (occupation active, in this session or restored from
        /// the save).</summary>
        public bool IsNodeOccupied(int nodeIndex)
        {
            EnsureOccupationArrays();
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                if (_state.OccupiedNodeIndex[slot] == nodeIndex
                    && !string.IsNullOrEmpty(_state.OccupationEndUtc[slot]))
                    return true;
            }
            return false;
        }

        /// <summary>Occupation detail for the panel: which army is
        /// farming the node, with how many troops, and the time left.
        /// False when the node is not occupied.</summary>
        public bool OccupationInfo(int nodeIndex, out int army, out int troops, out TimeSpan remaining)
        {
            army = 0;
            troops = 0;
            remaining = TimeSpan.Zero;
            EnsureOccupationArrays();
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                if (_state.OccupiedNodeIndex[slot] != nodeIndex) continue;
                if (string.IsNullOrEmpty(_state.OccupationEndUtc[slot])) continue;
                if (!DateTime.TryParse(_state.OccupationEndUtc[slot], null, DateTimeStyles.RoundtripKind, out var endUtc)) continue;
                army = slot + 1;
                troops = _state.OccupationTroops[slot];
                remaining = endUtc - DateTime.UtcNow;
                return true;
            }
            return false;
        }

        /// <summary>Restore each farming army's marker at its occupied
        /// node when the Map reopens (or on a refresh) so the army is
        /// visibly farming there. No-op for slots that are not
        /// occupying or while the Map is hidden.</summary>
        public void RefreshMarkerVisual()
        {
            if (!_view.IsVisible) return;
            EnsureOccupationArrays();
            for (int slot = 0; slot < ArmySlotCount; slot++)
            {
                if (!_slots[slot].Occupying) continue;
                int nodeIndex = _state.OccupiedNodeIndex[slot];
                if (nodeIndex < 0 || nodeIndex > 8) continue;
                // The occupied node is found by index; its position
                // comes from the slot's point when this session started
                // it, or from the saved index via the view's node
                // positions (restored occupation).
                Vector3 pos;
                if (_slots[slot].Point != null && _slots[slot].Point.Index == nodeIndex)
                {
                    pos = _slots[slot].Point.Position;
                }
                else if (!_view.NodePosition(nodeIndex, out pos))
                {
                    continue;
                }
                _view.SetSquadComposition(slot, CompositionOf(_slots[slot], slot));
                _view.ShowMarkerAt(slot, pos + new Vector3(0f, 1f, 0f));
            }
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

        private void FightCamp(int slotIndex, MapPointData point)
        {
            var slot = _slots[slotIndex];
            var encounter = EncounterForTier(point.Tier);
            // Re-derive the squad's indices from its snapshotted
            // references — the other army's fight may have shifted
            // them while this army marched.
            var squad = IndicesOf(_state.Army, slot.Squad);
            var heroSquad = IndicesOf(_state.Heroes, slot.HeroSquad);
            if (encounter == null || (squad.Count == 0 && heroSquad.Count == 0))
            {
                OnExcursionResult?.Invoke(point.Title, "Could not start the fight.");
                StartReturnMarch(slotIndex, point);
                return;
            }
            // Same fight as the Expeditions menu: energy spent at start,
            // no refund on defeat, rewards only on victory, casualties
            // injured (hospital). TryStartMixed applies all of it, with
            // heroes marching alongside the army (2026-11-10) and gaining
            // XP. allowLockedTier: map excursions let the player attack
            // above their Nexus — the fight proceeds and (almost
            // certainly) fails.
            if (_expeditions.TryStartMixed(encounter, squad, heroSquad, out var result, out var reason, allowLockedTier: true))
            {
                _saveGame?.Invoke();
                _notifyChanged?.Invoke();
                // Play the fight animation first (~2s on the map), then
                // show the result, vanish the camp on victory, and march
                // back. The combat itself already resolved above — the
                // animation is purely visual.
                _view.PlayFightAnimation(slotIndex, point.Index, result.Victory, () =>
                {
                    OnExcursionResult?.Invoke(
                        result.Victory ? "VICTORY!" : (result.Timeout ? "DEFEAT — TIMEOUT" : "DEFEAT"),
                        FormatBattleResult(result));
                    // A defeated camp vanishes (camp field, 2026-11-10):
                    // the camp and its creatures disappear, and the slot
                    // refills after CampRespawnMinutes with a fresh tier
                    // rolled by Nexus level. Losing leaves the camp in
                    // place, so the player can retry.
                    if (result.Victory) VanishCampSlot(SlotForPoint(point));
                    StartReturnMarch(slotIndex, point);
                });
            }
            else
            {
                OnExcursionResult?.Invoke(point.Title, reason);
                StartReturnMarch(slotIndex, point);
            }
        }

        // Boss lair (2026-11-09; ladder 2026-11-10): the War Robot. This
        // old map-march fight is now unreachable — tapping the lair opens
        // the lair view (heroes, 15-level ladder) instead. Kept so the
        // class still compiles if anything references it.
        private void FightBoss(int slotIndex, MapPointData point)
        {
            var encounter = EncounterForBoss();
            var squad = IndicesOf(_state.Army, _slots[slotIndex].Squad);
            if (encounter == null || squad.Count == 0)
            {
                OnExcursionResult?.Invoke(point.Title, "Could not start the fight.");
                StartReturnMarch(slotIndex, point);
                return;
            }
            // Same fight as the Expeditions menu: energy spent at start,
            // no refund on defeat, rewards only on victory, casualties
            // injured. allowLockedTier: map excursions let the player
            // attack above their Nexus — the fight proceeds and (almost
            // certainly) fails.
            if (_expeditions.TryStart(encounter, squad, out var result, out var reason, allowLockedTier: true))
            {
                _saveGame?.Invoke();
                _notifyChanged?.Invoke();
                // Play the fight animation first (~2s on the map), then
                // show the result, vanish the boss on victory, and march
                // back. The combat itself already resolved above — the
                // animation is purely visual.
                _view.PlayFightAnimation(slotIndex, point.Index, result.Victory, () =>
                {
                    OnExcursionResult?.Invoke(
                        result.Victory ? "VICTORY!" : (result.Timeout ? "DEFEAT — TIMEOUT" : "DEFEAT"),
                        FormatBattleResult(result));
                    StartReturnMarch(slotIndex, point);
                });
            }
            else
            {
                OnExcursionResult?.Invoke(point.Title, reason);
                StartReturnMarch(slotIndex, point);
            }
        }

        // Return march: back to the castle, then the excursion ends.
        private void StartReturnMarch(int slotIndex, MapPointData point)
        {
            _view.StartMarch(slotIndex, point.Position + new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 0f), () => FinishMarch(slotIndex));
        }

        // Default squad (available army + heroes combined, strongest
        // first, capped): scores every non-deployed Army unit and
        // Hero by power and takes the top MaxSquadSize, split back
        // into army / hero index lists. Units out with the other army
        // are skipped (two armies, 2026-11-11). Per-unit power is the
        // same formula as PowerService.
        private void BuildDefaultSquad(out List<int> armyIndices, out List<int> heroIndices)
        {
            var scored = new List<(bool isHero, int index, long power)>();
            if (_state.Army != null)
            {
                for (int i = 0; i < _state.Army.Count; i++)
                {
                    var unit = _state.Army[i];
                    if (unit == null) continue;
                    if (IsDeployed(unit)) continue;
                    var def = _units != null ? _units.Find(u => u.Id == unit.Id) : null;
                    if (def == null) continue;
                    long basePower = def.BaseHealth + 8L * def.BaseAttack + 4L * def.BaseDefense;
                    long power = (long)Math.Round(basePower * (1.0 + 0.08 * (Math.Max(1, unit.Level) - 1)));
                    scored.Add((false, i, power));
                }
            }
            if (_state.Heroes != null)
            {
                for (int i = 0; i < _state.Heroes.Count; i++)
                {
                    var hero = _state.Heroes[i];
                    if (hero == null) continue;
                    if (IsDeployed(hero)) continue;
                    var def = _units != null ? _units.Find(u => u.Id == hero.Id) : null;
                    if (def == null) continue;
                    long basePower = def.BaseHealth + 8L * def.BaseAttack + 4L * def.BaseDefense;
                    long power = (long)Math.Round(basePower * (1.0 + 0.08 * (Math.Max(1, hero.Level) - 1)));
                    scored.Add((true, i, power));
                }
            }
            scored.Sort((a, b) => b.power.CompareTo(a.power));
            armyIndices = new List<int>();
            heroIndices = new List<int>();
            // Strongest first, up to the squad cap — but at most ONE
            // hero per excursion (Patrick 2026-11-10): extra heroes are
            // skipped and the next-strongest army units fill the squad.
            foreach (var s in scored)
            {
                if (armyIndices.Count + heroIndices.Count >= _balance.MaxSquadSize) break;
                if (s.isHero)
                {
                    if (heroIndices.Count >= 1) continue;
                    heroIndices.Add(s.index);
                }
                else armyIndices.Add(s.index);
            }
        }

        /// <summary>Army portion of the default (strongest) squad.</summary>
        private List<int> BuildSquadHeroIndices()
        {
            BuildDefaultSquad(out _, out var heroIndices);
            return heroIndices;
        }

        /// <summary>Whole army, strongest first, capped at the squad size.
        /// Per-unit power is the same formula as PowerService: (HP +
        /// 8*ATK + 4*DEF) * (1 + 0.08*(level-1)). This is the army
        /// portion of the combined default squad (army + heroes) — see
        /// BuildDefaultSquad.</summary>
        private List<int> BuildSquadIndices()
        {
            BuildDefaultSquad(out var armyIndices, out _);
            return armyIndices;
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
            if (result.CasualtyNames.Count == 0 && result.HeroCasualtyNames.Count == 0)
            {
                body.AppendLine("Casualties: none");
            }
            else
            {
                if (result.CasualtyNames.Count > 0)
                    body.AppendLine("Injured: " + string.Join(", ", result.CasualtyNames) + " — heal them at the Hospital.");
                if (result.HeroCasualtyNames.Count > 0)
                    body.AppendLine("Heroes down: " + string.Join(", ", result.HeroCasualtyNames) + " — heroes recover after the fight.");
            }
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
