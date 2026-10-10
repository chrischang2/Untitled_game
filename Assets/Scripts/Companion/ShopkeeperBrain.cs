using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Language;
using UntitledGame.Progression;

namespace UntitledGame.Companion
{
    /// <summary>
    /// A Mandarin-only shopkeeper. Each customer line is first interpreted by a JSON-constrained LLM call
    /// (buy / ask price / sell fish / yes / no...). The game then applies the real rules (prices, money,
    /// ownership, bucket contents) and tells the shopkeeper what happened in a [Game: ...] note, so the
    /// model narrates the outcome but can never invent prices or give things away.
    /// </summary>
    public class ShopkeeperBrain : DialogueAgent
    {
        public class Offer
        {
            public bool selling;       // true = customer sells fish to the shop
            public string itemId;
            public int quantity = 1;
            public int price;
            public string fishId = "all";
            public bool crabBait;      // 海叔: the fish go in the crab pots as bait (no money now, a bigger next haul)
            public int busTo = -1;     // 张师傅: a ride to this region
            public string english;     // "Bamboo Rod - 120 yuan" for the UI card
        }

        [Serializable]
        private class IntentResult
        {
            public string intent;
            public string item;
            public int quantity;
            public string fish;
        }

        [SerializeField] private string shopId;
        [SerializeField] private float serviceRadius = 4.5f;

        private static readonly List<ShopkeeperBrain> All = new List<ShopkeeperBrain>();
        public static IReadOnlyList<ShopkeeperBrain> Keepers => All;
        public static bool AnyBusy => All.Exists(k => k.IsBusy);
        /// <summary>When the player last spoke to (or nodded at) any shopkeeper.</summary>
        public static float LastCustomerActivity { get; private set; } = -999f;

        public static event Action<ShopkeeperBrain> OfferChanged;
        public static event Action<ShopkeeperBrain, string, string> TransactionDone; // keeper, english summary, chinese summary

        public ShopDef Shop => Catalog.Shop(shopId);
        public override string DisplayName => Shop?.keeperName ?? "老板";
        public override string DisplayNameEnglish => Shop?.keeperEnglish ?? "Shopkeeper";
        public Offer PendingOffer { get; private set; }
        public float ServiceRadius => serviceRadius;

        private bool _greetedThisVisit;
        private float _lastInteraction = -999f;

        /// <summary>The customer said goodbye; the conversation ends once this reply is spoken.</summary>
        public bool ConversationOver { get; private set; }
        public bool InConversation => ShopConversation.Active == this;

        /// <summary>The player pressed E at this stall: greet them (or welcome them back).</summary>
        public void BeginConversation()
        {
            LastTalkedTo = this;
            HushOtherKeepers();
            ConversationOver = false;
            _lastInteraction = Time.time;
            LastCustomerActivity = Time.time;
            bool again = _greetedThisVisit;
            _greetedThisVisit = true;
            if (!CanChat)
            {
                voice?.Say(OfflineLine());
                return;
            }
            Interrupt();
            string greet = again
                ? "[Game: The customer turns back to you to talk again. Say something short and friendly."
                : "[Game: A customer walks up to your stall and wants to talk to you. Greet them with one short, simple sentence (e.g. 欢迎光临！).";
            if (Shop != null && Shop.school && !again)
                greet = "[Game: A student walks up to your test centre. Greet them in one short sentence and tell them simply that here they can 上课, 练习 or 考试.";
            if (Shop != null && Shop.buysFish && !again && DailyRequest.Active)
                greet += $" Also tell them what fish you'd like today, saying exactly: {DailyRequest.Chinese}。";
            if (Shop != null && Shop.busDriver && !again)
                greet += " Then tell them simply where your bus goes. " + BusNote() + (BusTrip.CannotGoOn() != null ? " Don't invite them to get on yet." : "");
            if (Shop != null && Shop.crabber)
            {
                // Coming down to the beach is how you collect the crab money.
                int paid = CrabPots.Claim();
                if (paid > 0)
                {
                    AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.7f);
                    GameEvents.Banner($"Crab pots: +¥{paid}", $"海叔 sold the crabs from your pots and hands you ¥{paid}.\n{CrabPots.Describe()}", true);
                    TransactionDone?.Invoke(this, $"Crab pots: +¥{paid}", $"螃蟹卖了{Catalog.ChineseNumber(paid)}块");
                    greet += $" You've sold the crabs from the customer's pots: hand them {Catalog.ChineseNumber(paid)}块 and say so simply (你的螃蟹卖了…块).";
                }
                else greet += " Their pots have nothing new yet (crabs come in every morning); say so simply.";
            }
            if (!again || _pendingQuestion == null)
            {
                // Each visit the keeper asks the player one question; harder ones as they become friends.
                var (q, level) = Affinity.PickQuestion(shopId);
                if (q != null)
                {
                    _pendingQuestion = q;
                    _pendingQuestionLevel = level;
                    ChatAudit.Write(MemoryKey, $"asks the customer (HSK {level}): {q}");
                    greet += $" Then ask them exactly this question: {q}";
                }
            }
            Ask(greet + "]");
        }

        private string _pendingQuestion;
        private int _pendingQuestionLevel;

        /// <summary>The keeper the player spoke with most recently (Mei uses it for "what does she like?").</summary>
        public static ShopkeeperBrain LastTalkedTo { get; private set; }

        /// <summary>
        /// All keepers share one LLM slot, and two requests on the same slot stalled llama-server for two minutes
        /// (an old keeper's goodbye + the next keeper's greeting). The player has moved on: cut the other one short.
        /// </summary>
        private void HushOtherKeepers()
        {
            foreach (var k in All)
                if (k != this && k.IsBusy) k.Interrupt();
        }

        /// <summary>The question this keeper asked and is waiting for an answer to (shown in the shop window).</summary>
        public string PendingQuestion => _pendingQuestion;
        public KeeperProfile Profile => KeeperProfiles.For(shopId);

        /// <summary>The player pressed E again or walked away.</summary>
        public void EndConversation(bool sayGoodbye)
        {
            if (Shop != null && Shop.school) HskSchool.Abandon();
            bool alreadyClosed = ConversationOver;
            ConversationOver = false;
            if (PendingOffer != null) SetOffer(null);
            if (!sayGoodbye || alreadyClosed || !CanChat) return;
            Interrupt();
            Ask("[Game: The customer is leaving your stall. Say a short goodbye (慢走！/ 再见！).]");
        }

        /// <summary>Stops talking at once (the player walked away): the reply being made and the voice already playing.</summary>
        public void Silence()
        {
            Interrupt();
            voice?.StopAll();
            ChatAudit.Write(MemoryKey, "went quiet: the customer walked away");
        }

        public void Configure(string shop, CharacterVoice v, Transform playerTransform)
        {
            shopId = shop;
            voice = v;
            voice?.MakeLocal(); // a keeper is heard at their stall, not wherever you are
            player = playerTransform;
            llmSlot = 1;
            sampling = new ChatSampling { temperature = 0.6f, top_p = 0.85f, top_k = 20, presence_penalty = 1.0f, max_tokens = 90 };
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            All.Add(this);
        }

        private void Start() => Environment.ShopSign.Create(this);

        protected override void OnDisable()
        {
            base.OnDisable();
            All.Remove(this);
        }

        protected override string BuildSystemPrompt() => ShopPersona.BuildSystemPrompt(Shop);

        protected override string OfflineLine() => "你好！";

        /// <summary>Says a scripted line straight away (no LLM): the teacher's lesson prompts and feedback.</summary>
        public void SayDirect(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            ChatAudit.Write(MemoryKey + "→", "says (scripted): " + line);
            ConversationLog.Add(DisplayName, line, false);
            if (voice != null) voice.Say(line);
        }

        private void SetOffer(Offer o, string ended = "withdrawn")
        {
            if (o != null) ChatAudit.Write(MemoryKey, "OFFER " + o.english);
            else if (PendingOffer != null) ChatAudit.Write(MemoryKey, $"offer {ended}: " + PendingOffer.english);
            PendingOffer = o;
            OfferChanged?.Invoke(this);
        }

        // ------------------------------------------------------------------ conversation

        /// <summary>
        /// The requests this stall understands, as short phrases (hanzi only), for scoring a recording against them
        /// directly: 我想练习 misheard as 我想练气 still counts if the audio fits the phrase well. Only a recording of just
        /// that phrase can score well, so longer sentences are never replaced.
        /// </summary>
        public List<string> SpokenOptions()
        {
            var list = new List<string> { "你好", "谢谢", "再见", "你喜欢什么", "你不喜欢什么" };
            foreach (var q in Affinity.FactQuestions.Values) list.Add(q.question);
            var shop = Shop;
            if (shop != null)
            {
                if (PendingOffer != null) list.AddRange(new[] { "好的", "好", "可以", "要", "不要", "不要了", "算了", "买吧" });
                if (shop.school)
                    foreach (var what in new[] { "上课", "练习", "考试" })
                        list.AddRange(new[] { "我想" + what, "我要" + what, "老师我想" + what, "老师我要" + what });
                if (shop.buysFish) list.AddRange(new[] { "我想卖鱼", "我要卖鱼", "我想卖所有的鱼" });
                if (shop.crabber) list.AddRange(new[] { "我想给你鱼", "给你鱼" });
                if (shop.busDriver)
                {
                    for (int i = 0; i <= Regions.Last; i++) list.Add("我想去" + Regions.Get(i).hanzi);
                    list.AddRange(new[] { "买票", "上车", "走吧" });
                }
                foreach (var def in Economy.ShopStock.Goods(shop))
                {
                    if (string.IsNullOrEmpty(def.hanzi)) continue;
                    list.AddRange(new[] { "我要" + def.hanzi, "我想买" + def.hanzi, def.hanzi + "多少钱" });
                }
            }
            return list.Select(p => new string(p.Where(Pinyin.IsHanzi).ToArray())).Where(p => p.Length > 0).Distinct().Take(120).ToList();
        }

        public override void HandlePlayerUtterance(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            Interrupt();
            var mei = CompanionBrain.Current;
            if (mei != null && mei.IsBusy) mei.Interrupt(); // the customer is talking to me now
            RecordPlayerLine(text);
            _lastInteraction = Time.time;
            LastCustomerActivity = Time.time;
            _greetedThisVisit = true;
            LastTalkedTo = this;
            HushOtherKeepers();

            if (Shop != null && Shop.school && HskSchool.TryHandle(this, text))
            {
                _pendingQuestion = null; // a lesson or test replaces her small-talk question
                return;
            }

            if (LooksEnglish(text))
            {
                ChatAudit.Write(MemoryKey, "understood: English (keeper doesn't speak it)");
                Ask($"[Game: The customer said something in English. You don't understand English at all. " +
                    "Kindly say in very simple Chinese that you only speak Chinese.]\n" + CustomerSaid(text));
                return;
            }
            if (!CanChat)
            {
                voice?.Say(OfflineLine());
                return;
            }
            string question = _pendingQuestion;
            _pendingQuestion = null;
            _pendingQuestionLevel = 0;
            int levelBefore = Affinity.Level(shopId);

            // 1. A gift ("送你…", "这是给你的礼物").
            string note = TryGift(text);
            bool chat = false;
            // 2. A question about them ("你喜欢什么？", "你是哪里人？"): they tell you (and it goes in the journal).
            if (note == null)
            {
                string kind = ShopIntentParser.FactQuestion(text);
                if (kind != null)
                {
                    ChatAudit.Write(MemoryKey, "understood (rules): asks about me: " + kind);
                    note = AnswerFact(kind, text);
                    chat = true;
                }
            }
            // 3. Shop business, read by rules.
            ShopIntentParser.Result parsed = null;
            if (note == null)
            {
                parsed = ShopIntentParser.Parse(Shop, text, PendingOffer != null);
                if (parsed != null)
                {
                    Debug.Log($"[Shop] {DisplayNameEnglish} heard \"{text}\" -> rules: {parsed}");
                    ChatAudit.Write(MemoryKey, "understood (rules): " + parsed);
                    note = Resolve(new IntentResult { intent = parsed.intent, item = parsed.item, quantity = parsed.quantity, fish = parsed.fish });
                    if (string.IsNullOrEmpty(note)) note = NotUnderstoodNote;
                    chat = parsed.intent == "greeting" || parsed.intent == "thanks" || parsed.intent == "goodbye";
                }
            }
            // 4. Their answer to the keeper's question (no slow classifier needed).
            if (note == null && question != null)
            {
                ChatAudit.Write(MemoryKey, $"taken as the answer to: {question}");
                note = $"[Game: You asked the customer: {question} This is their answer. React warmly and naturally in one or two short sentences.]";
                chat = true;
            }

            // Friendship goes up when the facts, the gift and the HSK test for the next level are all done.
            int levelNow = Affinity.Level(shopId);
            note = (note ?? "") + (levelNow > levelBefore ? LevelUpNote(levelNow) : "") + NewWordSurprise(text);

            if (note.Trim().Length == 0 || note.StartsWith("\n"))
            {
                // 5. Unclear: ask the AI classifier (small talk ends up as a friendly chat).
                StartCoroutine(ClassifyThenReply(text, note));
                return;
            }
            Ask(note + "\n" + CustomerSaid(text));
        }

        private const string ChatNote =
            "[Game: The customer is chatting with you, not buying. Reply naturally and in character in one or two short sentences " +
            "with simple HSK 1-3 words. You may ask a simple question back. Don't offer goods or name prices.]";

        /// <summary>Chance that using a new word with a keeper earns a little unexpected present (at most once a day each).</summary>
        public const float NewWordSurpriseChance = 0.15f;
        public static bool ForceSurprise;

        /// <summary>
        /// Every HSK word the player uses with this keeper for the first time is noted; now and then they're so pleased
        /// they hand over a small present. Never promised, so it stays a nice surprise.
        /// </summary>
        private string NewWordSurprise(string text)
        {
            var s = Affinity.State(shopId);
            s.wordsHeard ??= new List<string>();
            var fresh = HskVocab.Segment(Pinyin.ToSimplified(text)).Select(w => w.word).Where(w => w.Length > 1 || HskVocab.LevelOf(w) > 1)
                .Distinct().Where(w => !s.wordsHeard.Contains(w)).ToList();
            if (fresh.Count == 0) return "";
            s.wordsHeard.AddRange(fresh);
            if (s.surpriseDay == SaveSystem.Data.day || !(ForceSurprise || UnityEngine.Random.value < NewWordSurpriseChance)) return "";
            s.surpriseDay = SaveSystem.Data.day;
            string word = fresh.OrderByDescending(HskVocab.LevelOf).First();
            string present = Surprise.Give(DisplayNameEnglish, 1);
            GameEvents.Toast($"{DisplayName} liked hearing you say 「{word}」 {Pinyin.Of(word)} and gives you a little something: {present}.", 4.5f);
            ChatAudit.Write(MemoryKey, $"surprise for using the new word {word}: {present}");
            return $"\n[Game: You're pleased the customer used the word 「{word}」 so well. Hand them a small present as a surprise (这个送给你！).]";
        }

        /// <summary>The keeper's line when the friendship goes up (the banner is shown by the UI).</summary>
        private string LevelUpNote(int level)
        {
            if (level >= Affinity.MaxLevel && Shop != null && !Shop.busDriver)
                return $"\n[Game: You and this customer are now old friends (老朋友)! Tell them warmly, and that as a friend you'll {PerkPromise(Shop.role)}.]";
            string unlock = ShopStock.Goods(Shop).Any(i => i.minAffinity == level) ? " Now there are new things you'll sell them." : "";
            return $"\n[Game: You feel closer to this customer now ({Affinity.LevelHanzi[level]}). Say warmly that you're happy to be friends.{unlock}]";
        }

        /// <summary>What the keeper promises at the highest friendship (their perk), in simple Chinese.</summary>
        private static string PerkPromise(string role) => role switch
        {
            "tackle" => "leave them a pack of your best bait every morning (每天早上送你鱼饵)",
            "fish" => "pay them more for their fish (给你更多钱)",
            "furniture" => "make them something special for their house (送你一件家具)",
            "pet" => "help their cat bring more bait (猫会带来更多鱼饵)",
            "books" => "tell them where the rare fish are (告诉你哪里有很少见的鱼)",
            "gym" => "train them for less money (便宜一点)",
            "colours" => "give them a special colour for their boat (送你一个颜色)",
            "gifts" => "give them a little present every morning (每天送你小礼物)",
            "school" => "always have a present after a good lesson (上完课送你礼物)",
            "crabber" => "fill their crab pots fuller (螃蟹更多)",
            _ => "help them",
        };

        /// <summary>A short note of what's left for the next friendship level (toasts after learning something or a gift).</summary>
        private string NextStepText()
        {
            int next = Affinity.Level(shopId) + 1;
            if (next > Affinity.MaxLevel) return "";
            var left = Affinity.Requirements(shopId, next).Where(r => !r.done).Select(r => r.kind == "fact" ? Affinity.FactQuestions[r.factId].question : r.text).ToList();
            return left.Count == 0 ? "" : $"  <size=18>(for {Affinity.LevelHanzi[next]}: {string.Join(", ", left)})</size>";
        }

        /// <summary>"送你…" / "这是给你的礼物": takes the gift from the bag and reacts. Null when it isn't about a gift.</summary>
        private string TryGift(string text)
        {
            string s = Pinyin.ToSimplified(text);
            bool giftWords = s.Contains("送") || s.Contains("礼物") || s.Contains("给你");
            if (!giftWords || s.Contains("买")) return null;
            var owned = Inventory.Owned().Where(x => x.def.category == ItemCategory.Gift && x.count > 0).Select(x => x.def).ToList();
            var gift = owned.FirstOrDefault(g => ShopIntentParser.Mentions(s, g.hanzi));
            if (gift == null && owned.Count == 1 && (s.Contains("礼物") || s.Contains("送"))) gift = owned[0];
            if (gift == null)
            {
                if (!s.Contains("礼物") && !s.Contains("送")) return null; // "给你" alone is usually paying
                ChatAudit.Write(MemoryKey, "gift: they don't have that gift with them");
                return owned.Count == 0
                    ? "[Game: The customer talks about giving you a gift, but they don't have any gift with them. Smile and say it's fine.]"
                    : $"[Game: The customer wants to give you a gift but it's not clear which one. Ask them which (they have: {string.Join("、", owned.Select(g => g.hanzi))}).]";
            }
            if (Affinity.GaveGiftToday(shopId))
            {
                ChatAudit.Write(MemoryKey, "gift refused: already got one today");
                return $"[Game: The customer wants to give you {gift.hanzi}, but they already gave you a gift today. Thank them and kindly say one gift a day is more than enough.]";
            }
            Inventory.Remove(gift.id, 1);
            var p = Profile;
            bool liked = p != null && p.likes.Contains(gift.hanzi);
            bool disliked = p != null && p.dislikes.Contains(gift.hanzi);
            bool counted = Affinity.RecordGift(shopId, disliked);
            if (liked) Affinity.Learn(shopId, "like:" + gift.hanzi, "gift reaction");
            if (disliked) Affinity.Learn(shopId, "dislike:" + gift.hanzi, "gift reaction");
            ConversationLog.Add("You", $"(gives {DisplayName} {gift.hanzi})", true);
            // Likes and dislikes stay in Chinese (the journal shows 喜欢 / 不喜欢 with pinyin; ask Mei what they mean).
            string named = $"{gift.hanzi} {Pinyin.Of(gift.hanzi)}";
            string counts = counted ? " The gift counts toward your friendship." : disliked ? " A gift they don't like doesn't count." : " (You've already given a gift for this level.)";
            GameEvents.Toast((liked ? $"{DisplayName} 喜欢 {named}!" : disliked ? $"{DisplayName} 不喜欢 {named}..." : $"{DisplayName} thanks you for the gift.") + counts + NextStepText(), 5f);
            if (liked) return $"[Game: The customer gives you {gift.hanzi} as a gift. You LOVE it! Thank them happily and say why: {p.likeReason}]";
            if (disliked) return $"[Game: The customer gives you {gift.hanzi} as a gift, but you really don't like it. Thank them politely but honestly say you don't like it: {p.dislikeReason}]";
            return $"[Game: The customer gives you {gift.hanzi} as a gift. Thank them politely; it's nice, though not your favourite.]";
        }

        /// <summary>The customer asks about you (likes, dislikes, hobby, hometown, family).</summary>
        private string AnswerFact(string kind, string text)
        {
            var p = Profile;
            if (p == null) return ChatNote;
            // 刘奶奶 knows what everyone likes: "老王喜欢什么？"
            if (Shop != null && Shop.role == "gifts" && (kind == "like" || kind == "dislike"))
            {
                var other = ShopkeeperBrain.Keepers.FirstOrDefault(k => k != this && ShopIntentParser.Mentions(Pinyin.ToSimplified(text), k.DisplayName));
                // (only the keepers here are active, so she only knows her own town)
                if (other != null && other.Profile != null)
                {
                    var list = kind == "like" ? other.Profile.likes : other.Profile.dislikes;
                    foreach (var g in list) Affinity.Learn(other.shopId, kind + ":" + g, "told by 刘奶奶");
                    GameEvents.Toast($"Journal: you learned what {other.DisplayName} {(kind == "like" ? "likes" : "dislikes")}.", 3f);
                    return $"[Game: The customer asks what {other.DisplayName} {(kind == "like" ? "likes" : "doesn't like")}. You know! Tell them: {other.DisplayName}{(kind == "like" ? "喜欢" : "不喜欢")}{string.Join("和", list)}。]";
                }
            }
            int lvl = Affinity.Level(shopId);
            KeeperFact fact;
            string topic;
            switch (kind)
            {
                case "like":
                    topic = "what you like";
                    fact = p.likes.Select(l => p.Fact("like:" + l)).FirstOrDefault(f => !Affinity.Knows(shopId, f.id)) ?? p.Fact("like:" + p.likes[0]);
                    break;
                case "dislike":
                    topic = "what you don't like";
                    fact = p.dislikes.Select(d => p.Fact("dislike:" + d)).FirstOrDefault(f => !Affinity.Knows(shopId, f.id)) ?? p.Fact("dislike:" + p.dislikes[0]);
                    break;
                default:
                    topic = kind switch
                    {
                        "hometown" => "where you are from",
                        "siblings" => "whether you have brothers and sisters",
                        "hobby" => "your hobbies",
                        "food" => "what food you like",
                        "family" => "whether you're married and have children",
                        "birthday" => "your birthday",
                        "dream" => "what you hope to do in the future",
                        _ => "yourself",
                    };
                    fact = p.Fact(kind);
                    break;
            }
            if (fact == null) return ChatNote;
            if (fact.minLevel > lvl)
            {
                ChatAudit.Write(MemoryKey, $"won't share {fact.id} yet (needs friendship {fact.minLevel})");
                GameEvents.Toast($"{DisplayName} will tell you that once you're {Affinity.LevelHanzi[fact.minLevel]} ({Affinity.LevelEnglish[fact.minLevel]}).{NextStepText()}", 4.5f);
                return $"[Game: The customer asks about {topic}. You have only just met, so smile and say you'll tell them when you know each other a little better.]";
            }
            if (Affinity.Learn(shopId, fact.id, "asked"))
                GameEvents.Toast($"Journal: you learned something about {DisplayName} (J, People page).{NextStepText()}", 4.5f);
            return $"[Game: The customer asks about {topic}. Tell them in one or two short sentences, keeping it this simple: {fact.chinese}]";
        }

        private const string NotUnderstoodNote =
            "[Game: You aren't sure what the customer wants. Ask simply what they would like. Don't offer anything or name prices yourself.]";

        public static bool LooksEnglish(string text)
        {
            int latin = 0, hanzi = 0;
            foreach (char c in text)
            {
                if (Pinyin.IsHanzi(c)) hanzi++;
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            }
            return hanzi == 0 && latin >= 3;
        }

        private IEnumerator ClassifyThenReply(string text, string extraNote = "")
        {
            SetState(CompanionState.Thinking);
            var services = LocalAIServices.Instance;
            var messages = new List<ChatMessage>
            {
                new ChatMessage("system", ShopPersona.BuildClassifierPrompt(Shop, DescribeOffer())),
                new ChatMessage("user", $"Customer said: \"{text}\""),
            };
            string json = null, error = null;
            ChatAudit.Write(MemoryKey, "rules didn't understand; asking the AI classifier");
            float t0 = Time.realtimeSinceStartup;
            yield return OpenAIStreamingClient.CompleteJson(services.LlmBaseUrl, services.LlmModelName, messages,
                ShopPersona.ClassifierSchema(Shop), 60, 2, (j, e) => { json = j; error = e; });

            IntentResult intent = null;
            if (error == null && !string.IsNullOrEmpty(json))
            {
                try { intent = JsonUtility.FromJson<IntentResult>(json); }
                catch (Exception e) { Debug.LogWarning($"[{DisplayNameEnglish}] Bad intent JSON {json}: {e.Message}"); }
            }
            else if (error != null) Debug.LogWarning($"[{DisplayNameEnglish}] Intent call failed: {error}");

            Debug.Log($"[Shop] {DisplayNameEnglish} heard \"{text}\" -> {json}");
            ChatAudit.Write(MemoryKey, $"understood (AI classifier, {ChatAudit.Seconds(Time.realtimeSinceStartup - t0)})" + (error != null ? " ERROR " + error : ""),
                string.IsNullOrEmpty(json) ? "(no result)" : json.Replace("\n", " ").Replace("  ", " "));
            string note = intent != null ? Resolve(intent) : "";
            if (string.IsNullOrEmpty(note)) note = intent == null || intent.intent == "other" ? ChatNote : NotUnderstoodNote;
            Ask(note + extraNote + "\n" + CustomerSaid(text));
        }

        /// <summary>The player's words, clearly marked as the customer's (keepers used to continue them as their own line).</summary>
        private static string CustomerSaid(string text) => $"顾客说：「{text}」";

        private int _browseIndex;

        /// <summary>What the driver knows about the next leg: where it goes, the ticket and what's still missing (for the keeper).</summary>
        private static string BusNote()
        {
            string back = BusTrip.Previous >= 0 ? $" You can also take them back to {Regions.Get(BusTrip.Previous).hanzi} for free (回{Regions.Get(BusTrip.Previous).hanzi})." : "";
            if (BusTrip.Next < 0)
                return $"Your bus only goes as far as here, {Regions.Here.hanzi}, for now: the road further on is still being built (路还没修好).{back}";
            var to = Regions.Get(BusTrip.Next);
            string where = to.id == "desert" ? "沙漠旁边的海边" : to.id == "snow" ? "很冷的海边，有雪有冰" : to.id == "mars" ? "太空里的火星，那里的海是紫色的（the bus can fly there）" : "the next town on the coast";
            int fare = BusTrip.Fare();
            string ticket = BusTrip.NextPaid
                ? "They've already bought this ticket before, so it's free."
                : $"The ticket costs {Catalog.ChineseNumber(fare)}块 (one time only; after that the ride there is free). They have {Catalog.ChineseNumber(Inventory.Money)}块" +
                  (Inventory.Money < fare ? ", which isn't enough yet." : ".") + " They can say 我想去" + to.hanzi + " or 买票.";
            string hsk = Hsk.Level < BusTrip.HskToLeave(Regions.Current)
                ? $" Passengers to {to.hanzi} must first pass 汉语水平考试{Catalog.ChineseNumber(BusTrip.HskToLeave(Regions.Current))}级 (HSK {BusTrip.HskToLeave(Regions.Current)}) at 高老师's 考试中心."
                : "";
            return $"Your bus goes to {to.hanzi} ({to.english}), {where}. {ticket}{hsk}{back}";
        }

        /// <summary>"What do you sell?": the next few goods with their prices (a different few each time).</summary>
        private string BrowseNote()
        {
            var shop = Shop;
            if (shop.busDriver)
                return $"[Game: The customer asks about your bus. {BusNote()} Say it simply.]";
            if (shop.school)
                return "[Game: The student asks what you do here. Tell them simply: they can say 我想上课 for a lesson, 我想练习 to practise, or 我想考试 to take the HSK test.]";
            if (shop.crabber)
                return "[Game: The customer asks what you do. Say simply: you look after their crab pots (螃蟹笼) in the sea; every morning the crabs come in and " +
                       "you sell them, and they come here to get the money. You also sell pot upgrades, and if they give you fish (给你鱼), you put them in the pots and the next haul has more crabs. The pots are hauled every 15 minutes.]";
            if (shop.buysFish)
            {
                string cards = " Also say that everything goes on your scale, and the more they bring at once, the more you pay for each fish.";
                return Inventory.BucketCount > 0
                    ? $"[Game: The customer asks what you buy. Say you buy all kinds of fish from the sea, and bigger (heavier) or rarer fish pay more. They have {Inventory.DescribeBucketChinese()} in their bucket.{cards}]"
                    : $"[Game: The customer asks what you buy. Say you buy all kinds of fish from the sea, and bigger (heavier) or rarer fish pay more. Their bucket is empty.{cards}]";
            }
            var items = ShopStock.Goods(shop).ToList();
            if (items.Count == 0) return "";
            var some = new List<string>();
            for (int k = 0; k < Mathf.Min(3, items.Count); k++)
            {
                var i = items[(_browseIndex + k) % items.Count];
                some.Add($"{i.hanzi} {Catalog.ChineseNumber(Catalog.PriceOf(i))}块" + (i.packSize > 1 ? $"（一包{Catalog.ChineseNumber(i.packSize)}个）" : ""));
            }
            _browseIndex = (_browseIndex + 3) % items.Count;
            return $"[Game: The customer asks what you sell. Tell them about these, with prices: {string.Join("、", some)}. Then ask what they would like.]";
        }

        private string DescribeOffer()
        {
            var o = PendingOffer;
            if (o == null) return "none";
            if (o.busTo >= 0) return $"a bus ride to {Regions.Get(o.busTo).hanzi}";
            if (o.selling) return $"the shop buys the customer's fish for {o.price} yuan";
            return $"customer buys {o.quantity} x {Catalog.Get(o.itemId)?.hanzi} for {o.price} yuan";
        }

        private string Resolve(IntentResult r)
        {
            var shop = Shop;
            ItemDef item = shop.items.Contains(r.item) ? Catalog.Get(r.item) : null;
            int qty = Mathf.Clamp(r.quantity, 1, 20);
            if (shop.busDriver)
            {
                string bus = ResolveBus(r);
                if (bus != null) return bus;
            }
            // Goods of a later tier aren't on sale here yet (a surprise for the next stop: ShopStock).
            if (item != null && !ShopStock.Revealed(item) && (r.intent == "buy" || r.intent == "ask_price"))
            {
                SetOffer(null);
                ChatAudit.Write(MemoryKey, $"asked about {item.english}, which isn't on sale here yet");
                var goods = ShopStock.Goods(shop).Take(3).Select(i => i.hanzi).ToList();
                return "[Game: The customer asks for something you don't have. Say simply that you don't sell that" +
                       (goods.Count > 0 ? $", and mention what you do have ({string.Join("、", goods)})" : "") + ". Never hint that you might have it later.]";
            }
            switch (r.intent)
            {
                case "buy":
                case "ask_price":
                {
                    if (item == null)
                    {
                        return r.intent == "buy"
                            ? "[Game: The customer wants to buy something, but it's not clear which item. Ask them which one, mentioning two or three things you sell.]"
                            : "[Game: The customer asks about prices but not which item. Tell them the prices of two or three things you sell.]";
                    }
                    if (item.unique) qty = 1;
                    int friendship = Affinity.Level(shopId);
                    if (item.category == ItemCategory.Boat && item.id != "boat" && !Inventory.Owns("boat"))
                    {
                        SetOffer(null);
                        return $"[Game: The customer asks about {item.hanzi}, but they don't use your boat yet. Tell them to rent the 小船 first.]";
                    }
                    if (item.minAffinity > friendship)
                    {
                        SetOffer(null);
                        ChatAudit.Write(MemoryKey, $"won't sell {item.english} yet: needs friendship {item.minAffinity}, have {friendship}");
                        return $"[Game: The customer asks about {item.hanzi}. You only sell that to people you know well, and you don't know them well enough yet. " +
                               "Tell them kindly; maybe later, when you are better friends.]";
                    }
                    if (item.minHsk > Hsk.Level)
                    {
                        SetOffer(null);
                        ChatAudit.Write(MemoryKey, $"won't sell {item.english} yet: needs HSK {item.minHsk}, passed {Hsk.Level}");
                        GameEvents.Toast($"{item.english} needs the HSK {item.minHsk} test: take lessons and the test with Teacher Gao at the test centre.", 4.5f);
                        return $"[Game: The customer asks about {item.hanzi}. You only sell that to customers who have passed 汉语水平考试{Catalog.ChineseNumber(item.minHsk)}级 (HSK {item.minHsk}), " +
                               "and they haven't yet. Tell them kindly, and that 高老师 at the 考试中心 gives lessons and the test.]";
                    }
                    if (item.category == ItemCategory.Upgrade)
                    {
                        // One level at a time; each level opens with lessons of the matching HSK level (Hsk.UpgradeLevelOpen).
                        qty = 1;
                        if (PlayerStats.Level(item.stat) < PlayerStats.MaxLevel && !PlayerStats.CanTrainNext(item.stat))
                        {
                            SetOffer(null);
                            string needs = PlayerStats.NextNeeds(item.stat);
                            ChatAudit.Write(MemoryKey, $"won't sell {item.stat} level {PlayerStats.Level(item.stat) + 1}: needs {needs}");
                            if (PlayerStats.NextHidden(item.stat))
                                return $"[Game: The customer wants more {item.hanzi}, but they've done everything you offer here for now. Praise them warmly; don't promise anything more.]";
                            GameEvents.Toast($"The next {PlayerStats.StatName(item.stat).ToLower()} level needs {needs} with Teacher Gao at the test centre.", 4.5f);
                            return $"[Game: The customer wants more {item.hanzi}, but the next level is for people who have studied more Chinese at 高老师's 考试中心 " +
                                   $"(they need {needs}). Say so simply and encourage them to study (加油！).]";
                        }
                    }
                    Inventory.CanBuy(item.id, qty, out var reason, out int cost);
                    string what = Catalog.Counted(item, qty);
                    string pack = item.packSize > 1 ? $" (one pack has {item.packSize})" : "";
                    switch (reason)
                    {
                        case BuyResult.AlreadyOwned:
                            SetOffer(null);
                            if (item.category == ItemCategory.Upgrade)
                                return $"[Game: The customer wants {item.hanzi}, but they've already finished all of it. Praise them warmly.]";
                            return item.category == ItemCategory.Book
                                ? $"[Game: The customer asked about the book {item.hanzi}, but they already have it. Tell them kindly, and maybe suggest another book.]"
                                : $"[Game: The customer asked about {item.hanzi}, but they already own one. Tell them kindly.]";
                        case BuyResult.NoRoom:
                            SetOffer(null);
                            ChatAudit.Write(MemoryKey, $"bag full: no room for {item.english}");
                            GameEvents.Toast($"Your bag is full ({Inventory.SlotsUsed}/{Inventory.SlotCapacity} slots). Sell or use something first, or buy a bigger bucket from Old Wang.", 4.5f);
                            return $"[Game: The customer wants {what}, but they can't carry any more: their bag is full. Tell them kindly to come back when they have room.]";
                        case BuyResult.NotEnoughMoney:
                            SetOffer(null);
                            return $"[Game: The customer asked about {what}. It costs {Catalog.ChineseNumber(cost)}块{pack}, but they only have {Catalog.ChineseNumber(Inventory.Money)}块. Tell them the price and kindly that it's not enough money.]";
                        case BuyResult.Ok:
                            SetOffer(new Offer { itemId = item.id, quantity = qty, price = cost, english = $"{(qty > 1 ? qty + " x " : "")}{item.english} — ¥{cost}" });
                            return $"[Game: The customer asked for {what}. It costs {Catalog.ChineseNumber(cost)}块{pack}. Tell them the price and ask if they want it (要吗？).]";
                    }
                    return "";
                }
                case "sell_fish" when shop.crabber:
                {
                    if (Inventory.BucketCount == 0)
                    {
                        SetOffer(null);
                        return "[Game: The customer wants to give you fish for the crab pots, but their bucket is empty. Tell them kindly to come back after fishing.]";
                    }
                    var species = FishDatabase.Get(r.fish);
                    var fish = FishSale.Selection(species != null && Inventory.Bucket.Any(b => b.species.id == species.id) ? species.id : "all");
                    int extra = Mathf.FloorToInt(CrabPots.BaitValue(fish));
                    if (extra <= 0)
                    {
                        SetOffer(null);
                        return "[Game: The customer wants to give you fish for the crab pots, but the pots already have all the bait they can use today. Thank them and say: tomorrow.]";
                    }
                    SetOffer(new Offer { selling = true, crabBait = true, fishId = fish.Count == Inventory.BucketCount ? "all" : fish[0].speciesId, price = extra,
                        english = $"Put {fish.Count} fish in the crab pots — +¥{extra} in the next haul" });
                    return $"[Game: The customer offers you {fish.Count}条鱼 as bait for their crab pots. With them, the next haul of crabs will be worth about {Catalog.ChineseNumber(extra)}块 more. " +
                           "Say that simply and ask if they want to put them in (要放吗？).]";
                }
                case "sell_fish":
                {
                    if (!shop.buysFish)
                        return "[Game: The customer wants to sell fish. You don't buy fish here; tell them to go to 陈阿姨's 寿司店 (sushi bar) nearby.]";
                    if (Inventory.BucketCount == 0)
                    {
                        SetOffer(null);
                        return "[Game: The customer wants to sell fish, but their bucket is empty. Tell them kindly to come back after fishing.]";
                    }
                    var species = FishDatabase.Get(r.fish);
                    bool some = species != null && Inventory.Bucket.Any(b => b.species.id == species.id);
                    string desc = some
                        ? $"{Inventory.Bucket.Count(b => b.species.id == species.id)}条{species.hanzi}"
                        : Inventory.DescribeBucketChinese();
                    int count = some ? Inventory.Bucket.Count(b => b.species.id == species.id) : Inventory.BucketCount;
                    // Everything goes on her scale: the more weight, the bigger the multiplier (and friends get 5% a level).
                    var quote = FishSale.For(FishSale.Selection(some ? species.id : "all"), Affinity.Level(shopId));
                    int value = quote.total;
                    SetOffer(new Offer { selling = true, fishId = some ? species.id : "all", price = value,
                        english = $"Sell {count} catch{(count == 1 ? "" : "es")} ({quote.fills} bar{(quote.fills == 1 ? "" : "s")}, x{quote.multiplier:0.00}) — +¥{value}" });
                    string scale = quote.fills > 0 ? $" Together they fill your scale's bar {quote.fills} time{(quote.fills == 1 ? "" : "s")}, so you add a bonus (x{quote.multiplier:0.00})." : "";
                    return $"[Game: The customer wants to sell {desc}. You put them on your scale.{scale} You offer {Catalog.ChineseNumber(value)}块 in total. " +
                           "Say what you see (you're a sushi chef: say what you'd make with them), the total, and ask if that's OK (可以吗？).]";
                }
                case "confirm":
                    return PendingOffer != null ? Execute() : "";
                case "decline":
                    if (PendingOffer == null) return "";
                    SetOffer(null, "declined");
                    return "[Game: The customer doesn't want it after all. Say that's no problem.]";
                case "greeting":
                    return "[Game: The customer greets you. Welcome them warmly (欢迎光临).]";
                case "browse":
                    return BrowseNote();
                case "thanks":
                    return "[Game: The customer thanks you. Reply warmly and briefly (不客气).]";
                case "goodbye":
                    SetOffer(null);
                    if (InConversation) ConversationOver = true;
                    return "[Game: The customer is leaving. Say goodbye (慢走 / 再见).]";
                default:
                    return "";
            }
        }

        /// <summary>The bus driver's business: tickets, rides on and back. Null for anything else (small talk...).</summary>
        private string ResolveBus(IntentResult r)
        {
            switch (r.intent)
            {
                case "ask_price":
                case "buy":
                    return $"[Game: The customer asks about the bus and the ticket. {BusNote()} Say it simply.]";
                case "sell_fish":
                    return $"[Game: The customer offers you fish. You don't take fish: the ticket is paid with money. {BusNote()} Say so simply and kindly.]";
                case "travel":
                {
                    bool back = r.item == "back";
                    if (back)
                    {
                        if (BusTrip.Previous < 0)
                        {
                            SetOffer(null);
                            return $"[Game: The customer wants to go back, but this is the first stop on your route. {BusNote()} Say so simply.]";
                        }
                        var prev = Regions.Get(BusTrip.Previous);
                        if (PendingOffer != null && PendingOffer.busTo == BusTrip.Previous) return Execute();
                        SetOffer(new Offer { busTo = BusTrip.Previous, english = $"Ride back to {prev.english} ({prev.hanzi}) — free" });
                        return $"[Game: The customer wants to go back to {prev.hanzi}. That's free. Ask if they want to get on now (现在走吗？).]";
                    }
                    string why = BusTrip.CannotGoOn();
                    if (why != null)
                    {
                        SetOffer(null);
                        ChatAudit.Write(MemoryKey, "can't ride on: " + why);
                        if (why == "hsk") GameEvents.Toast($"You need the HSK {BusTrip.HskToLeave(Regions.Current)} test before you can ride on.", 4.5f);
                        if (why == "money") GameEvents.Toast(BusTrip.Describe(), 6f);
                        return $"[Game: The customer wants to ride on, but they can't yet. {BusNote()} Say what they still need, simply and kindly.]";
                    }
                    var to = Regions.Get(BusTrip.Next);
                    if (PendingOffer != null && PendingOffer.busTo == BusTrip.Next) return Execute();
                    int price = BusTrip.NextPaid ? 0 : BusTrip.Fare();
                    SetOffer(new Offer { busTo = BusTrip.Next, price = price,
                        english = price > 0 ? $"Bus ticket to {to.english} ({to.hanzi}) — ¥{price}" : $"Ride the bus to {to.english} ({to.hanzi}) — paid" });
                    return price > 0
                        ? $"[Game: The customer wants to ride to {to.hanzi}. The ticket costs {Catalog.ChineseNumber(price)}块 (only this once). Tell them the price and ask if they want to buy it and go now (要买票吗？).]"
                        : $"[Game: The customer wants to ride to {to.hanzi}; they've paid before. Ask if they want to leave now (现在走吗？).]";
                }
                default:
                    return null;
            }
        }

        /// <summary>Completes the pending offer and returns the note describing it.</summary>
        private string Execute()
        {
            var o = PendingOffer;
            SetOffer(null, "accepted");
            if (o == null) return "";
            if (o.busTo >= 0)
            {
                var to = Regions.Get(o.busTo);
                if (o.busTo > Regions.Current && !BusTrip.BuyTicket())
                {
                    ChatAudit.Write(MemoryKey, "ticket FAILED: not enough money");
                    return $"[Game: The customer doesn't have enough money for the ticket any more ({Catalog.ChineseNumber(BusTrip.Fare())}块). Tell them kindly.]";
                }
                if (o.price > 0)
                {
                    AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.7f);
                    GameEvents.Toast($"Bus ticket to {to.english}: -¥{o.price}. Riding back and forth on this leg is free from now on.", 4.5f);
                }
                ChatAudit.Write(MemoryKey, $"RIDE to {to.english}");
                TransactionDone?.Invoke(this, $"Bus to {to.english}", $"坐车去{to.hanzi}");
                if (BusStop.Instance != null) BusStop.Instance.Ride(o.busTo);
                else BusTrip.Arrive(o.busTo);
                return $"[Game: The customer gets on your bus to {to.hanzi}. Say 上车吧！ and that you're leaving now, in one short sentence.]";
            }
            if (o.selling && o.crabBait)
            {
                var fish = FishSale.Selection(o.fishId);
                float extra = CrabPots.AddBait(fish);
                string en = $"Put {fish.Count} fish in the crab pots (+¥{extra:0} in the next haul)";
                ChatAudit.Write(MemoryKey, en);
                AudioManager.Instance?.PlaySfx("SFX/water_small", 0.6f);
                GameEvents.Toast($"{en}. It comes in with the next haul (every 15 minutes).", 4f);
                TransactionDone?.Invoke(this, en, $"把{Catalog.ChineseNumber(fish.Count)}条鱼放进了螃蟹笼");
                return $"[Game: Done: you put the customer's {fish.Count}条鱼 in their crab pots. Tell them the next haul will have more crabs (下次螃蟹更多！).]";
            }
            if (o.selling)
            {
                bool fulfils = DailyRequest.CanFulfil && (o.fishId == "all" || o.fishId == DailyRequest.Fish.id);
                var quote = FishSale.For(FishSale.Selection(o.fishId), Affinity.Level(shopId));
                o.price = Mathf.Max(o.price, quote.total);
                var (count, money) = o.fishId == "all"
                    ? (Inventory.BucketCount, Inventory.SellAllFish())
                    : Inventory.SellSpecies(o.fishId);
                if (o.price > money)
                {
                    Inventory.Earn(o.price - money); // the scale's multiplier and the friendship bonus
                    money = o.price;
                }
                FishSale.Announce(quote);
                string en = $"Sold {count} catch{(count == 1 ? "" : "es")} (+¥{money})";
                AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.7f);
                ChatAudit.Write(MemoryKey, $"SALE {en}; player now has ¥{Inventory.Money}");
                TransactionDone?.Invoke(this, en, $"卖了{Catalog.ChineseNumber(count)}条鱼，得到{Catalog.ChineseNumber(money)}块");
                if (fulfils)
                {
                    string surprise = DailyRequest.Fulfil();
                    GameEvents.Banner("Just what Auntie Chen wanted!", $"She was hoping for {DailyRequest.Count} {DailyRequest.Fish.name.ToLower()} today, and gives you a little surprise: {surprise}.", true);
                    return $"[Game: Deal done! You paid the customer {Catalog.ChineseNumber(money)}块 for their fish, and it included the {DailyRequest.Fish.hanzi} you wanted today! " +
                           "You're delighted: thank them warmly and give them a little present to say thanks (这是给你的小礼物！).]";
                }
                return $"[Game: Deal done! You paid the customer {Catalog.ChineseNumber(money)}块 for their fish. Hand over the money and thank them.]";
            }
            var item = Catalog.Get(o.itemId);
            var result = Inventory.Buy(o.itemId, o.quantity);
            if (result != BuyResult.Ok) ChatAudit.Write(MemoryKey, $"sale FAILED: {result}");
            if (result != BuyResult.Ok)
                return $"[Game: The sale failed ({result}). Apologise simply.]";
            string english = $"Bought {(o.quantity > 1 ? o.quantity + " x " : "")}{item.english} (-¥{o.price})";
            AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.7f);
            ChatAudit.Write(MemoryKey, $"SALE {english}; player now has ¥{Inventory.Money}");
            TransactionDone?.Invoke(this, english, $"买了{item.hanzi}");
            if (item.category == ItemCategory.Upgrade)
            {
                GameEvents.Toast($"{item.english} with Coach Wu: {PlayerStats.StatName(item.stat)} is now level {PlayerStats.Level(item.stat)}/{PlayerStats.MaxLevel} ({PlayerStats.StatValue(item.stat)}).", 4.5f);
                return $"[Game: The customer paid {Catalog.ChineseNumber(o.price)}块 and you gave them a hard {item.hanzi} session. They improved (level {PlayerStats.Level(item.stat)}). Cheer them on in one or two short sentences (加油！).]";
            }
            if (item.category == ItemCategory.Line)
                GameEvents.Toast($"New line: {item.english}. Heavier fish can bite now (you always fish with your strongest line).", 4.5f);
            if (item.category == ItemCategory.Cosmetic)
            {
                UntitledGame.Home.Cosmetics.OnBought(item);
                GameEvents.Toast($"{item.english}: done! (Switch cosmetics any time in the bag.)", 4f);
                return $"[Game: Deal done! The customer paid {Catalog.ChineseNumber(o.price)}块 for {item.hanzi}. You've already put it on. Say how beautiful the colour looks!]";
            }
            if (item.category == ItemCategory.Bed)
            {
                GameEvents.Toast($"Master Li will deliver your {item.english.ToLower()} to the house. From tomorrow you'll have {Progression.Energy.Max:0} energy each morning.", 5f);
                return $"[Game: Deal done! The customer paid {Catalog.ChineseNumber(o.price)}块 for the {item.hanzi}. Say you'll carry it to their house today, and that they'll sleep very well.]";
            }
            if (item.bagSlots > 0)
                GameEvents.Toast($"{item.english}: your bag now holds {Inventory.SlotCapacity} slots.", 4f);
            if (item.category == ItemCategory.Boat)
            {
                GameEvents.Toast(item.id == "boat"
                    ? "You can use Old Wang's rowboat now: press F at the boat by the dock. It can go 25 m out before the currents push it back."
                    : $"{item.english}: your boat can now go {Fishing.Rowboat.Range:0} m out.", 5f);
                return $"[Game: Deal done! The customer paid {Catalog.ChineseNumber(o.price)}块 to use your little boat. It's tied up by the dock. Tell them to be careful on the water and wish them big fish.]";
            }
            if (item.category == ItemCategory.Book)
            {
                GameEvents.Toast($"You bought \"{item.english}\". Open your bag (I) to read it and discover new fish.", 5f);
                return $"[Game: Deal done! The customer paid {Catalog.ChineseNumber(o.price)}块 for the book {item.hanzi}. Hand it over, thank them and say something nice about the book.]";
            }
            string hint = item.placeable ? " It can be placed at their camp." : "";
            return $"[Game: Deal done! The customer paid {Catalog.ChineseNumber(o.price)}块 and got {item.hanzi}.{hint} Hand it over and thank them.]";
        }

        // ------------------------------------------------------------------ ambient behaviour

        protected override void Update()
        {
            base.Update();
            if (player == null) return;
            float d = DistanceToPlayer;

            if (d > serviceRadius * 2f)
            {
                if (PendingOffer != null) SetOffer(null);
                if (d > 14f) _greetedThisVisit = false;
                return;
            }

            // (Keepers no longer greet passers-by: the player starts a conversation with E.)

            // Face the customer when they're close.
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (d < serviceRadius * 1.5f && to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to.normalized), 240f * Time.deltaTime);
        }

        /// <summary>The keeper the player is addressing, if any (close, and roughly facing them).</summary>
        public static ShopkeeperBrain Facing(Transform playerTransform)
        {
            ShopkeeperBrain best = null;
            float bestScore = float.MaxValue;
            foreach (var k in All)
            {
                Vector3 to = k.transform.position - playerTransform.position;
                to.y = 0f;
                float d = to.magnitude;
                if (d > k.serviceRadius) continue;
                float angle = Vector3.Angle(playerTransform.forward, to);
                if (angle > 80f && d > 1.8f) continue;
                float score = d + angle * 0.02f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = k;
                }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            All.Clear();
            LastTalkedTo = null;
            LastCustomerActivity = -999f;
            OfferChanged = null;
            TransactionDone = null;
        }
    }
}
