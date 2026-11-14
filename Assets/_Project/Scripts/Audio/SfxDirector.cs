using System.Collections.Generic;
using UnityEngine;

namespace LunarDominion.Audio
{
    /// <summary>Sound effects (Cinematic Boss Fight Spec, Phase
    /// C5, 2026-11-13) — the game's first SFX, synthesized locally
    /// like the music, so they are owned outright. Clips load from
    /// Resources/Audio/Sfx and play through a small pool of
    /// AudioSources so impacts can overlap. A clip whose .ogg file
    /// has not been installed yet is skipped silently (one console
    /// warning) — the same rule as MusicDirector, so this patch is
    /// safe to pull before the files are in place. Playback is
    /// pure 2D and ignores Time.timeScale, so hit-stop never
    /// swallows a sound.</summary>
    public enum SfxClip
    {
        HeroHit,
        HeroShoot,
        SkillHit,
        Crit,
        Buff,
        BossHit,
        Sweep,
        SlamCharge,
        SlamHit,
        DroneLaunch,
        DroneShoot,
        DroneDown,
        Vent,
        Phase2,
        HeroFall,
        Victory,
        Defeat,
    }

    public class SfxDirector : MonoBehaviour
    {
        public static SfxDirector Instance { get; private set; }

        private const float MasterVolume = 0.9f;
        private const int PoolSize = 8;

        private AudioSource[] _pool;
        private int _next;
        private readonly Dictionary<SfxClip, AudioClip> _clips =
            new Dictionary<SfxClip, AudioClip>();
        private bool _warnedMissing;

        private void Awake()
        {
            Instance = this;
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.loop = false;
                src.playOnAwake = false;
                src.spatialBlend = 0f;   // pure 2D, like the music
                _pool[i] = src;
            }
            foreach (SfxClip clip in System.Enum.GetValues(typeof(SfxClip)))
                _clips[clip] = LoadClip(clip);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private AudioClip LoadClip(SfxClip clip)
        {
            var loaded = Resources.Load<AudioClip>("Audio/Sfx/Sfx_" + clip);
            if (loaded == null && !_warnedMissing)
            {
                _warnedMissing = true;
                Debug.LogWarning("[SfxDirector] No SFX clips found under "
                    + "Resources/Audio/Sfx — sound effects stay off until "
                    + "the .ogg files are installed there.");
            }
            return loaded;
        }

        /// <summary>Play a clip if the director exists and the
        /// file is installed; safe to call from anywhere.</summary>
        public static void Play(SfxClip clip)
        {
            if (Instance != null) Instance.PlayInternal(clip);
        }

        private void PlayInternal(SfxClip clip)
        {
            AudioClip audio;
            if (!_clips.TryGetValue(clip, out audio) || audio == null) return;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            src.clip = audio;
            src.volume = MasterVolume * GainFor(clip);
            src.pitch = 1f;
            src.Play();
        }

        private static float GainFor(SfxClip clip)
        {
            switch (clip)
            {
                case SfxClip.SlamHit: return 1.0f;
                case SfxClip.BossHit: return 0.95f;
                case SfxClip.Crit: return 0.9f;
                case SfxClip.HeroFall: return 0.9f;
                case SfxClip.Victory: return 0.85f;
                case SfxClip.Defeat: return 0.85f;
                case SfxClip.SkillHit: return 0.85f;
                case SfxClip.Phase2: return 0.85f;
                case SfxClip.Sweep: return 0.8f;
                case SfxClip.DroneLaunch: return 0.75f;
                case SfxClip.SlamCharge: return 0.7f;
                case SfxClip.Vent: return 0.7f;
                case SfxClip.Buff: return 0.6f;
                default: return 0.8f;
            }
        }
    }
}
