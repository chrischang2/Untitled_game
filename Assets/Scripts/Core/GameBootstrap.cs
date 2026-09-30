using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UntitledGame.CameraControl;
using UntitledGame.Companion;
using UntitledGame.Environment;
using UntitledGame.GenAI;
using UntitledGame.Player;

namespace UntitledGame.Core
{
    /// <summary>
    /// Loads the active save slot and puts the world back the way you left it (time of day, where you
    /// were standing, the chat log, what Mei and the shopkeepers remember). Copies the live state into
    /// the save on every write, autosaves, takes restore points, and switches slots by reloading the
    /// scene (the AI services survive the reload, so it only takes a moment).
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private DayNightCycle dayNight;
        [SerializeField] private float autosaveSeconds = 45f;

        private const int SavedChatLines = 150;

        public static GameBootstrap Instance { get; private set; }

        private static bool _sessionStarted;

        private PlayerController _player;
        private CameraRig _cam;
        private CompanionController _meiBody;
        private float _nextSave;
        private float _unsavedPlaySeconds;
        private int _lastDay;
        private bool _leaving;
        private LocalAIServices _services;

        private void Awake()
        {
            Instance = this;
            SaveSystem.Load();
            // A cozy game doesn't need 144 fps; leaving CPU/GPU headroom keeps Mei's AI snappy.
            double hz = Screen.currentResolution.refreshRateRatio.value;
            QualitySettings.vSyncCount = hz > 100 ? 2 : 1;
            Application.targetFrameRate = 60;
        }

        private void Start()
        {
            var data = SaveSystem.Data;
            if (dayNight == null) dayNight = DayNightCycle.Instance;
            if (dayNight != null)
            {
                dayNight.TimeOfDay = data.timeOfDay;
                dayNight.Day = Mathf.Max(1, data.day);
                dayNight.RealSecondsPerHour = SaveSystem.Settings.realSecondsPerHour;
                _lastDay = dayNight.Day;
            }

            _player = FindFirstObjectByType<PlayerController>();
            _cam = FindFirstObjectByType<CameraRig>();
            _meiBody = FindFirstObjectByType<CompanionController>();
            RestorePosition(data);

            RepairText(data);
            StartCoroutine(RefreshNotebookPinyin(data));
            ConversationLog.Import(data.conversation);
            foreach (var agent in Agents())
                agent.ImportMemory(data.memories.FirstOrDefault(m => m.agent == agent.MemoryKey));

            SaveSystem.Saving += CaptureLiveState;

            var st = SaveSystem.Settings;
            string summary = $"slot {SaveSystem.ActiveSlot}: day {data.day}, ¥{data.money}, {data.vocab.Count} words, {data.totalCatches} catches, " +
                             $"played {FormatDuration(data.playSeconds)} | Mei's English: {st.immersion}, pinyin: {st.pinyin}, voice recordings: {(st.keepVoiceRecordings ? "kept" : "off")}";
            if (!_sessionStarted)
            {
                _sessionStarted = true;
                ChatAudit.Banner("Playing " + summary);
                // A restore point of how things were before this session.
                SaveSystem.MakeRestorePoint("session start");
            }
            else ChatAudit.Banner("Loaded " + summary);

            if (SaveSystem.LoadWarning != null)
            {
                ChatAudit.Write("SAVE", SaveSystem.LoadWarning);
                Invoke(nameof(ShowLoadWarning), 2f);
            }

            _services = LocalAIServices.Instance;
            if (_services != null)
            {
                _services.StatusChanged += OnServicesChanged;
                OnServicesChanged();
            }
            _nextSave = Time.time + autosaveSeconds;
        }

        /// <summary>
        /// Older builds turned 么 into 幺 (a dictionary quirk), so saved lines could contain 什幺 / 怎幺.
        /// </summary>
        private static void RepairText(SaveData data)
        {
            int fixedCount = 0;
            string Fix(string s)
            {
                if (string.IsNullOrEmpty(s) || s.IndexOf('幺') < 0) return s;
                string r = s.Replace("什幺", "什么").Replace("怎幺", "怎么").Replace("这幺", "这么").Replace("那幺", "那么")
                    .Replace("多幺", "多么").Replace("要幺", "要么").Replace("为什幺", "为什么");
                if (r != s) fixedCount++;
                return r;
            }
            foreach (var l in data.conversation) l.text = Fix(l.text);
            foreach (var m in data.memories)
            foreach (var msg in m.messages) msg.content = Fix(msg.content);
            foreach (var v in data.vocab) v.hanzi = Fix(v.hanzi);
            if (fixedCount > 0) ChatAudit.Write("SAVE", $"repaired {fixedCount} saved lines with 幺 -> 么");
        }

        /// <summary>Re-derives the notebook's pinyin once the dictionary has loaded (older saves stored some wrong readings).</summary>
        private static System.Collections.IEnumerator RefreshNotebookPinyin(SaveData data)
        {
            while (!Language.Pinyin.Ready) yield return null;
            int changed = 0;
            foreach (var v in data.vocab)
            {
                string py = Language.Pinyin.Of(v.hanzi);
                if (!string.IsNullOrEmpty(py) && py != v.pinyin)
                {
                    ChatAudit.Write("NOTEBOOK", $"pinyin corrected: {v.hanzi} {v.pinyin} -> {py}");
                    v.pinyin = py;
                    changed++;
                }
            }
            if (changed > 0) SaveSystem.Save();
        }

        private void ShowLoadWarning() => GameEvents.Toast(SaveSystem.LoadWarning, 8f);

        private void OnDestroy()
        {
            SaveSystem.Saving -= CaptureLiveState;
            if (_services != null) _services.StatusChanged -= OnServicesChanged;
            if (Instance == this) Instance = null;
        }

        private static IEnumerable<DialogueAgent> Agents() => FindObjectsByType<DialogueAgent>(FindObjectsSortMode.None);

        private void RestorePosition(SaveData data)
        {
            if (!data.hasPlayerPos || _player == null) return;
            var p = new Vector3(data.playerX, data.playerY, data.playerZ);
            if (float.IsNaN(p.x) || float.IsNaN(p.z) || !PlayerController.IsWalkable(p))
            {
                ChatAudit.Write("SAVE", $"saved position ({p.x:0.0}, {p.z:0.0}) isn't walkable; starting at the dock instead");
                return;
            }
            p.y = Mathf.Max(p.y, WorldShape.TerrainHeight(p.x, p.z));
            _player.Teleport(p, data.playerYaw);
            ChatAudit.Write("SAVE", $"restored the player to ({p.x:0.0}, {p.y:0.0}, {p.z:0.0})");
            if (_cam != null) _cam.SetYaw(data.playerYaw);
            if (_meiBody != null) _meiBody.Warp();
        }

        private string _lastServiceLine;

        private void OnServicesChanged()
        {
            var s = _services;
            if (s == null) return;
            string line = $"LLM {s.LlmStatus} ({s.LlmModelName}), speech-to-text {s.SttStatus}, voices {s.TtsStatus}" +
                          (string.IsNullOrEmpty(s.LastError) ? "" : $" | last error: {s.LastError}");
            if (line == _lastServiceLine) return;
            _lastServiceLine = line;
            ChatAudit.Write("AI SERVICES", line);
        }

        /// <summary>Copies what only lives in the scene into the save data (runs before every write).</summary>
        private void CaptureLiveState()
        {
            if (_leaving) return;
            var data = SaveSystem.Data;
            if (dayNight != null)
            {
                data.timeOfDay = dayNight.TimeOfDay;
                data.day = dayNight.Day;
            }
            if (_player != null)
            {
                var t = _player.transform;
                data.hasPlayerPos = true;
                data.playerX = t.position.x;
                data.playerY = t.position.y;
                data.playerZ = t.position.z;
                data.playerYaw = t.eulerAngles.y;
            }
            data.conversation = ConversationLog.Export(SavedChatLines);
            data.memories = Agents().Select(a => a.ExportMemory()).Where(m => m.messages.Count > 0).ToList();
            data.playSeconds += _unsavedPlaySeconds;
            _unsavedPlaySeconds = 0f;
        }

        private void Update()
        {
            _unsavedPlaySeconds += Time.unscaledDeltaTime;

            // A restore point at the start of each new in-game day.
            if (dayNight != null && dayNight.Day != _lastDay)
            {
                _lastDay = dayNight.Day;
                SaveSystem.Save();
                SaveSystem.MakeRestorePoint($"day {_lastDay}");
            }

            if (Time.time < _nextSave) return;
            _nextSave = Time.time + autosaveSeconds;
            SaveSystem.Save();
        }

        private void OnApplicationQuit()
        {
            SaveSystem.Save();
            ChatAudit.Close();
        }

        // ------------------------------------------------------------------ slots (used by the Saves menu)

        /// <summary>Continue a slot, or start a new game in it (<paramref name="fresh"/>).</summary>
        public void LoadSlot(int slot, bool fresh)
        {
            SaveSystem.Save();
            Leave(() => SaveSystem.SwitchTo(slot, fresh), fresh ? $"New game in slot {slot}" : $"Switching to slot {slot}");
        }

        /// <summary>Go back to a restore point of the current slot.</summary>
        public void RestoreTo(RestorePoint rp)
        {
            SaveSystem.Save();
            Leave(() => SaveSystem.Restore(rp), $"Restoring slot {SaveSystem.ActiveSlot} to {rp.time:yyyy-MM-dd HH:mm} ({rp.reason})");
        }

        private void Leave(Action switchData, string why)
        {
            // From here on the old scene must not write its state over the newly loaded game.
            _leaving = true;
            SaveSystem.Saving -= CaptureLiveState;
            foreach (var agent in Agents()) agent.Interrupt();
            ChatAudit.Banner(why);
            switchData();
            InputGate.ClearAll();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public static string FormatDuration(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m";
        }
    }
}
