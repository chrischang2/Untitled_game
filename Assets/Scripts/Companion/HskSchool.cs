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
        /// <summary>The recogniser's runner-up reading of what was just said, offered as "did you mean ...?" (or null).</summary>
        public string alternative;
        /// <summary>In a lesson: the last wrong try at the current word (they have to say it again, or skip).</summary>
        public string retryHeard;
        public string retryAlternative;
        public ShopkeeperBrain teacher;
        /// <summary>When the last answer was given (unscaled time): the window shows its verdict, on its own, for a moment.</summary>
        public float lastAnsweredAt = -999f;
        /// <summary>When it began (unscaled time), for the time taken.</summary>
        public float startedAt = Time.unscaledTime;
        /// <summary>Seconds a question's verdict stays up before the next question appears.</summary>
        public const float ResultSeconds = 1.8f;
        /// <summary>The verdict of the last answer is on show (the next question isn't, yet).</summary>
        public bool ShowingResult => last != null && pending == null && Time.unscaledTime - lastAnsweredAt < ResultSeconds;

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
        /// <summary>
        /// A lesson or test wasn't passed: the screen shows the pass/fail message with a review of every question beneath
        /// it (UI.ReviewPanel), until the player closes it.
        /// </summary>
        public static event Action<LessonSession> ReviewRequested;
        public static event Action Changed;
        /// <summary>Set by the voice chat while it delivers a line: the recogniser's second choice for it.</summary>
        public static string IncomingAlternative;
        /// <summary>Set alongside it: how likely the audio was the word being asked (log-likelihood against the model's best reading).</summary>
        public static float? IncomingMargin;
        /// <summary>Also set: the word asked for is among the recogniser's top three guesses (accepted in the repeat-after-me parts of lessons).</summary>
        public static bool? IncomingTopMatch;
        /// <summary>The audio counts as the expected word when it is at least this likely (-3 = 5% as likely as the best reading).</summary>
        public const float ScoreMargin = -3f;
        /// <summary>The word being asked right now (what the recogniser should score the audio against), or null.</summary>
        public static string ExpectedWord => Current?.Current?.word?.hanzi;

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
                bool scoredOk = IncomingMargin.HasValue && IncomingMargin.Value > ScoreMargin;
                // Repeating after the teacher: any of the recogniser's top three guesses will do, so a lone syllable isn't said ten times.
                bool topOk = session.kind == LessonKind.Lesson && !q.recall && IncomingTopMatch == true;
                if (!isAnswer && !skipped && (scoredOk || topOk))
                {
                    // The recogniser's best reading was something else, but the word asked for is nearly as likely: take it as said.
                    ChatAudit.Write(teacher.MemoryKey, $"{session.Title} Q{session.index + 1}: heard \"{text}\" but \"{q.word.hanzi}\" " +
                                    (scoredOk ? $"scored {IncomingMargin.Value:0.0}" : "was among its top three guesses") + $": counted as {q.word.hanzi}");
                    text = q.word.hanzi;
                    s = Clean(text);
                    isAnswer = true;
                    IncomingAlternative = null;
                }
                if (NeedsConfirm(session, q))
                {
                    // Tests and lesson quizzes: show what was heard and let the player decide (Y to submit, or say it again).
                    session.pending = text;
                    session.alternative = IncomingAlternative;
                    ChatAudit.Write(teacher.MemoryKey, $"{session.Title} Q{session.index + 1}: heard \"{text}\" - waiting for the player to confirm");
                    Changed?.Invoke();
                    return true;
                }
                if (session.kind == LessonKind.Lesson && !isAnswer)
                {
                    Retry(teacher, session, q, text, IncomingAlternative);
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
            Submit(session, q, session.pending, session.alternative);
        }

        /// <summary>"Did you mean ...?": the player picks the recogniser's second choice instead, and it is graded as said.</summary>
        public static void SubmitAlternative()
        {
            var session = Current;
            var q = session?.Current;
            if (q == null || session.teacher == null) return;
            string alt = session.pending != null ? session.alternative : session.retryHeard != null ? session.retryAlternative : null;
            if (string.IsNullOrEmpty(alt)) return;
            ChatAudit.Write("HSK", $"{session.Title} Q{session.index + 1}: chose the second choice \"{alt}\" (first was \"{session.pending ?? session.retryHeard}\")");
            Submit(session, q, alt, null);
        }

        private static void Submit(LessonSession session, LessonQuestion q, string heard, string offer)
        {
            session.pending = null;
            session.alternative = null;
            session.retryAlternative = null;
            session.teacher.Interrupt();
            bool right = Grade(Clean(heard), q.word, q.recall);
            if (session.kind == LessonKind.Lesson && !right)
            {
                Retry(session.teacher, session, q, heard, offer);
                return;
            }
            Answer(session.teacher, session, q, heard, right, false);
        }

        /// <summary>Tests and the quiz part of lessons show what was heard first (Y to submit, N to say it again).</summary>
        private static bool NeedsConfirm(LessonSession session, LessonQuestion q) =>
            session.kind == LessonKind.Test || (session.kind == LessonKind.Lesson && q.recall);

        /// <summary>A lesson doesn't move on until the word is right: try again (or say 跳过 / skip).</summary>
        private static void Retry(ShopkeeperBrain teacher, LessonSession session, LessonQuestion q, string heard, string offer)
        {
            q.attempts++;
            session.retryHeard = heard;
            session.retryAlternative = offer;
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
            Current.alternative = null;
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
            // A round of the level's test: 20 random words from those not yet passed (every word of the level, in the end).
            var remaining = Hsk.TestPool(level);
            if (remaining.Count == 0)
            {
                Hsk.PassTest(level);
                teacher.SayDirect($"恭喜你！你通过了汉语水平考试{Catalog.ChineseNumber(level)}级！");
                return;
            }
            var pool = remaining.OrderBy(_ => UnityEngine.Random.value).Take(Hsk.TestQuestions).ToList();
            var session = new LessonSession { kind = LessonKind.Test, level = level };
            foreach (var w in pool) session.questions.Add(new LessonQuestion { word = w, recall = true });
            int cleared = Hsk.TestClearedCount(level), all = Hsk.TestWords(level).Count;
            Begin(teacher, session, $"好，现在考汉语水平考试{Catalog.ChineseNumber(level)}级。一共{Catalog.ChineseNumber(session.questions.Count)}题，" +
                                    $"对{Catalog.ChineseNumber(Hsk.PassMarkFor(session.questions.Count))}题就通过。" +
                                    (cleared > 0 ? $"你已经考过了{Catalog.ChineseNumber(cleared)}个词，还有{Catalog.ChineseNumber(all - cleared)}个。" : ""));
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
            session.lastAnsweredAt = Time.unscaledTime;
            session.retryHeard = null;
            session.retryAlternative = null;
            if (right) AudioManager.Instance?.PlaySfx("SFX/ui_confirmation_002", 0.7f, 0.02f); // a right answer sounds right
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
            FinishRound(teacher, session);
            // The history (journal, School page): what was taken, how long it took, and how it went.
            bool lesson = session.kind == LessonKind.Lesson;
            Hsk.LogSession((int)session.kind, session.level, session.lesson, Time.unscaledTime - session.startedAt,
                lesson ? session.RecallRight : session.Right, lesson ? session.RecallAsked : session.questions.Count, LastPassed);
        }

        private static void FinishRound(ShopkeeperBrain teacher, LessonSession session)
        {
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
                        RequestReview(session, "Lesson not passed", $"{session.Title}\nQuiz: {session.RecallRight}/{session.RecallAsked} right (you need {need}).");
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
                    int mark = Hsk.PassMarkFor(session.questions.Count);
                    bool passed = session.Right >= mark;
                    ChatAudit.Write(teacher.MemoryKey, $"{session.Title} round finished: {session.Right}/{session.questions.Count} (need {mark}) -> {(passed ? "PASSED" : "failed")}");
                    if (!passed)
                    {
                        // A failed round: every word goes back in the pool.
                        teacher.SayDirect($"你对了{Catalog.ChineseNumber(session.Right)}题。这次没通过，再努力一下！");
                        RequestReview(session, $"HSK {session.level} test: NOT PASSED", $"{session.Right}/{session.questions.Count} right  ·  you need {mark}\nLessons and practice help - try again any time!");
                        break;
                    }
                    LastPassed = true;
                    // The round's right answers are done; the missed words go back in the pool for a later round.
                    Hsk.ClearTestWords(session.level, session.questions.Where(q => q.correct).Select(q => q.word.hanzi));
                    int left = Hsk.TestPool(session.level).Count, total = Hsk.TestWords(session.level).Count;
                    ChatAudit.Write(teacher.MemoryKey, $"HSK {session.level} test: {total - left}/{total} words passed, {left} to go");
                    if (left > 0)
                    {
                        teacher.SayDirect($"很好！这一轮通过了。还有{Catalog.ChineseNumber(left)}个词，下次再考！");
                        var missedNow = session.questions.Where(q => !q.correct).Select(q => q.word.hanzi).ToList();
                        GameEvents.Banner($"HSK {session.level} test: round passed!", $"{session.Right}/{session.questions.Count} right  ·  {total - left}/{total} words of HSK {session.level} passed, {left} to go" +
                                          (missedNow.Count > 0 ? $"\nBack in the pool: {string.Join(" ", missedNow)}" : "") +
                                          "\nEvery word of the level has to be passed in a test round.", true);
                        break;
                    }
                    Hsk.PassTest(session.level);
                    teacher.SayDirect($"恭喜你！你通过了汉语水平考试{Catalog.ChineseNumber(session.level)}级！");
                    GameEvents.Banner($"HSK {session.level} PASSED!", $"{session.Right}/{session.questions.Count} right\n" +
                                      $"Unlocked (with enough friendship): {Hsk.Unlocks[session.level]}.\nThe interface now uses more Chinese.", true);
                    break;
                }
            }
        }

        /// <summary>The review panel shows the message and every question (plain banner if no screen is listening, as in tests without a UI).</summary>
        private static void RequestReview(LessonSession session, string title, string details)
        {
            if (ReviewRequested != null) ReviewRequested.Invoke(session);
            else GameEvents.Banner(title, details, false);
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
            ReviewRequested = null;
        }
    }
}
