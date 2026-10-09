using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Language;
using UntitledGame.Progression;

namespace UntitledGame.Companion
{
    public enum LessonKind { Lesson, Practice, Test }

    public class LessonQuestion
    {
        public HskVocab.Word word;
        public bool recall;      // false: listen and repeat (hanzi shown); true: English shown, say it in Chinese
        public bool answered;
        public bool correct;
        public bool skipped;
        public int attempts;     // wrong tries before it was right (lessons don't move on until it is)
        public string heard;
    }

    public class LessonSession
    {
        public LessonKind kind;
        public int level;
        public int lesson;       // 1-based (lessons only)
        public List<LessonQuestion> questions = new List<LessonQuestion>();
        public int index;
        public LessonQuestion last;
        /// <summary>In a test: what was heard for the current question, waiting for the player to confirm it (Y) or say it again.</summary>
        public string pending;
        /// <summary>In a lesson: the last wrong try at the current word (they have to say it again, or skip).</summary>
        public string retryHeard;
        public ShopkeeperBrain teacher;

        public LessonQuestion Current => index < questions.Count ? questions[index] : null;
        public int RecallAsked => questions.Count(q => q.recall && q.answered);
        public int RecallRight => questions.Count(q => q.recall && q.correct);
        public int Right => questions.Count(q => q.correct);

        public string Title => kind switch
        {
            LessonKind.Lesson => $"HSK {level} · Lesson {lesson}: {Hsk.LessonTitle(level, lesson).english}",
            LessonKind.Test => $"HSK {level} Test",
            _ => "Free practice",
        };
    }

    /// <summary>
    /// 高老师's lessons, free practice and HSK tests at the test centre (考试中心). Everything is spoken: say 我想上课,
    /// 我想练习 or 我想考试 to her. The teacher's lines are scripted (no LLM) so they're instant and exact, and every answer
    /// is graded by the game: the word itself, or the same sounds without tones (speech recognition can't hear tones
    /// reliably), unless what was heard is a different HSK word.
    /// </summary>
    public static class HskSchool
    {
        public const string ShopId = "school";

        public static LessonSession Current { get; private set; }
        /// <summary>Self-test: true/false forces the teacher's surprise present after a lesson (null = random).</summary>
        public static bool? ForceSurprise;
        /// <summary>The last finished lesson, practice or test, and whether it was passed (shown in the test centre window).</summary>
        public static LessonSession LastFinished { get; private set; }
        public static bool LastPassed { get; private set; }
        public static event Action Changed;

        private static readonly string[] QuitWords = { "不学了", "不上了", "不考了", "不练了", "结束", "停", "我要走" };
        private static readonly string[] SkipWords = { "不知道", "跳过", "下一个", "不会" };
        private static readonly string[] EnglishSkips = { "skip", "pass", "next", "don't know", "dont know", "no idea" };
        private static readonly string[] NotARequest = { "喜欢", "不想", "什么", "怎么", "难不难", "难吗" };
        private static readonly string[] Praise = { "对！", "很好！", "对了！", "非常好！", "没错！" };

        /// <summary>
        /// Handles a line said to the teacher: an answer while a session is running, or a request to start one.
        /// False when it's neither (the teacher then just chats).
        /// </summary>
        public static bool TryHandle(ShopkeeperBrain teacher, string text)
        {
            string s = Clean(text);
            var session = Current;
            if (session != null)
            {
                var q = session.Current;
                if (q == null) { End(); return false; }
                bool isAnswer = Grade(s, q.word, q.recall);
                if (!isAnswer && QuitWords.Any(s.Contains))
                {
                    ChatAudit.Write(teacher.MemoryKey, $"{session.Title} stopped at question {session.index + 1}/{session.questions.Count}");
                    End();
                    teacher.SayDirect(session.kind == LessonKind.Test ? "好，考试结束了。下次再来吧！" : "好，今天就学到这里。下次再来！");
                    return true;
                }
                bool skipped = !isAnswer && (SkipWords.Any(s.Contains) || EnglishSkips.Any((text ?? "").ToLowerInvariant().Contains));
                if (skipped)
                {
                    Answer(teacher, session, q, text, false, true);
                    return true;
                }
                if (NeedsConfirm(session, q))
                {
                    // Tests and lesson quizzes: show what was heard and let the player decide (Y to submit, or say it again).
                    session.pending = text;
                    ChatAudit.Write(teacher.MemoryKey, $"{session.Title} Q{session.index + 1}: heard \"{text}\" - waiting for the player to confirm");
                    Changed?.Invoke();
                    return true;
                }
                if (session.kind == LessonKind.Lesson && !isAnswer)
                {
                    Retry(teacher, session, q, text);
                    return true;
                }
                Answer(teacher, session, q, text, isAnswer, false);
                return true;
            }

            // Questions about her ("你喜欢考试吗？", "怎么考试？") are chat, not requests.
            if (NotARequest.Any(s.Contains)) return false;
            if (Wants(s, "考试") || Wants(s, "考"))
            {
                StartTest(teacher);
                return true;
            }
            if (Wants(s, "练习") || Wants(s, "复习"))
            {
                StartPractice(teacher);
                return true;
            }
            if (Wants(s, "上课") || Wants(s, "学习") || s.Contains("课"))
            {
                StartLesson(teacher, RequestedLesson(s));
                return true;
            }
            return false;
        }

        /// <summary>Test: the player confirms that what was heard is what they said (Y). It's graded now.</summary>
        public static void SubmitPending()
        {
            var session = Current;
            var q = session?.Current;
            if (q == null || session.pending == null || session.teacher == null) return;
            string heard = session.pending;
            session.pending = null;
            session.teacher.Interrupt();
            bool right = Grade(Clean(heard), q.word, q.recall);
            if (session.kind == LessonKind.Lesson && !right)
            {
                Retry(session.teacher, session, q, heard);
                return;
            }
            Answer(session.teacher, session, q, heard, right, false);
        }

        /// <summary>Tests and the quiz part of lessons show what was heard first (Y to submit, N to say it again).</summary>
        private static bool NeedsConfirm(LessonSession session, LessonQuestion q) =>
            session.kind == LessonKind.Test || (session.kind == LessonKind.Lesson && q.recall);

        /// <summary>A lesson doesn't move on until the word is right: try again (or say 跳过 / skip).</summary>
        private static void Retry(ShopkeeperBrain teacher, LessonSession session, LessonQuestion q, string heard)
        {
            q.attempts++;
            session.retryHeard = heard;
            if (q.attempts == 1) Hsk.RecordAnswer(q.word.hanzi, false, q.recall); // only the first miss counts against the word
            ChatAudit.Write(teacher.MemoryKey, $"{session.Title} Q{session.index + 1}: {q.word.hanzi} heard \"{heard}\" -> wrong, try {q.attempts + 1}");
            teacher.SayDirect(q.recall ? "不对，再试一次。" : $"再说一次：{q.word.hanzi}。");
            Changed?.Invoke();
        }

        /// <summary>Test: the player throws away what was heard (N) and will say it again.</summary>
        public static void DiscardPending()
        {
            if (Current?.pending == null) return;
            ChatAudit.Write("HSK", $"discarded \"{Current.pending}\" to say it again");
            Current.pending = null;
            Changed?.Invoke();
        }

        /// <summary>The player walked away or said goodbye: the session ends without a reward.</summary>
        public static void Abandon()
        {
            if (Current == null) return;
            ChatAudit.Write("HSK", $"{Current.Title} abandoned at question {Current.index + 1}/{Current.questions.Count}");
            End();
        }

        private static void End()
        {
            Current = null;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ starting

        public static void StartLesson(ShopkeeperBrain teacher, int requested = 0)
        {
            var (level, lesson) = Hsk.NextLesson();
            if (requested > 0) lesson = Mathf.Clamp(requested, 1, Hsk.LessonCounts[level]);
            var words = Hsk.LessonWords(level, lesson);
            if (words.Count == 0)
            {
                teacher.SayDirect("对不起，今天没有课。");
                return;
            }
            var session = new LessonSession { kind = LessonKind.Lesson, level = level, lesson = lesson };
            // Part 1: listen and repeat every word. Part 2: a short quiz from English.
            foreach (var w in words) session.questions.Add(new LessonQuestion { word = w, recall = false });
            var quiz = words.Where(Hsk.Askable).OrderBy(_ => UnityEngine.Random.value).Take(Hsk.RecallPerLesson).ToList();
            foreach (var w in quiz) session.questions.Add(new LessonQuestion { word = w, recall = true });
            string theme = Hsk.LessonTitle(level, lesson).chinese;
            Begin(teacher, session, $"好！我们上{Catalog.ChineseNumber(level)}级第{Catalog.ChineseNumber(lesson)}课" + (theme.Length > 0 ? $"：「{theme}」" : "") +
                                    $"。今天学{Catalog.ChineseNumber(words.Count)}个词。先听我说，然后跟我说。");
        }

        public static void StartPractice(ShopkeeperBrain teacher)
        {
            int top = Mathf.Min(Hsk.Level + 1, Hsk.MaxLevel);
            var pool = HskVocab.All.Where(w => w.level <= top).ToList();
            if (pool.Count == 0)
            {
                teacher.SayDirect("对不起，现在不能练习。");
                return;
            }
            // Spaced repetition: due and weak words first, a few easy wins, new words if there's room.
            var chosen = Hsk.PracticeWords(top, Hsk.PracticeQuestions);
            var session = new LessonSession { kind = LessonKind.Practice, level = top };
            foreach (var w in chosen)
                session.questions.Add(new LessonQuestion { word = w, recall = Hsk.Askable(w) && Hsk.Box(w.hanzi) >= 1 });
            Begin(teacher, session, "好，我们练习一下！");
        }

        public static void StartTest(ShopkeeperBrain teacher)
        {
            int level = Hsk.NextTest;
            if (level == 0)
            {
                teacher.SayDirect("你已经通过了汉语水平考试三级！太好了！你可以练习。");
                return;
            }
            var pool = HskVocab.Words(level).Where(Hsk.Askable).OrderBy(_ => UnityEngine.Random.value).Take(Hsk.TestQuestions).ToList();
            var session = new LessonSession { kind = LessonKind.Test, level = level };
            foreach (var w in pool) session.questions.Add(new LessonQuestion { word = w, recall = true });
            Begin(teacher, session, $"好，现在考汉语水平考试{Catalog.ChineseNumber(level)}级。一共{Catalog.ChineseNumber(session.questions.Count)}题，" +
                                    $"对{Catalog.ChineseNumber(Hsk.TestPassMark)}题就通过。");
        }

        private static void Begin(ShopkeeperBrain teacher, LessonSession session, string intro)
        {
            session.teacher = teacher;
            Current = session;
            ChatAudit.Write(teacher.MemoryKey, $"starts {session.Title}: {session.questions.Count} questions ({string.Join(" ", session.questions.Select(q => (q.recall ? "?" : "") + q.word.hanzi))})");
            teacher.SayDirect(intro);
            teacher.SayDirect(Prompt(session));
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ answering

        private static void Answer(ShopkeeperBrain teacher, LessonSession session, LessonQuestion q, string heard, bool right, bool skipped)
        {
            q.answered = true;
            q.correct = right;
            q.skipped = skipped;
            q.heard = heard;
            session.last = q;
            session.retryHeard = null;
            if (q.attempts == 0) Hsk.RecordAnswer(q.word.hanzi, right, q.recall); // right first time moves it up; a skip drops it
            ChatAudit.Write(teacher.MemoryKey, $"{session.Title} Q{session.index + 1}: {q.word.hanzi} ({(q.recall ? "from English: " + q.word.meaning : "repeat")}) " +
                                               $"heard \"{heard}\" -> {(right ? "RIGHT" : skipped ? "skipped" : "wrong")}");
            string feedback = right ? Praise[UnityEngine.Random.Range(0, Praise.Length)]
                : skipped ? $"是「{q.word.hanzi}」。"
                : q.recall ? $"不对，是「{q.word.hanzi}」。" : $"再听一次：{q.word.hanzi}。";
            session.index++;
            teacher.SayDirect(feedback);
            if (session.Current != null) teacher.SayDirect(Prompt(session));
            else Finish(teacher, session);
            Changed?.Invoke();
        }

        private static string Prompt(LessonSession session)
        {
            var q = session.Current;
            if (q == null) return "";
            if (!q.recall) return $"跟我说：{q.word.hanzi}。";
            bool firstQuiz = session.kind == LessonKind.Lesson && session.questions.IndexOf(q) > 0 && !session.questions[session.index - 1].recall;
            if (firstQuiz) return "现在我考考你。这个用汉语怎么说？";
            return session.kind == LessonKind.Test ? $"第{Catalog.ChineseNumber(session.index + 1)}题：用汉语怎么说？" : "这个用汉语怎么说？";
        }

        private static void Finish(ShopkeeperBrain teacher, LessonSession session)
        {
            Current = null;
            LastFinished = session;
            LastPassed = false;
            switch (session.kind)
            {
                case LessonKind.Lesson:
                {
                    int need = Mathf.Min(Hsk.LessonPassMark, Mathf.CeilToInt(session.RecallAsked * 0.75f));
                    bool passed = session.RecallRight >= need;
                    ChatAudit.Write(teacher.MemoryKey, $"{session.Title} finished: quiz {session.RecallRight}/{session.RecallAsked} -> {(passed ? "PASSED" : "not passed")}");
                    if (!passed)
                    {
                        teacher.SayDirect($"你对了{Catalog.ChineseNumber(session.RecallRight)}个。还要多练习，下次再来！");
                        GameEvents.Banner("Lesson not passed", $"{session.Title}\nQuiz: {session.RecallRight}/{session.RecallAsked} right (you need {need}). Try it again!", false);
                        break;
                    }
                    LastPassed = true;
                    bool first = Hsk.MarkLessonDone(session.level, session.lesson);
                    // Specific praise for what they got right first time (informational, not "you should").
                    var firstTry = session.questions.Where(q => q.correct && q.attempts == 0 && !q.skipped).Select(q => q.word.hanzi).Distinct().Take(3).ToList();
                    teacher.SayDirect(firstTry.Count > 0 ? $"太好了！「{string.Join("」「", firstTry)}」你说得很好！" : "太好了！这一课你学会了！");
                    // No fixed pay: sometimes she has a little surprise for you.
                    string surprise = null;
                    bool friend = Progression.Perks.Has("school"); // an old friend always has a present, and a bigger one
                    if (ForceSurprise ?? (friend || UnityEngine.Random.value < (first ? 0.6f : 0.3f)))
                    {
                        surprise = Surprise.Give(teacher.DisplayNameEnglish, 1, moneyScale: friend ? 0.8f : 0.4f); // small: learning is its own reward
                        teacher.SayDirect("这是给你的小礼物！");
                    }
                    GameEvents.Banner("Lesson passed!", $"{session.Title}\nQuiz: {session.RecallRight}/{session.RecallAsked} right" +
                                      (firstTry.Count > 0 ? $"  ·  first try: {string.Join(" ", firstTry)}" : "") +
                                      (surprise != null ? $"\n{teacher.DisplayNameEnglish} has a little present for you: {surprise}" : "") +
                                      $"\nHSK {session.level} lessons passed: {Hsk.LessonsDone(session.level)}/{Hsk.LessonCounts[session.level]}", true);
                    break;
                }
                case LessonKind.Practice:
                    ChatAudit.Write(teacher.MemoryKey, $"practice finished: {session.Right}/{session.questions.Count}");
                    teacher.SayDirect($"练习完了！{Catalog.ChineseNumber(session.questions.Count)}个词，你对了{Catalog.ChineseNumber(session.Right)}个。");
                    LastPassed = true;
                    GameEvents.Banner("Practice finished", $"{session.Right}/{session.questions.Count} right  ·  practice is free and doesn't pay", true);
                    break;
                case LessonKind.Test:
                {
                    bool passed = session.Right >= Hsk.TestPassMark;
                    ChatAudit.Write(teacher.MemoryKey, $"{session.Title} finished: {session.Right}/{session.questions.Count} -> {(passed ? "PASSED" : "failed")}");
                    if (!passed)
                    {
                        teacher.SayDirect($"你对了{Catalog.ChineseNumber(session.Right)}题。这次没通过，再努力一下！");
                        GameEvents.Banner($"HSK {session.level} test: NOT PASSED", $"{session.Right}/{session.questions.Count} right  ·  you need {Hsk.TestPassMark}\n" +
                                          "The words you missed are in Teacher Gao's window. Lessons and practice help - try again any time!", false);
                        break;
                    }
                    LastPassed = true;
                    Hsk.PassTest(session.level);
                    teacher.SayDirect($"恭喜你！你通过了汉语水平考试{Catalog.ChineseNumber(session.level)}级！");
                    GameEvents.Banner($"HSK {session.level} PASSED!", $"{session.Right}/{session.questions.Count} right\n" +
                                      $"Unlocked (with enough friendship): {Hsk.Unlocks[session.level]}.\nThe interface now uses more Chinese.", true);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ grading

        /// <summary>Simplified Chinese characters only, with digits turned back into Chinese numbers (8 -> 八, 100 -> 一百).</summary>
        public static string Clean(string text)
        {
            var sb = new StringBuilder();
            string s = Pinyin.ToSimplified(text ?? "");
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsDigit(s[i]))
                {
                    int j = i;
                    while (j < s.Length && char.IsDigit(s[j])) j++;
                    if (int.TryParse(s.Substring(i, j - i), out int n) && n < 100000000)
                        sb.Append(Catalog.ChineseNumber(n));
                    i = j - 1;
                    continue;
                }
                if (Pinyin.IsHanzi(s[i])) sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Did they say the word? Tones are ignored completely: if the characters that were heard have the word's
        /// pinyin without tones (any of their readings), it counts (喝 heard as 和, 菜 as 才, 写 as 谢). Initials and
        /// finals still have to be right (书 shū is not 十 shí). From English (recall), another HSK word with the same
        /// meaning also counts.
        /// </summary>
        public static bool Grade(string cleaned, HskVocab.Word word, bool recall)
        {
            if (string.IsNullOrEmpty(cleaned) || word == null) return false;
            if (Says(cleaned, word)) return true;
            if (recall)
            {
                foreach (var other in HskVocab.All)
                    if (other != word && other.meaning == word.meaning && cleaned.Contains(other.hanzi)) return true;
            }
            return false;
        }

        private static bool Says(string s, HskVocab.Word word)
        {
            string w = word.hanzi;
            if (s.Contains(w)) return true;
            if (w == "两" && s.Contains("二")) return true;
            int n = w.Length;
            if (s.Length < n) return false;
            // A one-character word must be (nearly) all they said, or any line with that sound would count.
            if (n == 1 && s.Length > 3) return false;
            for (int i = 0; i + n <= s.Length; i++)
            {
                bool same = true;
                for (int k = 0; k < n && same; k++) same = SoundsAlike(s[i + k], w[k]);
                if (same) return true;
            }
            return false;
        }

        /// <summary>Same toneless pinyin, for any reading of either character (s holds only hanzi).</summary>
        private static bool SoundsAlike(char heard, char target)
        {
            if (heard == target) return true;
            var a = Pinyin.PlainReadings(heard);
            var b = Pinyin.PlainReadings(target);
            foreach (var x in a)
                foreach (var y in b)
                    if (x == y) return true;
            return false;
        }

        /// <summary>"我想上课" / "上课" (also misheard on toneless pinyin, e.g. 商课).</summary>
        private static bool Wants(string s, string word) => ShopIntentParser.Mentions(s, word);

        /// <summary>"第三课" -> 3 (0 when no lesson number was said).</summary>
        private static int RequestedLesson(string s)
        {
            int at = s.IndexOf('第');
            if (at < 0) return 0;
            int end = s.IndexOf('课', at);
            if (end <= at + 1) return 0;
            return ShopIntentParser.ParseNumber(s.Substring(at + 1, end - at - 1));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            Changed = null;
        }
    }
}
