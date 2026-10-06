using System.Linq;
using UnityEngine;
using UntitledGame.Economy;
using UntitledGame.Progression;

namespace UntitledGame.Fishing
{
    /// <summary>
    /// How hard a fish is, against how strong the player is. Fish come in tiers (the HSK level of the book that teaches
    /// them: starters and the first two books are tier 0, then HSK 1, 2, 3), and within a tier the rarer ones are
    /// stronger. The player's power comes from Coach Wu's training (5 levels per tier, each tier opened by an HSK test).
    /// The gap between them (fish power - stat power) sets the minigame: tuned with a simulated player
    /// (reel_tune.py) so that with every upgrade of a tier its common fish are trivial (gap -2), its rarest moderate
    /// (gap -1), and the next tier's fish nearly impossible (gap +1.5) until you train further.
    /// </summary>
    public static class FishPower
    {
        /// <summary>
        /// Tier powers, set so a fully trained tier's rarest fish sits at a stamina/strength gap of about -0.9 and the
        /// next tier's commonest at about +1 (StatPower at full tiers: 0.5, 4, 7.5, 11).
        /// </summary>
        public static readonly float[] TierBase = { -2f, 1.54f, 5.04f, 8.54f };
        /// <summary>How much stronger a tier's rarest fish is than its commonest (stamina and strength).</summary>
        public const float RarityWeight = 1.6f;
        public const int LevelsPerTier = 5;

        /// <summary>The fish's tier: the HSK level needed for the book that teaches it (0 for starter fish).</summary>
        public static int TierOf(FishSpecies f)
        {
            if (f == null) return 0;
            var book = Catalog.Items.FirstOrDefault(i => i.teachesFish != null && i.teachesFish.Contains(f.id));
            return book == null ? 0 : Mathf.Clamp(book.minHsk, 0, 3);
        }

        /// <summary>
        /// 0 for the commonest, easiest fish of its tier, 1 for the rarest and hardest: the average of where its rarity
        /// and its listed difficulty sit among its tier-mates (when a tier has only one rarity, difficulty decides).
        /// </summary>
        public static float RarityRank(FishSpecies f)
        {
            if (f == null || !f.IsFish) return 0f;
            int tier = TierOf(f);
            var mates = FishDatabase.All.Where(x => x.IsFish && TierOf(x) == tier).ToList();
            float? Rank(System.Func<FishSpecies, float> key)
            {
                float lo = mates.Min(key), hi = mates.Max(key);
                return hi - lo < 0.001f ? (float?)null : (key(f) - lo) / (hi - lo);
            }
            float? rarity = Rank(x => (int)x.rarity), difficulty = Rank(x => x.difficulty);
            if (rarity == null && difficulty == null) return 0f;
            if (rarity == null) return difficulty.Value;
            if (difficulty == null) return rarity.Value;
            return (rarity.Value + difficulty.Value) * 0.5f;
        }

        /// <summary>Base sale price before size: ¥10 for a tier's commonest fish up to ¥40 for its rarest, x10 per tier.</summary>
        public static float BasePrice(FishSpecies f) => 10f * Mathf.Pow(10f, TierOf(f)) * (1f + 3f * RarityRank(f));

        public static float Power(FishSpecies f) => TierBase[TierOf(f)] + RarityWeight * RarityRank(f);

        /// <summary>A stat's power: 0.1 per level in tier 0 (a new player can already land tier-0 fish), 0.7 per level after.</summary>
        public static float StatPower(int level) => level <= LevelsPerTier ? 0.1f * level : 0.5f + 0.7f * (level - LevelsPerTier);

        /// <summary>The minigame for this fish at these training levels.</summary>
        public static ReelParams For(FishSpecies f, int barLevel, int gripLevel, int strengthLevel)
        {
            float bias = MotionBias(f.motion), p = Power(f) + bias;
            return FromGaps(BarGap(f, barLevel) + bias, p - StatPower(gripLevel), p - StatPower(strengthLevel));
        }

        /// <summary>
        /// Eye training works tier by tier: the five levels of a fish's own tier double its green bar (on top of the
        /// fish's rarity), and every tier starts from the same bar. So a player who has finished one tier's training
        /// sees the next tier's fish with the bar a beginner had on the first tier's fish; more training than a fish's
        /// tier keeps widening it, less narrows it.
        /// </summary>
        public static float BarGap(FishSpecies f, int barLevel) =>
            BarBeginnerGap + BarRarityWeight * RarityRank(f) - BarPerLevel * (barLevel - LevelsPerTier * TierOf(f));

        /// <summary>A tier's commonest fish, before that tier's eye training: a bar of about 20% of the track.</summary>
        public const float BarBeginnerGap = -0.1f;
        /// <summary>The rarest fish's bar, fully trained, is about 25% (its commonest gets about 40%).</summary>
        public const float BarRarityWeight = 2.47f;

        /// <summary>Gap for a level that doubles the bar every five levels (width = 0.28 x 0.8^(0.85 gap + 1.6)).</summary>
        public static readonly float BarPerLevel = Mathf.Log(2f) / (LevelsPerTier * 0.85f * Mathf.Log(1f / 0.8f));

        /// <summary>
        /// Some ways of swimming are harder to follow than others (darting fish most, fish that hug the top or bottom
        /// least), so each motion's power is nudged to make the same gap feel about the same (calibrated with the
        /// self-test's "balance" section: each motion reaches ~75% caught at the same gap as Mixed, about -1).
        /// </summary>
        public static float MotionBias(FishMotion m) => m switch
        {
            FishMotion.Dart => -0.25f,
            FishMotion.Sinker => 0.95f,
            FishMotion.Floater => 1.05f,
            FishMotion.Smooth => 1.75f,
            _ => 0f,
        };

        /// <summary>The minigame for these gaps (fish power minus stat power) for each of the three reeling stats.</summary>
        public static ReelParams FromGaps(float barGap, float gripGap, float calmGap)
        {
            float E(float d) => 0.85f * d + 1.6f; // the tuned curve (reel_tune.py)
            float eBar = E(barGap), eGrip = E(gripGap), eCalm = E(calmGap);
            return new ReelParams
            {
                gap = (barGap + gripGap + calmGap) / 3f,
                barSize = Mathf.Clamp(0.28f * Mathf.Pow(0.80f, eBar), 0.05f, 0.85f),
                moveRate = Mathf.Clamp(Mathf.Pow(1.32f, eCalm), 0.3f, 4.5f),
                gain = 0.30f * Mathf.Pow(0.92f, eGrip),
                drain = 0.17f * Mathf.Pow(1.22f, eGrip),
            };
        }

        public static ReelParams ForPlayer(FishSpecies f) => For(f, PlayerStats.Level("bar"), PlayerStats.Level("grip"), PlayerStats.Level("cast"));
    }

    public class ReelParams
    {
        public float gap;       // fish power minus the player's (average) stat power
        public float barSize;   // green bar height (fraction of the track)
        public float moveRate;  // how lively the fish is (1 = calm)
        public float gain;      // catch meter per second while the fish is in the bar
        public float drain;     // catch meter lost per second while it's out
    }

    /// <summary>
    /// The reeling minigame's physics, shared by the game and the self-test's simulated player. Holding lifts the bar,
    /// letting go lets it sink; the fish darts about the track in its own way, livelier with a higher move rate. In the
    /// bar the meter fills, outside it drains. Full = caught, empty = it got away.
    /// </summary>
    public class ReelSim
    {
        public const float StartProgress = 0.3f, Grace = 0.8f;

        public readonly ReelParams P;
        public readonly FishMotion Motion;
        public float BarPos, BarVel, FishPos, FishVel, Progress, Time, TimeInBar;
        public bool FishInBar, LeftBarAfterGrace;

        private float _target, _retarget, _grace, _seed;
        private readonly System.Random _rng;

        public ReelSim(ReelParams p, FishMotion motion, System.Random rng, float startBonus = 0f)
        {
            P = p;
            Motion = motion;
            _rng = rng;
            FishPos = R(0.25f, 0.6f);
            BarPos = Mathf.Clamp(FishPos - p.barSize * 0.5f, 0f, 1f - p.barSize); // the bar starts around the fish
            _target = FishPos;
            _retarget = 0.6f;
            _grace = Grace;
            _seed = R(0f, 100f);
            Progress = StartProgress + startBonus;
        }

        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        public bool Caught => Progress >= 1f;
        public bool Escaped => Progress <= 0f;
        /// <summary>0 = calm, 1 = as wild as fish get (drives jump size and spring, like difficulty used to).</summary>
        public float Wildness => Mathf.Clamp01((P.moveRate - 0.3f) / 3f);

        public void Step(float dt, bool hold)
        {
            Time += dt;
            float w = Wildness;
            // The bar: holding accelerates it up, gravity pulls it down, a little bounce off the bottom.
            BarVel = Mathf.Clamp(BarVel + (hold ? 2.6f : -2.2f) * dt, -1.5f, 1.5f);
            BarPos += BarVel * dt;
            if (BarPos < 0f) { BarPos = 0f; BarVel = -BarVel * 0.3f; }
            if (BarPos > 1f - P.barSize) { BarPos = 1f - P.barSize; BarVel = 0f; }

            // The fish: picks a new spot every so often, depending on how it swims.
            _retarget -= dt * P.moveRate;
            if (_retarget <= 0f)
            {
                float jump = 0.15f + 0.6f * w;
                switch (Motion)
                {
                    case FishMotion.Smooth: _target = Mathf.Clamp01(FishPos + R(-jump, jump) * 0.6f); _retarget = R(1.2f, 2.4f) - w * 0.6f; break;
                    case FishMotion.Sinker: _target = Mathf.Clamp01(R(0f, 0.7f) * R(0f, 1f) + R(-0.05f, 0.1f)); _retarget = R(0.8f, 1.8f) - w * 0.5f; break;
                    case FishMotion.Floater: _target = Mathf.Clamp01(1f - R(0f, 0.7f) * R(0f, 1f)); _retarget = R(0.8f, 1.8f) - w * 0.5f; break;
                    case FishMotion.Dart: _target = Mathf.Clamp01(FishPos + (R(0f, 1f) < 0.5f ? -1f : 1f) * R(jump * 0.6f, jump * 1.2f)); _retarget = R(0.35f, 1.1f) - w * 0.25f; break;
                    default: _target = R(0.02f, 0.98f); _retarget = R(0.7f, 1.7f) - w * 0.5f; break;
                }
                _retarget = Mathf.Max(0.18f, _retarget);
            }
            float spring = (6f + 22f * w) * (Motion == FishMotion.Dart ? 1.6f : 1f) * P.moveRate;
            FishVel += (_target - FishPos) * spring * dt;
            FishVel *= Mathf.Exp(-(5f - 2f * w) * dt);
            // It never quite holds still: a restless wander on top of its darts.
            FishVel += (Mathf.PerlinNoise(Time * 2.5f * P.moveRate, _seed) - 0.5f) * 3.2f * P.moveRate * dt;
            FishPos = Mathf.Clamp01(FishPos + FishVel * dt);

            // Catch meter.
            FishInBar = FishPos >= BarPos && FishPos <= BarPos + P.barSize;
            _grace -= dt;
            if (FishInBar)
            {
                Progress += dt * P.gain;
                TimeInBar += dt;
            }
            else if (_grace <= 0f)
            {
                Progress -= dt * P.drain;
                LeftBarAfterGrace = true;
            }
            Progress = Mathf.Clamp01(Progress);
        }

        /// <summary>
        /// A human-like player for tuning and tests: sees the fish 0.2 s late with a little aiming error, decides every
        /// 50 ms whether to hold, and aims the bar's centre at where it saw the fish.
        /// </summary>
        public static (bool caught, float time, float inBar) SimulateBot(ReelParams p, FishMotion motion, System.Random rng, float limit = 60f)
        {
            var sim = new ReelSim(p, motion, rng);
            const float dt = 1f / 60f;
            var history = new System.Collections.Generic.List<float>();
            bool hold = false;
            float decide = 0f;
            while (sim.Time < limit)
            {
                history.Add(sim.FishPos);
                int lag = Mathf.RoundToInt(0.2f / dt);
                float seen = history[Mathf.Max(0, history.Count - 1 - lag)];
                decide -= dt;
                if (decide <= 0f)
                {
                    decide = 0.05f;
                    double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                    float noise = (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2)) * 0.02f;
                    hold = seen + noise > sim.BarPos + p.barSize * 0.5f + 0.25f * sim.BarVel;
                }
                sim.Step(dt, hold);
                if (sim.Caught || sim.Escaped) break;
            }
            return (sim.Caught, sim.Time, sim.TimeInBar / Mathf.Max(0.001f, sim.Time));
        }
    }
}
