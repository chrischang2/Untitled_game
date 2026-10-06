using System.Collections.Generic;
using System.Linq;
using System.Text;
using UntitledGame.Economy;
using UntitledGame.Fishing;
using UntitledGame.Language;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Fast, deterministic reading of what a customer said at a stall. Item names are matched on toneless
    /// pinyin, so recognition slips and tone mistakes (猪鱼竿 for 竹鱼竿) still work. Returns null when the
    /// line isn't clear, and the shopkeeper then falls back to the LLM classifier.
    /// </summary>
    public static class ShopIntentParser
    {
        public class Result
        {
            public string intent;
            public string item = "";
            public int quantity = 1;
            public string fish = "";
            public override string ToString() => $"{intent} item={item} qty={quantity} fish={fish}";
        }

        // Other ways people name the goods (shortenings and everyday words).
        private static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>
        {
            { "rod_bamboo", new[] { "竹竿", "竹子鱼竿" } },
            { "rod_carbon", new[] { "碳素竿", "碳鱼竿", "碳竿", "碳纤维鱼竿" } },
            { "rod_gold", new[] { "金竿", "金色鱼竿", "黄金鱼竿" } },
            { "bait_worm", new[] { "虫子" } },
            { "bait_shrimp", new[] { "虾", "小虾" } },
            { "bait_dough", new[] { "面团" } },
            { "bait_glow", new[] { "夜光" } },
            { "bucket_big", new[] { "水桶", "桶" } },
            { "line_strong", new[] { "线" } },
            { "bobber_fancy", new[] { "鱼漂", "漂" } },
            { "floor_lamp", new[] { "落地灯", "台灯" } },
            { "plant", new[] { "植物", "花" } },
            { "teddy", new[] { "熊", "小熊", "熊娃娃" } },
            { "cat_food", new[] { "猫食", "食物" } },
            { "cat_treat", new[] { "鱼干", "零食" } },
            { "cat_bed", new[] { "猫床", "窝" } },
            { "cat_box", new[] { "箱子", "盒子" } },
            { "cat_bowl", new[] { "碗" } },
            { "yarn", new[] { "毛线" } },
            { "scratcher", new[] { "抓板" } },
            { "collar", new[] { "项圈" } },
            { "book_basics", new[] { "入门", "入门书", "钓鱼书" } },
            { "book_shore", new[] { "海边", "海边的书" } },
            { "book_rocks", new[] { "石头", "石头下" } },
            { "book_open", new[] { "远海" } },
            { "book_legends", new[] { "传说", "海的传说" } },
            { "bait_squid", new[] { "鱿鱼条" } },
            { "bait_crab", new[] { "螃蟹", "蟹" } },
            { "bait_fish", new[] { "活鱼", "小鱼" } },
            { "line_red", new[] { "红色的线", "红色的鱼线", "红鱼线" } },
            { "line_blue", new[] { "蓝色的线", "蓝色的鱼线", "蓝鱼线" } },
            { "line_black", new[] { "黑色的线", "黑色的鱼线", "黑鱼线" } },
            { "line_gold", new[] { "金色的线", "金色的鱼线", "金鱼线" } },
            { "cos_boat_red", new[] { "红船", "红色船" } },
            { "cos_boat_blue", new[] { "蓝船", "蓝色船" } },
            { "cos_boat_yellow", new[] { "黄船", "黄色船" } },
            { "cos_cat_red", new[] { "红帽子", "猫的红帽子" } },
            { "cos_cat_yellow", new[] { "黄帽子", "猫的黄帽子" } },
            { "cos_mei_white", new[] { "白帽子" } },
            { "cos_mei_red", new[] { "美的红帽" } },
            { "cos_house_red", new[] { "红色的房子", "红房顶" } },
            { "cos_house_blue", new[] { "蓝色的房子", "蓝房顶" } },
            { "cos_house_white", new[] { "白色的房子", "白房顶" } },
            { "up_cast", new[] { "力量", "力气" } },
            { "up_bar", new[] { "眼睛" } },
            { "up_grip", new[] { "跑步", "耐力" } },
            { "up_luck", new[] { "运气" } },
            { "up_quality", new[] { "技术" } },
            { "up_bite", new[] { "安静" } },
            { "boat", new[] { "船", "租船", "划船", "借船" } },
            { "boat_oars", new[] { "船桨", "桨" } },
            { "boat_sail", new[] { "帆" } },
            { "boat_new", new[] { "新的船", "大船" } },
            { "gift_flowers", new[] { "鲜花", "一束花" } },
            { "gift_tea", new[] { "茶叶", "绿茶" } },
            { "gift_sweets", new[] { "糖果" } },
            { "gift_hat", new[] { "草帽" } },
            { "gift_umbrella", new[] { "雨伞" } },
            { "cos_boat_gold", new[] { "金船", "金色船" } },
            { "cos_cat_green", new[] { "绿帽子", "猫的绿帽子" } },
            { "cos_cat_gold", new[] { "金帽子", "猫的金帽子" } },
            { "cos_mei_blue", new[] { "蓝帽子", "美的蓝帽" } },

        };

        private static readonly string[] Decline = { "不要", "不用", "算了", "不买", "不卖", "太贵", "不了", "别" };
        private static readonly string[] Confirm = { "要", "好", "可以", "行", "对", "是", "嗯", "没问题", "买", "卖", "成交", "当然" };
        private static readonly string[] PriceWords = { "多少钱", "几块", "多少", "怎么卖", "价格", "贵不贵" };
        private static readonly string[] Greetings = { "你好", "您好", "老板好", "阿姨好", "师傅好", "早上好", "晚上好" };
        private static readonly string[] BrowseWords = { "卖什么", "买什么", "有什么", "卖啥", "有啥", "看看", "看一看", "看一下", "都有" };
        private static readonly string[] Thanks = { "谢谢", "多谢", "谢了" };
        private static readonly string[] Goodbyes = { "再见", "拜拜", "走了", "下次见", "明天见" };

        /// <summary>
        /// Is the customer asking about the shopkeeper themself? Returns "hometown", "siblings", "hobby", "food", "family",
        /// "birthday", "dream", "like", "dislike" or null. (你喜欢做什么 is a hobby question, 你喜欢吃什么 a food one,
        /// 你喜欢什么 a "likes" one.)
        /// </summary>
        public static string FactQuestion(string text)
        {
            string s = Clean(text);
            if (s.Length < 3) return null;
            bool asking = s.Contains("什么") || s.Contains("吗") || s.Contains("哪") || s.Contains("几") || s.Contains("谁") || s.Contains("怎么") || s.Contains("有没有");
            if (s.Contains("生日")) return "birthday";
            if (s.Contains("喜欢吃") || s.Contains("爱吃") || s.Contains("最好吃") || (s.Contains("吃") && s.Contains("什么"))) return "food";
            if (s.Contains("兄弟") || s.Contains("姐妹") ||
                ((s.Contains("哥哥") || s.Contains("姐姐") || s.Contains("弟弟") || s.Contains("妹妹")) && asking)) return "siblings";
            if (s.Contains("梦想") || (s.Contains("以后") && (s.Contains("做") || s.Contains("想") || s.Contains("希望"))) || s.Contains("希望什么")) return "dream";
            if (s.Contains("爱好") || (s.Contains("喜欢做") && s.Contains("什么")) || (s.Contains("周末") && s.Contains("做")) || s.Contains("有空")) return "hobby";
            if (s.Contains("不喜欢") && asking) return "dislike";
            if (s.Contains("讨厌")) return "dislike";
            if (s.Contains("喜欢") && s.Contains("什么")) return "like";
            if (s.Contains("哪里人") || s.Contains("哪儿人") || s.Contains("哪国人") || s.Contains("老家") ||
                ((s.Contains("哪里") || s.Contains("哪儿")) && (s.Contains("来") || s.Contains("长大")))) return "hometown";
            if (s.Contains("结婚") || s.Contains("丈夫") || s.Contains("妻子") || s.Contains("家人") ||
                (s.Contains("家") && (s.Contains("几个人") || s.Contains("几口人") || s.Contains("家里"))) ||
                (s.Contains("孩子") && asking)) return "family";
            return null;
        }

        public static Result Parse(ShopDef shop, string text, bool offerPending)
        {
            string s = Clean(text);
            if (s.Length == 0) return null;

            // Selling fish (买/卖 sound the same without tones, so at the fish shop any 买/卖 + 鱼 counts).
            bool mentionsFish = s.Any(c => c == '鱼' || Syllable(c) == "yu");
            // "What do you sell?" / "What else is there?" / "Just looking" (unless an item is named).
            if (ContainsAny(s, BrowseWords) && FindItem(shop, s) == null) return new Result { intent = "browse" };

            // At 海叔's, giving (or "selling") fish means putting them in the crab pots as bait.
            if (shop.crabber && mentionsFish && !ContainsAny(s, Decline) && FindItem(shop, s) == null &&
                (s.Contains("给") || s.Contains("放") || s.Contains("卖") || s.Contains("用") || s.Contains("饵") || s.Any(c => Syllable(c) == "mai")))
                return new Result { intent = "sell_fish", fish = FindFish(s) };

            bool saysSell = s.Contains("卖") || (shop.buysFish && s.Any(c => Syllable(c) == "mai"));
            // At the fish market selling can only mean fish, even when the fish's name was misheard (蓝鳃鱼 -> 蓝晒油).
            if (saysSell && (mentionsFish || shop.buysFish) && !ContainsAny(s, Decline) && FindItem(shop, s) == null)
                return new Result { intent = "sell_fish", fish = FindFish(s) };

            var item = FindItem(shop, s);
            if (item != null)
            {
                bool price = ContainsAny(s, PriceWords);
                if (ContainsAny(s, Decline) && !price) return new Result { intent = "decline", item = item.id };
                return new Result { intent = price ? "ask_price" : "buy", item = item.id, quantity = FindQuantity(s) };
            }

            if (offerPending)
            {
                if (ContainsAny(s, Decline)) return new Result { intent = "decline" };
                if (ContainsAny(s, Confirm)) return new Result { intent = "confirm" };
                // A lone 要 is easily misheard (药, 摇, 咬...): any short answer that sounds like "yao" is a yes.
                if (s.Length <= 2 && s.Any(c => Syllable(c) == "yao")) return new Result { intent = "confirm" };
            }
            if (shop.buysFish && mentionsFish && !offerPending && (s.Contains("有") || s.Contains("这些") || s.Contains("看看")))
                return new Result { intent = "sell_fish", fish = FindFish(s) };
            if (ContainsAny(s, PriceWords)) return new Result { intent = "ask_price" };
            if (ContainsAny(s, Goodbyes)) return new Result { intent = "goodbye" };
            if (ContainsAny(s, Thanks) && s.Length <= 5) return new Result { intent = "thanks" };
            if (ContainsAny(s, Greetings) && s.Length <= 6) return new Result { intent = "greeting" };
            return null;
        }

        private static string Clean(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in Pinyin.ToSimplified(text))
                if (Pinyin.IsHanzi(c) || char.IsDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        private static bool ContainsAny(string s, string[] words) => words.Any(s.Contains);

        /// <summary>Longest item name (or alias) in the line. Names of 2+ characters also match on toneless pinyin.</summary>
        private static ItemDef FindItem(ShopDef shop, string s)
        {
            ItemDef best = null;
            int bestLen = 0;
            foreach (var id in shop.items)
            {
                var def = Catalog.Get(id);
                if (def == null) continue;
                var names = new List<string> { def.hanzi };
                if (Aliases.TryGetValue(id, out var extra)) names.AddRange(extra);
                foreach (var n in names)
                {
                    if (n.Length <= bestLen) continue;
                    if (Mentions(s, n))
                    {
                        best = def;
                        bestLen = n.Length;
                    }
                }
            }
            if (best != null) return best;

            // Nothing heard cleanly: allow one misheard syllable in a long name (力尿训练 -> 力量训练), if exactly one
            // item fits best.
            int bestScore = 0;
            bool tie = false;
            foreach (var id in shop.items)
            {
                var def = Catalog.Get(id);
                if (def == null) continue;
                var names = new List<string> { def.hanzi };
                if (Aliases.TryGetValue(id, out var extra)) names.AddRange(extra);
                int score = names.Max(n => FuzzyScore(s, n));
                if (score > bestScore) { bestScore = score; best = def; tie = false; }
                else if (score == bestScore && score > 0 && best != def) tie = true;
            }
            return tie ? null : best;
        }

        /// <summary>
        /// For names of 4+ characters: the most characters matching (exactly or by sound) in any window with at most one
        /// mismatch and at least half matching exactly; 0 if none.
        /// </summary>
        public static int FuzzyScore(string s, string name)
        {
            if (name.Length < 4) return 0;
            int best = 0;
            for (int i = 0; i + name.Length <= s.Length; i++)
            {
                int exact = 0, sound = 0, miss = 0;
                for (int k = 0; k < name.Length && miss <= 1; k++)
                {
                    char a = s[i + k], b = name[k];
                    if (a == b) exact++;
                    else if (Syllable(a).Length > 0 && Syllable(a) == Syllable(b)) sound++;
                    else miss++;
                }
                if (miss <= 1 && exact * 2 >= name.Length) best = System.Math.Max(best, exact * 2 + sound);
            }
            return best;
        }

        private static string FindFish(string s)
        {
            FishSpecies best = null;
            int bestLen = 0;
            foreach (var f in FishDatabase.All)
            {
                if (!f.IsFish || string.IsNullOrEmpty(f.hanzi) || f.hanzi.Length <= bestLen) continue;
                if (Mentions(s, f.hanzi))
                {
                    best = f;
                    bestLen = f.hanzi.Length;
                }
            }
            return best != null ? best.id : "";
        }

        /// <summary>
        /// True if the line contains the name, allowing same-sound characters (toneless) for names of two or
        /// more characters as long as at least one character matches exactly.
        /// </summary>
        public static bool Mentions(string s, string name)
        {
            if (s.Contains(name)) return true;
            if (name.Length < 2) return false;
            for (int i = 0; i + name.Length <= s.Length; i++)
            {
                int exact = 0;
                int k = 0;
                for (; k < name.Length; k++)
                {
                    char a = s[i + k], b = name[k];
                    if (a == b) { exact++; continue; }
                    string sa = Syllable(a);
                    if (sa.Length == 0 || sa != Syllable(b)) break;
                }
                if (k == name.Length && exact > 0) return true;
            }
            return false;
        }

        private static readonly Dictionary<char, string> SyllableCache = new Dictionary<char, string>();

        private static string Syllable(char c)
        {
            if (!Pinyin.Ready) return "";
            if (!SyllableCache.TryGetValue(c, out var syl)) SyllableCache[c] = syl = Pinyin.Plain(c.ToString());
            return syl;
        }

        private const string Digits = "零一二两三四五六七八九十百";

        /// <summary>First number in the line (两包 -> 2, 十二个 -> 12, 3个 -> 3); 1 if none.</summary>
        public static int FindQuantity(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                int j = i;
                while (j < s.Length && (Digits.IndexOf(s[j]) >= 0 || char.IsDigit(s[j]))) j++;
                if (j == i) continue;
                string run = s.Substring(i, j - i);
                // Skip "一" in words like 一下 / 一点 / 一起 / 一共.
                if (run == "一" && j < s.Length && "下点起共些样".IndexOf(s[j]) >= 0) { i = j; continue; }
                int n = ParseNumber(run);
                if (n > 0) return System.Math.Min(n, 20);
                i = j;
            }
            return 1;
        }

        public static int ParseNumber(string run)
        {
            if (run.All(char.IsDigit)) return int.TryParse(run, out int d) ? d : 0;
            int total = 0, current = 0;
            foreach (char c in run)
            {
                int v = "零一二三四五六七八九".IndexOf(c);
                if (c == '两') v = 2;
                if (v >= 0) current = v;
                else if (c == '十') { total += (current == 0 ? 1 : current) * 10; current = 0; }
                else if (c == '百') { total += (current == 0 ? 1 : current) * 100; current = 0; }
            }
            return total + current;
        }
    }
}
