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

        public static int SpeciesDiscovered => SaveSystem.Data.journal.Count(r => r.count > 0 && FishDatabase.Get(r.speciesId) != null);

        public static int TotalCatches => SaveSystem.Data.totalCatches;

        public static readonly string[] MedalNames = { "", "bronze", "silver", "gold" };
        public const float SilverAt = 0.6f, GoldAt = 0.9f;
        /// <summary>How many times a golden fish is worth.</summary>
        public const int GoldenValue = 5;

        /// <summary>The medal a catch of this weight earns: bronze for any, silver from 60% of the species' weight range, gold from 90%.</summary>
        public static int MedalFor(FishSpecies species, float weightKg)
        {
            if (species == null || !species.IsFish) return 0;
            float t = Mathf.InverseLerp(species.minWeight, species.maxWeight, weightKg);
            return t >= GoldAt ? 3 : t >= SilverAt ? 2 : 1;
        }

        public static int Medal(string speciesId) => Get(speciesId)?.medal ?? 0;
        public static int MedalCount(int medal) => SaveSystem.Data.journal.Count(r => r.medal >= medal && FishDatabase.Get(r.speciesId) != null);

        public static CatchResult Record(FishSpecies species, float length, Vector3 position, bool golden = false)
        {
            var data = SaveSystem.Data;
            var rec = Get(species.id);
            var result = new CatchResult { species = species, length = length, position = position, golden = golden };
            result.inBucket = Economy.Inventory.AddToBucket(species, length, golden);
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
            if (golden) rec.goldenCount++;
            if (species.IsFish && length > rec.bestLength)
            {
                result.isRecord = rec.count > 1;
                rec.bestLength = length;
            }
            int medal = MedalFor(species, length);
            if (medal > rec.medal)
            {
                rec.medal = medal;
                result.newMedal = medal;
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
