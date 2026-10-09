using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Economy;

namespace UntitledGame.Fishing
{
    public enum Rarity { Junk, Common, Uncommon, Rare, Legendary }

    [Flags]
    public enum TimeWindow
    {
        None = 0,
        Morning = 1,
        Day = 2,
        Evening = 4,
        Night = 8,
        Any = Morning | Day | Evening | Night,
    }

    public enum CatchModel { SmallFish, LargeFish, Bottle, Teacup, Driftwood }

    /// <summary>How the fish moves in the reeling minigame (as in Stardew Valley).</summary>
    public enum FishMotion { Mixed, Smooth, Sinker, Floater, Dart }

    /// <summary>
    /// One kind of sea creature (80 of them: 20 for each region's sea, from Willow Bay's sardines to Mars's star koi). A fish can only bite once every requirement is met: the player has read about it
    /// (a book from 周老师), the bobber is far enough from the shore, the bait on the hook is one it likes, the line is
    /// strong enough for it, and it's the right time of day. Weights are random within a real-life range.
    /// </summary>
    public class FishSpecies
    {
        public string id;
        public string name;
        public string hanzi;
        public string pinyin;
        public Rarity rarity;
        public float minWeight;          // kg
        public float maxWeight;          // kg
        public TimeWindow times = TimeWindow.Any;
        public bool likesRain;
        public int difficulty;           // 0-100: how wild it is in the minigame
        public FishMotion motion = FishMotion.Mixed;
        public float barSize = 0.28f;    // the green bar's height for this fish (fraction of the track), before upgrades
        public float minDistance;        // metres from the shoreline the bobber must be
        public string[] baits = new string[0]; // bait item ids it likes (much more likely to bite); "none" = a plain hook
        public bool needsBait;           // only bites on one of its baits (the oarfish and its glow lure)
        public float lineKg;             // line strength (kg) needed
        public Color body = Color.gray;
        public Color fins = Color.white;
        public Color glow = Color.clear;
        public CatchModel model = CatchModel.SmallFish;
        /// <summary>Body proportions applied to the model: (length, height, width), 1 = the plain model.</summary>
        public Vector3 shape = Vector3.one;
        public string blurb;

        public bool IsFish => rarity != Rarity.Junk;
        public float MinShoreDistance => minDistance;
        public bool TakesPlainHook => baits.Contains("none");
    }

    public struct CatchContext
    {
        public float hour;
        public bool raining;
        public ItemDef bait;           // null = plain hook
        public float lineKg;           // strength of the line on the rod
        public float shoreDistance;    // how far out the bobber is, in metres from the shoreline
    }

    public static class FishDatabase
    {
        private static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public const string PlainHook = "none";

        public static readonly List<FishSpecies> All = new List<FishSpecies>
        {
            // ---- tier 0: Willow Bay (forest beach)
            new FishSpecies { id = "sardine", name = "Sardine", hanzi = "沙丁鱼", pinyin = "shā dīng yú", rarity = Rarity.Common, minWeight = 0.03f, maxWeight = 0.15f,
                difficulty = 15, motion = FishMotion.Smooth, barSize = 0.3f, minDistance = 0f, baits = new[] { PlainHook, "bait_worm", "bait_dough" }, lineKg = 1f,
                body = C("#9FB4C4"), fins = C("#6F8798"), blurb = "Small, silvery and everywhere. Everybody's first catch." },
            new FishSpecies { id = "goby", name = "Goby", hanzi = "虾虎鱼", pinyin = "xiā hǔ yú", rarity = Rarity.Common, minWeight = 0.02f, maxWeight = 0.1f,
                difficulty = 10, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 0f, baits = new[] { PlainHook, "bait_worm", "bait_shrimp" },
                lineKg = 1f, body = C("#B59A6A"), fins = C("#7E6A47"), shape = new Vector3(0.85f, 1.15f, 1.1f),
                blurb = "A stubby little fish that sits on the sand under the dock." },
            new FishSpecies { id = "horsemackerel", name = "Horse Mackerel", hanzi = "竹荚鱼", pinyin = "zhú jiá yú", rarity = Rarity.Common, minWeight = 0.1f,
                maxWeight = 0.5f, difficulty = 25, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 2f,
                baits = new[] { "bait_worm", "bait_dough", "bait_shrimp" }, lineKg = 1f, body = C("#8FA9A2"), fins = C("#D8C25A"),
                shape = new Vector3(1.15f, 0.85f, 0.85f), blurb = "Slim and quick, with a yellow streak. Comes in shoals in the morning." },
            new FishSpecies { id = "mullet", name = "Mullet", hanzi = "鲻鱼", pinyin = "zī yú", rarity = Rarity.Common, minWeight = 0.3f, maxWeight = 2f,
                difficulty = 30, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 3f, baits = new[] { PlainHook, "bait_dough" }, lineKg = 2f,
                body = C("#7D8B94"), fins = C("#56626A"), shape = new Vector3(1.25f, 0.9f, 0.95f),
                blurb = "A long grey fish that jumps out of the water for no reason at all." },
            new FishSpecies { id = "croaker", name = "Yellow Croaker", hanzi = "小黄鱼", pinyin = "xiǎo huáng yú", rarity = Rarity.Common, minWeight = 0.1f,
                maxWeight = 0.6f, times = TimeWindow.Morning | TimeWindow.Day | TimeWindow.Evening, difficulty = 25, motion = FishMotion.Mixed, barSize = 0.26f,
                minDistance = 2f, baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 1f, body = C("#E2C25A"), fins = C("#C99A35"),
                blurb = "Golden and chatty: it really does croak." },
            new FishSpecies { id = "flounder", name = "Flounder", hanzi = "比目鱼", pinyin = "bǐ mù yú", rarity = Rarity.Common, minWeight = 0.3f, maxWeight = 2.5f,
                likesRain = true, difficulty = 30, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 3f, baits = new[] { "bait_worm", "bait_shrimp" },
                lineKg = 2f, body = C("#A68B66"), fins = C("#7E6648"), shape = new Vector3(1f, 0.35f, 1.6f),
                blurb = "Flat as a plate, with both eyes on one side." },
            new FishSpecies { id = "mackerel", name = "Mackerel", hanzi = "鲭鱼", pinyin = "qīng yú", rarity = Rarity.Common, minWeight = 0.3f, maxWeight = 1.2f,
                times = TimeWindow.Morning | TimeWindow.Day, difficulty = 40, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 6f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 2f, body = C("#4E7FA0"), fins = C("#2F4E63"),
                blurb = "Striped, fast, and travels in big shiny schools." },
            new FishSpecies { id = "herring", name = "Herring", hanzi = "鲱鱼", pinyin = "fēi yú", rarity = Rarity.Common, minWeight = 0.1f, maxWeight = 0.4f,
                times = TimeWindow.Morning | TimeWindow.Day, difficulty = 20, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 1f,
                baits = new[] { "bait_worm", "bait_dough" }, lineKg = 1f, body = C("#A9BCC8"), fins = C("#6C8494"), shape = new Vector3(1.1f, 0.9f, 0.85f),
                blurb = "Silver and oily, and it travels in enormous shoals." },
            new FishSpecies { id = "pufferfish", name = "Pufferfish", hanzi = "河豚", pinyin = "hé tún", rarity = Rarity.Uncommon, minWeight = 0.2f, maxWeight = 1.5f,
                times = TimeWindow.Day | TimeWindow.Evening, difficulty = 55, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 5f,
                baits = new[] { "bait_shrimp", "bait_crab" }, lineKg = 2f, body = C("#D9C27A"), fins = C("#A88F4C"), shape = new Vector3(0.8f, 1.35f, 1.35f),
                blurb = "Puffs up into a spiky ball when it's cross." },
            new FishSpecies { id = "seabream", name = "Sea Bream", hanzi = "鲷鱼", pinyin = "diāo yú", rarity = Rarity.Uncommon, minWeight = 0.5f, maxWeight = 4f,
                times = TimeWindow.Morning | TimeWindow.Day, difficulty = 50, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 10f,
                baits = new[] { "bait_shrimp", "bait_crab", "bait_squid" }, lineKg = 5f, body = C("#E28A86"), fins = C("#B9585A"), model = CatchModel.LargeFish,
                blurb = "Pink and lucky: the fish for celebrations." },
            new FishSpecies { id = "seabass", name = "Sea Bass", hanzi = "鲈鱼", pinyin = "lú yú", rarity = Rarity.Uncommon, minWeight = 0.5f, maxWeight = 6f,
                times = TimeWindow.Morning | TimeWindow.Evening | TimeWindow.Night, likesRain = true, difficulty = 55, motion = FishMotion.Mixed, barSize = 0.26f,
                minDistance = 8f, baits = new[] { "bait_shrimp", "bait_fish" }, lineKg = 6f, body = C("#8E9EA6"), fins = C("#5D6B72"), model = CatchModel.LargeFish,
                blurb = "Hunts along the shore when the light is low." },
            new FishSpecies { id = "hairtail", name = "Hairtail", hanzi = "带鱼", pinyin = "dài yú", rarity = Rarity.Uncommon, minWeight = 0.3f, maxWeight = 2f,
                times = TimeWindow.Evening | TimeWindow.Night, difficulty = 60, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 14f,
                baits = new[] { "bait_squid", "bait_fish" }, lineKg = 3f, body = C("#D7DDE3"), fins = C("#A9B3BC"), shape = new Vector3(2.2f, 0.7f, 0.45f),
                blurb = "Long, thin and shiny like a silver ribbon." },
            new FishSpecies { id = "cuttlefish", name = "Cuttlefish", hanzi = "墨鱼", pinyin = "mò yú", rarity = Rarity.Uncommon, minWeight = 0.3f, maxWeight = 2f,
                times = TimeWindow.Evening | TimeWindow.Night, difficulty = 50, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 8f,
                baits = new[] { "bait_shrimp" }, lineKg = 3f, body = C("#C9A98C"), fins = C("#8C6A55"), shape = new Vector3(0.9f, 0.8f, 1.2f),
                blurb = "Squirts ink when it's startled. Mind your shirt." },
            new FishSpecies { id = "redgurnard", name = "Red Gurnard", hanzi = "红娘鱼", pinyin = "hóng niáng yú", rarity = Rarity.Uncommon, minWeight = 0.3f,
                maxWeight = 1.5f, times = TimeWindow.Day, difficulty = 45, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 9f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 3f, body = C("#D8604E"), fins = C("#A8463A"),
                blurb = "Walks along the seabed on little finger-like fins." },
            new FishSpecies { id = "blackporgy", name = "Black Porgy", hanzi = "黑鲷", pinyin = "hēi diāo", rarity = Rarity.Rare, minWeight = 1f, maxWeight = 4f,
                times = TimeWindow.Evening | TimeWindow.Night, likesRain = true, difficulty = 66, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 12f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 6f, body = C("#4A4E55"), fins = C("#2E3136"), model = CatchModel.LargeFish,
                blurb = "A dark, clever bream that steals bait from careless anglers." },
            new FishSpecies { id = "turbot", name = "Turbot", hanzi = "多宝鱼", pinyin = "duō bǎo yú", rarity = Rarity.Rare, minWeight = 1f, maxWeight = 8f,
                times = TimeWindow.Day | TimeWindow.Evening, difficulty = 62, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 16f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 6f, body = C("#9A8468"), fins = C("#6E5C46"), model = CatchModel.LargeFish,
                shape = new Vector3(1f, 0.35f, 1.5f), blurb = "A round flatfish that hides under the sand. Chefs love it." },
            new FishSpecies { id = "johndory", name = "John Dory", hanzi = "海鲂", pinyin = "hǎi fáng", rarity = Rarity.Rare, minWeight = 0.5f, maxWeight = 3f,
                times = TimeWindow.Day, difficulty = 64, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 15f, baits = new[] { "bait_shrimp" },
                lineKg = 5f, body = C("#C9B27A"), fins = C("#8E7A4E"), shape = new Vector3(0.8f, 1.4f, 0.5f),
                blurb = "Thin and golden, with a dark thumbprint on each side." },
            new FishSpecies { id = "yellowtail", name = "Yellowtail", hanzi = "鰤鱼", pinyin = "shī yú", rarity = Rarity.Rare, minWeight = 2f, maxWeight = 10f,
                times = TimeWindow.Morning, difficulty = 68, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 20f,
                baits = new[] { "bait_shrimp", PlainHook }, lineKg = 6f, body = C("#7E98A6"), fins = C("#E3C24A"), model = CatchModel.LargeFish,
                blurb = "Fast and strong: a fish that really pulls back." },
            new FishSpecies { id = "spottedbass", name = "Seven-Star Bass", hanzi = "七星鲈", pinyin = "qī xīng lú", rarity = Rarity.Legendary, minWeight = 3f,
                maxWeight = 15f, times = TimeWindow.Evening, difficulty = 75, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 20f,
                baits = new[] { "bait_shrimp" }, lineKg = 6f, body = C("#8FA0A6"), fins = C("#3E4A50"), model = CatchModel.LargeFish,
                blurb = "A great bass with seven dark stars along its side." },
            new FishSpecies { id = "willowspirit", name = "Willow Spirit Fish", hanzi = "柳仙鱼", pinyin = "liǔ xiān yú", rarity = Rarity.Legendary, minWeight = 3f,
                maxWeight = 12f, times = TimeWindow.Night, difficulty = 78, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 22f,
                baits = new[] { "bait_dough", "bait_worm" }, lineKg = 6f, body = C("#B9D99A"), fins = C("#7FAF64"), glow = C("#9CD47A"),
                model = CatchModel.LargeFish, blurb = "A pale green fish said to be the spirit of the old willow tree." },

            // ---- tier 1: Golden Sand Bay (desert coast, warm sea)
            new FishSpecies { id = "clownfish", name = "Clownfish", hanzi = "小丑鱼", pinyin = "xiǎo chǒu yú", rarity = Rarity.Common, minWeight = 0.05f,
                maxWeight = 0.25f, difficulty = 20, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 0f,
                baits = new[] { PlainHook, "bait_worm", "bait_shrimp" }, lineKg = 1f, body = C("#F08A3C"), fins = C("#F7F2EA"),
                blurb = "Orange and white, and lives in a stinging anemone as if it's nothing." },
            new FishSpecies { id = "butterflyfish", name = "Butterflyfish", hanzi = "蝴蝶鱼", pinyin = "hú dié yú", rarity = Rarity.Common, minWeight = 0.1f,
                maxWeight = 0.4f, times = TimeWindow.Day, difficulty = 28, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 2f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 1f, body = C("#F2D24A"), fins = C("#3A3A40"), shape = new Vector3(0.8f, 1.3f, 0.5f),
                blurb = "Flat, yellow and flutters between the rocks." },
            new FishSpecies { id = "sandsmelt", name = "Sand Smelt", hanzi = "沙钻鱼", pinyin = "shā zuàn yú", rarity = Rarity.Common, minWeight = 0.05f,
                maxWeight = 0.3f, times = TimeWindow.Morning | TimeWindow.Day, difficulty = 25, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 1f,
                baits = new[] { "bait_worm" }, lineKg = 1f, body = C("#E3D3AC"), fins = C("#B9A57A"), shape = new Vector3(1.3f, 0.75f, 0.75f),
                blurb = "Dives into the sand when anything comes near." },
            new FishSpecies { id = "bluetang", name = "Blue Tang", hanzi = "蓝倒吊", pinyin = "lán dào diào", rarity = Rarity.Common, minWeight = 0.2f,
                maxWeight = 0.6f, times = TimeWindow.Day, difficulty = 32, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 4f,
                baits = new[] { "bait_shrimp" }, lineKg = 2f, body = C("#3E6FD0"), fins = C("#F2C94C"), shape = new Vector3(0.9f, 1.2f, 0.6f),
                blurb = "Bright blue with a yellow tail, and a bit forgetful." },
            new FishSpecies { id = "rabbitfish", name = "Rabbitfish", hanzi = "篮子鱼", pinyin = "lán zi yú", rarity = Rarity.Common, minWeight = 0.3f,
                maxWeight = 1.2f, times = TimeWindow.Day | TimeWindow.Evening, difficulty = 34, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 3f,
                baits = new[] { "bait_dough", "bait_worm" }, lineKg = 2f, body = C("#B7A77C"), fins = C("#8A7A52"),
                blurb = "Nibbles seaweed all day like a rabbit nibbles grass." },
            new FishSpecies { id = "goatfish", name = "Yellow Goatfish", hanzi = "黄羊鱼", pinyin = "huáng yáng yú", rarity = Rarity.Common, minWeight = 0.2f,
                maxWeight = 0.9f, times = TimeWindow.Day, difficulty = 30, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 3f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 2f, body = C("#E8C35A"), fins = C("#C9963A"),
                blurb = "Feels for food in the sand with two whiskers under its chin." },
            new FishSpecies { id = "sweetlips", name = "Sweetlips", hanzi = "胡椒鲷", pinyin = "hú jiāo diāo", rarity = Rarity.Common, minWeight = 0.5f,
                maxWeight = 3f, times = TimeWindow.Evening, difficulty = 38, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 6f,
                baits = new[] { "bait_shrimp", "bait_crab" }, lineKg = 6f, body = C("#D9D2B8"), fins = C("#3A3A40"), model = CatchModel.LargeFish,
                blurb = "Spotty, with big soft lips. It looks like it's about to kiss you." },
            new FishSpecies { id = "flyingfish", name = "Flying Fish", hanzi = "飞鱼", pinyin = "fēi yú", rarity = Rarity.Common, minWeight = 0.2f, maxWeight = 0.6f,
                times = TimeWindow.Morning | TimeWindow.Day, difficulty = 36, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 8f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 2f, body = C("#6C8FC4"), fins = C("#B9D2EC"), shape = new Vector3(1.2f, 0.8f, 1.3f),
                blurb = "Glides over the waves on wing-like fins." },
            new FishSpecies { id = "angelfish", name = "Angelfish", hanzi = "神仙鱼", pinyin = "shén xiān yú", rarity = Rarity.Uncommon, minWeight = 0.3f,
                maxWeight = 1.5f, times = TimeWindow.Day, difficulty = 48, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 6f,
                baits = new[] { "bait_shrimp" }, lineKg = 3f, body = C("#3F62B8"), fins = C("#F2D24A"), shape = new Vector3(0.8f, 1.4f, 0.5f),
                blurb = "Elegant stripes and long fins. It knows it's beautiful." },
            new FishSpecies { id = "parrotfish", name = "Parrotfish", hanzi = "鹦嘴鱼", pinyin = "yīng zuǐ yú", rarity = Rarity.Uncommon, minWeight = 1f,
                maxWeight = 5f, times = TimeWindow.Day, difficulty = 55, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 10f,
                baits = new[] { "bait_crab", "bait_shrimp" }, lineKg = 6f, body = C("#4FB39A"), fins = C("#E06A8C"), model = CatchModel.LargeFish,
                blurb = "Crunches coral with its beak and makes white sand." },
            new FishSpecies { id = "lionfish", name = "Lionfish", hanzi = "狮子鱼", pinyin = "shī zi yú", rarity = Rarity.Uncommon, minWeight = 0.3f, maxWeight = 1.2f,
                times = TimeWindow.Evening | TimeWindow.Night, difficulty = 58, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 8f,
                baits = new[] { "bait_shrimp", "bait_squid" }, lineKg = 3f, body = C("#C9503E"), fins = C("#F2E6D2"),
                blurb = "Covered in stripy spines like a lion's mane. Don't touch!" },
            new FishSpecies { id = "octopus", name = "Octopus", hanzi = "章鱼", pinyin = "zhāng yú", rarity = Rarity.Uncommon, minWeight = 1f, maxWeight = 8f,
                times = TimeWindow.Evening | TimeWindow.Night, difficulty = 65, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 6f,
                baits = new[] { "bait_crab" }, lineKg = 6f, body = C("#B5655A"), fins = C("#8B4740"), model = CatchModel.LargeFish,
                shape = new Vector3(0.7f, 1.3f, 1.3f), blurb = "Clever, shy and very good at hiding." },
            new FishSpecies { id = "triggerfish", name = "Triggerfish", hanzi = "炮弹鱼", pinyin = "pào dàn yú", rarity = Rarity.Uncommon, minWeight = 1f,
                maxWeight = 4f, times = TimeWindow.Day, difficulty = 60, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 12f,
                baits = new[] { "bait_crab", "bait_squid" }, lineKg = 6f, body = C("#5E6E8A"), fins = C("#E3B23C"), model = CatchModel.LargeFish,
                blurb = "Shaped like a cannonball and just as tough." },
            new FishSpecies { id = "stingray", name = "Stingray", hanzi = "魟鱼", pinyin = "hóng yú", rarity = Rarity.Uncommon, minWeight = 3f, maxWeight = 20f,
                times = TimeWindow.Evening | TimeWindow.Night, difficulty = 62, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 14f,
                baits = new[] { "bait_crab", "bait_squid" }, lineKg = 15f, body = C("#8A7A62"), fins = C("#5E5240"), model = CatchModel.LargeFish,
                shape = new Vector3(0.8f, 0.3f, 1.8f), blurb = "Glides over the sand like a big flat kite." },
            new FishSpecies { id = "conger", name = "Conger Eel", hanzi = "海鳗", pinyin = "hǎi mán", rarity = Rarity.Rare, minWeight = 1f, maxWeight = 12f,
                times = TimeWindow.Night, likesRain = true, difficulty = 70, motion = FishMotion.Smooth, barSize = 0.3f, minDistance = 12f,
                baits = new[] { "bait_squid", "bait_fish" }, lineKg = 15f, body = C("#5B5E52"), fins = C("#3E4038"), model = CatchModel.LargeFish,
                shape = new Vector3(2f, 0.6f, 0.6f), blurb = "Lives in rocky holes and comes out on rainy nights." },
            new FishSpecies { id = "grouper", name = "Grouper", hanzi = "石斑鱼", pinyin = "shí bān yú", rarity = Rarity.Rare, minWeight = 2f, maxWeight = 25f,
                times = TimeWindow.Day | TimeWindow.Evening, difficulty = 72, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 18f,
                baits = new[] { "bait_crab", "bait_fish" }, lineKg = 15f, body = C("#8B6A4A"), fins = C("#5E4632"), model = CatchModel.LargeFish,
                shape = new Vector3(0.95f, 1.15f, 1.15f), blurb = "A heavy, spotty rock-dweller with a huge mouth." },
            new FishSpecies { id = "skipjack", name = "Skipjack Tuna", hanzi = "鲣鱼", pinyin = "jiān yú", rarity = Rarity.Rare, minWeight = 2f, maxWeight = 8f,
                times = TimeWindow.Morning | TimeWindow.Day, difficulty = 75, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 30f,
                baits = new[] { "bait_fish", "bait_squid" }, lineKg = 15f, body = C("#3F5E86"), fins = C("#9FB3C9"), model = CatchModel.LargeFish,
                blurb = "A little tuna that never stops swimming." },
            new FishSpecies { id = "sailfish", name = "Sailfish", hanzi = "旗鱼", pinyin = "qí yú", rarity = Rarity.Rare, minWeight = 20f, maxWeight = 60f,
                times = TimeWindow.Day, difficulty = 85, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 34f, baits = new[] { "bait_fish", "bait_squid" },
                lineKg = 15f, body = C("#2E5C9A"), fins = C("#6FA0D8"), model = CatchModel.LargeFish, shape = new Vector3(1.4f, 1.2f, 0.7f),
                blurb = "The fastest fish in the sea, with a sail on its back." },
            new FishSpecies { id = "shark", name = "Blue Shark", hanzi = "鲨鱼", pinyin = "shā yú", rarity = Rarity.Legendary, minWeight = 30f, maxWeight = 120f,
                times = TimeWindow.Evening | TimeWindow.Night, difficulty = 88, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 36f,
                baits = new[] { "bait_squid", "bait_fish" }, lineKg = 15f, body = C("#4F78A8"), fins = C("#C9D6E3"), model = CatchModel.LargeFish,
                shape = new Vector3(1.4f, 0.9f, 0.9f), blurb = "Long, blue and elegant. Mei pretends she isn't scared." },
            new FishSpecies { id = "goldendragon", name = "Golden Sand Dragon", hanzi = "沙漠金龙", pinyin = "shā mò jīn lóng", rarity = Rarity.Legendary,
                minWeight = 10f, maxWeight = 40f, times = TimeWindow.Day, difficulty = 92, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 36f,
                baits = new[] { "bait_squid", "bait_crab" }, lineKg = 15f, body = C("#E8B83A"), fins = C("#C98A2A"), glow = C("#E8B83A"),
                model = CatchModel.LargeFish, shape = new Vector3(1.5f, 0.9f, 0.8f),
                blurb = "A gleaming golden fish that rises only when the desert sun is highest." },

            // ---- tier 2: Snow Bay (snowy coast, icy sea)
            new FishSpecies { id = "capelin", name = "Capelin", hanzi = "毛鳞鱼", pinyin = "máo lín yú", rarity = Rarity.Common, minWeight = 0.02f, maxWeight = 0.08f,
                times = TimeWindow.Morning | TimeWindow.Day, difficulty = 20, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 0f,
                baits = new[] { PlainHook, "bait_worm" }, lineKg = 1f, body = C("#A8BAC6"), fins = C("#6E8494"),
                blurb = "Tiny silver fish that arrive in their millions when the ice melts." },
            new FishSpecies { id = "arcticcod", name = "Arctic Cod", hanzi = "北极鳕", pinyin = "běi jí xuě", rarity = Rarity.Common, minWeight = 0.1f,
                maxWeight = 0.6f, difficulty = 28, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 2f, baits = new[] { "bait_worm", "bait_shrimp" },
                lineKg = 2f, body = C("#B7B09A"), fins = C("#857E6A"), blurb = "A small cod that hides in cracks under the sea ice." },
            new FishSpecies { id = "icefish", name = "Icefish", hanzi = "冰鱼", pinyin = "bīng yú", rarity = Rarity.Common, minWeight = 0.05f, maxWeight = 0.3f,
                times = TimeWindow.Morning | TimeWindow.Night, difficulty = 30, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 1f,
                baits = new[] { "bait_worm" }, lineKg = 1f, body = C("#DDEAF2"), fins = C("#B6CEDD"), shape = new Vector3(1.2f, 0.8f, 0.7f),
                blurb = "Almost see-through: it has no red blood at all." },
            new FishSpecies { id = "pollock", name = "Pollock", hanzi = "狭鳕", pinyin = "xiá xuě", rarity = Rarity.Common, minWeight = 0.5f, maxWeight = 3f,
                times = TimeWindow.Day | TimeWindow.Evening, difficulty = 32, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 4f,
                baits = new[] { "bait_shrimp", "bait_squid" }, lineKg = 6f, body = C("#7F8E8A"), fins = C("#556460"),
                blurb = "Swims in big grey crowds. Everyone's favourite fish fingers." },
            new FishSpecies { id = "saury", name = "Pacific Saury", hanzi = "秋刀鱼", pinyin = "qiū dāo yú", rarity = Rarity.Common, minWeight = 0.1f,
                maxWeight = 0.2f, times = TimeWindow.Evening | TimeWindow.Night, difficulty = 34, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 3f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 1f, body = C("#6F8FAE"), fins = C("#C9D3DC"), shape = new Vector3(1.6f, 0.7f, 0.6f),
                blurb = "A slim blade of a fish; grilled, it tastes of autumn." },
            new FishSpecies { id = "haddock", name = "Haddock", hanzi = "黑线鳕", pinyin = "hēi xiàn xuě", rarity = Rarity.Common, minWeight = 0.5f, maxWeight = 3f,
                times = TimeWindow.Day, difficulty = 36, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 5f, baits = new[] { "bait_worm", "bait_squid" },
                lineKg = 6f, body = C("#9A9C96"), fins = C("#3E4146"), blurb = "A cod with a black line down its side and a thumbprint on its shoulder." },
            new FishSpecies { id = "char", name = "Arctic Char", hanzi = "红点鲑", pinyin = "hóng diǎn guī", rarity = Rarity.Common, minWeight = 0.5f, maxWeight = 4f,
                times = TimeWindow.Morning | TimeWindow.Evening, difficulty = 38, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 6f,
                baits = new[] { "bait_worm", "bait_shrimp", "bait_fish" }, lineKg = 6f, body = C("#5E7A6A"), fins = C("#D8604E"),
                blurb = "Red-bellied and spotty, from the coldest water of all." },
            new FishSpecies { id = "cod", name = "Atlantic Cod", hanzi = "鳕鱼", pinyin = "xuě yú", rarity = Rarity.Common, minWeight = 1f, maxWeight = 10f,
                difficulty = 40, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 8f, baits = new[] { "bait_squid", "bait_fish" }, lineKg = 15f,
                body = C("#A79C7E"), fins = C("#7A7058"), model = CatchModel.LargeFish, blurb = "A big, calm fish with a little beard under its chin." },
            new FishSpecies { id = "grayling", name = "Grayling", hanzi = "茴鱼", pinyin = "huí yú", rarity = Rarity.Uncommon, minWeight = 0.3f, maxWeight = 2f,
                times = TimeWindow.Morning, difficulty = 52, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 7f,
                baits = new[] { "bait_worm", "bait_fish" }, lineKg = 3f, body = C("#8C96A8"), fins = C("#7A5FA8"),
                blurb = "Carries a tall purple fin like a flag." },
            new FishSpecies { id = "lumpfish", name = "Lumpfish", hanzi = "圆鳍鱼", pinyin = "yuán qí yú", rarity = Rarity.Uncommon, minWeight = 0.5f, maxWeight = 5f,
                times = TimeWindow.Day, difficulty = 50, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 10f,
                baits = new[] { "bait_shrimp", "bait_crab" }, lineKg = 6f, body = C("#6F8A5E"), fins = C("#4E6640"), shape = new Vector3(0.8f, 1.3f, 1.2f),
                blurb = "Round and bumpy; it sticks itself to rocks with a sucker." },
            new FishSpecies { id = "salmon", name = "Salmon", hanzi = "三文鱼", pinyin = "sān wén yú", rarity = Rarity.Uncommon, minWeight = 2f, maxWeight = 15f,
                times = TimeWindow.Morning | TimeWindow.Evening, likesRain = true, difficulty = 55, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 12f,
                baits = new[] { "bait_fish", "bait_shrimp" }, lineKg = 15f, body = C("#9AA6B0"), fins = C("#E58E6C"), model = CatchModel.LargeFish,
                blurb = "Strong, silver and always heading home." },
            new FishSpecies { id = "sablefish", name = "Sablefish", hanzi = "银鳕鱼", pinyin = "yín xuě yú", rarity = Rarity.Uncommon, minWeight = 1f, maxWeight = 6f,
                times = TimeWindow.Night, difficulty = 58, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 22f,
                baits = new[] { "bait_fish", "bait_squid" }, lineKg = 15f, body = C("#4A4E58"), fins = C("#2E3138"), model = CatchModel.LargeFish,
                blurb = "Black as night and soft as butter." },
            new FishSpecies { id = "halibut", name = "Halibut", hanzi = "大比目鱼", pinyin = "dà bǐ mù yú", rarity = Rarity.Uncommon, minWeight = 5f, maxWeight = 60f,
                times = TimeWindow.Day, difficulty = 60, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 20f,
                baits = new[] { "bait_fish", "bait_squid" }, lineKg = 40f, body = C("#7A6E5E"), fins = C("#544A3E"), model = CatchModel.LargeFish,
                shape = new Vector3(1f, 0.35f, 1.6f), blurb = "A flatfish as big as a door." },
            new FishSpecies { id = "wolffish", name = "Wolffish", hanzi = "狼鱼", pinyin = "láng yú", rarity = Rarity.Uncommon, minWeight = 2f, maxWeight = 15f,
                times = TimeWindow.Night, difficulty = 62, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 16f,
                baits = new[] { "bait_crab", "bait_squid" }, lineKg = 15f, body = C("#6E7480"), fins = C("#4A4E58"), model = CatchModel.LargeFish,
                shape = new Vector3(1.6f, 0.8f, 0.8f), blurb = "Big teeth and a grumpy face, but a gentle heart." },
            new FishSpecies { id = "icepike", name = "Ice Pike", hanzi = "冰梭鱼", pinyin = "bīng suō yú", rarity = Rarity.Rare, minWeight = 3f, maxWeight = 12f,
                times = TimeWindow.Evening | TimeWindow.Night, difficulty = 76, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 30f,
                baits = new[] { "bait_fish", "bait_squid" }, lineKg = 15f, body = C("#C6D8E4"), fins = C("#7FA2BC"), model = CatchModel.LargeFish,
                shape = new Vector3(1.8f, 0.7f, 0.7f), blurb = "A spear of a fish that hunts beneath the ice." },
            new FishSpecies { id = "auroratrout", name = "Aurora Trout", hanzi = "极光鳟", pinyin = "jí guāng zūn", rarity = Rarity.Rare, minWeight = 2f,
                maxWeight = 9f, times = TimeWindow.Night, difficulty = 78, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 26f,
                baits = new[] { "bait_fish", "bait_shrimp" }, lineKg = 15f, body = C("#5FC7A8"), fins = C("#8A6AD8"), glow = C("#6FE0C0"),
                model = CatchModel.LargeFish, blurb = "Its scales shimmer like the northern lights." },
            new FishSpecies { id = "greenlandshark", name = "Greenland Shark", hanzi = "格陵兰鲨", pinyin = "gé líng lán shā", rarity = Rarity.Rare, minWeight = 50f,
                maxWeight = 200f, times = TimeWindow.Night, difficulty = 80, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 50f,
                baits = new[] { "bait_fish", "bait_squid" }, lineKg = 40f, body = C("#5A6068"), fins = C("#3E434A"), model = CatchModel.LargeFish,
                shape = new Vector3(1.5f, 0.9f, 0.9f), blurb = "A slow, sleepy shark that may be four hundred years old." },
            new FishSpecies { id = "narwhal", name = "Narwhal", hanzi = "独角鲸", pinyin = "dú jiǎo jīng", rarity = Rarity.Rare, minWeight = 40f, maxWeight = 120f,
                times = TimeWindow.Day, difficulty = 82, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 45f, baits = new[] { "bait_fish" },
                lineKg = 40f, body = C("#8A96A4"), fins = C("#5E6874"), model = CatchModel.LargeFish, shape = new Vector3(1.7f, 0.9f, 0.9f),
                blurb = "Not really a fish, but nobody told it. Look at that tusk!" },
            new FishSpecies { id = "tuna", name = "Bluefin Tuna", hanzi = "金枪鱼", pinyin = "jīn qiāng yú", rarity = Rarity.Legendary, minWeight = 60f,
                maxWeight = 250f, times = TimeWindow.Morning | TimeWindow.Day, difficulty = 92, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 55f,
                baits = new[] { "bait_fish" }, lineKg = 40f, body = C("#273F66"), fins = C("#D9B44A"), model = CatchModel.LargeFish,
                blurb = "The king of the sea. Old Wang has dreamed of one for years." },
            new FishSpecies { id = "oarfish", name = "Oarfish", hanzi = "皇带鱼", pinyin = "huáng dài yú", rarity = Rarity.Legendary, minWeight = 40f,
                maxWeight = 200f, times = TimeWindow.Night, difficulty = 95, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 55f,
                baits = new[] { "bait_glow" }, needsBait = true, lineKg = 40f, body = C("#E3E7EE"), fins = C("#E0604E"), glow = C("#4D73D9"),
                model = CatchModel.LargeFish, shape = new Vector3(2.6f, 0.8f, 0.4f), blurb = "A silver giant from the deep, said to rise only on dark nights." },

            // ---- tier 3: Mars (a red planet with a purple sea)
            new FishSpecies { id = "dustminnow", name = "Dust Minnow", hanzi = "红沙鱼", pinyin = "hóng shā yú", rarity = Rarity.Common, minWeight = 0.05f,
                maxWeight = 0.2f, difficulty = 22, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 0f,
                baits = new[] { PlainHook, "bait_worm", "bait_dough" }, lineKg = 1f, body = C("#C8603A"), fins = C("#8E3E26"),
                blurb = "Tiny rust-red fish that swim in clouds like Martian dust." },
            new FishSpecies { id = "irongoby", name = "Iron Goby", hanzi = "铁头鱼", pinyin = "tiě tóu yú", rarity = Rarity.Common, minWeight = 0.2f, maxWeight = 1f,
                difficulty = 26, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 2f, baits = new[] { "bait_worm", "bait_crab" }, lineKg = 2f,
                body = C("#7A6E6A"), fins = C("#4E4644"), shape = new Vector3(0.9f, 1.1f, 1.1f), blurb = "Its head is as hard as a Martian rock." },
            new FishSpecies { id = "starsardine", name = "Star Sardine", hanzi = "星星鱼", pinyin = "xīng xing yú", rarity = Rarity.Common, minWeight = 0.05f,
                maxWeight = 0.3f, times = TimeWindow.Night, difficulty = 28, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 1f,
                baits = new[] { PlainHook, "bait_shrimp" }, lineKg = 1f, body = C("#D8DCF2"), fins = C("#9EA6D8"), glow = C("#E8ECFF"),
                blurb = "Twinkles at night, so you can find it in the dark." },
            new FishSpecies { id = "volcanocarp", name = "Volcano Carp", hanzi = "火山鲤", pinyin = "huǒ shān lǐ", rarity = Rarity.Common, minWeight = 0.5f,
                maxWeight = 3f, times = TimeWindow.Day, difficulty = 30, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 2f,
                baits = new[] { "bait_dough", "bait_worm" }, lineKg = 3f, body = C("#B5452E"), fins = C("#E8853A"),
                blurb = "Warm to the touch: it lives near hot springs under the sea." },
            new FishSpecies { id = "purplepuff", name = "Purple Puff", hanzi = "紫气球鱼", pinyin = "zǐ qì qiú yú", rarity = Rarity.Common, minWeight = 0.2f,
                maxWeight = 1f, times = TimeWindow.Day | TimeWindow.Evening, difficulty = 32, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 3f,
                baits = new[] { "bait_shrimp" }, lineKg = 2f, body = C("#9A5AC8"), fins = C("#D8A8F2"), shape = new Vector3(0.85f, 1.3f, 1.3f),
                blurb = "Floats like a balloon in the purple sea." },
            new FishSpecies { id = "moonfish", name = "Moonfish", hanzi = "月亮鱼", pinyin = "yuè liang yú", rarity = Rarity.Common, minWeight = 0.3f,
                maxWeight = 1.5f, times = TimeWindow.Night, difficulty = 34, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 4f,
                baits = new[] { "bait_shrimp", "bait_squid" }, lineKg = 3f, body = C("#E2E2EE"), fins = C("#B4B4CC"), glow = C("#D8D8FF"),
                shape = new Vector3(0.8f, 1.3f, 0.5f), blurb = "Round and pale; it glows softly like a little moon." },
            new FishSpecies { id = "robotfish", name = "Robot Fish", hanzi = "机器鱼", pinyin = "jī qì yú", rarity = Rarity.Common, minWeight = 0.5f, maxWeight = 2f,
                difficulty = 36, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 5f, baits = new[] { PlainHook, "bait_worm" }, lineKg = 6f,
                body = C("#A8B2BC"), fins = C("#E8743B"), shape = new Vector3(1.1f, 0.9f, 0.9f), blurb = "Nobody knows who built it. It seems quite happy." },
            new FishSpecies { id = "rocketfish", name = "Rocket Fish", hanzi = "火箭鱼", pinyin = "huǒ jiàn yú", rarity = Rarity.Common, minWeight = 0.3f,
                maxWeight = 1.5f, times = TimeWindow.Morning | TimeWindow.Day, difficulty = 40, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 7f,
                baits = new[] { "bait_shrimp", "bait_fish" }, lineKg = 6f, body = C("#E8E4DA"), fins = C("#D9534A"), shape = new Vector3(1.4f, 0.8f, 0.8f),
                blurb = "Shoots off so fast it leaves a trail of bubbles." },
            new FishSpecies { id = "crystalfish", name = "Crystal Fish", hanzi = "水晶鱼", pinyin = "shuǐ jīng yú", rarity = Rarity.Uncommon, minWeight = 0.5f,
                maxWeight = 3f, times = TimeWindow.Day, difficulty = 52, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 10f,
                baits = new[] { "bait_shrimp", "bait_crab" }, lineKg = 6f, body = C("#C8A8F2"), fins = C("#8E6AD8"), glow = C("#B48CFF"),
                blurb = "See-through like glass, with a purple heart inside." },
            new FishSpecies { id = "meteoreel", name = "Meteor Eel", hanzi = "流星鳗", pinyin = "liú xīng mán", rarity = Rarity.Uncommon, minWeight = 1f,
                maxWeight = 6f, times = TimeWindow.Night, difficulty = 55, motion = FishMotion.Smooth, barSize = 0.3f, minDistance = 12f,
                baits = new[] { "bait_squid", "bait_fish" }, lineKg = 15f, body = C("#3E4A7A"), fins = C("#6FE0F0"), glow = C("#6FE0F0"),
                model = CatchModel.LargeFish, shape = new Vector3(2f, 0.6f, 0.6f), blurb = "Streaks across the sea at night like a shooting star." },
            new FishSpecies { id = "twohead", name = "Two-Headed Trout", hanzi = "双头鱼", pinyin = "shuāng tóu yú", rarity = Rarity.Uncommon, minWeight = 1f,
                maxWeight = 5f, times = TimeWindow.Day, difficulty = 56, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 9f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 6f, body = C("#7AA06A"), fins = C("#4E7A44"), model = CatchModel.LargeFish,
                blurb = "One head eats while the other one keeps watch." },
            new FishSpecies { id = "sunfish", name = "Solar Sunfish", hanzi = "太阳鱼", pinyin = "tài yáng yú", rarity = Rarity.Uncommon, minWeight = 3f,
                maxWeight = 20f, times = TimeWindow.Day, difficulty = 58, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 18f,
                baits = new[] { "bait_squid", "bait_fish" }, lineKg = 15f, body = C("#F2A43A"), fins = C("#E8743B"), glow = C("#FFB84A"),
                model = CatchModel.LargeFish, shape = new Vector3(0.6f, 1.4f, 0.5f), blurb = "Soaks up the far-away sun and shines it back." },
            new FishSpecies { id = "alienoctopus", name = "Alien Octopus", hanzi = "外星章鱼", pinyin = "wài xīng zhāng yú", rarity = Rarity.Uncommon, minWeight = 2f,
                maxWeight = 12f, times = TimeWindow.Evening | TimeWindow.Night, difficulty = 60, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 14f,
                baits = new[] { "bait_crab", "bait_fish" }, lineKg = 15f, body = C("#6AC86A"), fins = C("#3E8A4E"), model = CatchModel.LargeFish,
                shape = new Vector3(0.7f, 1.3f, 1.3f), blurb = "Green, with nine arms. It waves with all of them." },
            new FishSpecies { id = "gravitygrouper", name = "Gravity Grouper", hanzi = "太空石斑", pinyin = "tài kōng shí bān", rarity = Rarity.Uncommon,
                minWeight = 5f, maxWeight = 30f, difficulty = 62, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 20f,
                baits = new[] { "bait_crab", "bait_fish" }, lineKg = 40f, body = C("#5E5A8A"), fins = C("#3E3A64"), model = CatchModel.LargeFish,
                shape = new Vector3(0.95f, 1.15f, 1.15f), blurb = "So heavy it bends the light around it. Probably." },
            new FishSpecies { id = "mooneel", name = "Moon Eel", hanzi = "月亮鳗", pinyin = "yuè liang mán", rarity = Rarity.Rare, minWeight = 4f, maxWeight = 20f,
                times = TimeWindow.Night, difficulty = 76, motion = FishMotion.Smooth, barSize = 0.3f, minDistance = 30f,
                baits = new[] { "bait_squid", "bait_glow" }, lineKg = 40f, body = C("#2E3E6E"), fins = C("#8AB4FF"), glow = C("#8AB4FF"),
                model = CatchModel.LargeFish, shape = new Vector3(2.2f, 0.6f, 0.6f), blurb = "A long blue eel that only comes up when both moons are out." },
            new FishSpecies { id = "nebula", name = "Nebula Angler", hanzi = "星云鱼", pinyin = "xīng yún yú", rarity = Rarity.Rare, minWeight = 5f, maxWeight = 25f,
                times = TimeWindow.Night, difficulty = 78, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 35f,
                baits = new[] { "bait_glow", "bait_fish" }, lineKg = 40f, body = C("#4A2E6E"), fins = C("#F27AC8"), glow = C("#FF7AD0"),
                model = CatchModel.LargeFish, shape = new Vector3(0.9f, 1.1f, 1.1f), blurb = "Carries a lantern on its head as bright as a nebula." },
            new FishSpecies { id = "galaxymarlin", name = "Galaxy Marlin", hanzi = "银河旗鱼", pinyin = "yín hé qí yú", rarity = Rarity.Rare, minWeight = 30f,
                maxWeight = 120f, times = TimeWindow.Day, difficulty = 80, motion = FishMotion.Dart, barSize = 0.24f, minDistance = 45f,
                baits = new[] { "bait_fish", "bait_squid" }, lineKg = 80f, body = C("#3A2E7A"), fins = C("#B48CFF"), glow = C("#9A7AFF"),
                model = CatchModel.LargeFish, shape = new Vector3(1.4f, 1.2f, 0.7f), blurb = "Its sail is full of stars." },
            new FishSpecies { id = "whaleshark", name = "Space Whale Shark", hanzi = "太空鲸鲨", pinyin = "tài kōng jīng shā", rarity = Rarity.Rare, minWeight = 80f,
                maxWeight = 300f, times = TimeWindow.Evening, difficulty = 82, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 60f,
                baits = new[] { "bait_fish", "bait_glow" }, lineKg = 80f, body = C("#4E5A7A"), fins = C("#E8ECF2"), model = CatchModel.LargeFish,
                shape = new Vector3(1.6f, 0.9f, 1f), blurb = "Gentle and enormous, with spots like a map of the stars." },
            new FishSpecies { id = "leviathan", name = "Martian Leviathan", hanzi = "火星巨鱼", pinyin = "huǒ xīng jù yú", rarity = Rarity.Legendary, minWeight = 120f,
                maxWeight = 400f, times = TimeWindow.Night, difficulty = 94, motion = FishMotion.Floater, barSize = 0.24f, minDistance = 80f,
                baits = new[] { "bait_glow" }, needsBait = true, lineKg = 80f, body = C("#8E3426"), fins = C("#E8743B"), glow = C("#FF5A3A"),
                model = CatchModel.LargeFish, shape = new Vector3(1.8f, 1f, 1f), blurb = "The oldest thing on Mars. Its back looks like a red mountain range." },
            new FishSpecies { id = "starkoi", name = "Golden Star Koi", hanzi = "金星锦鲤", pinyin = "jīn xīng jǐn lǐ", rarity = Rarity.Legendary, minWeight = 20f,
                maxWeight = 80f, times = TimeWindow.Day, difficulty = 97, motion = FishMotion.Mixed, barSize = 0.26f, minDistance = 70f,
                baits = new[] { "bait_fish", "bait_squid" }, lineKg = 80f, body = C("#F2C04A"), fins = C("#F7F2EA"), glow = C("#FFD45A"),
                model = CatchModel.LargeFish, shape = new Vector3(1.2f, 1f, 0.8f), blurb = "Said to have swum here from a distant star. It brings luck." },

        };

        public static FishSpecies Get(string id) => All.FirstOrDefault(f => f.id == id);

        /// <summary>Fish of this region's sea, plus any kind in the bag (what the shopkeepers' classifier needs to know).</summary>
        public static List<FishSpecies> LocalAndCarried()
        {
            int tier = Environment.Regions.FishTier();
            var carried = Inventory.BucketEntries.Select(b => b.speciesId).ToHashSet();
            return All.Where(f => f.IsFish && (FishPower.TierOf(f) == tier || carried.Contains(f.id))).ToList();
        }

        public static TimeWindow WindowFor(float hour)
        {
            if (hour >= 5f && hour < 10f) return TimeWindow.Morning;
            if (hour >= 10f && hour < 17f) return TimeWindow.Day;
            if (hour >= 17f && hour < 21f) return TimeWindow.Evening;
            return TimeWindow.Night;
        }

        private static float RarityWeight(Rarity r) => r switch
        {
            Rarity.Junk => 2.5f,
            Rarity.Common => 16f,
            Rarity.Uncommon => 9f,
            Rarity.Rare => 4f,
            Rarity.Legendary => 1.4f,
            _ => 1f,
        };

        /// <summary>
        /// Why this fish can't bite right now, or null if every requirement is met (junk only needs nothing).
        /// </summary>
        public static string Missing(FishSpecies f, CatchContext ctx)
        {
            if (!f.IsFish) return null;
            if (!Progression.PlayerStats.IsDiscovered(f)) return "undiscovered";
            if (FishPower.TierOf(f) != Environment.Regions.FishTier()) return $"doesn't live here (only in {Environment.Regions.Get(Environment.Regions.HomeOfTier(FishPower.TierOf(f))).english})";
            if ((f.times & WindowFor(ctx.hour)) == 0) return "wrong time of day";
            if (ctx.shoreDistance + 0.01f < f.minDistance) return $"too close to the shore (needs {f.minDistance:0} m out)";
            // Any bait works now (the liked ones make a bite much more likely, see Roll); only a few fish insist.
            if (f.needsBait && !LikesBait(f, ctx)) return "only bites on its special bait";
            if (ctx.lineKg + 0.01f < f.lineKg) return $"line too weak (needs {f.lineKg:0} kg)";
            return null;
        }

        public static IEnumerable<FishSpecies> Available(CatchContext ctx) => All.Where(f => Missing(f, ctx) == null);

        /// <summary>How much more often a fish bites on a bait it likes.</summary>
        public const float LikedBaitBonus = 4f;

        public static bool LikesBait(FishSpecies f, CatchContext ctx) => f.baits.Contains(ctx.bait != null ? ctx.bait.id : PlainHook);

        public static FishSpecies Roll(CatchContext ctx, System.Random rng = null)
        {
            var options = Available(ctx).ToList();
            if (options.Count == 0) return null; // nothing here wants this bait / line / spot
            bool rarePerk = Progression.Perks.Has("books");
            float Weight(FishSpecies f)
            {
                float w = RarityWeight(f.rarity);
                if (LikesBait(f, ctx)) w *= LikedBaitBonus;
                if (ctx.raining && f.likesRain) w *= 2f;
                // Further out than it needs to be: still bites, a bit more often for deep-water fish.
                if (f.minDistance > 0f && ctx.shoreDistance > f.minDistance + 10f) w *= 1.2f;
                // The bookseller here is an old friend: rarer fish come more often.
                if (f.rarity >= Rarity.Rare && rarePerk) w *= Progression.Perks.RareBiteBonus;
                return w;
            }
            float total = options.Sum(Weight);
            float roll = (float)(rng?.NextDouble() ?? UnityEngine.Random.value) * total;
            foreach (var f in options)
            {
                roll -= Weight(f);
                if (roll <= 0f) return f;
            }
            return options[options.Count - 1];
        }

        /// <summary>A weight within the species' range; the quality upgrade makes big ones more likely.</summary>
        /// <summary>
        /// A caught fish's weight. <paramref name="windowLow"/> (0 to 0.5) is where in its weight range a 50%-wide window
        /// starts: casting far out moves it up (see FishingController.WeightWindow). Quality training favours the top
        /// of the window.
        /// </summary>
        public static float RollWeight(FishSpecies f, int qualityLevel = 0, float windowLow = -1f)
        {
            float bias = 1.8f / (1f + 0.1f * Mathf.Max(0, qualityLevel)); // >1 favours small fish, <1 big ones (20 levels: 1.8 -> 0.6)
            float t = Mathf.Pow(UnityEngine.Random.value, bias);
            if (windowLow >= 0f) t = Mathf.Clamp(windowLow, 0f, 0.5f) + 0.5f * t;
            float kg = Mathf.Lerp(f.minWeight, f.maxWeight, t);
            return kg < 1f ? Mathf.Round(kg * 1000f) / 1000f : Mathf.Round(kg * 100f) / 100f;
        }

        public static string WeightText(float kg) => kg < 1f ? $"{kg * 1000f:0} g" : $"{kg:0.##} kg";

        public static string RarityLabel(Rarity r) => r switch
        {
            Rarity.Junk => "Treasure?",
            Rarity.Common => "Common",
            Rarity.Uncommon => "Uncommon",
            Rarity.Rare => "Rare",
            Rarity.Legendary => "Legendary",
            _ => "",
        };

        public static Color RarityColor(Rarity r) => r switch
        {
            Rarity.Junk => C("#9C8F7A"),
            Rarity.Common => C("#5E8A58"),
            Rarity.Uncommon => C("#4F8FC0"),
            Rarity.Rare => C("#A86BD1"),
            Rarity.Legendary => C("#E8A33A"),
            _ => Color.white,
        };

        public static string WhenText(FishSpecies f)
        {
            if (f.times == TimeWindow.Any) return "any time";
            var parts = new List<string>();
            if ((f.times & TimeWindow.Morning) != 0) parts.Add("mornings");
            if ((f.times & TimeWindow.Day) != 0) parts.Add("daytime");
            if ((f.times & TimeWindow.Evening) != 0) parts.Add("evenings");
            if ((f.times & TimeWindow.Night) != 0) parts.Add("nights");
            return string.Join(", ", parts);
        }

        public static string WhereText(FishSpecies f)
        {
            if (!f.IsFish) return "anywhere";
            string region = RegionText(f);
            if (f.minDistance >= 30f) return $"{f.minDistance:0}+ m out (boat){region}";
            if (f.minDistance <= 0f) return $"right by the beach{region}";
            return $"{f.minDistance:0}+ m from shore{region}";
        }

        /// <summary>", in Golden Sand Bay" for fish that don't live in every region's sea (empty for Willow Bay's).</summary>
        public static string RegionText(FishSpecies f)
        {
            int home = Environment.Regions.HomeOfTier(FishPower.TierOf(f));
            return home <= 0 ? "" : $", in {Environment.Regions.Get(home).english}";
        }

        /// <summary>"Earthworms, Shrimp Bait (or a plain hook)".</summary>
        public static string BaitText(FishSpecies f)
        {
            var names = f.baits.Where(b => b != PlainHook).Select(b => Catalog.Get(b)?.english ?? b).ToList();
            string list = names.Count > 0 ? string.Join(", ", names) : "";
            if (f.TakesPlainHook) list = list.Length > 0 ? list + " (or a plain hook)" : "a plain hook";
            return f.needsBait ? list + " only" : list + " (any bait works, these work best)";
        }

        public static string LineText(FishSpecies f)
        {
            var line = Catalog.LineFor(f.lineKg);
            return line != null ? $"{line.english} ({f.lineKg:0} kg)" : $"a {f.lineKg:0} kg line";
        }

        /// <summary>The book that teaches about this fish (null if it's known from the start).</summary>
        public static ItemDef BookFor(FishSpecies f) =>
            Catalog.Items.FirstOrDefault(i => i.category == ItemCategory.Book && i.teachesFish != null && i.teachesFish.Contains(f.id));
    }

    /// <summary>What the player caught, with its rolled weight (kg; the field keeps its old name for saves).</summary>
    public class CatchResult
    {
        public FishSpecies species;
        public float length;           // weight in kg
        public float Weight => length;
        public bool isNewSpecies;
        public bool isRecord;
        public bool inBucket;
        public bool perfect;
        public bool bonus;
        public bool golden;            // a rare golden variant
        public int newMedal;           // medal earned with this catch (0 = no new medal)
        public Vector3 position;
    }
}
