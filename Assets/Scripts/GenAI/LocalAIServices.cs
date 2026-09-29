using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UntitledGame.Core;
using Debug = UnityEngine.Debug;

namespace UntitledGame.GenAI
{
    public enum ServiceStatus { NotInstalled, Starting, Ready, Failed }

    /// <summary>
    /// Boots and babysits the local AI stack: llama-server (chat), whisper-server (speech-to-text)
    /// and Piper (text-to-speech). Everything runs on this machine; nothing leaves it.
    /// Servers already listening on the configured ports are reused (handy in the editor).
    /// </summary>
    public class LocalAIServices : MonoBehaviour
    {
        public static LocalAIServices Instance { get; private set; }

        public string Root { get; private set; }
        public LocalAIConfig Config { get; private set; }

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
        public string WhisperBaseUrl => $"http://127.0.0.1:{Config?.whisperPort ?? 8766}";

        public event Action StatusChanged;

        private Process _llm, _whisper;
        private PiperTTS _ttsEnglish, _ttsChinese;
        private string _ttsVoiceKey;
        private string LogDir => Root != null ? Path.Combine(Root, "logs") : Application.temporaryCachePath;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Root = LocalAIConfig.FindRoot();
            Config = LocalAIConfig.Load(Root);
            if (Root == null) LastError = "LocalAI folder not found. Run Tools/setup-local-ai.ps1 to install the voice + AI stack.";
        }

        private void Start()
        {
            StartCoroutine(BringUpLlm());
            StartCoroutine(BringUpWhisper());
            BringUpTts();
            SaveSystem.SettingsChanged += OnSettingsChanged;
        }

        private void OnDestroy()
        {
            SaveSystem.SettingsChanged -= OnSettingsChanged;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            _ttsEnglish?.Pump();
            _ttsChinese?.Pump();
        }

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

            var args = new StringBuilder();
            args.Append($"-m \"{model}\" --host 127.0.0.1 --port {Config.llmPort} -c {Config.contextSize} -np 1 ");
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

        // ------------------------------------------------------------------ Whisper

        private IEnumerator BringUpWhisper()
        {
            string url = WhisperBaseUrl + "/";
            bool alreadyUp = false;
            yield return Probe(url, ok => alreadyUp = ok);
            if (alreadyUp)
            {
                SttStatus = ServiceStatus.Ready;
                StatusChanged?.Invoke();
                yield break;
            }

            string exe = Root != null ? Path.Combine(Root, "whisper", "whisper-server.exe") : null;
            string model = Config.Resolve(Root, Config.whisperModel);
            if (exe == null || !File.Exists(exe) || model == null || !File.Exists(model))
            {
                SttStatus = ServiceStatus.NotInstalled;
                StatusChanged?.Invoke();
                yield break;
            }

            string args = $"-m \"{model}\" --host 127.0.0.1 --port {Config.whisperPort} -t {Config.whisperThreads} -l en";
            _whisper = Launch(exe, args, Path.Combine(LogDir, "whisper-server.log"));
            if (_whisper == null)
            {
                SttStatus = ServiceStatus.Failed;
                StatusChanged?.Invoke();
                yield break;
            }
            SttStatus = ServiceStatus.Starting;
            StatusChanged?.Invoke();
            float deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_whisper.HasExited)
                {
                    SttStatus = ServiceStatus.Failed;
                    LastError = "Speech recognition server stopped (see LocalAI/logs/whisper-server.log).";
                    StatusChanged?.Invoke();
                    yield break;
                }
                bool ok = false;
                yield return Probe(url, r => ok = r);
                if (ok)
                {
                    // Warm-up: the first inference pays one-time allocation costs; do it now, not when the player talks.
                    var noise = new float[MicRecorder.TargetRate];
                    var rnd = new System.Random(1);
                    for (int i = 0; i < noise.Length; i++) noise[i] = (float)(rnd.NextDouble() - 0.5) * 0.002f;
                    yield return WhisperClient.Transcribe(WhisperBaseUrl, WavUtility.EncodePcm16(noise, MicRecorder.TargetRate), "en", null, (_, __) => { });
                    SttStatus = ServiceStatus.Ready;
                    StatusChanged?.Invoke();
                    Debug.Log("[LocalAI] whisper-server ready.");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            SttStatus = ServiceStatus.Failed;
            StatusChanged?.Invoke();
        }

        // ------------------------------------------------------------------ Piper

        private string VoicePath(bool chinese)
        {
            if (chinese) return Config.Resolve(Root, Config.voiceZh);
            string name = SaveSystem.Settings.voiceName;
            if (!string.IsNullOrEmpty(name) && Root != null)
            {
                string p = Path.Combine(Root, "voices", name + ".onnx");
                if (File.Exists(p)) return p;
            }
            return Config.Resolve(Root, Config.voice);
        }

        private void BringUpTts()
        {
            string exe = Root != null ? Path.Combine(Root, "piper", "piper.exe") : null;
            string voice = VoicePath(false);
            if (exe == null || !File.Exists(exe) || voice == null || !File.Exists(voice))
            {
                TtsStatus = ServiceStatus.NotInstalled;
                StatusChanged?.Invoke();
                return;
            }
            _ttsEnglish?.Dispose();
            _ttsEnglish = new PiperTTS(exe, voice, Path.Combine(Application.temporaryCachePath, "tts"));
            float speed = 1f / Mathf.Clamp(SaveSystem.Settings.voiceSpeed, 0.6f, 1.6f);
            bool ok = _ttsEnglish.Start(speed, Path.Combine(LogDir, "piper.log"));
            _ttsVoiceKey = voice + speed.ToString(CultureInfo.InvariantCulture);
            TtsStatus = ok ? ServiceStatus.Ready : ServiceStatus.Failed;
            StatusChanged?.Invoke();
        }

        /// <summary>English voice by default; lazily starts the Mandarin voice when first needed.</summary>
        public PiperTTS GetTts(bool chinese)
        {
            if (!chinese) return _ttsEnglish != null && _ttsEnglish.IsRunning ? _ttsEnglish : null;
            if (_ttsChinese != null && _ttsChinese.IsRunning) return _ttsChinese;
            string exe = Root != null ? Path.Combine(Root, "piper", "piper.exe") : null;
            string voice = VoicePath(true);
            if (exe == null || !File.Exists(exe) || voice == null || !File.Exists(voice)) return null;
            _ttsChinese = new PiperTTS(exe, voice, Path.Combine(Application.temporaryCachePath, "tts"));
            return _ttsChinese.Start(1f / Mathf.Clamp(SaveSystem.Settings.voiceSpeed, 0.6f, 1.6f) * 1.05f, Path.Combine(LogDir, "piper-zh.log")) ? _ttsChinese : null;
        }

        private void OnSettingsChanged()
        {
            string voice = VoicePath(false);
            float speed = 1f / Mathf.Clamp(SaveSystem.Settings.voiceSpeed, 0.6f, 1.6f);
            if (voice + speed.ToString(CultureInfo.InvariantCulture) != _ttsVoiceKey) BringUpTts();
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
            bool keep = Application.isEditor && Config != null && Config.keepServersRunningInEditor && !force;
            _ttsEnglish?.Dispose();
            _ttsChinese?.Dispose();
            _ttsEnglish = _ttsChinese = null;
            if (keep) return;
            Kill(_llm);
            Kill(_whisper);
            _llm = _whisper = null;
        }

        private static void Kill(Process p)
        {
            try
            {
                if (p != null && !p.HasExited) p.Kill();
            }
            catch
            {
                // Already gone.
            }
        }

        public bool AnyStarting => LlmStatus == ServiceStatus.Starting || SttStatus == ServiceStatus.Starting;
    }
}
