using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Language;

namespace UntitledGame.Progression
{
    /// <summary>
    /// Friendship with each shopkeeper (0-100 points, five levels). Points come from talking to them in Chinese:
    /// more ambitious lines (higher HSK words, longer sentences) are worth more, talking about their interests and
    /// answering their questions earn a bonus, and gifts they like are worth a lot. Higher levels unlock better
    /// goods and more personal facts. Talk points are capped per keeper per day so it can't be ground out.
    /// </summary>
    public static class Affinity
    {
        public static readonly int[] Thresholds = { 0, 15, 35, 60, 90 };
        public static readonly string[] LevelHanzi = { "陌生人", "认识", "朋友", "好朋友", "老朋友" };
        public static readonly string[] LevelEnglish = { "stranger", "acquaintance", "friend", "good friend", "old friend" };
        public const int MaxPoints = 100;
        public const int TalkCapPerDay = 15;
        public const int GiftLiked = 15, GiftNeutral = 5, GiftDisliked = -8;

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
            return s;
        }

        public static int Points(string shopId) => State(shopId).points;

        public static int Level(string shopId) => LevelFor(Points(shopId));

        public static int LevelFor(int points)
        {
            int lvl = 0;
            for (int i = 0; i < Thresholds.Length; i++) if (points >= Thresholds[i]) lvl = i;
            return lvl;
        }

        public static string LevelLabel(int level) => $"{LevelHanzi[level]} {Pinyin.Of(LevelHanzi[level])} ({LevelEnglish[level]})";

        /// <summary>Points needed for the next level, or 0 at the top.</summary>
        public static int NextThreshold(string shopId)
        {
            int lvl = Level(shopId);
            return lvl + 1 < Thresholds.Length ? Thresholds[lvl + 1] : 0;
        }

        /// <summary>Adds (or removes) points; returns the change in level.</summary>
        public static int Add(string shopId, int points, string reason)
        {
            var s = State(shopId);
            int before = LevelFor(s.points);
            s.points = Mathf.Clamp(s.points + points, 0, MaxPoints);
            int after = LevelFor(s.points);
            ChatAudit.Write("FRIENDSHIP", $"{shopId} {(points >= 0 ? "+" : "")}{points} ({reason}) -> {s.points} points, level {after} {LevelHanzi[after]}");
            Changed?.Invoke();
            if (after != before) LevelChanged?.Invoke(shopId, before, after);
            return after - before;
        }

        // ------------------------------------------------------------------ talking

        public class LineScore
        {
            public int points;
            public string why = "";
            public int hskWords;
            public int maxLevel;
        }

        /// <summary>
        /// Scores a line the player said to a keeper: 1-3 for the hardest HSK level used, +1 for 4+ different HSK words,
        /// +1 more for 7+, +1 for talking about the keeper's interests, + 2x the question's level when answering their question.
        /// Repeats and very short lines score nothing, and talk points are capped per day.
        /// </summary>
        public static LineScore ScoreLine(string shopId, string text, int questionLevel)
        {
            var result = new LineScore();
            var sb = new StringBuilder();
            foreach (char c in text) if (Pinyin.IsHanzi(c)) sb.Append(c);
            string clean = sb.ToString();
            var s = State(shopId);
            if (clean.Length < 2) { result.why = "too short"; return result; }
            if (s.recentLines.Contains(clean)) { result.why = "said that already"; return result; }
            var words = HskVocab.Segment(clean).GroupBy(w => w.word).Select(g => g.First()).ToList();
            result.hskWords = words.Count;
            if (words.Count == 0) { result.why = "no HSK 1-3 words recognised"; return result; }
            result.maxLevel = words.Max(w => w.level);

            int pts = result.maxLevel;
            var why = new List<string> { $"HSK {result.maxLevel}" };
            if (words.Count >= 4) { pts++; why.Add($"{words.Count} words"); }
            if (words.Count >= 7) pts++;
            var profile = KeeperProfiles.For(shopId);
            if (profile != null && (profile.topics.Any(clean.Contains) || profile.likes.Any(clean.Contains)))
            {
                pts++;
                why.Add("about their interests");
            }
            if (questionLevel > 0 && clean.Length >= 3)
            {
                pts += questionLevel * 2;
                why.Add($"answered their HSK {questionLevel} question");
            }

            // Daily cap per keeper.
            if (s.talkDay != Today)
            {
                s.talkDay = Today;
                s.talkPointsToday = 0;
            }
            int allowed = Mathf.Max(0, TalkCapPerDay - s.talkPointsToday);
            if (pts > allowed) why.Add(allowed == 0 ? "daily limit reached" : "near the daily limit");
            pts = Mathf.Min(pts, allowed);
            s.talkPointsToday += pts;
            s.recentLines.Add(clean);
            if (s.recentLines.Count > 30) s.recentLines.RemoveAt(0);

            result.points = pts;
            result.why = string.Join(", ", why);
            return result;
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
            return true;
        }

        // ------------------------------------------------------------------ gifts

        public static bool GaveGiftToday(string shopId) => State(shopId).lastGiftDay == Today;

        public static void MarkGift(string shopId) => State(shopId).lastGiftDay = Today;

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
