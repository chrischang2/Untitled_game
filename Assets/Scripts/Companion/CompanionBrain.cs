using System;
using System.Collections.Generic;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Progression;
using Random = UnityEngine.Random;

namespace UntitledGame.Companion
{
    public enum CompanionState { Idle, Listening, Transcribing, Thinking, Speaking }

    /// <summary>
    /// Mei's conversational brain. Player speech/text goes to the local LLM (streamed); each finished
    /// sentence is handed straight to <see cref="CompanionVoice"/> so she starts talking before the
    /// whole reply is written. She also reacts to catches, weather and quiet moments by herself.
    /// </summary>
    public class CompanionBrain : MonoBehaviour
    {
        [Tooltip("Optional legacy non-streaming client (implements ILocalLLMClient), used only if the local AI services are unavailable.")]
        [SerializeField] private MonoBehaviour llmClientBehaviour;
        [SerializeField] private PhraseUnlockSystem phraseUnlockSystem;
        [Tooltip("Persona override. Leave empty to use the built-in Mei persona.")]
        [TextArea(3, 12)]
        [SerializeField] private string systemPrompt = "";

        [SerializeField] private CompanionVoice voice;
        [SerializeField] private CompanionController body;
        [SerializeField] private Transform player;
        [SerializeField] private FishingController fishing;
        [SerializeField] private ChatSampling sampling = new ChatSampling();
        [SerializeField] private int maxHistoryMessages = 30;
        [SerializeField] private int trimHistoryTo = 16;

        public CompanionState State { get; private set; } = CompanionState.Idle;

        /// <summary>Full reply text once Mei has finished generating it.</summary>
        public event Action<string> ReplyReceived;
        public event Action<string> PlayerSaid;
        public event Action<string> SentenceSpoken;
        public event Action<CompanionState> StateChanged;
        public event Action LogChanged;

        public readonly List<(string speaker, string text)> Log = new List<(string, string)>();

        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly SentenceSplitter _splitter = new SentenceSplitter();
        private ILocalLLMClient _legacy;
        private ChatStreamHandle _stream;
        private string _systemPromptCache;
        private bool _replyDone = true;
        private ChatMessage _pendingUser;
        private bool _greeted;
        private float _lastInteraction;
        private float _lastReaction = -999f;
        private float _nextChatter;
        private DayPhase _lastPhase;
        private bool _wasRaining;

        public PhraseUnlockSystem Phrases => phraseUnlockSystem;
        public bool IsBusy => State != CompanionState.Idle;
        public float LastResponseLatency { get; private set; } = -1f;

        public bool CanChat
        {
            get
            {
                var s = LocalAIServices.Instance;
                return (s != null && s.LlmStatus == ServiceStatus.Ready) || _legacy != null;
            }
        }

        public void Configure(CompanionVoice v, CompanionController c, Transform p, FishingController f)
        {
            voice = v;
            body = c;
            player = p;
            fishing = f;
        }

        private void Awake()
        {
            _legacy = llmClientBehaviour as ILocalLLMClient;
            if (voice == null) voice = GetComponent<CompanionVoice>();
            if (body == null) body = GetComponent<CompanionController>();
        }

        private void OnEnable()
        {
            FishingController.FishCaught += OnFishCaught;
            FishingController.FishEscaped += OnFishEscaped;
            SaveSystem.SettingsChanged += OnSettingsChanged;
            if (voice != null)
            {
                voice.SentenceStarted += OnSentenceStarted;
                voice.AllFinished += OnVoiceFinished;
            }
        }

        private void OnDisable()
        {
            FishingController.FishCaught -= OnFishCaught;
            FishingController.FishEscaped -= OnFishEscaped;
            SaveSystem.SettingsChanged -= OnSettingsChanged;
            if (voice != null)
            {
                voice.SentenceStarted -= OnSentenceStarted;
                voice.AllFinished -= OnVoiceFinished;
            }
        }

        private void Start()
        {
            _lastInteraction = Time.time;
            _nextChatter = Time.time + Random.Range(150f, 240f);
            if (DayNightCycle.Instance != null) _lastPhase = DayNightCycle.Instance.Phase;
        }

        private void SetState(CompanionState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        private float DistanceToPlayer => player == null ? 0f : Vector3.Distance(player.position, transform.position);

        // ------------------------------------------------------------------ public API

        /// <summary>Player pressed push-to-talk: stop talking and listen.</summary>
        public void BeginListening()
        {
            Interrupt();
            SetState(CompanionState.Listening);
        }

        public void BeginTranscribing() => SetState(CompanionState.Transcribing);

        public void CancelListening()
        {
            if (State == CompanionState.Listening || State == CompanionState.Transcribing) SetState(CompanionState.Idle);
        }

        /// <summary>Send something the player said (transcribed) or typed.</summary>
        public void SendPlayerMessage(string playerText)
        {
            if (string.IsNullOrWhiteSpace(playerText)) return;
            playerText = playerText.Trim();
            Interrupt();
            AddLog("You", playerText);
            PlayerSaid?.Invoke(playerText);
            _lastInteraction = Time.time;
            _nextChatter = Time.time + Random.Range(150f, 260f);
            body?.FacePlayerFor(6f);

            string situation = CompanionPersona.Situation(fishing != null ? fishing.ActivityDescription : "", DistanceToPlayer);
            Ask(situation + "\n" + playerText);
        }

        /// <summary>Let Mei react to something that happened. Returns false if she's busy.</summary>
        public bool SendGameEvent(string description, string instruction = "React naturally in one short sentence.")
        {
            if (!CanChat || IsBusy) return false;
            _lastReaction = Time.time;
            Ask($"[Game: {description}] ({instruction})");
            return true;
        }

        /// <summary>Stop the current reply (player barged in).</summary>
        public void Interrupt()
        {
            if (_stream != null && !_stream.Done)
            {
                string partial = SpeechText.CleanForDisplay(_stream.FullText);
                _stream.Cancel();
                if (partial.Length > 0)
                {
                    _history.Add(new ChatMessage("assistant", partial + "..."));
                    AddLog(CompanionPersona.Name, partial + "...");
                }
                else DropPendingUser();
            }
            _pendingUser = null;
            _stream = null;
            _replyDone = true;
            voice?.StopAll();
            SetState(CompanionState.Idle);
        }

        public void ClearConversation()
        {
            Interrupt();
            _history.Clear();
            Log.Clear();
            LogChanged?.Invoke();
        }

        // ------------------------------------------------------------------ core

        private void Ask(string userContent)
        {
            if (!CanChat)
            {
                voice?.Say(OfflineLine());
                return;
            }

            string sp = CompanionPersona.BuildSystemPrompt(systemPrompt);
            if (sp != _systemPromptCache) _systemPromptCache = sp;

            _pendingUser = new ChatMessage("user", userContent);
            _history.Add(_pendingUser);
            if (_history.Count > maxHistoryMessages)
            {
                int remove = _history.Count - trimHistoryTo;
                // Keep pairs aligned: always start the kept history on a user turn.
                while (remove < _history.Count && _history[remove].role != "user") remove++;
                _history.RemoveRange(0, Mathf.Min(remove, _history.Count - 1));
            }

            var messages = new List<ChatMessage>(_history.Count + 1) { new ChatMessage("system", _systemPromptCache) };
            messages.AddRange(_history);

            _splitter.Reset();
            _replyDone = false;
            SetState(CompanionState.Thinking);

            var services = LocalAIServices.Instance;
            if (services != null && services.LlmStatus == ServiceStatus.Ready)
            {
                _stream = OpenAIStreamingClient.Stream(this, services.LlmBaseUrl, services.LlmModelName, messages, sampling, OnDelta, OnStreamComplete);
            }
            else if (_legacy != null)
            {
                StartCoroutine(_legacy.SendChat(new ChatRequest { messages = messages }, r =>
                {
                    if (r.Success) OnDelta(r.ReplyText + " ");
                    FinishReply(r.Success ? r.ReplyText : null, r.Error);
                }));
            }
        }

        private void OnDelta(string delta)
        {
            foreach (var sentence in _splitter.Feed(delta)) voice?.Say(sentence);
        }

        private void OnStreamComplete(ChatStreamHandle h)
        {
            if (h != _stream || h.Cancelled) return;
            if (h.FirstTokenAt > 0f) LastResponseLatency = h.FirstTokenAt - h.StartedAt;
            FinishReply(h.FullText, h.Error);
        }

        private void FinishReply(string fullText, string error)
        {
            _stream = null;
            string rest = _splitter.Flush();
            if (!string.IsNullOrWhiteSpace(rest)) voice?.Say(rest);

            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[Mei] LLM error: {error}");
                if (string.IsNullOrWhiteSpace(fullText)) voice?.Say("Hmm, sorry, my head's a bit foggy. Could you say that again?");
            }

            string clean = SpeechText.CleanForDisplay(fullText ?? "");
            if (clean.Length == 0) DropPendingUser();
            _pendingUser = null;
            if (clean.Length > 0)
            {
                _history.Add(new ChatMessage("assistant", clean));
                AddLog(CompanionPersona.Name, clean);
                ReplyReceived?.Invoke(clean);
            }
            _replyDone = true;
            if (voice == null || !voice.IsSpeaking) SetState(CompanionState.Idle);
        }

        /// <summary>Removes an unanswered user turn so the history never has two user messages in a row.</summary>
        private void DropPendingUser()
        {
            if (_pendingUser != null && _history.Count > 0 && _history[_history.Count - 1] == _pendingUser)
                _history.RemoveAt(_history.Count - 1);
        }

        private void OnSentenceStarted(string s)
        {
            SetState(CompanionState.Speaking);
            SentenceSpoken?.Invoke(s);
        }

        private void OnVoiceFinished()
        {
            if (_replyDone && (State == CompanionState.Speaking || State == CompanionState.Thinking)) SetState(CompanionState.Idle);
        }

        private void AddLog(string speaker, string text)
        {
            Log.Add((speaker, text));
            if (Log.Count > 200) Log.RemoveAt(0);
            LogChanged?.Invoke();
        }

        private string OfflineLine()
        {
            var s = LocalAIServices.Instance;
            if (s != null && s.LlmStatus == ServiceStatus.Starting)
                return "Mm, give me a moment, I'm still waking up...";
            return "I'd love to chat, but my thinking cap isn't installed yet. Run the setup script and I'll be all ears!";
        }

        private void OnSettingsChanged()
        {
            string sp = CompanionPersona.BuildSystemPrompt(systemPrompt);
            if (sp != _systemPromptCache) _systemPromptCache = sp;
        }

        // ------------------------------------------------------------------ autonomous behaviour

        private void Update()
        {
            if (State == CompanionState.Thinking && voice != null && voice.IsSpeaking) SetState(CompanionState.Speaking);
            if (State == CompanionState.Speaking && _replyDone && voice != null && !voice.IsSpeaking) SetState(CompanionState.Idle);

            if (!CanChat) return;

            if (!_greeted && Time.timeSinceLevelLoad > 1.5f)
            {
                string phase = DayNightCycle.Instance != null ? DayNightCycle.Instance.PhaseDescription : "day";
                if (SendGameEvent($"Your friend just arrived at the lake this {phase}. You're happy to see them.",
                        "Greet them warmly in one or two short sentences, maybe mention the weather or the fish."))
                {
                    _greeted = true;
                    _lastInteraction = Time.time;
                }
                return;
            }

            var dn = DayNightCycle.Instance;
            if (dn != null && dn.Phase != _lastPhase)
            {
                var from = _lastPhase;
                _lastPhase = dn.Phase;
                if (!IsBusy && Random.value < 0.6f && DistanceToPlayer < 20f)
                {
                    if (dn.Phase == DayPhase.Evening) SendGameEvent("The sun is starting to set and the sky is turning orange.");
                    else if (dn.Phase == DayPhase.Night && from == DayPhase.Dusk) SendGameEvent("It's getting dark; the stars are coming out and the fireflies are appearing.");
                    else if (dn.Phase == DayPhase.Dawn) SendGameEvent("The sun is just coming up over the hills. You both stayed up all night fishing.");
                }
            }

            bool raining = Weather.Instance != null && Weather.Instance.IsRaining;
            if (raining != _wasRaining)
            {
                _wasRaining = raining;
                if (!IsBusy && DistanceToPlayer < 20f)
                    SendGameEvent(raining ? "It just started raining softly on the lake." : "The rain has stopped and everything smells fresh.");
            }

            if (SaveSystem.Settings.companionChatter && !IsBusy && Time.time > _nextChatter && DistanceToPlayer < 14f)
            {
                _nextChatter = Time.time + Random.Range(170f, 300f);
                if (Time.time - _lastInteraction > 90f)
                {
                    SendGameEvent("It's been quiet for a little while.",
                        "Say one small cozy observation about the lake, the weather or the time of day, or ask the player a light, friendly question. One or two short sentences.");
                }
            }
        }

        private void OnFishCaught(CatchResult r)
        {
            body?.Celebrate();
            if (!CanChat || IsBusy) return;
            bool special = r.isNewSpecies || r.species.rarity >= Rarity.Rare || r.isRecord || !r.species.IsFish;
            if (!special && (Time.time - _lastReaction < 25f || Random.value > 0.55f)) return;

            string what = r.species.IsFish
                ? $"The player just caught a {r.species.name} ({r.species.hanzi}), {r.length:0} cm, {FishDatabase.RarityLabel(r.species.rarity).ToLower()}."
                : $"The player just fished up a {r.species.name} ({r.species.hanzi}): {r.species.blurb}";
            if (r.isNewSpecies) what += " It's their first one ever, a new journal entry!";
            if (r.isRecord) what += " That's their biggest one yet!";
            SendGameEvent(what, "React with genuine excitement in one or two short sentences. Maybe share a tiny fun fact.");
        }

        private void OnFishEscaped(string reason)
        {
            if (!CanChat || IsBusy || Time.time - _lastReaction < 20f) return;
            if (reason == "snapped" && Random.value < 0.5f)
                SendGameEvent("The player's line just snapped and a fish got away.", "Comfort or gently tease them in one short sentence.");
            else if (reason == "escaped" && Random.value < 0.3f)
                SendGameEvent("A fish just slipped off the player's hook and swam away.", "Say something encouraging in one short sentence.");
        }
    }
}
