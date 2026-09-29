using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Environment;

namespace UntitledGame.Core
{
    /// <summary>
    /// Music playlist (with comfy silences between tracks), layered ambience that follows the time
    /// of day and weather, and pooled one-shot sound effects. Clips live under Resources/{Music,Ambience,SFX}.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [SerializeField] private float minSilence = 20f;
        [SerializeField] private float maxSilence = 60f;

        private AudioSource _music;
        private AudioSource _birds, _crickets, _forest, _water, _rain;
        private readonly List<AudioSource> _sfx2D = new List<AudioSource>();
        private readonly List<AudioSource> _sfx3D = new List<AudioSource>();
        private readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, AudioClip[]> _groups = new Dictionary<string, AudioClip[]>();
        private AudioClip[] _playlist;
        private int _trackIndex = -1;
        private float _nextTrackTime;
        private float _duck = 1f;

        public float RainAmount { get; set; }

        /// <summary>Set by the companion voice while speaking so music politely ducks.</summary>
        public bool VoiceActive { get; set; }

        private void Awake()
        {
            Instance = this;
            _music = MakeSource("Music", false);
            _birds = MakeLoop("Birds", "Ambience/birds");
            _crickets = MakeLoop("Crickets", "Ambience/crickets");
            _forest = MakeLoop("Forest", "Ambience/forest");
            _water = MakeLoop("Water", "Ambience/loop_water_02");
            _rain = MakeLoop("Rain", "Ambience/loop_rain");
            for (int i = 0; i < 10; i++) _sfx2D.Add(MakeSource("Sfx2D", false));
            for (int i = 0; i < 10; i++)
            {
                var s = MakeSource("Sfx3D", false);
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 2f;
                s.maxDistance = 45f;
                _sfx3D.Add(s);
            }

            _playlist = Resources.LoadAll<AudioClip>("Music").OrderBy(_ => Random.value).ToArray();
            _nextTrackTime = Time.time + 4f;
        }

        private AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        private AudioSource MakeLoop(string name, string clipPath)
        {
            var s = MakeSource(name, true);
            s.clip = Resources.Load<AudioClip>(clipPath);
            s.volume = 0f;
            if (s.clip != null)
            {
                s.time = Random.Range(0f, s.clip.length * 0.9f);
                s.Play();
            }
            return s;
        }

        private void Update()
        {
            var st = SaveSystem.Settings;
            float master = st.masterVolume;
            AudioListener.volume = master;

            // Music with ducking under the companion's voice.
            _duck = Mathf.MoveTowards(_duck, VoiceActive ? 0.35f : 1f, Time.unscaledDeltaTime * 1.5f);
            _music.volume = st.musicVolume * 0.55f * _duck;
            if (!_music.isPlaying && _playlist.Length > 0 && Time.time >= _nextTrackTime)
            {
                _trackIndex = (_trackIndex + 1) % _playlist.Length;
                _music.clip = _playlist[_trackIndex];
                _music.Play();
                _nextTrackTime = Time.time + _music.clip.length + Random.Range(minSilence, maxSilence);
            }

            // Ambience crossfades with time of day.
            float dark = DayNightCycle.Instance != null ? DayNightCycle.Instance.Darkness : 0f;
            float amb = st.ambienceVolume;
            float rainMute = 1f - RainAmount * 0.7f;
            _birds.volume = Smooth(_birds.volume, amb * 0.35f * (1f - dark) * rainMute);
            _crickets.volume = Smooth(_crickets.volume, amb * 0.3f * dark);
            _forest.volume = Smooth(_forest.volume, amb * 0.22f);
            _rain.volume = Smooth(_rain.volume, amb * 0.55f * RainAmount);

            // Water lapping louder near the shore.
            float waterTarget = 0f;
            var listener = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            float shoreDist = Mathf.Abs(WorldShape.ShoreDistance(listener.x, listener.z));
            waterTarget = amb * Mathf.Lerp(0.45f, 0.06f, Mathf.InverseLerp(2f, 22f, shoreDist));
            _water.volume = Smooth(_water.volume, waterTarget);
        }

        private static float Smooth(float current, float target) =>
            Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * 0.25f);

        public AudioClip Clip(string path)
        {
            if (_cache.TryGetValue(path, out var c)) return c;
            c = Resources.Load<AudioClip>(path);
            _cache[path] = c;
            if (c == null) Debug.LogWarning($"[Audio] Missing clip {path}");
            return c;
        }

        /// <summary>Random clip from a set sharing a prefix, e.g. "SFX/splash_0" -> splash_01..06.</summary>
        public AudioClip RandomClip(string folder, string prefix)
        {
            string key = folder + "/" + prefix;
            if (!_groups.TryGetValue(key, out var arr))
            {
                arr = Resources.LoadAll<AudioClip>(folder).Where(a => a.name.StartsWith(prefix)).ToArray();
                _groups[key] = arr;
            }
            return arr.Length == 0 ? null : arr[Random.Range(0, arr.Length)];
        }

        public void PlaySfx(string path, float volume = 1f, float pitchJitter = 0.05f) =>
            PlayClip(Clip(path), volume, pitchJitter);

        public void PlayClip(AudioClip clip, float volume = 1f, float pitchJitter = 0.05f)
        {
            if (clip == null) return;
            var s = _sfx2D.FirstOrDefault(x => !x.isPlaying) ?? _sfx2D[0];
            s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            s.PlayOneShot(clip, volume * SaveSystem.Settings.sfxVolume);
        }

        public void PlayAt(string path, Vector3 position, float volume = 1f, float pitchJitter = 0.08f) =>
            PlayClipAt(Clip(path), position, volume, pitchJitter);

        public void PlayClipAt(AudioClip clip, Vector3 position, float volume = 1f, float pitchJitter = 0.08f)
        {
            if (clip == null) return;
            var s = _sfx3D.FirstOrDefault(x => !x.isPlaying) ?? _sfx3D[0];
            s.transform.position = position;
            s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            s.PlayOneShot(clip, volume * SaveSystem.Settings.sfxVolume);
        }

        public void SkipTrack()
        {
            _music.Stop();
            _nextTrackTime = Time.time;
        }
    }
}
