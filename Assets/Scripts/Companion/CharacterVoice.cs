using System;
using System.Collections.Generic;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.GenAI;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Speaks sentences in order as they stream in: each is synthesised by Piper in the background,
    /// then played back-to-back. Falls back to timed subtitles when TTS is off or unavailable.
    /// Mixed English/Mandarin sentences are split by script and voiced by the matching Piper voice.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CompanionVoice : MonoBehaviour
    {
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
        private int _generation;
        private readonly float[] _ampBuf = new float[256];

        public event Action<string> SentenceStarted;
        public event Action AllFinished;

        public bool IsSpeaking => _current != null || _queue.Count > 0;
        public float Amplitude { get; private set; }
        public string CurrentSentence => _current?.display;

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

            var services = LocalAIServices.Instance;
            if (!SaveSystem.Settings.speakReplies || services == null || services.TtsStatus != ServiceStatus.Ready)
            {
                u.ready = true;
                u.duration = ReadTime(display);
                return;
            }

            bool mandarin = SaveSystem.Settings.language == LanguageMode.MandarinPractice || SpeechText.ContainsCjk(display);
            string spoken = SpeechText.CleanForSpeech(display, dropParentheses: mandarin);
            var runs = mandarin ? SpeechText.SplitByScript(spoken) : new List<(string, bool)> { (spoken, false) };
            if (runs.Count == 0)
            {
                u.ready = true;
                u.duration = ReadTime(display);
                return;
            }

            int gen = _generation;
            var parts = new WavUtility.PcmData?[runs.Count];
            int remaining = runs.Count;
            bool finished = false;

            void Finish()
            {
                if (finished || gen != _generation) return;
                finished = true;
                u.clip = Concat(parts, display);
                u.duration = u.clip != null ? u.clip.length : ReadTime(display);
                u.ready = true;
            }

            for (int i = 0; i < runs.Count; i++)
            {
                int idx = i;
                var tts = services.GetTts(runs[i].Item2);
                if (tts == null)
                {
                    remaining--;
                    continue;
                }
                tts.Synthesize(runs[i].Item1, pcm =>
                {
                    parts[idx] = pcm;
                    if (--remaining == 0) Finish();
                });
            }
            if (remaining == 0) Finish();
        }

        public void StopAll()
        {
            _generation++;
            _queue.Clear();
            _current = null;
            if (_source != null) _source.Stop();
        }

        private static float ReadTime(string text)
        {
            int words = text.Split(' ').Length + (SpeechText.ContainsCjk(text) ? text.Length / 3 : 0);
            return Mathf.Clamp(0.6f + words * 0.3f, 1.2f, 9f);
        }

        private static AudioClip Concat(WavUtility.PcmData?[] parts, string name)
        {
            int rate = 0;
            var chunks = new List<float[]>();
            foreach (var p in parts)
            {
                if (!p.HasValue || p.Value.samples == null || p.Value.samples.Length == 0) continue;
                var data = p.Value;
                if (rate == 0) rate = data.sampleRate;
                var mono = data.samples;
                if (data.channels > 1)
                {
                    mono = new float[data.samples.Length / data.channels];
                    for (int i = 0; i < mono.Length; i++) mono[i] = data.samples[i * data.channels];
                }
                chunks.Add(WavUtility.Resample(mono, data.sampleRate, rate));
            }
            if (chunks.Count == 0) return null;
            int gap = Mathf.RoundToInt(rate * 0.08f);
            int total = 0;
            foreach (var c in chunks) total += c.Length + gap;
            var all = new float[total];
            int o = 0;
            foreach (var c in chunks)
            {
                Array.Copy(c, 0, all, o, c.Length);
                o += c.Length + gap;
            }
            return WavUtility.ToClip(new WavUtility.PcmData { samples = all, sampleRate = rate, channels = 1 }, "mei_" + name.GetHashCode());
        }

        private void Update()
        {
            _source.volume = SaveSystem.Settings.voiceVolume;

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
                        _currentEnd = Time.time + _current.clip.length + 0.12f;
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
            else if (_current != null) amp = 0.35f + 0.25f * Mathf.Sin(Time.time * 11f); // subtitle-only "talking"
            Amplitude = Mathf.Lerp(Amplitude, amp, 1f - Mathf.Exp(-18f * Time.deltaTime));

            if (AudioManager.Instance != null) AudioManager.Instance.VoiceActive = IsSpeaking;
        }
    }
}
