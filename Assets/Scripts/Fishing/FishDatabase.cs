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
    /// One kind of sea creature. A fish can only bite once every requirement is met: the player has read about it
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
        public string[] baits = new string[0]; // bait item ids it bites on; "none" = a plain hook works too
        public float lineKg;             // line strength (kg) needed
        public Color body = Color.gray;
        public Color fins = Color.white;
        public Color glow = Color.clear;
        public CatchModel model = CatchModel.SmallFish;
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
            // ---- near the beach (known from the start / 钓鱼入门)
            new FishSpecies { id = "sardine", name = "Sardine", hanzi = "沙丁鱼", pinyin = "shā dīng yú", rarity = Rarity.Common,
                minWeight = 0.03f, maxWeight = 0.15f, difficulty = 15, motion = FishMotion.Smooth, barSize = 0.34f, minDistance = 0f,
                baits = new[] { PlainHook, "bait_worm", "bait_dough" }, lineKg = 1f, body = C("#9FB4C4"), fins = C("#6F8798"),
                blurb = "Small, silvery and everywhere. Everybody's first catch." },
            new FishSpecies { id = "croaker", name = "Yellow Croaker", hanzi = "小黄鱼", pinyin = "xiǎo huáng yú", rarity = Rarity.Common,
                minWeight = 0.1f, maxWeight = 0.6f, times = TimeWindow.Morning | TimeWindow.Day | TimeWindow.Evening, difficulty = 25,
                motion = FishMotion.Mixed, barSize = 0.30f, minDistance = 2f, baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 1f,
                body = C("#E2C25A"), fins = C("#C99A35"), blurb = "Golden and chatty: it really does croak." },
            new FishSpecies { id = "flounder", name = "Flounder", hanzi = "比目鱼", pinyin = "bǐ mù yú", rarity = Rarity.Common,
                minWeight = 0.3f, maxWeight = 2.5f, likesRain = true, difficulty = 30, motion = FishMotion.Sinker, barSize = 0.28f, minDistance = 3f,
                baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 2f, body = C("#A68B66"), fins = C("#7E6648"),
                blurb = "Flat as a plate, with both eyes on one side." },
            new FishSpecies { id = "mackerel", name = "Mackerel", hanzi = "鲭鱼", pinyin = "qīng yú", rarity = Rarity.Common,
                minWeight = 0.3f, maxWeight = 1.2f, times = TimeWindow.Morning | TimeWindow.Day, difficulty = 40, motion = FishMotion.Dart,
                barSize = 0.26f, minDistance = 6f, baits = new[] { "bait_worm", "bait_shrimp" }, lineKg = 2f,
                body = C("#4E7FA0"), fins = C("#2F4E63"), blurb = "Striped, fast, and travels in big shiny schools." },
            new FishSpecies { id = "pufferfish", name = "Pufferfish", hanzi = "河豚", pinyin = "hé tún", rarity = Rarity.Uncommon,
                minWeight = 0.2f, maxWeight = 1.5f, times = TimeWindow.Day | TimeWindow.Evening, difficulty = 55, motion = FishMotion.Floater,
                barSize = 0.22f, minDistance = 5f, baits = new[] { "bait_shrimp", "bait_crab" }, lineKg = 2f,
                body = C("#D9C27A"), fins = C("#A88F4C"), blurb = "Puffs up into a spiky ball when it's cross." },

            // ---- the shore (海边的鱼)
            new FishSpecies { id = "seabream", name = "Sea Bream", hanzi = "鲷鱼", pinyin = "diāo yú", rarity = Rarity.Uncommon,
                minWeight = 0.5f, maxWeight = 4f, times = TimeWindow.Morning | TimeWindow.Day, difficulty = 50, motion = FishMotion.Mixed,
                barSize = 0.24f, minDistance = 10f, baits = new[] { "bait_shrimp", "bait_crab", "bait_squid" }, lineKg = 5f,
                body = C("#E28A86"), fins = C("#B9585A"), model = CatchModel.LargeFish, blurb = "Pink and lucky: the fish for celebrations." },
            new FishSpecies { id = "seabass", name = "Sea Bass", hanzi = "鲈鱼", pinyin = "lú yú", rarity = Rarity.Uncommon,
                minWeight = 0.5f, maxWeight = 6f, times = TimeWindow.Morning | TimeWindow.Evening | TimeWindow.Night, likesRain = true,
                difficulty = 55, motion = FishMotion.Mixed, barSize = 0.24f, minDistance = 8f, baits = new[] { "bait_shrimp", "bait_fish" },
                lineKg = 6f, body = C("#8E9EA6"), fins = C("#5D6B72"), model = CatchModel.LargeFish, blurb = "Hunts along the shore when the light is low." },
            new FishSpecies { id = "hairtail", name = "Hairtail", hanzi = "带鱼", pinyin = "dài yú", rarity = Rarity.Uncommon,
                minWeight = 0.3f, maxWeight = 2f, times = TimeWindow.Evening | TimeWindow.Night, difficulty = 60, motion = FishMotion.Dart,
                barSize = 0.22f, minDistance = 14f, baits = new[] { "bait_squid", "bait_fish" }, lineKg = 3f,
                body = C("#D7DDE3"), fins = C("#A9B3BC"), blurb = "Long, thin and shiny like a silver ribbon." },

            // ---- under the rocks (石头下的鱼)
            new FishSpecies { id = "octopus", name = "Octopus", hanzi = "章鱼", pinyin = "zhāng yú", rarity = Rarity.Uncommon,
                minWeight = 1f, maxWeight = 8f, times = TimeWindow.Evening | TimeWindow.Night, difficulty = 65, motion = FishMotion.Sinker,
                barSize = 0.22f, minDistance = 6f, baits = new[] { "bait_crab" }, lineKg = 6f,
                body = C("#B5655A"), fins = C("#8B4740"), model = CatchModel.LargeFish, blurb = "Clever, shy and very good at hiding." },
            new FishSpecies { id = "conger", name = "Conger Eel", hanzi = "海鳗", pinyin = "hǎi mán", rarity = Rarity.Rare,
                minWeight = 1f, maxWeight = 12f, times = TimeWindow.Night, likesRain = true, difficulty = 70, motion = FishMotion.Smooth,
                barSize = 0.20f, minDistance = 12f, baits = new[] { "bait_squid", "bait_fish" }, lineKg = 15f,
                body = C("#5B5E52"), fins = C("#3E4038"), model = CatchModel.LargeFish, blurb = "Lives in rocky holes and comes out on rainy nights." },
            new FishSpecies { id = "grouper", name = "Grouper", hanzi = "石斑鱼", pinyin = "shí bān yú", rarity = Rarity.Rare,
                minWeight = 2f, maxWeight = 25f, times = TimeWindow.Day | TimeWindow.Evening, difficulty = 72, motion = FishMotion.Sinker,
                barSize = 0.20f, minDistance = 18f, baits = new[] { "bait_crab", "bait_fish" }, lineKg = 15f,
                body = C("#8B6A4A"), fins = C("#5E4632"), model = CatchModel.LargeFish, blurb = "A heavy, spotty rock-dweller with a huge mouth." },

            // ---- the open sea (远海的鱼): the boat
            new FishSpecies { id = "skipjack", name = "Skipjack Tuna", hanzi = "鲣鱼", pinyin = "jiān yú", rarity = Rarity.Rare,
                minWeight = 2f, maxWeight = 8f, times = TimeWindow.Morning | TimeWindow.Day, difficulty = 75, motion = FishMotion.Dart,
                barSize = 0.19f, minDistance = 30f, baits = new[] { "bait_fish", "bait_squid" }, lineKg = 15f,
                body = C("#3F5E86"), fins = C("#9FB3C9"), model = CatchModel.LargeFish, blurb = "A little tuna that never stops swimming." },
            new FishSpecies { id = "sailfish", name = "Sailfish", hanzi = "旗鱼", pinyin = "qí yú", rarity = Rarity.Rare,
                minWeight = 25f, maxWeight = 90f, times = TimeWindow.Day, difficulty = 85, motion = FishMotion.Dart,
                barSize = 0.17f, minDistance = 45f, baits = new[] { "bait_fish", "bait_squid" }, lineKg = 40f,
                body = C("#2E5C9A"), fins = C("#6FA0D8"), model = CatchModel.LargeFish, blurb = "The fastest fish in the sea, with a sail on its back." },
            new FishSpecies { id = "shark", name = "Blue Shark", hanzi = "鲨鱼", pinyin = "shā yú", rarity = Rarity.Legendary,
                minWeight = 30f, maxWeight = 150f, times = TimeWindow.Evening | TimeWindow.Night, difficulty = 88, motion = FishMotion.Mixed,
                barSize = 0.17f, minDistance = 50f, baits = new[] { "bait_squid", "bait_fish" }, lineKg = 40f,
                body = C("#4F78A8"), fins = C("#C9D6E3"), model = CatchModel.LargeFish, blurb = "Long, blue and elegant. Mei pretends she isn't scared." },

            // ---- legends (海的传说)
            new FishSpecies { id = "tuna", name = "Bluefin Tuna", hanzi = "金枪鱼", pinyin = "jīn qiāng yú", rarity = Rarity.Legendary,
                minWeight = 60f, maxWeight = 250f, times = TimeWindow.Morning | TimeWindow.Day, difficulty = 92, motion = FishMotion.Mixed,
                barSize = 0.15f, minDistance = 60f, baits = new[] { "bait_fish" }, lineKg = 80f,
                body = C("#273F66"), fins = C("#D9B44A"), model = CatchModel.LargeFish, blurb = "The king of the sea. Old Wang has dreamed of one for years." },
            new FishSpecies { id = "oarfish", name = "Oarfish", hanzi = "皇带鱼", pinyin = "huáng dài yú", rarity = Rarity.Legendary,
                minWeight = 40f, maxWeight = 200f, times = TimeWindow.Night, difficulty = 95, motion = FishMotion.Floater,
                barSize = 0.15f, minDistance = 55f, baits = new[] { "bait_glow" }, lineKg = 40f,
                body = C("#E3E7EE"), fins = C("#E0604E"), glow = new Color(0.3f, 0.45f, 0.85f), model = CatchModel.LargeFish,
                blurb = "A silver giant from the deep, said to rise only on dark nights." },

        };

        public static FishSpecies Get(string id) => All.FirstOrDefault(f => f.id == id);

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
            if ((f.times & WindowFor(ctx.hour)) == 0) return "wrong time of day";
            if (ctx.shoreDistance + 0.01f < f.minDistance) return $"too close to the shore (needs {f.minDistance:0} m out)";
            string bait = ctx.bait != null ? ctx.bait.id : PlainHook;
            if (!f.baits.Contains(bait)) return "doesn't like this bait";
            if (ctx.lineKg + 0.01f < f.lineKg) return $"line too weak (needs {f.lineKg:0} kg)";
            return null;
        }

        public static IEnumerable<FishSpecies> Available(CatchContext ctx) => All.Where(f => Missing(f, ctx) == null);

        public static FishSpecies Roll(CatchContext ctx, System.Random rng = null)
        {
            var options = Available(ctx).ToList();
            if (options.Count == 0) return null; // nothing here wants this bait / line / spot
            float Weight(FishSpecies f)
            {
                float w = RarityWeight(f.rarity);
                if (ctx.raining && f.likesRain) w *= 2f;
                // Further out than it needs to be: still bites, a bit more often for deep-water fish.
                if (f.minDistance > 0f && ctx.shoreDistance > f.minDistance + 10f) w *= 1.2f;
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
        public static float RollWeight(FishSpecies f, int qualityLevel = 0)
        {
            float bias = 1.8f / (1f + 0.4f * Mathf.Max(0, qualityLevel)); // >1 favours small fish, <1 big ones
            float t = Mathf.Pow(UnityEngine.Random.value, bias);
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
            if (f.minDistance >= 30f) return $"{f.minDistance:0}+ m out (boat)";
            if (f.minDistance <= 0f) return "right by the beach";
            return $"{f.minDistance:0}+ m from shore";
        }

        /// <summary>"Earthworms, Shrimp Bait (or a plain hook)".</summary>
        public static string BaitText(FishSpecies f)
        {
            var names = f.baits.Where(b => b != PlainHook).Select(b => Catalog.Get(b)?.english ?? b).ToList();
            string list = names.Count > 0 ? string.Join(", ", names) : "";
            if (f.TakesPlainHook) list = list.Length > 0 ? list + " (or a plain hook)" : "a plain hook";
            return list;
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
        public Vector3 position;
    }
}
