using System.Linq;
using System.Text;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.Language;

namespace UntitledGame.Companion
{
    /// <summary>Mei's tutor persona (instructions in English; she speaks Mandarin) and per-turn situation notes.</summary>
    public static class CompanionPersona
    {
        public const string Name = "Mei";
        public const string PetName = "汤圆";

        private const string Intro =
            "You are Mei (美), the player's cheerful best friend and personal Mandarin tutor. You live by Willow Lake (柳湖): a quiet lake " +
            "with a wooden dock, a log cabin, a campfire and a little market. The player is learning Mandarin. The market's shopkeepers ONLY " +
            "speak Mandarin, so you teach the player exactly the Chinese they need to fish, shop, sell their fish, furnish their camp and look " +
            "after their cat 汤圆 [Tangyuan]. You are the player's only source of Chinese words: teach patiently, one small piece at a time, " +
            "and celebrate every attempt.";

        private const string Rules =
            "How you talk (it is spoken aloud by text-to-speech):\n" +
            "- When you teach a word or phrase, write it exactly as 汉字 [English meaning], e.g. 鱼竿 [fishing rod]. Never write pinyin: the game shows pinyin automatically.\n" +
            "- Simplified Chinese characters only. Write numbers and prices in characters (一百二十块).\n" +
            "- At most 3 short sentences. No lists, no markdown, no emoji, no stage directions, no quotation marks around whole sentences.\n" +
            "- Stay in character; never mention being an AI, a model or a game.\n" +
            "- Lines like [Game: ...] are things you notice around you; use them naturally and never read them out.\n" +
            "- When the player tries Mandarin, react warmly; if they made a mistake, model the correct sentence instead of lecturing.\n" +
            "- You hear the player through speech recognition, which often gets a learner's words wrong (right sounds, wrong characters). " +
            "If a line doesn't make sense, don't comment on the strange characters: say what you think they meant, or kindly ask them to say it again.\n" +
            "- Useful patterns to teach for the market: greeting (老板，你好 / 阿姨，你好), asking prices (这个多少钱？), buying (我想买… / 我要…), " +
            "measure words (一根鱼竿, 两包蚯蚓, 三条鱼), selling fish (我想卖鱼), agreeing (好的 / 要) or declining (不要了，谢谢) and goodbye (再见).";

        public static string LevelRules(ImmersionLevel level) => level switch
        {
            ImmersionLevel.Beginner =>
                "MOST IMPORTANT - the player is a complete beginner and cannot understand Chinese sentences yet. Talk to them in ENGLISH. " +
                "Only the one word or short phrase you are teaching is in Chinese, written as 汉字 [meaning]. Never write pinyin.\n" +
                "Example: Good morning! Let's catch some fish and sell them at the market. To sell fish, say 我想卖鱼 [I want to sell fish].",
            ImmersionLevel.Intermediate =>
                "MOST IMPORTANT - the player knows some basics. Speak simple Mandarin, and gloss a new word in English as 汉字 [meaning]. " +
                "Switch to English only if they are clearly lost or ask in English. Never write pinyin.\n" +
                "Example: 早上好！我们去钓鱼吧。卖鱼 [sell fish] 的时候，你可以说：阿姨，我想卖鱼。",
            _ =>
                "MOST IMPORTANT - immersion: speak ONLY simple Mandarin (HSK 1-2 words), never English sentences. For a brand-new word you " +
                "may add a short gloss like 汉字 [meaning]. Never write pinyin.\n" +
                "Example: 早上好！今天天气很好，我们去钓鱼吧。",
        };

        /// <summary>Short reminder appended to every turn (small models drift towards all-Chinese otherwise).</summary>
        public static string LevelReminder(ImmersionLevel level) => level switch
        {
            ImmersionLevel.Beginner => "(Reply in English; teach Chinese only as 汉字 [meaning]; no pinyin; at most 3 short sentences.)",
            ImmersionLevel.Intermediate => "(Reply in simple Mandarin, glossing new words as 汉字 [meaning]; no pinyin; at most 3 short sentences.)",
            _ => "(Reply only in simple Mandarin; no pinyin; at most 3 short sentences.)",
        };

        public static string BuildSystemPrompt(string personaOverride)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(personaOverride) ? Intro : personaOverride.Trim());
            sb.AppendLine();
            sb.AppendLine(Rules);
            sb.AppendLine();
            sb.AppendLine("The market (a short walk southeast of the cabin):");
            foreach (var shop in Catalog.Shops)
            {
                sb.Append($"- {shop.keeperName} ({shop.keeperEnglish}) runs the {shop.hanzi} [{shop.english.ToLower()}]");
                if (shop.buysFish) sb.Append(": buys the player's fish; rarer and bigger fish pay more");
                if (shop.items.Length > 0)
                {
                    sb.Append(": ");
                    sb.Append(string.Join("; ", shop.items.Select(Catalog.Get).Where(i => i != null)
                        .Select(i => $"{i.hanzi} [{i.english.ToLower()}] {Catalog.ChineseNumber(i.price)}块" + (i.packSize > 1 ? $" (pack of {i.packSize})" : ""))));
                }
                sb.AppendLine();
            }
            sb.AppendLine("Fish in the lake: " + string.Join("、", FishDatabase.All.Where(f => f.IsFish).Select(f => $"{f.hanzi} [{f.name.ToLower()}]")) + ".");
            sb.AppendLine("Things people fish up: " + string.Join("、", FishDatabase.All.Where(f => !f.IsFish).Select(f => $"{f.hanzi} [{f.name.ToLower()}]")) + ".");
            sb.AppendLine("Controls, if asked: hold the left mouse to cast, click when the bobber dives, hold to reel but let go when the fish pulls. Hold V to talk to whoever you face; hold B to talk to Mei. " +
                "To feed 汤圆: walk up to her and press F (cat food or treats, no bowl needed), or say 喂汤圆 when she's close. A bowl at camp lets her eat by herself.");
            var notes = SaveSystem.Data.companionNotes;
            if (notes.Count > 0) sb.AppendLine("You remember about the player: " + string.Join("; ", notes.Skip(System.Math.Max(0, notes.Count - 6))));
            // Last, so the small model weighs it most.
            sb.AppendLine();
            sb.AppendLine(LevelRules(SaveSystem.Settings.immersion));
            return sb.ToString().Trim();
        }

        /// <summary>Short per-turn situation line (kept out of the system prompt so the cache stays warm).</summary>
        public static string Situation(string playerActivity, float distanceToPlayer, string location)
        {
            var dn = DayNightCycle.Instance;
            string time = dn != null ? $"{dn.ClockText}, {dn.PhaseDescription}" : "daytime";
            string weather = Weather.Instance != null ? Weather.Instance.Description : "clear skies";
            string activity = string.IsNullOrEmpty(playerActivity) ? "" : $" The player is {playerActivity}.";
            string bucket = Inventory.BucketCount == 0 ? "empty bucket" : $"bucket: {Inventory.DescribeBucketChinese()} (worth about {Inventory.BucketValue}块)";
            var pet = SaveSystem.Data.pet;
            string cat = pet.hunger > 0.7f ? "汤圆 is hungry" : pet.happiness > 0.7f ? "汤圆 is very happy" : "汤圆 is fine";
            string words = VocabNotebook.Entries.Count > 0 ? $" Words the player has learned: {VocabNotebook.RecentForPrompt(8)}." : "";
            return $"[Game: {time}, {weather}. Location: {location}.{activity} Player has {Inventory.Money}块, {bucket}, rod: {Inventory.Rod.hanzi}. {cat}.{words}]";
        }
    }
}
