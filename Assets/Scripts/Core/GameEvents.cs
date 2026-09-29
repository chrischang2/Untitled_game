using System;

namespace UntitledGame.Core
{
    /// <summary>Loose coupling between gameplay and UI.</summary>
    public static class GameEvents
    {
        public static event Action<string, float> ToastRequested;

        public static void Toast(string message, float seconds = 2.6f) => ToastRequested?.Invoke(message, seconds);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => ToastRequested = null;
    }
}
