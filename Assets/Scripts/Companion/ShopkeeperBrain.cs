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
            // Clear lines are understood instantly by rules; only unclear ones go to the LLM classifier.
            var parsed = ShopIntentParser.Parse(Shop, text, PendingOffer != null);
            if (parsed != null)
            {
                Debug.Log($"[Shop] {DisplayNameEnglish} heard \"{text}\" -> rules: {parsed}");
                ChatAudit.Write(MemoryKey, "understood (rules): " + parsed);
                string note = Resolve(new IntentResult { intent = parsed.intent, item = parsed.item, quantity = parsed.quantity, fish = parsed.fish });
                if (string.IsNullOrEmpty(note)) ChatAudit.Write(MemoryKey, "no game action for that intent");
                Ask((string.IsNullOrEmpty(note) ? NotUnderstoodNote : note) + "\n" + CustomerSaid(text));
                return;
            }
            StartCoroutine(ClassifyThenReply(text));
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

        private IEnumerator ClassifyThenReply(string text)
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
            Ask((string.IsNullOrEmpty(note) ? NotUnderstoodNote : note) + "\n" + CustomerSaid(text));
        }

        /// <summary>The player's words, clearly marked as the customer's (keepers used to continue them as their own line).</summary>
        private static string CustomerSaid(string text) => $"顾客说：「{text}」";

        private int _browseIndex;

        /// <summary>"What do you sell?": the next few goods with their prices (a different few each time).</summary>
        private string BrowseNote()
        {
            var shop = Shop;
            if (shop.buysFish)
                return Inventory.BucketCount > 0
                    ? $"[Game: The customer asks what you buy. Say you buy all kinds of fish from the lake, and bigger or rarer fish pay more. They have {Inventory.DescribeBucketChinese()} in their bucket.]"
                    : "[Game: The customer asks what you buy. Say you buy all kinds of fish from the lake, and bigger or rarer fish pay more. Their bucket is empty.]";
            var items = shop.items.Select(Catalog.Get).Where(i => i != null).ToList();
            if (items.Count == 0) return "";
            var some = new List<string>();
            for (int k = 0; k < Mathf.Min(3, items.Count); k++)
            {
                var i = items[(_browseIndex + k) % items.Count];
                some.Add($"{i.hanzi} {Catalog.ChineseNumber(i.price)}块" + (i.packSize > 1 ? $"（一包{Catalog.ChineseNumber(i.packSize)}个）" : ""));
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
                    Inventory.CanBuy(item.id, qty, out var reason, out int cost);
                    string what = Catalog.Counted(item, qty);
                    string pack = item.packSize > 1 ? $" (one pack has {item.packSize})" : "";
                    switch (reason)
                    {
                        case BuyResult.AlreadyOwned:
                            SetOffer(null);
                            return $"[Game: The customer asked about {item.hanzi}, but they already own one. Tell them kindly.]";
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
            string hint = item.placeable ? " It can be placed at their camp." : "";
            return $"[Game: Deal done! The customer paid {Catalog.ChineseNumber(o.price)}块 and got {item.hanzi}.{hint} Hand it over and thank them.]";
        }

        /// <summary>Offer card buttons (the customer nods / shakes their head instead of speaking).</summary>
        public void AnswerOffer(bool yes)
        {
            if (PendingOffer == null) return;
            Interrupt();
            ChatAudit.Write("YOU→" + MemoryKey, yes ? "(accepted the offer card)" : "(declined the offer card)");
            LastCustomerActivity = Time.time;
            string note = yes ? Execute() : "[Game: The customer shakes their head: they don't want it. Say that's no problem.]";
            if (!yes) SetOffer(null, "declined");
            ConversationLog.Add("You", yes ? "(nods)" : "(shakes head)", true);
            Ask(note);
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

            // Greet customers who walk up.
            if (!_greetedThisVisit && d < serviceRadius && CanChat && !IsBusy && Time.time - _lastInteraction > 20f)
            {
                _greetedThisVisit = true;
                _lastInteraction = Time.time;
                Ask("[Game: A customer walks up to your stall. Greet them with one short, simple sentence (e.g. 欢迎光临！).]");
            }

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
            LastCustomerActivity = -999f;
            OfferChanged = null;
            TransactionDone = null;
        }
    }
}
