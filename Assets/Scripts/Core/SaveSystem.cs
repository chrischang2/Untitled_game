using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UntitledGame.Core
{
    /// <summary>How much English Mei mixes into her Mandarin.</summary>
    public enum ImmersionLevel
    {
        Beginner = 0,      // Mandarin + short English explanations
        Intermediate = 1,  // Mandarin; English only to gloss new words or when asked
        Immersion = 2,     // Mandarin only
    }

    public enum PinyinMode
    {
        Always = 0,
        NewWordsOnly = 1,
        Off = 2,
    }

    /// <summary>Preferences shared by every save slot (stored in settings.json).</summary>
    [Serializable]
    public class GameSettings
    {
        public float masterVolume = 1f;
        public float musicVolume = 0.55f;
        public float ambienceVolume = 0.8f;
        public float sfxVolume = 0.9f;
        public float voiceVolume = 1f;
        public float voiceSpeed = 0.9f;
        /// <summary>Kokoro speaker for Mei's natural voice, or -1 for the classic (fastest) Matcha + Piper voices.</summary>
        public int meiVoice = 3;
        /// <summary>0 = Auto (SenseVoice; Qwen3-ASR for lines with English), 1 = SenseVoice only, 2 = Qwen3-ASR for everything.</summary>
        public int asrMode;
        public bool speakReplies = true;
        public bool companionChatter = true;
        public bool handsFree;
        public ImmersionLevel immersion = ImmersionLevel.Beginner;
        public PinyinMode pinyin = PinyinMode.Always;
        public float realSecondsPerHour = 120f;   // 6am-2am (the waking day) = 40 real minutes
        public int settingsVersion;
        public float mouseSensitivity = 1f;
        public bool keepVoiceRecordings = true;
    }

    [Serializable]
    public class CaughtRecord
    {
        public string speciesId;
        public int count;
        public float bestLength;
        public int firstDay;
    }

    [Serializable]
    public class ItemStack
    {
        public string id;
        public int count;
    }

    [Serializable]
    public class BucketFish
    {
        public string speciesId;
        public float length;
    }

    [Serializable]
    public class PlacedItem
    {
        public string id;
        public float x, y, z, yaw;
    }

    [Serializable]
    public class PetState
    {
        public float hunger = 0.3f;     // 0 = full, 1 = starving
        public float happiness = 0.6f;  // 0..1
        public bool bowlFilled;
        public bool collar;
        public int lastTreatDay;
        public int fullDay = -1;    // the day she was fed until full: she brings a bait the next day
        public int giftDay = -1;    // the last day she brought one
    }

    [Serializable]
    public class VocabEntry
    {
        public string hanzi;
        public string pinyin;
        public string meaning;
        public int heard;
        public int said;
        public int firstDay;
    }

    /// <summary>One line of the chat log (C), kept so the transcript survives between sessions.</summary>
    [Serializable]
    public class SavedLine
    {
        public string speaker;
        public string text;
        public bool fromPlayer;
    }

    [Serializable]
    public class SavedMessage
    {
        public string role;
        public string content;
    }

    /// <summary>A character's recent conversation, so Mei (and the keepers) remember you next time.</summary>
    [Serializable]
    public class AgentMemory
    {
        public string agent;
        public List<SavedMessage> messages = new List<SavedMessage>();
    }

    /// <summary>Friendship with one shopkeeper (see Progression.Affinity).</summary>
    [Serializable]
    public class KeeperState
    {
        public string shopId;
        public int points;
        public int talkDay = -1;
        public int talkPointsToday;
        public int lastGiftDay = -1;
        public List<string> facts = new List<string>();          // learned facts: "hometown", "like:茶", ...
        public List<string> recentLines = new List<string>();    // no points for repeating yourself
        public List<string> askedQuestions = new List<string>();
    }

    [Serializable]
    public class SaveData
    {
        public int version = 3;
        public string createdUtc = "";
        public string lastPlayedUtc = "";
        public double playSeconds;

        public int money = 50;
        public string equippedRod = "rod_old";
        public string selectedBait = "";
        public List<ItemStack> items = new List<ItemStack>();
        public List<BucketFish> bucket = new List<BucketFish>();
        public List<PlacedItem> placed = new List<PlacedItem>();
        public PetState pet = new PetState();
        public List<VocabEntry> vocab = new List<VocabEntry>();
        public bool visitedMarket;
        public List<CaughtRecord> journal = new List<CaughtRecord>();
        public float timeOfDay = 7.5f;
        public int day = 1;
        public int totalCatches;
        public List<string> companionNotes = new List<string>();

        // Stats (see Progression.PlayerStats): five levels each, trained with Coach Wu.
        public int statCast, statBar, statGrip, statLuck, statQuality, statBite;
        public int strength = 1; // old (lake) saves: becomes cast-distance levels
        public List<string> booksRead = new List<string>();
        public List<string> discoveredFish;
        public List<KeeperState> keepers = new List<KeeperState>();
        public float energy = -1f;   // -1 = full (a new game)
        public bool islandCamp;      // the camp on the island has been set up
        public string cosBoat = "", cosCat = "", cosMei = "", cosHouse = ""; // cosmetics being used

        // HSK lessons and tests at the test centre (see Progression.Hsk).
        public int hskLevel;                                   // highest HSK test passed (0-3)
        public List<string> lessonsDone = new List<string>();  // "level-lesson", e.g. "1-3"
        public int lastTestDay = -1;                           // one test a day
        public List<string> hskMissed = new List<string>();    // words answered wrong (come back in practice)

        // Where you were standing.
        public bool hasPlayerPos;
        public float playerX, playerY, playerZ, playerYaw;

        // What was said.
        public List<SavedLine> conversation = new List<SavedLine>();
        public List<AgentMemory> memories = new List<AgentMemory>();
    }

    /// <summary>Summary of a slot for the Saves menu (read without switching to it).</summary>
    public class SlotInfo
    {
        public int slot;
        public bool exists;
        public int day;
        public int money;
        public int words;
        public int catches;
        public double playSeconds;
        public DateTime lastPlayed;
    }

    public class RestorePoint
    {
        public string path;
        public string reason;
        public DateTime time;
        public int day;
        public int money;
        public int words;
    }

    /// <summary>
    /// Save slots in the user's persistent data folder:
    /// <code>
    /// settings.json                         preferences shared by all slots (+ which slot you last played)
    /// Saves/slot1.json (+ .bak)             the game itself; written atomically, previous copy kept as .bak
    /// Saves/RestorePoints/slot1_*.json      automatic snapshots (each session start and each new day)
    /// </code>
    /// The self-test uses its own "SelfTest" folder so it never touches real progress.
    /// </summary>
    public static class SaveSystem
    {
        public const int SlotCount = 3;
        private const int RestorePointsPerSlot = 12;

        private static SaveData _data;
        private static SettingsFile _settings;

        [Serializable]
        private class SettingsFile
        {
            public GameSettings settings = new GameSettings();
            public int lastSlot = 1;
        }

        // Only used to read the settings out of a pre-slot (v2) save file.
        [Serializable]
        private class LegacySave
        {
            public GameSettings settings;
        }

        public static SaveData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        public static GameSettings Settings
        {
            get
            {
                if (_settings == null) LoadSettings();
                return _settings.settings;
            }
        }

        public static int ActiveSlot
        {
            get
            {
                if (_settings == null) LoadSettings();
                return Mathf.Clamp(_settings.lastSlot, 1, SlotCount);
            }
        }

        /// <summary>Set when the slot file was unreadable and the previous copy (.bak) was used instead.</summary>
        public static string LoadWarning { get; private set; }

        public static event Action SettingsChanged;
        /// <summary>Raised just before the current game is written, so live objects can copy their state in.</summary>
        public static event Action Saving;

        /// <summary>The automated self-test uses its own profile so it never touches real progress.</summary>
        public static bool IsTestProfile => Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-selftest");

        public static string DataRoot => IsTestProfile ? Path.Combine(Application.persistentDataPath, "SelfTest") : Application.persistentDataPath;
        public static string SavesFolder => Path.Combine(DataRoot, "Saves");
        public static string RestoreFolder => Path.Combine(SavesFolder, "RestorePoints");
        private static string SettingsPath => Path.Combine(DataRoot, "settings.json");
        private static string LegacyPath => Path.Combine(Application.persistentDataPath, IsTestProfile ? "willowlake_selftest.json" : "willowlake_save.json");

        public static string SlotPath(int slot) => Path.Combine(SavesFolder, $"slot{slot}.json");
        public static string FilePath => SlotPath(ActiveSlot);

        // ------------------------------------------------------------------ settings

        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath)) _settings = JsonUtility.FromJson<SettingsFile>(File.ReadAllText(SettingsPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not read settings, using defaults: {e.Message}");
            }
            if (_settings == null)
            {
                _settings = new SettingsFile();
                // First run with slots: carry the old single save's settings over.
                try
                {
                    if (File.Exists(LegacyPath))
                    {
                        var legacy = JsonUtility.FromJson<LegacySave>(File.ReadAllText(LegacyPath));
                        if (legacy?.settings != null) _settings.settings = legacy.settings;
                    }
                }
                catch
                {
                    // Keep the defaults.
                }
            }
            _settings.settings ??= new GameSettings();
            // v1: days got longer (the old default was 60 real seconds per game hour; now 120).
            if (_settings.settings.settingsVersion < 1)
            {
                if (_settings.settings.realSecondsPerHour <= 60f) _settings.settings.realSecondsPerHour = 120f;
                _settings.settings.settingsVersion = 1;
            }
        }

        private static void SaveSettings()
        {
            if (_settings == null) return;
            WriteAtomic(SettingsPath, JsonUtility.ToJson(_settings, true));
        }

        public static void NotifySettingsChanged()
        {
            SettingsChanged?.Invoke();
            SaveSettings();
        }

        // ------------------------------------------------------------------ game data

        public static void Load()
        {
            MigrateLegacySave();
            _data = ReadSlot(ActiveSlot, out string warning);
            LoadWarning = warning;
            if (_data == null)
            {
                _data = NewGame();
                if (warning != null) Debug.LogWarning("[Save] " + warning);
            }
            Normalise(_data);
        }

        private static SaveData NewGame() => new SaveData { createdUtc = DateTime.UtcNow.ToString("o") };

        private static void Normalise(SaveData d)
        {
            d.journal ??= new List<CaughtRecord>();
            d.companionNotes ??= new List<string>();
            d.items ??= new List<ItemStack>();
            d.bucket ??= new List<BucketFish>();
            d.placed ??= new List<PlacedItem>();
            d.pet ??= new PetState();
            d.vocab ??= new List<VocabEntry>();
            d.conversation ??= new List<SavedLine>();
            d.memories ??= new List<AgentMemory>();
            if (string.IsNullOrEmpty(d.equippedRod)) d.equippedRod = "rod_old";
            UntitledGame.Progression.PlayerStats.Normalise(d);
            d.keepers ??= new List<KeeperState>();
            foreach (var k in d.keepers)
            {
                k.facts ??= new List<string>();
                k.recentLines ??= new List<string>();
                k.askedQuestions ??= new List<string>();
            }
            if (string.IsNullOrEmpty(d.createdUtc)) d.createdUtc = DateTime.UtcNow.ToString("o");
            d.version = 3;
        }

        /// <summary>
        /// Reads a slot. A damaged file is kept aside (never deleted) and the previous copy is used instead.
        /// Returns null if the slot is empty or nothing could be read.
        /// </summary>
        private static SaveData ReadSlot(int slot, out string warning)
        {
            warning = null;
            string path = SlotPath(slot);
            var data = TryRead(path, out bool damaged);
            if (data != null) return data;
            if (damaged)
            {
                string aside = path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(path, aside, true); } catch { /* best effort */ }
                data = TryRead(path + ".bak", out _);
                warning = data != null
                    ? $"Slot {slot} was damaged; loaded the previous save instead (the damaged file was kept as {Path.GetFileName(aside)})."
                    : $"Slot {slot} was damaged and has no backup; starting fresh (the damaged file was kept as {Path.GetFileName(aside)}).";
            }
            return data;
        }

        private static SaveData TryRead(string path, out bool damaged)
        {
            damaged = false;
            if (!File.Exists(path)) return null;
            try
            {
                string json = File.ReadAllText(path);
                var d = string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<SaveData>(json);
                if (d == null) damaged = true;
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not read {path}: {e.Message}");
                damaged = true;
                return null;
            }
        }

        /// <summary>Before save slots there was a single willowlake_save.json: it becomes slot 1 (the old file is left alone).</summary>
        private static void MigrateLegacySave()
        {
            try
            {
                if (File.Exists(SlotPath(1)) || !File.Exists(LegacyPath)) return;
                Directory.CreateDirectory(SavesFolder);
                File.Copy(LegacyPath, SlotPath(1));
                Debug.Log("[Save] Moved the old save into slot 1.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not migrate the old save: {e.Message}");
            }
        }

        public static void Save()
        {
            if (_data == null) return;
            try { Saving?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            _data.lastPlayedUtc = DateTime.UtcNow.ToString("o");
            WriteAtomic(FilePath, JsonUtility.ToJson(_data, true), keepBackup: true);
            SaveSettings();
        }

        /// <summary>Writes to a temp file first, then swaps it in, so a crash mid-write can't corrupt the save.</summary>
        private static void WriteAtomic(string path, string contents, bool keepBackup = false)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, contents);
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, keepBackup ? path + ".bak" : null);
                    }
                    catch (Exception)
                    {
                        // Some file systems don't support Replace; fall back to copy + move.
                        if (keepBackup) File.Copy(path, path + ".bak", true);
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not write {path}: {e.Message}");
            }
        }

        /// <summary>Starts over in the current slot (keeps settings). Takes a restore point first.</summary>
        public static void ResetProgress()
        {
            if (_data != null && File.Exists(FilePath)) MakeRestorePoint("before starting over");
            _data = NewGame();
            Normalise(_data);
            Save();
        }

        // ------------------------------------------------------------------ slots

        public static SlotInfo Describe(int slot)
        {
            var info = new SlotInfo { slot = slot };
            var d = slot == ActiveSlot && _data != null ? _data : TryRead(SlotPath(slot), out _);
            if (d == null) return info;
            info.exists = true;
            Fill(d, out info.day, out info.money, out info.words, out info.catches);
            info.playSeconds = d.playSeconds;
            info.lastPlayed = ParseUtc(d.lastPlayedUtc);
            return info;
        }

        private static void Fill(SaveData d, out int day, out int money, out int words, out int catches)
        {
            day = Mathf.Max(1, d.day);
            money = d.money;
            words = d.vocab?.Count ?? 0;
            catches = d.totalCatches;
        }

        private static DateTime ParseUtc(string s) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime() : DateTime.MinValue;

        /// <summary>Makes <paramref name="slot"/> the active game: loads it, or starts a new game there if it's empty (or <paramref name="fresh"/>).</summary>
        public static void SwitchTo(int slot, bool fresh)
        {
            slot = Mathf.Clamp(slot, 1, SlotCount);
            if (_settings == null) LoadSettings();
            if (fresh && File.Exists(SlotPath(slot)))
            {
                _settings.lastSlot = slot;
                MakeRestorePoint("before a new game");
            }
            _settings.lastSlot = slot;
            SaveSettings();
            if (fresh)
            {
                _data = NewGame();
                Normalise(_data);
                Save();
            }
            else Load();
        }

        /// <summary>"Save as": writes the current game into another slot and keeps playing there.</summary>
        public static void SaveInto(int slot)
        {
            slot = Mathf.Clamp(slot, 1, SlotCount);
            if (_settings == null) LoadSettings();
            if (slot == ActiveSlot)
            {
                Save();
                return;
            }
            try { Saving?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            if (File.Exists(SlotPath(slot)))
            {
                int keep = _settings.lastSlot;
                _settings.lastSlot = slot;
                MakeRestorePoint("before being overwritten");
                _settings.lastSlot = keep;
            }
            _settings.lastSlot = slot;
            SaveSettings();
            Save();
        }

        // ------------------------------------------------------------------ restore points

        /// <summary>Snapshots the active slot's file as it is on disk right now.</summary>
        public static void MakeRestorePoint(string reason)
        {
            try
            {
                string src = FilePath;
                if (!File.Exists(src)) return;
                Directory.CreateDirectory(RestoreFolder);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string safe = new string(reason.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
                File.Copy(src, Path.Combine(RestoreFolder, $"slot{ActiveSlot}_{stamp}_{safe}.json"), true);
                foreach (var old in RestorePoints(ActiveSlot).Skip(RestorePointsPerSlot))
                {
                    try { File.Delete(old.path); } catch { /* best effort */ }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not make a restore point: {e.Message}");
            }
        }

        /// <summary>Newest first.</summary>
        public static List<RestorePoint> RestorePoints(int slot)
        {
            var list = new List<RestorePoint>();
            if (!Directory.Exists(RestoreFolder)) return list;
            foreach (var path in Directory.GetFiles(RestoreFolder, $"slot{slot}_*.json"))
            {
                // slot1_2026-09-29_21-40-05_session-start.json
                string name = Path.GetFileNameWithoutExtension(path);
                string[] parts = name.Split('_');
                if (parts.Length < 3) continue;
                if (!DateTime.TryParseExact(parts[1] + "_" + parts[2], "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) continue;
                var rp = new RestorePoint { path = path, time = time, reason = parts.Length > 3 ? string.Join(" ", parts.Skip(3)).Replace('-', ' ') : "" };
                var d = TryRead(path, out _);
                if (d != null) Fill(d, out rp.day, out rp.money, out rp.words, out _);
                list.Add(rp);
            }
            return list.OrderByDescending(r => r.time).ToList();
        }

        /// <summary>Replaces the active slot with a restore point (the current state becomes a restore point itself).</summary>
        public static bool Restore(RestorePoint rp)
        {
            try
            {
                Save();
                MakeRestorePoint("before restoring");
                File.Copy(rp.path, FilePath, true);
                Load();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not restore {rp.path}: {e.Message}");
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _data = null;
            _settings = null;
            SettingsChanged = null;
            Saving = null;
            LoadWarning = null;
        }
    }
}
