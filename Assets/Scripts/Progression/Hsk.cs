using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Language;

namespace UntitledGame.Progression
{
    /// <summary>
    /// The player's HSK progress at 高老师's test centre (考试中心): which lessons they've passed and which HSK test
    /// (1-3) they've passed. Passing a test unlocks goods in every shop (ItemDef.minHsk, alongside friendship), better
    /// fish prices (Auntie Chen's membership cards), new cosmetics, and switches more of the interface to Chinese.
    /// </summary>
    public static class Hsk
    {
        public const int MaxLevel = 3;
        /// <summary>Lessons per HSK level (together they cover every word of that level).</summary>
        public static readonly int[] LessonCounts = { 0, 11, 10, 20 };
        /// <summary>Money for passing a lesson for the first time (practice never pays).</summary>
        public static readonly int[] LessonReward = { 0, 40, 60, 70 };
        public const int RecallPerLesson = 8, LessonPassMark = 6;
        public const int TestQuestions = 20, TestPassMark = 15;
        public const int PracticeQuestions = 10;

        /// <summary>What each HSK level unlocks (for the test centre's window and toasts). Friendship still applies too.</summary>
        public static readonly string[] Unlocks =
        {
            "",
            "fish sell for 10% more, blue line, squid & crab bait, good oars, huge bucket, Fish under the Rocks, double bed, sofa, radio, new cosmetics",
            "fish sell for 20% more, black line, live baitfish, sail, giant bucket, Fish of the Open Sea, big bed, new cosmetics",
            "fish sell for 35% more, gold line, glow lure, new boat (the island), Legends of the Sea, golden cosmetics",
        };

        /// <summary>How much more Auntie Chen pays for fish at each HSK level (automatic: no card to buy).</summary>
        public static readonly float[] FishBonusByLevel = { 0f, 0.10f, 0.20f, 0.35f };
        public static float FishBonus => FishBonusByLevel[Level];

        /// <summary>
        /// HSK 1 lessons by theme (the other levels are a fixed shuffle). Each word appears in exactly one lesson.
        /// </summary>
        private static readonly (string english, string chinese, string words)[] Hsk1Themes =
        {
            ("Hello and thank you", "你好", "我 你 他 她 我们 好 谢谢 不客气 对不起 没关系 再见 请 喂 叫 名字"),
            ("Numbers and money", "多少钱", "一 二 三 四 五 六 七 八 九 十 零 几 多少 块 钱"),
            ("Family and people", "家人", "爸爸 妈妈 儿子 女儿 朋友 同学 老师 学生 医生 先生 小姐 人 谁 认识 岁"),
            ("Time and dates", "几点", "今天 明天 昨天 现在 时候 上午 中午 下午 点 分钟 年 月 日 星期"),
            ("Food, drink and shopping", "吃饭", "吃 喝 菜 米饭 茶 水 水果 苹果 杯子 饭馆 买 商店"),
            ("Places and getting around", "去哪儿", "家 学校 医院 火车站 北京 中国 出租车 飞机 去 来 回 住 在 里 前面 后面"),
            ("Things around you", "东西", "东西 书 电脑 电视 电影 衣服 桌子 椅子 猫 狗 字 汉语 个 本 些"),
            ("Everyday actions", "做什么", "看 看见 听 说话 读 写 做 工作 学习 睡觉 打电话 开 坐"),
            ("Describing things and feelings", "怎么样", "大 小 多 少 热 冷 漂亮 高兴 很 太 天气 下雨 爱 喜欢 想"),
            ("Questions and pointing", "什么", "什么 哪 那 这 怎么 怎么样 吗 呢 是 有"),
            ("Little words", "不、没、都", "不 没 都 和 的 了 会 能 上 下"),
        };

        /// <summary>The lesson's theme ("Numbers and money", "多少钱"), or ("Lesson 3", "第三课") where there are no themes.</summary>
        public static (string english, string chinese) LessonTitle(int level, int lesson)
        {
            if (level == 1 && lesson >= 1 && lesson <= Hsk1Themes.Length) return (Hsk1Themes[lesson - 1].english, Hsk1Themes[lesson - 1].chinese);
            return ($"Lesson {lesson}", "");
        }

        public static event Action Changed;

        private static SaveData Data => SaveSystem.Data;

        /// <summary>The highest HSK test passed (0-3).</summary>
        public static int Level => Mathf.Clamp(Data.hskLevel, 0, MaxLevel);

        private static List<string> Done => Data.lessonsDone ??= new List<string>();
        private static List<string> Missed => Data.hskMissed ??= new List<string>();

        public static bool LessonDone(int level, int lesson) => Done.Contains($"{level}-{lesson}");
        public static int LessonsDone(int level) => Enumerable.Range(1, LessonCounts[level]).Count(n => LessonDone(level, n));

        /// <summary>Marks a lesson passed; true the first time (when it pays).</summary>
        public static bool MarkLessonDone(int level, int lesson)
        {
            if (LessonDone(level, lesson)) return false;
            Done.Add($"{level}-{lesson}");
            ChatAudit.Write("HSK", $"passed lesson {lesson} of HSK {level}");
            Changed?.Invoke();
            return true;
        }

        /// <summary>The next lesson to take: the first not yet passed at the lowest level open to the player.</summary>
        public static (int level, int lesson) NextLesson()
        {
            int top = Mathf.Min(Level + 1, MaxLevel);
            for (int l = 1; l <= top; l++)
                for (int n = 1; n <= LessonCounts[l]; n++)
                    if (!LessonDone(l, n)) return (l, n);
            // Everything open is done: start again from lesson 1 of the newest level.
            return (top, 1);
        }

        /// <summary>The HSK test the player can take next (0 once they've passed HSK 3).</summary>
        public static int NextTest => Level >= MaxLevel ? 0 : Level + 1;

        public static void PassTest(int level)
        {
            if (level <= Data.hskLevel) return;
            Data.hskLevel = Mathf.Clamp(level, 0, MaxLevel);
            ChatAudit.Write("HSK", $"passed the HSK {level} test");
            Changed?.Invoke();
        }

        /// <summary>Words the player got wrong come back more often in free practice.</summary>
        public static void NoteAnswer(string word, bool right)
        {
            if (right) Missed.Remove(word);
            else if (!Missed.Contains(word))
            {
                Missed.Add(word);
                if (Missed.Count > 60) Missed.RemoveAt(0);
            }
        }

        public static IReadOnlyList<string> MissedWords => Missed;

        // ------------------------------------------------------------------ curriculum

        private static readonly Dictionary<int, List<List<HskVocab.Word>>> Lessons = new Dictionary<int, List<List<HskVocab.Word>>>();

        /// <summary>
        /// The words of one lesson (1-based). Each level's words are shuffled with a fixed seed and split evenly, so the
        /// lessons are the same for everyone and together cover the whole level.
        /// </summary>
        public static List<HskVocab.Word> LessonWords(int level, int lesson)
        {
            if (level < 1 || level > MaxLevel) return new List<HskVocab.Word>();
            if (level == 1 && !Lessons.ContainsKey(1))
                Lessons[1] = Hsk1Themes.Select(t => t.words.Split(' ').Select(HskVocab.Get).Where(w => w != null).ToList()).ToList();
            if (!Lessons.TryGetValue(level, out var split))
            {
                var words = HskVocab.Words(level);
                var rng = new System.Random(1000 + level * 7919);
                for (int i = words.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (words[i], words[j]) = (words[j], words[i]);
                }
                int count = LessonCounts[level];
                split = new List<List<HskVocab.Word>>();
                for (int k = 0; k < count; k++)
                {
                    int from = words.Count * k / count, to = words.Count * (k + 1) / count;
                    split.Add(words.GetRange(from, to - from));
                }
                Lessons[level] = split;
            }
            return lesson >= 1 && lesson <= split.Count ? split[lesson - 1] : new List<HskVocab.Word>();
        }

        /// <summary>Particles and measure words ("(question particle)") are practised by repeating, never asked from English.</summary>
        public static bool Askable(HskVocab.Word w) => !string.IsNullOrEmpty(w.meaning) && !w.meaning.StartsWith("(");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Changed = null;
    }
}
