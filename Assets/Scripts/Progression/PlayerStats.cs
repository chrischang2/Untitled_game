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
    /// - Stats (trained with 武教练, five levels each): cast distance, green-bar width, escape allowance, bonus-fish
    ///   chance, fish size (quality) and bite speed.
    /// - Knowledge: books from 周老师. You can only catch fish you've read about; a new game knows just the sardine.
    /// (Equipment - baits and lines - lives in the Inventory.)
    /// </summary>
    public static class PlayerStats
    {
        public const int MaxLevel = 5;
        public const string StarterFish = "sardine";

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
            _ => 0,
        }, 0, MaxLevel);

        private static void SetLevel(string stat, int value)
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
            _ => stat,
        };

        /// <summary>Highest stat level 武教练 will train you to at a friendship level (friends get more).</summary>
        public static int TrainingCap(int friendship) => Mathf.Clamp(friendship + 1, 1, MaxLevel);

        public static float CastDistance => 7f + 2.5f * Level("cast");
        public static float BarMultiplier => 1f + 0.12f * Level("bar");
        public static float DrainMultiplier => 1f - 0.1f * Level("grip");
        public static float StartProgress => 0.3f + 0.03f * Level("grip");
        public static float BonusFishChance => 0.06f * Level("luck");
        public static int QualityLevel => Level("quality");
        public static float BiteTimeMultiplier => 1f - 0.1f * Level("bite");

        public static string StatValue(string stat) => stat switch
        {
            "cast" => $"{CastDistance:0.#} m",
            "bar" => $"+{Level("bar") * 12}%",
            "grip" => $"-{Level("grip") * 10}% escape speed",
            "luck" => $"{BonusFishChance * 100f:0}% bonus fish",
            "quality" => QualityLevel == 0 ? "normal" : $"+{QualityLevel} bigger",
            "bite" => $"{Level("bite") * 10}% faster",
            _ => "",
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
            if (!d.discoveredFish.Contains(StarterFish)) d.discoveredFish.Insert(0, StarterFish);
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
