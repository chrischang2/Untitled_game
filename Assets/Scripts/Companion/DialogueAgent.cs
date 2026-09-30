using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.GenAI;
using UntitledGame.Language;

namespace UntitledGame.Companion
{
    public enum CompanionState { Idle, Listening, Transcribing, Thinking, Speaking }

    /// <summary>Everything said by anyone, for the chat log (and as study material).</summary>
    public static class ConversationLog
    {
        public struct Entry
        {
            public string speaker;
            public string text;
            public bool fromPlayer;
        }

        public static readonly List<Entry> Entries = new List<Entry>();
        public static event Action Changed;

        public static void Add(string speaker, string text, bool fromPlayer)
        {
            Entries.Add(new Entry { speaker = speaker, text = text, fromPlayer = fromPlayer });
            if (Entries.Count > 300) Entries.RemoveAt(0);
            Changed?.Invoke();
        }

        public static void Clear()
        {
            Entries.Clear();
            Changed?.Invoke();
        }

        public static List<SavedLine> Export(int max) =>
            Entries.GetRange(Math.Max(0, Entries.Count - max), Math.Min(max, Entries.Count))
                .ConvertAll(e => new SavedLine { speaker = e.speaker, text = e.text, fromPlayer = e.fromPlayer });

        public static void Import(List<SavedLine> lines)
        {
            Entries.Clear();
            if (lines != null)
                foreach (var l in lines) Entries.Add(new Entry { speaker = l.speaker, text = l.text, fromPlayer = l.fromPlayer });
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Entries.Clear();
            Changed = null;
        }
    }

    /// <summary>
    /// Someone the player can talk to: keeps a conversation history, streams replies from the local LLM
    /// and hands each finished sentence straight to <see cref="CharacterVoice"/> so speech starts before
    /// the reply is complete. Each agent kind uses its own llama-server slot so its prompt cache stays warm.
    /// </summary>
    public abstract class DialogueAgent : MonoBehaviour
    {
        [SerializeField] protected CharacterVoice voice;
        [SerializeField] protected Transform player;
        [SerializeField] protected ChatSampling sampling = new ChatSampling();
        [SerializeField] protected int llmSlot = -1;
        [SerializeField] protected int maxHistoryMessages = 24;
        [SerializeField] protected int trimHistoryTo = 12;

        public abstract string DisplayName { get; }
        public virtual string DisplayNameEnglish => DisplayName;
        public CharacterVoice Voice => voice;
        public CompanionState State { get; private set; } = CompanionState.Idle;
        public bool IsBusy => State != CompanionState.Idle;
        public float LastResponseLatency { get; private set; } = -1f;

        public event Action<string> SentenceSpoken;
        public event Action<CompanionState> StateChanged;
        public event Action<string> ReplyReceived;
        public event Action<string> PlayerSaid;

        protected readonly List<ChatMessage> History = new List<ChatMessage>();
        private readonly SentenceSplitter _splitter = new SentenceSplitter(8);
        private ChatStreamHandle _stream;
        private ChatMessage _pendingUser;
        private bool _replyDone = true;
        private string _systemPromptCache;
        private string _loggedSystemPrompt;
        private string _pendingFullContent;
        private bool _retriedAfterOverflow;

        /// <summary>Stable id for this character in save files and logs.</summary>
        public string MemoryKey => DisplayNameEnglish;

        /// <summary>The recent conversation, for the save file.</summary>
        public AgentMemory ExportMemory() => new AgentMemory
        {
            agent = MemoryKey,
            messages = History.ConvertAll(m => new SavedMessage { role = m.role, content = m.content }),
        };

        /// <summary>Restores a saved conversation so the character remembers the player.</summary>
        public void ImportMemory(AgentMemory memory)
        {
            History.Clear();
            if (memory?.messages == null) return;
            foreach (var m in memory.messages)
            {
                if (string.IsNullOrEmpty(m.role) || m.content == null) continue;
                string content = m.role == "user" ? Compact(m.content) : m.content;
                if (content.Length > 0) History.Add(new ChatMessage(m.role, content));
            }
            // A saved history should start on a user turn.
            while (History.Count > 0 && History[0].role != "user") History.RemoveAt(0);
            if (History.Count > 0) ChatAudit.Write(MemoryKey, $"remembered {History.Count} messages from the save");
        }

        public bool HasMemory => History.Count > 0;

        // Older saves kept every per-turn note in the history; drop the situation, overheard-talk and reminder lines.
        private static readonly System.Text.RegularExpressions.Regex StaleNote = new System.Text.RegularExpressions.Regex(
            @"^\s*(\[Game: \d{1,2}:\d{2} [AP]M.*|\[Game: You just overheard.*|\(Reply .*\))\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);

        private static string Compact(string userTurn) =>
            string.Join("\n", StaleNote.Replace(userTurn, "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

        public virtual bool CanChat
        {
            get
            {
                var s = LocalAIServices.Instance;
                return s != null && s.LlmStatus == ServiceStatus.Ready;
            }
        }

        public float DistanceToPlayer => player == null ? 0f : Vector3.Distance(player.position, transform.position);

        protected virtual void OnEnable()
        {
            if (voice == null) voice = GetComponent<CharacterVoice>();
            if (voice != null)
            {
                voice.SentenceStarted += OnSentenceStarted;
                voice.AllFinished += OnVoiceFinished;
            }
        }

        protected virtual void OnDisable()
        {
            if (voice != null)
            {
                voice.SentenceStarted -= OnSentenceStarted;
                voice.AllFinished -= OnVoiceFinished;
            }
        }

        protected void SetState(CompanionState s)
        {
            if (State == s) return;
            State = s;
            StateChanged?.Invoke(s);
        }

        protected abstract string BuildSystemPrompt();

        /// <summary>The player said (or typed) something to this character.</summary>
        public abstract void HandlePlayerUtterance(string text);

        // ------------------------------------------------------------------ listening states

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

        /// <summary>Records the player's line (log, events) - subclasses call this before replying.</summary>
        protected void RecordPlayerLine(string text)
        {
            ChatAudit.Write("YOU→" + MemoryKey, text);
            ConversationLog.Add("You", text, true);
            PlayerSaid?.Invoke(text);
            VocabNotebook.ObservePlayerLine(text);
        }

        // ------------------------------------------------------------------ core

        /// <summary>Stop the current reply (player barged in).</summary>
        public void Interrupt()
        {
            if (_stream != null && !_stream.Done)
            {
                string partial = SpeechText.CleanForDisplay(_stream.FullText);
                ChatAudit.Write(MemoryKey, "interrupted mid-reply" + (partial.Length > 0 ? ": " + partial : " (before any words)"));
                _stream.Cancel();
                if (partial.Length > 0)
                {
                    History.Add(new ChatMessage("assistant", partial + "..."));
                    ConversationLog.Add(DisplayName, partial + "...", false);
                }
                else DropPendingUser();
            }
            _pendingUser = null;
            _stream = null;
            _replyDone = true;
            voice?.StopAll();
            SetState(CompanionState.Idle);
        }

        public virtual void ClearConversation()
        {
            Interrupt();
            ChatAudit.Write(MemoryKey, "conversation cleared");
            History.Clear();
        }

        /// <summary>Sends one user turn (situation notes + what the player said) and streams the reply.</summary>
        /// <param name="userContent">Everything the model needs for this turn (situation notes, instructions, the player's words).</param>
        /// <param name="rememberAs">
        /// What stays in the conversation history afterwards. Per-turn situation notes are only useful once; keeping them
        /// made Mei's history overflow the model's context, after which every request failed.
        /// </param>
        protected void Ask(string userContent, string rememberAs = null)
        {
            if (!CanChat)
            {
                voice?.Say(OfflineLine());
                return;
            }

            string sp = BuildSystemPrompt();
            if (sp != _systemPromptCache) _systemPromptCache = sp;
            if (sp != _loggedSystemPrompt)
            {
                _loggedSystemPrompt = sp;
                ChatAudit.Write("→" + MemoryKey, "SYSTEM PROMPT (changed)", sp);
            }
            ChatAudit.Write("→" + MemoryKey, $"turn (history {History.Count} msgs, slot {llmSlot})", userContent);

            _pendingUser = new ChatMessage("user", string.IsNullOrEmpty(rememberAs) ? userContent : rememberAs);
            _pendingFullContent = userContent;
            History.Add(_pendingUser);
            if (History.Count > maxHistoryMessages)
            {
                int remove = History.Count - trimHistoryTo;
                while (remove < History.Count && History[remove].role != "user") remove++;
                History.RemoveRange(0, Mathf.Min(remove, History.Count - 1));
            }
            _retriedAfterOverflow = false;

            _splitter.Reset();
            _replyDone = false;
            SetState(CompanionState.Thinking);
            Send();
        }

        /// <summary>Fits the history to the context window, then streams the reply to the pending turn.</summary>
        private void Send()
        {
            FitToContext();
            var messages = new List<ChatMessage>(History.Count + 1) { new ChatMessage("system", _systemPromptCache) };
            foreach (var m in History) messages.Add(m == _pendingUser ? new ChatMessage("user", _pendingFullContent) : m);

            var services = LocalAIServices.Instance;
            _stream = OpenAIStreamingClient.Stream(this, services.LlmBaseUrl, services.LlmModelName, messages, sampling, OnDelta, OnStreamComplete, llmSlot);
        }

        /// <summary>Room for the prompt: the slot's context minus the reply and a safety margin for the chat template.</summary>
        private int PromptBudget()
        {
            int ctx = LocalAIServices.Instance?.Config?.contextSize ?? 4096;
            return Mathf.Max(1024, ctx - sampling.max_tokens - 300);
        }

        /// <summary>Rough token count for Qwen: about one token per Chinese character, about 3.2 characters per token otherwise.</summary>
        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 4;
            int cjk = 0;
            foreach (char c in text) if (SpeechText.IsCjk(c)) cjk++;
            return 6 + Mathf.CeilToInt(cjk * 1.15f + (text.Length - cjk) / 3.2f);
        }

        private int EstimatePrompt()
        {
            int total = EstimateTokens(_systemPromptCache);
            foreach (var m in History) total += EstimateTokens(m == _pendingUser ? _pendingFullContent : m.content);
            return total;
        }

        /// <summary>Drops the oldest exchanges until the prompt fits (always keeps the turn being answered).</summary>
        private void FitToContext(int budget = -1)
        {
            if (budget < 0) budget = PromptBudget();
            int before = EstimatePrompt(), removed = 0;
            while (History.Count > 1 && EstimatePrompt() > budget)
            {
                History.RemoveAt(0);
                removed++;
                while (History.Count > 1 && History[0].role != "user")
                {
                    History.RemoveAt(0);
                    removed++;
                }
            }
            if (removed > 0)
                ChatAudit.Write(MemoryKey, $"forgot the {removed} oldest messages to fit the context (~{before} -> ~{EstimatePrompt()} tokens, budget {budget})");
        }

        private void OnDelta(string delta)
        {
            foreach (var sentence in _splitter.Feed(delta)) voice?.Say(sentence);
        }

        private void OnStreamComplete(ChatStreamHandle h)
        {
            if (h != _stream || h.Cancelled) return;
            if (h.FirstTokenAt > 0f) LastResponseLatency = h.FirstTokenAt - h.StartedAt;
            _stream = null;

            // The server rejects prompts longer than the context (HTTP 400). Forget more history and try once more.
            if (!string.IsNullOrEmpty(h.Error) && h.Error.Contains("400") && string.IsNullOrEmpty(h.FullText) && !_retriedAfterOverflow && _pendingUser != null)
            {
                _retriedAfterOverflow = true;
                int half = Mathf.Max(256, EstimatePrompt() / 2);
                ChatAudit.Write(MemoryKey, $"request rejected ({h.Error}); forgetting older messages and retrying");
                FitToContext(half);
                Send();
                return;
            }
            string rest = _splitter.Flush();
            if (!string.IsNullOrWhiteSpace(rest)) voice?.Say(rest);

            if (!string.IsNullOrEmpty(h.Error))
            {
                Debug.LogWarning($"[{DisplayNameEnglish}] LLM error: {h.Error}");
                if (string.IsNullOrWhiteSpace(h.FullText)) voice?.Say(ErrorLine());
            }

            string clean = SpeechText.CleanForDisplay(h.FullText ?? "");
            float first = h.FirstTokenAt > 0f ? h.FirstTokenAt - h.StartedAt : -1f;
            string raw = h.FullText ?? "";
            ChatAudit.Write(MemoryKey + "→",
                $"reply: first token {ChatAudit.Seconds(first)}, total {ChatAudit.Seconds(Time.realtimeSinceStartup - h.StartedAt)}" +
                (string.IsNullOrEmpty(h.Error) ? "" : $", ERROR {h.Error}") + (clean.Length == 0 ? ", EMPTY" : ""),
                raw == clean ? clean : $"raw:   {raw}\nshown: {clean}");
            if (clean.Length == 0) DropPendingUser();
            _pendingUser = null;
            if (clean.Length > 0)
            {
                History.Add(new ChatMessage("assistant", clean));
                ConversationLog.Add(DisplayName, clean, false);
                OnReplyFinished(clean);
                ReplyReceived?.Invoke(clean);
            }
            _replyDone = true;
            if (voice == null || !voice.IsSpeaking) SetState(CompanionState.Idle);
        }

        /// <summary>Hook for subclasses (e.g. Mei feeds the vocab notebook).</summary>
        protected virtual void OnReplyFinished(string text) { }

        protected virtual string OfflineLine() => "……";
        protected virtual string ErrorLine() => "嗯……？";

        private void DropPendingUser()
        {
            if (_pendingUser != null && History.Count > 0 && History[History.Count - 1] == _pendingUser)
                History.RemoveAt(History.Count - 1);
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

        protected virtual void Update()
        {
            if (State == CompanionState.Thinking && voice != null && voice.IsSpeaking) SetState(CompanionState.Speaking);
            if (State == CompanionState.Speaking && _replyDone && voice != null && !voice.IsSpeaking) SetState(CompanionState.Idle);
        }
    }
}
