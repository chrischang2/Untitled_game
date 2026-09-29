using UnityEngine;

namespace UntitledGame.Progression
{
    /// <summary>A single learnable Chinese phrase tied to a gameplay unlock.</summary>
    [CreateAssetMenu(menuName = "Untitled Game/Chinese Phrase", fileName = "NewPhrase")]
    public class ChinesePhrase : ScriptableObject
    {
        public string id;
        [TextArea] public string hanzi;
        public string pinyin;
        public string englishMeaning;

        [Tooltip("Human-readable description of what unlocks when this phrase is learned.")]
        public string unlockDescription;
    }
}
