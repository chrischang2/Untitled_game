using System;
using System.Collections.Generic;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.GenAI;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Speaks a character's lines in order as they stream in. Each sentence is split by script:
    /// Chinese runs go to the character's Mandarin voice, English runs (Mei's explanations and the
    /// [meaning] glosses) to the English voice - shopkeepers have no English voice and simply don't
    /// speak it. Falls back to timed subtitles when speech isn't available.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CharacterVoice : MonoBehaviour
    {
        [SerializeField] private string chineseVoice = SpeechEngine.VoiceMei;
        [SerializeField] private string englishVoice = SpeechEngine.VoiceEnglish;
        [SerializeField] private float pitch = 1f;

        private class Utterance
        {
            public string display;
            public AudioClip clip;
            public bool ready;
            public float duration;
            public float created;
        }

        private AudioSource _source;
        private readonly Queue<Utterance> _queue = new Queue<Utterance>();
        private Utterance _current;
        private float _currentEnd;
        private SpeechTicket _ticket = new SpeechTicket();
        private readonly float[] _ampBuf = new float[256];

        private static readonly List<CharacterVoice> All = new List<CharacterVoice>();

        /// <summary>True while any character is talking (music ducks).</summary>
        public static bool AnySpeaking => All.Exists(v => v.IsSpeaking);

        public event Action<string> SentenceStarted;
        public event Action AllFinished;

        public bool IsSpeaking => _current != null || _queue.Count > 0;
        public float Amplitude { get; private set; }
        public string CurrentSentence => _current?.display;

        public void Configure(string zhVoice, string enVoice, float voicePitch)
        {
            chineseVoice = zhVoice;
            englishVoice = enVoice;
            pitch = voicePitch;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0.55f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 6f;
            _source.maxDistance = 60f;
            _source.dopplerLevel = 0f;
        }

        public void Say(string sentence)
        {
            string display = SpeechText.CleanForDisplay(sentence);
            if (string.IsNullOrWhiteSpace(display)) return;
            var u = new Utterance { display = display, created = Time.time };
            _queue.Enqueue(u);

            var speech = LocalAIServices.Instance != null ? LocalAIServices.Instance.Speech : null;
            if (!SaveSystem.Settings.speakReplies || speech == null || !speech.TtsReady)
            {
                u.ready = true;
                u.duration = ReadTime(display);
                return;
            }

            // Glosses "鱼竿 [fishing rod]" are read aloud as "鱼竿, fishing rod".
            string spoken = SpeechText.CleanForSpeech(display, dropParentheses: true).Replace('[', ',').Replace(']', ',').Replace('【', ',').Replace('】', ',');
            var runs = new List<(string text, string voice)>();
            foreach (var (text, chinese) in SpeechText.SplitByScript(spoken))
            {
                string voice = chinese ? chineseVoice : englishVoice;
                if (!string.IsNullOrEmpty(voice) && speech.HasVoice(voice)) runs.Add((text, voice));
            }
            if (runs.Count == 0)
            {
                u.ready = true;
                u.duration = ReadTime(display);
                return;
            }

            var ticket = _ticket;
            var parts = new WavUtility.PcmData?[runs.Count];
            int remaining = runs.Count;
            float speed = Mathf.Clamp(SaveSystem.Settings.voiceSpeed, 0.6f, 1.4f);
            for (int i = 0; i < runs.Count; i++)
            {
                int idx = i;
                speech.Synthesize(runs[i].voice, runs[i].text, speed, ticket, pcm =>
                {
                    parts[idx] = pcm;
                    if (--remaining > 0 || ticket.Cancelled) return;
                    u.clip = Concat(parts, display);
                    u.duration = u.clip != null ? u.clip.length / pitch : ReadTime(display);
                    u.ready = true;
                });
            }
        }

        public void StopAll()
        {
            _ticket.Cancelled = true;
            _ticket = new SpeechTicket();
            _queue.Clear();
            _current = null;
            if (_source != null) _source.Stop();
        }

        private static float ReadTime(string text)
        {
            int units = text.Split(' ').Length + (SpeechText.ContainsCjk(text) ? text.Length / 3 : 0);
            return Mathf.Clamp(0.6f + units * 0.3f, 1.2f, 9f);
        }

        private static AudioClip Concat(WavUtility.PcmData?[] parts, string name)
        {
            int rate = 0;
            foreach (var p in parts) if (p.HasValue) rate = Mathf.Max(rate, p.Value.sampleRate);
            if (rate == 0) return null;
            var chunks = new List<float[]>();
            foreach (var p in parts)
            {
                if (!p.HasValue || p.Value.samples == null || p.Value.samples.Length == 0) continue;
                chunks.Add(WavUtility.Resample(p.Value.samples, p.Value.sampleRate, rate));
            }
            if (chunks.Count == 0) return null;
            int gap = Mathf.RoundToInt(rate * 0.1f);
            int total = 0;
            foreach (var c in chunks) total += c.Length + gap;
            var all = new float[total];
            int o = 0;
            foreach (var c in chunks)
            {
                Array.Copy(c, 0, all, o, c.Length);
                o += c.Length + gap;
            }
            return WavUtility.ToClip(new WavUtility.PcmData { samples = all, sampleRate = rate, channels = 1 }, "voice_" + name.GetHashCode());
        }

        private void Update()
        {
            _source.volume = SaveSystem.Settings.voiceVolume;
            _source.pitch = pitch;

            if (_current == null && _queue.Count > 0)
            {
                var head = _queue.Peek();
                if (!head.ready && Time.time - head.created > 12f)
                {
                    head.ready = true;
                    head.duration = ReadTime(head.display);
                }
                if (head.ready)
                {
                    _current = _queue.Dequeue();
                    if (_current.clip != null)
                    {
                        _source.clip = _current.clip;
                        _source.Play();
                        _currentEnd = Time.time + _current.duration + 0.15f;
                    }
                    else _currentEnd = Time.time + _current.duration;
                    SentenceStarted?.Invoke(_current.display);
                }
            }

            if (_current != null && Time.time >= _currentEnd)
            {
                if (_current.clip != null) Destroy(_current.clip);
                _current = null;
                if (_queue.Count == 0) AllFinished?.Invoke();
            }

            float amp = 0f;
            if (_source.isPlaying)
            {
                _source.GetOutputData(_ampBuf, 0);
                float sum = 0f;
                foreach (float s in _ampBuf) sum += s * s;
                amp = Mathf.Clamp01(Mathf.Sqrt(sum / _ampBuf.Length) * 6f);
            }
            else if (_current != null) amp = 0.35f + 0.25f * Mathf.Sin(Time.time * 11f);
            Amplitude = Mathf.Lerp(Amplitude, amp, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
    }
}
