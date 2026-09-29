using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UntitledGame.Core
{
    public enum LanguageMode
    {
        English = 0,
        MandarinPractice = 1,
    }

    [Serializable]
    public class GameSettings
    {
        public float masterVolume = 1f;
        public float musicVolume = 0.55f;
        public float ambienceVolume = 0.8f;
        public float sfxVolume = 0.9f;
        public float voiceVolume = 1f;
        public string voiceName = "en_US-kristin-medium";
        public float voiceSpeed = 1f;
        public bool speakReplies = true;
        public bool companionChatter = true;
        public bool handsFree;
        public LanguageMode language = LanguageMode.English;
        public float realSecondsPerHour = 60f;
        public float mouseSensitivity = 1f;
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
    public class SaveData
    {
        public int version = 1;
        public GameSettings settings = new GameSettings();
        public List<CaughtRecord> journal = new List<CaughtRecord>();
        public float timeOfDay = 7.5f;
        public int day = 1;
        public int totalCatches;
        public List<string> companionNotes = new List<string>();
    }

    /// <summary>Tiny JSON save file in the user's persistent data folder.</summary>
    public static class SaveSystem
    {
        private static SaveData _data;
        public static SaveData Data
        {
            get
            {
                if (_data == null) Load();
                return _data;
            }
        }

        public static GameSettings Settings => Data.settings;
        public static event Action SettingsChanged;

        /// <summary>The automated self-test uses its own profile so it never touches real progress.</summary>
        public static bool IsTestProfile => Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-selftest");

        public static string FilePath => Path.Combine(Application.persistentDataPath, IsTestProfile ? "willowlake_selftest.json" : "willowlake_save.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    _data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not read save file, starting fresh: {e.Message}");
            }
            _data ??= new SaveData();
            _data.settings ??= new GameSettings();
            _data.journal ??= new List<CaughtRecord>();
            _data.companionNotes ??= new List<string>();
        }

        public static void Save()
        {
            if (_data == null) return;
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(_data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not write save file: {e.Message}");
            }
        }

        public static void NotifySettingsChanged()
        {
            SettingsChanged?.Invoke();
            Save();
        }

        public static void ResetProgress()
        {
            var settings = Data.settings;
            _data = new SaveData { settings = settings };
            Save();
        }
    }
}
