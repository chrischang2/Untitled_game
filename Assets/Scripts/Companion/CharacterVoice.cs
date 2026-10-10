using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.GenAI;

namespace UntitledGame.Companion
{
    /// <summary>
    /// The voices Mei can have (Esc → "Mei sounds like"). The natural ones are Kokoro v1.1-zh speakers that
    /// read both her Mandarin and her English; "classic" is the original fast Matcha (Mandarin) + Piper (English) pair.
    /// Samples of each are in Captures/mei-voice-samples/mei_voice_N.wav.
    /// </summary>
    public static class MeiVoices
    {
        public const int Classic = -1;

        private static readonly (int speaker, string pitch)[] Natural =
        {
            (3, "high"), (47, "high"), (55, "high"), (40, "high"),
            (22, "medium"), (30, "medium"), (15, "medium"), (8, "low"),
        };

        public static string Label(int speaker)
        {
            if (speaker == Classic) return "Classic (fastest)";
            foreach (var (s, pitch) in Natural)
                if (s == speaker) return $"Natural #{s} · {pitch}";
            return $"Natural #{speaker}";
        }

        public static int Next(int speaker)
        {
            if (speaker == Classic) return Natural[0].speaker;
            for (int i = 0; i < Natural.Length; i++)
                if (Natural[i].speaker == speaker) return i + 1 < Natural.Length ? Natural[i + 1].speaker : Classic;
            return Natural[0].speaker;
        }

        /// <summary>Mei says a line so the player can hear the voice they just picked.</summary>
        public static void Preview()
        {
            var mei = CompanionBrain.Current;
            if (mei == null || mei.Voice == null || mei.IsBusy) return;
            mei.Voice.StopAll();
            mei.Voice.Say("你好！我是美。 How does this voice sound?");
        }
    }

    /// <summary>
    /// Speaks a character's lines in order as they stream in. Each sentence is split by script:
    /// Chinese runs go to the character's Mandarin voice, English runs (Mei's explanations and the
    /// [meaning] glosses) to the English voice - shopkeepers have no English voice and simply don't
    /// speak it. Mei's natural voice reads both. Each sentence is synthesised in short chunks and starts
    /// playing as soon as its first chunk is ready, so a slower (more natural) voice doesn't add much delay.
    /// Falls back to timed subtitles when speech isn't available.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CharacterVoice : MonoBehaviour
    {
        [SerializeField] private string chineseVoice = SpeechEngine.VoiceMei;
        [SerializeField] private string englishVoice = SpeechEngine.VoiceEnglish;
        [SerializeField] private float pitch = 1f;

        private const float PartGap = 0.06f;
        private const float PartTimeout = 10f;

        private class Part
        {
            public AudioClip clip;
            public bool done;
        }

        private class Utterance
        {
            public string display;
            public Part[] parts;   // null: subtitles only
            public int next;       // next part to play
            public float duration; // subtitles only
            public float created;
            public bool Started => parts == null || (parts.Length > 0 && parts[0].done);
        }

        private AudioSource _source;
        private readonly Queue<Utterance> _queue = new Queue<Utterance>();
        private Utterance _current;
        private float _currentEnd;   // subtitles: when the line ends; audio: when the playing part ends
        private float _waitingSince; // audio: when we started waiting for the next part
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

        [SerializeField] private bool localVoice;

        /// <summary>A shopkeeper's voice: fully 3D and short-ranged, so it stays at their stall (Mei's follows you).</summary>
        public void MakeLocal() => localVoice = true;
        public float SpatialBlend => _source != null ? _source.spatialBlend : 0f;
        public float MaxHearingDistance => _source != null ? _source.maxDistance : 0f;

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
            _source.spatialBlend = localVoice ? 1f : 0.55f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = localVoice ? 2.5f : 6f;
            _source.maxDistance = localVoice ? 14f : 60f;
            _source.dopplerLevel = 0f;
        }

        /// <summary>Which voices read this character's Chinese and English right now (Mei's follow the settings).</summary>
        private (string zh, string en, bool natural) ResolveVoices(SpeechEngine speech)
        {
            int speaker = SaveSystem.Settings.meiVoice;
            if (chineseVoice == SpeechEngine.VoiceMei && speaker != MeiVoices.Classic && speech.HasVoice(SpeechEngine.VoiceNatural))
            {
                string v = SpeechEngine.Speaker(SpeechEngine.VoiceNatural, speaker);
                return (v, v, true);
            }
            return (chineseVoice, englishVoice, false);
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
                u.duration = ReadTime(display);
                return;
            }

            // Glosses "鱼竿 [fishing rod]" are read aloud as "鱼竿, fishing rod".
            string spoken = SpeechText.CleanForSpeech(display, dropParentheses: true).Replace('[', ',').Replace(']', ',').Replace('【', ',').Replace('】', ',');
            var (zhVoice, enVoice, natural) = ResolveVoices(speech);
            var runs = new List<(string text, string voice)>();
            foreach (var (text, chinese) in SpeechText.SplitByScript(spoken))
            {
                string voice = chinese ? zhVoice : enVoice;
                if (string.IsNullOrEmpty(voice) || !speech.HasVoice(voice)) continue;
                // The natural voice is slower: smaller pieces start playing sooner.
                if (natural) foreach (var chunk in SplitClauses(text)) runs.Add((chunk, voice));
                else runs.Add((text, voice));
            }
            if (runs.Count == 0)
            {
                u.duration = ReadTime(display);
                return;
            }

            var ticket = _ticket;
            float speed = Mathf.Clamp(SaveSystem.Settings.voiceSpeed, 0.6f, 1.4f);
            u.parts = new Part[runs.Count];
            for (int i = 0; i < runs.Count; i++)
            {
                var part = u.parts[i] = new Part();
                string name = display;
                speech.Synthesize(runs[i].voice, runs[i].text, speed, ticket, pcm =>
                {
                    if (ticket.Cancelled) return;
                    if (pcm.HasValue && pcm.Value.samples != null && pcm.Value.samples.Length > 0)
                        part.clip = WavUtility.ToClip(pcm.Value, "voice_" + name.GetHashCode());
                    part.done = true;
                });
            }
        }

        /// <summary>
        /// Splits a run at clause punctuation ("你好！今天天气真好，" → "你好！" + "今天天气真好，"), keeping
        /// pieces long enough to sound natural.
        /// </summary>
        public static List<string> SplitClauses(string text, int minChars = 6)
        {
            var pieces = new List<string>();
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                sb.Append(c);
                bool cjkStop = "，。！？；：、".IndexOf(c) >= 0;
                bool latinStop = ",.!?;:".IndexOf(c) >= 0 && (i + 1 >= text.Length || text[i + 1] == ' ');
                if ((cjkStop || latinStop) && sb.ToString().Trim().Length >= minChars)
                {
                    pieces.Add(sb.ToString().Trim());
                    sb.Clear();
                }
            }
            string rest = sb.ToString().Trim();
            if (rest.Length > 0)
            {
                // A tiny tail ("吧。") sounds odd on its own: glue it to the previous piece.
                if (pieces.Count > 0 && rest.Trim(',', '，', '.', '。', ' ').Length < 3) pieces[pieces.Count - 1] += rest;
                else pieces.Add(rest);
            }
            return pieces;
        }

        public void StopAll()
        {
            _ticket.Cancelled = true;
            _ticket = new SpeechTicket();
            if (_current?.parts != null) DestroyClips(_current);
            foreach (var u in _queue) DestroyClips(u);
            _queue.Clear();
            _current = null;
            if (_source != null) _source.Stop();
        }

        private static void DestroyClips(Utterance u)
        {
            if (u.parts == null) return;
            foreach (var p in u.parts)
            {
                if (p.clip != null) Destroy(p.clip);
                p.clip = null;
            }
        }

        private static float ReadTime(string text)
        {
            int units = text.Split(' ').Length + (SpeechText.ContainsCjk(text) ? text.Length / 3 : 0);
            return Mathf.Clamp(0.6f + units * 0.3f, 1.2f, 9f);
        }

        private void Update()
        {
            _source.volume = SaveSystem.Settings.voiceVolume;
            _source.pitch = pitch;

            if (_current == null && _queue.Count > 0)
            {
                var head = _queue.Peek();
                if (!head.Started && Time.time - head.created > 12f)
                {
                    // Speech never arrived: show it as a subtitle instead.
                    DestroyClips(head);
                    head.parts = null;
                    head.duration = ReadTime(head.display);
                }
                if (head.Started)
                {
                    _current = _queue.Dequeue();
                    _currentEnd = _current.parts == null ? Time.time + _current.duration : 0f;
                    _waitingSince = Time.time;
                    SentenceStarted?.Invoke(_current.display);
                }
            }

            if (_current != null) AdvanceCurrent();

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

        private void AdvanceCurrent()
        {
            var u = _current;
            if (Time.time < _currentEnd) return;

            if (u.parts != null)
            {
                // Play the next chunk once it's synthesised (skip it if it failed or takes far too long).
                while (u.next < u.parts.Length)
                {
                    var part = u.parts[u.next];
                    if (!part.done)
                    {
                        if (Time.time - _waitingSince < PartTimeout) return;
                        part.done = true;
                    }
                    u.next++;
                    if (u.next >= 2 && u.parts[u.next - 2].clip != null)
                    {
                        Destroy(u.parts[u.next - 2].clip);
                        u.parts[u.next - 2].clip = null;
                    }
                    if (part.clip == null) continue;
                    _source.clip = part.clip;
                    _source.Play();
                    bool last = u.next >= u.parts.Length;
                    _currentEnd = Time.time + part.clip.length / pitch + (last ? 0.15f : PartGap);
                    _waitingSince = _currentEnd;
                    return;
                }
                DestroyClips(u);
            }

            _current = null;
            if (_queue.Count == 0) AllFinished?.Invoke();
        }
    }
}
