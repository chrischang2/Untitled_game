using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Environment
{
    /// <summary>
    /// Lightweight weather: now and then a soft rain shower rolls in (clouds thicken, rain particles
    /// follow the camera, raindrop ripples on the lake). Fish bite a little better in the rain.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        public static Weather Instance { get; private set; }

        [SerializeField] private ParticleSystem rain;
        [SerializeField] private ParticleSystem snow;
        [SerializeField] private float checkEveryGameHours = 2f;
        [SerializeField, Range(0, 1)] private float rainChance = 0.22f;

        private float _rainTarget;
        private float _lastCheckHour = -1f;
        private float _rainUntilHour = -1f;
        private float _dropTimer;

        public float RainAmount { get; private set; }
        public bool IsRaining => RainAmount > 0.4f;
        public string Description => Snowy
            ? (IsRaining ? "heavy snow" : "light snow")
            : IsRaining ? "a gentle rain" : (RainAmount > 0.05f ? "clouding over" : "clear skies");

        /// <summary>In a snowy region showers fall as snow, and a few flakes are always drifting down.</summary>
        public static bool Snowy => Regions.Here.snow;

        public void SetRain(ParticleSystem ps) => rain = ps;
        public void SetSnow(ParticleSystem ps) => snow = ps;

        private void Awake() => Instance = this;

        /// <summary>Test hook: jump straight to a rain amount.</summary>
        public void DebugSetRain(float amount)
        {
            RainAmount = amount;
            _rainTarget = amount;
        }

        public void ForceRain(bool on, float hours = 2f)
        {
            var dn = DayNightCycle.Instance;
            _rainTarget = on ? 1f : 0f;
            _rainUntilHour = on && dn != null ? (dn.TimeOfDay + hours) % 24f : -1f;
        }

        private void Update()
        {
            var dn = DayNightCycle.Instance;
            if (dn != null)
            {
                float hour = dn.TimeOfDay;
                if (_lastCheckHour < 0f) _lastCheckHour = hour;
                float elapsed = Mathf.Repeat(hour - _lastCheckHour, 24f);
                if (elapsed >= checkEveryGameHours)
                {
                    _lastCheckHour = hour;
                    // Each region has its own weather (it hardly ever rains in the desert).
                    float chance = rainChance * Regions.Here.rainChance / 0.22f;
                    if (_rainTarget < 0.5f && Random.value < chance) ForceRain(true, Random.Range(1f, 3f));
                }
                if (_rainTarget > 0.5f && _rainUntilHour >= 0f && Mathf.Abs(Mathf.DeltaAngle(hour * 15f, _rainUntilHour * 15f)) < 3f)
                {
                    _rainTarget = 0f;
                }
                dn.CloudCover = Mathf.Lerp(0.25f, 0.95f, RainAmount);
            }

            RainAmount = Mathf.MoveTowards(RainAmount, _rainTarget, Time.deltaTime / 20f);
            bool snowy = Snowy;
            if (AudioManager.Instance != null) AudioManager.Instance.RainAmount = snowy ? 0f : RainAmount;

            if (rain != null)
            {
                var em = rain.emission;
                em.rateOverTime = snowy ? 0f : 900f * RainAmount;
                if (Camera.main != null) rain.transform.position = Camera.main.transform.position + Vector3.up * 8f;
            }
            if (snow != null)
            {
                var em = snow.emission;
                em.rateOverTime = snowy ? 45f + 420f * RainAmount : 0f;
                if (Camera.main != null) snow.transform.position = Camera.main.transform.position + Vector3.up * 7f;
            }

            if (!snowy && RainAmount > 0.3f && Camera.main != null)
            {
                _dropTimer -= Time.deltaTime;
                if (_dropTimer <= 0f)
                {
                    _dropTimer = Mathf.Lerp(0.8f, 0.15f, RainAmount);
                    Vector3 c = Camera.main.transform.position;
                    Vector2 r = Random.insideUnitCircle * 14f;
                    var p = new Vector3(c.x + r.x, 0f, c.z + r.y);
                    if (WorldShape.IsWater(p.x, p.z)) WaterRipples.Spawn(p, 0.35f);
                }
            }
        }
    }
}
