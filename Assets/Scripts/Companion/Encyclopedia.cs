using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Progression;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Mei knows everything about Willow Lake. When the player mentions a shopkeeper (or a gift, or asks who
    /// likes what), the relevant facts are handed to her for that turn, so "what does Lao Wang like?" gets
    /// "Lao Wang likes tea and hats". Likes/dislikes she tells the player also go into the journal.
    /// (Facts are injected per turn rather than kept in her system prompt: the context window is small.)
    /// </summary>
    public static class Encyclopedia
    {
        private static readonly Dictionary<string, string[]> Names = new Dictionary<string, string[]>
        {
            { "tackle", new[] { "老王", "王", "old wang", "lao wang", "wang", "wong", "tackle" } },
            { "fish", new[] { "陈阿姨", "陈", "阿姨", "auntie chen", "chen", "auntie", "fish market", "fishmonger", "sushi", "寿司" } },
            { "furniture", new[] { "李师傅", "师傅", "master li", "li shifu", "shifu", "li", "furniture", "carpenter" } },
            { "pet", new[] { "小林", "xiao lin", "lin", "pet shop" } },
            { "books", new[] { "周老师", "老师", "teacher zhou", "zhou", "bookshop", "bookseller", "book shop" } },
            { "gym", new[] { "武教练", "教练", "coach wu", "coach", "wu", "trainer", "gym" } },
            { "gifts", new[] { "刘奶奶", "奶奶", "granny liu", "granny", "grandma", "liu", "gift shop" } },
            { "colours", new[] { "小方", "xiao fang", "fang", "colour shop", "color shop", "paint" } },
            { "school", new[] { "高老师", "teacher gao", "gao", "test centre", "test center", "考试中心" } },
            { "crabber", new[] { "海叔", "uncle hai", "hai shu", "crab", "crabs", "crabber", "螃蟹", "蟹笼" } },
        };

        private static bool Says(string lower, string name) =>
            name.Any(c => c > 127) ? lower.Contains(name) : Regex.IsMatch(lower, $@"\b{Regex.Escape(name)}\b");

        private static bool AsksAboutLikes(string lower) =>
            Regex.IsMatch(lower, @"\b(l+ikes?|loves?|favou?rite|dislikes?|hates?|gifts?|present|give)\b") ||
            lower.Contains("喜欢") || lower.Contains("礼物") || lower.Contains("送");

        /// <summary>A [Game: ...] note with what Mei knows that's relevant to the player's line (empty if nothing is).</summary>
        public static string NoteFor(string playerText)
        {
            string lower = (playerText ?? "").ToLowerInvariant();
            var sb = new StringBuilder();
            var keepers = Names.Where(kv => kv.Value.Any(n => Says(lower, n))).Select(kv => kv.Key).Distinct().Take(2).ToList();
            bool likes = AsksAboutLikes(lower);
            // English names get mangled by the Mandarin recogniser ("Old Wang" -> "old whine"): "old w..." is Old Wang,
            // and a likes question without a name is about whoever the player last talked to.
            if (keepers.Count == 0 && Regex.IsMatch(lower, @"\bold w")) keepers.Add("tackle");
            if (keepers.Count == 0 && likes && ShopkeeperBrain.LastTalkedTo != null && !Regex.IsMatch(lower, @"\b(who|everyone|everybody)\b"))
                keepers.Add(ShopkeeperBrain.LastTalkedTo.Shop.id);
            foreach (var id in keepers) sb.Append(DescribeKeeper(id, likes)).Append(ShopGoods(id));
            sb.Append(TopicNotes(lower));

            // Lessons and HSK tests: where the player stands and what's next.
            if (keepers.Contains("school") || Regex.IsMatch(lower, @"\b(hsk|test|tests|exam|exams|lesson|lessons|class|classes)\b") ||
                lower.Contains("考试") || lower.Contains("上课") || lower.Contains("水平"))
                sb.Append(DescribeHsk());

            // Fish: what it takes to catch one (for undiscovered fish only which book teaches it, so books still matter).
            foreach (var f in Fishing.FishDatabase.All.Where(f => f.IsFish &&
                         (lower.Contains(f.hanzi) || Regex.IsMatch(lower, $@"\b{Regex.Escape(f.name.ToLowerInvariant())}\b") ||
                          Regex.IsMatch(lower, $@"\b{Regex.Escape(f.name.Split(' ').Last().ToLowerInvariant())}s?\b"))).Take(2))
            {
                var book = Fishing.FishDatabase.BookFor(f);
                sb.Append(Progression.PlayerStats.IsDiscovered(f)
                    ? $"\n[Game: About {f.hanzi} [{f.name.ToLower()}]: {Fishing.FishDatabase.WeightText(f.minWeight)} to {Fishing.FishDatabase.WeightText(f.maxWeight)}; " +
                      $"bites {Fishing.FishDatabase.WhereText(f)}, {Fishing.FishDatabase.WhenText(f)}; bait: {Fishing.FishDatabase.BaitText(f)}; line: {Fishing.FishDatabase.LineText(f)}.]"
                    : $"\n[Game: The player hasn't learned about {f.hanzi} [{f.name.ToLower()}] yet. Don't give details: tell them the book " +
                      $"{(book != null ? $"{book.hanzi} [{book.english}] from 周老师's bookshop" : "at the bookshop")} explains how to catch it.]");
            }

            // "Who likes tea?" / "what gift should I buy?": who likes and dislikes each gift mentioned.
            var gifts = Catalog.Items.Where(i => i.category == ItemCategory.Gift &&
                (lower.Contains(i.hanzi) || Regex.IsMatch(lower, $@"\b{Regex.Escape(i.english.ToLowerInvariant())}\b"))).ToList();
            foreach (var g in gifts)
            {
                var fans = KeeperProfiles.All.Where(p => p.likes.Contains(g.hanzi)).Select(p => Catalog.Shop(p.shopId)?.keeperName).ToList();
                var haters = KeeperProfiles.All.Where(p => p.dislikes.Contains(g.hanzi)).Select(p => Catalog.Shop(p.shopId)?.keeperName).ToList();
                sb.Append($"\n[Game: About {g.hanzi} [{g.english.ToLower()}] (¥{g.price} at 刘奶奶's 礼品店): " +
                          $"liked by {(fans.Count > 0 ? string.Join("、", fans) : "nobody in particular")}; " +
                          $"disliked by {(haters.Count > 0 ? string.Join("、", haters) : "nobody")}.]");
            }
            if (keepers.Count == 0 && gifts.Count == 0 && likes && Regex.IsMatch(lower, @"\b(who|everyone|everybody|shopkeepers?)\b"))
            {
                sb.Append("\n[Game: What everyone likes: " + string.Join("; ", KeeperProfiles.All.Select(p =>
                    $"{Catalog.Shop(p.shopId)?.keeperName} likes {string.Join("、", p.likes)}, dislikes {string.Join("、", p.dislikes)}")) + ".]");
            }
            return sb.ToString();
        }

        private static string DescribeKeeper(string shopId, bool learnLikes)
        {
            var shop = Catalog.Shop(shopId);
            var p = KeeperProfiles.For(shopId);
            if (shop == null || p == null) return "";
            int level = Affinity.Level(shopId);
            string Gloss(string hanzi) => $"{hanzi} [{Catalog.GiftByHanzi(hanzi)?.english.ToLower() ?? hanzi}]";
            var locked = shop.items.Select(Catalog.Get).Where(i => i != null && (i.minAffinity > level || i.minHsk > Hsk.Level))
                .Select(i => $"{i.hanzi} [{i.english.ToLower()}] (needs " +
                             string.Join(" and ", new[] { i.minAffinity > level ? Affinity.LevelHanzi[i.minAffinity] : null, i.minHsk > Hsk.Level ? $"HSK {i.minHsk}" : null }.Where(x => x != null)) + ")").ToList();
            if (learnLikes)
            {
                bool any = false;
                foreach (var l in p.likes) any |= Affinity.Learn(shopId, "like:" + l, "told by Mei");
                foreach (var d in p.dislikes) any |= Affinity.Learn(shopId, "dislike:" + d, "told by Mei");
                if (any) GameEvents.Toast($"Journal: Mei told you what {shop.keeperName} likes (J, People page).", 3f);
            }
            return $"\n[Game: What you know about {shop.keeperName} ({shop.keeperEnglish}) of the {shop.hanzi} [{shop.english.ToLower()}]. " +
                   $"{shop.keeperEnglish} LIKES {string.Join(" and ", p.likes.Select(Gloss))}, and DISLIKES {string.Join(" and ", p.dislikes.Select(Gloss))}" +
                   (learnLikes ? " (the player is asking about this: say these likes plainly first)" : "") + $". Personality: {p.personality}. " +
                   KnownFacts(shopId, p) +
                   $" The player's friendship with them: {Affinity.LevelHanzi[level]} ({Affinity.LevelEnglish[level]}). " + NextLevelText(shopId, level) +
                   (locked.Count > 0 ? $"Goods not sold to the player yet: {string.Join(", ", locked)}. " : "") +
                   "Answer the player's question from this, briefly and plainly (e.g. \"Lao Wang likes tea\").]";
        }

        /// <summary>
        /// Only what the player has already found out. The rest they must ask the shopkeeper themselves (that's how
        /// friendship grows), so Mei is told which question to suggest instead of the answer.
        /// </summary>
        private static string KnownFacts(string shopId, KeeperProfile p)
        {
            var known = p.facts.Where(f => Affinity.Knows(shopId, f.id)).Select(f => f.english).ToList();
            var unknown = p.facts.Where(f => !Affinity.Knows(shopId, f.id) && Affinity.FactQuestions.ContainsKey(f.id))
                .Select(f => $"{Affinity.FactQuestions[f.id].english} (ask {Affinity.FactQuestions[f.id].question})").ToList();
            return (known.Count > 0 ? "What the player has learned about them: " + string.Join(" ", known) + " " : "") +
                   (unknown.Count > 0 ? "The player hasn't asked them yet about: " + string.Join("; ", unknown) +
                                        ". You don't tell those answers: encourage the player to ask the shopkeeper in Chinese, and teach the question if they want. " : "");
        }

        private static string NextLevelText(string shopId, int level)
        {
            if (level >= Affinity.MaxLevel) return "";
            var left = Affinity.Requirements(shopId, level + 1).Where(r => !r.done).Select(r => r.text).ToList();
            return $"For {Affinity.LevelHanzi[level + 1]} the player still needs to: {string.Join("; ", left)}. ";
        }

        private static string DescribeHsk()
        {
            int lvl = Hsk.Level;
            var (nl, nn) = Hsk.NextLesson();
            string lessons = string.Join(", ", Enumerable.Range(1, Hsk.MaxLevel).Select(l => $"HSK {l}: {Hsk.LessonsDone(l)}/{Hsk.LessonCounts[l]} lessons passed"));
            return $"\n[Game: Lessons and tests: 高老师 (Teacher Gao) runs the 考试中心 [test centre] at the market. Say 我想上课 to her for the next lesson " +
                   $"(HSK {nl} lesson {nn}; she sometimes has a little present afterwards), 我想练习 for practice of the words due for review, or 我想考试 for the HSK test (any time). " +
                   $"The player has passed HSK {lvl}. {lessons}. " +
                   (Hsk.NextTest > 0 ? $"Passing HSK {Hsk.NextTest} unlocks: {Hsk.Unlocks[Hsk.NextTest]} " : "They have passed every test. ") +
                   "Answer the player's question from this, briefly.]";
        }

        /// <summary>Short how-the-game-works section for Mei's system prompt.</summary>
        /// <summary>
        /// The short how-the-game-works overview kept in Mei's system prompt. The details live in topic notes that are only
        /// added to a turn when the player's line is about that topic (her context window is small: 4096 tokens).
        /// </summary>
        public static string GameGuide() =>
            "You know how life in Willow Bay works and answer questions about it plainly. When the player asks about something, " +
            "a [Game: ...] note gives you the details (prices, rules); rely on it and don't invent numbers. In short: fish, sell them to 陈阿姨, " +
            "make friends with the shopkeepers by talking Chinese with them, take lessons and HSK tests with 高老师, and do up the house. " +
            "Keys: E talk at a stall, hold V to speak, B ask you, F use things, I bag, J journal, N notebook.";

        private static readonly (string pattern, string note)[] Topics =
        {
            (@"\b(friend|friends|friendship|affinity|closer|level|levels|gift|gifts|present|unlock|unlocks|locked)\b|朋友|礼物|认识|送",
             "Friendship works the same with every shopkeeper. Each level needs: learning things about them by asking in Chinese " +
             "(认识: 你是哪里人？; 朋友: 你有哥哥姐姐吗？ and 你的爱好是什么？; 好朋友: 你喜欢吃什么？ and 你结婚了吗？; 老朋友: 你的生日是几月几号？ and 你以后想做什么？), " +
             "one gift for each level (not one they dislike; gifts from 刘奶奶's 礼品店; say 这是送给你的… to give one, one a day), and an HSK test " +
             "(朋友 needs HSK 1, 好朋友 HSK 2, 老朋友 HSK 3). Better goods unlock with friendship; the shop window shows what's left."),
            (@"\b(hsk|test|tests|exam|exams|lesson|lessons|class|practice|practise|words?)\b|考试|上课|练习|水平|词",
             "Lessons: 高老师 at the 考试中心. Say 我想上课 (a lesson, by theme), 我想练习 (practice of the words due for review) or 我想考试 " +
             "(the next HSK test, any time). Every word has a mastery level; using words in conversation counts too. Passing tests unlocks goods " +
             "(along with friendship) and turns more of the interface Chinese; every lesson passed (+2%) and test passed (+15%) raises the bonus on 陈阿姨's fish scale. She sometimes gives a little present after a lesson."),
            (@"\b(fish|fishing|bait|bite|bites|catch|cast|reel|line|lines|medal|medals|golden|trophy|big)\b|钓|鱼|饵",
             "Fishing: a fish can bite once the player has read about it (books from 周老师's 书店), the bobber is far enough out, the line is strong " +
             "enough and it's the right time of day. Any bait works, but its favourite bait makes a fish about four times as likely (the 皇带鱼 oarfish " +
             "only takes the 夜光饵 glow lure). 老王 sells baits and lines (红线 6 kg, 蓝线 15 kg, 黑线 40 kg, 金线 80 kg). Reeling: hold the mouse to lift " +
             "the green bar and keep the fish in it. Medals by weight (bronze, silver from 60% of a kind's weight range, gold from 90%); golden fish " +
             "(rare) are worth five times as much; a bubbling patch of water is a fish frenzy (fish bite twice as fast and come bigger there); 5 catches in a row make golden fish more likely; gold medals and golden fish go on the trophy wall at home. 武教练 trains 力量 (calmer fish), " +
             "眼睛 (wider bar), 跑步 (faster catch, slower escapes), 运气 (bonus and golden fish), 技术 (bigger fish), 安静 (faster bites): 20 levels each, " +
             "and every 5 levels need the next HSK test. Fish come in tiers (the HSK level of the book about them): stronger fish need more training, and each tier sells for about ten times the last."),
            (@"\b(crab|crabs|pot|pots|passive|mahjong|jianzi|games?|pitch)\b|螃蟹|蟹笼|海叔|麻将|毽子|投壶",
             "海叔 (Uncle Hai) has a crab stall down the beach past the market. He looks after the player's crab pots: every morning the crabs come in and he sells them; " +
             "the player collects the money by going to talk to him (uncollected crabs only keep a day or more, depending on the cooler). Giving him fish (给你鱼) puts them in the pots " +
             "as bait for a bigger haul the next day. He sells pot upgrades (more pots, bigger pots, lures, deep-water ropes, bait know-how, a cooler, a helper), five per HSK level. " +
             "Next to the beach path, three games stalls are being built: 投壶 pitch-pot (opens after HSK 1: throw eight arrows into a pot's mouth; 中 = a hit, 4+ hits wins a prize once a day), 毽子 jianzi (HSK 2) and 麻将 mahjong (HSK 3), which are coming soon."),
            (@"\b(energy|tired|sleep|sleeping|bed|beds|night|pass out|passed out|faint|comfort|furniture|house|home)\b|睡|累|床",
             "Energy: each cast that lands in the water costs 10, and reeling drains more (heavy fish drain fast; E while reeling cuts the line). " +
             "Sleep in the bed any time to wake at 6am with full energy. At 2am or zero energy the player passes out, keeps only the 3 most valuable " +
             "bag slots and wakes at 10am at home. Better beds from 李师傅 and furniture (comfort) give more energy; press F next to furniture to move it."),
            (@"\b(bag|bucket|buckets|slot|slots|carry|space|full)\b|桶|包",
             "The bag only holds fish (each kind stacks in one slot); everything else takes no space. It starts with 4 slots; 老王's buckets give 8, 12 and 16."),
            (@"\b(boat|row|rowing|island|current|currents|sea|far|camp|sail|oars)\b|船|岛",
             "The boat: rent 老王's 小船 once you're 认识. Past its range the currents push it back; 好船桨 (40 m), 船帆 (60 m) and the 新船 (to the island, " +
             "where the player can camp and sleep for days) go further."),
            (@"\b(paint|hat|hats|colou?r|colou?rs|cosmetic|cosmetics|roof|look)\b|颜色|帽子",
             "小方's 颜色店 sells cosmetics: boat paint, little hats for 汤圆, sun hats for you (Mei) and roof colours; they're put on when bought and can be switched in the bag."),
            (@"\b(how do i|controls?|keys?|press|button|cast)\b",
             "Controls: hold the left mouse to cast, click when the bobber dives, hold to reel. At a stall press E, then hold V to speak; hold B to ask Mei. " +
             "To feed 汤圆, press F next to her (or say 喂汤圆); a bowl at camp lets her eat by herself."),
        };

        /// <summary>Detail notes for the topics the player's line is about (at most three, to keep the turn small).</summary>
        public static string TopicNotes(string lower)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var (pattern, note) in Topics)
            {
                if (n >= 3) break;
                if (!Regex.IsMatch(lower, pattern)) continue;
                sb.Append("\n[Game: ").Append(note).Append("]");
                n++;
            }
            return sb.ToString();
        }

        /// <summary>What a shop sells, with prices (only added when the player mentions that shopkeeper).</summary>
        public static string ShopGoods(string shopId)
        {
            var shop = Catalog.Shop(shopId);
            if (shop == null) return "";
            if (shop.buysFish)
                return $"\n[Game: {shop.keeperName} is a sushi chef who buys fish (rarer and bigger pay more). Everything sold together goes on her scale: rarer and bigger fish fill its bar faster (the first bar takes about 4 good common fish, the next 8 uncommon ones, and so on), " +
                       $"and each bar filled adds {FishSale.PerFill * 100f:0}% to the price (10% plus 2% per lesson passed and 15% per HSK test passed), so selling a lot at once pays more." +
                       (DailyRequest.Active ? $" Today she'd like: {DailyRequest.Chinese} [{DailyRequest.Count} {DailyRequest.Fish.name.ToLower()}{(DailyRequest.Big ? ", big ones" : "")}]; bringing them earns a little present." : "") + "]";
            if (shop.items.Length == 0) return "";
            return $"\n[Game: {shop.keeperName} sells: " + string.Join("; ", shop.items.Select(Catalog.Get).Where(i => i != null)
                .Select(i => $"{i.hanzi} [{i.english.ToLower()}] {Catalog.ChineseNumber(Catalog.PriceOf(i))}块" + (i.packSize > 1 ? $" (pack of {i.packSize})" : ""))) + ".]";
        }

        /// <summary>One line per shop for Mei's system prompt: who sells what kind of thing (no prices).</summary>
        public static string MarketSummary()
        {
            string Kinds(ShopDef shop)
            {
                if (shop.buysFish) return "buys the player's fish";
                if (shop.school) return "lessons and HSK tests";
                var kinds = shop.items.Select(Catalog.Get).Where(i => i != null).Select(i => i.category switch
                {
                    ItemCategory.Bait => "bait",
                    ItemCategory.Line => "fishing lines",
                    ItemCategory.Boat => "the boat and boat upgrades",
                    ItemCategory.Accessory => "buckets",
                    ItemCategory.Bed => "beds",
                    ItemCategory.Furniture => "furniture",
                    ItemCategory.PetConsumable => "cat food",
                    ItemCategory.PetSupply => "cat things",
                    ItemCategory.Book => "fishing books",
                    ItemCategory.Upgrade => "training",
                    ItemCategory.Cosmetic => "cosmetics",
                    ItemCategory.Gift => "gifts",
                    _ => null,
                }).Where(k => k != null).Distinct();
                return string.Join(", ", kinds);
            }
            return string.Join("\n", Catalog.Shops.Select(s => $"- {s.keeperName} ({s.keeperEnglish}), {s.hanzi} [{s.english.ToLower()}]: {Kinds(s)}"));
        }
    }
}
