using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Language;
using UntitledGame.Progression;

namespace UntitledGame.UI
{
    /// <summary>
    /// Interface text that turns Chinese as the player learns. A label shows only its Chinese once every word in it is
    /// mastered (top box; names don't count). Otherwise its tier (the HSK level of its Chinese) decides: at that level it
    /// shows both ("第三天 Day 3"), and once the player has passed the next test only the Chinese. Before then English.
    /// HSK 3 labels go straight to Chinese at HSK 3, the last test. Every interface word is in the HSK 1-3 list or the
    /// game's own words (taught at the end of HSK 2 and 3), so in the end the whole interface is Chinese.
    /// </summary>
    public static class UiText
    {
        private static readonly Dictionary<string, (bool mastered, float until)> Cache = new Dictionary<string, (bool, float)>();

        /// <summary>Every vocabulary word in the Chinese is mastered (checked at most once a second per label).</summary>
        public static bool AllMastered(string chinese)
        {
            if (string.IsNullOrEmpty(chinese)) return false;
            float now = Time.unscaledTime;
            if (Cache.TryGetValue(chinese, out var c) && now < c.until) return c.mastered;
            var words = HskVocab.Segment(Pinyin.ToSimplified(chinese));
            bool mastered = words.Count > 0 && words.All(w => Hsk.Mastered(w.word));
            if (Cache.Count > 500) Cache.Clear();
            Cache[chinese] = (mastered, now + 1f);
            return mastered;
        }

        /// <summary>The words of a label still to master before it turns Chinese.</summary>
        public static List<string> WordsToMaster(string chinese) =>
            HskVocab.Segment(Pinyin.ToSimplified(chinese ?? "")).Select(w => w.word).Where(w => !Hsk.Mastered(w)).Distinct().ToList();

        public static void ForgetCache() => Cache.Clear();

        private static bool ChineseOnly(int tier) => Hsk.Level > tier || (Hsk.Level >= Hsk.MaxLevel && tier >= Hsk.MaxLevel);

        /// <summary>English, both, or Chinese depending on the HSK level passed.</summary>
        public static string T(string english, string chinese, int tier)
        {
            if (ChineseOnly(tier) || AllMastered(chinese)) return chinese;
            if (Hsk.Level == tier && tier > 0) return $"{chinese} <size=75%><color=#8A7563>{english}</color></size>";
            return english;
        }

        /// <summary>Like <see cref="T"/> but without rich text (window titles, buttons).</summary>
        public static string Plain(string english, string chinese, int tier)
        {
            if (ChineseOnly(tier) || AllMastered(chinese)) return chinese;
            if (Hsk.Level == tier && tier > 0) return $"{chinese} {english}";
            return english;
        }

        /// <summary>The time with a Chinese part of day: 上午 8:30, 下午 3:00, 晚上 9:10.</summary>
        public static string ChineseClock(float hours)
        {
            int h = (int)hours % 24;
            int m = (int)((hours - (int)hours) * 60f);
            string part = h < 5 ? "晚上" : h < 9 ? "早上" : h < 12 ? "上午" : h < 13 ? "中午" : h < 18 ? "下午" : "晚上";
            int h12 = h % 12 == 0 ? 12 : h % 12;
            return $"{part} {h12}:{m:00}";
        }
    }
}
