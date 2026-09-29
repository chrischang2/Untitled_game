using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UntitledGame.GenAI
{
    [Serializable]
    public class ChatSampling
    {
        public float temperature = 0.75f;
        public float top_p = 0.85f;
        public int top_k = 20;
        public float presence_penalty = 1.2f;
        public int max_tokens = 160;
    }

    /// <summary>Live handle for an in-flight streamed reply.</summary>
    public class ChatStreamHandle
    {
        public bool Done { get; internal set; }
        public bool Cancelled { get; private set; }
        public string Error { get; internal set; }
        public string FullText => Builder.ToString();
        public float StartedAt { get; internal set; }
        public float FirstTokenAt { get; internal set; } = -1f;
        internal readonly StringBuilder Builder = new StringBuilder();
        internal UnityWebRequest Request;

        public void Cancel()
        {
            if (Done) return;
            Cancelled = true;
            try { Request?.Abort(); } catch { /* already disposed */ }
        }
    }

    /// <summary>
    /// Streams chat completions from any OpenAI-compatible server (llama.cpp's llama-server,
    /// Ollama's /v1, LM Studio...). Tokens arrive through server-sent events and are forwarded live.
    /// </summary>
    public static class OpenAIStreamingClient
    {
        [Serializable]
        private class Request
        {
            public string model;
            public List<ChatMessage> messages;
            public bool stream = true;
            public float temperature;
            public float top_p;
            public int top_k;
            public float presence_penalty;
            public int max_tokens;
            public bool cache_prompt = true;
            public TemplateKwargs chat_template_kwargs = new TemplateKwargs();
        }

        [Serializable]
        private class TemplateKwargs
        {
            public bool enable_thinking;
        }

        [Serializable] private class Chunk { public Choice[] choices; }
        [Serializable] private class Choice { public Delta delta; public string finish_reason; }
        [Serializable] private class Delta { public string content; }

        public static ChatStreamHandle Stream(
            MonoBehaviour host, string baseUrl, string model, List<ChatMessage> messages, ChatSampling sampling,
            Action<string> onDelta, Action<ChatStreamHandle> onComplete)
        {
            var handle = new ChatStreamHandle { StartedAt = Time.realtimeSinceStartup };
            host.StartCoroutine(Run(baseUrl, model, messages, sampling, handle, onDelta, onComplete));
            return handle;
        }

        private static IEnumerator Run(string baseUrl, string model, List<ChatMessage> messages, ChatSampling s,
            ChatStreamHandle handle, Action<string> onDelta, Action<ChatStreamHandle> onComplete)
        {
            var body = new Request
            {
                model = string.IsNullOrEmpty(model) ? "local" : model,
                messages = messages,
                temperature = s.temperature,
                top_p = s.top_p,
                top_k = s.top_k,
                presence_penalty = s.presence_penalty,
                max_tokens = s.max_tokens,
            };
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

            using var req = new UnityWebRequest(baseUrl.TrimEnd('/') + "/chat/completions", "POST");
            req.uploadHandler = new UploadHandlerRaw(payload);
            req.downloadHandler = new SseHandler(delta =>
            {
                if (handle.Cancelled) return;
                if (handle.FirstTokenAt < 0f) handle.FirstTokenAt = Time.realtimeSinceStartup;
                handle.Builder.Append(delta);
                onDelta?.Invoke(delta);
            });
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "text/event-stream");
            req.timeout = 120;
            handle.Request = req;

            yield return req.SendWebRequest();

            if (!handle.Cancelled && req.result != UnityWebRequest.Result.Success)
            {
                handle.Error = $"{req.result}: {req.error}";
            }
            handle.Request = null;
            handle.Done = true;
            onComplete?.Invoke(handle);
        }

        /// <summary>Parses "data: {json}" lines as bytes arrive (runs on the main thread).</summary>
        private class SseHandler : DownloadHandlerScript
        {
            private readonly Action<string> _onDelta;
            private readonly List<byte> _pending = new List<byte>(4096);

            public SseHandler(Action<string> onDelta) : base(new byte[4096]) => _onDelta = onDelta;

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength == 0) return true;
                for (int i = 0; i < dataLength; i++)
                {
                    byte b = data[i];
                    if (b == (byte)'\n')
                    {
                        HandleLine(Encoding.UTF8.GetString(_pending.ToArray()));
                        _pending.Clear();
                    }
                    else _pending.Add(b);
                }
                return true;
            }

            protected override void CompleteContent()
            {
                if (_pending.Count > 0) HandleLine(Encoding.UTF8.GetString(_pending.ToArray()));
                _pending.Clear();
            }

            private void HandleLine(string line)
            {
                line = line.Trim();
                if (!line.StartsWith("data:")) return;
                string json = line.Substring(5).Trim();
                if (json == "[DONE]" || json.Length == 0) return;
                try
                {
                    var chunk = JsonUtility.FromJson<Chunk>(json);
                    string text = chunk?.choices != null && chunk.choices.Length > 0 ? chunk.choices[0].delta?.content : null;
                    if (!string.IsNullOrEmpty(text)) _onDelta(text);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[LLM] Bad stream chunk: {e.Message}");
                }
            }
        }
    }
}
