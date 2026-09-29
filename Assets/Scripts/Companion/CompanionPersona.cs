using System.Linq;
using System.Text;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Fishing;

namespace UntitledGame.Companion
{
    /// <summary>Builds Mei's system prompt and the compact per-turn situation notes.</summary>
    public static class CompanionPersona
    {
        public const string Name = "Mei";

        public const string DefaultPersona =
            "You are Mei, the player's close friend and fishing buddy at Willow Lake: a quiet lake in the hills with a little wooden dock, " +
            "a log cabin, a campfire and a tabby cat called Mochi who naps on the dock. You are warm, playful, a little dreamy, and you love " +
            "tea, stargazing, fish soup and silly fish facts. You and the player are hanging out and talking out loud in a relaxed voice chat.";

        public const string Rules =
            "How to reply:\n" +
            "- This is spoken aloud by a text-to-speech voice. Talk naturally in 1 to 3 short sentences, under 45 words.\n" +
            "- Plain words only: no emoji, no lists, no markdown, no asterisks, no stage directions, no sound effects.\n" +
            "- Stay in character as Mei. Never mention being an AI, a model, a program or a game.\n" +
            "- Be curious about the player. Sometimes ask a small question back, but not every time.\n" +
            "- Lines in square brackets like [Game: ...] are things you notice around you. Use them naturally; never read them out.\n" +
            "- If the player just says something short, reply short. Keep it cozy.";

        public const string MandarinRules =
            "Language practice: the player is a beginner learning Mandarin Chinese. Speak mostly simple English, and in each reply teach or " +
            "use ONE short, useful Mandarin word or phrase, written in Chinese characters followed by pinyin in brackets, like 钓鱼 (diào yú). " +
            "Invite the player to try saying it. If the player tries Mandarin, cheer them on and gently correct mistakes. Favour fishing, nature, " +
            "food and everyday greetings.";

        public static string BuildSystemPrompt(string personaOverride)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(personaOverride) ? DefaultPersona : personaOverride.Trim());
            sb.AppendLine();
            sb.AppendLine(Rules);
            sb.AppendLine();
            sb.AppendLine("What you know about fishing at Willow Lake (use only if it comes up):");
            foreach (var f in FishDatabase.All.Where(f => f.IsFish))
            {
                sb.AppendLine($"- {f.name} ({f.hanzi}): {FishDatabase.RarityLabel(f.rarity).ToLower()}, {FishDatabase.WhenText(f)}, {FishDatabase.WhereText(f)}.");
            }
            sb.AppendLine("- Controls if asked: hold left mouse to cast, click when the bobber dives, hold to reel but ease off when the fish pulls. Hold V to talk.");
            if (SaveSystem.Settings.language == LanguageMode.MandarinPractice)
            {
                sb.AppendLine();
                sb.AppendLine(MandarinRules);
            }
            var notes = SaveSystem.Data.companionNotes;
            if (notes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Things you remember about the player from earlier days: " + string.Join("; ", notes.Skip(System.Math.Max(0, notes.Count - 6))));
            }
            return sb.ToString().Trim();
        }

        /// <summary>One short line describing the moment (kept small so the prompt cache stays warm).</summary>
        public static string Situation(string playerActivity, float distanceToPlayer)
        {
            var dn = DayNightCycle.Instance;
            string time = dn != null ? $"{dn.ClockText}, {dn.PhaseDescription}" : "daytime";
            string weather = Weather.Instance != null ? Weather.Instance.Description : "clear skies";
            string activity = string.IsNullOrEmpty(playerActivity) ? "hanging out with you" : playerActivity;
            string where = distanceToPlayer < 5f ? "right next to you" : distanceToPlayer < 15f ? "nearby" : "a little way off";
            var last = CatchJournal.Session.Count > 0 ? CatchJournal.Session[CatchJournal.Session.Count - 1] : null;
            string lastCatch = last != null ? $"; their last catch today: {last.species.name}{(last.species.IsFish ? $" {last.length:0} cm" : "")}" : "";
            return $"[Game: {time}, {weather}. The player is {where}, {activity}. Journal {CatchJournal.SpeciesDiscovered}/{FishDatabase.All.Count}, {CatchJournal.Session.Count} caught today{lastCatch}.]";
        }
    }
}
