using UntitledGame.Progression;

namespace UntitledGame.UI
{
    /// <summary>
    /// Interface text that turns Chinese as the player passes HSK tests. Each label has a tier (the HSK level of its
    /// Chinese): at that level it shows both ("第三天 Day 3"), and once the player has passed the next test it shows
    /// only the Chinese. Before then it stays English. HSK 3 labels go straight to Chinese at HSK 3, the last test.
    /// </summary>
    public static class UiText
    {
        private static bool ChineseOnly(int tier) => Hsk.Level > tier || (Hsk.Level >= Hsk.MaxLevel && tier >= Hsk.MaxLevel);

        /// <summary>English, both, or Chinese depending on the HSK level passed.</summary>
        public static string T(string english, string chinese, int tier)
        {
            if (ChineseOnly(tier)) return chinese;
            if (Hsk.Level == tier && tier > 0) return $"{chinese} <size=75%><color=#8A7563>{english}</color></size>";
            return english;
        }

        /// <summary>Like <see cref="T"/> but without rich text (window titles, buttons).</summary>
        public static string Plain(string english, string chinese, int tier)
        {
            if (ChineseOnly(tier)) return chinese;
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
