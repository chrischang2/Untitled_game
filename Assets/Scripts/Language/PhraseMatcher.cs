using System;
using System.Collections.Generic;
using UntitledGame.Core;

namespace UntitledGame.Language
{
    /// <summary>
    /// Compares what speech recognition heard with phrases the player knows, by toneless syllables.
    /// Recognition often turns a learner's 喂汤圆 (wèi tāngyuán) into 未天员 (wèi tiānyuán): same sounds,
    /// wrong characters. These helpers let the game work out what was probably meant.
    /// </summary>
    public static class PhraseMatcher
    {
        private static readonly Dictionary<char, string> Cache = new Dictionary<char, string>();

        /// <summary>Toneless pinyin of one character ("喂" -> "wei"), or "" if unknown.</summary>
        public static string Syllable(char c)
        {
            if (!Pinyin.Ready || !Pinyin.IsHanzi(c)) return "";
            if (!Cache.TryGetValue(c, out var s)) Cache[c] = s = Pinyin.Plain(c.ToString());
            return s;
        }

        /// <summary>The syllables of the Chinese characters in a line (other characters are skipped).</summary>
        public static List<string> Syllables(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (char c in Pinyin.ToSimplified(text))
            {
                if (!Pinyin.IsHanzi(c)) continue;
                string s = Syllable(c);
                list.Add(s.Length > 0 ? s : c.ToString());
            }
            return list;
        }

        /// <summary>0..1: how alike two lines sound (edit distance over syllables).</summary>
        public static float Similarity(IList<string> a, IList<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0f;
            var d = new int[a.Count + 1, b.Count + 1];
            for (int i = 0; i <= a.Count; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Count; j++) d[0, j] = j;
            for (int i = 1; i <= a.Count; i++)
            for (int j = 1; j <= b.Count; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return 1f - d[a.Count, b.Count] / (float)Math.Max(a.Count, b.Count);
        }

        /// <summary>
        /// The notebook phrase the player was most likely trying to say, when what was heard is close to it but not
        /// the same characters. Null when nothing is close (or the line already contains the phrase exactly).
        /// </summary>
        public static VocabEntry ProbablyMeant(string heard, float minSimilarity = 0.6f)
        {
            string simplified = Pinyin.ToSimplified(heard ?? "");
            var said = Syllables(simplified);
            if (said.Count < 2) return null;
            VocabEntry best = null;
            float bestScore = minSimilarity;
            foreach (var v in SaveSystem.Data.vocab)
            {
                if (string.IsNullOrEmpty(v.hanzi) || v.hanzi.Length < 2 || simplified.Contains(v.hanzi)) continue;
                var target = Syllables(v.hanzi);
                if (Math.Abs(target.Count - said.Count) > 1) continue;
                float score = Similarity(said, target);
                if (score >= bestScore)
                {
                    best = v;
                    bestScore = score + 0.0001f;
                }
            }
            return best;
        }

        /// <summary>
        /// True for "feed Tangyuan / feed the cat", however it was heard: 喂 (wei) followed by 汤/圆/猫 sounds
        /// (喂汤圆, 喂猫, 未天员...).
        /// </summary>
        public static bool SoundsLikeFeedingTheCat(string heard)
        {
            var s = Syllables(heard);
            int wei = s.IndexOf("wei");
            if (wei < 0) return false;
            for (int i = wei + 1; i < s.Count && i <= wei + 3; i++)
                if (s[i] == "tang" || s[i] == "yuan" || s[i] == "mao") return true;
            return false;
        }
    }
}
