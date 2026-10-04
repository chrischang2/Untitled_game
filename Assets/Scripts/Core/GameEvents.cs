using System;

namespace UntitledGame.Core
{
    /// <summary>Loose coupling between gameplay and UI.</summary>
    public static class GameEvents
    {
        public static event Action<string, float> ToastRequested;

        public static void Toast(string message, float seconds = 2.6f) => ToastRequested?.Invoke(message, seconds);

        /// <summary>A big message in the middle of the screen (title, details, good news?), e.g. a test result.</summary>
        public static event Action<string, string, bool> BannerRequested;
        public static void Banner(string title, string details, bool good) => BannerRequested?.Invoke(title, details, good);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => ToastRequested = null;
    }
}
