using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Fishing;

namespace UntitledGame.Progression
{
    /// <summary>
    /// The player's fishing progression.
    /// - Stats (trained with 武教练): cast distance and calmer fish (strength), green-bar width, escape allowance,
    ///   bonus-fish chance, fish size (quality) and bite speed. 20 levels each in four tiers of five; tier t needs
    ///   the HSK t test. Training costs 10, 20, 40, 70, 100 in the first tier, ten times that in each tier after.
    ///   The three reeling stats are matched against each fish's power (FishPower).
    /// - Knowledge: books from 周老师. You can only catch fish you've read about; a new game knows just the sardine.
    /// (Equipment - baits and lines - lives in the Inventory.)
    /// </summary>
    public static class PlayerStats
    {
        public const int MaxLevel = 20, LevelsPerTier = 5;
        public static readonly int[] TierPrices = { 10, 20, 40, 70, 100 };
        public const string StarterFish = "sardine";
        /// <summary>Fish everyone knows from the start (catchable from the dock with the starter line).</summary>
        public static readonly string[] StarterFishes = { "sardine", "goby", "horsemackerel", "mullet" };

        public static readonly string[] Stats = { "cast", "bar", "grip", "luck", "quality", "bite" };

        public static event Action Changed;

        private static SaveData D => SaveSystem.Data;

        // ------------------------------------------------------------------ stats

        public static int Level(string stat) => Mathf.Clamp(stat switch
        {
            "cast" => D.statCast,
            "bar" => D.statBar,
            "grip" => D.statGrip,
            "luck" => D.statLuck,
            "quality" => D.statQuality,
            "bite" => D.statBite,
            _ => D.crabLevels?.Find(s => s.id == stat)?.level ?? 0, // 海叔's crab-pot upgrades
        }, 0, MaxLevel);

        public static void SetLevel(string stat, int value)
        {
            value = Mathf.Clamp(value, 0, MaxLevel);
            switch (stat)
            {
                case "cast": D.statCast = value; break;
                case "bar": D.statBar = value; break;
                case "grip": D.statGrip = value; break;
                case "luck": D.statLuck = value; break;
                case "quality": D.statQuality = value; break;
                case "bite": D.statBite = value; break;
                default:
                    if (!CrabPots.IsCrabStat(stat)) break;
                    D.crabLevels ??= new List<StatLevel>();
                    var entry = D.crabLevels.Find(s => s.id == stat);
                    if (entry == null) D.crabLevels.Add(entry = new StatLevel { id = stat });
                    entry.level = value;
                    break;
            }
        }

        /// <summary>One level up; returns false at the maximum.</summary>
        public static bool Upgrade(string stat)
        {
            int before = Level(stat);
            if (before >= MaxLevel) return false;
            SetLevel(stat, before + 1);
            ChatAudit.Write("STATS", $"{stat} {before} -> {before + 1}");
            Changed?.Invoke();
            return true;
        }

        public static string StatName(string stat) => stat switch
        {
            "cast" => "Cast distance",
            "bar" => "Green bar",
            "grip" => "Escape allowance",
            "luck" => "Bonus fish",
            "quality" => "Fish size",
            "bite" => "Bite speed",
            _ => CrabPots.StatName(stat),
        };

        /// <summary>The training tier a level belongs to (levels 1-5 are tier 0, 6-10 tier 1...).</summary>
        public static int TierOfLevel(int level) => Mathf.Max(0, (level - 1) / LevelsPerTier);
        /// <summary>The HSK test needed before 武教练 trains this stat to its next level.</summary>
        public static int HskNeededForNext(string stat) => TierOfLevel(Level(stat) + 1);
        public static bool CanTrainNext(string stat) => Level(stat) < MaxLevel && Hsk.Level >= HskNeededForNext(stat);
        /// <summary>The next level's price: 10, 20, 40, 70, 100 in tier 0, ten times as much each tier after.</summary>
        public static int NextPrice(string stat)
        {
            int next = Mathf.Min(Level(stat) + 1, MaxLevel);
            int tier = TierOfLevel(next);
            return TierPrices[(next - 1) % LevelsPerTier] * (int)Mathf.Pow(10, tier);
        }

        /// <summary>Everyone casts up to 10 m (strength training calms hooked fish instead, see FishPower).</summary>
        public const float CastDistance = 10f;
        public static float BonusFishChance => 0.025f * Level("luck");
        public static int QualityLevel => Level("quality");
        public static float BiteTimeMultiplier => 1f - 0.03f * Level("bite");
        /// <summary>Your reeling power from a stat's training (matched against each fish's power).</summary>
        public static float Power(string stat) => FishPower.StatPower(Level(stat));

        public static string StatValue(string stat) => stat switch
        {
            "cast" => $"power {Power("cast"):0.#}",
            "bar" => $"power {Power("bar"):0.#}",
            "grip" => $"power {Power("grip"):0.#}",
            "luck" => $"{BonusFishChance * 100f:0}% bonus fish",
            "quality" => QualityLevel == 0 ? "normal" : $"+{QualityLevel} bigger",
            "bite" => $"{Level("bite") * 3}% faster",
            _ => CrabPots.StatValue(stat),
        };

        // ------------------------------------------------------------------ knowledge

        public static IReadOnlyList<string> BooksRead => D.booksRead;
        public static int Knowledge => D.booksRead.Count;
        public static bool HasRead(string bookId) => D.booksRead.Contains(bookId);

        /// <summary>Junk is always "known"; a fish only once you've read about it (the sardine from the start).</summary>
        public static bool IsDiscovered(FishSpecies f) => f != null && (!f.IsFish || D.discoveredFish.Contains(f.id));

        public static int DiscoveredFishCount => FishDatabase.All.Count(f => f.IsFish && IsDiscovered(f));
        public static int TotalFishCount => FishDatabase.All.Count(f => f.IsFish);

        public static IEnumerable<FishSpecies> Discovered => FishDatabase.All.Where(f => f.IsFish && IsDiscovered(f));

        /// <summary>Reads a book you own: discovers its fish. Returns the newly discovered species.</summary>
        public static List<FishSpecies> ReadBook(string bookId)
        {
            var book = Catalog.Get(bookId);
            var found = new List<FishSpecies>();
            if (book == null || book.category != ItemCategory.Book || !Inventory.Owns(bookId)) return found;
            if (!D.booksRead.Contains(bookId)) D.booksRead.Add(bookId);
            foreach (var id in book.teachesFish ?? new string[0])
            {
                var f = FishDatabase.Get(id);
                if (f == null || D.discoveredFish.Contains(id)) continue;
                D.discoveredFish.Add(id);
                found.Add(f);
            }
            ChatAudit.Write("STATS", $"read {book.english}: discovered {(found.Count == 0 ? "nothing new" : string.Join(", ", found.Select(f => f.name)))}");
            Changed?.Invoke();
            return found;
        }

        /// <summary>
        /// Save upgrade. The lake became a sea: old fish, books and the strength stat no longer exist. Unknown ids are
        /// dropped, the starter fish is always known, and old strength training carries over as cast-distance levels.
        /// </summary>
        public static void Normalise(SaveData d)
        {
            d.booksRead ??= new List<string>();
            d.booksRead.RemoveAll(id => Catalog.Get(id) == null);
            d.discoveredFish ??= new List<string>();
            d.discoveredFish.RemoveAll(id => FishDatabase.Get(id) == null);
            for (int i = StarterFishes.Length - 1; i >= 0; i--)
                if (!d.discoveredFish.Contains(StarterFishes[i])) d.discoveredFish.Insert(0, StarterFishes[i]);
            foreach (var book in d.booksRead)
                foreach (var id in Catalog.Get(book)?.teachesFish ?? new string[0])
                    if (!d.discoveredFish.Contains(id)) d.discoveredFish.Add(id);
            if (d.strength > 1 && d.statCast == 0) d.statCast = Mathf.Clamp(d.strength - 1, 0, MaxLevel);
            d.strength = 1;
        }

        public static string Describe() =>
            $"Fishing knowledge: {Knowledge} book{(Knowledge == 1 ? "" : "s")} ({DiscoveredFishCount}/{TotalFishCount} fish known)  ·  " +
            string.Join("  ·  ", Stats.Select(s => $"{StatName(s)} {Level(s)}/{MaxLevel}"));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Changed = null;
    }
}
