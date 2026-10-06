using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;

namespace UntitledGame.Fishing
{
    /// <summary>
    /// A fish frenzy: a patch of bubbling, rippling water off the beach where a shoal is feeding. Cast into it and
    /// fish bite twice as fast and come up bigger (the weight window moves up). Every couple of minutes the shoal
    /// moves on somewhere else within reach of the dock and the beach.
    /// </summary>
    public class FishFrenzy : MonoBehaviour
    {
        public static FishFrenzy Instance { get; private set; }
        public const float Radius = 3.5f, BiteSpeedUp = 2f, WeightBoost = 0.2f, Lifetime = 120f;

        public Vector3 Position { get; private set; }
        private float _movesAt, _nextRipple, _nextSplash;
        private readonly System.Random _rng = new System.Random();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            new GameObject("FishFrenzy").AddComponent<FishFrenzy>();
        }

        private void Awake()
        {
            Instance = this;
            Move();
        }

        /// <summary>True when a point (the bobber) is inside the frenzy.</summary>
        public static bool Contains(Vector3 p)
        {
            if (Instance == null) return false;
            var d = p - Instance.Position;
            d.y = 0f;
            return d.magnitude < Radius;
        }

        /// <summary>Somewhere a cast can reach: off the dock's end or along the beach, in open water.</summary>
        public void Move()
        {
            for (int i = 0; i < 40; i++)
            {
                float x = (float)(_rng.NextDouble() * 50.0 - 20.0);
                float z = WorldShape.ShoreZ(x) + 4f + (float)_rng.NextDouble() * 9f;
                if (!WorldShape.IsWater(x, z) || WorldShape.IsOnDock(x, z, 2.5f)) continue;
                Position = new Vector3(x, WorldShape.WaterLevel, z);
                break;
            }
            _movesAt = Time.time + Lifetime;
            ChatAudit.Write("FISHING", $"a fish frenzy at ({Position.x:0}, {Position.z:0}), {WorldShape.ShoreDistance(Position.x, Position.z) * -1f:0} m out");
        }

        private float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        private void Update()
        {
            if (Time.time > _movesAt) Move();
            if (Time.time > _nextRipple)
            {
                _nextRipple = Time.time + 0.3f;
                WaterRipples.Spawn(Position + new Vector3(R(-Radius, Radius) * 0.7f, 0f, R(-Radius, Radius) * 0.7f), R(0.3f, 0.7f));
            }
            if (Time.time > _nextSplash)
            {
                _nextSplash = Time.time + R(0.8f, 2f);
                Effects.Splash(Position + new Vector3(R(-2f, 2f), 0f, R(-2f, 2f)), R(0.25f, 0.5f));
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
