using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Language;

namespace UntitledGame.Progression
{
    /// <summary>
    /// Friendship with each shopkeeper: five levels (陌生人 → 老朋友). Every shopkeeper works the same way, so the player
    /// practises the same questions with each of them. To reach a level you must:
    /// <list type="bullet">
    /// <item>learn the things about them for that level by asking (level 1: where they're from; level 2: brothers and
    /// sisters, and their hobby; level 3: favourite food, and whether they're married / have children; level 4: their
    /// birthday, and their dream for the future),</item>
    /// <item>give them one gift at the level before (any gift they don't dislike), and</item>
    /// <item>have passed the HSK test one below it (level 2 needs HSK 1, level 3 HSK 2, level 4 HSK 3).</item>
    /// </list>
    /// Higher levels unlock better goods (ItemDef.minAffinity).
    /// </summary>
    public static class Affinity
    {
        public const int MaxLevel = 4;
        public static readonly string[] LevelHanzi = { "陌生人", "认识", "朋友", "好朋友", "老朋友" };
        public static readonly string[] LevelEnglish = { "stranger", "acquaintance", "friend", "good friend", "old friend" };

        /// <summary>What has to be learned about every shopkeeper to reach each level (index = level).</summary>
        public static readonly string[][] FactsFor =
        {
            new string[0],
            new[] { "hometown" },
            new[] { "siblings", "hobby" },
            new[] { "food", "family" },
            new[] { "birthday", "dream" },
        };

        /// <summary>The question that finds each thing out (the same for every shopkeeper), and what it's about in English.</summary>
        public static readonly Dictionary<string, (string question, string english)> FactQuestions = new Dictionary<string, (string, string)>
        {
            { "hometown", ("你是哪里人？", "where they're from") },
            { "siblings", ("你有哥哥姐姐吗？", "brothers and sisters") },
            { "hobby", ("你的爱好是什么？", "their hobby") },
            { "food", ("你喜欢吃什么？", "their favourite food") },
            { "family", ("你结婚了吗？", "married? children?") },
            { "birthday", ("你的生日是几月几号？", "their birthday") },
            { "dream", ("你以后想做什么？", "their dream for the future") },
        };

        /// <summary>The friendship level at which a shopkeeper will tell you this (one below the level it's needed for).</summary>
        public static int ShareLevel(string factId)
        {
            for (int l = 1; l <= MaxLevel; l++)
                if (FactsFor[l].Contains(factId)) return l - 1;
            return 0; // likes and dislikes: any time
        }

        /// <summary>
        /// The HSK test needed for a friendship level with a keeper (level 1: none). It never goes beyond the test you
        /// study for in their region (HSK region+1), so the closest friendship in each region needs that region's test.
        /// </summary>
        public static int HskNeeded(string shopId, int level)
        {
            int region = Economy.Catalog.Shop(shopId)?.region ?? 0;
            return Mathf.Clamp(Mathf.Min(level - 1, (region < 0 ? Hsk.MaxLevel : region + 1)), 0, Hsk.MaxLevel);
        }

        /// <summary>(shop id, old level, new level)</summary>
        public static event Action<string, int, int> LevelChanged;
        public static event Action Changed;

        private static int Today => SaveSystem.Data.day;

        public static KeeperState State(string shopId)
        {
            var list = SaveSystem.Data.keepers;
            var s = list.FirstOrDefault(k => k.shopId == shopId);
            if (s == null)
            {
                s = new KeeperState { shopId = shopId };
                list.Add(s);
            }
            s.giftLevels ??= new List<int>();
            s.facts ??= new List<string>();
            return s;
        }

        /// <summary>The friendship level now: every level up to it has its facts, its gift and its HSK test.</summary>
        public static int Level(string shopId)
        {
            int lvl = 0;
            for (int l = 1; l <= MaxLevel; l++)
            {
                if (!Requirements(shopId, l).All(r => r.done)) break;
                lvl = l;
            }
            return lvl;
        }

        public static string LevelLabel(int level) => $"{LevelHanzi[level]} {Pinyin.Of(LevelHanzi[level])} ({LevelEnglish[level]})";

        public class Requirement
        {
            public string kind;     // "fact", "gift" or "hsk"
            public string factId;
            public string text;     // English, with the Chinese question for facts
            public bool done;
        }

        /// <summary>What reaching <paramref name="level"/> takes, and which parts are done.</summary>
        public static List<Requirement> Requirements(string shopId, int level)
        {
            var list = new List<Requirement>();
            if (level < 1 || level > MaxLevel) return list;
            var s = State(shopId);
            foreach (var f in FactsFor[level])
            {
                var (q, en) = FactQuestions[f];
                list.Add(new Requirement { kind = "fact", factId = f, text = $"learn {en}: {q}", done = s.facts.Contains(f) });
            }
            list.Add(new Requirement { kind = "gift", text = "give a gift", done = s.giftLevels.Contains(level) });
            int hsk = HskNeeded(shopId, level);
            if (hsk > 0) list.Add(new Requirement { kind = "hsk", text = $"pass HSK {hsk}", done = Hsk.Level >= hsk });
            return list;
        }

        /// <summary>Checks for a level change (after learning something, a gift, or a passed test) and announces it.</summary>
        public static void Evaluate(string shopId)
        {
            var s = State(shopId);
            int before = s.level;
            int now = Level(shopId);
            if (now == before) return;
            s.level = now;
            ChatAudit.Write("FRIENDSHIP", $"{shopId}: level {before} -> {now} {LevelHanzi[now]}");
            Changed?.Invoke();
            LevelChanged?.Invoke(shopId, before, now);
        }

        public static void EvaluateAll()
        {
            foreach (var k in SaveSystem.Data.keepers.ToList()) Evaluate(k.shopId);
        }

        /// <summary>True when everything for the next level is done except the HSK test.</summary>
        public static bool WaitingForHsk(string shopId)
        {
            int next = Level(shopId) + 1;
            if (next > MaxLevel) return false;
            var req = Requirements(shopId, next);
            return req.Where(r => r.kind != "hsk").All(r => r.done) && req.Any(r => r.kind == "hsk" && !r.done);
        }

        // ------------------------------------------------------------------ facts

        public static bool Knows(string shopId, string factId) => State(shopId).facts.Contains(factId);

        /// <summary>Records a fact in the journal; returns true if it was new.</summary>
        public static bool Learn(string shopId, string factId, string source)
        {
            var s = State(shopId);
            if (s.facts.Contains(factId)) return false;
            s.facts.Add(factId);
            ChatAudit.Write("JOURNAL", $"learned {shopId}/{factId} ({source})");
            Changed?.Invoke();
            Evaluate(shopId);
            return true;
        }

        // ------------------------------------------------------------------ gifts

        public static bool GaveGiftToday(string shopId) => State(shopId).lastGiftDay == Today;

        /// <summary>
        /// A gift was given today. Unless they dislike it, it counts for the next friendship level (one gift per level).
        /// Returns true if it counted.
        /// </summary>
        public static bool RecordGift(string shopId, bool disliked)
        {
            var s = State(shopId);
            s.lastGiftDay = Today;
            int next = Level(shopId) + 1;
            bool counts = !disliked && next <= MaxLevel && !s.giftLevels.Contains(next);
            if (counts) s.giftLevels.Add(next);
            ChatAudit.Write("FRIENDSHIP", $"{shopId}: gift {(disliked ? "disliked" : "accepted")}{(counts ? $", counts for level {next}" : "")}");
            Changed?.Invoke();
            Evaluate(shopId);
            return counts;
        }

        /// <summary>Self-test / debug: learns everything and gives the gifts up to a level (HSK still caps it).</summary>
        public static void DebugGrant(string shopId, int level)
        {
            var s = State(shopId);
            for (int l = 1; l <= Mathf.Min(level, MaxLevel); l++)
            {
                foreach (var f in FactsFor[l]) if (!s.facts.Contains(f)) s.facts.Add(f);
                if (!s.giftLevels.Contains(l)) s.giftLevels.Add(l);
            }
            Changed?.Invoke();
            Evaluate(shopId);
        }

        // ------------------------------------------------------------------ questions

        /// <summary>A question this keeper hasn't asked for a while, at a difficulty matching the friendship.</summary>
        public static (string question, int hskLevel) PickQuestion(string shopId)
        {
            var p = KeeperProfiles.For(shopId);
            if (p == null) return (null, 0);
            int lvl = Level(shopId);
            int band = lvl <= 0 ? 0 : lvl <= 2 ? Mathf.Min(lvl, 1) + (UnityEngine.Random.value < 0.4f ? 1 : 0) : 2;
            band = Mathf.Clamp(band, 0, 2);
            var s = State(shopId);
            var options = p.questions[band].Where(q => !s.askedQuestions.Contains(q)).ToList();
            if (options.Count == 0)
            {
                s.askedQuestions.RemoveAll(q => p.questions[band].Contains(q));
                options = p.questions[band].ToList();
            }
            string chosen = options[UnityEngine.Random.Range(0, options.Count)];
            s.askedQuestions.Add(chosen);
            return (chosen, band + 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LevelChanged = null;
            Changed = null;
        }
    }
}
