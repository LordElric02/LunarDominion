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
        public GameObject BarRoot;
        public Transform BarFill;
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
        // Drone figure scale (2026-11-11): the template was
        // sized for the 3x boss; at 6x the drones read as floor
        // clutter, so the turn-based fight summons them doubled
        // (Patrick: 'make the drones bigger with a glow' — the
        // glow ring/eye live in BossLairView's drone template).
        private const float DroneFigureScale = 2f;

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

        // ---- Phase 3 (2026-11-11): code-driven posing of the
        // A.R.E.S. pivot rig (the FBX empties: Hips, Torso, Head,
        // ShoulderL/R, knees). Poses are Euler offsets composed
        // on each pivot's captured initial rotation — the rig's
        // own local rotations are safe to pose (unlike the FBX
        // root's). Presentation only: damage, timing of damage
        // application and the economy are untouched. The initial
        // pose is kept in statics because the fight component is
        // recreated per fight while the model persists: a later
        // fight first restores the authored pose, so a victory
        // collapse never leaks into the next fight's "initial".
        private Transform _rigHips, _rigTorso, _rigHead, _rigShoulderL, _rigShoulderR, _rigKneeL, _rigKneeR;
        private bool _rigOk;
        private Vector3 _hipsUpInParent = Vector3.up;
        private float _hipsParentScale = 6f;
        private static bool _rigCaptured;
        private static Quaternion _sHipsRot, _sTorsoRot, _sHeadRot, _sShLRot, _sShRRot, _sKneeLRot, _sKneeRRot;
        private static Vector3 _sHipsPos;
        private static bool _sHasShoulders, _sHasKnees;
        // Pose state: current values ease toward the targets in
        // Update; idle sway/scan and the recoil impulse layer on
        // top at application time; _defeatP is driven directly
        // by FinishRoutine (Update stops once Finished).
        private float _poseTorsoPitch, _poseTorsoPitchT;
        private float _poseTorsoYaw, _poseTorsoYawT;
        private float _poseShoulder, _poseShoulderT;
        private float _poseHipsDip, _poseHipsDipT;   // world units, down is negative-applied
        private float _recoil;
        private float _defeatP;
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

        // ================= Camera shake (Phase 5) =================
        // A shake is a temporary positional offset driven from
        // Update: while a shake is active the camera sits at the
        // captured base position plus Perlin-noise jitter scaled
        // by the remaining strength; while idle the base tracks
        // the camera, so any framing the view applies between
        // fights is adopted automatically. Since Cinematic C1 the
        // base pose itself is owned by the BossFightCamera shot
        // director while a fight runs — the shake offsets from
        // the director's pose, unchanged. Strengths: slam landing
        // 0.55, boss collapse 0.45, hero down 0.25, phase 2 onset
        // 0.2, basic blast impact 0.12. One knob: ShakeGain.
        private const float ShakeGain = 1.4f;
        private Camera _cam;
        private Vector3 _camBasePos;
        private float _shake;

        // Cinematic C1: the shot director (created in Begin).
        public BossFightCamera Director { get; private set; }

        /// <summary>Tap-to-skip for camera shots: the lair view
        /// calls this on a non-UI tap while an action plays.</summary>
        public void SkipShot()
        {
            if (Director != null) Director.SnapHome();
        }

        private void AddShake(float strength)
        {
            _shake = Mathf.Max(_shake, strength);
        }

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
            _cam = Camera.main;
            if (_cam != null) _camBasePos = _cam.transform.position;
            if (Director == null) Director = gameObject.AddComponent<BossFightCamera>();
            if (_cam != null) Director.CaptureHome(_cam, bossRoot);
            SetupRig();
            AddLog("A.R.E.S.-09 awakens. Choose each hero's action.");
            StartRound();
        }

        // ================= Boss rig posing (Phase 3) =================

        private static Transform DeepFind(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private void SetupRig()
        {
            _rigOk = false;
            if (_bossVisual == null) return;
            _rigHips = DeepFind(_bossVisual, "Hips");
            _rigTorso = DeepFind(_bossVisual, "Torso");
            _rigHead = DeepFind(_bossVisual, "Head");
            _rigShoulderL = DeepFind(_bossVisual, "ShoulderL");
            _rigShoulderR = DeepFind(_bossVisual, "ShoulderR");
            _rigKneeL = DeepFind(_bossVisual, "LegL_Knee");
            _rigKneeR = DeepFind(_bossVisual, "LegR_Knee");
            if (_rigHips == null || _rigTorso == null || _rigHead == null) return;
            if (_rigCaptured)
            {
                // A previous fight may have left the rig posed
                // (the victory collapse): restore the authored
                // pose before treating anything as initial.
                RestoreRig();
            }
            else
            {
                _sHipsRot = _rigHips.localRotation;
                _sTorsoRot = _rigTorso.localRotation;
                _sHeadRot = _rigHead.localRotation;
                _sHipsPos = _rigHips.localPosition;
                _sHasShoulders = _rigShoulderL != null && _rigShoulderR != null;
                if (_sHasShoulders)
                {
                    _sShLRot = _rigShoulderL.localRotation;
                    _sShRRot = _rigShoulderR.localRotation;
                }
                _sHasKnees = _rigKneeL != null && _rigKneeR != null;
                if (_sHasKnees)
                {
                    _sKneeLRot = _rigKneeL.localRotation;
                    _sKneeRRot = _rigKneeR.localRotation;
                }
                _rigCaptured = true;
            }
            // The hips dip is applied in the hips' parent space:
            // precompute which local direction is world-up there
            // (the FBX frame may not match Unity's) and the
            // parent scale, so dips can be specified in world
            // units. Ancestors only translate during a fight, so
            // this stays valid.
            if (_rigHips.parent != null)
            {
                _hipsUpInParent = _rigHips.parent.InverseTransformDirection(Vector3.up).normalized;
                float s = _rigHips.parent.lossyScale.y;
                if (s > 0.01f) _hipsParentScale = s;
            }
            _rigOk = true;
        }

        private void RestoreRig()
        {
            if (_rigHips != null) { _rigHips.localRotation = _sHipsRot; _rigHips.localPosition = _sHipsPos; }
            if (_rigTorso != null) _rigTorso.localRotation = _sTorsoRot;
            if (_rigHead != null) _rigHead.localRotation = _sHeadRot;
            if (_sHasShoulders)
            {
                if (_rigShoulderL != null) _rigShoulderL.localRotation = _sShLRot;
                if (_rigShoulderR != null) _rigShoulderR.localRotation = _sShRRot;
            }
            if (_sHasKnees)
            {
                if (_rigKneeL != null) _rigKneeL.localRotation = _sKneeLRot;
                if (_rigKneeR != null) _rigKneeR.localRotation = _sKneeRRot;
            }
            _poseTorsoPitch = _poseTorsoPitchT = 0f;
            _poseTorsoYaw = _poseTorsoYawT = 0f;
            _poseShoulder = _poseShoulderT = 0f;
            _poseHipsDip = _poseHipsDipT = 0f;
            _recoil = 0f;
            _defeatP = 0f;
        }

        private void SetPose(float torsoPitch, float torsoYaw, float shoulder)
        {
            _poseTorsoPitchT = torsoPitch;
            _poseTorsoYawT = torsoYaw;
            _poseShoulderT = shoulder;
        }

        private void SetHipsDip(float worldDip)
        {
            _poseHipsDipT = worldDip;
        }

        private void ApplyRigPose()
        {
            if (!_rigOk) return;
            float t = Time.time;
            float idle = 1f - _defeatP;
            float sway = Mathf.Sin(t * 0.9f) * 3.0f * idle;
            float scan = Mathf.Sin(t * 0.7f) * 26f * idle;
            _rigTorso.localRotation = _sTorsoRot * Quaternion.Euler(
                _poseTorsoPitch + sway - _recoil * 10f + _defeatP * 26f,
                _poseTorsoYaw, 0f);
            _rigHead.localRotation = _sHeadRot * Quaternion.Euler(
                _defeatP * 30f - _recoil * 13f,
                scan - _poseTorsoYaw * 0.5f, 0f);
            if (_sHasShoulders)
            {
                var sh = Quaternion.Euler(_poseShoulder + _defeatP * 16f, 0f, 0f);
                _rigShoulderL.localRotation = _sShLRot * sh;
                _rigShoulderR.localRotation = _sShRRot * sh;
            }
            float dipWorld = -_poseHipsDip - _defeatP * 2.6f;
            _rigHips.localPosition = _sHipsPos
                + _hipsUpInParent * (dipWorld / _hipsParentScale);
            if (_sHasKnees)
            {
                _rigKneeL.localRotation = _sKneeLRot * Quaternion.Euler(_defeatP * 16f, 0f, 0f);
                _rigKneeR.localRotation = _sKneeRRot * Quaternion.Euler(_defeatP * 10f, 0f, 0f);
            }
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
            // Not CurrentHero(): that guard returns null while
            // AwaitingTarget is set, which deadlocked this
            // method — target clicks silently did nothing and
            // the target row never closed (Patrick, 2026-11-10).
            // Resolve the acting hero directly.
            if (SelectedHero < 0 || SelectedHero >= Heroes.Count) return;
            var hero = Heroes[SelectedHero];
            if (!hero.Alive) return;
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
            if (Director != null) Director.PlayShot(BossShot.HeroStrike, hero.Figure.transform);
            Vector3 targetPos = drone != null ? drone.Figure.transform.position : _bossRoot.position;
            if (hero.Class == UnitClass.Ranged)
            {
                yield return BoltRoutine(FigureChest(hero.Figure.transform), targetPos + Vector3.up * (drone != null ? 2.2f : 8.5f),
                    new Color(0.6f, 0.9f, 1f));
            }
            else
            {
                yield return LungeRoutine(hero.Figure.transform, hero.Home, targetPos,
                    drone != null ? 2.6f : 5.2f);
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
            if (Director != null)
                Director.PlayShot(skill.IsAttack ? BossShot.HeroStrike : BossShot.SkillWide, hero.Figure.transform);
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
                        drone != null ? drone.Figure.transform.position : bossPos,
                        drone != null ? 2.6f : 5.2f);
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
                        (drone != null ? drone.Figure.transform.position : bossPos) + Vector3.up * (drone != null ? 2.2f : 8.5f),
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
                    yield return BoltRoutine(FigureChest(hero.Figure.transform), bossPos + Vector3.up * 8.5f,
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
                        drone != null ? drone.Figure.transform.position : bossPos,
                        drone != null ? 2.6f : 5.2f);
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
            _recoil = Mathf.Min(1f, _recoil + 0.55f);
            if (dmg > 0 && _bossRoot != null)
                SpawnDamageNumber(_bossRoot.position + Vector3.up * 12.2f, dmg,
                    new Color(1f, 0.95f, 0.6f), 0.7f);
            AddLog($"{hero.Name} — {label}: {dmg} dmg" + (_ventActive ? " (core exposed!)" : "") + ".");
            if (!_phase2 && BossHp > 0 && BossHp <= BossMaxHp / 2)
            {
                _phase2 = true;
                AddShake(0.2f);
                if (!_dronesSummoned) _pendingSummon = true;
                AddLog("A.R.E.S. core exposed — PHASE 2. Drones incoming!");
                OnPhase2?.Invoke();
                if (Director != null) Director.PlayShot(BossShot.Phase2Push);
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
            RefreshDroneBar(drone);
            if (dmg > 0 && drone.Figure != null)
                SpawnDamageNumber(drone.Figure.transform.position + Vector3.up * 2.2f, dmg,
                    new Color(1f, 0.8f, 0.4f), 0.28f);
            AddLog($"{hero.Name} — {label} {drone.Name}: {dmg} dmg.");
            if (drone.Hp <= 0 && drone.Alive)
            {
                drone.Alive = false;
                if (drone.BarRoot != null) drone.BarRoot.SetActive(false);
                AddLog($"{drone.Name} destroyed!");
                StartCoroutine(DroneFallRoutine(drone));
            }
            OnChanged?.Invoke();
        }

        // ================= Drone health bars =================

        // A small bar riding above each drone (Patrick,
        // 2026-11-10): dark backing + a red fill that shrinks
        // from the right as the drone takes damage. Built here
        // in the controller because drones spawn mid-fight;
        // the heroes' bars live in the view. The bar is a
        // child of the drone figure, so it follows the hover
        // and the flight in; it hides when the drone falls.
        private void BuildDroneBar(TurnDroneState drone)
        {
            var root = new GameObject("DroneBar");
            root.transform.SetParent(drone.Figure.transform, false);
            root.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            // The figure is scaled (DroneFigureScale); counter-
            // scale the bar so it keeps its original world size
            // instead of doubling with the drone. Its height
            // still benefits: local y 0.85 rides at 1.7 world
            // units over the doubled body.
            root.transform.localScale = Vector3.one * (1f / DroneFigureScale);
            BarBox(root, "DroneBarBg", new Vector3(1.6f, 0.22f, 0.06f), Vector3.zero,
                new Color(0.05f, 0.08f, 0.13f));
            var fill = BarBox(root, "DroneBarFill", new Vector3(1.46f, 0.14f, 0.08f),
                new Vector3(0f, 0f, -0.02f), new Color(0.90f, 0.22f, 0.21f));
            drone.BarRoot = root;
            drone.BarFill = fill.transform;
        }

        private static GameObject BarBox(GameObject parent, string name, Vector3 scale, Vector3 pos, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rend = go.GetComponent<Renderer>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                mat.color = color;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                rend.material = mat;
            }
            return go;
        }

        private void RefreshDroneBar(TurnDroneState drone)
        {
            if (drone.BarFill == null) return;
            float frac = drone.MaxHp > 0 ? Mathf.Clamp01((float)drone.Hp / drone.MaxHp) : 0f;
            float w = 1.46f * frac;
            drone.BarFill.localScale = new Vector3(Mathf.Max(0.001f, w), 0.14f, 0.08f);
            drone.BarFill.localPosition = new Vector3(-(1.46f - w) / 2f, 0f, -0.02f);
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
            if (dmg > 0)
                SpawnDamageNumber(hero.Figure.transform.position + Vector3.up * 4.1f, dmg,
                    new Color(1f, 0.45f, 0.3f), 0.38f);
            if (hero.Hp <= 0)
            {
                hero.Alive = false;
                AddLog($"{hero.Name} is DOWN!");
                AddShake(0.25f);
                StartCoroutine(TipOverRoutine(hero));
            }
            else if (dmg > 0 || absorbed > 0)
            {
                StartCoroutine(FlinchRoutine(hero));
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
                if (Director != null) Director.PlayShot(BossShot.BossCharge);
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
            if (Director != null) Director.PlayShot(BossShot.BossStrike);
            // Wind up (rig), then the boss root lunges toward
            // the target and back, striking at the lunge.
            SetPose(-10f, 0f, -12f);
            yield return new WaitForSeconds(0.28f);
            SetPose(15f, 0f, 14f);
            Vector3 home = _bossRoot.position;
            Vector3 dir = target.Figure.transform.position - home;
            dir.y = 0f;
            Vector3 stop = home + dir.normalized * 2.4f;
            yield return MoveRoutine(_bossRoot, home, stop, 0.18f);
            // Point-blank cannon blast: the shot covers the
            // last stretch into the target; damage lands with it.
            yield return StartCoroutine(BlastRoutine(
                _bossRoot.position + Vector3.up * 7.5f,
                FigureChest(target.Figure.transform)));
            int dmg = CombatService.Damage(BossEffAtk, target.EffDef,
                UnitClass.Mechanized, target.Class, BossLevel, target.Level);
            ApplyHeroDamage(target, dmg, "A.R.E.S.");
            SetPose(0f, 0f, 0f);
            yield return MoveRoutine(_bossRoot, stop, home, 0.18f);
        }

        private IEnumerator SweepRoutine()
        {
            AddLog("A.R.E.S. fires THERMAL SWEEP!");
            OnChanged?.Invoke();
            if (Director != null) Director.PlayShot(BossShot.BossCharge);
            // Phase 4 presentation: a target marker lights up
            // under every living hero, then a bolt strikes each
            // hero in turn — damage lands with each hero's own
            // bolt (the old single fat beam is gone). The sweep
            // arm pose swings with the shots.
            int atk = Mathf.RoundToInt(BossEffAtk * SweepAtkMult);
            var targets = new List<TurnHeroState>();
            var damages = new List<int>();
            foreach (var hero in Heroes)
            {
                if (!hero.Alive) continue;
                targets.Add(hero);
                damages.Add(CombatService.Damage(atk, hero.EffDef,
                    UnitClass.Mechanized, hero.Class, BossLevel, hero.Level));
            }
            var markers = new List<GameObject>();
            foreach (var hero in targets)
                markers.Add(MakeTargetMarker(hero.Home));
            SetPose(0f, -20f, 26f);
            yield return new WaitForSeconds(0.38f);
            SetPose(0f, 20f, 26f);
            Vector3 origin = _bossRoot.position + Vector3.up * 7.5f;
            var boltColor = new Color(1f, 0.42f, 0.12f);
            for (int i = 0; i < targets.Count; i++)
            {
                StartCoroutine(BoltRoutine(origin,
                    FigureChest(targets[i].Figure.transform), boltColor, 0.14f));
                ApplyHeroDamage(targets[i], damages[i], "THERMAL SWEEP");
                if (i + 1 < targets.Count)
                    yield return new WaitForSeconds(0.09f);
            }
            foreach (var m in markers) Destroy(m);
            SetPose(0f, 0f, 0f);
        }

        private IEnumerator SlamRoutine()
        {
            AddLog("SEISMIC SLAM!");
            OnChanged?.Invoke();
            if (Director != null) Director.PlayShot(BossShot.SlamImpact);
            // The boss rears up (rig leans back, arms raised)
            // and crashes down (rig pitches forward, hips dip).
            SetPose(-18f, 0f, 20f);
            Vector3 orig = _bossVisual.localPosition;
            float t = 0f;
            while (t < 0.18f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.18f);
                _bossVisual.localPosition = orig + Vector3.up * (2.4f * p);
                yield return null;
            }
            SetPose(26f, 0f, -8f);
            SetHipsDip(1.1f);
            t = 0f;
            while (t < 0.10f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.10f);
                _bossVisual.localPosition = orig + Vector3.up * (2.4f * (1f - p));
                yield return null;
            }
            _bossVisual.localPosition = orig;
            // The landing shockwave rolls out across the floor
            // as the damage lands (Phase 4), and the impact
            // shakes the camera (Phase 5).
            AddShake(0.55f);
            StartCoroutine(ShockwaveRoutine(_bossRoot.position));
            int atk = Mathf.RoundToInt(BossEffAtk * SlamAtkMult);
            foreach (var hero in Heroes)
            {
                if (!hero.Alive) continue;
                int dmg = CombatService.Damage(atk, hero.EffDef,
                    UnitClass.Mechanized, hero.Class, BossLevel, hero.Level);
                ApplyHeroDamage(hero, dmg, "SEISMIC SLAM");
            }
            SetPose(0f, 0f, 0f);
            SetHipsDip(0f);
        }

        private IEnumerator SummonDronesRoutine()
        {
            AddLog("A.R.E.S. launches sub-drones!");
            OnChanged?.Invoke();
            if (Director != null) Director.PlayShot(BossShot.BossCharge);
            SetPose(0f, 0f, 30f);
            Vector3 bossPos = _bossRoot.position;
            string[] names = { "Drone A", "Drone B" };
            for (int i = 0; i < 2; i++)
            {
                var fig = Instantiate(_droneTemplate);
                fig.name = "TurnDrone_" + (i + 1);
                fig.transform.SetParent(transform, false);
                fig.SetActive(true);
                fig.transform.localScale = Vector3.one * DroneFigureScale;
                // Spawn a touch wider than the original +-6.8:
                // at double size the drone's radius is ~1.1, and
                // 6.8 left almost no clearance from the boss's
                // 5.6 half-width.
                Vector3 home = bossPos + new Vector3(i == 0 ? -7.6f : 7.6f, 4.2f, -2.6f);
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
                BuildDroneBar(drone);
                StartCoroutine(MoveRoutine(fig.transform, fig.transform.position, home, 0.55f));
            }
            yield return new WaitForSeconds(0.6f);
            SetPose(0f, 0f, 0f);
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
                AddShake(0.45f);
                if (Director != null) Director.PlayShot(BossShot.Victory);
                // Defeat collapse (Phase 3): the rig crumples —
                // hips sink, knees bend, torso pitches forward,
                // head droops, shoulders drop — while the wrapper
                // keeps only a gentle lean and sink. (The old
                // 30-degree root slump alone swept the 17.8-unit
                // body too wide; the rig carries the collapse
                // now.) Update() no longer runs once Finished,
                // so this loop drives the pose directly. The
                // collapsed pose stays for the result panel; the
                // next fight's SetupRig restores the authored
                // pose from the statics before capturing.
                float t = 0f;
                while (t < 1.7f)
                {
                    t += Time.deltaTime;
                    float p = Mathf.Clamp01(t / 1.7f);
                    _bossVisual.localRotation = _bossVisualOrigRot * Quaternion.Euler(13f * p, 0f, 0f);
                    Vector3 pos = _bossVisualOrigPos;
                    pos.y = _bossVisualOrigPos.y - 1.4f * p;
                    _bossVisual.localPosition = pos;
                    _defeatP = p;
                    ApplyRigPose();
                    yield return null;
                }
            }
            else
            {
                // The boss still stands: return the rig to its
                // authored pose before handing back to the lair.
                // The defeat shot (C1) gets a beat longer to land.
                if (Director != null) Director.PlayShot(BossShot.Defeat);
                RestoreRig();
                yield return new WaitForSeconds(1.1f);
            }
            // Hand the camera back exactly as captured (Phase 5);
            // since C1 the captured pose is the director's home.
            _shake = 0f;
            if (_cam != null && Director != null)
            {
                _cam.transform.position = Director.HomePos;
                _cam.transform.rotation = Director.HomeRot;
                _cam.fieldOfView = Director.HomeFov;
                Director.Release();
            }
            else if (_cam != null) _cam.transform.position = _camBasePos;
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

        private IEnumerator LungeRoutine(Transform fig, Vector3 home, Vector3 targetPos,
            float stopGap = 2.6f)
        {
            Vector3 dir = targetPos - home;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dist < 0.01f) yield break;
            // stopGap is measured back from the target's centre:
            // 2.6 for drones, 5.2 for the boss — at 6x scale its
            // toes reach ~2.9 units from the root centre, so the
            // old gap put the hero inside the boss's feet.
            Vector3 stop = home + dir.normalized * Mathf.Max(0f, dist - stopGap);
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

        // ================= Phase 4 presentation =================
        // Traveling/impact effects sequenced so the damage frame
        // is the impact frame. Presentation only.

        /// <summary>A floating damage number: rises and fades
        /// over ~0.9s, facing the (static) fight camera. TextMesh
        /// uses the built-in font material — no runtime shader
        /// lookup, so nothing new can be stripped on Android.
        /// NOTE: characterSize renders ~5x larger than its face
        /// value at fontSize 64 (measured from Patrick's L2
        /// video: 2.6 produced digits a third of the screen
        /// tall), so the sizes at the call sites are small:
        /// boss 0.7, heroes 0.38, drones 0.28.</summary>
        private void SpawnDamageNumber(Vector3 pos, int dmg, Color color, float size)
        {
            var go = new GameObject("DamageNumber");
            go.transform.SetParent(transform, true);
            pos.x += UnityEngine.Random.Range(-0.7f, 0.7f);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = dmg.ToString();
            tm.fontSize = 64;
            tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            if (Camera.main != null)
                go.transform.rotation = Camera.main.transform.rotation;
            StartCoroutine(DamageNumberRoutine(go));
        }

        private IEnumerator DamageNumberRoutine(GameObject go)
        {
            var tm = go.GetComponent<TextMesh>();
            Vector3 start = go.transform.position;
            Color baseColor = tm.color;
            float t = 0f;
            while (t < 0.9f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.9f);
                go.transform.position = start + Vector3.up * (1.7f * p);
                var c = baseColor;
                c.a = 1f - p * p;
                tm.color = c;
                yield return null;
            }
            Destroy(go);
        }

        /// <summary>A hit reaction: the hero rocks back and
        /// recovers. Rotation only, composed on the figure's
        /// original rotation — position and scale belong to the
        /// move/flash routines, so nothing fights them.</summary>
        private IEnumerator FlinchRoutine(TurnHeroState hero)
        {
            var fig = hero.Figure.transform;
            float t = 0f;
            while (t < 0.26f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.26f);
                float s = Mathf.Sin(p * Mathf.PI);
                fig.rotation = hero.OrigRot * Quaternion.Euler(-10f * s, 0f, 0f);
                yield return null;
            }
            fig.rotation = hero.OrigRot;
        }

        /// <summary>A glowing shot that travels from -> to and
        /// is destroyed on arrival; the caller applies damage
        /// right after this completes.</summary>
        private IEnumerator BlastRoutine(Vector3 from, Vector3 to)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BossBlast";
            go.transform.SetParent(transform, true);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.65f;
            var rend = go.GetComponent<Renderer>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                var c = new Color(1f, 0.5f, 0.15f);
                mat.color = c;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 2.6f);
                rend.material = mat;
            }
            float t = 0f;
            while (t < 0.09f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.09f);
                go.transform.position = Vector3.Lerp(from, to, p);
                yield return null;
            }
            Destroy(go);
            // The blast's arrival is the impact frame (Phase 5).
            AddShake(0.12f);
        }

        /// <summary>The slam's ground shockwave: an additive
        /// disc (URP Unlit recipe, the ambience's proven
        /// transparent setup) expanding to ~17 units and fading
        /// over half a second.</summary>
        private IEnumerator ShockwaveRoutine(Vector3 centre)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SlamShockwave";
            go.transform.SetParent(transform, true);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = centre + Vector3.up * 0.18f;
            var rend = go.GetComponent<Renderer>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var col = new Color(1f, 0.52f, 0.18f, 0.38f);
            Material mat = null;
            if (sh != null)
            {
                mat = new Material(sh);
                mat.SetColor("_BaseColor", col);
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = 3000;
                rend.material = mat;
            }
            float t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / 0.5f);
                float r = 1f + 16f * (1f - (1f - p) * (1f - p));
                go.transform.localScale = new Vector3(2f * r, 0.12f, 2f * r);
                if (mat != null)
                {
                    var c = col;
                    c.a = col.a * (1f - p);
                    mat.SetColor("_BaseColor", c);
                }
                yield return null;
            }
            Destroy(go);
        }

        /// <summary>A red target disc under a hero (Thermal
        /// Sweep's lock-on marker).</summary>
        private GameObject MakeTargetMarker(Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "SweepTargetMarker";
            go.transform.SetParent(transform, true);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = pos + Vector3.up * 0.09f;
            go.transform.localScale = new Vector3(2.2f, 0.07f, 2.2f);
            var rend = go.GetComponent<Renderer>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                var c = new Color(1f, 0.25f, 0.08f);
                mat.color = c;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * 2.4f);
                rend.material = mat;
            }
            return go;
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
            // Camera shake (Phase 5) — deliberately before the
            // Finished early-out so the victory-collapse shake
            // plays during FinishRoutine.
            if (_cam != null)
            {
                // Cinematic C1: while the shot director is active
                // it owns the base pose and the shake offsets from
                // it; inactive, the base tracks the camera exactly
                // as the pre-C1 code did.
                if (Director != null && Director.Active)
                {
                    Director.Tick(Time.deltaTime);
                    _camBasePos = Director.BasePos;
                    _cam.transform.rotation = Director.BaseRot;
                    _cam.fieldOfView = Director.BaseFov;
                }
                if (_shake > 0.002f)
                {
                    float n = Time.time * 47f;
                    float m = _shake * ShakeGain;
                    _cam.transform.position = _camBasePos + new Vector3(
                        (Mathf.PerlinNoise(n, 3.7f) - 0.5f) * m,
                        (Mathf.PerlinNoise(9.1f, n) - 0.5f) * m,
                        (Mathf.PerlinNoise(n * 0.83f, 7.3f) - 0.5f) * m * 0.6f);
                    _shake = Mathf.MoveTowards(_shake, 0f, Time.deltaTime * 1.5f);
                    if (_shake <= 0.002f) _cam.transform.position = _camBasePos;
                }
                else if (Director != null && Director.Active)
                {
                    _cam.transform.position = _camBasePos;
                }
                else
                {
                    _camBasePos = _cam.transform.position;
                }
            }
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
                    _slamRing.Show(8.5f, 0.72f + 0.28f * Mathf.Sin(Time.time * 4f));
                }
                else if (!_slamCharged)
                {
                    _slamRing.Hide();
                }
            }
            // Boss rig posing (Phase 3): ease the pose values
            // toward their targets, decay the hit recoil, apply.
            if (_rigOk)
            {
                float k = 1f - Mathf.Exp(-7f * Time.deltaTime);
                _poseTorsoPitch += (_poseTorsoPitchT - _poseTorsoPitch) * k;
                _poseTorsoYaw += (_poseTorsoYawT - _poseTorsoYaw) * k;
                _poseShoulder += (_poseShoulderT - _poseShoulder) * k;
                _poseHipsDip += (_poseHipsDipT - _poseHipsDip) * k;
                _recoil = Mathf.MoveTowards(_recoil, 0f, Time.deltaTime * 3.2f);
                ApplyRigPose();
            }
        }
    }
}
