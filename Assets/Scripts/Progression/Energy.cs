using System;
using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;

namespace UntitledGame.Progression
{
    /// <summary>
    /// Energy for the day. Fishing uses it (a cast, and continuously while reeling: heavier fish drain it faster).
    /// Each morning it refills to the maximum, which comes from your bed (Master Li sells better ones) plus the
    /// home's comfort level (every piece of furniture you place adds comfort). At zero you pass out.
    /// </summary>
    public static class Energy
    {
        public static readonly int[] BedEnergy = { 100, 140, 180, 230 };
        public static readonly int[] ComfortThresholds = { 0, 3, 6, 10, 15, 20 };
        public const int EnergyPerComfortLevel = 10;
        public const string StarterBed = "bed_mat";

        public static event Action Changed;
        /// <summary>Energy just hit zero.</summary>
        public static event Action Exhausted;

        private static SaveData D => SaveSystem.Data;

        /// <summary>The best bed you own (the house comes with a sleeping mat).</summary>
        public static ItemDef Bed => Catalog.Items.Where(i => i.category == ItemCategory.Bed && (i.id == StarterBed || Inventory.Owns(i.id)))
            .OrderByDescending(i => i.bedLevel).FirstOrDefault();

        public static int BedLevel => Bed?.bedLevel ?? 0;

        /// <summary>Comfort from placed furniture: each kind counts once (a second identical chair adds nothing).</summary>
        public static int ComfortPoints => D.placed.Select(p => p.id).Distinct().Sum(id => Catalog.Get(id)?.comfort ?? 0);

        public static int ComfortLevel
        {
            get
            {
                int pts = ComfortPoints, lvl = 0;
                for (int i = 0; i < ComfortThresholds.Length; i++) if (pts >= ComfortThresholds[i]) lvl = i;
                return lvl;
            }
        }

        public static int NextComfortThreshold => ComfortLevel + 1 < ComfortThresholds.Length ? ComfortThresholds[ComfortLevel + 1] : 0;

        public static float Max => BedEnergy[Mathf.Clamp(BedLevel, 0, BedEnergy.Length - 1)] + EnergyPerComfortLevel * ComfortLevel;

        public static float Current => D.energy < 0f ? Max : Mathf.Min(D.energy, Max);

        public static float Fraction => Current / Mathf.Max(1f, Max);

        public static void Spend(float amount)
        {
            if (amount <= 0f) return;
            float before = Current;
            D.energy = Mathf.Max(0f, before - amount);
            Changed?.Invoke();
            if (before > 0f && D.energy <= 0f) Exhausted?.Invoke();
        }

        /// <summary>Refills (a fraction of) the maximum: 1 after a night in bed, less after passing out.</summary>
        public static void Refill(float fraction = 1f)
        {
            D.energy = Max * Mathf.Clamp01(fraction);
            Changed?.Invoke();
        }

        /// <summary>Reeling drain per second: a little for a sardine, a lot for a 200 kg tuna.</summary>
        public static float ReelDrainPerSecond(float weightKg) => 0.4f + 0.9f * Mathf.Log(1f + Mathf.Max(0f, weightKg), 2f);

        public const float CastCost = 10f; // every cast

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Changed = null;
            Exhausted = null;
        }
    }
}
