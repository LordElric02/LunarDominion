using UnityEngine;

namespace LunarDominion.Boss
{
    /// <summary>Camera shots for the turn-based boss fight
    /// (Cinematic Boss Fight Spec, Phase C1, 2026-11-13). Shots are
    /// presentation only: they never block a routine, so they add
    /// zero time to the fight's input-locked windows.</summary>
    public enum BossShot
    {
        Home,
        HeroStrike,
        SkillWide,
        BossStrike,
        BossCharge,
        SlamImpact,
        Phase2Push,
        Victory,
        Defeat,
    }

    /// <summary>The C1 shot director. It owns the fight camera's
    /// base pose (position / aim / field of view / roll) while a
    /// fight runs: TurnBasedBossFight ticks it every frame and the
    /// Phase 5 shake keeps offsetting from the base pose exactly as
    /// before. A shot blends in, holds briefly, then returns to the
    /// home framing captured at fight start — orders are always
    /// given from home. Tapping empty space mid-action calls
    /// SnapHome and skips the shot instantly.
    ///
    /// HARD RULE (arena geometry): computed shot positions are
    /// clamped inside the arena — the wall ring sits at r ~29.5
    /// from the arena centre and the ceiling at ~27, so shots stay
    /// within r 25 and below y 23. The captured home framing is
    /// trusted as-is (it is the view's proven framing).</summary>
    public class BossFightCamera : MonoBehaviour
    {
        private struct Pose
        {
            public Vector3 Pos;
            public Vector3 Aim;
            public float Fov;
            public float Roll;

            public Quaternion Rot
            {
                get
                {
                    Vector3 dir = Aim - Pos;
                    if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
                    return Quaternion.LookRotation(dir.normalized, Vector3.up)
                        * Quaternion.AngleAxis(Roll, Vector3.forward);
                }
            }

            public static Pose Blend(Pose a, Pose b, float t)
            {
                Pose p;
                p.Pos = Vector3.Lerp(a.Pos, b.Pos, t);
                p.Aim = Vector3.Lerp(a.Aim, b.Aim, t);
                p.Fov = Mathf.Lerp(a.Fov, b.Fov, t);
                p.Roll = Mathf.Lerp(a.Roll, b.Roll, t);
                return p;
            }
        }

        private const float MaxRadius = 25f;
        private const float MaxHeight = 23f;
        private static readonly Vector2 ArenaCentreXZ = new Vector2(500f, 0f);

        private Transform _bossRoot;
        private Pose _home;
        private Pose _cur;
        private Pose _from;
        private Pose _to;
        private bool _blending;
        private float _blendT;
        private float _blendDur = 0.25f;
        private float _returnAt = -1f;
        private bool _returning;

        public bool Active { get; private set; }
        public Vector3 BasePos => _cur.Pos;
        public Quaternion BaseRot => _cur.Rot;
        public float BaseFov => _cur.Fov;
        public Vector3 HomePos => _home.Pos;
        public Quaternion HomeRot => _home.Rot;
        public float HomeFov => _home.Fov;

        private Vector3 BossPos => _bossRoot != null
            ? _bossRoot.position
            : new Vector3(500f, 0f, 10f);

        /// <summary>Called from the fight's Begin with the camera
        /// exactly as the lair view framed it: that framing becomes
        /// home, and Release hands it back untouched.</summary>
        public void CaptureHome(Camera cam, Transform bossRoot)
        {
            _bossRoot = bossRoot;
            Vector3 aim = cam.transform.position + cam.transform.forward * 30f;
            _home = new Pose
            {
                Pos = cam.transform.position,
                Aim = aim,
                Fov = cam.fieldOfView,
                Roll = 0f,
            };
            _cur = _home;
            _from = _home;
            _to = _home;
            _blending = false;
            _returnAt = -1f;
            _returning = false;
            Active = true;
        }

        public void PlayShot(BossShot shot, Transform heroAnchor = null)
        {
            if (!Active) return;
            Vector3 boss = BossPos;
            Pose target;
            float blend;
            float hold;
            switch (shot)
            {
                case BossShot.HeroStrike:
                {
                    // Low angle behind the acting hero, looking up
                    // past them at the boss; a slight Dutch roll
                    // that the return home eases back out.
                    Vector3 h = heroAnchor != null
                        ? heroAnchor.position
                        : new Vector3(500f, 0f, -2f);
                    target = new Pose
                    {
                        Pos = h + new Vector3(-2.4f, 1.9f, -4.6f),
                        Aim = boss + Vector3.up * 8.0f,
                        Fov = _home.Fov - 8f,
                        Roll = 4f,
                    };
                    blend = 0.22f;
                    hold = 0.55f;
                    break;
                }
                case BossShot.SkillWide:
                {
                    // A touch lower and closer than home, aimed at
                    // the middle ground so the whole team reads in
                    // frame for buffs and support skills.
                    target = new Pose
                    {
                        Pos = _home.Pos + new Vector3(0f, -2.5f, 2.0f),
                        Aim = new Vector3(500f, 4f, 4f),
                        Fov = _home.Fov + 4f,
                        Roll = 0f,
                    };
                    blend = 0.25f;
                    hold = 0.6f;
                    break;
                }
                case BossShot.BossStrike:
                {
                    // A mild push-in from home for the boss's basic
                    // attack — the lunge itself does the travelling.
                    target = new Pose
                    {
                        Pos = _home.Pos + new Vector3(0f, -1.5f, 2.5f),
                        Aim = boss + Vector3.up * 8.5f,
                        Fov = _home.Fov - 4f,
                        Roll = 0f,
                    };
                    blend = 0.22f;
                    hold = 0.5f;
                    break;
                }
                case BossShot.BossCharge:
                {
                    // Tracks in toward the boss's core: Thermal
                    // Sweep, the Seismic Slam charge, drone launch.
                    target = new Pose
                    {
                        Pos = boss + new Vector3(5.5f, 4.2f, -13.5f),
                        Aim = boss + Vector3.up * 8.5f,
                        Fov = _home.Fov - 6f,
                        Roll = 0f,
                    };
                    blend = 0.3f;
                    hold = 1.1f;
                    break;
                }
                case BossShot.SlamImpact:
                {
                    // A quick pull up and back as the slam lands;
                    // the Phase 5 shake rides on top of it.
                    target = new Pose
                    {
                        Pos = _home.Pos + new Vector3(0f, 3.5f, -3.0f),
                        Aim = _home.Aim,
                        Fov = _home.Fov,
                        Roll = 0f,
                    };
                    blend = 0.18f;
                    hold = 0.3f;
                    break;
                }
                case BossShot.Phase2Push:
                {
                    // The slow dolly-in when Phase 2 begins; this
                    // closer framing becomes the new home for the
                    // rest of the fight.
                    target = new Pose
                    {
                        Pos = _home.Pos + new Vector3(0f, -0.5f, 3.0f),
                        Aim = _home.Aim,
                        Fov = _home.Fov - 3f,
                        Roll = 0f,
                    };
                    blend = 1.6f;
                    hold = -1f;
                    break;
                }
                case BossShot.Victory:
                {
                    // A short, tight drift toward the collapsing
                    // boss, matched to the collapse animation.
                    target = new Pose
                    {
                        Pos = boss + new Vector3(-9f, 3.5f, -11f),
                        Aim = boss + Vector3.up * 6.0f,
                        Fov = _home.Fov - 5f,
                        Roll = 0f,
                    };
                    blend = 1.6f;
                    hold = -1f;
                    break;
                }
                case BossShot.Defeat:
                {
                    // A slow rise and widen over the fallen squad.
                    target = new Pose
                    {
                        Pos = _home.Pos + new Vector3(0f, 6.0f, -6.0f),
                        Aim = new Vector3(500f, 1f, -2f),
                        Fov = _home.Fov + 6f,
                        Roll = 0f,
                    };
                    blend = 1.0f;
                    hold = -1f;
                    break;
                }
                default:
                {
                    target = _home;
                    blend = 0.25f;
                    hold = -1f;
                    break;
                }
            }
            target.Pos = ClampToArena(target.Pos);
            _from = _cur;
            _to = target;
            _blendT = 0f;
            _blendDur = Mathf.Max(0.01f, blend);
            _blending = true;
            _returning = false;
            _returnAt = hold < 0f ? -1f : Time.time + blend + hold;
            if (shot == BossShot.Phase2Push) _home = target;
        }

        /// <summary>Tap-to-skip: blend straight back to the home
        /// framing, fast enough to feel instant.</summary>
        public void SnapHome()
        {
            if (!Active || _returning) return;
            _returning = true;
            _from = _cur;
            _to = _home;
            _blendT = 0f;
            _blendDur = 0.12f;
            _blending = true;
            _returnAt = -1f;
        }

        /// <summary>Advances the current shot. Called by the fight
        /// every frame while active; never blocks anything.</summary>
        public void Tick(float dt)
        {
            if (!Active) return;
            if (_blending)
            {
                _blendT += dt;
                float p = Mathf.Clamp01(_blendT / _blendDur);
                _cur = Pose.Blend(_from, _to, Mathf.SmoothStep(0f, 1f, p));
                if (p >= 1f)
                {
                    _blending = false;
                    _returning = false;
                }
            }
            else if (_returnAt >= 0f && Time.time >= _returnAt)
            {
                _returning = true;
                _from = _cur;
                _to = _home;
                _blendT = 0f;
                _blendDur = 0.3f;
                _blending = true;
                _returnAt = -1f;
            }
        }

        /// <summary>The fight restores the captured home pose
        /// itself, then releases the director; the lair view owns
        /// the camera again from there.</summary>
        public void Release()
        {
            Active = false;
        }

        private static Vector3 ClampToArena(Vector3 pos)
        {
            Vector2 xz = new Vector2(pos.x - ArenaCentreXZ.x, pos.z - ArenaCentreXZ.y);
            if (xz.magnitude > MaxRadius)
            {
                xz = xz.normalized * MaxRadius;
                pos.x = ArenaCentreXZ.x + xz.x;
                pos.z = ArenaCentreXZ.y + xz.y;
            }
            pos.y = Mathf.Min(pos.y, MaxHeight);
            return pos;
        }
    }
}
