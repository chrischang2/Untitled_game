using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UntitledGame.Language
{
    /// <summary>
    /// The HSK 2.0 level 1-3 word list (595 words, StreamingAssets/hsk1-3.txt: word, level, pinyin, English gloss).
    /// Used to score how ambitious the player's Chinese is (friendship points), to keep the game's own vocabulary at
    /// about HSK 3, and as the curriculum for 高老师's lessons and tests.
    /// </summary>
    public static class HskVocab
    {
        public class Word
        {
            public string hanzi;
            public int level;
            public string pinyin;
            public string meaning;
        }

        private static Dictionary<string, Word> _words;
        private static List<Word> _ordered;
        private static int _maxLen = 1;

        public const string FileName = "hsk1-3.txt";

        private static void EnsureLoaded()
        {
            if (_words != null) return;
            _words = new Dictionary<string, Word>();
            _ordered = new List<Word>();
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var cols = line.Split('\t');
                    if (cols.Length < 2 || cols[0].Length == 0 || !int.TryParse(cols[1], out int lvl)) continue;
                    string w = cols[0];
                    if (_words.ContainsKey(w)) continue;
                    var word = new Word
                    {
                        hanzi = w,
                        level = lvl,
                        pinyin = cols.Length > 2 ? cols[2] : "",
                        meaning = cols.Length > 3 ? cols[3] : "",
                    };
                    _words[w] = word;
                    _ordered.Add(word);
                    _maxLen = Mathf.Max(_maxLen, w.Length);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HSK] Could not read {path}: {e.Message}");
            }
        }

        public static bool Ready
        {
            get { EnsureLoaded(); return _words.Count > 0; }
        }

        /// <summary>1-3, or 0 if the word isn't in HSK 1-3.</summary>
        public static int LevelOf(string word)
        {
            EnsureLoaded();
            return _words.TryGetValue(word, out var w) ? w.level : 0;
        }

        /// <summary>The word's entry (pinyin, meaning), or null.</summary>
        public static Word Get(string word)
        {
            EnsureLoaded();
            return _words.TryGetValue(word, out var w) ? w : null;
        }

        /// <summary>Every word of one HSK level, in list order.</summary>
        public static List<Word> Words(int level)
        {
            EnsureLoaded();
            return _ordered.Where(w => w.level == level).ToList();
        }

        public static IReadOnlyList<Word> All
        {
            get { EnsureLoaded(); return _ordered; }
        }

        /// <summary>HSK words in the text, longest match first (爸爸 not 爸+爸); characters outside the list are skipped.</summary>
        public static List<(string word, int level)> Segment(string text)
        {
            EnsureLoaded();
            var result = new List<(string, int)>();
            if (string.IsNullOrEmpty(text)) return result;
            int i = 0;
            while (i < text.Length)
            {
                if (!Pinyin.IsHanzi(text[i])) { i++; continue; }
                int matched = 0;
                for (int len = Mathf.Min(_maxLen, text.Length - i); len >= 1; len--)
                {
                    string w = text.Substring(i, len);
                    if (_words.TryGetValue(w, out var word))
                    {
                        result.Add((w, word.level));
                        matched = len;
                        break;
                    }
                }
                i += matched > 0 ? matched : 1;
            }
            return result;
        }
    }
}
