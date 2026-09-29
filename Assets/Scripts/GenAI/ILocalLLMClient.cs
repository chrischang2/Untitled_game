using System;
using System.Collections;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// Backend-agnostic contract for talking to a local LLM (Ollama, llama.cpp server, etc.).
    /// Swap implementations without touching companion/dialogue code.
    /// </summary>
    public interface ILocalLLMClient
    {
        /// <summary>Sends the conversation so far and invokes onComplete with the reply text.</summary>
        IEnumerator SendChat(ChatRequest request, Action<ChatResult> onComplete);
    }

    public readonly struct ChatResult
    {
        public readonly bool Success;
        public readonly string ReplyText;
        public readonly string Error;

        public ChatResult(bool success, string replyText, string error)
        {
            Success = success;
            ReplyText = replyText;
            Error = error;
        }

        public static ChatResult Ok(string replyText) => new ChatResult(true, replyText, null);
        public static ChatResult Fail(string error) => new ChatResult(false, null, error);
    }
}
