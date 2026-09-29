using System;
using System.Collections.Generic;

namespace UntitledGame.GenAI
{
    [Serializable]
    public class ChatMessage
    {
        public string role; // "system" | "user" | "assistant"
        public string content;

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [Serializable]
    public class ChatRequest
    {
        public string model;
        public List<ChatMessage> messages = new List<ChatMessage>();
        public bool stream;
    }

    // --- Ollama /api/chat response shape (non-streaming) ---
    [Serializable]
    internal class OllamaChatResponse
    {
        public string model;
        public OllamaMessage message;
        public bool done;
    }

    [Serializable]
    internal class OllamaMessage
    {
        public string role;
        public string content;
    }
}
