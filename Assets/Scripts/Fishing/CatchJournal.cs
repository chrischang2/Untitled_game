using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;

namespace UntitledGame.Fishing
{
    /// <summary>Persistent record of everything caught (species, counts, personal bests).</summary>
    public static class CatchJournal
    {
        public static event Action<CatchResult> Recorded;

        private static readonly List<CatchResult> SessionCatches = new List<CatchResult>();

        public static IReadOnlyList<CatchResult> Session => SessionCatches;

        public static CaughtRecord Get(string id) => SaveSystem.Data.journal.FirstOrDefault(r => r.speciesId == id);

        public static int SpeciesDiscovered => SaveSystem.Data.journal.Count(r => r.count > 0);

        public static int TotalCatches => SaveSystem.Data.totalCatches;

        public static CatchResult Record(FishSpecies species, float length, Vector3 position)
        {
            var data = SaveSystem.Data;
            var rec = Get(species.id);
            var result = new CatchResult { species = species, length = length, position = position };
            result.inBucket = Economy.Inventory.AddToBucket(species, length);
            if (rec == null)
            {
                rec = new CaughtRecord
                {
                    speciesId = species.id,
                    firstDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : 1,
                };
                data.journal.Add(rec);
                result.isNewSpecies = true;
            }
            rec.count++;
            if (species.IsFish && length > rec.bestLength)
            {
                result.isRecord = rec.count > 1;
                rec.bestLength = length;
            }
            data.totalCatches++;
            SessionCatches.Add(result);
            SaveSystem.Save();
            Recorded?.Invoke(result);
            return result;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => SessionCatches.Clear();
    }
}
