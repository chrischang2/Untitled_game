using System.Collections;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.GenAI;
using UntitledGame.Language;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Voice input: hold V to talk to whoever you're talking with (the shopkeeper you started a conversation
    /// with using E, otherwise Mei), hold B to always talk to Mei - during a shop conversation that's a quiet
    /// side chat about it. Audio -> SenseVoice (pinned to Mandarin, in-process) -> text -> character.
    /// Optional hands-free mode uses simple voice activity detection instead of a key.
    /// </summary>
    public class VoiceChatController : MonoBehaviour
    {
        [SerializeField] private CompanionBrain mei;
        [SerializeField] private MicRecorder mic;
        [SerializeField] private Transform player;
        [SerializeField] private KeyCode talkKey = KeyCode.V;
        [SerializeField] private KeyCode meiKey = KeyCode.B;
        [SerializeField] private float maxRecordSeconds = 25f;

        [Header("Hands-free (voice activity detection)")]
        [SerializeField] private float vadMinThreshold = 0.018f;
        [SerializeField] private float vadNoiseMultiplier = 3.2f;
        [SerializeField] private float vadStartSeconds = 0.15f;
        [SerializeField] private float vadEndSilenceSeconds = 0.8f;
        [SerializeField] private float vadMaxSeconds = 15f;

        private float _noiseFloor = 0.005f;
        private float _aboveTime, _belowTime;
        private bool _vadRecording;
        private float _quietSince;
        private KeyCode _heldKey;
        private DialogueAgent _recordingFor;

        public bool HandsFree => SaveSystem.Settings.handsFree;
        public MicRecorder Mic => mic;
        public bool IsTranscribing { get; private set; }
        public float LastTranscriptionSeconds { get; private set; } = -1f;
        public string LastTranscript { get; private set; } = "";
        public CompanionBrain Mei => mei;

        /// <summary>Who V (or hands-free speech) is addressed to right now: the shopkeeper you're in a conversation with (E), else Mei.</summary>
        public DialogueAgent CurrentTarget => ShopConversation.Active != null ? ShopConversation.Active : mei;

        /// <summary>Who the in-progress recording will be sent to.</summary>
        public DialogueAgent RecordingTarget => _recordingFor;

        public void Configure(CompanionBrain b, MicRecorder m, Transform p)
        {
            mei = b;
            mic = m;
            player = p;
        }

        private void Update()
        {
            if (mic == null || mei == null) return;

            mic.KeepOpen = HandsFree;

            // Holding a talk key (V, or B for Mei - B works in hands-free mode too).
            if (_pushToTalk)
            {
                if (!mic.IsRecording || mic.IsFinishing)
                {
                    _pushToTalk = false;
                }
                else
                {
                    bool released = !Input.GetKey(_heldKey) || InputGate.GameplayBlocked;
                    if (released || mic.RecordingSeconds > maxRecordSeconds) StopAndSend();
                    return;
                }
            }

            if (!InputGate.GameplayBlocked && Input.GetKeyDown(meiKey))
            {
                if (_vadRecording)
                {
                    // Hands-free had started listening for someone else: B takes over for Mei.
                    _vadRecording = false;
                    mic.CancelRecording();
                    _recordingFor?.CancelListening();
                }
                if (!mic.IsRecording) TryBegin(meiKey, mei);
                return;
            }

            if (HandsFree)
            {
                UpdateHandsFree();
                return;
            }

            if (InputGate.GameplayBlocked || mic.IsRecording) return;
            if (Input.GetKeyDown(talkKey)) TryBegin(talkKey, CurrentTarget);
        }

        private bool _pushToTalk;

        private bool SpeechReady(bool toast)
        {
            if (!mic.HasMicrophone)
            {
                if (toast) GameEvents.Toast("No microphone found. Press T to type instead.", 3.5f);
                return false;
            }
            var s = LocalAIServices.Instance;
            if (s == null || s.SttStatus != ServiceStatus.Ready)
            {
                if (toast)
                    GameEvents.Toast(s != null && s.SttStatus == ServiceStatus.Starting
                        ? "Speech recognition is still loading... one sec!"
                        : "Speech recognition isn't installed yet. Press T to type instead.", 3.5f);
                return false;
            }
            return true;
        }

        /// <summary>Mei stops mid-remark when the player turns to talk to someone else.</summary>
        private void HushMei(DialogueAgent target)
        {
            if (mei != null && target != mei && mei.IsBusy) mei.Interrupt();
        }

        private void TryBegin(KeyCode key, DialogueAgent target)
        {
            if (IsTranscribing || target == null || !SpeechReady(true)) return;
            if (!mic.Begin()) return;
            _pushToTalk = true;
            _heldKey = key;
            _recordingFor = target;
            HushMei(target);
            target.BeginListening();
            AudioManager.Instance?.PlaySfx("SFX/ui_pluck_001", 0.35f, 0f);
        }

        private void UpdateHandsFree()
        {
            if (!SpeechReady(false) || InputGate.GameplayBlocked)
            {
                if (_vadRecording)
                {
                    _vadRecording = false;
                    mic.CancelRecording();
                    _recordingFor?.CancelListening();
                }
                return;
            }
            if (!mic.IsOpen) mic.Open();

            // Don't listen to the characters through the speakers: pause while anyone talks (+ a short tail).
            bool someoneTalking = CharacterVoice.AnySpeaking || mei.State == CompanionState.Thinking;
            if (someoneTalking) _quietSince = Time.unscaledTime;
            bool deaf = someoneTalking || Time.unscaledTime - _quietSince < 0.6f || IsTranscribing || mic.IsFinishing;

            float rms = mic.RawRms;
            float threshold = Mathf.Max(vadMinThreshold, _noiseFloor * vadNoiseMultiplier);
            bool loud = rms > threshold;
            if (!_vadRecording && !loud) _noiseFloor = Mathf.Lerp(_noiseFloor, rms, Time.unscaledDeltaTime * 0.5f);

            if (deaf)
            {
                _aboveTime = 0f;
                if (_vadRecording)
                {
                    _vadRecording = false;
                    mic.CancelRecording();
                }
                return;
            }

            if (!_vadRecording)
            {
                _aboveTime = loud ? _aboveTime + Time.unscaledDeltaTime : 0f;
                if (_aboveTime >= vadStartSeconds && mic.Begin(0.5f, 0f))
                {
                    _vadRecording = true;
                    _belowTime = 0f;
                    _recordingFor = CurrentTarget;
                    HushMei(_recordingFor);
                    _recordingFor.BeginListening();
                }
                return;
            }

            _belowTime = loud ? 0f : _belowTime + Time.unscaledDeltaTime;
            if (_belowTime >= vadEndSilenceSeconds || mic.RecordingSeconds > vadMaxSeconds)
            {
                _vadRecording = false;
                _aboveTime = 0f;
                var target = _recordingFor;
                target.BeginTranscribing();
                mic.End(samples => StartCoroutine(Process(samples, target)));
            }
        }

        private void StopAndSend()
        {
            _pushToTalk = false;
            var target = _recordingFor ?? mei;
            target.BeginTranscribing();
            AudioManager.Instance?.PlaySfx("SFX/ui_pluck_002", 0.3f, 0f);
            mic.End(samples => StartCoroutine(Process(samples, target)));
        }

        /// <summary>Transcribe 16 kHz mono samples and hand the text to a character (also used by the self-test).</summary>
        public IEnumerator Process(float[] samples, DialogueAgent target)
        {
            target ??= mei;
            float seconds = samples.Length / (float)MicRecorder.TargetRate;
            float peak = MicRecorder.PeakRms(samples);
            string who = "MIC→" + target.MemoryKey;
            string wav = ChatAudit.SaveAudio(samples, MicRecorder.TargetRate, "to-" + target.MemoryKey);
            string clip = $"{seconds:0.0} s audio, peak level {peak:0.000}" + (wav != null ? $", {wav}" : "");
            if (seconds < 0.35f || peak < 0.01f)
            {
                ChatAudit.Write(who, "ignored: too short or too quiet (" + clip + ")");
                target.CancelListening();
                _recordingFor = null;
                GameEvents.Toast(HandsFree ? "I didn't catch that." : "I didn't catch that. Hold V while you speak.");
                yield break;
            }

            IsTranscribing = true;
            string text = null, error = null;
            bool done = false;
            float t0 = Time.realtimeSinceStartup;
            var speech = LocalAIServices.Instance.Speech;
            speech.Transcribe(samples, (t, e) =>
            {
                text = t;
                error = e;
                done = true;
            }, (SpeechEngine.AsrMode)SaveSystem.Settings.asrMode);
            while (!done) yield return null;
            LastTranscriptionSeconds = Time.realtimeSinceStartup - t0;
            IsTranscribing = false;
            _recordingFor = null;

            if (error != null)
            {
                ChatAudit.Write(who, $"recognition ERROR {error} ({clip})");
                target.CancelListening();
                GameEvents.Toast("Couldn't hear you properly: " + error, 3.5f);
                yield break;
            }
            string rawText = text;
            text = Normalize(text);
            LastTranscript = text ?? "";
            ChatAudit.Write(who, $"heard \"{text}\" ({speech.LastEngine}, recognised in {ChatAudit.Seconds(LastTranscriptionSeconds)}; {clip})",
                rawText != text ? $"recogniser raw: \"{rawText}\"" : null);
            if (string.IsNullOrWhiteSpace(text) || SpeechText.LooksLikeHallucination(text))
            {
                ChatAudit.Write(who, "dropped: no words / looks like a recognition hallucination");
                target.CancelListening();
                GameEvents.Toast("Hmm, I didn't catch any words. Try again?");
                yield break;
            }
            target.HandlePlayerUtterance(text);
        }

        /// <summary>Simplified characters; SenseVoice's SHOUTED English turned into normal sentence case.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = Pinyin.ToSimplified(text.Trim());
            bool hasLower = false, hasUpper = false;
            foreach (char c in text)
            {
                if (char.IsLower(c)) hasLower = true;
                if (c >= 'A' && c <= 'Z') hasUpper = true;
            }
            if (hasUpper && !hasLower)
            {
                text = text.ToLowerInvariant();
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\bi\b", "I");
                int first = text.IndexOfAny("abcdefghijklmnopqrstuvwxyz".ToCharArray());
                if (first >= 0) text = text.Substring(0, first) + char.ToUpperInvariant(text[first]) + text.Substring(first + 1);
            }
            return text;
        }
    }
}
