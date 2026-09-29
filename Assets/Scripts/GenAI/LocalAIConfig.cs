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
        public string llmModel = "models/Qwen3.5-2B-Q4_K_M.gguf";
        public string whisperModel = "models/ggml-base.bin";
        public string voice = "voices/en_US-kristin-medium.onnx";
        public string voiceZh = "voices/zh_CN-huayan-medium.onnx";
        public int llmPort = 8765;
        public int whisperPort = 8766;
        public int contextSize = 4096;
        public int gpuLayers = -1;           // -1 = let llama.cpp fit automatically
        public int whisperThreads = 6;       // the LLM is idle while you talk, so Whisper can have more cores
        public int llmThreads = 4;           // leave CPU cores for the game + whisper (llama.cpp degrades badly when oversubscribed)
        public string extraLlmArgs = "";     // appended verbatim to the llama-server command line
        public string externalLlmUrl = "";   // e.g. "http://localhost:11434/v1" to use Ollama / LM Studio instead
        public string externalLlmModel = ""; // model name for the external server
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
