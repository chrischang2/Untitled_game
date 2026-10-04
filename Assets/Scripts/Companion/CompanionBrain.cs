using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Language;
using UntitledGame.Progression;
using Random = UnityEngine.Random;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Mei: best friend and Mandarin tutor. Answers the player (in Mandarin, with as much English as the
    /// immersion level allows), teaches exactly the words needed for the market, and speaks up on her own:
    /// greetings, catches (naming the fish in Chinese), arriving at the market, a full bucket, a hungry
    /// Tangyuan, and praise after the player manages a purchase in Mandarin.
    /// </summary>
    public class CompanionBrain : DialogueAgent
    {
        [Tooltip("Kept for the old scaffold scene tools; unused.")]
        [SerializeField] private MonoBehaviour llmClientBehaviour;
        [SerializeField] private PhraseUnlockSystem phraseUnlockSystem;
        [Tooltip("Persona override. Leave empty to use the built-in Mei persona.")]
        [TextArea(3, 12)]
        [SerializeField] private string systemPrompt = "";

        [SerializeField] private CompanionController body;
        [SerializeField] private FishingController fishing;

        public override string DisplayName => CompanionPersona.Name;
        public PhraseUnlockSystem Phrases => phraseUnlockSystem;

        private bool _greeted;
        private float _lastInteraction;
        private float _lastReaction = -999f;
        private float _nextChatter;
        private DayPhase _lastPhase;
        private bool _wasRaining;
        private bool _wasInMarket;
        private bool _bucketHintGiven;
        private float _lastPetHint = -999f;

        public void Configure(CharacterVoice v, CompanionController c, Transform p, FishingController f)
        {
            voice = v;
            body = c;
            player = p;
            fishing = f;
            llmSlot = 0;
            sampling = new ChatSampling { temperature = 0.7f, top_p = 0.85f, top_k = 20, presence_penalty = 1.0f, max_tokens = 150 };
        }

        public static CompanionBrain Current { get; private set; }

        protected override void OnEnable()
        {
            base.OnEnable();
            Current = this;
            if (body == null) body = GetComponent<CompanionController>();
            FishingController.FishCaught += OnFishCaught;
            FishingController.FishEscaped += OnFishEscaped;
            ShopkeeperBrain.TransactionDone += OnTransaction;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Current == this) Current = null;
            FishingController.FishCaught -= OnFishCaught;
            FishingController.FishEscaped -= OnFishEscaped;
            ShopkeeperBrain.TransactionDone -= OnTransaction;
        }

        private void Start()
        {
            _lastInteraction = Time.time;
            _nextChatter = Time.time + Random.Range(150f, 240f);
            if (DayNightCycle.Instance != null) _lastPhase = DayNightCycle.Instance.Phase;
        }

        protected override string BuildSystemPrompt() => CompanionPersona.BuildSystemPrompt(systemPrompt);

        protected override string OfflineLine()
        {
            var s = LocalAIServices.Instance;
            return s != null && s.LlmStatus == ServiceStatus.Starting ? "等一下哦 [wait a moment], I'm still waking up!" : "我的脑子还没装好 [my brain isn't installed yet]. Run the setup script!";
        }

        protected override string ErrorLine() => "嗯？ [Hm?] Could you say that again?";

        protected override void OnReplyFinished(string text) => VocabNotebook.ObserveTutorLine(text);

        // ------------------------------------------------------------------ talking

        /// <summary>Kept for compatibility with older callers.</summary>
        public void SendPlayerMessage(string playerText) => HandlePlayerUtterance(playerText);

        public override void HandlePlayerUtterance(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            Interrupt();
            RecordPlayerLine(text);
            _lastInteraction = Time.time;
            _nextChatter = Time.time + Random.Range(150f, 260f);
            body?.FacePlayerFor(6f);
            string notes = SpokenAction(text) + HeardHint(text);
            string facts = Encyclopedia.NoteFor(text); // per-turn only: never kept in her memory (small context)
            var keeper = ShopConversation.Active;
            string shopTalk = keeper != null ? SideChatNote(keeper) : OverheardShopTalk();
            if (keeper != null) ChatAudit.Write(MemoryKey, $"side chat while talking with {keeper.DisplayName}");
            // Only the player's words are remembered: the situation notes are re-sent fresh every turn.
            Ask(BuildSituation() + shopTalk + notes + facts + "\n" + text + "\n" + CompanionPersona.LevelReminder(SaveSystem.Settings.immersion),
                rememberAs: keeper != null ? $"[Game: asked quietly while talking with {keeper.DisplayName}]\n{text}"
                    : notes.Trim().Length > 0 ? notes.Trim() + "\n" + text : text);
        }

        /// <summary>
        /// B during a shop conversation: the player turns to Mei and asks quietly. She sees the whole conversation
        /// so far and helps with exactly that: what the keeper just said, or how to say something to them.
        /// </summary>
        private static string SideChatNote(ShopkeeperBrain keeper)
        {
            var entries = ConversationLog.Entries;
            int from = Mathf.Clamp(ShopConversation.StartedAtLogIndex, 0, entries.Count);
            var lines = entries.Skip(from).Where(e => e.speaker != CompanionPersona.Name).ToList();
            if (lines.Count > 10) lines = lines.Skip(lines.Count - 10).ToList();
            string convo = lines.Count == 0
                ? "nothing has been said yet"
                : string.Join(" / ", lines.Select(e => $"{(e.fromPlayer ? "Player" : keeper.DisplayName)}: {e.text}"));
            var lastKeeperLine = lines.LastOrDefault(e => !e.fromPlayer).text;
            // The 4B model once told the player to accept by saying the keeper's own question (要吗？): spell out the replies.
            string offer = keeper.PendingOffer != null
                ? $" Their offer waiting for an answer: {keeper.PendingOffer.english}. To accept, the player says 要 or 好的 [yes, OK]; to decline, 不要了，谢谢 [no thanks]. (要吗？ is the keeper's question, not an answer.)"
                : "";
            return $"\n[Game: The player is in the middle of a conversation with {keeper.DisplayName} ({keeper.DisplayNameEnglish}) at the {keeper.Shop?.hanzi} " +
                   $"[{keeper.Shop?.english}], and has turned to you to ask quietly. The conversation so far: {convo}.{offer}" +
                   (string.IsNullOrEmpty(lastKeeperLine) ? "" : $" {keeper.DisplayName}'s last line was: {lastKeeperLine}") +
                   " Help with this conversation only. If they ask what was said or meant, translate that line simply and point out its key word as 汉字 [meaning]." +
                   $" If they ask how to say something, give exactly one short sentence they can say to {keeper.DisplayName} next, as 汉字 [meaning]." +
                   " Be brief, so they can turn back and say it.]";
        }

        /// <summary>Let Mei react to something that happened. Returns false if she's busy.</summary>
        /// <summary>
        /// Mei only speaks when spoken to (the player asked for this), apart from greeting them when the game opens.
        /// Her other reactions (catches, weather, the market...) are kept in the code but switched off here.
        /// </summary>
        public const bool OnlyWhenSpokenTo = true;

        public bool SendGameEvent(string description, string instruction = "React naturally in one or two short sentences.", bool evenWhileShopping = false, bool isGreeting = false)
        {
            if (OnlyWhenSpokenTo && !isGreeting) return false;
            if (!CanChat || IsBusy) return false;
            // Don't talk over a shopkeeper or butt into a purchase.
            if (ShopkeeperBrain.AnyBusy || (!evenWhileShopping && (ShopConversation.Active != null || Time.time - ShopkeeperBrain.LastCustomerActivity < 15f))) return false;
            _lastReaction = Time.time;
            Ask($"{BuildSituation()}\n[Game: {description}] ({instruction}) {CompanionPersona.LevelReminder(SaveSystem.Settings.immersion)}",
                rememberAs: $"[Game: {description}]");
            return true;
        }

        public override void ClearConversation()
        {
            base.ClearConversation();
            ConversationLog.Clear();
        }

        /// <summary>
        /// Saying 喂汤圆 (feed Tangyuan) really feeds her when she's close. Mei used to tell the player to say it,
        /// but nothing happened, so she kept repeating the advice while the cat stayed hungry.
        /// </summary>
        private string SpokenAction(string text)
        {
            if (!PhraseMatcher.SoundsLikeFeedingTheCat(text)) return "";
            var cat = UntitledGame.Home.PetController.Instance;
            if (cat == null) return "";
            var result = cat.Feed(maxDistance: 8f);
            ChatAudit.Write(MemoryKey, $"spoken command: feed Tangyuan -> {result}");
            return result switch
            {
                UntitledGame.Home.PetController.FeedResult.FedFood => $"\n[Game: The player said it and fed {CompanionPersona.PetName} some cat food. She's eating happily. Praise them: saying it worked!]",
                UntitledGame.Home.PetController.FeedResult.FedTreat => $"\n[Game: The player said it and gave {CompanionPersona.PetName} a dried-fish treat. Praise them: saying it worked!]",
                UntitledGame.Home.PetController.FeedResult.NoFood => $"\n[Game: The player tried to feed {CompanionPersona.PetName}, but they have no cat food or treats. Suggest 小林's pet shop.]",
                UntitledGame.Home.PetController.FeedResult.NotHungry => $"\n[Game: The player tried to feed {CompanionPersona.PetName}, but she isn't hungry right now.]",
                _ => $"\n[Game: The player tried to feed {CompanionPersona.PetName}, but she's too far away ({cat.DistanceToPlayer:0} m). They need to walk over to her first.]",
            };
        }

        /// <summary>When the recogniser's text sounds like a phrase the player has learned, tell Mei what they probably meant.</summary>
        private static string HeardHint(string text)
        {
            var meant = PhraseMatcher.ProbablyMeant(text);
            if (meant == null) return "";
            ChatAudit.Write("Mei", $"sounds like the notebook phrase {meant.hanzi} [{meant.meaning}]");
            return $"\n[Game: Speech recognition heard \"{text}\", which sounds like {meant.hanzi} [{meant.meaning}], a phrase they learned. They were probably saying that; treat it as correct and don't comment on the odd characters.]";
        }

        private string BuildSituation()
        {
            string where = player != null ? WorldLocations.Describe(player.position) : "on the beach";
            return CompanionPersona.Situation(fishing != null ? fishing.ActivityDescription : "", DistanceToPlayer, where);
        }

        /// <summary>If the player just talked with a shopkeeper, Mei "overheard" it and can help.</summary>
        private static string OverheardShopTalk()
        {
            var recent = ConversationLog.Entries.Skip(System.Math.Max(0, ConversationLog.Entries.Count - 4))
                .Where(e => e.speaker != CompanionPersona.Name).ToList();
            if (!recent.Any(e => !e.fromPlayer)) return "";
            return "\n[Game: You just overheard at the shop: " + string.Join(" / ", recent.Select(e => $"{(e.fromPlayer ? "Player" : e.speaker)}: {e.text}")) + "]";
        }

        // ------------------------------------------------------------------ autonomous behaviour

        protected override void Update()
        {
            base.Update();
            if (!CanChat) return;
            float dist = DistanceToPlayer;

            if (!_greeted && Time.timeSinceLevelLoad > 1.5f)
            {
                string phase = DayNightCycle.Instance != null ? DayNightCycle.Instance.PhaseDescription : "day";
                bool returning = HasMemory || VocabNotebook.Entries.Count > 0;
                var review = VocabNotebook.Entries.OrderBy(v => v.said).ThenBy(_ => Random.value).FirstOrDefault();
                bool sent = returning
                    ? SendGameEvent($"Your friend is back at the beach this {phase} after some time away.",
                        review != null
                            ? "Welcome them back warmly in one or two short sentences. No lesson."
                            : "Welcome them back warmly and suggest what to do today. Keep it short. No lesson.", isGreeting: true)
                    : SendGameEvent($"Your friend just arrived at the beach this {phase}.",
                        "Greet them warmly and suggest a plan for today, e.g. catch some fish and sell them at the market. Keep it short. No lesson.", isGreeting: true);
                if (sent)
                {
                    _greeted = true;
                    _lastInteraction = Time.time;
                }
                return;
            }

            // Arriving at the market: teach how to greet a shopkeeper / what to say.
            bool inMarket = player != null && WorldShape.InMarket(player.position.x, player.position.z, 2f);
            if (inMarket && !_wasInMarket && !IsBusy && dist < 12f)
            {
                bool first = !SaveSystem.Data.visitedMarket;
                SaveSystem.Data.visitedMarket = true;
                SendGameEvent(first ? "You and the player just arrived at the market for the first time. The shopkeepers only speak Mandarin."
                                    : "You and the player are back at the market.",
                    first ? "Tell them the shopkeepers only speak Mandarin, press E at a stall to talk, and hold B to ask you quietly for help. Keep it short. No lesson."
                          : "React in a few words. No lesson.");
            }
            _wasInMarket = inMarket;

            // Full bucket: teach how to sell.
            if (Inventory.BucketFull && !_bucketHintGiven && !IsBusy && dist < 15f)
            {
                _bucketHintGiven = true;
                SendGameEvent("The player's fish bucket is full.", "Suggest selling the fish to 陈阿姨 at the fish shop. No lesson.");
            }
            if (!Inventory.BucketFull) _bucketHintGiven = false;

            // Hungry cat.
            if (SaveSystem.Data.pet.hunger > 0.75f && Time.time - _lastPetHint > 240f && !IsBusy && dist < 15f)
            {
                _lastPetHint = Time.time;
                bool hasFood = Inventory.Count("cat_food") > 0 || Inventory.Count("cat_treat") > 0;
                SendGameEvent($"{CompanionPersona.PetName} the cat looks hungry.",
                    hasFood ? "Remind them to feed her: walk up to her and press F. No lesson." : "Suggest buying cat food at 小林's pet shop. No lesson.");
            }

            var dn = DayNightCycle.Instance;
            if (dn != null && dn.Phase != _lastPhase)
            {
                var from = _lastPhase;
                _lastPhase = dn.Phase;
                if (!IsBusy && Random.value < 0.6f && dist < 20f)
                {
                    if (dn.Phase == DayPhase.Evening) SendGameEvent("The sun is starting to set and the sky is turning orange.", "React in one short sentence. No lesson.");
                    else if (dn.Phase == DayPhase.Night && from == DayPhase.Dusk) SendGameEvent("It's getting dark; stars and fireflies are coming out.", "React in one short sentence. No lesson.");
                    else if (dn.Phase == DayPhase.Dawn) SendGameEvent("The sun is just coming up. You both stayed up all night fishing.", "React in one short sentence. No lesson.");
                }
            }

            bool raining = Weather.Instance != null && Weather.Instance.IsRaining;
            if (raining != _wasRaining)
            {
                _wasRaining = raining;
                if (!IsBusy && dist < 20f)
                    SendGameEvent(raining ? "It just started raining softly." : "The rain has stopped.", "React briefly. No lesson.");
            }

            if (SaveSystem.Settings.companionChatter && !IsBusy && Time.time > _nextChatter && dist < 14f)
            {
                _nextChatter = Time.time + Random.Range(170f, 300f);
                if (Time.time - _lastInteraction > 90f)
                    SendGameEvent("It's been quiet for a little while.",
                        "Say one short, friendly thing about what's around you (the sea, the waves, Tangyuan, the weather), like a friend would. No lesson.");
            }
        }

        private void OnFishCaught(CatchResult r)
        {
            body?.Celebrate();
            if (!CanChat || IsBusy) return;
            bool special = r.isNewSpecies || r.species.rarity >= Rarity.Rare || r.isRecord || !r.species.IsFish;
            if (!special && (Time.time - _lastReaction < 25f || Random.value > 0.6f)) return;
            string what = r.species.IsFish
                ? $"The player just caught a {r.species.name} ({r.species.hanzi}), {FishDatabase.WeightText(r.length)}, {FishDatabase.RarityLabel(r.species.rarity).ToLower()}."
                : $"The player just fished up a {r.species.name} ({r.species.hanzi}).";
            if (r.isNewSpecies) what += " It's their first one ever!";
            if (r.isRecord) what += " Their biggest one yet!";
            if (!Inventory.BucketFull) what += " It went into their bucket.";
            SendGameEvent(what, "React with excitement in one or two short sentences. No lesson.");
        }

        private void OnFishEscaped(string reason)
        {
            if (!CanChat || IsBusy || Time.time - _lastReaction < 20f) return;
            if (reason == "snapped" && Random.value < 0.5f)
                SendGameEvent("The player's line just snapped and a fish got away.", "Comfort them in one short sentence. No lesson.");
        }

        private void OnTransaction(ShopkeeperBrain keeper, string english, string chinese)
        {
            if (!CanChat || DistanceToPlayer > 16f || Random.value > 0.7f) return;
            // Let the shopkeeper finish first; Mei chimes in afterwards.
            StartCoroutine(PraiseLater(keeper, english));
        }

        private System.Collections.IEnumerator PraiseLater(ShopkeeperBrain keeper, string english)
        {
            float t = Time.time;
            yield return new WaitForSeconds(0.5f);
            while ((keeper.IsBusy || IsBusy) && Time.time - t < 20f) yield return null;
            while (ShopkeeperBrain.AnyBusy && Time.time - t < 25f) yield return null;
            SendGameEvent($"The player just did this in Mandarin with {keeper.DisplayName}: {english}.", "Praise them briefly and warmly.", evenWhileShopping: true);
        }
    }
}
