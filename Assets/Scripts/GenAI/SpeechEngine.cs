using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SherpaOnnx;
using Debug = UnityEngine.Debug;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// Loads sherpa-onnx's native library from the LocalAI folder (kept out of Assets so the repo stays small).
    /// Once loaded by full path, [DllImport("sherpa-onnx-c-api")] resolves to the same module.
    /// </summary>
    public static class SherpaNative
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string path);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        private static bool _loaded;

        public static bool TryLoad(string dir, out string error)
        {
            error = null;
            if (_loaded) return true;
            string api = Path.Combine(dir, "sherpa-onnx-c-api.dll");
            if (!File.Exists(api))
            {
                error = $"sherpa-onnx not found in {dir}";
                return false;
            }
            SetDllDirectory(dir);
            foreach (var dll in new[] { "onnxruntime.dll", "onnxruntime_providers_shared.dll", "sherpa-onnx-c-api.dll" })
            {
                string p = Path.Combine(dir, dll);
                if (File.Exists(p) && LoadLibrary(p) == IntPtr.Zero)
                {
                    error = $"LoadLibrary({dll}) failed: {Marshal.GetLastWin32Error()}";
                    return false;
                }
            }
            _loaded = true;
            return true;
        }
    }

    /// <summary>Lets a speaker cancel its queued synthesis jobs (e.g. when interrupted).</summary>
    public class SpeechTicket
    {
        public volatile bool Cancelled;
    }

    /// <summary>
    /// In-process speech: SenseVoice recognition (pinned to Mandarin) and several sherpa-onnx TTS voices.
    /// Recognition and synthesis each run on their own worker thread so a long reply being voiced never
    /// delays hearing the player. Results are delivered on Unity's main thread via <see cref="Pump"/>.
    /// </summary>
    public class SpeechEngine : IDisposable
    {
        public const string VoiceMei = "mei";          // Matcha zh-en: Mei's Mandarin
        public const string VoiceEnglish = "en";       // Piper Kristin: Mei's English asides
        public const string VoiceMale = "zh_male";     // Piper chaowen
        public const string VoiceFemale = "zh_female"; // Piper xiao_ya

        public class VoiceSpec
        {
            public string id;
            public OfflineTtsConfig config;
        }

        private OfflineRecognizer _asr;
        private readonly Dictionary<string, OfflineTts> _voices = new Dictionary<string, OfflineTts>();
        private readonly Dictionary<string, VoiceSpec> _specs = new Dictionary<string, VoiceSpec>();
        private readonly Worker _asrWorker = new Worker("ASR");
        private readonly Worker _ttsWorker = new Worker("TTS");
        private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();

        public bool AsrReady { get; private set; }
        public bool TtsReady { get; private set; }
        public string Error { get; private set; }

        public void Pump()
        {
            while (_mainThread.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Loads models in the background; <paramref name="onDone"/> fires on the main thread.</summary>
        public void Load(string sherpaDir, string asrModelDir, List<VoiceSpec> voices, int threads, Action onDone)
        {
            foreach (var v in voices) _specs[v.id] = v;
            _asrWorker.Post(() =>
            {
                try
                {
                    if (!SherpaNative.TryLoad(sherpaDir, out string err)) throw new Exception(err);
                    var cfg = OfflineRecognizerConfig.Default();
                    cfg.ModelConfig.SenseVoice.Model = Path.Combine(asrModelDir, "model.int8.onnx");
                    cfg.ModelConfig.SenseVoice.Language = "zh"; // pinned to Mandarin
                    cfg.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
                    cfg.ModelConfig.Tokens = Path.Combine(asrModelDir, "tokens.txt");
                    cfg.ModelConfig.NumThreads = threads;
                    cfg.ModelConfig.Provider = "cpu";
                    cfg.ModelConfig.Debug = 0;
                    _asr = new OfflineRecognizer(cfg);
                    AsrReady = true;
                }
                catch (Exception e)
                {
                    Error = "Speech recognition failed to load: " + e.Message;
                    Debug.LogWarning("[Speech] " + Error);
                }

                // Voices load on the TTS thread (Mei first so she's ready soonest).
                _ttsWorker.Post(() =>
                {
                    foreach (var v in voices)
                    {
                        try { _voices[v.id] = new OfflineTts(v.config); }
                        catch (Exception e) { Debug.LogWarning($"[Speech] Voice {v.id} failed to load: {e.Message}"); }
                    }
                    TtsReady = _voices.Count > 0;
                    _mainThread.Enqueue(onDone);
                });
            });
        }

        public bool HasVoice(string id) => _voices.ContainsKey(id);

        /// <summary>Transcribes 16 kHz mono audio. Callback gets (text, null) or (null, error).</summary>
        public void Transcribe(float[] samples16k, Action<string, string> callback)
        {
            if (!AsrReady)
            {
                callback?.Invoke(null, "speech recognition not ready");
                return;
            }
            _asrWorker.Post(() =>
            {
                string text = null, error = null;
                try
                {
                    using var stream = _asr.CreateStream();
                    stream.AcceptWaveform(16000, samples16k);
                    _asr.Decode(stream);
                    text = stream.Result.Text;
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                _mainThread.Enqueue(() => callback?.Invoke(text, error));
            });
        }

        /// <summary>Synthesises text with a voice; callback gets PCM (or null) on the main thread.</summary>
        public void Synthesize(string voiceId, string text, float speed, SpeechTicket ticket, Action<WavUtility.PcmData?> callback)
        {
            if (!_voices.ContainsKey(voiceId) || string.IsNullOrWhiteSpace(text))
            {
                callback?.Invoke(null);
                return;
            }
            _ttsWorker.Post(() =>
            {
                WavUtility.PcmData? pcm = null;
                if (ticket == null || !ticket.Cancelled)
                {
                    try
                    {
                        var audio = _voices[voiceId].Generate(text, speed, 0);
                        var samples = audio.Samples;
                        int rate = audio.SampleRate;
                        audio.Dispose();
                        if (samples.Length > 0) pcm = new WavUtility.PcmData { samples = samples, sampleRate = rate, channels = 1 };
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Speech] TTS failed ({voiceId}): {e.Message}");
                    }
                }
                _mainThread.Enqueue(() => callback?.Invoke(pcm));
            });
        }

        public void Dispose()
        {
            _asrWorker.Dispose();
            _ttsWorker.Dispose();
            _asr?.Dispose();
            foreach (var v in _voices.Values) v.Dispose();
            _voices.Clear();
        }

        // ------------------------------------------------------------------ voice presets

        public static VoiceSpec Matcha(string id, string dir, string vocoder, int threads)
        {
            var c = OfflineTtsConfig.Default();
            c.Model.Matcha.AcousticModel = Path.Combine(dir, "model-steps-3.onnx");
            c.Model.Matcha.Vocoder = vocoder;
            c.Model.Matcha.Lexicon = Path.Combine(dir, "lexicon.txt");
            c.Model.Matcha.Tokens = Path.Combine(dir, "tokens.txt");
            c.Model.Matcha.DataDir = Path.Combine(dir, "espeak-ng-data");
            c.Model.NumThreads = threads;
            c.Model.Provider = "cpu";
            c.RuleFsts = JoinExisting(dir, "date-zh.fst", "phone-zh.fst", "number-zh.fst");
            c.MaxNumSentences = 1;
            return new VoiceSpec { id = id, config = c };
        }

        public static VoiceSpec Piper(string id, string dir, int threads)
        {
            var c = OfflineTtsConfig.Default();
            string onnx = Directory.GetFiles(dir, "*.onnx")[0];
            c.Model.Vits.Model = onnx;
            c.Model.Vits.Tokens = Path.Combine(dir, "tokens.txt");
            string lexicon = Path.Combine(dir, "lexicon.txt");
            if (File.Exists(lexicon)) c.Model.Vits.Lexicon = lexicon;
            string espeak = Path.Combine(dir, "espeak-ng-data");
            if (Directory.Exists(espeak)) c.Model.Vits.DataDir = espeak;
            c.Model.NumThreads = threads;
            c.Model.Provider = "cpu";
            c.RuleFsts = JoinExisting(dir, "date.fst", "phone.fst", "number.fst");
            c.MaxNumSentences = 1;
            return new VoiceSpec { id = id, config = c };
        }

        private static string JoinExisting(string dir, params string[] files)
        {
            var list = new List<string>();
            foreach (var f in files)
            {
                string p = Path.Combine(dir, f);
                if (File.Exists(p)) list.Add(p);
            }
            return string.Join(",", list);
        }

        /// <summary>Single background thread with a job queue.</summary>
        private class Worker : IDisposable
        {
            private readonly BlockingCollection<Action> _jobs = new BlockingCollection<Action>();
            private readonly Thread _thread;

            public Worker(string name)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Speech-" + name };
                _thread.Start();
            }

            public void Post(Action a)
            {
                if (!_jobs.IsAddingCompleted) _jobs.Add(a);
            }

            private void Run()
            {
                foreach (var job in _jobs.GetConsumingEnumerable())
                {
                    try { job(); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }

            public void Dispose()
            {
                _jobs.CompleteAdding();
            }
        }
    }
}
