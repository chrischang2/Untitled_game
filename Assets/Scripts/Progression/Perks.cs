using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;

namespace UntitledGame.Progression
{
    /// <summary>
    /// What each stall gives you at the highest friendship (老朋友), in the region where that keeper lives. Every region
    /// has its own keepers, so each perk is earned again with the new people at each stop:
    /// - tackle: a free pack of the best bait on sale, every morning
    /// - fish (the cook who buys your fish): +20% on every sale
    /// - furniture: a one-of-a-kind piece for this region's house (comfort 6)
    /// - pet: Tangyuan brings twice as much bait
    /// - books: rare and legendary fish here bite 30% more often
    /// - gym: training costs 25% less
    /// - colours: an exclusive boat paint
    /// - gifts: a free gift every morning
    /// - school: a present after every lesson passed, and a bigger one
    /// - crabber: the crab pots bring in 25% more
    /// </summary>
    public static class Perks
    {
        public static readonly string[] Roles = { "tackle", "fish", "furniture", "pet", "books", "gym", "colours", "gifts", "school", "crabber" };

        public static string Describe(string role) => role switch
        {
            "tackle" => "a free pack of the best bait here every morning",
            "fish" => "+20% on every fish you sell to them",
            "furniture" => "a one-of-a-kind piece of furniture for your house here",
            "pet" => "Tangyuan brings twice as much bait",
            "books" => "rare and legendary fish here bite 30% more often",
            "gym" => "training with them costs 25% less",
            "colours" => "an exclusive boat paint",
            "gifts" => "a free gift every morning",
            "school" => "a present after every lesson you pass",
            "crabber" => "your crab pots bring in 25% more",
            _ => "",
        };

        /// <summary>The keeper of this kind of stall in the current region has become an old friend.</summary>
        public static bool Has(string role)
        {
            var shop = Catalog.ShopFor(role, Regions.Current);
            return shop != null && Affinity.Level(shop.id) >= Affinity.MaxLevel;
        }

        public const float FishSaleBonus = 0.2f, RareBiteBonus = 1.3f, TrainingDiscount = 0.75f, CrabBonus = 1.25f;

        private static SaveData D => SaveSystem.Data;

        /// <summary>One-time rewards (signature furniture, boat paint) for every old friend not yet rewarded.</summary>
        public static void CheckGrants()
        {
            D.perksGranted ??= new List<string>();
            foreach (var shop in Catalog.Shops.Where(s => s.role == "furniture" || s.role == "colours"))
            {
                if (D.perksGranted.Contains(shop.id) || Affinity.Level(shop.id) < Affinity.MaxLevel) continue;
                string regionId = Regions.Get(shop.region).id;
                string item = shop.role == "furniture" ? "sig_" + regionId : "cos_boat_" + regionId;
                if (Catalog.Get(item) == null) continue;
                D.perksGranted.Add(shop.id);
                Inventory.Add(item, 1);
                ChatAudit.Write("PERK", $"{shop.keeperEnglish} (old friends) gives {item}");
                GameEvents.Banner($"A gift from {shop.keeperName}", $"Now that you're old friends, {shop.keeperEnglish} gives you {Catalog.Get(item).english} ({Catalog.Get(item).hanzi})." +
                                  (shop.role == "furniture" ? " Place it in your house here (I, then the bag)." : " Switch to it in the bag."), true);
                SaveSystem.Save();
            }
        }

        /// <summary>Morning perks, once per day (called every frame; cheap until the day changes).</summary>
        public static void Tick()
        {
            if (D.perkDay == D.day) return;
            D.perkDay = D.day;
            if (Has("tackle"))
            {
                var shop = Catalog.ShopFor("tackle", Regions.Current);
                var bait = ShopStock.Goods(shop).Where(i => i.category == ItemCategory.Bait && i.minAffinity <= Affinity.Level(shop.id))
                    .OrderByDescending(i => i.price).FirstOrDefault();
                if (bait != null)
                {
                    Inventory.Add(bait.id, bait.packSize);
                    GameEvents.Toast($"{shop.keeperName} left you a pack of {bait.english} ({bait.hanzi}) as a friend.", 4.5f);
                    ChatAudit.Write("PERK", $"morning bait from {shop.keeperEnglish}: {bait.packSize} x {bait.id}");
                }
            }
            if (Has("gifts"))
            {
                var shop = Catalog.ShopFor("gifts", Regions.Current);
                var gifts = ShopStock.Goods(shop).Where(i => i.category == ItemCategory.Gift).ToList();
                if (gifts.Count > 0)
                {
                    var g = gifts[(D.day * 7 + 3) % gifts.Count];
                    Inventory.Add(g.id, 1);
                    GameEvents.Toast($"{shop.keeperName} gave you {g.english} ({g.hanzi}) this morning.", 4.5f);
                    ChatAudit.Write("PERK", $"morning gift from {shop.keeperEnglish}: {g.id}");
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            Affinity.LevelChanged -= OnLevel;
            Affinity.LevelChanged += OnLevel;
        }

        private static void OnLevel(string shopId, int before, int now)
        {
            if (now >= Affinity.MaxLevel) CheckGrants();
        }
    }
}
