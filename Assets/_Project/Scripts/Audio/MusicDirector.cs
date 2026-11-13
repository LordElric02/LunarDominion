using UnityEngine;

namespace LunarDominion.Audio
{
    /// <summary>Background music for the three game areas (2026-11-13).
    /// Loads the starter loops from Resources/Audio/Music and crossfades
    /// between them as the player moves: Base theme on the Base, Heroes,
    /// Army and Research tabs; Exterior theme on the Map; Lair theme
    /// inside the robot lair (which also covers its boss fights).
    ///
    /// Two AudioSources ping-pong: Play() fades the outgoing source down
    /// while the incoming one fades up, both in Update. A track whose
    /// .ogg file has not been installed yet is skipped with one console
    /// warning — the game simply stays silent for that area, so this
    /// patch is safe to pull before the files are in place.</summary>
    public enum MusicTrack
    {
        None,
        Base,
        Exterior,
        Lair,
    }

    public class MusicDirector : MonoBehaviour
    {
        private const float FadeSeconds = 1.4f;
        private const float MasterVolume = 0.85f;

        private AudioSource _a;
        private AudioSource _b;
        private AudioSource _front;   // the source fading in / playing
        private AudioSource _back;    // the source fading out
        private float _frontTarget;
        private float _backTarget;

        private AudioClip _baseClip;
        private AudioClip _exteriorClip;
        private AudioClip _lairClip;
        private MusicTrack _current = MusicTrack.None;
        private bool _warnedMissing;

        private void Awake()
        {
            _a = CreateSource();
            _b = CreateSource();
            _front = _a;
            _back = _b;
            _baseClip = LoadClip("Music_Base");
            _exteriorClip = LoadClip("Music_Exterior");
            _lairClip = LoadClip("Music_Lair");
        }

        private void Start()
        {
            // The whole UI is runtime-built and the scene camera is not
            // guaranteed to carry a listener — without one, no audio is
            // heard at all. Add it to the main camera if it is missing.
            var cam = Camera.main;
            if (cam != null && cam.GetComponent<AudioListener>() == null)
                cam.gameObject.AddComponent<AudioListener>();
        }

        private AudioSource CreateSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // pure 2D music, no world position
            src.volume = 0f;
            return src;
        }

        private AudioClip LoadClip(string clipName)
        {
            var clip = Resources.Load<AudioClip>("Audio/Music/" + clipName);
            if (clip == null && !_warnedMissing)
            {
                _warnedMissing = true;
                Debug.LogWarning("[MusicDirector] No music clips found under "
                    + "Resources/Audio/Music — music stays off until the "
                    + ".ogg files are installed there.");
            }
            return clip;
        }

        public void PlayBase() => Play(MusicTrack.Base);
        public void PlayExterior() => Play(MusicTrack.Exterior);
        public void PlayLair() => Play(MusicTrack.Lair);

        public void Play(MusicTrack track)
        {
            if (track == _current) return;
            _current = track;

            var clip = ClipFor(track);
            var gain = GainFor(track);

            // Swap the sources: the old front becomes the fade-out back.
            var old = _front;
            _front = _back;
            _back = old;
            _backTarget = 0f;

            if (clip == null)
            {
                _frontTarget = 0f;
                return;
            }
            if (_front.clip != clip)
            {
                _front.clip = clip;
                _front.volume = 0f;
            }
            if (!_front.isPlaying) _front.Play();
            _frontTarget = gain;
        }

        private AudioClip ClipFor(MusicTrack track)
        {
            switch (track)
            {
                case MusicTrack.Base: return _baseClip;
                case MusicTrack.Exterior: return _exteriorClip;
                case MusicTrack.Lair: return _lairClip;
                default: return null;
            }
        }

        private float GainFor(MusicTrack track)
        {
            // The lair theme was mastered louder than the other two, so
            // it gets a trim to sit at a comparable level in game.
            return MasterVolume * (track == MusicTrack.Lair ? 0.7f : 0.9f);
        }

        private void Update()
        {
            Ramp(_front, _frontTarget);
            Ramp(_back, _backTarget);
        }

        private void Ramp(AudioSource src, float target)
        {
            if (src == null) return;
            if (!src.isPlaying) return;
            float step = (MasterVolume / FadeSeconds) * Time.deltaTime;
            src.volume = Mathf.MoveTowards(src.volume, target, step);
            if (target <= 0f && src.volume <= 0.001f) src.Stop();
        }
    }
}
