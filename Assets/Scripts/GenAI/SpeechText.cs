using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace UntitledGame.GenAI
{
    /// <summary>Text helpers for turning streamed LLM output into speakable sentences.</summary>
    public static class SpeechText
    {
        private static readonly Regex StageDirections = new Regex(@"\*[^*]{1,60}\*", RegexOptions.Compiled);
        // Square brackets are kept: tutors gloss words as 汉字 [meaning].
        private static readonly Regex Markdown = new Regex(@"[*_#`~>]", RegexOptions.Compiled);
        // Model-written pinyin (often wrong; the game adds its own from CC-CEDICT): runs of tone-marked syllables.
        private const string Toned = "āáǎàēéěèīíǐìōóǒòūúǔùǖǘǚǜ";
        private static readonly Regex ModelPinyin = new Regex(
            @"\s*[\(（]?(?:[A-Za-zü]*[" + Toned + @"][A-Za-zü" + Toned + @"]*[\s,，']*)+[\)）]?", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex Pinyin = new Regex(@"\s*[\(（][^\)）]{1,60}[\)）]", RegexOptions.Compiled);
        private static readonly Regex GameNotes = new Regex(@"\[(game|context)[^\]]*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Strip things that shouldn't be shown or spoken (markdown, emoji, stage directions).</summary>
        public static string CleanForDisplay(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = GameNotes.Replace(text, "");
            text = StageDirections.Replace(text, "");
            text = Markdown.Replace(text, "");
            text = ModelPinyin.Replace(text, " ");
            text = text.Replace("[]", "").Replace("()", "");
            text = RemoveEmoji(text);
            text = LeakedPrefix.Replace(text, "");
            return Spaces.Replace(text, " ").Trim();
        }

        // The model sometimes imitates the note format ("Game: ...", "Mei: ...") at the start of a line.
        private static readonly Regex LeakedPrefix = new Regex(@"(^|(?<=[.!?]\s))\s*(game|context|mei|assistant)\s*:\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Speech version: also drops parenthesised pinyin/translations so TTS doesn't read them twice.</summary>
        public static string CleanForSpeech(string text, bool dropParentheses)
        {
            text = CleanForDisplay(text);
            if (dropParentheses) text = Pinyin.Replace(text, "");
            text = text.Replace("~", "").Replace("…", "...");
            return Spaces.Replace(text, " ").Trim();
        }

        public static string RemoveEmoji(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsSurrogate(c)) continue; // emoji and other astral symbols
                if (c >= '☀' && c <= '➿') continue; // misc symbols & dingbats
                if (c == '️' || c == '‍') continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool IsCjk(char c) =>
            (c >= '一' && c <= '鿿') || (c >= '㐀' && c <= '䶿') || (c >= '　' && c <= '〿') || (c >= '＀' && c <= '￯');

        public static bool ContainsCjk(string s)
        {
            foreach (char c in s) if (c >= '一' && c <= '鿿') return true;
            return false;
        }

        /// <summary>Splits text into runs of Chinese vs non-Chinese for bilingual TTS.</summary>
        public static List<(string text, bool chinese)> SplitByScript(string s)
        {
            var runs = new List<(string, bool)>();
            var sb = new StringBuilder();
            bool? current = null;
            foreach (char c in s)
            {
                bool neutral = char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsDigit(c);
                bool cjk = IsCjk(c);
                if (!neutral && current.HasValue && cjk != current.Value)
                {
                    Flush();
                    current = cjk;
                }
                if (!neutral && !current.HasValue) current = cjk;
                sb.Append(c);
            }
            Flush();
            return runs;

            void Flush()
            {
                string t = sb.ToString().Trim();
                if (t.Length > 0 && HasLetters(t)) runs.Add((t, current ?? false));
                sb.Clear();
            }
        }

        private static bool HasLetters(string s)
        {
            foreach (char c in s) if (char.IsLetter(c)) return true;
            return false;
        }

        private static readonly string[] Hallucinations =
        {
            "thank you.", "thanks for watching", "thank you for watching", "you", "bye.", "[blank_audio]",
            "(music)", "[music]", "(silence)", "[silence]", "subtitles by", "♪", "请不吝点赞", "字幕", "謝謝觀看", "谢谢观看",
        };

        /// <summary>Whisper likes to invent "Thank you." from silence; filter the classics.</summary>
        public static bool LooksLikeHallucination(string transcript)
        {
            string t = transcript.Trim().ToLowerInvariant();
            if (t.Length == 0) return true;
            foreach (var h in Hallucinations)
            {
                if (t == h || (h.Length > 6 && t.Contains(h))) return true;
            }
            return t.StartsWith("[") && t.EndsWith("]");
        }
    }

    /// <summary>Accumulates streamed tokens and emits complete sentences as soon as they're ready.</summary>
    public class SentenceSplitter
    {
        private readonly StringBuilder _buffer = new StringBuilder();
        private readonly int _minChars;

        public SentenceSplitter(int minChars = 18) => _minChars = minChars;

        public void Reset() => _buffer.Clear();

        public List<string> Feed(string delta)
        {
            var result = new List<string>();
            _buffer.Append(delta);
            while (true)
            {
                int cut = FindBoundary(_buffer.ToString());
                if (cut < 0) break;
                string sentence = _buffer.ToString(0, cut).Trim();
                _buffer.Remove(0, cut);
                if (sentence.Length > 0) result.Add(sentence);
            }
            return result;
        }

        public string Flush()
        {
            string rest = _buffer.ToString().Trim();
            _buffer.Clear();
            return rest;
        }

        private int FindBoundary(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool cjkStop = c == '。' || c == '！' || c == '？' || c == '；';
                bool latinStop = c == '.' || c == '!' || c == '?' || c == '\n';
                if (!cjkStop && !latinStop) continue;
                if (i + 1 < _minChars && c != '\n') continue;

                if (cjkStop)
                {
                    int end = i + 1;
                    while (end < s.Length && (s[end] == '”' || s[end] == '」' || s[end] == '）' || s[end] == ')')) end++;
                    return end;
                }

                // Need to see the next char to be sure it's the end ("3.5", "...", "Mr.").
                if (i + 1 >= s.Length) return -1;
                char next = s[i + 1];
                if (c == '.' && i > 0 && char.IsDigit(s[i - 1]) && char.IsDigit(next)) continue;
                if (next == '.' || next == '!' || next == '?') continue;
                if (char.IsWhiteSpace(next) || next == '"' || next == '\'' || next == ')')
                {
                    int end = i + 1;
                    while (end < s.Length && (s[end] == '"' || s[end] == '\'' || s[end] == ')')) end++;
                    return end;
                }
            }
            return -1;
        }
    }
}
