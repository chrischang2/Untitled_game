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
            { "fish", new[] { "陈阿姨", "陈", "阿姨", "auntie chen", "chen", "auntie", "fish market", "fishmonger" } },
            { "furniture", new[] { "李师傅", "师傅", "master li", "li shifu", "shifu", "li", "furniture", "carpenter" } },
            { "pet", new[] { "小林", "xiao lin", "lin", "pet shop" } },
            { "books", new[] { "周老师", "老师", "teacher zhou", "zhou", "bookshop", "bookseller", "book shop" } },
            { "gym", new[] { "武教练", "教练", "coach wu", "coach", "wu", "trainer", "gym" } },
            { "gifts", new[] { "刘奶奶", "奶奶", "granny liu", "granny", "grandma", "liu", "gift shop" } },
            { "colours", new[] { "小方", "xiao fang", "fang", "colour shop", "color shop", "paint" } },
            { "school", new[] { "高老师", "teacher gao", "gao", "test centre", "test center", "考试中心" } },
        };

        private static bool Says(string lower, string name) =>
            name.Any(c => c > 127) ? lower.Contains(name) : Regex.IsMatch(lower, $@"\b{Regex.Escape(name)}\b");

        private static bool AsksAboutLikes(string lower) =>
            Regex.IsMatch(lower, @"\b(like|likes|love|loves|favou?rite|dislike|dislikes|hate|hates|gift|gifts|present|give)\b") ||
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
            foreach (var id in keepers) sb.Append(DescribeKeeper(id, likes));

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
            int next = Affinity.NextThreshold(shopId);
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
                   string.Join(" ", p.facts.Where(f => f.id != "secret").Select(f => f.english)) +
                   $" The player's friendship with them: {Affinity.LevelHanzi[level]} ({Affinity.LevelEnglish[level]}, {Affinity.Points(shopId)} points" +
                   (next > 0 ? $"; {next} for {Affinity.LevelHanzi[level + 1]}" : "") + "). " +
                   (locked.Count > 0 ? $"Goods not sold to the player yet: {string.Join(", ", locked)}. " : "") +
                   "Answer the player's question from this, briefly and plainly (e.g. \"Lao Wang likes tea\").]";
        }

        private static string DescribeHsk()
        {
            int lvl = Hsk.Level;
            var (nl, nn) = Hsk.NextLesson();
            string lessons = string.Join(", ", Enumerable.Range(1, Hsk.MaxLevel).Select(l => $"HSK {l}: {Hsk.LessonsDone(l)}/{Hsk.LessonCounts[l]} lessons passed"));
            return $"\n[Game: Lessons and tests: 高老师 (Teacher Gao) runs the 考试中心 [test centre] at the market. Say 我想上课 to her for the next lesson " +
                   $"(HSK {nl} lesson {nn}; it pays money the first time it's passed), 我想练习 for free practice (no money), or 我想考试 for the HSK test (any time). " +
                   $"The player has passed HSK {lvl}. {lessons}. " +
                   (Hsk.NextTest > 0 ? $"Passing HSK {Hsk.NextTest} unlocks: {Hsk.Unlocks[Hsk.NextTest]} " : "They have passed every test. ") +
                   "Answer the player's question from this, briefly.]";
        }

        /// <summary>Short how-the-game-works section for Mei's system prompt.</summary>
        public static string GameGuide() =>
            "You also know how life in Willow Bay works, and answer the player's questions about it plainly:\n" +
            "- At a stall, press E to talk to the shopkeeper. Everything with shopkeepers is done by speaking Chinese (hold V); B asks you quietly.\n" +
            "- Friendship (好感): each shopkeeper likes the player more when they chat in Chinese (harder words and longer sentences count more, " +
            "talking about the keeper's interests and answering their questions count extra, a few points a day per keeper) and when given gifts they like " +
            "(gifts are sold at 刘奶奶's 礼品店; say 送给你… to give one, one gift a day). Levels: 陌生人, 认识, 朋友, 好朋友, 老朋友. " +
            "Better goods and personal stories unlock with friendship. Facts learned go in the journal (J, People).\n" +
            "- Lessons and tests: 高老师 runs the 考试中心 [test centre]. Say 我想上课 (a lesson; pays money the first time), 我想练习 (free practice) or " +
            "我想考试 (the HSK 1, 2 or 3 test, as often as they like). Many goods need BOTH enough friendship with the seller AND a passed HSK test. " +
            "Passing tests also makes 陈阿姨 pay more for fish automatically (+10% / +20% / +35%) and unlocks more cosmetics.\n" +
            "- Fishing: a fish only bites when every requirement is met: the player has read about it (books from 周老师's 书店; the journal then shows " +
            "what it needs), the bobber is far enough from the shore, the bait on the hook is one it likes, the line is strong enough, and it's the right time of day. " +
            "老王 sells baits and stronger lines (红线 6 kg, 蓝线 15 kg, 黑线 40 kg, 金线 80 kg), and lends his 小船 [boat] to friends for fishing far out. " +
            "武教练 trains six skills: 力量 (cast further), 眼睛 (wider green bar), 跑步 (fish escape more slowly), 运气 (bonus fish), 技术 (bigger fish), 安静 (faster bites). " +
            "Reeling: hold the mouse to lift the green bar and keep the fish inside it until the meter fills. " +
            "If 汤圆 is fed until she's full, the next day she brings the player some bait.\n" +
            "- Energy: fishing uses energy (casting, and reeling: heavy fish drain it fast; press E while reeling to cut the line and save energy). " +
            "Sleep in the bed at home (any time) to wake at 6am with full energy. Each cast costs 10 energy. At 2am, or at zero energy, the player passes out: they lose everything " +
            "in their bag except the 3 most valuable slots and wake at 10am at home. Better beds from 李师傅 (单人床, 双人床, 大床) and placing furniture " +
            "(home comfort) give more energy.\n" +
            "- The bag only holds fish (each kind stacks in one slot); bait, gifts, books and everything else don't take space. " +
            "It starts with 4 slots; 老王's buckets (大水桶, 很大的水桶, 最大的水桶) give 8, 12, 16. Only fish can be caught (no junk).\n" +
            "- The boat: past its range the currents push it back. 老王 sells 好船桨 (40 m), 船帆 (60 m) and a 新船 that reaches the island far out at sea, " +
            "where the player can set up camp and sleep for several days.\n" +
            "- 小方's 颜色店 [colour shop] sells cosmetics: boat paint, little hats for 汤圆, sun hats for you (Mei), and roof colours for the house.\n" +
            "- Keys: F uses things (boat, cat, bowl), I bag, J journal, N notebook.";
    }
}
