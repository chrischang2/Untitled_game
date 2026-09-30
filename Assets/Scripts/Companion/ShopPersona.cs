using System.Linq;
using System.Text;
using UntitledGame.Economy;
using UntitledGame.Fishing;

namespace UntitledGame.Companion
{
    /// <summary>Prompts for a Mandarin-only shopkeeper and for the shop's intent classifier.</summary>
    public static class ShopPersona
    {
        public static string BuildSystemPrompt(ShopDef shop)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {shop.keeperName}, {shop.personality}. You run the {shop.hanzi} at the little market by Willow Lake (柳湖).");
            sb.AppendLine("You ONLY speak Mandarin Chinese in Simplified characters. You do not understand English at all.");
            sb.AppendLine("Your customer is a foreigner who is just starting to learn Mandarin, so speak slowly and simply: one or two very short sentences " +
                          "with everyday words (HSK 1-2 level). Be friendly and patient; you are happy they are trying.");
            sb.AppendLine("Write numbers and prices in Chinese characters (一百二十块). Never use English, pinyin, lists, markdown or emoji. Stay in character.");
            sb.AppendLine("Lines in [Game: ...] tell you what just happened in your shop (which item the customer asked for, its price, whether they can " +
                          "afford it, or that a sale was completed). Follow them exactly and describe the result naturally in Mandarin. Never read them out " +
                          "and never invent prices.");
            sb.AppendLine("The customer's words (顾客说：「…」) come through speech recognition and are often slightly wrong because of their accent. " +
                          "Never repeat their words back, never comment on strange words and never pretend they said something else: the [Game] note tells you what they meant.");
            if (shop.items.Length > 0)
            {
                sb.AppendLine("What you sell: " + string.Join("；", shop.items.Select(Catalog.Get).Where(i => i != null)
                    .Select(i => $"{i.hanzi} {Catalog.ChineseNumber(i.price)}块" + (i.packSize > 1 ? $"（一包{Catalog.ChineseNumber(i.packSize)}个）" : ""))) + "。");
            }
            if (shop.buysFish)
            {
                sb.AppendLine("You buy fish that local anglers catch. The price depends on the kind of fish and its size; the game tells you the total. " +
                              "You also love chatting about how to cook fish.");
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
                    sb.AppendLine($"- {i.id}: {i.hanzi} = {i.english}, {i.price} yuan");
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
            var intents = shop.buysFish
                ? new[] { "sell_fish", "ask_price", "browse", "confirm", "decline", "greeting", "thanks", "goodbye", "other" }
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
