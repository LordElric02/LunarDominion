using System;
using System.Collections;
using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Expeditions;
using UnityEngine;

namespace LunarDominion.Boss
{
    /// <summary>One hero in the turn-based boss fight: fight-only
    /// state (HP, shields, buffs, cooldowns) layered over the
    /// persistent SquadCombatant. Nothing here is saved — buffs and
    /// cooldowns live and die with the fight.</summary>
    public class TurnHeroState
    {
        public SquadCombatant Member;
        public GameObject Figure;
        public Vector3 Home;
        public Quaternion OrigRot;
        public Transform Disc;
        public int MaxHp;
        public int Hp;
        public int Atk;
        public int Def;
        public int Speed;
        public int Shield;
        public int ShieldRounds;
        public bool Alive = true;
        public bool Acted;
        public bool Defending;
        public int AtkBuffRounds;
        public int DefBuffRounds;
        public int DeadeyeCharges;
        public readonly int[] Cooldowns = new int[3];

        public string Name => Member != null ? Member.Definition.DisplayName : "Hero";
        public UnitClass Class => Member.Definition.Class;
        public int Level => Member.Unit.Level;

        /// <summary>Defense with Bulwark applied (the boss's
        /// hits are computed against this).</summary>
        public int EffDef => Mathf.RoundToInt(Def * (DefBuffRounds > 0 ? TurnBasedBossFight.BulwarkDefMult : 1f));
    }

    /// <summary>One sub-drone in the turn-based boss fight
    /// (summoned at Phase 2). Mechanized class for counters, no
    /// DEF — heroes chew through them fast; their job is to soak
    /// actions and chip the weakest hero.</summary>
    public class TurnDroneState
    {
        public GameObject Figure;
        public Vector3 Home;
        public int MaxHp;
        public int Hp;
        public int Atk;
        public int Speed;
        public bool Alive = true;
        public string Name;
    }

    /// <summary>One entry of the round's turn order for the
    /// lair's order strip.</summary>
    public class TurnOrderEntry
    {
        public string Label;
        public int Speed;
        public bool Current;
        public bool Done;
    }

    /// <summary>The turn-based A.R.E.S.-09 boss fight
    /// (2026-11-10 spec, replacing the real-time mode as the
    /// arena fight; the real-time classes stay in the project,
    /// dormant). Round structure: a speed-ordered queue — each
    /// round, heroes, the boss and any drones act in Speed order
    /// (heroes gain +2 Speed per level; the boss gains +2 per
    /// ladder level) — the player choosing each hero's action
    /// when their slot comes up (Attack / Defend / 3 skills on
    /// round cooldowns), the boss answering with its skill
    /// rotation (basic strike at the weakest hero, Thermal Sweep
    /// every 3rd boss turn, telegraphed Seismic Slam, drones at
    /// 50% HP, Phase 2 core venting). 20 rounds; the boss still
    /// standing at the cap is a timeout defeat. All damage flows
    /// through CombatService.Damage; buffs scale its inputs.</summary>
    public class TurnBasedBossFight : MonoBehaviour
    {
        // ---- Tuning (the spec's numbers) ----
        public const int MaxRounds = 20;
        public const float RallyAtkMult = 1.15f;
        public const int RallyRounds = 3;
        public const float BulwarkDefMult = 1.30f;
        public const int BulwarkRounds = 2;
        public const float PrecisionMult = 1.80f;
        public const float DeadeyeMult = 1.50f;
        public const int DeadeyeChargesGranted = 2;
        public const float PiercingMult = 1.50f;
        public const float SuppressAtkMult = 0.80f;
        public const int SuppressRounds = 2;
        public const float AegisShieldFrac = 0.20f;
        public const int AegisRounds = 3;
        public const int TauntRounds = 2;
        public const float ArmorBreakMult = 1.30f;
        public const float ArmorBreakDefMult = 0.80f;
        public const int ArmorBreakRounds = 2;

        private const float SweepAtkMult = 0.60f;
        private const float SlamAtkMult = 1.20f;
        private const float VentBurnMult = 0.42f;
        private const float VentVulnMult = 1.25f;
        private const int DroneBaseHp = 170;
        private const float DroneAtkFrac = 0.20f;
        private const int DroneSpeed = 55;

        public readonly List<TurnHeroState> Heroes = new List<TurnHeroState>();
        public readonly List<TurnDroneState> Drones = new List<TurnDroneState>();
        public readonly List<string> Log = new List<string>();

        public int BossMaxHp { get; private set; }
        public int BossHp { get; private set; }
        public int BossAtk { get; private set; }
        public int BossDef { get; private set; }
        public int BossLevel { get; private set; }

        /// <summary>Turn-order speed of the boss: 45 + 2 per
        /// ladder level (L1 47, L15 75) — early levels act late,
        /// the capstone cuts in ahead of all but the fastest
        /// heroes.</summary>
        public int BossSpeed => 45 + 2 * BossLevel;
        public int Round { get; private set; }
        public bool Finished { get; private set; }
        public bool Busy { get; private set; }
        public bool AwaitingTarget { get; private set; }
        public int SelectedHero { get; private set; } = -1;
        public bool SlamCharged => _slamCharged;
        public bool Phase2 => _phase2;

        /// <summary>Raised after every state change; the lair
        /// view refreshes its turn panel from it.</summary>
        public event Action OnChanged;
        /// <summary>Raised once when Phase 2 begins (the view
        /// switches the arena ambience / core pulse).</summary>
        public event Action OnPhase2;
        /// <summary>(victory, timeout, fallen squad indices) —
        /// raised once, after the outro animation.</summary>
        public event Action<bool, bool, List<int>> OnEnded;

        private Transform _bossRoot;
        private Transform _bossVisual;
        private Vector3 _bossVisualOrigPos;
        private Quaternion _bossVisualOrigRot;
        private TelegraphRing _slamRing;
        private GameObject _droneTemplate;
        private float _enemyScale = 1f;

        private int _bossTurn;
        private bool _slamCharged;
        private bool _phase2;
        private bool _dronesSummoned;
        private bool _pendingSummon;
        private bool _ventActive;
        private int _bossAtkDebuffRounds;
        private int _bossDefDebuffRounds;
        private int _tauntRounds;
        private TurnHeroState _tauntHero;
        private bool _pendingAttack;
        private int _pendingSkillSlot = -1;

        // Speed-ordered turn queue (2026-11-10): a round is one
        // pass through the queue — heroes, the boss and drones
        // interleaved by Speed instead of heroes-then-boss.
        private enum ActorKind { Hero, Boss, Drone }

        private struct TurnActor
        {
            public ActorKind Kind;
            public int Index;
            public int Speed;
        }

        private readonly List<TurnActor> _order = new List<TurnActor>();
        private int _orderStep;

        private int BossEffAtk => Mathf.RoundToInt(BossAtk * (_bossAtkDebuffRounds > 0 ? SuppressAtkMult : 1f));
        private int BossEffDef => Mathf.RoundToInt(BossDef * (_bossDefDebuffRounds > 0 ? ArmorBreakDefMult : 1f));

        public void Begin(List<TurnHeroState> heroes, int bossMaxHp, int bossAtk, int bossDef,
            int bossLevel, float enemyScale, Transform bossRoot, Transform bossVisual,
            TelegraphRing slamRing, GameObject droneTemplate)
        {
            Heroes.AddRange(heroes);
            foreach (var hero in Heroes)
            {
                hero.Home = hero.Figure.transform.position;
                hero.OrigRot = hero.Figure.transform.rotation;
                hero.Hp = hero.MaxHp;
                var disc = hero.Figure.transform.Find("HeroDisc");
                hero.Disc = disc;
            }
            BossMaxHp = bossMaxHp;
            BossHp = bossMaxHp;
            BossAtk = bossAtk;
            BossDef = bossDef;
            BossLevel = bossLevel;
            _enemyScale = enemyScale;
            _bossRoot = bossRoot;
            _bossVisual = bossVisual;
            if (bossVisual != null)
            {
                _bossVisualOrigPos = bossVisual.localPosition;
                _bossVisualOrigRot = bossVisual.localRotation;
            }
            _slamRing = slamRing;
            _droneTemplate = droneTemplate;
            AddLog("A.R.E.S.-09 awakens. Choose each hero's action.");
            StartRound();
        }

        // ================= Round flow =================

        private void StartRound()
        {
            if (Finished) return;
            Round++;
            if (Round > MaxRounds)
            {
                Round = MaxRounds;
                StartCoroutine(FinishRoutine(false, true));
                return;
            }
            foreach (var hero in Heroes)
            {
                hero.Acted = false;
                // Defending is NOT cleared here: it lasts until
                // the hero's own next turn (AdvanceToNextActor
                // clears it when their slot comes up).
                for (int i = 0; i < hero.Cooldowns.Length; i++)
                {
                    if (hero.Cooldowns[i] > 0) hero.Cooldowns[i]--;
                }
            }
            BuildOrder();
            _orderStep = 0;
            AddLog($"— Round {Round} —");
            OnChanged?.Invoke();
            AdvanceToNextActor();
        }

        // The round's turn order: everyone who will act, fastest
        // first. Ties go heroes, then the boss, then drones;
        // squad order breaks the rest.
        private void BuildOrder()
        {
            _order.Clear();
            for (int i = 0; i < Heroes.Count; i++)
            {
                if (Heroes[i].Alive)
                    _order.Add(new TurnActor { Kind = ActorKind.Hero, Index = i, Speed = Heroes[i].Speed });
            }
            _order.Add(new TurnActor { Kind = ActorKind.Boss, Index = -1, Speed = BossSpeed });
            for (int i = 0; i < Drones.Count; i++)
            {
                if (Drones[i].Alive)
                    _order.Add(new TurnActor { Kind = ActorKind.Drone, Index = i, Speed = Drones[i].Speed });
            }
            _order.Sort((a, b) =>
            {
                int bySpeed = b.Speed.CompareTo(a.Speed);
                if (bySpeed != 0) return bySpeed;
                int byKind = a.Kind.CompareTo(b.Kind);
                if (byKind != 0) return byKind;
                return a.Index.CompareTo(b.Index);
            });
        }

        /// <summary>The current turn order for the lair's order
        /// strip (labels are full names; the view shortens
        /// them).</summary>
        public List<TurnOrderEntry> GetTurnOrder()
        {
            var list = new List<TurnOrderEntry>();
            for (int i = 0; i < _order.Count; i++)
            {
                var actor = _order[i];
                string label = actor.Kind == ActorKind.Hero ? Heroes[actor.Index].Name
                    : actor.Kind == ActorKind.Boss ? "A.R.E.S."
                    : Drones[actor.Index].Name;
                list.Add(new TurnOrderEntry
                {
                    Label = label,
                    Speed = actor.Speed,
                    Current = i == _orderStep && !Finished,
                    Done = i < _orderStep,
                });
            }
            return list;
        }

        // Step the queue: hero slots wait for the player's order;
        // boss and drone slots run their actions and step on.
        // Dead heroes are skipped (they may have fallen earlier
        // in the round, before their slot came up).
        private void AdvanceToNextActor()
        {
            if (Finished) return;
            while (_orderStep < _order.Count)
            {
                var actor = _order[_orderStep];
                if (actor.Kind == ActorKind.Hero)
                {
                    var hero = Heroes[actor.Index];
                    if (!hero.Alive)
                    {
                        _orderStep++;
                        continue;
                    }
                    SelectedHero = actor.Index;
                    // A fresh turn ends this hero's Defend.
                    hero.Defending = false;
                    Busy = false;
                    OnChanged?.Invoke();
                    return;
                }
                if (actor.Kind == ActorKind.Boss)
                {
                    SelectedHero = -1;
                    Busy = true;
                    OnChanged?.Invoke();
                    StartCoroutine(BossActionRoutine());
                    return;
                }
                var drone = Drones[actor.Index];
                if (!drone.Alive)
                {
                    _orderStep++;
                    continue;
                }
                SelectedHero = -1;
                Busy = true;
                OnChanged?.Invoke();
                StartCoroutine(DroneTurnRoutine(drone));
                return;
            }
            StartCoroutine(RoundEndRoutine());
        }

        private void CompleteHeroAction()
        {
            _orderStep++;
            AdvanceToNextActor();
        }

        private bool AllHeroesDown()
        {
            foreach (var hero in Heroes)
            {
                if (hero.Alive) return false;
            }
            return true;
        }

        private bool DronesAlive()
        {
            foreach (var drone in Drones)
            {
                if (drone.Alive) return true;
            }
            return false;
        }

        // ================= Player input (from the view) =================

        public void SelectHero(int index)
        {
            // Turn order is set by Speed now: the acting hero is
            // whoever's slot it is. Chip taps no longer reorder —
            // this stays only so the view's wiring is harmless.
            if (Finished || Busy || AwaitingTarget) return;
            if (index != SelectedHero) return;
            OnChanged?.Invoke();
        }

        public void ChooseAttack()
        {
            var hero = CurrentHero();
            if (hero == null) return;
            if (DronesAlive())
            {
                _pendingAttack = true;
                _pendingSkillSlot = -1;
                AwaitingTarget = true;
                OnChanged?.Invoke();
            }
            else
            {
                StartCoroutine(AttackRoutine(hero, null));
            }
        }

        public void ChooseDefend()
        {
            var hero = CurrentHero();
            if (hero == null) return;
            hero.Defending = true;
            hero.Acted = true;
            AddLog($"{hero.Name} defends (half damage until their next turn).");
            StartCoroutine(FlashRoutine(hero.Figure.transform, 1.06f));
            OnChanged?.Invoke();
            CompleteHeroAction();
        }

        public void ChooseSkill(int slot)
        {
            var hero = CurrentHero();
            if (hero == null) return;
            if (slot < 0 || slot > 2) return;
            var skills = SkillCatalog.ForHero(hero.Member.Definition.Id);
            var skill = skills[slot];
            if (skill == null || hero.Cooldowns[slot] > 0) return;
            if (skill.IsAttack && DronesAlive())
            {
                _pendingAttack = false;
                _pendingSkillSlot = slot;
                AwaitingTarget = true;
                OnChanged?.Invoke();
            }
            else
            {
                StartCoroutine(SkillRoutine(hero, slot, null));
            }
        }

        /// <summary>target: 0 = the boss, 1.. = Drones[target-1].</summary>
        public void ChooseTarget(int target)
        {
            if (!AwaitingTarget) return;
            var hero = CurrentHero();
            if (hero == null) return;
            TurnDroneState drone = null;
            if (target > 0)
            {
                if (target - 1 >= Drones.Count) return;
                drone = Drones[target - 1];
                if (!drone.Alive) return;
            }
            AwaitingTarget = false;
            if (_pendingAttack) StartCoroutine(AttackRoutine(hero, drone));
            else if (_pendingSkillSlot >= 0) StartCoroutine(SkillRoutine(hero, _pendingSkillSlot, drone));
            _pendingAttack = false;
            _pendingSkillSlot = -1;
        }

        public void CancelTarget()
        {
            if (!AwaitingTarget) return;
            AwaitingTarget = false;
            _pendingAttack = false;
            _pendingSkillSlot = -1;
            OnChanged?.Invoke();
        }

        private TurnHeroState CurrentHero()
        {
            if (Finished || Busy || AwaitingTarget) return null;
            if (SelectedHero < 0 || SelectedHero >= Heroes.Count) return null;
            var hero = Heroes[SelectedHero];
            return hero.Alive && !hero.Acted ? hero : null;
        }

        /// <summary>What the boss will do on its next turn — the
        /// intent line that makes the fight readable.</summary>
        public string IntentText()
        {
            if (Finished) return "—";
            if (_slamCharged) return "SEISMIC SLAM lands this turn!";
            int next = _bossTurn + 1;
            if (next % 5 == 4) return "strikes, then starts charging SEISMIC SLAM";
            if (next % 3 == 0) return "THERMAL SWEEP (hits all heroes)";
            if (_tauntRounds > 0 && _tauntHero != null && _tauntHero.Alive)
                return $"strikes {_tauntHero.Name} (taunted)";
            return "strikes the weakest hero";
        }

        // ================= Hero actions =================

        private int HeroAtk(TurnHeroState hero)
        {
            return Mathf.RoundToInt(hero.Atk * (hero.AtkBuffRounds > 0 ? RallyAtkMult : 1f));
        }

        private IEnumerator AttackRoutine(TurnHeroState hero, TurnDroneState drone)
        {
            Busy = true;
            OnChanged?.Invoke();
            Vector3 targetPos = drone != null ? drone.Figure.transform.position : _bossRoot.position;
            if (hero.Class == UnitClass.Ranged)
            {
                yield return BoltRoutine(FigureChest(hero.Figure.transform), targetPos + Vector3.up * 2.2f,
                    new Color(0.6f, 0.9f, 1f));
            }
            else
            {
                yield return LungeRoutine(hero.Figure.transform, hero.Home, targetPos);
            }
            int dmg = CombatService.Damage(HeroAtk(hero), drone != null ? 0 : BossEffDef,
                hero.Class, UnitClass.Mechanized, hero.Level, BossLevel);
            if (hero.DeadeyeCharges > 0)
            {
                dmg = Mathf.RoundToInt(dmg * DeadeyeMult);
                hero.DeadeyeCharges--;
            }
            if (drone != null)
            {
                ApplyDroneDamage(hero, drone, dmg, "attacks");
                StartCoroutine(FlashRoutine(drone.Figure.transform, 1.15f));
            }
            else
            {
                ApplyBossDamage(hero, dmg, "attacks A.R.E.S.");
                StartCoroutine(FlashRoutine(_bossVisual, 1.04f));
            }
            hero.Acted = true;
            OnChanged?.Invoke();
            CompleteHeroAction();
        }

        private IEnumerator SkillRoutine(TurnHeroState hero, int slot, TurnDroneState drone)
        {
            Busy = true;
            OnChanged?.Invoke();
            var skill = SkillCatalog.ForHero(hero.Member.Definition.Id)[slot];
            hero.Cooldowns[slot] = skill.Cooldown;
            Vector3 bossPos = _bossRoot.position;
            switch (skill.Id)
            {
                case HeroSkillId.Rally:
                    foreach (var ally in Heroes)
                    {
                        if (ally.Alive) ally.AtkBuffRounds = RallyRounds;
                    }
                    AddLog($"{hero.Name} uses RALLY — team ATK up for {RallyRounds} rounds!");
                    yield return PulseAllHeroes();
                    break;

                case HeroSkillId.Bulwark:
                    foreach (var ally in Heroes)
                    {
                        if (ally.Alive) ally.DefBuffRounds = BulwarkRounds;
                    }
                    AddLog($"{hero.Name} uses BULWARK — team DEF up for {BulwarkRounds} rounds!");
                    yield return PulseAllHeroes();
                    break;

                case HeroSkillId.Deadeye:
                    hero.DeadeyeCharges = DeadeyeChargesGranted;
                    AddLog($"{hero.Name} uses DEADEYE — next {DeadeyeChargesGranted} attacks at x{DeadeyeMult} damage.");
                    yield return FlashRoutine(hero.Figure.transform, 1.10f);
                    break;

                case HeroSkillId.PrecisionStrike:
                {
                    yield return LungeRoutine(hero.Figure.transform, hero.Home,
                        drone != null ? drone.Figure.transform.position : bossPos);
                    int dmg = CombatService.Damage(
                        Mathf.RoundToInt(HeroAtk(hero) * PrecisionMult),
                        drone != null ? 0 : BossEffDef / 2,
                        hero.Class, UnitClass.Mechanized, hero.Level, BossLevel);
                    dmg = ConsumeDeadeye(hero, dmg);
                    if (drone != null) ApplyDroneDamage(hero, drone, dmg, "PRECISION STRIKE");
                    else ApplyBossDamage(hero, dmg, "PRECISION STRIKE");
                    break;
                }

                case HeroSkillId.PiercingShot:
                {
                    yield return BoltRoutine(FigureChest(hero.Figure.transform),
                        (drone != null ? drone.Figure.transform.position : bossPos) + Vector3.up * 2.2f,
                        new Color(1f, 0.75f, 0.35f));
                    int dmg = CombatService.Damage(
                        Mathf.RoundToInt(HeroAtk(hero) * PiercingMult), 0,
                        hero.Class, UnitClass.Mechanized, hero.Level, BossLevel);
                    dmg = ConsumeDeadeye(hero, dmg);
                    if (drone != null) ApplyDroneDamage(hero, drone, dmg, "PIERCING SHOT");
                    else ApplyBossDamage(hero, dmg, "PIERCING SHOT");
                    break;
                }

                case HeroSkillId.SuppressingFire:
                    yield return BoltRoutine(FigureChest(hero.Figure.transform), bossPos + Vector3.up * 2.2f,
                        new Color(1f, 0.55f, 0.2f));
                    _bossAtkDebuffRounds = SuppressRounds;
                    AddLog($"{hero.Name} uses SUPPRESSING FIRE — boss ATK down for {SuppressRounds} rounds.");
                    StartCoroutine(FlashRoutine(_bossVisual, 1.04f));
                    break;

                case HeroSkillId.AegisProtocol:
                {
                    int shield = Mathf.RoundToInt(hero.MaxHp * AegisShieldFrac);
                    foreach (var ally in Heroes)
                    {
                        if (!ally.Alive) continue;
                        ally.Shield = shield;
                        ally.ShieldRounds = AegisRounds;
                    }
                    AddLog($"{hero.Name} uses AEGIS PROTOCOL — team shielded for {shield} HP!");
                    yield return PulseAllHeroes();
                    break;
                }

                case HeroSkillId.GuardianTaunt:
                    _tauntRounds = TauntRounds;
                    _tauntHero = hero;
                    AddLog($"{hero.Name} uses GUARDIAN TAUNT — the boss must attack her for {TauntRounds} rounds.");
                    yield return FlashRoutine(hero.Figure.transform, 1.12f);
                    break;

                case HeroSkillId.ArmorBreak:
                {
                    yield return LungeRoutine(hero.Figure.transform, hero.Home,
                        drone != null ? drone.Figure.transform.position : bossPos);
                    int dmg = CombatService.Damage(
                        Mathf.RoundToInt(HeroAtk(hero) * ArmorBreakMult),
                        drone != null ? 0 : BossEffDef,
                        hero.Class, UnitClass.Mechanized, hero.Level, BossLevel);
                    dmg = ConsumeDeadeye(hero, dmg);
                    if (drone != null)
                    {
                        ApplyDroneDamage(hero, drone, dmg, "ARMOR BREAK");
                    }
                    else
                    {
                        ApplyBossDamage(hero, dmg, "ARMOR BREAK");
                        _bossDefDebuffRounds = ArmorBreakRounds;
                        AddLog($"A.R.E.S.'s armor is cracked — DEF down for {ArmorBreakRounds} rounds.");
                    }
                    break;
                }
            }
            hero.Acted = true;
            OnChanged?.Invoke();
            CompleteHeroAction();
        }

        private int ConsumeDeadeye(TurnHeroState hero, int dmg)
        {
            if (hero.DeadeyeCharges <= 0) return dmg;
            hero.DeadeyeCharges--;
            return Mathf.RoundToInt(dmg * DeadeyeMult);
        }

        // ================= Damage =================

        private void ApplyBossDamage(TurnHeroState hero, int dmg, string label)
        {
            if (_ventActive) dmg = Mathf.RoundToInt(dmg * VentVulnMult);
            BossHp = Mathf.Max(0, BossHp - dmg);
            AddLog($"{hero.Name} — {label}: {dmg} dmg" + (_ventActive ? " (core exposed!)" : "") + ".");
            if (!_phase2 && BossHp > 0 && BossHp <= BossMaxHp / 2)
            {
                _phase2 = true;
                if (!_dronesSummoned) _pendingSummon = true;
                AddLog("A.R.E.S. core exposed — PHASE 2. Drones incoming!");
                OnPhase2?.Invoke();
            }
            if (BossHp <= 0 && !Finished)
            {
                StartCoroutine(FinishRoutine(true, false));
            }
            OnChanged?.Invoke();
        }

        private void ApplyDroneDamage(TurnHeroState hero, TurnDroneState drone, int dmg, string label)
        {
            drone.Hp = Mathf.Max(0, drone.Hp - dmg);
            AddLog($"{hero.Name} — {label} {drone.Name}: {dmg} dmg.");
            if (drone.Hp <= 0 && drone.Alive)
            {
                drone.Alive = false;
                AddLog($"{drone.Name} destroyed!");
                StartCoroutine(DroneFallRoutine(drone));
            }
            OnChanged?.Invoke();
        }

        private void ApplyHeroDamage(TurnHeroState hero, int dmg, string source)
        {
            if (!hero.Alive) return;
            bool defended = hero.Defending;
            if (defended) dmg = Mathf.Max(1, dmg / 2);
            int absorbed = Mathf.Min(hero.Shield, dmg);
            hero.Shield -= absorbed;
            dmg -= absorbed;
            hero.Hp = Mathf.Max(0, hero.Hp - dmg);
            AddLog($"{source} hits {hero.Name} for {dmg}"
                + (absorbed > 0 ? $" ({absorbed} absorbed by shield)" : "")
                + (defended ? " (defending)" : "") + ".");
            StartCoroutine(FlashRoutine(hero.Figure.transform, 1.10f));
            if (hero.Hp <= 0)
            {
                hero.Alive = false;
                AddLog($"{hero.Name} is DOWN!");
                StartCoroutine(TipOverRoutine(hero));
            }
            OnChanged?.Invoke();
        }

        private TurnHeroState LowestHpHero()
        {
            TurnHeroState best = null;
            foreach (var hero in Heroes)
            {
                if (!hero.Alive) continue;
                if (best == null || hero.Hp < best.Hp) best = hero;
            }
            return best;
        }

        private TurnHeroState BossBasicTarget()
        {
            if (_tauntRounds > 0 && _tauntHero != null && _tauntHero.Alive) return _tauntHero;
            return LowestHpHero();
        }

        // ================= Boss + drone turns =================

        // The boss's slot in the turn order: summon drones first
        // if Phase 2 just triggered, then one action (the
        // telegraphed slam, the every-3rd-turn sweep, or a basic
        // strike), then the next slam starts charging on cadence.
        private IEnumerator BossActionRoutine()
        {
            yield return new WaitForSeconds(0.45f);
            if (Finished) yield break;

            if (_pendingSummon)
            {
                _pendingSummon = false;
                _dronesSummoned = true;
                yield return SummonDronesRoutine();
                if (Finished) yield break;
            }

            _bossTurn++;
            // The vent window closes as the boss acts again.
            _ventActive = false;
            if (_slamCharged)
            {
                _slamCharged = false;
                if (_slamRing != null) _slamRing.Hide();
                yield return SlamRoutine();
            }
            else if (_bossTurn % 3 == 0)
            {
                yield return SweepRoutine();
            }
            else
            {
                yield return BossBasicRoutine();
            }
            if (Finished) yield break;

            // Telegraph: the slam charges on turns 4, 9, 14, 19
            // and lands on 5, 10, 15, 20.
            if (_bossTurn % 5 == 4)
            {
                _slamCharged = true;
                AddLog("A.R.E.S. is charging SEISMIC SLAM — it lands next turn!");
                OnChanged?.Invoke();
            }

            _orderStep++;
            if (AllHeroesDown())
            {
                StartCoroutine(FinishRoutine(false, false));
                yield break;
            }
            AdvanceToNextActor();
        }

        // A drone's slot in the turn order: one shot at the
        // weakest hero. Drones summoned this round join the
        // order from the next round.
        private IEnumerator DroneTurnRoutine(TurnDroneState drone)
        {
            yield return DroneShootRoutine(drone);
            _orderStep++;
            if (AllHeroesDown())
            {
                StartCoroutine(FinishRoutine(false, false));
                yield break;
            }
            AdvanceToNextActor();
        }

        private IEnumerator BossBasicRoutine()
        {
            var target = BossBasicTarget();
            if (target == null) yield break;
            // The boss root lunges toward the target and back.
            Vector3 home = _bossRoot.position;
            Vector3 dir = target.Figure.transform.position - home;
            dir.y = 0f;
            Vector3 stop = home + dir.normalized * 1.8f;
            yield return MoveRoutine(_bossRoot, home, stop, 0.18f);
            int dmg = CombatService.Damage(BossEffAtk, target.EffDef,
                UnitClass.Mechanized, target.Class, BossLevel, target.Level);
            ApplyHeroDamage(target, dmg, "A.R.E.S.");
            yield return MoveRoutine(_bossRoot, stop, home, 0.18f);
        }

        private IEnumerator SweepRoutine()
        {
            AddLog("A.R.E.S. fires THERMAL SWEEP!");
            OnChanged?.Invoke();
            // A broad beam flash across the hero line.
            Vector3 bossPos = _bossRoot.position;
            Vector3 heroPos = Heroes.Count > 0 ? Heroes[0].Home : bossPos + Vector3.back * 20f;
            Vector3 mid = (bossPos + heroPos) / 2f;
            mid.y = 2.0f;
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "TurnSweepBeam";
            Destroy(beam.GetComponent<Collider>());
            var rend = beam.GetComponent<Renderer>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                var beamColor = new Color(1f, 0.42f, 0.12f);
                mat.color = beamColor;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", beamColor * 2.2f);
                rend.material = mat;
            }
            beam.transform.position = mid;
            beam.transform.LookAt(new Vector3(heroPos.x, 2.0f, heroPos.z));
            beam.transform.localScale = new Vector3(11f, 1.4f, Vector3.Distance(bossPos, heroPos));
            yield return new WaitForSeconds(0.28f);
            Destroy(beam);
            int atk = Mathf.RoundToInt(BossEffAtk * SweepAtkMult);
            foreach (var hero in Heroes)
            {
                if (!hero.Alive) continue;
                int dmg = CombatService.Damage(atk, hero.EffDef,
                    UnitClass.Mechanized, hero.Class, BossLevel, hero.Level);
                ApplyHeroDamage(hero, dmg, "THERMAL SWEEP");
            }
        }

        private IEnumerator SlamRoutine()
        {
            AddLog("SEISMIC SLAM!");
            OnChanged?.Invoke();
            // The boss rears up and crashes down.
            Vector3 orig = _bossVisual.localPosition;
            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.18f);
                _bossVisual.localPosition = orig + Vector3.up * (2.4f * p);
                yield return null;
            }
            t = 0f;
            while (t < 0.10f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.10f);
                _bossVisual.localPosition = orig + Vector3.up * (2.4f * (1f - p));
                yield return null;
            }
            _bossVisual.localPosition = orig;
            int atk = Mathf.RoundToInt(BossEffAtk * SlamAtkMult);
            foreach (var hero in Heroes)
            {
                if (!hero.Alive) continue;
                int dmg = CombatService.Damage(atk, hero.EffDef,
                    UnitClass.Mechanized, hero.Class, BossLevel, hero.Level);
                ApplyHeroDamage(hero, dmg, "SEISMIC SLAM");
            }
        }

        private IEnumerator SummonDronesRoutine()
        {
            AddLog("A.R.E.S. launches sub-drones!");
            OnChanged?.Invoke();
            Vector3 bossPos = _bossRoot.position;
            string[] names = { "Drone A", "Drone B" };
            for (int i = 0; i < 2; i++)
            {
                var fig = Instantiate(_droneTemplate);
                fig.name = "TurnDrone_" + (i + 1);
                fig.transform.SetParent(transform, false);
                fig.SetActive(true);
                Vector3 home = bossPos + new Vector3(i == 0 ? -3.4f : 3.4f, 3.4f, -2.2f);
                fig.transform.position = home + Vector3.up * 5f;
                var drone = new TurnDroneState
                {
                    Figure = fig,
                    Home = home,
                    Name = names[i],
                    MaxHp = Mathf.RoundToInt(DroneBaseHp * Mathf.Pow(1.3f, BossLevel - 1) * _enemyScale),
                    Atk = Mathf.RoundToInt(BossAtk * DroneAtkFrac),
                    Speed = DroneSpeed,
                };
                drone.Hp = drone.MaxHp;
                Drones.Add(drone);
                StartCoroutine(MoveRoutine(fig.transform, fig.transform.position, home, 0.55f));
            }
            yield return new WaitForSeconds(0.6f);
            OnChanged?.Invoke();
        }

        private IEnumerator DroneShootRoutine(TurnDroneState drone)
        {
            var target = LowestHpHero();
            if (target == null) yield break;
            yield return BoltRoutine(drone.Figure.transform.position,
                FigureChest(target.Figure.transform), new Color(1f, 0.5f, 0.2f), 0.10f);
            int dmg = CombatService.Damage(drone.Atk, target.EffDef,
                UnitClass.Mechanized, target.Class, BossLevel, target.Level);
            ApplyHeroDamage(target, dmg, drone.Name);
            yield return new WaitForSeconds(0.12f);
        }

        private IEnumerator RoundEndRoutine()
        {
            // Core venting (Phase 2): melee heroes burn, and the
            // boss takes +25% damage until its next action.
            if (_phase2 && _bossTurn % 3 == 2)
            {
                _ventActive = true;
                AddLog("The core VENTS — searing heat! A.R.E.S. takes +25% damage until it acts.");
                OnChanged?.Invoke();
                yield return new WaitForSeconds(0.35f);
                int atk = Mathf.RoundToInt(BossEffAtk * VentBurnMult);
                foreach (var hero in Heroes)
                {
                    if (!hero.Alive || hero.Class == UnitClass.Ranged) continue;
                    int dmg = CombatService.Damage(atk, hero.EffDef / 2,
                        UnitClass.Mechanized, hero.Class, BossLevel, hero.Level);
                    ApplyHeroDamage(hero, dmg, "CORE VENT");
                }
                if (AllHeroesDown())
                {
                    StartCoroutine(FinishRoutine(false, false));
                    yield break;
                }
            }
            // Countdowns.
            foreach (var hero in Heroes)
            {
                if (hero.AtkBuffRounds > 0) hero.AtkBuffRounds--;
                if (hero.DefBuffRounds > 0) hero.DefBuffRounds--;
                if (hero.ShieldRounds > 0)
                {
                    hero.ShieldRounds--;
                    if (hero.ShieldRounds == 0) hero.Shield = 0;
                }
            }
            if (_bossAtkDebuffRounds > 0) _bossAtkDebuffRounds--;
            if (_bossDefDebuffRounds > 0) _bossDefDebuffRounds--;
            if (_tauntRounds > 0) _tauntRounds--;
            Busy = false;
            StartRound();
        }

        // ================= Finish =================

        // Player surrender (2026-11-10): stops whatever is
        // mid-flight and ends the fight as a defeat through
        // the normal finish path — fallen heroes are the ones
        // already down; the view applies the defeat completion
        // (no rewards, energy not refunded).
        private bool _surrendered;

        public void Surrender()
        {
            if (Finished) return;
            _surrendered = true;
            StopAllCoroutines();
            StartCoroutine(FinishRoutine(false, false));
        }

        private IEnumerator FinishRoutine(bool victory, bool timeout)
        {
            if (Finished) yield break;
            Finished = true;
            Busy = true;
            AwaitingTarget = false;
            if (_slamRing != null) _slamRing.Hide();
            AddLog(victory ? "A.R.E.S.-09 DESTROYED!" : (timeout ? "Out of time — the boss still stands." : (_surrendered ? "Squad retreats — surrendered." : "Squad down.")));
            OnChanged?.Invoke();
            if (victory && _bossVisual != null)
            {
                float t = 0f;
                while (t < 1.2f)
                {
                    t += Time.deltaTime;
                    float p = Mathf.Clamp01(t / 1.2f);
                    _bossVisual.localRotation = _bossVisualOrigRot * Quaternion.Euler(75f * p, 0f, 0f);
                    Vector3 pos = _bossVisualOrigPos;
                    pos.y = _bossVisualOrigPos.y - 1.6f * p;
                    _bossVisual.localPosition = pos;
                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(0.8f);
            }
            var fallen = new List<int>();
            foreach (var hero in Heroes)
            {
                if (!hero.Alive) fallen.Add(hero.Member.SquadIndex);
            }
            OnEnded?.Invoke(victory, timeout, fallen);
        }

        // ================= Animation helpers =================

        private static Vector3 FigureChest(Transform fig)
        {
            return fig.position + Vector3.up * 1.8f;
        }

        private IEnumerator MoveRoutine(Transform target, Vector3 from, Vector3 to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
                target.position = Vector3.Lerp(from, to, p);
                yield return null;
            }
            target.position = to;
        }

        private IEnumerator LungeRoutine(Transform fig, Vector3 home, Vector3 targetPos)
        {
            Vector3 dir = targetPos - home;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dist < 0.01f) yield break;
            Vector3 stop = home + dir.normalized * Mathf.Max(0f, dist - 2.6f);
            yield return MoveRoutine(fig, home, stop, 0.20f);
            yield return new WaitForSeconds(0.07f);
            yield return MoveRoutine(fig, stop, home, 0.20f);
        }

        private IEnumerator BoltRoutine(Vector3 from, Vector3 to, Color color, float width = 0.16f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TurnBolt";
            Destroy(go.GetComponent<Collider>());
            var rend = go.GetComponent<Renderer>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                mat.color = color;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2f);
                rend.material = mat;
            }
            go.transform.position = (from + to) / 2f;
            go.transform.LookAt(to);
            go.transform.localScale = new Vector3(width, width, Vector3.Distance(from, to));
            yield return new WaitForSeconds(0.16f);
            Destroy(go);
        }

        private IEnumerator FlashRoutine(Transform target, float peak)
        {
            if (target == null) yield break;
            Vector3 baseScale = target.localScale;
            float t = 0f;
            while (t < 0.20f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.20f);
                float s = 1f + (peak - 1f) * Mathf.Sin(p * Mathf.PI);
                target.localScale = baseScale * s;
                yield return null;
            }
            target.localScale = baseScale;
        }

        private IEnumerator PulseAllHeroes()
        {
            foreach (var hero in Heroes)
            {
                if (hero.Alive) StartCoroutine(FlashRoutine(hero.Figure.transform, 1.08f));
            }
            yield return new WaitForSeconds(0.25f);
        }

        private IEnumerator TipOverRoutine(TurnHeroState hero)
        {
            float t = 0f;
            Quaternion target = hero.OrigRot * Quaternion.Euler(0f, 0f, -80f);
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.35f);
                hero.Figure.transform.rotation = Quaternion.Slerp(hero.OrigRot, target, p);
                yield return null;
            }
        }

        private IEnumerator DroneFallRoutine(TurnDroneState drone)
        {
            float t = 0f;
            Vector3 from = drone.Figure.transform.position;
            Vector3 to = from + Vector3.down * 3.2f;
            Quaternion origRot = drone.Figure.transform.rotation;
            Quaternion target = origRot * Quaternion.Euler(50f, 0f, 40f);
            while (t < 0.45f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.45f);
                drone.Figure.transform.position = Vector3.Lerp(from, to, p);
                drone.Figure.transform.rotation = Quaternion.Slerp(origRot, target, p);
                yield return null;
            }
        }

        private void AddLog(string line)
        {
            Log.Add(line);
            while (Log.Count > 8) Log.RemoveAt(0);
        }

        private void Update()
        {
            if (Finished) return;
            // Drones hover.
            for (int i = 0; i < Drones.Count; i++)
            {
                var drone = Drones[i];
                if (!drone.Alive || drone.Figure == null) continue;
                Vector3 pos = drone.Home;
                pos.y += Mathf.Sin(Time.time * 2.2f + i * 1.7f) * 0.25f;
                drone.Figure.transform.position = pos;
            }
            // The selected hero's disc pulses.
            for (int i = 0; i < Heroes.Count; i++)
            {
                var hero = Heroes[i];
                if (hero.Disc == null) continue;
                float s = 1.6f;
                if (i == SelectedHero && !Busy && hero.Alive && !hero.Acted)
                    s = 1.6f + Mathf.Sin(Time.time * 5f) * 0.18f;
                hero.Disc.localScale = new Vector3(s, 0.1f, s);
            }
            // The slam telegraph ring breathes at the boss's feet.
            if (_slamRing != null)
            {
                if (_slamCharged && _bossRoot != null)
                {
                    _slamRing.transform.position = _bossRoot.position + Vector3.up * 0.12f;
                    _slamRing.Show(7f, 0.72f + 0.28f * Mathf.Sin(Time.time * 4f));
                }
                else if (!_slamCharged)
                {
                    _slamRing.Hide();
                }
            }
        }
    }
}
