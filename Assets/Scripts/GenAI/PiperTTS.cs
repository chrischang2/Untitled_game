using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// Keeps one Piper process alive per voice (model load is paid once) and feeds it JSON lines.
    /// Piper writes each utterance to a WAV file and prints the path; we load it back as an AudioClip.
    /// Call <see cref="Pump"/> from the main thread every frame.
    /// </summary>
    public class PiperTTS : IDisposable
    {
        private readonly string _exe;
        private readonly string _voice;
        private readonly string _outDir;
        private Process _process;
        private StreamWriter _stdin;
        private readonly ConcurrentQueue<string> _finished = new ConcurrentQueue<string>();
        private readonly List<Pending> _pending = new List<Pending>();
        private float _lengthScale = 1f;
        private int _counter;

        private class Pending
        {
            public string path;
            public Action<WavUtility.PcmData?> callback;
            public float queuedAt;
        }

        public string VoiceName => Path.GetFileNameWithoutExtension(_voice);
        public bool IsRunning => _process != null && !_process.HasExited;
        public int PendingCount => _pending.Count;

        public PiperTTS(string exe, string voiceModel, string outDir)
        {
            _exe = exe;
            _voice = voiceModel;
            _outDir = outDir;
        }

        public bool Start(float lengthScale, string logPath)
        {
            try
            {
                Directory.CreateDirectory(_outDir);
                _lengthScale = lengthScale;
                var psi = new ProcessStartInfo
                {
                    FileName = _exe,
                    Arguments = $"--model \"{_voice}\" --json-input --length_scale {_lengthScale.ToString(System.Globalization.CultureInfo.InvariantCulture)} --sentence_silence 0.12",
                    WorkingDirectory = Path.GetDirectoryName(_exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var log = ProcessLog.Open(logPath);
                _process.OutputDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    string line = e.Data.Trim();
                    if (line.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) _finished.Enqueue(line);
                    else log?.Write(line);
                };
                _process.ErrorDataReceived += (_, e) => { if (e.Data != null) log?.Write(e.Data); };
                _process.Start();
                WindowsJobObject.Assign(_process);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _stdin = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TTS] Failed to start Piper: {e.Message}");
                _process = null;
                return false;
            }
        }

        /// <summary>Queue text; callback receives decoded PCM (or null on failure) on the main thread.</summary>
        public void Synthesize(string text, Action<WavUtility.PcmData?> callback)
        {
            if (!IsRunning || string.IsNullOrWhiteSpace(text) || !HasSpeakable(text))
            {
                callback?.Invoke(null);
                return;
            }
            string path = Path.Combine(_outDir, $"{VoiceName}_{DateTime.Now.Ticks}_{_counter++}.wav").Replace('\\', '/');
            string json = "{\"text\":\"" + Escape(text) + "\",\"output_file\":\"" + Escape(path) + "\"}";
            try
            {
                _stdin.WriteLine(json);
                _pending.Add(new Pending { path = path, callback = callback, queuedAt = Time.realtimeSinceStartup });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TTS] Write failed: {e.Message}");
                callback?.Invoke(null);
            }
        }

        public void Pump()
        {
            while (_finished.TryDequeue(out string donePath))
            {
                if (_pending.Count == 0) continue;
                // Match by output path; anything queued before it evidently produced no audio.
                string norm = Normalize(donePath);
                int idx = _pending.FindIndex(p => Normalize(p.path) == norm);
                if (idx < 0) idx = 0;
                for (int i = 0; i < idx; i++) _pending[i].callback?.Invoke(null);
                var match = _pending[idx];
                _pending.RemoveRange(0, idx + 1);

                string file = File.Exists(donePath) ? donePath : match.path;
                WavUtility.PcmData? pcm = null;
                try
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    if (WavUtility.TryDecode(bytes, out var decoded)) pcm = decoded;
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TTS] Could not read {file}: {e.Message}");
                }
                match.callback?.Invoke(pcm);
            }

            // Safety: if Piper died or choked, fail stale requests instead of hanging the conversation.
            while (_pending.Count > 0 && (!IsRunning || Time.realtimeSinceStartup - _pending[0].queuedAt > 15f))
            {
                var stale = _pending[0];
                _pending.RemoveAt(0);
                stale.callback?.Invoke(null);
            }
        }

        private static string Normalize(string path) => path.Replace('\\', '/').Trim().ToLowerInvariant();

        private static bool HasSpeakable(string s)
        {
            foreach (char c in s) if (char.IsLetterOrDigit(c)) return true;
            return false;
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append(' '); break;
                    case '\r': break;
                    case '\t': sb.Append(' '); break;
                    default:
                        if (c < 0x20) break;
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            try
            {
                _stdin?.Close();
                if (_process != null && !_process.HasExited) _process.Kill();
            }
            catch
            {
                // Process may already be gone.
            }
            _process = null;
        }
    }

    /// <summary>Thread-safe append-only log file for child process output.</summary>
    public class ProcessLog
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();

        private ProcessLog(StreamWriter w) => _writer = w;

        public static ProcessLog Open(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var w = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                return new ProcessLog(w);
            }
            catch
            {
                return null;
            }
        }

        public void Write(string line)
        {
            lock (_lock)
            {
                try { _writer.WriteLine(line); } catch { /* ignore */ }
            }
        }
    }
}
