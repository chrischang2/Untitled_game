using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// Talks to a locally running Ollama server (default http://localhost:11434).
    /// Requires `ollama serve` running with a model already pulled (e.g. `ollama pull qwen2.5:3b`).
    /// </summary>
    public class OllamaLLMClient : MonoBehaviour, ILocalLLMClient
    {
        [SerializeField] private string baseUrl = "http://localhost:11434";
        [SerializeField] private string model = "qwen2.5:3b";
        [SerializeField] private int timeoutSeconds = 30;

        public IEnumerator SendChat(ChatRequest request, Action<ChatResult> onComplete)
        {
            request.model = string.IsNullOrEmpty(request.model) ? model : request.model;
            request.stream = false;

            string json = JsonUtility.ToJson(request);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using var www = new UnityWebRequest($"{baseUrl}/api/chat", "POST");
            www.uploadHandler = new UploadHandlerRaw(body);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            www.timeout = timeoutSeconds;

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(ChatResult.Fail($"{www.result}: {www.error}"));
                yield break;
            }

            try
            {
                var parsed = JsonUtility.FromJson<OllamaChatResponse>(www.downloadHandler.text);
                onComplete?.Invoke(ChatResult.Ok(parsed?.message?.content ?? string.Empty));
            }
            catch (Exception e)
            {
                onComplete?.Invoke(ChatResult.Fail($"Failed to parse response: {e.Message}"));
            }
        }
    }
}
