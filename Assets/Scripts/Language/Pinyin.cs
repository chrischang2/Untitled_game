using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UntitledGame.Language
{
    /// <summary>
    /// Deterministic pinyin from Simplified Chinese using CC-CEDICT (word-level, longest match first so
    /// multi-reading characters like 长/行/了 read correctly in context). The LLM never writes pinyin:
    /// small models get tones wrong, and a tutor must not teach wrong tones.
    /// Dictionary: StreamingAssets/cedict_ts.u8 (downloaded by Tools/fetch-assets.ps1, CC BY-SA 4.0).
    /// </summary>
    public static class Pinyin
    {
        private static Dictionary<string, string> _words;       // simplified word -> "yu2 gan1"
        private static Dictionary<char, char> _tradToSimp;
        private static Dictionary<char, HashSet<string>> _allReadings; // every toneless reading of each character
        private static int _maxWord = 1;
        private static volatile bool _ready;
        private static bool _started;

        public static bool Ready => _ready;

        // Most common readings for characters that CC-CEDICT lists several ways.
        private static readonly Dictionary<char, string> SingleOverrides = new Dictionary<char, string>
        {
            { '了', "le5" }, { '的', "de5" }, { '得', "de5" }, { '着', "zhe5" }, { '么', "me5" }, { '吗', "ma5" },
            { '呢', "ne5" }, { '吧', "ba5" }, { '啊', "a5" }, { '个', "ge4" }, { '们', "men5" }, { '还', "hai2" },
            { '都', "dou1" }, { '长', "chang2" }, { '行', "xing2" }, { '好', "hao3" }, { '要', "yao4" }, { '会', "hui4" },
            { '没', "mei2" }, { '为', "wei4" }, { '大', "da4" }, { '一', "yi1" }, { '不', "bu4" }, { '和', "he2" },
            { '只', "zhi3" }, { '种', "zhong3" }, { '重', "zhong4" }, { '便', "bian4" }, { '乐', "le4" }, { '教', "jiao1" },
            { '少', "shao3" }, { '看', "kan4" }, { '分', "fen1" }, { '干', "gan4" }, { '哪', "na3" }, { '那', "na4" },
            { '地', "di4" }, { '过', "guo4" }, { '条', "tiao2" }, { '钓', "diao4" }, { '卖', "mai4" }, { '买', "mai3" },
            { '块', "kuai4" }, { '给', "gei3" }, { '差', "cha4" }, { '空', "kong1" }, { '数', "shu4" }, { '觉', "jue2" },
            { '朝', "chao2" }, { '将', "jiang1" }, { '当', "dang1" }, { '发', "fa1" }, { '啦', "la5" },
            { '哦', "o4" }, { '嗯', "ng4" }, { '呀', "ya5" }, { '嘛', "ma5" }, { '喂', "wei4" },
        };

        /// <summary>Starts loading the dictionary on a background thread (safe to call repeatedly).</summary>
        public static void Init()
        {
            if (_started) return;
            _started = true;
            string path = Path.Combine(Application.streamingAssetsPath, "cedict_ts.u8");
            new Thread(() => Load(path)) { IsBackground = true, Name = "Pinyin" }.Start();
        }

        private static void Load(string path)
        {
            var words = new Dictionary<string, string>(130000);
            var t2s = new Dictionary<char, char>(8000);
            var usedAsSimplified = new HashSet<char>();
            // How often each character is read each way inside multi-character words.
            var readings = new Dictionary<char, Dictionary<string, int>>(12000);
            var all = new Dictionary<char, HashSet<string>>(12000);
            void AddReading(char c, string numbered)
            {
                string plain = Toneless(numbered);
                if (plain.Length == 0) return;
                if (!all.TryGetValue(c, out var set)) all[c] = set = new HashSet<string>();
                set.Add(plain);
            }
            int max = 1;
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[Pinyin] Dictionary missing at {path} (run Tools/fetch-assets.ps1).");
                    return;
                }
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    int sp1 = line.IndexOf(' ');
                    int sp2 = line.IndexOf(' ', sp1 + 1);
                    int lb = line.IndexOf('[', sp2);
                    int rb = line.IndexOf(']', lb + 1);
                    if (sp1 < 0 || sp2 < 0 || lb < 0 || rb < 0) continue;
                    string trad = line.Substring(0, sp1);
                    string simp = line.Substring(sp1 + 1, sp2 - sp1 - 1);
                    string py = line.Substring(lb + 1, rb - lb - 1);
                    bool proper = py.Length > 0 && char.IsUpper(py[0]);
                    if (!words.TryGetValue(simp, out var existing) || (IsProper(existing) && !proper))
                        words[simp] = py;
                    if (simp.Length > max) max = Math.Min(simp.Length, 8);
                    foreach (char c in simp) usedAsSimplified.Add(c);
                    if (simp.Length == 1 && !proper) AddReading(simp[0], py);
                    if (simp.Length > 1 && !proper)
                    {
                        string[] syl = py.Split(' ');
                        if (syl.Length == simp.Length)
                        {
                            for (int i = 0; i < simp.Length; i++)
                            {
                                if (!readings.TryGetValue(simp[i], out var counts)) readings[simp[i]] = counts = new Dictionary<string, int>();
                                string r = syl[i].ToLowerInvariant();
                                counts[r] = counts.TryGetValue(r, out int n) ? n + 1 : 1;
                                AddReading(simp[i], r);
                            }
                        }
                    }
                    if (trad.Length == simp.Length)
                    {
                        for (int i = 0; i < trad.Length; i++)
                            if (trad[i] != simp[i] && !t2s.ContainsKey(trad[i])) t2s[trad[i]] = simp[i];
                    }
                }
                // A character that is itself written in simplified text is never converted: CC-CEDICT lists 么 as
                // the traditional form of 幺 ("youngest"), which would otherwise turn every 什么 into 什幺.
                foreach (char c in usedAsSimplified) t2s.Remove(c);
                // A lone character keeps its most common reading, not the first one listed: CC-CEDICT lists 汤 as
                // Tang1 (surname), shang1 (rare) and tang1 (soup), and 上 / 台 / 与 / 那 had the same problem.
                foreach (var kv in readings)
                {
                    string key = kv.Key.ToString();
                    if (!words.TryGetValue(key, out var current)) continue;
                    string best = null;
                    int bestN = 0;
                    foreach (var r in kv.Value)
                        if (r.Value > bestN) { best = r.Key; bestN = r.Value; }
                    kv.Value.TryGetValue(current.ToLowerInvariant(), out int currentN);
                    if (best != null && bestN >= 3 && currentN < bestN) words[key] = best;
                }
                _words = words;
                _tradToSimp = t2s;
                _allReadings = all;
                _maxWord = max;
                _ready = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Pinyin] Failed to load dictionary: " + e.Message);
            }
        }

        private static bool IsProper(string py) => py.Length > 0 && char.IsUpper(py[0]);

        public static bool IsHanzi(char c) => c >= '一' && c <= '鿿';

        public static bool ContainsHanzi(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (IsHanzi(c)) return true;
            return false;
        }

        /// <summary>Converts any Traditional characters to Simplified.</summary>
        public static string ToSimplified(string text)
        {
            if (!_ready || string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text) sb.Append(_tradToSimp.TryGetValue(c, out char s) ? s : c);
            return sb.ToString();
        }

        /// <summary>Pinyin for a word or phrase, e.g. 鱼竿 -> "yúgān".</summary>
        public static string Of(string hanzi) => Annotate(hanzi).Trim();

        /// <summary>
        /// Pinyin line for mixed text: words joined per Hanyu Pinyin orthography, spaces between words,
        /// punctuation normalised, non-Chinese text passed through. Returns "" if the text has no hanzi.
        /// </summary>
        public static string Annotate(string text)
        {
            if (!ContainsHanzi(text)) return "";
            if (!_ready) return "";
            text = ToSimplified(text);
            var sb = new StringBuilder(text.Length * 3);
            int i = 0;
            bool lastWasWord = false;
            while (i < text.Length)
            {
                char c = text[i];
                if (!IsHanzi(c))
                {
                    string p = Punct(c);
                    if (p == " " && sb.Length > 0 && sb[sb.Length - 1] == ' ') { i++; continue; }
                    sb.Append(p);
                    lastWasWord = false;
                    i++;
                    continue;
                }
                int len = Math.Min(_maxWord, text.Length - i);
                string match = null, py = null;
                for (; len >= 1; len--)
                {
                    string w = text.Substring(i, len);
                    if (!AllHanzi(w)) continue;
                    if (len == 1 && SingleOverrides.TryGetValue(c, out string ov)) { match = w; py = ov; break; }
                    if (_words.TryGetValue(w, out py)) { match = w; break; }
                }
                if (match == null) { match = c.ToString(); py = ""; }
                if (lastWasWord) sb.Append(' ');
                sb.Append(ToneMarks(py, joined: true));
                lastWasWord = true;
                i += match.Length;
            }
            return sb.ToString().Trim();
        }

        private static bool AllHanzi(string w)
        {
            foreach (char c in w) if (!IsHanzi(c)) return false;
            return true;
        }

        private static string Punct(char c) => c switch
        {
            '，' => ", ", '。' => ". ", '！' => "! ", '？' => "? ", '、' => ", ", '：' => ": ", '；' => "; ",
            '（' => " (", '）' => ") ", '“' => " \"", '”' => "\" ", '…' => "...", '～' => "~",
            _ => c.ToString(),
        };

        /// <summary>"yu2 gan1" -> "yúgān" (joined) or "yú gān".</summary>
        public static string ToneMarks(string numbered, bool joined)
        {
            if (string.IsNullOrEmpty(numbered)) return "";
            var parts = numbered.Split(' ');
            var sb = new StringBuilder();
            for (int k = 0; k < parts.Length; k++)
            {
                string syl = Syllable(parts[k].ToLowerInvariant());
                if (k > 0)
                {
                    // Apostrophe before a/e/o-initial syllables inside a word (xī'ān).
                    if (joined && syl.Length > 0 && "aeoāáǎàēéěèōóǒò".IndexOf(syl[0]) >= 0) sb.Append('\'');
                    else if (!joined) sb.Append(' ');
                }
                sb.Append(syl);
            }
            return sb.ToString();
        }

        private static readonly string[] Marks = { "āēīōūǖ", "áéíóúǘ", "ǎěǐǒǔǚ", "àèìòùǜ" };

        private static string Syllable(string s)
        {
            if (s.Length == 0) return s;
            char last = s[s.Length - 1];
            int tone = char.IsDigit(last) ? last - '0' : 5;
            string body = char.IsDigit(last) ? s.Substring(0, s.Length - 1) : s;
            body = body.Replace("u:", "ü").Replace("v", "ü");
            if (tone < 1 || tone > 4) return body;
            int idx = body.IndexOf('a');
            if (idx < 0) idx = body.IndexOf('e');
            if (idx < 0 && body.Contains("ou")) idx = body.IndexOf('o');
            if (idx < 0)
            {
                for (int j = body.Length - 1; j >= 0; j--)
                {
                    if ("iouü".IndexOf(body[j]) >= 0) { idx = j; break; }
                }
            }
            if (idx < 0) return body;
            int vowel = "aeiouü".IndexOf(body[idx]);
            char marked = Marks[tone - 1][vowel];
            return body.Substring(0, idx) + marked + body.Substring(idx + 1);
        }

        /// <summary>"lu:4" / "Lv3" -> "lv"; "hao3" -> "hao".</summary>
        private static string Toneless(string numbered)
        {
            var sb = new StringBuilder();
            foreach (char c in numbered.ToLowerInvariant().Replace("u:", "v"))
                if (c >= 'a' && c <= 'z') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// Every toneless reading CC-CEDICT gives a character (还 -> hai, huan). Includes the common one even if the
        /// dictionary isn't loaded yet. Used to grade spoken answers without caring about tones.
        /// </summary>
        public static IReadOnlyCollection<string> PlainReadings(char c)
        {
            if (_ready && _allReadings != null && _allReadings.TryGetValue(c, out var set))
            {
                string main = Plain(c.ToString());
                if (main.Length > 0 && !set.Contains(main)) set.Add(main);
                return set;
            }
            string only = _ready ? Plain(c.ToString()) : "";
            return only.Length > 0 ? new[] { only } : Array.Empty<string>();
        }

        /// <summary>Toneless, space-free pinyin for fuzzy matching ("鱼竿" -> "yugan").</summary>
        public static string Plain(string text)
        {
            string p = Annotate(text);
            var sb = new StringBuilder();
            foreach (char c in p.Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetter(c) && c < 128) sb.Append(char.ToLowerInvariant(c));
                else if (c == 'ü') sb.Append('v');
            }
            return sb.ToString();
        }
    }
}
