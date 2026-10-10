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
    /// 海叔's crab pots: passive income. He hauls the pots every 15 minutes of in-game time that you actually play (sleeping
    /// and bus trips skip the clock, so they don't count), and the crabs wait at his stall on the beach until you come down
    /// and collect the money (he pays out when you start talking to him). Nothing is lost however long you take. Fish you
    /// give him go in the pots as bait and make the next haul bigger.
    ///
    /// Three upgrades, 20 levels each in tiers of five (each level opens with lessons, like Coach Wu's training, and is
    /// priced the same): more pots, bigger pots and longer lines. With all three at the end of a tier, a day's haul is
    /// worth about one biggest-size most valuable fish of that tier (the crab value curve is solved from that target,
    /// see <see cref="CrabValue"/>).
    ///
    /// A day's hauls (a full waking day of play, 6am-2am = 80 hauls) = pots x pot size x how full they get (three quarters)
    /// x crab value; each haul brings one eightieth of that.
    /// </summary>
    public static class CrabPots
    {
        public static readonly string[] Stats = { "crab_pots", "crab_size", "crab_deep" };
        public static bool IsCrabStat(string stat) => stat != null && stat.StartsWith("crab_");

        private static SaveData D => SaveSystem.Data;
        private static int L(string stat) => PlayerStats.Level(stat);
        private static int Today => DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : Mathf.Max(1, D.day);

        // ------------------------------------------------------------------ what each upgrade does

        public static int Pots(int level) => 1 + level;                                // 1 -> 21 pots
        public static int PotSize(int level) => 3 + level;                             // 3 -> 23 crabs a pot
        public const float Fill = 0.75f;                                               // the pots are three quarters full
        public const float BaitRate = 1.5f;                                            // a fish's price x this as extra crabs
        public const float BaitCap = 0.5f;                                             // ...up to this share of a day's haul

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
                return TierTarget(k - 1) / (Pots(lv) * PotSize(lv) * Fill);
            }
            int tier = Mathf.Min(level / PlayerStats.LevelsPerTier, 3);
            float u = (level - tier * PlayerStats.LevelsPerTier) / (float)PlayerStats.LevelsPerTier;
            float a = Anchor(tier), b = Anchor(tier + 1);
            return a * Mathf.Pow(b / a, u);
        }

        public static float DailyAt(int pots, int size, int deep) => Pots(pots) * PotSize(size) * Fill * CrabValue(deep);

        /// <summary>Minutes of in-game time between hauls, and how many hauls a full waking day (6am-2am) holds.</summary>
        public const float HaulMinutes = 15f;
        public const float WakingHours = 20f;
        public static int HaulsPerDay => Mathf.RoundToInt(WakingHours * 60f / HaulMinutes);

        /// <summary>One haul's money right now (before bait).</summary>
        public static float PerHaul => Daily / HaulsPerDay;

        /// <summary>In-game minutes until the next haul.</summary>
        public static float MinutesToNextHaul
        {
            get
            {
                Accrue();
                return D.crabLastMinute < 0 ? HaulMinutes : Mathf.Max(0f, HaulMinutes - (float)(D.playMinutes - D.crabLastMinute));
            }
        }

        /// <summary>A full day's hauls right now (before fish bait), in yuan.</summary>
        public static float Daily => DailyAt(L("crab_pots"), L("crab_size"), L("crab_deep")) *
                                     (Progression.Perks.Has("crabber") ? Progression.Perks.CrabBonus : 1f);
        public static int Crabs => Mathf.RoundToInt(Pots(L("crab_pots")) * PotSize(L("crab_size")) * Fill);

        // ------------------------------------------------------------------ the days' hauls

        /// <summary>Adds up the hauls since the last check: one every 15 minutes of in-game time played. Nothing is ever lost.</summary>
        public static void Accrue()
        {
            double now = D.playMinutes;
            if (D.crabLastMinute < 0 || D.crabLastMinute > now) { D.crabLastMinute = now; return; }
            int hauls = (int)((now - D.crabLastMinute) / HaulMinutes);
            if (hauls <= 0) return;
            // Fish put in the pots go into the next haul.
            D.crabPending += hauls * PerHaul + D.crabBaitWaiting;
            D.crabBaitWaiting = 0f;
            D.crabLastMinute += hauls * HaulMinutes;
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
            ChatAudit.Write("CRABS", $"collected ¥{money} ({Crabs} crabs a day at ¥{CrabValue(L("crab_deep")):0.##}, a haul every {HaulMinutes:0} in-game minutes)");
            return money;
        }

        // ------------------------------------------------------------------ fish as bait

        /// <summary>Bait money from fish put in the pots today (the daily limit counts it), and what still waits for the next haul.</summary>
        public static float BaitToday => D.crabBaitDay == Today ? D.crabBaitBonus : 0f;
        public static float BaitWaiting => D.crabBaitWaiting;
        public static float BaitCapNow => Daily * BaitCap;

        /// <summary>What these fish would add to tomorrow's haul (capped), without using them.</summary>
        public static float BaitValue(IEnumerable<BucketFish> fish)
        {
            float add = fish.Sum(b => Inventory.FishValue(b)) * BaitRate;
            return Mathf.Min(add, Mathf.Max(0f, BaitCapNow - BaitToday));
        }

        /// <summary>Puts the fish in the pots: they're used up, and the next haul grows. Returns the extra money.</summary>
        public static float AddBait(List<BucketFish> fish)
        {
            Accrue();
            float add = BaitValue(fish);
            if (D.crabBaitDay != Today) { D.crabBaitDay = Today; D.crabBaitBonus = 0f; }
            D.crabBaitBonus += add;
            D.crabBaitWaiting += add;
            Inventory.RemoveFish(fish);
            ChatAudit.Write("CRABS", $"baited the pots with {fish.Count} fish: +¥{add:0} in the next haul (today's bait ¥{D.crabBaitBonus:0} of ¥{BaitCapNow:0} max)");
            return add;
        }

        // ------------------------------------------------------------------ text

        public static string StatName(string stat) => stat switch
        {
            "crab_pots" => "More pots",
            "crab_size" => "Bigger pots",
            "crab_deep" => "Longer lines",
            _ => stat,
        };

        public static string StatValue(string stat)
        {
            int l = L(stat);
            return stat switch
            {
                "crab_pots" => $"{Pots(l)} pot{(Pots(l) == 1 ? "" : "s")}",
                "crab_size" => $"{PotSize(l)} crabs a pot",
                "crab_deep" => $"crabs worth ¥{CrabValue(l):0.#}",
                _ => "",
            };
        }

        public static string Describe() =>
            $"A haul every {HaulMinutes:0} minutes (¥{PerHaul:0.#} each, ¥{Daily:0} over a full day), ¥{Pending} waiting" +
            (BaitWaiting > 0 ? $", +¥{BaitWaiting:0} in the next haul from fish bait" : "");
    }
}
