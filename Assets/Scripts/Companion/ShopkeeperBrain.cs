using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
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

        public void Configure(string shop, CharacterVoice v, Transform playerTransform)
        {
            shopId = shop;
            voice = v;
            player = playerTransform;
            llmSlot = 1;
            sampling = new ChatSampling { temperature = 0.6f, top_p = 0.85f, top_k = 20, presence_penalty = 1.0f, max_tokens = 90 };
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            All.Add(this);
        }

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
            int questionLevel = _pendingQuestionLevel;
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

            // Friendship: every Chinese line counts; answering their question counts extra.
            bool answered = question != null && (parsed == null || chat);
            note = (note ?? "") + FriendshipFor(text, answered ? questionLevel : 0, levelBefore);

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

        /// <summary>Scores the line for friendship; returns an extra note when the friendship level went up.</summary>
        private string FriendshipFor(string text, int questionLevel, int levelBefore)
        {
            var score = Affinity.ScoreLine(shopId, text, questionLevel);
            ChatAudit.Write(MemoryKey, $"friendship: +{score.points} ({score.why})");
            if (score.points > 0)
            {
                Affinity.Add(shopId, score.points, "talk: " + score.why);
                GameEvents.Toast($"{DisplayName} friendship +{score.points}  <size=18>({score.why})</size>", 2.2f);
            }
            int now = Affinity.Level(shopId);
            return now > levelBefore ? LevelUpNote(now) : "";
        }

        private string LevelUpNote(int level)
        {
            string unlock = Shop.items.Select(Catalog.Get).Any(i => i != null && i.minAffinity == level) ? " Now there are new things you'll sell them." : "";
            GameEvents.Toast($"You and {DisplayName} ({DisplayNameEnglish}) are now {Affinity.LevelLabel(level)}!{unlock}", 5f);
            var p = Profile;
            var fact = p?.AllFacts().FirstOrDefault(f => f.minLevel <= level && !Affinity.Knows(shopId, f.id) && !f.id.StartsWith("dislike:"));
            string share = "";
            if (fact != null)
            {
                Affinity.Learn(shopId, fact.id, "friendship level " + level);
                share = $" Also share something personal, keeping it this simple: {fact.chinese}";
            }
            return $"\n[Game: You feel closer to this customer now ({Affinity.LevelHanzi[level]}). Say warmly that you're happy to be friends.{unlock}{share}]";
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
            Affinity.MarkGift(shopId);
            var p = Profile;
            bool liked = p != null && p.likes.Contains(gift.hanzi);
            bool disliked = p != null && p.dislikes.Contains(gift.hanzi);
            int pts = liked ? Affinity.GiftLiked : disliked ? Affinity.GiftDisliked : Affinity.GiftNeutral;
            Affinity.Add(shopId, pts, $"gift {gift.english} ({(liked ? "liked" : disliked ? "disliked" : "neutral")})");
            if (liked) Affinity.Learn(shopId, "like:" + gift.hanzi, "gift reaction");
            if (disliked) Affinity.Learn(shopId, "dislike:" + gift.hanzi, "gift reaction");
            ConversationLog.Add("You", $"(gives {DisplayName} {gift.hanzi})", true);
            // Likes and dislikes stay in Chinese (the journal shows 喜欢 / 不喜欢 with pinyin; ask Mei what they mean).
            string named = $"{gift.hanzi} {Pinyin.Of(gift.hanzi)}";
            GameEvents.Toast(liked ? $"{DisplayName} 喜欢 {named}! Friendship +{pts}" : disliked ? $"{DisplayName} 不喜欢 {named}... Friendship {pts}" : $"{DisplayName} thanks you for the gift. Friendship +{pts}", 4f);
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
            if (shopId == "gifts" && (kind == "like" || kind == "dislike"))
            {
                var other = ShopkeeperBrain.Keepers.FirstOrDefault(k => k != this && ShopIntentParser.Mentions(Pinyin.ToSimplified(text), k.DisplayName));
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
                    topic = kind == "hometown" ? "where you are from" : kind == "family" ? "your family" : "your hobbies";
                    fact = p.Fact(kind);
                    break;
            }
            if (fact == null) return ChatNote;
            if (fact.minLevel > lvl)
            {
                ChatAudit.Write(MemoryKey, $"won't share {fact.id} yet (needs friendship {fact.minLevel})");
                return $"[Game: The customer asks about {topic}. You have only just met, so smile and say you'll tell them when you know each other a little better.]";
            }
            if (Affinity.Learn(shopId, fact.id, "asked"))
                GameEvents.Toast($"Journal: you learned something about {DisplayName} (J, People page).", 3f);
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

        /// <summary>"What do you sell?": the next few goods with their prices (a different few each time).</summary>
        private string BrowseNote()
        {
            var shop = Shop;
            if (shop.school)
                return "[Game: The student asks what you do here. Tell them simply: they can say 我想上课 for a lesson, 我想练习 to practise, or 我想考试 to take the HSK test.]";
            if (shop.buysFish)
            {
                string cards = Hsk.FishBonus > 0 ? $" Also say that because they passed 汉语水平考试{Catalog.ChineseNumber(Hsk.Level)}级 you pay them more for every fish." : "";
                return Inventory.BucketCount > 0
                    ? $"[Game: The customer asks what you buy. Say you buy all kinds of fish from the sea, and bigger (heavier) or rarer fish pay more. They have {Inventory.DescribeBucketChinese()} in their bucket.{cards}]"
                    : $"[Game: The customer asks what you buy. Say you buy all kinds of fish from the sea, and bigger (heavier) or rarer fish pay more. Their bucket is empty.{cards}]";
            }
            var items = shop.items.Select(Catalog.Get).Where(i => i != null).ToList();
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
            if (o.selling) return $"the shop buys the customer's fish for {o.price} yuan";
            return $"customer buys {o.quantity} x {Catalog.Get(o.itemId)?.hanzi} for {o.price} yuan";
        }

        private string Resolve(IntentResult r)
        {
            var shop = Shop;
            ItemDef item = shop.items.Contains(r.item) ? Catalog.Get(r.item) : null;
            int qty = Mathf.Clamp(r.quantity, 1, 20);
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
                        // One level at a time; friends are trained to higher levels.
                        qty = 1;
                        int lvl = PlayerStats.Level(item.stat);
                        if (lvl >= PlayerStats.TrainingCap(friendship) && lvl < PlayerStats.MaxLevel)
                        {
                            SetOffer(null);
                            return $"[Game: The customer wants more {item.hanzi}, but you only train people harder once you know them better. " +
                                   "Say so in an encouraging way: come and chat more first!]";
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
                case "sell_fish":
                {
                    if (!shop.buysFish)
                        return "[Game: The customer wants to sell fish. You don't buy fish here; tell them to go to 陈阿姨's 鱼店 nearby.]";
                    if (Inventory.BucketCount == 0)
                    {
                        SetOffer(null);
                        return "[Game: The customer wants to sell fish, but their bucket is empty. Tell them kindly to come back after fishing.]";
                    }
                    var species = FishDatabase.Get(r.fish);
                    bool some = species != null && Inventory.Bucket.Any(b => b.species.id == species.id);
                    int value = some
                        ? Inventory.Bucket.Where(b => b.species.id == species.id).Sum(b => Catalog.FishPrice(b.species, b.length))
                        : Inventory.BucketValue;
                    string desc = some
                        ? $"{Inventory.Bucket.Count(b => b.species.id == species.id)}条{species.hanzi}"
                        : Inventory.DescribeBucketChinese();
                    int count = some ? Inventory.Bucket.Count(b => b.species.id == species.id) : Inventory.BucketCount;
                    value = Mathf.RoundToInt(value * (1f + 0.05f * Affinity.Level(shopId))); // friends get a better price
                    SetOffer(new Offer { selling = true, fishId = some ? species.id : "all", price = value, english = $"Sell {count} catch{(count == 1 ? "" : "es")} — +¥{value}" });
                    return $"[Game: The customer wants to sell {desc}. You offer {Catalog.ChineseNumber(value)}块 in total. Say what you see, the total, and ask if that's OK (可以吗？).]";
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

        /// <summary>Completes the pending offer and returns the note describing it.</summary>
        private string Execute()
        {
            var o = PendingOffer;
            SetOffer(null, "accepted");
            if (o == null) return "";
            if (o.selling)
            {
                var (count, money) = o.fishId == "all"
                    ? (Inventory.BucketCount, Inventory.SellAllFish())
                    : Inventory.SellSpecies(o.fishId);
                if (o.price > money)
                {
                    Inventory.Earn(o.price - money); // the friendship bonus on the offer
                    money = o.price;
                }
                string en = $"Sold {count} catch{(count == 1 ? "" : "es")} (+¥{money})";
                AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.7f);
                ChatAudit.Write(MemoryKey, $"SALE {en}; player now has ¥{Inventory.Money}");
                TransactionDone?.Invoke(this, en, $"卖了{Catalog.ChineseNumber(count)}条鱼，得到{Catalog.ChineseNumber(money)}块");
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
                    : $"{item.english}: your boat can now go {Fishing.Rowboat.Range:0} m out" + (item.id == "boat_new" ? " - far enough to reach the island!" : "."), 5f);
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
