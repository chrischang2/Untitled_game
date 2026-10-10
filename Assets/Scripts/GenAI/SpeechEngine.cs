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
        public const string VoiceMei = "mei";          // Matcha zh-en: Mei's Mandarin ("classic" voice)
        public const string VoiceEnglish = "en";       // Piper Kristin: Mei's English asides (classic voice)
        public const string VoiceMale = "zh_male";     // Piper chaowen
        public const string VoiceFemale = "zh_female"; // Piper xiao_ya
        public const string VoiceNatural = "natural";  // Kokoro v1.1-zh: 100+ speakers, Mandarin + English

        /// <summary>A voice id for one speaker of a multi-speaker voice: "natural:22".</summary>
        public static string Speaker(string voice, int speakerId) => $"{voice}:{speakerId}";

        public class VoiceSpec
        {
            public string id;
            public OfflineTtsConfig config;
            /// <summary>Slow voices get their own thread so they never hold up the quick shopkeeper voices.</summary>
            public bool ownThread;
        }

        private OfflineRecognizer _asr;
        private OfflineRecognizer _qwen;

        /// <summary>Qwen3-ASR 0.6B: better than SenseVoice at English (and mixed) lines, worse at learners' Mandarin, ~8x slower.</summary>
        public bool QwenReady { get; private set; }

        /// <summary>Which recogniser produced the last result, for the chat log (written on the ASR thread).</summary>
        public volatile string LastEngine = "";

        public enum AsrMode { Auto = 0, SenseVoice = 1, Qwen = 2 }
        private readonly ConcurrentDictionary<string, OfflineTts> _voices = new ConcurrentDictionary<string, OfflineTts>();
        private readonly ConcurrentDictionary<string, Worker> _voiceWorkers = new ConcurrentDictionary<string, Worker>();
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

                // Slow voices load (and later speak) on their own thread, in parallel with the rest.
                foreach (var v in voices)
                {
                    if (!v.ownThread) continue;
                    var spec = v;
                    var worker = _voiceWorkers.GetOrAdd(spec.id, id => new Worker("TTS-" + id));
                    worker.Post(() =>
                    {
                        try
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            _voices[spec.id] = new OfflineTts(spec.config);
                            Debug.Log($"[Speech] Voice {spec.id} loaded in {sw.Elapsed.TotalSeconds:0.0}s");
                        }
                        catch (Exception e) { Debug.LogWarning($"[Speech] Voice {spec.id} failed to load: {e.Message}"); }
                    });
                }

                // The other voices load on the shared TTS thread (Mei first so she's ready soonest).
                _ttsWorker.Post(() =>
                {
                    foreach (var v in voices)
                    {
                        if (v.ownThread) continue;
                        try { _voices[v.id] = new OfflineTts(v.config); }
                        catch (Exception e) { Debug.LogWarning($"[Speech] Voice {v.id} failed to load: {e.Message}"); }
                    }
                    TtsReady = _voices.Count > 0;
                    _mainThread.Enqueue(onDone);
                });
            });
        }

        /// <summary>True once the voice (or the multi-speaker voice behind a "natural:22" id) has loaded.</summary>
        public bool HasVoice(string id) => _voices.ContainsKey(SplitSpeaker(id, out _));

        /// <summary>Configured (it may still be loading).</summary>
        public bool HasVoiceConfigured(string id) => _specs.ContainsKey(SplitSpeaker(id, out _));

        private static string SplitSpeaker(string id, out int speaker)
        {
            speaker = 0;
            if (string.IsNullOrEmpty(id)) return id;
            int colon = id.IndexOf(':');
            if (colon < 0) return id;
            int.TryParse(id.Substring(colon + 1), out speaker);
            return id.Substring(0, colon);
        }

        /// <summary>Loads Qwen3-ASR on the recognition thread (after SenseVoice, so it never delays the first words).</summary>
        public void LoadQwen(string dir, int threads)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            _asrWorker.Post(() =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var cfg = OfflineRecognizerConfig.Default();
                    cfg.ModelConfig.Qwen3Asr.ConvFrontend = Path.Combine(dir, "conv_frontend.onnx");
                    cfg.ModelConfig.Qwen3Asr.Encoder = Path.Combine(dir, "encoder.int8.onnx");
                    cfg.ModelConfig.Qwen3Asr.Decoder = Path.Combine(dir, "decoder.int8.onnx");
                    cfg.ModelConfig.Qwen3Asr.Tokenizer = Path.Combine(dir, "tokenizer");
                    cfg.ModelConfig.NumThreads = threads;
                    cfg.ModelConfig.Provider = "cpu";
                    cfg.ModelConfig.Debug = 0;
                    _qwen = new OfflineRecognizer(cfg);
                    QwenReady = true;
                    Debug.Log($"[Speech] Qwen3-ASR loaded in {sw.Elapsed.TotalSeconds:0.0}s");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Speech] Qwen3-ASR failed to load: " + e.Message);
                }
            });
        }

        private SenseVoiceCtc _ctc;
        /// <summary>The second-best reading of the last recording ("did you mean ...?"), or null.</summary>
        public string LastAlternative { get; private set; }
        /// <summary>How likely the last recording was the expected word, against the best reading (0 = just as likely); null if unknown.</summary>
        public float? LastMargin { get; private set; }
        /// <summary>The expected word is among the model's top three guesses at each of its characters (null if unknown).</summary>
        public bool? LastTopMatch { get; private set; }
        /// <summary>Of the spoken options passed in (a stall's requests), the one the audio fits best, and how well (null if none given).</summary>
        public string LastOption { get; private set; }
        public float LastOptionMargin { get; private set; } = -100f;

        /// <summary>Loads SenseVoice a second time, directly on onnxruntime, to read its runner-up guesses.</summary>
        public void LoadAlternatives(string asrModelDir, int threads)
        {
            _asrWorker.Post(() =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _ctc = new SenseVoiceCtc(asrModelDir, threads);
                    Debug.Log($"[Speech] Alternative-reading model loaded in {sw.Elapsed.TotalSeconds:0.0}s");
                }
                catch (Exception e)
                {
                    _ctc = null;
                    Debug.LogWarning("[Speech] Alternative readings unavailable: " + e.Message);
                }
            });
        }

        /// <summary>Synchronous (call on the recognition thread or in tests): the model's best guess and runner-up.</summary>
        public SenseVoiceCtc.Analysis AnalyzeNow(float[] samples16k, string expectedWord = null)
        {
            if (_ctc == null) return null;
            _ctc.PrepareSounds();
            return _ctc.Analyze(samples16k, string.IsNullOrEmpty(expectedWord) ? null : _ctc.ClassesFor(expectedWord));
        }

        /// <summary>Synchronous (tests): how well the audio fits each phrase (0 = as well as the best reading).</summary>
        public float[] ScoreOptionsNow(float[] samples16k, IReadOnlyList<string> options)
        {
            if (_ctc == null) return null;
            _ctc.PrepareSounds();
            var classes = new List<int[][]>();
            foreach (var o in options) classes.Add(_ctc.ClassesFor(o));
            return _ctc.Analyze(samples16k, null, classes)?.optionMargins;
        }
        public bool AlternativesReady => _ctc != null;

        private static string Decode(OfflineRecognizer rec, float[] samples16k)
        {
            using var stream = rec.CreateStream();
            stream.AcceptWaveform(16000, samples16k);
            rec.Decode(stream);
            return stream.Result.Text;
        }

        private static bool HasEnglish(string text)
        {
            int latin = 0;
            foreach (char c in text ?? "") if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
            return latin >= 3;
        }

        /// <summary>Qwen3 sometimes runs on (repeating, or reciting) far past what the audio could hold.</summary>
        private static bool LooksRunaway(string text, float seconds) =>
            string.IsNullOrWhiteSpace(text) || text.Length > seconds * 9f + 8f;

        /// <summary>
        /// Transcribes 16 kHz mono audio. Callback gets (text, null) or (null, error).
        /// Auto: SenseVoice (fast, best for learners' Mandarin); lines with English in them are re-heard by Qwen3-ASR,
        /// which handles English and mixed sentences much better. Tested on the player's own recordings.
        /// </summary>
        public void Transcribe(float[] samples16k, Action<string, string> callback, AsrMode mode = AsrMode.Auto, string expectedWord = null,
                               IReadOnlyList<string> options = null)
        {
            if (!AsrReady)
            {
                callback?.Invoke(null, "speech recognition not ready");
                return;
            }
            int[][] expectedClasses = null;
            if (_ctc != null)
            {
                _ctc.PrepareSounds();
                if (!string.IsNullOrEmpty(expectedWord)) expectedClasses = _ctc.ClassesFor(expectedWord);
            }
            List<int[][]> optionClasses = null;
            if (_ctc != null && options != null && options.Count > 0)
            {
                optionClasses = new List<int[][]>(options.Count);
                foreach (var o in options) optionClasses.Add(_ctc.ClassesFor(o));
            }
            _asrWorker.Post(() =>
            {
                string text = null, error = null, alt = null;
                float? margin = null;
                float[] optionMargins = null;
                bool? topMatch = null;
                float seconds = samples16k.Length / 16000f;
                try
                {
                    try
                    {
                        if (_ctc != null && mode != AsrMode.Qwen)
                        {
                            var a = _ctc.Analyze(samples16k, expectedClasses, optionClasses);
                            optionMargins = a?.optionMargins;
                            alt = a?.alternative;
                            margin = a?.margin;
                            topMatch = a?.topMatch;
                        }
                    }
                    catch (Exception e) { Debug.LogWarning("[Speech] alternative reading failed: " + e.Message); }
                    if (mode == AsrMode.Qwen && QwenReady)
                    {
                        text = Decode(_qwen, samples16k);
                        LastEngine = "Qwen3-ASR";
                        if (LooksRunaway(text, seconds))
                        {
                            text = Decode(_asr, samples16k);
                            LastEngine = "SenseVoice (Qwen3 ran away)";
                        }
                    }
                    else
                    {
                        text = Decode(_asr, samples16k);
                        LastEngine = "SenseVoice";
                        if (mode == AsrMode.Auto && QwenReady && HasEnglish(text))
                        {
                            string q = Decode(_qwen, samples16k);
                            if (!LooksRunaway(q, seconds))
                            {
                                LastEngine = $"Qwen3-ASR (English; SenseVoice heard \"{text}\")";
                                text = q;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                _mainThread.Enqueue(() =>
                {
                    LastAlternative = alt;
                    LastMargin = margin;
                    LastOption = null;
                    LastOptionMargin = -100f;
                    if (optionMargins != null && options != null)
                        for (int i = 0; i < optionMargins.Length && i < options.Count; i++)
                            if (optionMargins[i] > LastOptionMargin) { LastOptionMargin = optionMargins[i]; LastOption = options[i]; }
                    LastTopMatch = topMatch;
                    callback?.Invoke(text, error);
                });
            });
        }

        /// <summary>Synthesises text with a voice; callback gets PCM (or null) on the main thread.</summary>
        public void Synthesize(string voiceId, string text, float speed, SpeechTicket ticket, Action<WavUtility.PcmData?> callback)
        {
            string baseId = SplitSpeaker(voiceId, out int speaker);
            if (!_voices.TryGetValue(baseId, out var tts) || string.IsNullOrWhiteSpace(text))
            {
                callback?.Invoke(null);
                return;
            }
            var worker = _voiceWorkers.TryGetValue(baseId, out var own) ? own : _ttsWorker;
            worker.Post(() =>
            {
                WavUtility.PcmData? pcm = null;
                if (ticket == null || !ticket.Cancelled)
                {
                    try
                    {
                        var audio = tts.Generate(text, speed, speaker);
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
            foreach (var w in _voiceWorkers.Values) w.Dispose();
            _asr?.Dispose();
            _qwen?.Dispose();
            _ctc?.Dispose();
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

        /// <summary>
        /// Kokoro v1.1-zh: much more natural than Matcha/Piper, but about 0.6x real time on this CPU, so it
        /// gets its own thread and the caller feeds it short chunks (see <c>CharacterVoice</c>).
        /// </summary>
        public static VoiceSpec Kokoro(string id, string dir, int threads)
        {
            var c = OfflineTtsConfig.Default();
            c.Model.Kokoro.Model = Path.Combine(dir, "model.onnx");
            c.Model.Kokoro.Voices = Path.Combine(dir, "voices.bin");
            c.Model.Kokoro.Tokens = Path.Combine(dir, "tokens.txt");
            c.Model.Kokoro.DataDir = Path.Combine(dir, "espeak-ng-data");
            c.Model.Kokoro.DictDir = Path.Combine(dir, "dict");
            c.Model.Kokoro.Lexicon = Path.Combine(dir, "lexicon-us-en.txt") + "," + Path.Combine(dir, "lexicon-zh.txt");
            c.Model.NumThreads = threads;
            c.Model.Provider = "cpu";
            c.RuleFsts = JoinExisting(dir, "date-zh.fst", "phone-zh.fst", "number-zh.fst");
            c.MaxNumSentences = 1;
            return new VoiceSpec { id = id, config = c, ownThread = true };
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
