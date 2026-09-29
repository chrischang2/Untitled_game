using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UntitledGame.Progression
{
    /// <summary>
    /// Tracks which Chinese phrases the player has learned and fires unlock events.
    /// Persists to PlayerPrefs as a simple JSON blob; swap for a save system later.
    /// </summary>
    public class PhraseUnlockSystem : MonoBehaviour
    {
        private const string SaveKey = "UntitledGame.LearnedPhrases";

        public event Action<ChinesePhrase> PhraseUnlocked;

        [SerializeField] private List<ChinesePhrase> allPhrases = new List<ChinesePhrase>();

        private readonly HashSet<string> _learnedIds = new HashSet<string>();

        private void Awake() => Load();

        public bool IsLearned(string phraseId) => _learnedIds.Contains(phraseId);

        /// <summary>Called when the companion's LLM judges the player successfully used/recognized a phrase.</summary>
        public void MarkLearned(string phraseId)
        {
            if (!_learnedIds.Add(phraseId)) return;

            Save();
            var phrase = allPhrases.FirstOrDefault(p => p.id == phraseId);
            if (phrase != null)
            {
                PhraseUnlocked?.Invoke(phrase);
            }
        }

        private void Save()
        {
            var data = new SaveData { learnedIds = _learnedIds.ToList() };
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        private void Load()
        {
            _learnedIds.Clear();
            if (!PlayerPrefs.HasKey(SaveKey)) return;

            var data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
            if (data?.learnedIds == null) return;

            foreach (var id in data.learnedIds) _learnedIds.Add(id);
        }

        [Serializable]
        private class SaveData
        {
            public List<string> learnedIds;
        }
    }
}
