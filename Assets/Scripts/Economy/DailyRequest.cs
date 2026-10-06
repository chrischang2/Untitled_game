using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Fishing;

namespace UntitledGame.Economy
{
    /// <summary>
    /// Auntie Chen's fish of the day: each morning she wants a few of one kind of fish you know (sometimes big ones).
    /// She asks in Chinese; bring them and sell them to her and she gives you a surprise on top of the price.
    /// </summary>
    public static class DailyRequest
    {
        private static SaveData D => SaveSystem.Data;
        private static int Today => D.day;

        public static FishSpecies Fish => FishDatabase.Get(D.requestFish);
        public static int Count => Mathf.Max(1, D.requestCount);
        public static float MinKg => D.requestMinKg;
        public static bool Big => D.requestMinKg > 0f;
        public static bool Done => D.requestDay == Today && D.requestDone;

        /// <summary>Today's request exists and isn't done yet.</summary>
        public static bool Active
        {
            get
            {
                Ensure();
                return D.requestDay == Today && !D.requestDone && Fish != null;
            }
        }

        /// <summary>Picks today's request (once a day) from the fish the player knows about.</summary>
        public static void Ensure()
        {
            if (D.requestDay == Today) return;
            var known = Progression.PlayerStats.Discovered.ToList();
            D.requestDay = Today;
            D.requestDone = false;
            if (known.Count == 0)
            {
                D.requestFish = "";
                return;
            }
            // Common fish more often; rarer ones now and then, and only one of those.
            var rng = new System.Random(Today * 7919 + known.Count);
            float Weight(FishSpecies f) => f.rarity switch { Rarity.Common => 6f, Rarity.Uncommon => 3f, Rarity.Rare => 1f, _ => 0.25f };
            float roll = (float)rng.NextDouble() * known.Sum(Weight);
            var pick = known[known.Count - 1];
            foreach (var f in known)
            {
                roll -= Weight(f);
                if (roll <= 0f) { pick = f; break; }
            }
            D.requestFish = pick.id;
            D.requestCount = pick.rarity >= Rarity.Rare ? 1 : 1 + rng.Next(3);
            D.requestMinKg = rng.NextDouble() < 0.3 ? Mathf.Lerp(pick.minWeight, pick.maxWeight, 0.5f) : 0f;
            ChatAudit.Write("REQUEST", $"day {Today}: Auntie Chen wants {D.requestCount} x {pick.name}{(D.requestMinKg > 0 ? $" of {D.requestMinKg:0.0} kg+" : "")}");
        }

        /// <summary>"今天我想要两条大鲈鱼" (what she says, and what the shop window shows).</summary>
        public static string Chinese => Fish == null ? "" : $"今天我想要{Catalog.CountNumber(Count)}条{(Big ? "大" : "")}{Fish.hanzi}";

        /// <summary>Fish in the bucket that would do.</summary>
        public static int Matching => Fish == null ? 0 : Inventory.BucketEntries.Count(b => b.speciesId == Fish.id && b.length + 0.001f >= MinKg);

        public static bool CanFulfil => Active && Matching >= Count;

        /// <summary>Marks today's request done and hands out a surprise. Returns what it was (English, for the toast).</summary>
        public static string Fulfil(System.Random rng = null)
        {
            D.requestDone = true;
            var fish = Fish;
            string reward = Surprise.Give("Auntie Chen", fish != null && fish.rarity >= Rarity.Rare ? 3 : 2, rng);
            ChatAudit.Write("REQUEST", $"fulfilled ({Count} x {fish?.name}); surprise: {reward}");
            return reward;
        }
    }

    /// <summary>
    /// Surprise presents: a random little something (money, bait or a gift), never promised in advance. Unexpected rewards
    /// don't spoil the fun of what earned them the way expected payments do.
    /// </summary>
    public static class Surprise
    {
        /// <summary>Gives a random present of the given size (1 small, 2 nice, 3 generous); returns a description.</summary>
        public static string Give(string from, int size, System.Random rng = null, float moneyScale = 1f)
        {
            double r = rng?.NextDouble() ?? Random.value;
            int Next(int lo, int hi) => rng != null ? rng.Next(lo, hi) : Random.Range(lo, hi);
            if (r < 0.4)
            {
                int money = Mathf.Max(1, Mathf.RoundToInt(Next(10, 30) * size * size * moneyScale));
                Inventory.Earn(money);
                return $"¥{money}";
            }
            if (r < 0.75)
            {
                var baits = Catalog.Items.Where(i => i.category == ItemCategory.Bait && i.minHsk <= Progression.Hsk.Level && i.id != "bait_glow").ToList();
                var bait = baits[Next(0, baits.Count)];
                int n = bait.packSize * (size >= 3 ? 2 : 1);
                Inventory.Add(bait.id, n);
                return $"{n} {bait.english.ToLower()}";
            }
            var gifts = Catalog.Items.Where(i => i.category == ItemCategory.Gift && i.minAffinity == 0).ToList();
            var gift = gifts[Next(0, gifts.Count)];
            Inventory.Add(gift.id, 1);
            return $"{gift.english.ToLower()} ({gift.hanzi}, a gift for someone)";
        }
    }
}
