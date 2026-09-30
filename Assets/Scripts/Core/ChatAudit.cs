using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UntitledGame.Core
{
    /// <summary>
    /// A readable, per-session record of every conversation, for auditing and tracking down problems:
    /// what the microphone heard (with a WAV of each line), exactly what each character was sent
    /// (system prompt when it changes, game notes, the player's words), the raw and cleaned replies with
    /// timings, how shopkeepers understood each line, offers and sales, interruptions and errors.
    /// <code>
    /// ChatLogs/2026-09-29_21-40-05/chat.log
    /// ChatLogs/2026-09-29_21-40-05/audio/214012_to-Mei.wav
    /// </code>
    /// </summary>
    public static class ChatAudit
    {
        private const int KeepAudioDays = 30;
        private const int Indent = 22;

        private static readonly object Lock = new object();
        private static StreamWriter _writer;
        private static bool _failed;
        private static bool _hooked;

        public static string Folder => Path.Combine(SaveSystem.DataRoot, "ChatLogs");
        public static string SessionFolder { get; private set; }
        public static string SessionFile => SessionFolder != null ? Path.Combine(SessionFolder, "chat.log") : null;

        private static bool Open()
        {
            if (_writer != null) return true;
            if (_failed) return false;
            try
            {
                SessionFolder = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                Directory.CreateDirectory(SessionFolder);
                _writer = new StreamWriter(new FileStream(SessionFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                _writer.WriteLine($"Willow Lake chat log - session started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _writer.WriteLine($"Build {Application.version}, Unity {Application.unityVersion}, {(Application.isEditor ? "editor" : "player")}, {SystemInfo.operatingSystem}");
                _writer.WriteLine(new string('=', 100));
                PruneOldAudio();
                if (!_hooked)
                {
                    _hooked = true;
                    Application.logMessageReceivedThreaded += OnUnityLog;
                }
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                Debug.LogWarning($"[ChatAudit] Could not open the chat log: {e.Message}");
                return false;
            }
        }

        /// <summary>One event. <paramref name="who"/> is e.g. "Mei", "MIC→Old Wang", "Old Wang"; multi-line text is indented.</summary>
        public static void Write(string who, string what, string detail = null)
        {
            lock (Lock)
            {
                if (!Open()) return;
                try
                {
                    string head = $"{DateTime.Now:HH:mm:ss.f}  {Pad(who, 14)}";
                    _writer.WriteLine(head + what);
                    if (!string.IsNullOrEmpty(detail))
                    {
                        string pad = new string(' ', Indent);
                        foreach (var line in detail.Replace("\r", "").Split('\n')) _writer.WriteLine(pad + line);
                    }
                }
                catch
                {
                    // Never let logging break the game.
                }
            }
        }

        /// <summary>A section break, e.g. when a save slot is loaded.</summary>
        public static void Banner(string text)
        {
            lock (Lock)
            {
                if (!Open()) return;
                try
                {
                    _writer.WriteLine(new string('-', 100));
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss.f}  {text}");
                    _writer.WriteLine(new string('-', 100));
                }
                catch
                {
                    // Ignore.
                }
            }
        }

        /// <summary>Saves a mic recording next to the log (if enabled) and returns its relative path, or null.</summary>
        public static string SaveAudio(float[] samples, int sampleRate, string label)
        {
            if (samples == null || samples.Length == 0 || !SaveSystem.Settings.keepVoiceRecordings) return null;
            lock (Lock)
            {
                if (!Open()) return null;
            }
            try
            {
                string dir = Path.Combine(SessionFolder, "audio");
                Directory.CreateDirectory(dir);
                string safe = string.IsNullOrEmpty(label) ? "line" : label;
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                string name = $"{DateTime.Now:HHmmss-fff}_{safe.Replace(' ', '-')}.wav";
                File.WriteAllBytes(Path.Combine(dir, name), GenAI.WavUtility.EncodePcm16(samples, sampleRate));
                return "audio/" + name;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ChatAudit] Could not save audio: {e.Message}");
                return null;
            }
        }

        public static string Seconds(float s) => s < 0 ? "-" : s.ToString("0.00", CultureInfo.InvariantCulture) + " s";

        private static string Pad(string s, int n) => s.Length >= n ? s + " " : s + new string(' ', n - s.Length);

        // Unity errors and exceptions go into the log too, so a failure shows up next to what caused it.
        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            string trace = string.IsNullOrEmpty(stackTrace) ? null : string.Join("\n", stackTrace.Split('\n'), 0, Math.Min(6, stackTrace.Split('\n').Length)).TrimEnd();
            Write("ERROR", condition, trace);
        }

        private static void PruneOldAudio()
        {
            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-KeepAudioDays);
                foreach (var session in Directory.GetDirectories(Folder))
                {
                    string audio = Path.Combine(session, "audio");
                    if (Directory.Exists(audio) && Directory.GetCreationTime(session) < cutoff) Directory.Delete(audio, true);
                }
            }
            catch
            {
                // Best effort.
            }
        }

        public static void Close()
        {
            lock (Lock)
            {
                if (_writer == null) return;
                try
                {
                    _writer.WriteLine(new string('=', 100));
                    _writer.WriteLine($"Session ended {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    _writer.Dispose();
                }
                catch
                {
                    // Ignore.
                }
                _writer = null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Close();
            if (_hooked) Application.logMessageReceivedThreaded -= OnUnityLog;
            _hooked = false;
            _failed = false;
            SessionFolder = null;
        }
    }
}
