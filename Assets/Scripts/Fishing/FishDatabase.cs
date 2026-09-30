using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Environment;

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

    /// <summary>One catchable thing. Chinese names are here from day one for the language-learning mode.</summary>
    public class FishSpecies
    {
        public string id;
        public string name;
        public string hanzi;
        public string pinyin;
        public Rarity rarity;
        public float minLength;
        public float maxLength;
        public TimeWindow times = TimeWindow.Any;
        public float minDepth;
        public bool likesRain;
        public float difficulty; // 0 (calm) .. 1 (feisty)
        public Color body = Color.gray;
        public Color fins = Color.white;
        public Color glow = Color.clear;
        public CatchModel model = CatchModel.SmallFish;
        public string blurb;

        public bool IsFish => rarity != Rarity.Junk;
        public string LengthText(float cm) => IsFish ? $"{cm:0.#} cm" : "";
    }

    public struct CatchContext
    {
        public float hour;
        public float depth;
        public bool raining;
        public bool nearLilies;
        public Economy.ItemDef bait;   // null = plain hook
        public float rareBonus;        // from the rod
    }

    public static class FishDatabase
    {
        private static Color C(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        public static readonly List<FishSpecies> All = new List<FishSpecies>
        {
            new FishSpecies { id = "minnow", name = "Minnow", hanzi = "小鱼", pinyin = "xiǎo yú", rarity = Rarity.Common,
                minLength = 4, maxLength = 9, difficulty = 0.05f, body = C("#B9C7CF"), fins = C("#8FA3AD"),
                blurb = "Tiny, quick and everywhere. Everybody's first catch." },
            new FishSpecies { id = "bluegill", name = "Bluegill", hanzi = "蓝鳃鱼", pinyin = "lán sāi yú", rarity = Rarity.Common,
                minLength = 10, maxLength = 24, times = TimeWindow.Morning | TimeWindow.Day, minDepth = 0.4f, difficulty = 0.15f,
                body = C("#5C8FB8"), fins = C("#F0A04B"), blurb = "Loves warm shallows and sunny afternoons." },
            new FishSpecies { id = "crucian", name = "Crucian Carp", hanzi = "鲫鱼", pinyin = "jì yú", rarity = Rarity.Common,
                minLength = 10, maxLength = 32, minDepth = 0.6f, difficulty = 0.2f, body = C("#C9A95B"), fins = C("#A7773C"),
                blurb = "A golden little carp that makes the best fish soup." },
            new FishSpecies { id = "perch", name = "Perch", hanzi = "河鲈", pinyin = "hé lú", rarity = Rarity.Common,
                minLength = 15, maxLength = 40, times = TimeWindow.Morning | TimeWindow.Day, minDepth = 0.8f, difficulty = 0.3f,
                body = C("#9DB35A"), fins = C("#E0663A"), blurb = "Striped and bold, hunts in little gangs." },
            new FishSpecies { id = "loach", name = "Loach", hanzi = "泥鳅", pinyin = "ní qiu", rarity = Rarity.Common,
                minLength = 8, maxLength = 20, times = TimeWindow.Evening | TimeWindow.Night, likesRain = true, difficulty = 0.2f,
                body = C("#8A6B4A"), fins = C("#5E4632"), blurb = "Wriggly mud-dweller. Comes out when it rains." },
            new FishSpecies { id = "carp", name = "Common Carp", hanzi = "鲤鱼", pinyin = "lǐ yú", rarity = Rarity.Uncommon,
                minLength = 30, maxLength = 80, minDepth = 1.2f, difficulty = 0.45f, model = CatchModel.LargeFish,
                body = C("#B58A45"), fins = C("#C0603A"), blurb = "A strong, patient fish. A symbol of luck and perseverance." },
            new FishSpecies { id = "grass_carp", name = "Grass Carp", hanzi = "草鱼", pinyin = "cǎo yú", rarity = Rarity.Uncommon,
                minLength = 40, maxLength = 100, times = TimeWindow.Day, minDepth = 1.2f, difficulty = 0.5f, model = CatchModel.LargeFish,
                body = C("#8C9A5C"), fins = C("#6B7445"), blurb = "Munches water plants all day long." },
            new FishSpecies { id = "trout", name = "Rainbow Trout", hanzi = "虹鳟", pinyin = "hóng zūn", rarity = Rarity.Uncommon,
                minLength = 25, maxLength = 60, times = TimeWindow.Morning | TimeWindow.Evening, minDepth = 1.5f, difficulty = 0.55f,
                model = CatchModel.LargeFish, body = C("#9FB7C9"), fins = C("#E57A9A"), blurb = "Shimmers pink and silver in the low sun." },
            new FishSpecies { id = "bass", name = "Largemouth Bass", hanzi = "大口黑鲈", pinyin = "dà kǒu hēi lú", rarity = Rarity.Uncommon,
                minLength = 25, maxLength = 60, times = TimeWindow.Morning | TimeWindow.Evening, minDepth = 1f, difficulty = 0.5f,
                model = CatchModel.LargeFish, body = C("#5E8A4C"), fins = C("#3E5E33"), blurb = "Ambushes from the lily pads." },
            new FishSpecies { id = "catfish", name = "Catfish", hanzi = "鲶鱼", pinyin = "nián yú", rarity = Rarity.Uncommon,
                minLength = 30, maxLength = 90, times = TimeWindow.Evening | TimeWindow.Night, minDepth = 2f, likesRain = true,
                difficulty = 0.55f, model = CatchModel.LargeFish, body = C("#5B5F66"), fins = C("#3B3E44"),
                blurb = "Whiskered night owl of the lake bottom." },
            new FishSpecies { id = "koi", name = "Golden Koi", hanzi = "锦鲤", pinyin = "jǐn lǐ", rarity = Rarity.Rare,
                minLength = 30, maxLength = 70, times = TimeWindow.Morning, minDepth = 0.8f, difficulty = 0.5f,
                model = CatchModel.LargeFish, body = C("#F08A2E"), fins = C("#FFF2DC"), blurb = "Said to bring good fortune to whoever sees one." },
            new FishSpecies { id = "pike", name = "Pike", hanzi = "狗鱼", pinyin = "gǒu yú", rarity = Rarity.Rare,
                minLength = 50, maxLength = 120, times = TimeWindow.Day, minDepth = 2f, difficulty = 0.75f, model = CatchModel.LargeFish,
                body = C("#6E8F4E"), fins = C("#B3A04A"), blurb = "Toothy and dramatic. Hold on tight!" },
            new FishSpecies { id = "snakehead", name = "Snakehead", hanzi = "黑鱼", pinyin = "hēi yú", rarity = Rarity.Rare,
                minLength = 40, maxLength = 90, times = TimeWindow.Day | TimeWindow.Evening, minDepth = 1f, difficulty = 0.7f,
                model = CatchModel.LargeFish, body = C("#3F4A3A"), fins = C("#6B5A3A"), blurb = "Can gulp air. A little bit grumpy." },
            new FishSpecies { id = "eel", name = "Eel", hanzi = "鳗鱼", pinyin = "mán yú", rarity = Rarity.Rare,
                minLength = 40, maxLength = 100, times = TimeWindow.Night, minDepth = 2f, likesRain = true, difficulty = 0.65f,
                body = C("#4D5A3A"), fins = C("#6E7A4A"), blurb = "Slippery traveller of rainy nights." },
            new FishSpecies { id = "sturgeon", name = "Old Sturgeon", hanzi = "鲟鱼", pinyin = "xún yú", rarity = Rarity.Legendary,
                minLength = 100, maxLength = 200, times = TimeWindow.Night, minDepth = 3.5f, difficulty = 0.95f,
                model = CatchModel.LargeFish, body = C("#6F7A80"), fins = C("#A8B0B5"), blurb = "An ancient giant of the deep water. Mei's never seen one." },
            new FishSpecies { id = "moon_koi", name = "Moonlight Koi", hanzi = "月光锦鲤", pinyin = "yuè guāng jǐn lǐ", rarity = Rarity.Legendary,
                minLength = 50, maxLength = 80, times = TimeWindow.Night, minDepth = 1.5f, difficulty = 0.85f, model = CatchModel.LargeFish,
                body = C("#CFE3FF"), fins = C("#9CC3FF"), glow = new Color(0.25f, 0.4f, 0.8f), blurb = "Glows softly under the stars. A lake legend." },
            new FishSpecies { id = "bottle", name = "Message in a Bottle", hanzi = "漂流瓶", pinyin = "piāo liú píng", rarity = Rarity.Junk,
                model = CatchModel.Bottle, blurb = "There's a tiny note inside... it just says \"hi :)\"" },
            new FishSpecies { id = "teacup", name = "Lost Teacup", hanzi = "茶杯", pinyin = "chá bēi", rarity = Rarity.Junk,
                model = CatchModel.Teacup, blurb = "Somebody's favourite cup. Mei might want it." },
            new FishSpecies { id = "driftwood", name = "Driftwood", hanzi = "浮木", pinyin = "fú mù", rarity = Rarity.Junk,
                model = CatchModel.Driftwood, blurb = "Smooth and sun-bleached. Good for the campfire." },
        };

        public static FishSpecies Get(string id) => All.FirstOrDefault(f => f.id == id);

        public static int CatchableFishCount => All.Count;

        public static TimeWindow WindowFor(float hour)
        {
            if (hour >= 5f && hour < 10f) return TimeWindow.Morning;
            if (hour >= 10f && hour < 17f) return TimeWindow.Day;
            if (hour >= 17f && hour < 21f) return TimeWindow.Evening;
            return TimeWindow.Night;
        }

        private static float RarityWeight(Rarity r) => r switch
        {
            Rarity.Junk => 3f,
            Rarity.Common => 16f,
            Rarity.Uncommon => 7f,
            Rarity.Rare => 2.2f,
            Rarity.Legendary => 0.45f,
            _ => 1f,
        };

        public static IEnumerable<FishSpecies> Available(CatchContext ctx)
        {
            var window = WindowFor(ctx.hour);
            return All.Where(f => (f.times & window) != 0 && ctx.depth + 0.01f >= f.minDepth);
        }

        public static FishSpecies Roll(CatchContext ctx, System.Random rng = null)
        {
            var options = Available(ctx).ToList();
            if (options.Count == 0) return All[0];
            float Weight(FishSpecies f)
            {
                float w = RarityWeight(f.rarity);
                if (ctx.raining && f.likesRain) w *= 2.2f;
                if (ctx.nearLilies && (f.id == "bass" || f.id == "koi" || f.id == "bluegill")) w *= 1.8f;
                if (f.minDepth > 1.5f) w *= Mathf.Lerp(0.6f, 1.4f, Mathf.InverseLerp(f.minDepth, f.minDepth + 2f, ctx.depth));
                if (f.rarity == Rarity.Rare) w *= 1f + ctx.rareBonus;
                if (f.rarity == Rarity.Legendary) w *= 1f + ctx.rareBonus * 2f;
                var b = ctx.bait;
                if (b != null && (!b.nightOnly || WindowFor(ctx.hour) == TimeWindow.Night))
                {
                    w *= f.rarity switch
                    {
                        Rarity.Common => b.commonMult,
                        Rarity.Uncommon => b.uncommonMult,
                        Rarity.Rare => b.rareMult,
                        Rarity.Legendary => b.legendaryMult,
                        _ => 1f,
                    };
                    if (b.favouredFish != null && System.Array.IndexOf(b.favouredFish, f.id) >= 0) w *= 2.4f;
                    if (f.rarity == Rarity.Junk) w *= 0.6f;
                }
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

        public static float RollLength(FishSpecies f)
        {
            if (!f.IsFish) return 0f;
            // Bias towards smaller fish; big ones are special.
            float t = Mathf.Pow(UnityEngine.Random.value, 1.6f);
            return Mathf.Round(Mathf.Lerp(f.minLength, f.maxLength, t) * 10f) / 10f;
        }

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
            Rarity.Common => C("#7FA37A"),
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
            if (f.minDepth >= 3f) return "the deepest water";
            if (f.minDepth >= 1.5f) return "deep water";
            if (f.minDepth >= 0.8f) return "a little way out";
            return "shallows";
        }
    }

    /// <summary>What the player caught, with its rolled size.</summary>
    public class CatchResult
    {
        public FishSpecies species;
        public float length;
        public bool isNewSpecies;
        public bool isRecord;
        public bool inBucket;
        public Vector3 position;
    }
}
