using System;
using System.IO;
using UnityEngine;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// Where the local AI runtime lives and how to launch it. Defaults are overridden by
    /// LocalAI/localai.json (written by Tools/setup-local-ai.ps1) so models can be swapped without a rebuild.
    /// </summary>
    [Serializable]
    public class LocalAIConfig
    {
        public string llmModel = "models/Qwen3.5-4B-Q4_K_M.gguf";
        public int llmPort = 8765;
        public int contextSize = 4096;       // per conversation slot
        public string flashAttention = "auto"; // llama-server -fa: "on", "off" or "auto" (computes attention in tiles: less memory, faster prompts)
        public string kvCacheType = "f16";   // KV cache precision: "f16", or "q8_0" (about half the memory; needs flash attention on)
        public int llmSlots = 3;             // Mei / shopkeepers / intent classifier each keep a warm prompt cache
        public int gpuLayers = -1;           // -1 = let llama.cpp fit automatically
        public int llmThreads = 4;           // leave CPU cores for the game + speech (llama.cpp degrades badly when oversubscribed)
        public string extraLlmArgs = "";     // appended verbatim to the llama-server command line
        public string externalLlmUrl = "";   // e.g. "http://localhost:11434/v1" to use Ollama / LM Studio instead
        public string externalLlmModel = ""; // model name for the external server

        // Speech (sherpa-onnx, in-process).
        public string sherpaDir = "sherpa";
        public string asrModel = "models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09";
        public string qwenAsrModel = "models/sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25"; // optional: better English lines
        public string voiceMei = "models/matcha-icefall-zh-en";
        public string vocoder = "models/vocos-16khz-univ.onnx";
        public string voiceEnglish = "models/vits-piper-en_US-kristin-medium";
        public string voiceMale = "models/vits-piper-zh_CN-chaowen-medium";
        public string voiceFemale = "models/vits-piper-zh_CN-xiao_ya-medium";
        public string voiceNatural = "models/kokoro-multi-lang-v1_1"; // Mei's natural voice (Kokoro, fp32: int8 is slower on this CPU)
        public int speechThreads = 4;
        public int naturalVoiceThreads = 6;  // Kokoro: 6 threads ran at ~0.64x real time next to the LLM
        public bool keepServersRunningInEditor = true;

        public const string FolderName = "LocalAI";
        public const string EnvVar = "WILLOWLAKE_LOCALAI";

        /// <summary>Looks next to the game / project (and a few parents) for the LocalAI folder.</summary>
        public static string FindRoot()
        {
            string env = System.Environment.GetEnvironmentVariable(EnvVar);
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            string dir = Application.dataPath;
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
            {
                dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(dir)) break;
                string candidate = Path.Combine(dir, FolderName);
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        public static LocalAIConfig Load(string root)
        {
            var cfg = new LocalAIConfig();
            if (root == null) return cfg;
            string path = Path.Combine(root, "localai.json");
            if (!File.Exists(path)) return cfg;
            try
            {
                string json = File.ReadAllText(path).TrimStart('﻿');
                JsonUtility.FromJsonOverwrite(json, cfg);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LocalAI] Could not parse {path}: {e.Message}");
            }
            return cfg;
        }

        public string Resolve(string root, string relative) =>
            string.IsNullOrEmpty(relative) || root == null ? null : Path.GetFullPath(Path.Combine(root, relative));
    }
}
