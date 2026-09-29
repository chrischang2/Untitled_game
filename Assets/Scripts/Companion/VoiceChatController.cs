using System.Collections;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.GenAI;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Push-to-talk glue: hold V, speak, release. Audio -> whisper.cpp -> text -> Mei's brain.
    /// </summary>
    public class VoiceChatController : MonoBehaviour
    {
        [SerializeField] private CompanionBrain brain;
        [SerializeField] private MicRecorder mic;
        [SerializeField] private KeyCode pushToTalk = KeyCode.V;
        [SerializeField] private float maxRecordSeconds = 25f;
        [SerializeField] private string recognitionHint = "Mei, Mochi, Willow Lake, fishing, bobber, bait, dock, campfire, trout, carp, perch, catfish, koi, pike.";

        [Header("Hands-free (voice activity detection)")]
        [SerializeField] private float vadMinThreshold = 0.018f;
        [SerializeField] private float vadNoiseMultiplier = 3.2f;
        [SerializeField] private float vadStartSeconds = 0.15f;
        [SerializeField] private float vadEndSilenceSeconds = 0.8f;
        [SerializeField] private float vadMaxSeconds = 15f;

        private float _noiseFloor = 0.005f;
        private float _aboveTime, _belowTime;
        private bool _vadRecording;
        private float _meiQuietSince;

        public KeyCode PushToTalkKey => pushToTalk;
        public bool HandsFree => SaveSystem.Settings.handsFree;
        public MicRecorder Mic => mic;
        public bool IsTranscribing { get; private set; }
        public float LastTranscriptionSeconds { get; private set; } = -1f;

        public void Configure(CompanionBrain b, MicRecorder m)
        {
            brain = b;
            mic = m;
        }

        private void Update()
        {
            if (mic == null || brain == null) return;

            mic.KeepOpen = HandsFree;
            if (HandsFree)
            {
                UpdateHandsFree();
                return;
            }

            if (mic.IsRecording && !mic.IsFinishing)
            {
                bool released = !Input.GetKey(pushToTalk) || InputGate.GameplayBlocked;
                if (released || mic.RecordingSeconds > maxRecordSeconds) StopAndSend();
                return;
            }

            if (InputGate.GameplayBlocked) return;
            if (Input.GetKeyDown(pushToTalk)) TryBegin();
        }

        public void TryBegin()
        {
            if (IsTranscribing) return;
            if (!mic.HasMicrophone)
            {
                GameEvents.Toast("No microphone found. Press T to type to Mei instead.", 3.5f);
                return;
            }
            var s = LocalAIServices.Instance;
            if (s == null || s.SttStatus != ServiceStatus.Ready)
            {
                GameEvents.Toast(s != null && s.SttStatus == ServiceStatus.Starting
                    ? "Mei's ears are still waking up... one sec!"
                    : "Speech recognition isn't installed yet. Press T to type instead.", 3.5f);
                return;
            }
            if (!mic.Begin()) return;
            brain.BeginListening();
            AudioManager.Instance?.PlaySfx("SFX/ui_pluck_001", 0.35f, 0f);
        }

        private void UpdateHandsFree()
        {
            var s = LocalAIServices.Instance;
            bool sttReady = s != null && s.SttStatus == ServiceStatus.Ready;
            if (!mic.HasMicrophone || !sttReady || InputGate.GameplayBlocked)
            {
                if (_vadRecording)
                {
                    _vadRecording = false;
                    mic.CancelRecording();
                    brain.CancelListening();
                }
                return;
            }
            if (!mic.IsOpen) mic.Open();

            // Don't listen to Mei through the speakers: pause detection while she talks (+ a short tail).
            bool meiTalking = brain.State == CompanionState.Speaking || brain.State == CompanionState.Thinking;
            if (meiTalking) _meiQuietSince = Time.unscaledTime;
            bool deaf = meiTalking || Time.unscaledTime - _meiQuietSince < 0.6f || IsTranscribing || mic.IsFinishing;

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
                    brain.BeginListening();
                }
                return;
            }

            _belowTime = loud ? 0f : _belowTime + Time.unscaledDeltaTime;
            if (_belowTime >= vadEndSilenceSeconds || mic.RecordingSeconds > vadMaxSeconds)
            {
                _vadRecording = false;
                _aboveTime = 0f;
                brain.BeginTranscribing();
                mic.End(samples => StartCoroutine(Process(samples)));
            }
        }

        private void StopAndSend()
        {
            brain.BeginTranscribing();
            AudioManager.Instance?.PlaySfx("SFX/ui_pluck_002", 0.3f, 0f);
            mic.End(samples => StartCoroutine(Process(samples)));
        }

        /// <summary>Transcribe pre-recorded 16 kHz mono samples (also used by the automated self-test).</summary>
        public IEnumerator Process(float[] samples)
        {
            float seconds = samples.Length / (float)MicRecorder.TargetRate;
            if (seconds < 0.35f || MicRecorder.PeakRms(samples) < 0.01f)
            {
                brain.CancelListening();
                GameEvents.Toast("I didn't catch that. Hold V while you speak.");
                yield break;
            }

            IsTranscribing = true;
            byte[] wav = WavUtility.EncodePcm16(samples, MicRecorder.TargetRate);
            string lang = SaveSystem.Settings.language == LanguageMode.MandarinPractice ? "auto" : "en";
            string text = null, error = null;
            float t0 = Time.realtimeSinceStartup;
            yield return WhisperClient.Transcribe(LocalAIServices.Instance.WhisperBaseUrl, wav, lang, recognitionHint, (t, e) =>
            {
                text = t;
                error = e;
            });
            LastTranscriptionSeconds = Time.realtimeSinceStartup - t0;
            IsTranscribing = false;

            if (error != null)
            {
                brain.CancelListening();
                GameEvents.Toast("Couldn't hear you properly: " + error, 3.5f);
                yield break;
            }
            if (string.IsNullOrWhiteSpace(text) || SpeechText.LooksLikeHallucination(text))
            {
                brain.CancelListening();
                GameEvents.Toast("Hmm, I didn't catch any words. Try again?");
                yield break;
            }
            brain.SendPlayerMessage(text);
        }
    }
}
