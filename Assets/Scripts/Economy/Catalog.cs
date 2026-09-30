using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Fishing;

namespace UntitledGame.Economy
{
    public enum ItemCategory { Rod, Bait, Accessory, Furniture, PetSupply, PetConsumable, Wearable }

    /// <summary>
    /// Something you can own. Chinese names live here for the characters' prompts only - the UI shows
    /// English (menus stay English); the Chinese is learned from Mei.
    /// </summary>
    public class ItemDef
    {
        public string id;
        public string english;
        public string hanzi;
        public ItemCategory category;
        public int price;
        public int packSize = 1;          // consumables come in packs
        public bool unique;               // own at most one
        public bool placeable;            // can be put down around the camp
        public string model;              // Kenney kit path, or "proc:<name>" for code-built props
        public float modelScale = 1f;
        public string description;

        // Rod stats.
        public float castDistance;
        public float reelSpeed = 1f;
        public float tensionRate = 1f;
        public float lineStrength = 0.35f;
        public float rareBonus;

        // Bait effect (weight multipliers).
        public float commonMult = 1f, uncommonMult = 1f, rareMult = 1f, legendaryMult = 1f, biteSpeed = 1f;
        public string[] favouredFish;
        public bool nightOnly;

        public bool IsConsumable => category == ItemCategory.Bait || category == ItemCategory.PetConsumable;
    }

    public class ShopDef
    {
        public string id;
        public string english;          // "Tackle Shop" (UI)
        public string hanzi;            // 渔具店 (sign)
        public string keeperName;       // 老王
        public string keeperEnglish;    // Old Wang
        public string voice;            // SpeechEngine voice id
        public float pitch = 1f;
        public string personality;
        public bool buysFish;
        public string[] items;
    }

    public static class Catalog
    {
        public const string StarterRod = "rod_old";

        public static readonly List<ItemDef> Items = new List<ItemDef>
        {
            // ---- Rods
            new ItemDef { id = StarterRod, english = "Old Rod", hanzi = "旧鱼竿", category = ItemCategory.Rod, price = 0, unique = true,
                castDistance = 17f, reelSpeed = 1f, tensionRate = 1f, lineStrength = 0.35f, description = "Grandpa's old rod. Creaky but loyal." },
            new ItemDef { id = "rod_bamboo", english = "Bamboo Rod", hanzi = "竹鱼竿", category = ItemCategory.Rod, price = 120, unique = true,
                castDistance = 20f, reelSpeed = 1.15f, tensionRate = 0.9f, lineStrength = 0.5f, description = "Light and springy. Casts further." },
            new ItemDef { id = "rod_carbon", english = "Carbon Rod", hanzi = "碳素鱼竿", category = ItemCategory.Rod, price = 450, unique = true,
                castDistance = 24f, reelSpeed = 1.3f, tensionRate = 0.8f, lineStrength = 0.7f, rareBonus = 0.2f, description = "Strong and fast. Rare fish notice." },
            new ItemDef { id = "rod_gold", english = "Golden Rod", hanzi = "金鱼竿", category = ItemCategory.Rod, price = 1200, unique = true,
                castDistance = 28f, reelSpeed = 1.5f, tensionRate = 0.7f, lineStrength = 0.9f, rareBonus = 0.45f, description = "Legends say the old sturgeon can't resist it." },

            // ---- Bait (consumed when a fish bites)
            new ItemDef { id = "bait_worm", english = "Earthworms", hanzi = "蚯蚓", category = ItemCategory.Bait, price = 20, packSize = 10,
                biteSpeed = 1.35f, commonMult = 1.3f, description = "Fish bite faster." },
            new ItemDef { id = "bait_shrimp", english = "Shrimp Bait", hanzi = "虾饵", category = ItemCategory.Bait, price = 45, packSize = 10,
                uncommonMult = 1.9f, rareMult = 1.3f, description = "Bigger fish love shrimp." },
            new ItemDef { id = "bait_dough", english = "Dough Bait", hanzi = "面饵", category = ItemCategory.Bait, price = 30, packSize = 10,
                favouredFish = new[] { "crucian", "carp", "grass_carp", "koi" }, description = "Carp can't resist it." },
            new ItemDef { id = "bait_glow", english = "Glow Lure", hanzi = "夜光饵", category = ItemCategory.Bait, price = 150, packSize = 5,
                nightOnly = true, rareMult = 2f, legendaryMult = 3.5f, description = "Glows in the dark. Night legends only." },

            // ---- Accessories
            new ItemDef { id = "bucket_big", english = "Big Bucket", hanzi = "大水桶", category = ItemCategory.Accessory, price = 200, unique = true,
                model = "SurvivalKit/bucket", description = "Holds 16 fish instead of 8." },
            new ItemDef { id = "line_strong", english = "Strong Line", hanzi = "鱼线", category = ItemCategory.Accessory, price = 160, unique = true,
                description = "Survives a little more tension before snapping." },
            new ItemDef { id = "bobber_fancy", english = "Fancy Float", hanzi = "浮漂", category = ItemCategory.Accessory, price = 60, unique = true,
                description = "Easier to see: more time to react to bites." },

            // ---- Furniture (placeable around the camp)
            new ItemDef { id = "chair", english = "Cushioned Chair", hanzi = "椅子", category = ItemCategory.Furniture, price = 80, placeable = true, model = "FurnitureKit/chairCushion", modelScale = 0.22f },
            new ItemDef { id = "table", english = "Round Table", hanzi = "桌子", category = ItemCategory.Furniture, price = 120, placeable = true, model = "FurnitureKit/tableRound", modelScale = 0.22f },
            new ItemDef { id = "sofa", english = "Sofa", hanzi = "沙发", category = ItemCategory.Furniture, price = 300, placeable = true, model = "FurnitureKit/loungeSofa", modelScale = 0.22f },
            new ItemDef { id = "floor_lamp", english = "Floor Lamp", hanzi = "灯", category = ItemCategory.Furniture, price = 150, placeable = true, model = "FurnitureKit/lampRoundFloor", modelScale = 0.22f },
            new ItemDef { id = "plant", english = "Potted Plant", hanzi = "盆栽", category = ItemCategory.Furniture, price = 60, placeable = true, model = "FurnitureKit/pottedPlant", modelScale = 0.2f },
            new ItemDef { id = "rug", english = "Round Rug", hanzi = "地毯", category = ItemCategory.Furniture, price = 90, placeable = true, model = "FurnitureKit/rugRound", modelScale = 0.22f },
            new ItemDef { id = "bookcase", english = "Bookcase", hanzi = "书架", category = ItemCategory.Furniture, price = 200, placeable = true, model = "FurnitureKit/bookcaseOpen", modelScale = 0.2f },
            new ItemDef { id = "bench", english = "Garden Bench", hanzi = "长椅", category = ItemCategory.Furniture, price = 140, placeable = true, model = "HolidayKit/bench", modelScale = 1.4f },
            new ItemDef { id = "street_lamp", english = "Lamp Post", hanzi = "路灯", category = ItemCategory.Furniture, price = 110, placeable = true, model = "FantasyTown/lantern", modelScale = 1.25f },
            new ItemDef { id = "radio", english = "Radio", hanzi = "收音机", category = ItemCategory.Furniture, price = 180, placeable = true, model = "FurnitureKit/radio", modelScale = 0.18f },
            new ItemDef { id = "teddy", english = "Teddy Bear", hanzi = "玩具熊", category = ItemCategory.Furniture, price = 70, placeable = true, model = "FurnitureKit/bear", modelScale = 0.15f },

            // ---- Pet shop
            new ItemDef { id = "cat_food", english = "Cat Food", hanzi = "猫粮", category = ItemCategory.PetConsumable, price = 15, packSize = 5, description = "Fill Tangyuan's bowl." },
            new ItemDef { id = "cat_treat", english = "Dried Fish Treats", hanzi = "小鱼干", category = ItemCategory.PetConsumable, price = 25, packSize = 5, description = "Tangyuan's favourite snack." },
            new ItemDef { id = "cat_bed", english = "Cat Bed", hanzi = "猫窝", category = ItemCategory.PetSupply, price = 150, unique = true, placeable = true, model = "proc:cat_bed" },
            new ItemDef { id = "cat_box", english = "Cardboard Box", hanzi = "纸箱", category = ItemCategory.PetSupply, price = 20, unique = true, placeable = true, model = "FurnitureKit/cardboardBoxOpen", modelScale = 0.2f },
            new ItemDef { id = "cat_bowl", english = "Food Bowl", hanzi = "猫碗", category = ItemCategory.PetSupply, price = 40, unique = true, placeable = true, model = "proc:cat_bowl" },
            new ItemDef { id = "yarn", english = "Yarn Ball", hanzi = "毛线球", category = ItemCategory.PetSupply, price = 30, unique = true, placeable = true, model = "proc:yarn" },
            new ItemDef { id = "scratcher", english = "Scratching Post", hanzi = "猫抓板", category = ItemCategory.PetSupply, price = 120, unique = true, placeable = true, model = "proc:scratcher" },
            new ItemDef { id = "collar", english = "Bell Collar", hanzi = "铃铛", category = ItemCategory.Wearable, price = 90, unique = true, description = "A little bell for Tangyuan. Jingle!" },
        };

        public static readonly List<ShopDef> Shops = new List<ShopDef>
        {
            new ShopDef { id = "tackle", english = "Tackle Shop", hanzi = "渔具店", keeperName = "老王", keeperEnglish = "Old Wang",
                voice = "zh_male", pitch = 0.93f,
                personality = "a gruff but kind old fisherman who loves to brag about big catches and gives honest advice about rods and bait",
                items = new[] { "rod_bamboo", "rod_carbon", "rod_gold", "bait_worm", "bait_shrimp", "bait_dough", "bait_glow", "bucket_big", "line_strong", "bobber_fancy" } },
            new ShopDef { id = "fish", english = "Fish Market", hanzi = "鱼店", keeperName = "陈阿姨", keeperEnglish = "Auntie Chen",
                voice = "zh_female", pitch = 0.94f, buysFish = true,
                personality = "a warm, chatty auntie who buys fish from local anglers, fusses over customers and shares cooking tips",
                items = new string[0] },
            new ShopDef { id = "furniture", english = "Furniture Shop", hanzi = "家具店", keeperName = "李师傅", keeperEnglish = "Master Li",
                voice = "zh_male", pitch = 1.05f,
                personality = "a calm, precise carpenter who is proud of his work and speaks slowly and politely",
                items = new[] { "chair", "table", "sofa", "floor_lamp", "plant", "rug", "bookcase", "bench", "street_lamp", "radio", "teddy" } },
            new ShopDef { id = "pet", english = "Pet Shop", hanzi = "宠物店", keeperName = "小林", keeperEnglish = "Xiao Lin",
                voice = "zh_female", pitch = 1.08f,
                personality = "a cheerful young woman who adores cats and always asks about Tangyuan",
                items = new[] { "cat_food", "cat_treat", "cat_bed", "cat_box", "cat_bowl", "yarn", "scratcher", "collar" } },
        };

        public static ItemDef Get(string id) => Items.FirstOrDefault(i => i.id == id);
        public static ShopDef Shop(string id) => Shops.FirstOrDefault(s => s.id == id);

        /// <summary>What the fishmonger pays for a catch.</summary>
        public static int FishPrice(FishSpecies s, float length)
        {
            int baseValue = s.rarity switch
            {
                Rarity.Junk => 3,
                Rarity.Common => 12,
                Rarity.Uncommon => 35,
                Rarity.Rare => 110,
                Rarity.Legendary => 520,
                _ => 5,
            };
            if (!s.IsFish) return baseValue;
            float t = Mathf.InverseLerp(s.minLength, s.maxLength, length);
            return Mathf.Max(1, Mathf.RoundToInt(baseValue * Mathf.Lerp(0.8f, 1.5f, t)));
        }

        /// <summary>The measure word for counting an item: 一根鱼竿, 两包蚯蚓, 一个猫碗, 一把椅子...</summary>
        public static string MeasureWord(ItemDef item)
        {
            if (item.packSize > 1) return "包";
            switch (item.id)
            {
                case "chair": return "把";
                case "table": case "sofa": case "bench": return "张";
                case "rug": return "块";
                case "plant": return "盆";
                case "line_strong": return "卷";
                case "radio": return "台";
                case "floor_lamp": case "street_lamp": return "盏";
                case "teddy": return "只";
            }
            return item.category == ItemCategory.Rod ? "根" : "个";
        }

        /// <summary>A number used for counting things: 两 instead of 二 for two (两包, not 二包).</summary>
        public static string CountNumber(int n) => n == 2 ? "两" : ChineseNumber(n);

        /// <summary>"两包蚯蚓", "一根竹鱼竿".</summary>
        public static string Counted(ItemDef item, int n) => CountNumber(n) + MeasureWord(item) + item.hanzi;

        /// <summary>Numbers written in Chinese characters (so prices double as reading practice).</summary>
        public static string ChineseNumber(int n)
        {
            if (n == 0) return "零";
            string[] d = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            string[] u = { "", "十", "百", "千" };
            if (n >= 10000) return (n / 10000 == 2 ? "两" : ChineseNumber(n / 10000)) + "万" + (n % 10000 == 0 ? "" : (n % 10000 < 1000 ? "零" : "") + ChineseNumber(n % 10000));
            var sb = new System.Text.StringBuilder();
            string s = n.ToString();
            bool zero = false;
            for (int i = 0; i < s.Length; i++)
            {
                int digit = s[i] - '0';
                int pos = s.Length - 1 - i;
                if (digit == 0) { zero = sb.Length > 0; continue; }
                if (zero) { sb.Append("零"); zero = false; }
                if (!(digit == 1 && pos == 1 && sb.Length == 0)) sb.Append(digit == 2 && pos >= 2 ? "两" : d[digit]);
                sb.Append(u[pos]);
            }
            return sb.ToString();
        }
    }
}
