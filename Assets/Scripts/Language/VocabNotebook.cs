using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;

namespace UntitledGame.Language
{
    /// <summary>
    /// Every word Mei teaches (written as 汉字 [meaning]) lands here, with dictionary pinyin.
    /// When the player later uses the word themselves, it's counted - that's the learning loop.
    /// Mei is the only source: nothing gets in unless she taught it.
    /// </summary>
    public static class VocabNotebook
    {
        public static event Action<VocabEntry> WordLearned;
        public static event Action<VocabEntry> WordUsed;

        private static readonly Regex Gloss = new Regex(
            // 汉字 [meaning] is what Mei is asked for; the small model sometimes writes 汉字 (meaning) instead, so take both.
            @"([一-鿿][一-鿿，、？！。…～ ]{0,11})\s*[\[【(（]([^\]】)）\n]{1,48})[\]】)）]", RegexOptions.Compiled);

        private static readonly Dictionary<string, float> LastUsedToast = new Dictionary<string, float>();

        public static IReadOnlyList<VocabEntry> Entries => SaveSystem.Data.vocab;

        public static string HanziOnly(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) if (Pinyin.IsHanzi(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Everyday phrases that aren't single HSK words (so the HSK list has no meaning for them).</summary>
        private static readonly Dictionary<string, string> Everyday = new Dictionary<string, string>
        {
            { "你好", "hello" }, { "您好", "hello (polite)" }, { "早上好", "good morning" }, { "晚上好", "good evening" },
            { "晚安", "good night" }, { "好的", "OK, sure" }, { "加油", "keep going, you can do it" }, { "欢迎光临", "welcome (to a shop)" },
            { "慢走", "take care (said to someone leaving)" },
        };

        /// <summary>
        /// Scans a line Mei said for taught words: 汉字 [meaning] glosses, and (when she forgets the gloss in an English
        /// line, e.g. "greet him first with 你好") Chinese words that are HSK words, with the HSK list's meaning.
        /// </summary>
        public static void ObserveTutorLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var found = new List<(string hanzi, string meaning)>();
            foreach (Match m in Gloss.Matches(text)) found.Add((HanziOnly(m.Groups[1].Value), m.Groups[2].Value.Trim()));
            if (Regex.IsMatch(text, "[A-Za-z]{3,}"))
            {
                string rest = Gloss.Replace(text, " ");
                foreach (Match run in Regex.Matches(rest, @"[㐀-鿿]+"))
                {
                    var whole = HskVocab.Get(run.Value);
                    if (whole != null) { found.Add((whole.hanzi, whole.meaning)); continue; }
                    if (Everyday.TryGetValue(run.Value, out var everyday)) { found.Add((run.Value, everyday)); continue; }
                    foreach (var (word, _) in HskVocab.Segment(run.Value))
                        if (word.Length >= 2 && HskVocab.Get(word) is { } w) found.Add((w.hanzi, w.meaning));
                }
            }
            foreach (var (hanzi, meaning) in found)
            {
                if (hanzi.Length == 0 || string.IsNullOrEmpty(meaning) || Pinyin.ContainsHanzi(meaning)) continue;
                var list = SaveSystem.Data.vocab;
                var entry = list.FirstOrDefault(v => v.hanzi == hanzi);
                if (entry == null)
                {
                    entry = new VocabEntry
                    {
                        hanzi = hanzi,
                        pinyin = Pinyin.Of(hanzi),
                        meaning = meaning,
                        heard = 1,
                        firstDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : 1,
                    };
                    list.Add(entry);
                    ChatAudit.Write("NOTEBOOK", $"new word: {entry.hanzi} {entry.pinyin} [{entry.meaning}]");
                    SaveSystem.Save();
                    WordLearned?.Invoke(entry);
                }
                else
                {
                    entry.heard++;
                    if (string.IsNullOrEmpty(entry.pinyin)) entry.pinyin = Pinyin.Of(hanzi);
                }
            }
        }

        /// <summary>Credits the player for using notebook words in what they said.</summary>
        public static void ObservePlayerLine(string text)
        {
            string said = HanziOnly(text ?? "");
            if (said.Length == 0) return;
            bool changed = false;
            foreach (var v in SaveSystem.Data.vocab)
            {
                if (v.hanzi.Length == 0 || !said.Contains(v.hanzi)) continue;
                v.said++;
                changed = true;
                ChatAudit.Write("NOTEBOOK", $"you used {v.hanzi} ({v.said}x so far)");
                if (!LastUsedToast.TryGetValue(v.hanzi, out float t) || Time.time - t > 45f)
                {
                    LastUsedToast[v.hanzi] = Time.time;
                    WordUsed?.Invoke(v);
                }
            }
            if (changed) SaveSystem.Save();
        }

        /// <summary>A few recent words, for Mei's prompt ("words the player has been learning").</summary>
        public static string RecentForPrompt(int max)
        {
            var list = SaveSystem.Data.vocab;
            return string.Join("、", list.Skip(Math.Max(0, list.Count - max)).Select(v => $"{v.hanzi} [{v.meaning}]{(v.said > 0 ? " (used)" : "")}"));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            WordLearned = null;
            WordUsed = null;
            LastUsedToast.Clear();
        }
    }
}
