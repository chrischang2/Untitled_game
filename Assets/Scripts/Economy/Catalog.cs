using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Fishing;

namespace UntitledGame.Economy
{
    public enum ItemCategory { Rod, Bait, Accessory, Furniture, PetSupply, PetConsumable, Wearable, Book, Service, Gift, Boat, Line, Upgrade, Bed, Cosmetic, Card }

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

        // Rods (no longer sold: casting distance is a stat now; kept so older saves still load).
        public float castDistance;

        // Bait: each fish bites only on the baits it likes (FishSpecies.baits); some baits also make fish bite sooner.
        public float biteSpeed = 1f;

        // Lines: the heaviest fighters need a stronger line (kg).
        public float lineKg;

        // Stat upgrades (Coach Wu): which stat, and the price of level 1 (each level costs one more step).
        public string stat;

        // Furniture: how much it adds to the home's comfort (more comfort = more energy each day).
        public int comfort;
        // Beds: bed level (more energy). Buckets: how many bag slots you have.
        public int bedLevel;
        public int bagSlots;
        // Boat upgrades: how far out (metres from shore) the boat can go before the currents push it back.
        public float boatRange;
        // Cosmetics: which thing it changes ("boat", "cat", "mei", "house") and the colour.
        public string cosmeticFor;
        public string colorHex;

        /// <summary>Only fish take bag slots; every item is a key item that never fills the bag.</summary>
        public bool TakesSlot => false;

        // Books: the fish you discover by reading it.
        public string[] teachesFish;
        /// <summary>Furniture and beds belong to one region's house (-1 = any region, like the sleeping mat).</summary>
        public int region = -1;
        /// <summary>Gives off light at night (lamps, lanterns, stoves).</summary>
        public bool glows;

        /// <summary>Friendship level (Progression.Affinity) the shopkeeper needs before they'll sell it.</summary>
        public int minAffinity;
        /// <summary>HSK test (Progression.Hsk, at 高老师's test centre) the player must have passed before it's sold. Both checks apply.</summary>
        public int minHsk;

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
        public bool school;             // 高老师's test centre: lessons, practice and HSK tests (no goods)
        public bool crabber;            // 海叔's crab pots: passive income, collected at his stall (CrabPots)
        public bool busDriver;          // 张师傅 at the bus stop: sells tickets and drives you on (BusTrip)
        /// <summary>What kind of stall ("tackle", "fish", ... "crabber", "bus"); Willow Bay's keepers' ids are their role.</summary>
        public string role;
        /// <summary>The region the keeper lives in (-1 = travels with you: the bus driver).</summary>
        public int region;
        /// <summary>The stall they keep (WorldShape.StallPosition), the same in every region.</summary>
        public int stall;
        public bool female;
        public string[] items;
    }

    public static class Catalog
    {
        public const string StarterRod = "rod_old";

        public static readonly List<ItemDef> Items = new List<ItemDef>
        {
            // ---- Rods: not sold any more (older saves may own them). Everyone fishes with the old rod.
            new ItemDef { id = StarterRod, english = "Old Rod", hanzi = "旧鱼竿", category = ItemCategory.Rod, price = 0, unique = true, description = "Grandpa's old rod. Creaky but loyal." },
            new ItemDef { id = "rod_bamboo", english = "Bamboo Rod", hanzi = "竹鱼竿", category = ItemCategory.Rod, price = 120, unique = true },
            new ItemDef { id = "rod_carbon", english = "Carbon Rod", hanzi = "碳素鱼竿", category = ItemCategory.Rod, price = 450, unique = true },
            new ItemDef { id = "rod_gold", english = "Golden Rod", hanzi = "金鱼竿", category = ItemCategory.Rod, price = 1200, unique = true },

            // ---- Bait (one is used each time a fish bites). Each fish only bites on the baits it likes.
            new ItemDef { id = "bait_worm", english = "Earthworms", hanzi = "蚯蚓", category = ItemCategory.Bait, price = 20, packSize = 10,
                biteSpeed = 1.3f, description = "Everyday bait: small fish near the beach love it." },
            new ItemDef { id = "bait_dough", english = "Dough Bait", hanzi = "面饵", category = ItemCategory.Bait, price = 25, packSize = 10,
                biteSpeed = 1.2f, description = "Soft and cheap. Sardines like it." },
            new ItemDef { id = "bait_shrimp", english = "Shrimp Bait", hanzi = "虾饵", category = ItemCategory.Bait, price = 45, packSize = 10,
                biteSpeed = 1.15f, description = "Most shore fish can't resist shrimp." },
            new ItemDef { id = "bait_squid", minHsk = 1, minAffinity = 1, english = "Squid Strips", hanzi = "鱿鱼", category = ItemCategory.Bait, price = 70, packSize = 5,
                biteSpeed = 1.1f, description = "Tough, smelly, and loved by hunters of the deep." },
            new ItemDef { id = "bait_crab", minHsk = 1, minAffinity = 1, english = "Little Crabs", hanzi = "小螃蟹", category = ItemCategory.Bait, price = 90, packSize = 5,
                biteSpeed = 1.1f, description = "Rock-dwellers like octopus and grouper go mad for crab." },
            new ItemDef { id = "bait_fish", minHsk = 2, minAffinity = 2, english = "Live Baitfish", hanzi = "小活鱼", category = ItemCategory.Bait, price = 120, packSize = 5,
                description = "Big predators of the open sea only want something alive." },
            new ItemDef { id = "bait_glow", minHsk = 2, minAffinity = 2, english = "Glow Lure", hanzi = "夜光饵", category = ItemCategory.Bait, price = 150, packSize = 5,
                description = "Glows in the dark water. Said to call up something strange." },

            // ---- Lines: big fish need a stronger line (you start with a 3 kg white line)
            new ItemDef { id = "line_red", english = "Red Line (6 kg)", hanzi = "红线", category = ItemCategory.Line, price = 100, unique = true, lineKg = 6f,
                description = "Holds fish up to about 6 kg." },
            new ItemDef { id = "line_blue", minHsk = 1, minAffinity = 1, english = "Blue Line (15 kg)", hanzi = "蓝线", category = ItemCategory.Line, price = 300, unique = true, lineKg = 15f,
                description = "Strong enough for eels and grouper." },
            new ItemDef { id = "line_black", minHsk = 2, minAffinity = 2, english = "Black Line (40 kg)", hanzi = "黑线", category = ItemCategory.Line, price = 800, unique = true, lineKg = 40f,
                description = "For sailfish and sharks." },
            new ItemDef { id = "line_gold", minHsk = 3, minAffinity = 3, english = "Gold Line (80 kg)", hanzi = "金线", category = ItemCategory.Line, price = 2000, unique = true, lineKg = 80f,
                description = "Old Wang's best. Nothing in the sea can break it." },

            // ---- Accessories
            // Buckets: more bag slots (everything you carry takes a slot; you start with 4).
            new ItemDef { id = "bucket_big", english = "Big Bucket (8 slots)", hanzi = "大水桶", category = ItemCategory.Accessory, price = 200, unique = true, bagSlots = 8,
                model = "SurvivalKit/bucket", description = "Carry 8 slots of things instead of 4." },
            new ItemDef { id = "bucket_huge", minHsk = 1, minAffinity = 1, english = "Huge Bucket (12 slots)", hanzi = "很大的水桶", category = ItemCategory.Accessory, price = 600, unique = true, bagSlots = 12,
                description = "Carry 12 slots of things." },
            new ItemDef { id = "bucket_giant", minHsk = 2, minAffinity = 2, english = "Giant Bucket (16 slots)", hanzi = "最大的水桶", category = ItemCategory.Accessory, price = 1500, unique = true, bagSlots = 16,
                description = "Carry 16 slots: enough for a long day out at sea." },
            new ItemDef { id = "line_strong", english = "Strong Line (old)", hanzi = "鱼线", category = ItemCategory.Accessory, price = 160, unique = true },
            new ItemDef { id = "bobber_fancy", english = "Fancy Float", hanzi = "浮漂", category = ItemCategory.Accessory, price = 60, unique = true,
                description = "Easier to see: more time to react to bites." },

            // ---- Furniture (placeable around the camp)
            new ItemDef { id = "chair", region = 0, comfort = 1, english = "Cushioned Chair", hanzi = "椅子", category = ItemCategory.Furniture, price = 80, placeable = true, model = "FurnitureKit/chairCushion", modelScale = 0.22f },
            new ItemDef { id = "table", region = 0, comfort = 1, english = "Round Table", hanzi = "桌子", category = ItemCategory.Furniture, price = 120, placeable = true, model = "FurnitureKit/tableRound", modelScale = 0.22f },
            new ItemDef { id = "sofa", region = 0, comfort = 3, minAffinity = 2, english = "Sofa", hanzi = "沙发", category = ItemCategory.Furniture, price = 300, placeable = true, model = "FurnitureKit/loungeSofa", modelScale = 0.22f },
            new ItemDef { id = "floor_lamp", region = 0, comfort = 2, english = "Floor Lamp", hanzi = "灯", category = ItemCategory.Furniture, price = 150, placeable = true, model = "FurnitureKit/lampRoundFloor", modelScale = 0.22f, glows = true },
            new ItemDef { id = "plant", region = 0, comfort = 1, english = "Potted Plant", hanzi = "盆栽", category = ItemCategory.Furniture, price = 60, placeable = true, model = "FurnitureKit/pottedPlant", modelScale = 0.2f },
            new ItemDef { id = "rug", region = 0, comfort = 2, english = "Round Rug", hanzi = "地毯", category = ItemCategory.Furniture, price = 90, placeable = true, model = "FurnitureKit/rugRound", modelScale = 0.22f },
            new ItemDef { id = "bookcase", region = 0, comfort = 2, minAffinity = 1, english = "Bookcase", hanzi = "书架", category = ItemCategory.Furniture, price = 200, placeable = true, model = "FurnitureKit/bookcaseOpen", modelScale = 0.2f },
            new ItemDef { id = "bench", region = 0, comfort = 1, english = "Garden Bench", hanzi = "长椅", category = ItemCategory.Furniture, price = 140, placeable = true, model = "HolidayKit/bench", modelScale = 1.4f },
            new ItemDef { id = "street_lamp", region = 0, comfort = 1, english = "Lamp Post", hanzi = "路灯", category = ItemCategory.Furniture, price = 110, placeable = true, model = "FantasyTown/lantern", modelScale = 1.25f, glows = true },
            new ItemDef { id = "radio", region = 0, comfort = 2, minAffinity = 2, english = "Radio", hanzi = "收音机", category = ItemCategory.Furniture, price = 180, placeable = true, model = "FurnitureKit/radio", modelScale = 0.18f },
            new ItemDef { id = "teddy", region = 0, comfort = 1, english = "Teddy Bear", hanzi = "玩具熊", category = ItemCategory.Furniture, price = 70, placeable = true, model = "FurnitureKit/bear", modelScale = 0.15f },

            // ---- Beds (Master Li): the house comes with a sleeping mat; better beds give more energy each day
            new ItemDef { id = "bed_mat", english = "Sleeping Mat", hanzi = "床垫", category = ItemCategory.Bed, price = 0, unique = true, bedLevel = 0,
                model = "SurvivalKit/bedroll", modelScale = 2.6f, description = "Thin, but it's yours. 100 energy." },
            new ItemDef { id = "bed_single", region = 0, english = "Single Bed", hanzi = "单人床", category = ItemCategory.Bed, price = 300, unique = true, bedLevel = 1,
                model = "FurnitureKit/bedSingle", modelScale = 0.22f, description = "A real bed. 140 energy each morning." },
            new ItemDef { id = "bed_double", region = 0, minAffinity = 1, english = "Double Bed", hanzi = "双人床", category = ItemCategory.Bed, price = 800, unique = true, bedLevel = 2,
                model = "FurnitureKit/bedDouble", modelScale = 0.22f, description = "Wide and soft. 180 energy each morning." },
            new ItemDef { id = "bed_big", region = 0, minAffinity = 2, english = "Big Soft Bed", hanzi = "大床", category = ItemCategory.Bed, price = 2000, unique = true, bedLevel = 3,
                model = "FurnitureKit/bedDouble", modelScale = 0.27f, description = "Master Li's finest. 230 energy each morning." },

            // ---- Pet shop
            new ItemDef { id = "cat_food", english = "Cat Food", hanzi = "猫粮", category = ItemCategory.PetConsumable, price = 15, packSize = 5, description = "Fill Tangyuan's bowl." },
            new ItemDef { id = "cat_treat", english = "Dried Fish Treats", hanzi = "小鱼干", category = ItemCategory.PetConsumable, price = 25, packSize = 5, description = "Tangyuan's favourite snack." },
            new ItemDef { id = "cat_bed", minAffinity = 1, english = "Cat Bed", hanzi = "猫窝", category = ItemCategory.PetSupply, price = 150, unique = true, placeable = true, model = "proc:cat_bed" },
            new ItemDef { id = "cat_box", english = "Cardboard Box", hanzi = "纸箱", category = ItemCategory.PetSupply, price = 20, unique = true, placeable = true, model = "FurnitureKit/cardboardBoxOpen", modelScale = 0.2f },
            new ItemDef { id = "cat_bowl", english = "Food Bowl", hanzi = "猫碗", category = ItemCategory.PetSupply, price = 40, unique = true, placeable = true, model = "proc:cat_bowl" },
            new ItemDef { id = "yarn", english = "Yarn Ball", hanzi = "毛线球", category = ItemCategory.PetSupply, price = 30, unique = true, placeable = true, model = "proc:yarn" },
            new ItemDef { id = "scratcher", minAffinity = 1, english = "Scratching Post", hanzi = "猫抓板", category = ItemCategory.PetSupply, price = 120, unique = true, placeable = true, model = "proc:scratcher" },
            new ItemDef { id = "collar", minAffinity = 2, english = "Bell Collar", hanzi = "铃铛", category = ItemCategory.Wearable, price = 90, unique = true, description = "A little bell for Tangyuan. Jingle!" },

            // ---- Bookshop: reading a book discovers new fish (you can only catch fish you know about)
            // Each book teaches its fish: where they swim, what bait they like and what line they need (see the journal).
            new ItemDef { id = "book_basics", english = "Fishing for Beginners", hanzi = "钓鱼入门", category = ItemCategory.Book, price = 40, unique = true,
                teachesFish = new[] { "croaker", "flounder", "mackerel", "herring", "pufferfish", "redgurnard" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "The fish you meet first, and how to catch them." },
            new ItemDef { id = "book_shore", english = "Fish of the Shore", hanzi = "海边的鱼", category = ItemCategory.Book, price = 150, unique = true,
                teachesFish = new[] { "seabream", "seabass", "hairtail", "cuttlefish", "blackporgy" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "Bigger fish along the shore, for a stronger line." },
            new ItemDef { id = "book_bay", minAffinity = 1, english = "Secrets of Willow Bay", hanzi = "柳湾的秘密", category = ItemCategory.Book, price = 260, unique = true,
                teachesFish = new[] { "turbot", "johndory", "yellowtail", "spottedbass", "willowspirit" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "The bay's rare fish, and the legend of the willow spirit." },
            new ItemDef { id = "book_rocks", minHsk = 1, minAffinity = 1, english = "Fish under the Rocks", hanzi = "石头下的鱼", category = ItemCategory.Book, price = 300, unique = true,
                teachesFish = new[] { "octopus", "conger", "grouper", "sweetlips", "goatfish", "triggerfish", "stingray" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "What hides among the warm rocks of Golden Sand Bay." },
            new ItemDef { id = "book_reef", minHsk = 1, minAffinity = 1, english = "Fish of the Coral Reef", hanzi = "珊瑚里的鱼", category = ItemCategory.Book, price = 360, unique = true,
                teachesFish = new[] { "clownfish", "butterflyfish", "bluetang", "angelfish", "parrotfish", "lionfish", "rabbitfish" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "The bright little fish of the coral reef." },
            new ItemDef { id = "book_warm", minHsk = 1, minAffinity = 2, english = "Fish of the Warm Sea", hanzi = "热海的鱼", category = ItemCategory.Book, price = 450, unique = true,
                teachesFish = new[] { "sandsmelt", "flyingfish", "skipjack", "sailfish", "shark", "goldendragon" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "Fast fish of the warm open sea, and the golden sand dragon." },
            new ItemDef { id = "book_open", minHsk = 2, minAffinity = 2, english = "Fish of the Open Sea", hanzi = "远海的鱼", category = ItemCategory.Book, price = 500, unique = true,
                teachesFish = new[] { "tuna", "halibut", "sablefish", "greenlandshark", "narwhal", "pollock" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "Giants of the cold open sea. You will need the boat." },
            new ItemDef { id = "book_ice", minHsk = 2, minAffinity = 2, english = "Fish under the Ice", hanzi = "冰海的鱼", category = ItemCategory.Book, price = 600, unique = true,
                teachesFish = new[] { "capelin", "icefish", "arcticcod", "char", "grayling", "icepike", "auroratrout" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "Fish that love the coldest water." },
            new ItemDef { id = "book_north", minHsk = 2, minAffinity = 3, english = "Fish of the North", hanzi = "北方的鱼", category = ItemCategory.Book, price = 700, unique = true,
                teachesFish = new[] { "cod", "haddock", "saury", "salmon", "lumpfish", "wolffish", "oarfish" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "Northern favourites, and the oarfish of the deep." },
            new ItemDef { id = "book_legends", minHsk = 3, minAffinity = 3, english = "Fish of Mars", hanzi = "火星的鱼", category = ItemCategory.Book, price = 900, unique = true,
                teachesFish = new[] { "dustminnow", "volcanocarp", "irongoby", "purplepuff", "rocketfish", "robotfish", "moonfish" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "What swims in the purple sea of Mars." },
            new ItemDef { id = "book_space", minHsk = 3, minAffinity = 3, english = "Fish from Space", hanzi = "太空的鱼", category = ItemCategory.Book, price = 1100, unique = true,
                teachesFish = new[] { "starsardine", "meteoreel", "crystalfish", "twohead", "sunfish", "alienoctopus", "gravitygrouper" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "Strange fish that fell from the stars." },
            new ItemDef { id = "book_stars", minHsk = 3, minAffinity = 3, english = "Legends of the Stars", hanzi = "星星的传说", category = ItemCategory.Book, price = 1400, unique = true,
                teachesFish = new[] { "galaxymarlin", "whaleshark", "nebula", "mooneel", "leviathan", "starkoi" }, model = "FurnitureKit/books", modelScale = 0.2f,
                description = "The rarest fish in the universe. Bring your best line." },

            // ---- Old Wang's rowboat: lets you row out to deep water, where the big fish are
            new ItemDef { id = "boat", english = "Rowboat (use of Old Wang's boat)", hanzi = "小船", category = ItemCategory.Boat, price = 300, unique = true, minAffinity = 1,
                boatRange = 25f, description = "Row out to sea (up to 25 m: past that the currents push you back). Press F at the boat by the dock." },
            new ItemDef { id = "boat_oars", minHsk = 1, english = "Good Oars (40 m)", hanzi = "好船桨", category = ItemCategory.Boat, price = 400, unique = true, minAffinity = 1,
                boatRange = 40f, description = "Row against the currents out to 40 m from shore." },
            new ItemDef { id = "boat_sail", minHsk = 2, english = "Sail (60 m)", hanzi = "船帆", category = ItemCategory.Boat, price = 1000, unique = true, minAffinity = 2,
                boatRange = 60f, description = "A little sail: 60 m out, where the bluefin tuna swim." },
            new ItemDef { id = "boat_new", minHsk = 3, english = "New Boat (any current)", hanzi = "新船", category = ItemCategory.Boat, price = 2500, unique = true, minAffinity = 3,
                boatRange = 150f, description = "Strong enough for any current: as far out to sea as you like." },

            // ---- Gift shop (刘奶奶): give gifts to shopkeepers by saying so (送给你...); everyone likes different things
            new ItemDef { id = "gift_flowers", english = "Flowers", hanzi = "花", category = ItemCategory.Gift, price = 30, description = "A bunch of fresh flowers." },
            new ItemDef { id = "gift_tea", english = "Tea", hanzi = "茶", category = ItemCategory.Gift, price = 40, description = "Good green tea." },
            new ItemDef { id = "gift_coffee", english = "Coffee", hanzi = "咖啡", category = ItemCategory.Gift, price = 35, description = "Strong coffee beans." },
            new ItemDef { id = "gift_cake", english = "Cake", hanzi = "蛋糕", category = ItemCategory.Gift, price = 45, description = "A little cream cake." },
            new ItemDef { id = "gift_watermelon", english = "Watermelon", hanzi = "西瓜", category = ItemCategory.Gift, price = 25, description = "Big and sweet." },
            new ItemDef { id = "gift_bananas", english = "Bananas", hanzi = "香蕉", category = ItemCategory.Gift, price = 15, description = "A bunch of bananas." },
            new ItemDef { id = "gift_grapes", english = "Grapes", hanzi = "葡萄", category = ItemCategory.Gift, price = 25, description = "Purple grapes." },
            new ItemDef { id = "gift_sweets", english = "Sweets", hanzi = "糖", category = ItemCategory.Gift, price = 10, description = "A bag of sweets." },
            new ItemDef { id = "gift_bread", english = "Bread", hanzi = "面包", category = ItemCategory.Gift, price = 15, description = "Fresh, soft bread." },
            new ItemDef { id = "gift_milk", english = "Milk", hanzi = "牛奶", category = ItemCategory.Gift, price = 12, description = "A bottle of fresh milk." },
            new ItemDef { id = "gift_hat", english = "Sun Hat", hanzi = "帽子", category = ItemCategory.Gift, price = 60, description = "A straw sun hat." },
            new ItemDef { id = "gift_umbrella", english = "Umbrella", hanzi = "伞", category = ItemCategory.Gift, price = 50, description = "A pretty umbrella." },
            new ItemDef { id = "gift_watch", english = "Wristwatch", hanzi = "手表", category = ItemCategory.Gift, price = 150, minAffinity = 2, description = "A handsome watch." },

            // ---- 小方's colour shop: cosmetics for the boat, Tangyuan, Mei and the house (put on as soon as you buy them)
            new ItemDef { id = "cos_boat_red", english = "Red Boat Paint", hanzi = "红色的船", category = ItemCategory.Cosmetic, price = 150, unique = true, cosmeticFor = "boat", colorHex = "#E0604E", description = "Paint the rowboat a cheerful red." },
            new ItemDef { id = "cos_boat_blue", english = "Blue Boat Paint", hanzi = "蓝色的船", category = ItemCategory.Cosmetic, price = 150, unique = true, cosmeticFor = "boat", colorHex = "#4F8FC0", description = "Paint the rowboat sea blue." },
            new ItemDef { id = "cos_boat_yellow", minHsk = 1, minAffinity = 1, english = "Yellow Boat Paint", hanzi = "黄色的船", category = ItemCategory.Cosmetic, price = 200, unique = true, cosmeticFor = "boat", colorHex = "#F2C94C", description = "Sunny yellow. Easy to spot from the beach." },
            new ItemDef { id = "cos_cat_red", english = "Tangyuan's Red Hat", hanzi = "小红帽", category = ItemCategory.Cosmetic, price = 120, unique = true, cosmeticFor = "cat", colorHex = "#E0604E", description = "A tiny red hat for Tangyuan." },
            new ItemDef { id = "cos_cat_yellow", minHsk = 1, minAffinity = 1, english = "Tangyuan's Yellow Hat", hanzi = "小黄帽", category = ItemCategory.Cosmetic, price = 150, unique = true, cosmeticFor = "cat", colorHex = "#F2C94C", description = "A tiny yellow hat with a pompom." },
            new ItemDef { id = "cos_mei_white", english = "Mei's White Sun Hat", hanzi = "美的白帽子", category = ItemCategory.Cosmetic, price = 180, unique = true, cosmeticFor = "mei", colorHex = "#F4EFE6", description = "A wide white sun hat for Mei." },
            new ItemDef { id = "cos_mei_red", minHsk = 1, minAffinity = 1, english = "Mei's Red Sun Hat", hanzi = "美的红帽子", category = ItemCategory.Cosmetic, price = 220, unique = true, cosmeticFor = "mei", colorHex = "#C9504A", description = "A bright red sun hat for Mei." },
            new ItemDef { id = "cos_house_red", english = "Red Roof", hanzi = "红房子", category = ItemCategory.Cosmetic, price = 300, unique = true, cosmeticFor = "house", colorHex = "#C9504A", description = "Repaint the cabin's roof red." },
            new ItemDef { id = "cos_house_blue", minHsk = 1, minAffinity = 1, english = "Blue Roof", hanzi = "蓝房子", category = ItemCategory.Cosmetic, price = 300, unique = true, cosmeticFor = "house", colorHex = "#4F7FB0", description = "Repaint the cabin's roof blue." },
            new ItemDef { id = "cos_mei_blue", minHsk = 2, minAffinity = 2, english = "Mei's Blue Sun Hat", hanzi = "美的蓝帽子", category = ItemCategory.Cosmetic, price = 350, unique = true, cosmeticFor = "mei", colorHex = "#4F7FB0", description = "A sky-blue sun hat for Mei." },
            new ItemDef { id = "cos_cat_green", minHsk = 2, minAffinity = 2, english = "Tangyuan's Green Hat", hanzi = "小绿帽", category = ItemCategory.Cosmetic, price = 300, unique = true, cosmeticFor = "cat", colorHex = "#6FAF5F", description = "A tiny green hat, like a leaf." },
            new ItemDef { id = "cos_boat_gold", minHsk = 3, minAffinity = 3, english = "Golden Boat Paint", hanzi = "金色的船", category = ItemCategory.Cosmetic, price = 1500, unique = true, cosmeticFor = "boat", colorHex = "#E8B83A", description = "Shining gold. Everyone in the bay will know you passed HSK 3." },
            new ItemDef { id = "cos_cat_gold", minHsk = 3, minAffinity = 3, english = "Tangyuan's Golden Crown", hanzi = "小金帽", category = ItemCategory.Cosmetic, price = 800, unique = true, cosmeticFor = "cat", colorHex = "#E8B83A", description = "A golden hat fit for a king of cats." },
            new ItemDef { id = "cos_house_white", minHsk = 2, minAffinity = 2, english = "White Roof", hanzi = "白房子", category = ItemCategory.Cosmetic, price = 400, unique = true, cosmeticFor = "house", colorHex = "#EDEAE4", description = "A clean white roof, like a seaside cottage." },

            // ---- Coach Wu's training: 20 levels per stat in four tiers of five (each tier needs that HSK test).
            //      Price per level: 10, 20, 40, 70, 100, then ten times as much each tier (PlayerStats.NextPrice).
            new ItemDef { id = "up_cast", english = "Strength Training", hanzi = "力量训练", category = ItemCategory.Upgrade, price = 10, stat = "cast",
                description = "Hooked fish thrash less." },
            new ItemDef { id = "up_bar", english = "Eye Training", hanzi = "眼睛训练", category = ItemCategory.Upgrade, price = 10, stat = "bar",
                description = "A wider green bar when reeling: each set of five levels doubles it for that tier's fish." },
            new ItemDef { id = "up_grip", english = "Stamina Training", hanzi = "跑步训练", category = ItemCategory.Upgrade, price = 10, stat = "grip",
                description = "The catch meter fills faster and fish get away more slowly." },
            new ItemDef { id = "up_luck", english = "Luck Training", hanzi = "运气训练", category = ItemCategory.Upgrade, price = 10, stat = "luck",
                description = "+2.5% chance per level of a bonus fish, and golden fish come a little more often." },
            new ItemDef { id = "up_quality", english = "Technique Training", hanzi = "技术训练", category = ItemCategory.Upgrade, price = 10, stat = "quality",
                description = "Bigger fish: each kind comes in heavier, within its normal weight range." },
            new ItemDef { id = "up_bite", english = "Calm Training", hanzi = "安静训练", category = ItemCategory.Upgrade, price = 10, stat = "bite",
                description = "Sit quietly: fish bite 3% sooner per level." },

            // ---- 海叔's crab pots: three upgrades, 20 levels each, five per tier, priced like Coach Wu's training (CrabPots).
            new ItemDef { id = "crab_pots", english = "More Crab Pots", hanzi = "蟹笼", category = ItemCategory.Upgrade, price = 10, stat = "crab_pots",
                description = "One more pot in the water per level." },
            new ItemDef { id = "crab_size", english = "Bigger Pots", hanzi = "大蟹笼", category = ItemCategory.Upgrade, price = 10, stat = "crab_size",
                description = "Each pot holds one more crab per level." },
            new ItemDef { id = "crab_deep", english = "Longer Lines", hanzi = "长绳子", category = ItemCategory.Upgrade, price = 10, stat = "crab_deep",
                description = "Longer lines reach deeper water, where bigger and rarer crabs live: each crab is worth more." },

        };

        public static readonly List<ShopDef> Shops = new List<ShopDef>
        {
            new ShopDef { id = "tackle", english = "Tackle Shop", hanzi = "渔具店", keeperName = "老王", keeperEnglish = "Old Wang",
                voice = "zh_male", pitch = 0.93f,
                personality = "a gruff but kind old fisherman who loves to brag about big catches and gives honest advice about rods and bait",
                items = new[] { "bait_worm", "bait_dough", "bait_shrimp", "bait_squid", "bait_crab", "bait_fish", "bait_glow",
                    "line_red", "line_blue", "line_black", "line_gold", "boat", "boat_oars", "boat_sail", "boat_new", "bucket_big", "bucket_huge", "bucket_giant", "bobber_fancy" } },
            new ShopDef { id = "fish", english = "Sushi Bar", hanzi = "寿司店", keeperName = "陈阿姨", keeperEnglish = "Auntie Chen",
                voice = "zh_female", pitch = 0.94f, buysFish = true,
                personality = "a warm, chatty sushi chef who buys fish from local anglers for her little sushi bar, weighs every catch on her old scale and fusses over customers",
                items = new string[0] },
            new ShopDef { id = "furniture", english = "Furniture Shop", hanzi = "家具店", keeperName = "李师傅", keeperEnglish = "Master Li",
                voice = "zh_male", pitch = 1.05f,
                personality = "a calm, precise carpenter who is proud of his work and speaks slowly and politely",
                items = new[] { "bed_single", "bed_double", "bed_big", "chair", "table", "sofa", "floor_lamp", "plant", "rug", "bookcase", "bench", "street_lamp", "radio", "teddy" } },
            new ShopDef { id = "pet", english = "Pet Shop", hanzi = "宠物店", keeperName = "小林", keeperEnglish = "Xiao Lin",
                voice = "zh_female", pitch = 1.08f,
                personality = "a cheerful young woman who adores cats and always asks about Tangyuan",
                items = new[] { "cat_food", "cat_treat", "cat_bed", "cat_box", "cat_bowl", "yarn", "scratcher", "collar" } },
            new ShopDef { id = "books", english = "Bookshop", hanzi = "书店", keeperName = "周老师", keeperEnglish = "Teacher Zhou",
                voice = "zh_female", pitch = 0.98f,
                personality = "a gentle retired schoolteacher who has read every fishing book ever written and loves recommending the right one",
                items = new[] { "book_basics", "book_shore", "book_bay" } },
            new ShopDef { id = "gym", english = "Fitness Trainer", hanzi = "健身房", keeperName = "武教练", keeperEnglish = "Coach Wu",
                voice = "zh_male", pitch = 0.98f,
                personality = "an energetic fitness coach who is always cheerful, loves push-ups and believes anyone can get strong",
                items = new[] { "up_cast", "up_bar", "up_grip", "up_luck", "up_quality", "up_bite" } },
            new ShopDef { id = "colours", english = "Colour Shop", hanzi = "颜色店", keeperName = "小方", keeperEnglish = "Xiao Fang",
                voice = "zh_male", pitch = 1.12f,
                personality = "a cheerful young artist who loves colours and clothes",
                items = new[] { "cos_boat_red", "cos_boat_blue", "cos_boat_yellow", "cos_boat_gold", "cos_cat_red", "cos_cat_yellow", "cos_cat_green", "cos_cat_gold",
                    "cos_mei_white", "cos_mei_red", "cos_mei_blue", "cos_house_red", "cos_house_blue", "cos_house_white" } },
            new ShopDef { id = "gifts", english = "Gift Shop", hanzi = "礼品店", keeperName = "刘奶奶", keeperEnglish = "Granny Liu",
                voice = "zh_female", pitch = 0.9f,
                personality = "a sweet, talkative grandmother who knows what everybody likes",
                items = new[] { "gift_flowers", "gift_tea", "gift_coffee", "gift_cake", "gift_watermelon", "gift_bananas", "gift_grapes",
                    "gift_sweets", "gift_bread", "gift_milk", "gift_hat", "gift_umbrella", "gift_watch" } },
            // Stall 8: the test centre (lessons, practice and HSK tests; nothing for sale).
            new ShopDef { id = "school", english = "Test Centre", hanzi = "考试中心", keeperName = "高老师", keeperEnglish = "Teacher Gao",
                voice = "zh_female", pitch = 1.02f, school = true,
                personality = "a strict but warm Chinese teacher who runs the HSK test centre, loves good pronunciation and believes everyone can learn",
                items = new string[0] },
            // Stall 9: down the beach, 海叔's crab pots (passive income; collect it at his stall).
            new ShopDef { id = "crabber", english = "Crab Pots", hanzi = "螃蟹摊", keeperName = "海叔", keeperEnglish = "Uncle Hai",
                voice = "zh_male", pitch = 0.88f, crabber = true,
                personality = "a sunburnt, easy-going crab fisherman who keeps pots off the beach, looks after the player's pots too and pays them for the catch",
                items = new[] { "crab_pots", "crab_size", "crab_deep" } },
            // Shop 10: the bus driver at the bus stop (no stall; he turns up once the first HSK test is passed).
            new ShopDef { id = "bus", english = "Bus Stop", hanzi = "汽车站", keeperName = "张师傅", keeperEnglish = "Driver Zhang",
                voice = "zh_male", pitch = 1.0f, busDriver = true,
                personality = "a cheerful bus driver who drives the little bus along the coast and takes his fare in fish",
                items = new string[0] },
        };

        static Catalog()
        {
            // Willow Bay's keepers are the first ten shops (their ids are their roles); the bus driver travels with you.
            for (int i = 0; i < Shops.Count; i++)
            {
                var s = Shops[i];
                s.role = s.busDriver ? "bus" : s.id;
                s.region = s.busDriver ? -1 : 0;
                s.stall = s.busDriver ? Environment.WorldShape.BusStall : i;
                s.female = s.voice == "zh_female";
            }
            // The other stops' keepers and their goods (Companion.RegionKeepers).
            Items.AddRange(Companion.RegionKeepers.Items);
            foreach (var s in Companion.RegionKeepers.Shops)
            {
                if (s.items == null) s.items = Shops.First(w => w.role == s.role && w.region == 0).items;
                Shops.Add(s);
            }
        }

        /// <summary>The keeper of this kind of stall in a region (the bus driver for "bus").</summary>
        public static ShopDef ShopFor(string role, int region) =>
            Shops.FirstOrDefault(s => s.role == role && (s.region == region || s.region < 0));

        /// <summary>The ten stall keepers of a region, in stall order.</summary>
        public static IEnumerable<ShopDef> StallShops(int region) => Shops.Where(s => s.region == region).OrderBy(s => s.stall);

        /// <summary>The keepers you can meet in a region: its ten, and the bus driver.</summary>
        public static IEnumerable<ShopDef> PeopleIn(int region) => Shops.Where(s => s.region == region || s.region < 0);

        /// <summary>The gift item with this Chinese name (what keepers like and dislike), if it's sold.</summary>
        public static ItemDef GiftByHanzi(string hanzi) => Items.FirstOrDefault(i => i.category == ItemCategory.Gift && i.hanzi == hanzi);

        public static ItemDef Get(string id) => Items.FirstOrDefault(i => i.id == id);

        /// <summary>The cheapest line that holds a fish needing this strength (null = the starter white line is enough).</summary>
        public static ItemDef LineFor(float kg) => kg <= StarterLineKg ? null
            : Items.Where(i => i.category == ItemCategory.Line && i.lineKg + 0.01f >= kg).OrderBy(i => i.lineKg).FirstOrDefault();

        public const float StarterLineKg = 3f;

        /// <summary>What an item costs right now (stat upgrades: the next level's price).</summary>
        public static int PriceOf(ItemDef item) =>
            item.category == ItemCategory.Upgrade ? UntitledGame.Progression.PlayerStats.NextPrice(item.stat) : item.price;
        public static ShopDef Shop(string id) => Shops.FirstOrDefault(s => s.id == id);

        /// <summary>
        /// What the sushi chef pays for a catch: ten times as much for each fish tier (the HSK level of the book about
        /// it), ¥10 to ¥40 within a tier by rarity, and more for a heavier one of its kind (x0.7 smallest, x1.8 biggest).
        /// </summary>
        public static int FishPrice(FishSpecies s, float weightKg)
        {
            if (!s.IsFish) return 3;
            float t = Mathf.InverseLerp(s.minWeight, s.maxWeight, weightKg);
            return Mathf.Max(1, Mathf.RoundToInt(FishPower.BasePrice(s) * Mathf.Lerp(0.7f, 1.8f, t)));
        }

        /// <summary>(Replaced by the sushi chef's scale: lessons and tests raise its multiplier per bar fill, see FishSale.)</summary>
        public static float FishHskBonus => 0f;

        /// <summary>The measure word for counting an item: 一根鱼竿, 两包蚯蚓, 一个猫碗, 一把椅子...</summary>
        public static string MeasureWord(ItemDef item)
        {
            if (item.packSize > 1) return "包";
            if (item.category == ItemCategory.Book) return "本";
            if (item.category == ItemCategory.Service || item.category == ItemCategory.Upgrade) return "次";
            if (item.category == ItemCategory.Line) return "卷";
            if (item.category == ItemCategory.Bed) return "张";
            if (item.category == ItemCategory.Boat) return "条";
            if (item.category == ItemCategory.Card) return "张";
            switch (item.id)
            {
                case "gift_flowers": return "束";
                case "gift_tea": case "gift_coffee": case "gift_sweets": return "包";
                case "gift_bananas": case "gift_grapes": return "串";
                case "gift_milk": return "瓶";
                case "gift_hat": return "顶";
                case "gift_umbrella": return "把";
                case "gift_watch": return "块";
            }
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
