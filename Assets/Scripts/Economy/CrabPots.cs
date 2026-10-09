using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.Progression;

namespace UntitledGame.Economy
{
    /// <summary>
    /// 海叔's crab pots: passive income. Every new day the pots bring in crabs, which wait at his stall on the beach
    /// until you come down and collect the money (he pays out when you start talking to him). Uncollected crabs only
    /// keep so long (the cooler). Fish you give him go in the pots as bait and make the next day's haul bigger.
    ///
    /// Seven upgrades, 20 levels each in tiers of five (each tier opens with its HSK test, priced like Coach Wu's
    /// training). With every upgrade of a tier, a day's haul is worth about one biggest-size rarest fish of that tier
    /// (the crab value curve is solved from that target, see <see cref="CrabValue"/>).
    ///
    /// Daily haul = pots x pot size x how full they get x crab value x price bonus.
    /// </summary>
    public static class CrabPots
    {
        public static readonly string[] Stats = { "crab_pots", "crab_size", "crab_lure", "crab_deep", "crab_bait", "crab_cooler", "crab_helper" };
        public static bool IsCrabStat(string stat) => stat != null && stat.StartsWith("crab_");

        private static SaveData D => SaveSystem.Data;
        private static int L(string stat) => PlayerStats.Level(stat);
        private static int Today => DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : Mathf.Max(1, D.day);

        // ------------------------------------------------------------------ what each upgrade does

        public static int Pots(int level) => 1 + level;                                // 1 -> 21 pots
        public static int PotSize(int level) => 3 + level;                             // 3 -> 23 crabs a pot
        public static float Fill(int level) => 0.5f + 0.025f * level;                  // half full -> brim full
        public static float PriceBonus(int level) => 1f + 0.03f * level;               // +3% a level (the helper sells them)
        public static int KeepDays(int level) => 1 + level / 4;                        // the cooler: 1 -> 6 days
        public static float BaitRate(int level) => 1.2f + 0.04f * level;               // a fish's price x this as extra crabs
        public static float BaitCap(int level) => 0.3f + 0.05f * level;                // ...up to this share of a day's haul

        /// <summary>The target at tier t: one biggest-size fish of the tier's most valuable kind.</summary>
        public static float TierTarget(int tier)
        {
            var fish = FishDatabase.All.Where(f => f.IsFish && FishPower.TierOf(f) == tier).ToList();
            return fish.Count == 0 ? 72f * Mathf.Pow(10f, tier) : fish.Max(f => Catalog.FishPrice(f, f.maxWeight));
        }

        /// <summary>
        /// What one crab is worth at a deep-water level. Solved so that with every upgrade of tier t (all at level
        /// 5(t+1)) a day's haul equals <see cref="TierTarget"/>(t); in between it grows evenly (geometrically).
        /// Level 0 is a ¥1 shore crab.
        /// </summary>
        public static float CrabValue(int level)
        {
            level = Mathf.Clamp(level, 0, PlayerStats.MaxLevel);
            float Anchor(int k) // value at level 5k
            {
                if (k <= 0) return 1f;
                int lv = k * PlayerStats.LevelsPerTier;
                return TierTarget(k - 1) / (Pots(lv) * PotSize(lv) * Fill(lv) * PriceBonus(lv));
            }
            int tier = Mathf.Min(level / PlayerStats.LevelsPerTier, 3);
            float u = (level - tier * PlayerStats.LevelsPerTier) / (float)PlayerStats.LevelsPerTier;
            float a = Anchor(tier), b = Anchor(tier + 1);
            return a * Mathf.Pow(b / a, u);
        }

        public static float DailyAt(int pots, int size, int lure, int deep, int helper) =>
            Pots(pots) * PotSize(size) * Fill(lure) * CrabValue(deep) * PriceBonus(helper);

        /// <summary>A normal day's haul right now (before fish bait), in yuan.</summary>
        public static float Daily => DailyAt(L("crab_pots"), L("crab_size"), L("crab_lure"), L("crab_deep"), L("crab_helper")) *
                                     (Progression.Perks.Has("crabber") ? Progression.Perks.CrabBonus : 1f);
        public static int Crabs => Mathf.RoundToInt(Pots(L("crab_pots")) * PotSize(L("crab_size")) * Fill(L("crab_lure")));
        public static int KeepDaysNow => KeepDays(L("crab_cooler"));

        // ------------------------------------------------------------------ the days' hauls

        /// <summary>Adds up the hauls since the last check (each new day brings one; the cooler caps how many wait).</summary>
        public static void Accrue()
        {
            int today = Today;
            if (D.crabLastDay < 0) { D.crabLastDay = today; return; }
            if (today <= D.crabLastDay) return;
            float daily = Daily, cap = daily * KeepDaysNow;
            for (int day = D.crabLastDay + 1; day <= today; day++)
            {
                // Fish put in the pots on a day make the next morning's haul bigger (that extra always fits).
                float bait = D.crabBaitDay == day - 1 ? D.crabBaitBonus : 0f;
                D.crabPending = Mathf.Min(D.crabPending + daily + bait, cap + bait);
            }
            D.crabLastDay = today;
        }

        /// <summary>Money waiting at 海叔's stall.</summary>
        public static int Pending
        {
            get
            {
                Accrue();
                return Mathf.FloorToInt(D.crabPending);
            }
        }

        /// <summary>Pays out what's waiting (called when you start talking to 海叔). Returns the money.</summary>
        public static int Claim()
        {
            int money = Pending;
            if (money <= 0) return 0;
            D.crabPending -= money;
            Inventory.Earn(money);
            ChatAudit.Write("CRABS", $"collected ¥{money} ({Crabs} crabs a day at ¥{CrabValue(L("crab_deep")):0.##}, keeps {KeepDaysNow} days)");
            return money;
        }

        // ------------------------------------------------------------------ fish as bait

        /// <summary>Extra money tomorrow from fish already put in the pots today.</summary>
        public static float BaitToday => D.crabBaitDay == Today ? D.crabBaitBonus : 0f;
        public static float BaitCapNow => Daily * BaitCap(L("crab_bait"));

        /// <summary>What these fish would add to tomorrow's haul (capped), without using them.</summary>
        public static float BaitValue(IEnumerable<BucketFish> fish)
        {
            float add = fish.Sum(b => Inventory.FishValue(b)) * BaitRate(L("crab_bait"));
            return Mathf.Min(add, Mathf.Max(0f, BaitCapNow - BaitToday));
        }

        /// <summary>Puts the fish in the pots: they're used up, and tomorrow's haul grows. Returns the extra money.</summary>
        public static float AddBait(List<BucketFish> fish)
        {
            Accrue();
            float add = BaitValue(fish);
            if (D.crabBaitDay != Today) { D.crabBaitDay = Today; D.crabBaitBonus = 0f; }
            D.crabBaitBonus += add;
            Inventory.RemoveFish(fish);
            ChatAudit.Write("CRABS", $"baited the pots with {fish.Count} fish: +¥{add:0} tomorrow (today's bait ¥{D.crabBaitBonus:0} of ¥{BaitCapNow:0} max)");
            return add;
        }

        // ------------------------------------------------------------------ text

        public static string StatName(string stat) => stat switch
        {
            "crab_pots" => "Crab pots",
            "crab_size" => "Pot size",
            "crab_lure" => "Crab lures",
            "crab_deep" => "Deep-water ropes",
            "crab_bait" => "Fish-bait know-how",
            "crab_cooler" => "Cooler",
            "crab_helper" => "Market helper",
            _ => stat,
        };

        public static string StatValue(string stat)
        {
            int l = L(stat);
            return stat switch
            {
                "crab_pots" => $"{Pots(l)} pot{(Pots(l) == 1 ? "" : "s")}",
                "crab_size" => $"{PotSize(l)} crabs a pot",
                "crab_lure" => $"{Fill(l) * 100f:0}% full each day",
                "crab_deep" => $"crabs worth ¥{CrabValue(l):0.#}",
                "crab_bait" => $"fish pay x{BaitRate(l):0.00}, up to +{BaitCap(l) * 100f:0}% a day",
                "crab_cooler" => $"keeps {KeepDays(l)} day{(KeepDays(l) == 1 ? "" : "s")}",
                "crab_helper" => $"+{(PriceBonus(l) - 1f) * 100f:0}% price",
                _ => "",
            };
        }

        public static string Describe() =>
            $"{Crabs} crabs a day (¥{Daily:0}), keeps {KeepDaysNow} day{(KeepDaysNow == 1 ? "" : "s")}, ¥{Pending} waiting" +
            (BaitToday > 0 ? $", +¥{BaitToday:0} tomorrow from fish bait" : "");
    }
}
