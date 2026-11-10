using System;
using System.Collections.Generic;
using UnityEngine;

namespace LunarDominion.Boss
{
    /// <summary>Scaling stats for one ladder level of A.R.E.S.-09,
    /// derived from the ladder encounter itself (EncounterCatalog's
    /// boss spawn) so the real-time boss IS the ladder boss: same
    /// table, real-time presentation. Health is multiplied up
    /// (a turn-based round resolves in ~1s of real-time hero attacks,
    /// and a boss fight should last minutes, not seconds). Attack
    /// values map the spawn's per-hit ATK onto the attack kit:
    /// Slam x1.7, Sweep tick x0.375, Stomp x0.75, Drone shot x0.2,
    /// Vent x0.42 dps — at ladder L1 (ATK 120) that reproduces the
    /// design doc's 200 / 45 / 90 / 24 / 50 exactly.</summary>
    public struct AresStats
    {
        public float MaxHealth;
        public float Defense;
        public float SlamDamage;
        public float SweepTickDamage;
        public float StompDamage;
        public float DroneHealth;
        public float DroneShotDamage;
        public float VentDamagePerSecond;

        // THE tuning lever for fight length (design doc Part 5):
        // raise it if fights feel short, lower it if they drag.
        public const float RealtimeHealthFactor = 8f;

        public static AresStats FromLadder(int spawnHealth, int spawnAttack,
            int spawnDefense, float nexusScale)
        {
            float hp = spawnHealth * nexusScale;
            float atk = spawnAttack * nexusScale;
            return new AresStats
            {
                MaxHealth = hp * RealtimeHealthFactor,
                Defense = spawnDefense * nexusScale,
                SlamDamage = atk * 1.7f,
                SweepTickDamage = atk * 0.375f,
                StompDamage = atk * 0.75f,
                DroneHealth = 170f * (spawnHealth / 5000f) * nexusScale,
                DroneShotDamage = atk * 0.2f,
                VentDamagePerSecond = atk * 0.42f,
            };
        }
    }

    /// <summary>What the boss needs from a hero. Implemented by
    /// AresHeroCombatant on the hero figure; heroes auto-attack and
    /// auto-position, the player only dodges / focus-fires.</summary>
    public interface IHeroCombatant
    {
        Transform Transform { get; }
        bool IsAlive { get; }
        bool IsDodging { get; }         // dash i-frames active
        float Range { get; }            // attack range band (world units)
        float AttackDamage { get; }     // per hit: full CombatService.Damage result vs the boss (counter + DEF included)
        float HealthFraction { get; }
        void TakeDamage(float damage);  // direct hit: hero DEF applies, min 5
        void TakeDot(float damage);     // damage over time: DEF spread thin, min 1
        void Knockback(Vector3 from, float force);
    }

    public enum AresState
    {
        Dormant, Intro,
        Idle, TelegraphSlam, Slam, TelegraphSweep, Sweep,
        VentWindup, Venting, SummonDrones,
        Defeated
    }

    /// <summary>A.R.E.S.-09 encounter controller: a timer-driven
    /// finite state machine (design doc Part 4). Phase 1 cycles
    /// Seismic Slam / Thermal Sweep / Stomp; at 50% HP Phase 2
    /// starts (faster cycle, 3 sub-drones, core venting with a
    /// +25% damage-taken trade-off). The controller sits on a fight
    /// root at the boss's arena position and rotates that root to
    /// track heroes; the boss MODEL is a child (VisualRoot) so the
    /// slam crouch can dip it without touching the root. The sweep
    /// beam hangs off a pivot child so its yaw is exact, independent
    /// of the FBX node frames. UI is events only — this class draws
    /// nothing itself beyond driving its transforms/telegraphs.</summary>
    public class AresBossController : MonoBehaviour
    {
        // ---- tuning (base values; phase 2 multiplies speed) ----
        private const float SlamRadius = 9f;
        private const float SlamTelegraphTime = 1.25f;
        private const float SlamWaveTime = 0.55f;   // ring expands to full radius
        private const float SweepRange = 14f;
        private const float SweepArcDegrees = 110f;
        private const float SweepDuration = 1.8f;
        private const float SweepTelegraphTime = 0.8f;
        private const float SweepTickInterval = 0.5f;
        private const float StompRange = 3.5f;
        private const float VentRadius = 6.5f;
        private const float VentWindupTime = 0.9f;
        private const float VentDuration = 3f;
        private const float VentPeriod = 7f;
        private const float VentCoreBonus = 0.25f;  // +25% damage taken while venting
        private const float Phase2Speed = 1.25f;
        private const float IdleTime = 1.1f;
        private const float RecoverTime = 0.6f;
        private const float BeamHeight = 5.0f;      // sweep pivot height (boss chest)

        // ---- events for the lair view / HUD ----
        public event Action<float, float> OnHealthChanged; // current, max
        public event Action<int> OnPhaseChanged;           // 1 or 2
        public event Action<string> OnTelegraph;           // attack name, for UI warnings
        public event Action OnSlamImpact;                  // dust burst / camera shake hook
        public event Action OnDefeated;
        public event Action OnSquadDefeated;               // all heroes down

        private Transform _visualRoot;      // boss model wrapper (crouch dips it)
        private Transform _sweepPivot;      // beam yaw pivot at BeamHeight
        private TelegraphRing _slamRing, _ventRing;
        private SweepBeam _beam;
        private GameObject _droneTemplate;

        private AresStats _stats;
        private float _health;
        private int _phase = 1;
        private AresState _state = AresState.Dormant;
        private float _stateTime;
        private float _slamCooldown, _sweepCooldown, _stompCooldown, _ventTimer;
        private bool _dronesSummoned;
        private bool _over;
        private readonly List<IHeroCombatant> _heroes = new List<IHeroCombatant>();
        private readonly List<SubDrone> _drones = new List<SubDrone>();
        private readonly List<Vector4> _coverSpheres = new List<Vector4>(); // xyz centre, w radius
        private SubDrone _focusDrone;

        private struct PendingHit
        {
            public IHeroCombatant Hero;
            public float TimeLeft;
            public float Damage;
            public float KnockbackForce;
        }
        private readonly List<PendingHit> _pendingHits = new List<PendingHit>();

        public float HealthFraction => _stats.MaxHealth > 0f ? _health / _stats.MaxHealth : 0f;
        public bool IsVenting => _state == AresState.Venting;
        public bool IsRunning => !_over && _state != AresState.Dormant;
        public int Phase => _phase;
        public List<SubDrone> Drones => _drones;

        /// <summary>Wire the fight. visualRoot is the boss model's
        /// wrapper transform (a child of this root). Rings are
        /// positioned by the caller at this root's position.
        /// coverSpheres are world-space (centre, radius) for the
        /// arena's cover boulders — the sweep beam is blocked by
        /// them (segment-sphere test, no physics layers needed).</summary>
        public void Setup(AresStats stats, IEnumerable<IHeroCombatant> heroes,
            GameObject droneTemplate, Transform visualRoot,
            TelegraphRing slamRing, TelegraphRing ventRing,
            IEnumerable<Vector4> coverSpheres)
        {
            _stats = stats;
            _health = stats.MaxHealth;
            _heroes.Clear();
            foreach (var h in heroes) if (h != null) _heroes.Add(h);
            _droneTemplate = droneTemplate;
            _visualRoot = visualRoot;
            _slamRing = slamRing;
            _ventRing = ventRing;
            _drones.Clear();
            _coverSpheres.Clear();
            if (coverSpheres != null) _coverSpheres.AddRange(coverSpheres);
            BuildSweepPivot();
        }

        private void BuildSweepPivot()
        {
            if (_sweepPivot != null) return;
            var pivotGo = new GameObject("SweepPivot");
            pivotGo.transform.SetParent(transform, false);
            pivotGo.transform.localPosition = new Vector3(0f, BeamHeight, 0f);
            _sweepPivot = pivotGo.transform;
            var beamGo = new GameObject("SweepBeam");
            beamGo.transform.SetParent(_sweepPivot, false);
            _beam = beamGo.AddComponent<SweepBeam>();
            _beam.Build(SweepRange);
        }

        public void BeginFight()
        {
            _phase = 1;
            _over = false;
            _dronesSummoned = false;
            _focusDrone = null;
            _pendingHits.Clear();
            _state = AresState.Intro;
            _stateTime = 1.5f;
            _ventTimer = VentPeriod;
            _slamCooldown = 1.5f;   // a breath before the first slam
            _sweepCooldown = 3.0f;
            _stompCooldown = 0f;
            OnHealthChanged?.Invoke(_health, _stats.MaxHealth);
            OnPhaseChanged?.Invoke(1);
        }

        /// <summary>Force-stop the fight (the view is closing or the
        /// squad wiped). The controller goes quiet; visuals are the
        /// view's to clean up.</summary>
        public void EndFight()
        {
            _over = true;
            _state = AresState.Dormant;
            SetBeam(false);
            _slamRing?.Hide();
            _ventRing?.Hide();
            _pendingHits.Clear();
        }

        /// <summary>Squad focus fire: heroes prefer this drone while
        /// it lives. Set by the view (drone HUD buttons).</summary>
        public void SetFocusDrone(SubDrone drone) { _focusDrone = drone; }

        /// <summary>The hero squad's current target drone, or null
        /// when they should hit the boss.</summary>
        public SubDrone HeroTarget()
        {
            if (_focusDrone != null && _focusDrone.IsAlive) return _focusDrone;
            _focusDrone = null;
            // Default: burn the first living drone if any are up and
            // the player has not chosen — drones left alone out-DPS
            // the squad, so ignoring them is never right for long.
            // (Kept OFF: target choice is the player's test. Heroes
            // hit the boss unless a drone is focused.)
            return null;
        }

        /// <summary>Nearest living hero to a world position (drone
        /// targeting + torso tracking).</summary>
        public IHeroCombatant NearestHero(Vector3 from)
        {
            IHeroCombatant nearest = null;
            float best = float.MaxValue;
            foreach (var hero in _heroes)
            {
                if (!hero.IsAlive) continue;
                float d = Vector3.Distance(from, hero.Transform.position);
                if (d < best) { best = d; nearest = hero; }
            }
            return nearest;
        }

        /// <summary>Hero damage entry point (hero auto-attacks call
        /// this — see TickHeroAttacks). rawDamage is the hero's full
        /// CombatService.Damage result, so the boss's DEF is already
        /// subtracted inside it — it is NOT subtracted again here.
        /// Venting exposes the core: +25% damage taken.</summary>
        public void TakeDamage(float rawDamage)
        {
            if (_over || _state == AresState.Dormant || _state == AresState.Defeated) return;
            float dmg = rawDamage;
            if (IsVenting) dmg *= 1f + VentCoreBonus;
            _health -= dmg;
            OnHealthChanged?.Invoke(Mathf.Max(0f, _health), _stats.MaxHealth);
            if (_health <= 0f)
            {
                _state = AresState.Defeated;
                _over = true;
                SetBeam(false);
                _slamRing?.Hide();
                _ventRing?.Hide();
                OnDefeated?.Invoke();
                return;
            }
            if (_phase == 1 && _health <= _stats.MaxHealth * 0.5f) EnterPhase2();
        }

        private void EnterPhase2()
        {
            _phase = 2;
            OnPhaseChanged?.Invoke(2);
            _state = AresState.SummonDrones;
            _stateTime = 1.2f;
        }

        private float SpeedMul => _phase == 2 ? Phase2Speed : 1f;

        private void Update()
        {
            if (_over || _state == AresState.Dormant) return;
            float dt = Time.deltaTime;
            _slamCooldown -= dt; _sweepCooldown -= dt; _stompCooldown -= dt;
            if (_phase == 2 && _dronesSummoned) _ventTimer -= dt;
            _stateTime -= dt * SpeedMul;

            TrackHeroes(dt);
            TickHeroAttacks(dt);
            TickPendingHits(dt);
            CheckSquadWiped();

            switch (_state)
            {
                case AresState.Intro:
                    if (_stateTime <= 0f) Goto(AresState.Idle, IdleTime);
                    break;
                case AresState.Idle:
                    SetCrouch(0f, dt);
                    if (_stateTime <= 0f) PickAttack();
                    break;
                case AresState.TelegraphSlam:
                    // The boss rears down as the ring contracts.
                    SetCrouch(Mathf.Clamp01(1f - _stateTime / (SlamTelegraphTime / SpeedMul)), dt);
                    _slamRing?.Show(SlamRadius, 1f - _stateTime / (SlamTelegraphTime / SpeedMul));
                    if (_stateTime <= 0f) StrikeSlam();
                    break;
                case AresState.Slam:
                    SetCrouch(0f, dt);
                    if (_stateTime <= 0f) Goto(AresState.Idle, RecoverTime + IdleTime);
                    break;
                case AresState.TelegraphSweep:
                    if (_stateTime <= 0f) BeginSweep();
                    break;
                case AresState.Sweep:
                    TickSweep(dt);
                    if (_stateTime <= 0f) { SetBeam(false); Goto(AresState.Idle, RecoverTime + IdleTime); }
                    break;
                case AresState.SummonDrones:
                    if (_stateTime <= 0f) { SpawnDrones(); Goto(AresState.Idle, IdleTime); }
                    break;
                case AresState.VentWindup:
                    _ventRing?.Show(VentRadius, 1f - _stateTime / (VentWindupTime / SpeedMul));
                    if (_stateTime <= 0f)
                    {
                        _state = AresState.Venting;
                        _stateTime = VentDuration;
                        _ventTickTimer = 0f;
                    }
                    break;
                case AresState.Venting:
                    TickVenting(dt);
                    if (_stateTime <= 0f)
                    {
                        _ventRing?.Hide();
                        _ventTimer = VentPeriod;
                        Goto(AresState.Idle, RecoverTime);
                    }
                    break;
            }
        }

        // The slam crouch: dip the model wrapper as the telegraph
        // builds, snap back on the strike (StrikeSlam resets it).
        private float _crouch;
        private void SetCrouch(float target, float dt)
        {
            _crouch = Mathf.MoveTowards(_crouch, target, dt * 2.2f);
            if (_visualRoot != null)
            {
                Vector3 p = _visualRoot.localPosition;
                p.y = -0.5f * _crouch;
                _visualRoot.localPosition = p;
            }
        }

        private void Goto(AresState next, float seconds)
        {
            _state = next;
            _stateTime = seconds / SpeedMul; // durations compress in phase 2
        }

        private void PickAttack()
        {
            if (_phase == 2 && _dronesSummoned && _ventTimer <= 0f)
            {
                OnTelegraph?.Invoke("Core Venting");
                Goto(AresState.VentWindup, VentWindupTime);
                return;
            }
            if (_stompCooldown <= 0f && AnyHeroWithin(StompRange))
            {
                StrikeStomp();
                return;
            }
            bool slamReady = _slamCooldown <= 0f;
            bool sweepReady = _sweepCooldown <= 0f;
            if (slamReady && (!sweepReady || UnityEngine.Random.value < 0.5f))
            {
                OnTelegraph?.Invoke("Seismic Slam");
                Goto(AresState.TelegraphSlam, SlamTelegraphTime);
            }
            else if (sweepReady)
            {
                OnTelegraph?.Invoke("Thermal Sweep");
                Goto(AresState.TelegraphSweep, SweepTelegraphTime);
            }
            else
            {
                Goto(AresState.Idle, 0.4f);
            }
        }

        // ---- Seismic Slam ----
        private void StrikeSlam()
        {
            _slamRing?.Hide();
            _slamCooldown = 7f;
            _crouch = 0f;
            SetCrouch(0f, 1f);
            OnSlamImpact?.Invoke();
            // The wave expands from the boss at SlamRadius per
            // SlamWaveTime; each hero is hit when the wavefront
            // passes THEIR distance — a dash timed to that moment
            // avoids it, which is the skill the telegraph teaches.
            float waveSpeed = SlamRadius / SlamWaveTime;
            foreach (var hero in _heroes)
            {
                if (!hero.IsAlive) continue;
                float d = Vector3.Distance(Flat(transform.position), Flat(hero.Transform.position));
                if (d > SlamRadius) continue;
                _pendingHits.Add(new PendingHit
                {
                    Hero = hero,
                    TimeLeft = d / waveSpeed,
                    Damage = _stats.SlamDamage,
                    KnockbackForce = 6f,
                });
            }
            Goto(AresState.Slam, SlamWaveTime);
        }

        private void TickPendingHits(float dt)
        {
            for (int i = _pendingHits.Count - 1; i >= 0; i--)
            {
                var hit = _pendingHits[i];
                hit.TimeLeft -= dt;
                if (hit.TimeLeft > 0f)
                {
                    _pendingHits[i] = hit;
                    continue;
                }
                _pendingHits.RemoveAt(i);
                if (hit.Hero.IsAlive && !hit.Hero.IsDodging)
                {
                    hit.Hero.TakeDamage(hit.Damage);
                    hit.Hero.Knockback(transform.position, hit.KnockbackForce);
                }
            }
        }

        // ---- Thermal Sweep ----
        private float _sweepTickTimer;

        private void BeginSweep()
        {
            _sweepCooldown = 6f;
            _sweepTickTimer = 0f;
            SetBeam(true);
            Goto(AresState.Sweep, SweepDuration);
        }

        private void TickSweep(float dt)
        {
            float duration = SweepDuration / SpeedMul;
            float t = Mathf.Clamp01(1f - _stateTime / duration);
            float sweepAngle = Mathf.Lerp(-SweepArcDegrees * 0.5f, SweepArcDegrees * 0.5f, t);
            if (_sweepPivot != null)
            {
                Vector3 e = _sweepPivot.localEulerAngles;
                e.y = sweepAngle;
                _sweepPivot.localEulerAngles = e;
            }
            _sweepTickTimer -= dt;
            if (_sweepTickTimer > 0f) return;
            _sweepTickTimer = SweepTickInterval;
            Vector3 beamDir = _sweepPivot != null ? Flat(_sweepPivot.forward) : Flat(transform.forward);
            Vector3 origin = transform.position + Vector3.up * BeamHeight;
            foreach (var hero in _heroes)
            {
                if (!hero.IsAlive || hero.IsDodging) continue;
                Vector3 toHero = Flat(hero.Transform.position) - Flat(transform.position);
                if (toHero.magnitude > SweepRange) continue;
                if (Vector3.Angle(beamDir, toHero) > 10f) continue;   // beam half-width
                if (BlockedByCover(origin, hero)) continue;           // boulders block it
                hero.TakeDamage(_stats.SweepTickDamage);
            }
        }

        // Cover: the boss->hero segment passes through a cover
        // sphere whose top reaches beam height. Segment-sphere
        // distance test — no physics layers or colliders needed.
        private bool BlockedByCover(Vector3 origin, IHeroCombatant hero)
        {
            Vector3 target = hero.Transform.position + Vector3.up * 1.5f;
            Vector3 seg = target - origin;
            float segLen = seg.magnitude;
            if (segLen < 0.01f) return false;
            Vector3 segDir = seg / segLen;
            foreach (var sphere in _coverSpheres)
            {
                Vector3 centre = new Vector3(sphere.x, sphere.y, sphere.z);
                if (centre.y + sphere.w < BeamHeight - 1.5f) continue; // too low to block
                Vector3 toCentre = centre - origin;
                float along = Vector3.Dot(toCentre, segDir);
                if (along < 0f || along > segLen) continue;
                Vector3 closest = origin + segDir * along;
                if (Vector3.Distance(closest, centre) < sphere.w * 0.85f) return true;
            }
            return false;
        }

        // ---- Stomp ----
        private void StrikeStomp()
        {
            _stompCooldown = 3f;
            foreach (var hero in _heroes)
            {
                if (!hero.IsAlive || hero.IsDodging) continue;
                float d = Vector3.Distance(Flat(transform.position), Flat(hero.Transform.position));
                if (d <= StompRange + 0.75f) hero.TakeDamage(_stats.StompDamage);
            }
            Goto(AresState.Slam, 0.4f); // reuse the short recover state
        }

        // ---- Venting (phase 2) ----
        private float _ventTickTimer;

        private void TickVenting(float dt)
        {
            _ventTickTimer -= dt;
            if (_ventTickTimer > 0f) return;
            _ventTickTimer = 0.25f; // quarter-second ticks of dps/4
            foreach (var hero in _heroes)
            {
                if (!hero.IsAlive || hero.IsDodging) continue;
                float d = Vector3.Distance(Flat(transform.position), Flat(hero.Transform.position));
                if (d <= VentRadius) hero.TakeDot(_stats.VentDamagePerSecond * 0.25f);
            }
        }

        // ---- Drones (phase 2) ----
        private void SpawnDrones()
        {
            _dronesSummoned = true;
            if (_droneTemplate == null) return;
            for (int i = 0; i < 3; i++)
            {
                var go = Instantiate(_droneTemplate, transform.position + Vector3.up * 6f, Quaternion.identity);
                go.SetActive(true);
                var drone = go.GetComponent<SubDrone>();
                if (drone == null) drone = go.AddComponent<SubDrone>();
                drone.Setup(this, _stats.DroneHealth, _stats.DroneShotDamage, i);
                _drones.Add(drone);
            }
        }

        // ---- Hero auto-attacks & root tracking ----
        private float _heroAttackTimer;

        private void TickHeroAttacks(float dt)
        {
            _heroAttackTimer -= dt;
            if (_heroAttackTimer > 0f) return;
            _heroAttackTimer = 1.0f; // one hit per hero per second
            foreach (var hero in _heroes)
            {
                if (!hero.IsAlive) continue;
                var drone = HeroTarget();
                if (drone != null)
                {
                    float d = Vector3.Distance(hero.Transform.position, drone.transform.position);
                    if (d <= hero.Range + 2.5f) drone.TakeDamage(hero.AttackDamage);
                }
                else
                {
                    float d = Vector3.Distance(Flat(hero.Transform.position), Flat(transform.position));
                    if (d <= hero.Range + 1.5f) TakeDamage(hero.AttackDamage);
                }
            }
        }

        private void TrackHeroes(float dt)
        {
            // Root yaw toward the nearest living hero (between
            // attacks the boss re-faces its next victim; the sweep
            // pivot inherits this yaw and adds its arc offset).
            if (_state != AresState.Idle && _state != AresState.Intro) return;
            var nearest = NearestHero(transform.position);
            if (nearest == null) return;
            Vector3 to = Flat(nearest.Transform.position) - Flat(transform.position);
            if (to.sqrMagnitude < 0.01f) return;
            Quaternion target = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 45f * SpeedMul * dt);
        }

        private bool AnyHeroWithin(float range)
        {
            foreach (var hero in _heroes)
            {
                if (!hero.IsAlive) continue;
                if (Vector3.Distance(Flat(transform.position), Flat(hero.Transform.position)) <= range) return true;
            }
            return false;
        }

        private void CheckSquadWiped()
        {
            if (_state == AresState.Defeated) return;
            foreach (var hero in _heroes)
            {
                if (hero.IsAlive) return;
            }
            if (_heroes.Count == 0) return;
            _over = true;
            _state = AresState.Dormant;
            SetBeam(false);
            OnSquadDefeated?.Invoke();
        }

        private void SetBeam(bool on)
        {
            if (_beam != null) _beam.SetActive(on);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }

    /// <summary>Ground telegraph: an expanding disc showing an
    /// incoming AoE. Built from a runtime primitive with an explicit
    /// URP Lit material (project magenta rule). Show() is called
    /// every frame during the telegraph with progress 0→1; the disc
    /// fills toward the full radius.</summary>
    public class TelegraphRing : MonoBehaviour
    {
        private MeshRenderer _disc;

        public void Build(Color color)
        {
            var discGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            discGo.transform.SetParent(transform, false);
            discGo.transform.localScale = new Vector3(2f, 0.05f, 2f);
            Destroy(discGo.GetComponent<Collider>());
            _disc = discGo.GetComponent<MeshRenderer>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                var mat = new Material(lit);
                mat.color = color;
                _disc.material = mat;
            }
            gameObject.SetActive(false);
        }

        public void Show(float radius, float progress)
        {
            gameObject.SetActive(true);
            float r = Mathf.Max(0.1f, radius * Mathf.Clamp01(progress));
            transform.localScale = new Vector3(r, 1f, r);
        }

        public void Hide() { gameObject.SetActive(false); }
    }

    /// <summary>The Thermal Sweep beam visual: a long thin box from
    /// the sweep pivot, emissive orange-red. Damage logic lives in
    /// the controller (angle checks); this is visual only and
    /// follows the pivot automatically as a child.</summary>
    public class SweepBeam : MonoBehaviour
    {
        public void Build(float range)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = new Vector3(0f, 0f, range * 0.5f);
            cube.transform.localScale = new Vector3(0.5f, 0.5f, range);
            Destroy(cube.GetComponent<Collider>());
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var renderer = cube.GetComponent<MeshRenderer>();
            if (lit != null && renderer != null)
            {
                var mat = new Material(lit);
                mat.color = new Color(1f, 0.3f, 0.1f);
                mat.SetColor("_EmissionColor", new Color(3f, 0.8f, 0.2f));
                mat.EnableKeyword("_EMISSION");
                renderer.material = mat;
            }
            gameObject.SetActive(false);
        }

        public void SetActive(bool on) { gameObject.SetActive(on); }
    }

    /// <summary>Phase 2 sub-drone: hovers, orbits the boss, shoots
    /// the nearest living hero on an interval. Killed via TakeDamage
    /// (hero focus fire routes here through the controller).</summary>
    public class SubDrone : MonoBehaviour
    {
        private AresBossController _boss;
        private float _health;
        private float _shotDamage;
        private int _orbitSlot;
        private float _shotTimer;

        public bool IsAlive => _health > 0f;
        public float HealthFraction => IsAlive ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        private float _maxHealth;

        public void Setup(AresBossController boss, float health, float shotDamage, int orbitSlot)
        {
            _boss = boss;
            _health = health;
            _maxHealth = health;
            _shotDamage = shotDamage;
            _orbitSlot = orbitSlot;
            _shotTimer = 1.5f + orbitSlot * 0.4f; // stagger first shots
        }

        public void TakeDamage(float rawDamage)
        {
            if (!IsAlive) return;
            _health -= Mathf.Max(1f, rawDamage); // drones: no DEF
            if (_health <= 0f) gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsAlive || _boss == null || !_boss.IsRunning) return;
            // Orbit the boss at radius 8, height ~5, slot-offset phase.
            float a = Time.time * 0.35f + _orbitSlot * (Mathf.PI * 2f / 3f);
            Vector3 center = _boss.transform.position;
            transform.position = center + new Vector3(
                Mathf.Cos(a) * 8f,
                5f + Mathf.Sin(Time.time * 1.3f + _orbitSlot) * 0.4f,
                Mathf.Sin(a) * 8f);
            _shotTimer -= Time.deltaTime;
            if (_shotTimer <= 0f)
            {
                _shotTimer = 1.5f;
                var target = _boss.NearestHero(transform.position);
                if (target != null && target.IsAlive && !target.IsDodging)
                    target.TakeDamage(_shotDamage);
            }
        }
    }
}
