using System.Collections.Generic;
using LunarDominion.Core;
using LunarDominion.Expeditions;
using LunarDominion.Units;
using UnityEngine;

namespace LunarDominion.Boss
{
    /// <summary>One hero in the real-time boss fight (design doc
    /// Part 3): wraps the hero's figure GameObject and its persistent
    /// OwnedUnit/UnitDefinition, and implements IHeroCombatant for
    /// AresBossController. Stats come from the same formulas as the
    /// turn-based engine — UnitStats level scaling, research class
    /// multipliers, and CombatService.Damage for the hero's hits
    /// (counter triangle + level modifier included), so a hero hits
    /// exactly as hard here as in ExpeditionService fights.
    /// Movement: the hero glides to its slot — its range band from
    /// the boss at a fixed bearing assigned at spawn — and the
    /// player's only direct control is the dash (tap the hero's
    /// HUD chip): a fast lateral hop around the boss with 0.4s of
    /// invulnerability, on a 2.2s cooldown.</summary>
    public class AresHeroCombatant : MonoBehaviour, IHeroCombatant
    {
        private const float MoveSpeed = 6f;
        private const float DashDuration = 0.4f;    // i-frames
        private const float DashTravelTime = 0.18f; // the hop itself
        private const float DashCooldown = 2.2f;
        private const float DashArcDegrees = 45f;   // bearing change per dash

        private Transform _bossRoot;
        private float _bearing;             // radians around the boss
        private float _dashBearing;
        private float _range;
        private int _attackPerHit;          // via CombatService.Damage, boss DEF included
        private int _defenseScaled;
        private float _maxHealth;
        private float _health;
        private bool _active;               // fight running
        private float _dashTimer;           // > 0 while i-frames last
        private float _dashCooldownTimer;
        private float _dashTravelTimer;
        private Vector3 _knockbackVel;
        private float _fallTimer = -1f;     // >= 0 once fallen (death anim)
        private float _flashTimer;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Color> _baseColors = new List<Color>();

        public OwnedUnit Unit { get; private set; }
        public UnitDefinition Definition { get; private set; }

        public Transform Transform => transform;
        public bool IsAlive => _health > 0f;
        public bool IsDodging => _dashTimer > 0f;
        public float Range => _range;
        public float AttackDamage => _attackPerHit;
        public float HealthFraction => _maxHealth > 0f ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        public float DashCooldownFraction => DashCooldown > 0f
            ? 1f - Mathf.Clamp01(_dashCooldownTimer / DashCooldown) : 1f;
        public string DisplayName => Definition != null ? Definition.DisplayName : "Hero";

        /// <summary>bossDefense / bossLevel come from the fight's
        /// AresStats + ladder level, so AttackDamage is the exact
        /// per-hit number CombatService.Damage would roll in the
        /// turn-based engine (boss class is Mechanized).</summary>
        public void Setup(OwnedUnit unit, UnitDefinition definition, Transform bossRoot,
            float bearingDegrees, float range, float bossDefense, int bossLevel,
            float researchHpMult, float researchAtkMult, float researchDefMult)
        {
            Unit = unit;
            Definition = definition;
            _bossRoot = bossRoot;
            _bearing = bearingDegrees * Mathf.Deg2Rad;
            _dashBearing = _bearing;
            _range = range;
            int level = Mathf.Max(1, unit.Level);
            _maxHealth = Mathf.Round(UnitStats.ScaledHealth(definition, level) * researchHpMult);
            _health = _maxHealth;
            _defenseScaled = Mathf.RoundToInt(UnitStats.ScaledDefense(definition, level) * researchDefMult);
            float bossDefScaled = bossDefense;
            _attackPerHit = CombatService.Damage(
                Mathf.RoundToInt(UnitStats.ScaledAttack(definition, level) * researchAtkMult),
                Mathf.RoundToInt(bossDefScaled),
                definition.Class, UnitClass.Mechanized, level, bossLevel);
            _renderers.Clear();
            _baseColors.Clear();
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || renderer.material == null) continue;
                _renderers.Add(renderer);
                _baseColors.Add(renderer.material.color);
            }
        }

        public void SetActive(bool active) { _active = active; }

        /// <summary>Player input: dash this hero (called by the lair
        /// view's HUD chip). Returns false if the dash is on
        /// cooldown or the hero is down.</summary>
        public bool TryDash()
        {
            if (!_active || !IsAlive || _dashCooldownTimer > 0f) return false;
            _dashTimer = DashDuration;
            _dashCooldownTimer = DashCooldown;
            _dashTravelTimer = DashTravelTime;
            // Hop sideways around the boss — out of slams and beams
            // alike, while staying in the hero's range band.
            _dashBearing = _bearing + DashArcDegrees * Mathf.Deg2Rad;
            return true;
        }

        public void TakeDamage(float damage)
        {
            if (!IsAlive || IsDodging) return;
            float dmg = Mathf.Max(5f, damage - _defenseScaled * 0.5f);
            ApplyDamage(dmg);
        }

        public void TakeDot(float damage)
        {
            if (!IsAlive || IsDodging) return;
            // DoT ticks arrive 4x/second; DEF mitigation is spread
            // across the ticks so high-DEF heroes blunt venting
            // without the per-hit min-5 floor inflating small ticks.
            float dmg = Mathf.Max(1f, damage - _defenseScaled * 0.125f);
            ApplyDamage(dmg);
        }

        private void ApplyDamage(float dmg)
        {
            _health -= dmg;
            _flashTimer = 0.12f;
            if (_health <= 0f)
            {
                _health = 0f;
                _fallTimer = 0f; // the fall plays out in Update
            }
        }

        public void Knockback(Vector3 from, float force)
        {
            if (!IsAlive) return;
            Vector3 dir = transform.position - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            _knockbackVel = dir.normalized * force;
        }

        private void Update()
        {
            if (!_active) return;
            float dt = Time.deltaTime;
            if (_dashTimer > 0f) _dashTimer -= dt;
            if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;

            // Hit flash: tint the figure's materials toward white,
            // then restore. (Figure materials are unique instances —
            // BossLairView.SetColor creates one per renderer.)
            if (_flashTimer > 0f)
            {
                _flashTimer -= dt;
                float f = _flashTimer > 0f ? 0.6f : 0f;
                for (int i = 0; i < _renderers.Count; i++)
                {
                    if (_renderers[i] == null) continue;
                    _renderers[i].material.color = Color.Lerp(_baseColors[i], Color.white, f);
                }
            }

            if (!IsAlive)
            {
                // Fall: tip backward and settle, then rest.
                if (_fallTimer >= 0f && _fallTimer < 0.6f)
                {
                    _fallTimer += dt;
                    float p = Mathf.Clamp01(_fallTimer / 0.6f);
                    transform.localRotation = Quaternion.Euler(-80f * p, 0f, 0f);
                    Vector3 pos = transform.position;
                    pos.y = Mathf.Lerp(pos.y, 0.1f, p * 0.3f);
                    transform.position = pos;
                }
                return;
            }

            if (_bossRoot == null) return;
            // During the dash hop the bearing eases to the dash
            // bearing; afterwards the dash bearing becomes home.
            if (_dashTravelTimer > 0f)
            {
                _dashTravelTimer -= dt;
                float step = (DashArcDegrees * Mathf.Deg2Rad) * (dt / DashTravelTime);
                _bearing = Mathf.MoveTowards(_bearing, _dashBearing, step);
                if (_dashTravelTimer <= 0f) _bearing = _dashBearing;
            }
            Vector3 bossPos = _bossRoot.position;
            Vector3 slot = bossPos + new Vector3(Mathf.Sin(_bearing), 0f, Mathf.Cos(_bearing)) * _range;
            slot.y = 0f;
            Vector3 toSlot = slot - transform.position;
            toSlot.y = 0f;
            float speed = _dashTravelTimer > 0f ? MoveSpeed * 3.5f : MoveSpeed;
            Vector3 move = toSlot.normalized * Mathf.Min(toSlot.magnitude, speed * dt);
            if (toSlot.magnitude > 0.05f) transform.position += move;
            // Knockback shove, damped.
            if (_knockbackVel.sqrMagnitude > 0.01f)
            {
                transform.position += _knockbackVel * dt;
                _knockbackVel = Vector3.Lerp(_knockbackVel, Vector3.zero, dt * 6f);
            }
            // Stay grounded and face the boss. (Slots are in range
            // by construction, so no arena clamp is needed; only
            // knockback can push a hero out, and the slot glide
            // pulls it back.)
            transform.position = new Vector3(transform.position.x, 0f, transform.position.z);
            Vector3 look = bossPos - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }
    }
}
