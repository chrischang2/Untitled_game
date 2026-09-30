using System.Collections.Generic;

namespace UntitledGame.Core
{
    /// <summary>
    /// Who is allowed to read gameplay input right now. Menus / text entry register as blockers so
    /// that e.g. typing "wasd" into the chat box doesn't walk the player into the lake.
    /// </summary>
    public static class InputGate
    {
        private static readonly HashSet<object> GameplayBlockers = new HashSet<object>();
        private static readonly HashSet<object> MovementBlockers = new HashSet<object>();

        /// <summary>True while a text field has focus: no hotkeys at all.</summary>
        public static bool TextEntryActive { get; set; }

        public static bool GameplayBlocked => GameplayBlockers.Count > 0 || TextEntryActive;
        public static bool MovementBlocked => GameplayBlocked || MovementBlockers.Count > 0;

        public static void BlockGameplay(object who) => GameplayBlockers.Add(who);
        public static void UnblockGameplay(object who) => GameplayBlockers.Remove(who);
        public static void BlockMovement(object who) => MovementBlockers.Add(who);
        public static void UnblockMovement(object who) => MovementBlockers.Remove(who);

        /// <summary>Called when the scene is reloaded (loading a save): old menus can't unblock themselves.</summary>
        public static void ClearAll() => Reset();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            GameplayBlockers.Clear();
            MovementBlockers.Clear();
            TextEntryActive = false;
        }
    }
}
