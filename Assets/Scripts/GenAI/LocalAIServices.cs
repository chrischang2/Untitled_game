using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace UntitledGame.GenAI
{
    public enum ServiceStatus { NotInstalled, Starting, Ready, Failed }

    /// <summary>
    /// Boots and babysits the local AI stack: llama-server (chat, out of process) and sherpa-onnx
    /// (Mandarin speech recognition + voices, in process). Everything runs on this machine.
    /// A llama-server already listening on the configured port is reused (handy in the editor).
    /// </summary>
    public class LocalAIServices : MonoBehaviour
    {
        public static LocalAIServices Instance { get; private set; }

        public string Root { get; private set; }
        public LocalAIConfig Config { get; private set; }
        public SpeechEngine Speech { get; private set; }

        public ServiceStatus LlmStatus { get; private set; } = ServiceStatus.NotInstalled;
        public ServiceStatus SttStatus { get; private set; } = ServiceStatus.NotInstalled;
        public ServiceStatus TtsStatus { get; private set; } = ServiceStatus.NotInstalled;
        public string LastError { get; private set; }

        public string LlmBaseUrl => string.IsNullOrEmpty(Config?.externalLlmUrl)
            ? $"http://127.0.0.1:{Config?.llmPort ?? 8765}/v1"
            : Config.externalLlmUrl;
        public string LlmModelName => string.IsNullOrEmpty(Config?.externalLlmUrl)
            ? Path.GetFileNameWithoutExtension(Config?.llmModel ?? "local")
            : Config.externalLlmModel;

        public event Action StatusChanged;

        private Process _llm;
        private string LogDir => Root != null ? Path.Combine(Root, "logs") : Application.temporaryCachePath;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // Survives scene reloads (loading a save slot), so the model and voices stay loaded.
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            Root = LocalAIConfig.FindRoot();
            Config = LocalAIConfig.Load(Root);
            if (Root == null) LastError = "LocalAI folder not found. Run Tools/setup-local-ai.ps1 to install the voice + AI stack.";
        }

        private void Start()
        {
            StartCoroutine(BringUpLlm());
            BringUpSpeech();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update() => Speech?.Pump();

        // ------------------------------------------------------------------ LLM

        private IEnumerator BringUpLlm()
        {
            if (!string.IsNullOrEmpty(Config.externalLlmUrl))
            {
                LlmStatus = ServiceStatus.Starting;
                StatusChanged?.Invoke();
                yield return Probe(Config.externalLlmUrl.TrimEnd('/') + "/models", ok =>
                {
                    LlmStatus = ok ? ServiceStatus.Ready : ServiceStatus.Failed;
                    if (!ok) LastError = $"Could not reach external LLM at {Config.externalLlmUrl}";
                });
                StatusChanged?.Invoke();
                yield break;
            }

            string healthUrl = $"http://127.0.0.1:{Config.llmPort}/health";
            bool alreadyUp = false;
            yield return Probe(healthUrl, ok => alreadyUp = ok);
            if (alreadyUp)
            {
                Debug.Log("[LocalAI] Reusing llama-server already running.");
                LlmStatus = ServiceStatus.Ready;
                StatusChanged?.Invoke();
                yield break;
            }

            string exe = Root != null ? Path.Combine(Root, "llama", "llama-server.exe") : null;
            string model = Config.Resolve(Root, Config.llmModel);
            if (exe == null || !File.Exists(exe) || model == null || !File.Exists(model))
            {
                LlmStatus = ServiceStatus.NotInstalled;
                StatusChanged?.Invoke();
                yield break;
            }

            int slots = Mathf.Max(1, Config.llmSlots);
            var args = new StringBuilder();
            args.Append($"-m \"{model}\" --host 127.0.0.1 --port {Config.llmPort} -c {Config.contextSize * slots} -np {slots} ");
            args.Append("--reasoning off --no-webui --jinja ");
            if (Config.gpuLayers >= 0) args.Append($"-ngl {Config.gpuLayers} ");
            if (Config.llmThreads > 0) args.Append($"-t {Config.llmThreads} -tb {Config.llmThreads} ");
            if (!string.IsNullOrWhiteSpace(Config.extraLlmArgs)) args.Append(Config.extraLlmArgs).Append(' ');
            _llm = Launch(exe, args.ToString(), Path.Combine(LogDir, "llama-server.log"));
            if (_llm == null)
            {
                LlmStatus = ServiceStatus.Failed;
                StatusChanged?.Invoke();
                yield break;
            }

            LlmStatus = ServiceStatus.Starting;
            StatusChanged?.Invoke();
            float deadline = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_llm.HasExited)
                {
                    LastError = "The language model server stopped unexpectedly (see LocalAI/logs/llama-server.log).";
                    LlmStatus = ServiceStatus.Failed;
                    StatusChanged?.Invoke();
                    yield break;
                }
                bool ok = false;
                yield return Probe(healthUrl, r => ok = r);
                if (ok)
                {
                    LlmStatus = ServiceStatus.Ready;
                    StatusChanged?.Invoke();
                    Debug.Log("[LocalAI] llama-server ready.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            LastError = "The language model took too long to load.";
            LlmStatus = ServiceStatus.Failed;
            StatusChanged?.Invoke();
        }

        // ------------------------------------------------------------------ speech

        private void BringUpSpeech()
        {
            if (Root == null) return;
            string sherpa = Config.Resolve(Root, Config.sherpaDir);
            string asr = Config.Resolve(Root, Config.asrModel);
            if (!Directory.Exists(sherpa) || !Directory.Exists(asr))
            {
                SttStatus = TtsStatus = ServiceStatus.NotInstalled;
                StatusChanged?.Invoke();
                return;
            }

            int th = Mathf.Max(1, Config.speechThreads);
            var voices = new List<SpeechEngine.VoiceSpec>();
            void Add(Func<SpeechEngine.VoiceSpec> make, string dir)
            {
                if (dir == null || !Directory.Exists(dir)) return;
                try { voices.Add(make()); }
                catch (Exception e) { Debug.LogWarning($"[LocalAI] Voice config failed for {dir}: {e.Message}"); }
            }
            string mei = Config.Resolve(Root, Config.voiceMei);
            Add(() => SpeechEngine.Matcha(SpeechEngine.VoiceMei, mei, Config.Resolve(Root, Config.vocoder), th), mei);
            string en = Config.Resolve(Root, Config.voiceEnglish);
            Add(() => SpeechEngine.Piper(SpeechEngine.VoiceEnglish, en, th), en);
            string male = Config.Resolve(Root, Config.voiceMale);
            Add(() => SpeechEngine.Piper(SpeechEngine.VoiceMale, male, th), male);
            string female = Config.Resolve(Root, Config.voiceFemale);
            Add(() => SpeechEngine.Piper(SpeechEngine.VoiceFemale, female, th), female);

            Speech = new SpeechEngine();
            SttStatus = TtsStatus = ServiceStatus.Starting;
            StatusChanged?.Invoke();
            float t0 = Time.realtimeSinceStartup;
            Speech.Load(sherpa, asr, voices, th, () =>
            {
                SttStatus = Speech.AsrReady ? ServiceStatus.Ready : ServiceStatus.Failed;
                TtsStatus = Speech.TtsReady ? ServiceStatus.Ready : ServiceStatus.Failed;
                if (Speech.Error != null) LastError = Speech.Error;
                Debug.Log($"[LocalAI] Speech ready in {Time.realtimeSinceStartup - t0:0.0}s (ASR {SttStatus}, TTS {TtsStatus}).");
                StatusChanged?.Invoke();
            });
        }

        // ------------------------------------------------------------------ helpers

        private Process Launch(string exe, string args, string logPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                var p = new Process { StartInfo = psi };
                var log = ProcessLog.Open(logPath);
                p.OutputDataReceived += (_, e) => { if (e.Data != null) log?.Write(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) log?.Write(e.Data); };
                p.Start();
                WindowsJobObject.Assign(p);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                Debug.Log($"[LocalAI] Started {Path.GetFileName(exe)} {args}");
                return p;
            }
            catch (Exception e)
            {
                LastError = $"Could not start {Path.GetFileName(exe)}: {e.Message}";
                Debug.LogWarning("[LocalAI] " + LastError);
                return null;
            }
        }

        private static IEnumerator Probe(string url, Action<bool> result)
        {
            using var req = UnityWebRequest.Get(url);
            req.timeout = 2;
            yield return req.SendWebRequest();
            result(req.result == UnityWebRequest.Result.Success && req.responseCode < 400);
        }

        private void OnApplicationQuit() => Shutdown(false);

        public void Shutdown(bool force)
        {
            Speech?.Dispose();
            Speech = null;
            bool keep = Application.isEditor && Config != null && Config.keepServersRunningInEditor && !force;
            if (keep) return;
            try
            {
                if (_llm != null && !_llm.HasExited) _llm.Kill();
            }
            catch
            {
                // Already gone.
            }
            _llm = null;
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
