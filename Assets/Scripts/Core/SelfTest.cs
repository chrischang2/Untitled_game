using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Player;
using UntitledGame.UI;

namespace UntitledGame.Core
{
    /// <summary>
    /// Automated end-to-end check, run with the command-line flag <c>-selftest</c>:
    /// waits for the local AI stack, lets Mei greet, feeds a synthesised spoken question through
    /// Whisper -> LLM -> Piper, lands a fish, captures screenshots and writes a report, then quits.
    /// </summary>
    public class SelfTest : MonoBehaviour
    {
        private readonly StringBuilder _log = new StringBuilder();
        private string _outDir;
        private float _t0;

        private void Start()
        {
            if (!System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-selftest")) return;
            string root = LocalAIConfig.FindRoot();
            _outDir = root != null ? Path.Combine(Path.GetDirectoryName(root), "Captures", "selftest") : Path.Combine(Application.persistentDataPath, "selftest");
            Directory.CreateDirectory(_outDir);
            _t0 = Time.realtimeSinceStartup;
            SaveSystem.ResetProgress();
            SaveSystem.Settings.handsFree = false;
            SaveSystem.Settings.language = System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-mandarin")
                ? LanguageMode.MandarinPractice
                : LanguageMode.English;
            StartCoroutine(Run());
        }

        private void Log(string msg)
        {
            string line = $"[{Time.realtimeSinceStartup - _t0,6:0.0}s] {msg}";
            _log.AppendLine(line);
            Debug.Log("[SelfTest] " + msg);
            File.WriteAllText(Path.Combine(_outDir, "report.txt"), _log.ToString());
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_outDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            Log($"screenshot {name}.png");
        }

        private IEnumerator Run()
        {
            var services = LocalAIServices.Instance;
            var brain = FindFirstObjectByType<CompanionBrain>();
            var voice = FindFirstObjectByType<CompanionVoice>();
            var voiceChat = FindFirstObjectByType<VoiceChatController>();
            var fishing = FindFirstObjectByType<FishingController>();
            Log($"LocalAI root: {services?.Root ?? "(none)"}  model: {services?.Config?.llmModel}");

            brain.SentenceSpoken += s => Log($"Mei says: \"{s}\"");
            brain.PlayerSaid += s => Log($"Player said (transcribed): \"{s}\"");

            yield return new WaitForSeconds(1f);
            yield return Shot("01_start");
            yield return MeasureFps("while AI services boot", 3f);

            float deadline = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < deadline &&
                   (services.LlmStatus == ServiceStatus.Starting || services.SttStatus == ServiceStatus.Starting))
                yield return null;
            Log($"Services: LLM={services.LlmStatus} STT={services.SttStatus} TTS={services.TtsStatus} {services.LastError}");

            // Greeting (Mei starts it herself).
            deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline && brain.State == CompanionState.Idle && brain.Log.Count == 0) yield return null;
            yield return new WaitForSeconds(1.2f);
            yield return Shot("02_greeting");
            yield return WaitIdle(brain, 60f);
            Log($"Greeting latency to first token: {brain.LastResponseLatency:0.00}s");

            // Spoken question: synthesise with Piper, run through Whisper like a real mic recording.
            var tts = services.GetTts(false);
            if (tts != null && services.SttStatus == ServiceStatus.Ready)
            {
                WavUtility.PcmData? pcm = null;
                bool done = false;
                tts.Synthesize("Hey Mei! What kind of fish can I catch here at night?", p => { pcm = p; done = true; });
                while (!done) yield return null;
                if (pcm.HasValue)
                {
                    var mono = WavUtility.Resample(pcm.Value.samples, pcm.Value.sampleRate, MicRecorder.TargetRate);
                    float t = Time.realtimeSinceStartup;
                    brain.BeginListening();
                    yield return voiceChat.Process(mono);
                    Log($"Whisper transcription took {voiceChat.LastTranscriptionSeconds:0.00}s");
                    while (brain.State == CompanionState.Thinking) yield return null;
                    Log($"Time from end of speech to Mei's first spoken sentence: {Time.realtimeSinceStartup - t:0.00}s (LLM first token {brain.LastResponseLatency:0.00}s)");
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot("03_answer");
                    yield return WaitIdle(brain, 60f);
                }
            }
            else Log("Skipping voice round-trip (TTS or STT unavailable).");

            // Typed message path.
            brain.SendPlayerMessage("I love the sunset here. Do you want some tea?");
            yield return WaitIdle(brain, 60f);

            yield return MeasureFps("idle after chatting", 3f);

            // A real fishing round through the input path, from the end of the dock.
            yield return FishingRound(fishing);

            // Journal + settings panels.
            var ui = FindFirstObjectByType<GameUI>();
            ui.ToggleJournal();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("12_journal");
            ui.ToggleJournal();
            ui.ToggleSettings();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("13_settings");
            ui.ToggleSettings();
            ui.ToggleChatLog();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("14_chatlog");
            ui.ToggleChatLog();

            // Catch a fish and let Mei react.
            fishing.DebugCatch("koi", 52f);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("04_catch");
            yield return new WaitForSeconds(3f);
            yield return WaitIdle(brain, 60f);

            // Rain.
            Weather.Instance.DebugSetRain(1f);
            yield return new WaitForSeconds(2.5f);
            yield return Shot("15_rain");
            Weather.Instance.DebugSetRain(0f);

            // Evening + night looks.
            DayNightCycle.Instance.TimeOfDay = 19.3f;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("05_evening");
            DayNightCycle.Instance.TimeOfDay = 22.5f;
            yield return new WaitForSeconds(4f);
            yield return Shot("06_night");

            Log($"Conversation log ({brain.Log.Count} lines):");
            foreach (var (speaker, text) in brain.Log) Log($"   {speaker}: {text}");
            Log("SELFTEST COMPLETE");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        private IEnumerator MeasureFps(string label, float seconds)
        {
            int frames = 0;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                frames++;
                yield return null;
            }
            Log($"FPS {label}: {frames / seconds:0}");
        }

        private IEnumerator FishingRound(FishingController f)
        {
            var player = f.GetComponent<PlayerController>();
            Vector2 end = WorldShape.DockShorePoint - WorldShape.DockDirection * (WorldShape.DockLength - 1f);
            player.Teleport(new Vector3(end.x, WorldShape.DockDeckHeight + 0.05f, end.y), 0f);
            yield return new WaitForSeconds(2.5f);

            f.SimulateHold = true;
            f.SimulateClick();
            yield return new WaitForSeconds(0.55f);
            yield return Shot("07_charging");
            f.SimulateHold = false;
            float t = Time.time;
            while (f.State != FishingState.Waiting && f.State != FishingState.Idle && Time.time - t < 5f) yield return null;
            Log($"Cast landed -> state {f.State}");
            yield return new WaitForSeconds(2f);
            yield return Shot("08_waiting");

            f.DebugBiteNow();
            t = Time.time;
            while (f.State != FishingState.Bite && Time.time - t < 5f) yield return null;
            yield return new WaitForSeconds(0.25f);
            yield return Shot("09_bite");
            Log($"Bite! Hooking a {f.HookedSpecies?.name}");
            f.SimulateClick();
            yield return null;

            t = Time.time;
            bool shot = false;
            int frames = 0;
            while (f.State == FishingState.Reeling && Time.time - t < 45f)
            {
                f.SimulateHold = !f.FishPulling && f.Tension < 0.75f;
                if (!shot && Time.time - t > 2.5f)
                {
                    shot = true;
                    yield return Shot("10_reeling");
                }
                frames++;
                yield return null;
            }
            f.SimulateHold = false;
            Log($"Reeling finished after {Time.time - t:0.0}s (avg {frames / Mathf.Max(0.1f, Time.time - t):0} fps) -> {f.State}; catch: {f.LastCatch?.species.name} {f.LastCatch?.length:0.#} cm");
            yield return new WaitForSeconds(0.9f);
            yield return Shot("11_landed");
            yield return new WaitForSeconds(2f);
            f.SimulateClick();
            yield return new WaitForSeconds(1f);
        }

        private IEnumerator WaitIdle(CompanionBrain brain, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            yield return null;
            while (Time.realtimeSinceStartup < deadline && brain.State != CompanionState.Idle) yield return null;
            yield return new WaitForSeconds(0.5f);
        }
    }
}
