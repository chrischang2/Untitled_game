using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Fishing;
using UntitledGame.Progression;

namespace UntitledGame.Economy
{
    /// <summary>
    /// Selling fish to 陈阿姨 the sushi chef: everything goes on her scale, and each fish adds points to a bar by its
    /// rarity and size (not its weight). A common fish of the biggest size is worth about as much as an average fish
    /// one rarity up: points = 1.5^(rarity above common) x (0.5 + size), size 0 smallest to 1 biggest.
    /// The bars fill one after another: the first takes 4 common fish at 80% size, the second 8 uncommon ones, then
    /// 16 rare, 32 legendary, 64 legendary... Every bar filled adds to the price multiplier: 10% per fill, plus 2% for
    /// every lesson passed and 15% for every HSK test passed. So bigger hauls (and studying) pay more.
    /// </summary>
    public static class FishSale
    {
        public const float BasePerFill = 0.10f, PerLesson = 0.02f, PerTest = 0.15f;
        public const float RarityStep = 1.5f, FillQuality = 0.8f;

        /// <summary>A fish's points on the scale: rarer and bigger fish fill the bar faster. Junk barely counts.</summary>
        public static float Points(FishSpecies s, float kg)
        {
            if (s == null || !s.IsFish) return 0.2f;
            float size = Mathf.InverseLerp(s.minWeight, s.maxWeight, kg);
            return Mathf.Pow(RarityStep, Mathf.Max(0, (int)s.rarity - (int)Rarity.Common)) * (0.5f + size);
        }

        /// <summary>How many points bar k (0 = the first) holds: 4 x 2^k fish of rarity k (capped at legendary) at 80% size.</summary>
        public static float BarCapacity(int k) =>
            4f * Mathf.Pow(2f, k) * Mathf.Pow(RarityStep, Mathf.Min(k, (int)Rarity.Legendary - (int)Rarity.Common)) * (0.5f + FillQuality);

        /// <summary>The fish count and rarity that fill bar k (for the scale's caption).</summary>
        public static (int count, Rarity rarity) BarRecipe(int k) =>
            (4 * (1 << Mathf.Min(k, 20)), (Rarity)Mathf.Min((int)Rarity.Common + k, (int)Rarity.Legendary));

        public class Fish
        {
            public FishSpecies species;
            public float kg;
            public float points;  // what it adds to the bar
            public bool golden;
            public int value;   // her price for it alone (before the scale's multiplier)
        }

        public class Quote
        {
            public List<Fish> fish = new List<Fish>();
            public float kg;
            public float points;        // the bar's total
            public int baseValue;       // sum of the fish's own prices
            public int friendshipPct;   // 5% per friendship level with her
            public int fills;           // stages of the bar reached
            public float perFill;       // multiplier per fill
            public float multiplier;    // 1 + fills x perFill
            public int total;           // what she pays
        }

        /// <summary>Fired when a sale is weighed (the UI plays the scale animation).</summary>
        public static event Action<Quote> Weighed;

        public static int LessonsPassed => Enumerable.Range(1, Hsk.MaxLevel).Sum(Hsk.LessonsDone);
        public static float PerFill => BasePerFill + PerLesson * LessonsPassed + PerTest * Hsk.Level;
        /// <summary>Bars filled by this many points.</summary>
        public static int Fills(float points)
        {
            int k = 0;
            float need = BarCapacity(0);
            while (points + 0.0001f >= need && k < 60) { points -= need; k++; need = BarCapacity(k); }
            return k;
        }

        /// <summary>How full the current bar is (0..1) at this many points.</summary>
        public static float FillFraction(float points)
        {
            int k = 0;
            float need = BarCapacity(0);
            while (points + 0.0001f >= need && k < 60) { points -= need; k++; need = BarCapacity(k); }
            return Mathf.Clamp01(points / need);
        }

        /// <summary>What she'd pay for these fish (all of them weighed together).</summary>
        public static Quote For(IEnumerable<BucketFish> fish, int friendshipLevel)
        {
            var q = new Quote { friendshipPct = 5 * Mathf.Max(0, friendshipLevel), perFill = PerFill };
            foreach (var b in fish)
            {
                var s = FishDatabase.Get(b.speciesId);
                if (s == null) continue;
                q.fish.Add(new Fish { species = s, kg = b.length, points = Points(s, b.length), golden = b.golden, value = Inventory.FishValue(b) });
            }
            q.kg = q.fish.Sum(f => f.kg);
            q.points = q.fish.Sum(f => f.points);
            q.baseValue = q.fish.Sum(f => f.value);
            q.fills = Fills(q.points);
            q.multiplier = 1f + q.fills * q.perFill;
            q.total = Mathf.RoundToInt(q.baseValue * (1f + q.friendshipPct / 100f) * q.multiplier);
            return q;
        }

        /// <summary>The fish a sale would include: one species, or everything ("all").</summary>
        public static List<BucketFish> Selection(string speciesId) =>
            Inventory.BucketEntries.Where(b => speciesId == "all" || string.IsNullOrEmpty(speciesId) || b.speciesId == speciesId).ToList();

        public static void Announce(Quote q)
        {
            ChatAudit.Write("SALE", $"scale: {q.fish.Count} fish, {q.points:0.0} points, {q.fills} fills x {q.perFill:P0} = x{q.multiplier:0.00}; base ¥{q.baseValue}, friendship +{q.friendshipPct}% -> ¥{q.total}");
            Weighed?.Invoke(q);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Weighed = null;
    }
}
