using System.Linq;
using System.Text;
using UntitledGame.Economy;
using UntitledGame.Fishing;
using UntitledGame.Progression;

namespace UntitledGame.Companion
{
    /// <summary>Prompts for a Mandarin-only shopkeeper and for the shop's intent classifier.</summary>
    public static class ShopPersona
    {
        public static string BuildSystemPrompt(ShopDef shop)
        {
            var sb = new StringBuilder();
            var profile = KeeperProfiles.For(shop.id);
            string personality = profile != null ? profile.personality : shop.personality;
            sb.AppendLine($"You are {shop.keeperName}, {personality}. You run the {shop.hanzi} at the little market in the seaside village of Willow Bay (柳湾).");
            if (profile != null) sb.AppendLine($"How you talk: {profile.speakingStyle}.");
            sb.AppendLine("You ONLY speak Mandarin Chinese in Simplified characters. You do not understand English at all.");
            sb.AppendLine("Your customer is a foreigner learning Mandarin, so speak slowly and simply: one or two very short sentences. " +
                          "Use ONLY very common words (HSK levels 1-3, the first 600 words a learner meets), apart from the names of your goods. " +
                          "Be friendly and patient; you are happy they are trying.");
            if (profile != null)
            {
                sb.AppendLine("About you (only share these when the customer asks or the game tells you to; never all at once): " +
                              string.Join(" ", profile.facts.Select(f => f.chinese)) +
                              $" 你喜欢：{string.Join("、", profile.likes)}。你不喜欢：{string.Join("、", profile.dislikes)}。");
                sb.AppendLine("Things you love talking about: " + string.Join("、", profile.topics) + ".");
                int level = Affinity.Level(shop.id);
                sb.AppendLine($"How well you know this customer: {Affinity.LevelHanzi[level]} ({Affinity.LevelEnglish[level]}). " +
                              (level == 0 ? "You have only just met: be polite and friendly." : level >= 3 ? "You are close friends: be warm and cheerful with them." : "You know them a bit: be warm."));
            }
            if (shop.id == "gifts")
            {
                sb.AppendLine("You know what everyone at the market likes, and you love telling customers so they can pick good gifts: " +
                    string.Join("；", KeeperProfiles.All.Where(p => p.shopId != "gifts").Select(p =>
                        $"{Catalog.Shop(p.shopId)?.keeperName}喜欢{string.Join("和", p.likes)}，不喜欢{string.Join("和", p.dislikes)}")) + "。");
            }
            sb.AppendLine("Write numbers and prices in Chinese characters (一百二十块). Never use English, pinyin, lists, markdown or emoji. Stay in character.");
            sb.AppendLine("Lines in [Game: ...] tell you what just happened in your shop (which item the customer asked for, its price, whether they can " +
                          "afford it, or that a sale was completed). Follow them exactly and describe the result naturally in Mandarin. Never read them out " +
                          "and never invent prices.");
            sb.AppendLine("The customer's words (顾客说：「…」) come through speech recognition and are often slightly wrong because of their accent. " +
                          "Never repeat their words back, never comment on strange words and never pretend they said something else: the [Game] note tells you what they meant.");
            if (shop.items.Length > 0)
            {
                sb.AppendLine("What you sell: " + string.Join("；", shop.items.Select(Catalog.Get).Where(i => i != null)
                    .Select(i => $"{i.hanzi} {Catalog.ChineseNumber(Catalog.PriceOf(i))}块" + (i.packSize > 1 ? $"（一包{Catalog.ChineseNumber(i.packSize)}个）" : ""))) + "。");
            }
            if (shop.school)
            {
                sb.AppendLine("You run the HSK test centre. The game runs your lessons (上课), free practice (练习) and HSK 1-3 tests (考试) itself when " +
                              "the student asks for them; in between you just chat. Encourage them to study, and keep your Chinese very simple.");
            }
            if (shop.buysFish)
            {
                sb.AppendLine("You are a sushi chef with a little sushi bar (寿司店). You buy the fish local anglers catch and weigh it all on your old scale; " +
                              "the more they bring at once, the more you pay per fish (the game tells you the total). You love talking about which fish make the best sushi.");
            }
            if (shop.crabber)
            {
                sb.AppendLine("You keep crab pots (螃蟹笼) in the sea off the beach, including the customer's own pots. Every morning you empty them and sell the crabs; " +
                              "the customer comes down to collect the money (the game tells you how much). Fish they give you go in the pots as bait, so there are more crabs the next day. " +
                              "You also sell upgrades for their pots.");
            }
            sb.AppendLine("Regulars: Mei (美) often comes by with the customer. The customer's cat is called 汤圆.");
            return sb.ToString().Trim();
        }

        public static string BuildClassifierPrompt(ShopDef shop, string pendingOffer)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You interpret what a customer said to {shop.keeperName} at the {shop.english} ({shop.hanzi}).");
            sb.AppendLine("The customer is a Mandarin learner and the text comes from speech recognition, so it may contain homophone mistakes " +
                          "(for example 雨干 instead of 鱼竿). Match items by sound and meaning.");
            if (shop.items.Length > 0)
            {
                sb.AppendLine("Items for sale (id: Chinese = English, price):");
                foreach (var i in shop.items.Select(Catalog.Get).Where(i => i != null))
                    sb.AppendLine($"- {i.id}: {i.hanzi} = {i.english}, {Catalog.PriceOf(i)} yuan");
            }
            if (shop.crabber)
            {
                sb.AppendLine("Here, giving or selling fish (给你鱼 / 放鱼 / 卖鱼) means putting them in the crab pots as bait: intent sell_fish. Use fish \"all\" unless they name one kind.");
                sb.AppendLine("Fish: " + string.Join(", ", FishDatabase.All.Select(f => $"{f.id} = {f.hanzi} ({f.name})")));
            }
            if (shop.buysFish)
            {
                sb.AppendLine("This shop BUYS fish from the customer (intent sell_fish). Use fish \"all\" unless they name one kind.");
                sb.AppendLine("Fish: " + string.Join(", ", FishDatabase.All.Select(f => $"{f.id} = {f.hanzi} ({f.name})")));
            }
            sb.AppendLine($"Pending offer waiting for the customer's answer: {pendingOffer}.");
            sb.AppendLine("Intents: buy (wants to purchase an item), ask_price (asks how much something costs), sell_fish (wants to sell fish), " +
                          "confirm (says yes to the pending offer, e.g. 好的/要/可以/买吧/行), decline (says no / changes their mind, e.g. 不要/算了/不用了), " +
                          "browse (asks what you sell / what else there is, e.g. 你卖什么/有什么/还有什么/我看看), greeting, thanks (谢谢), goodbye, other (small talk or anything else).");
            sb.AppendLine("If they want to buy or ask about something but it is not clear which item, use item \"unclear\". Quantity defaults to 1.");
            return sb.ToString().Trim();
        }

        public static string ClassifierSchema(ShopDef shop)
        {
            var intents = shop.buysFish || shop.crabber
                ? new[] { "sell_fish", "buy", "ask_price", "browse", "confirm", "decline", "greeting", "thanks", "goodbye", "other" }
                : new[] { "buy", "ask_price", "browse", "confirm", "decline", "greeting", "thanks", "goodbye", "other" };
            var items = shop.items.Concat(new[] { "unclear", "none" });
            var fish = FishDatabase.All.Select(f => f.id).Concat(new[] { "all", "none" });
            string Enum(System.Collections.Generic.IEnumerable<string> v) => "[" + string.Join(",", v.Select(x => "\"" + x + "\"")) + "]";
            return "{\"type\":\"object\",\"properties\":{" +
                   "\"intent\":{\"type\":\"string\",\"enum\":" + Enum(intents) + "}," +
                   "\"item\":{\"type\":\"string\",\"enum\":" + Enum(items) + "}," +
                   "\"quantity\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":20}," +
                   "\"fish\":{\"type\":\"string\",\"enum\":" + Enum(fish) + "}" +
                   "},\"required\":[\"intent\",\"item\",\"quantity\",\"fish\"]}";
        }
    }
}
