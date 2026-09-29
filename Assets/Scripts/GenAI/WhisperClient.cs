using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace UntitledGame.GenAI
{
    /// <summary>Sends a WAV to whisper.cpp's server (/inference) and returns the transcript.</summary>
    public static class WhisperClient
    {
        [Serializable]
        private class Response
        {
            public string text;
            public string error;
        }

        /// <param name="language">"en", "zh" or "auto".</param>
        /// <param name="prompt">Optional vocabulary hint (names, fishing words) to bias recognition.</param>
        public static IEnumerator Transcribe(string baseUrl, byte[] wav, string language, string prompt, Action<string, string> onDone)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", wav, "speech.wav", "audio/wav"),
                new MultipartFormDataSection("response_format", "json"),
                new MultipartFormDataSection("temperature", "0.0"),
                new MultipartFormDataSection("language", string.IsNullOrEmpty(language) ? "en" : language),
            };
            if (!string.IsNullOrEmpty(prompt)) form.Add(new MultipartFormDataSection("prompt", prompt));

            using var req = UnityWebRequest.Post(baseUrl.TrimEnd('/') + "/inference", form);
            req.timeout = 60;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onDone?.Invoke(null, $"{req.result}: {req.error}");
                yield break;
            }
            try
            {
                var r = JsonUtility.FromJson<Response>(req.downloadHandler.text);
                if (!string.IsNullOrEmpty(r?.error)) onDone?.Invoke(null, r.error);
                else onDone?.Invoke((r?.text ?? "").Trim(), null);
            }
            catch (Exception e)
            {
                onDone?.Invoke(null, e.Message);
            }
        }
    }
}
