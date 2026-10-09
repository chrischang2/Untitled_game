using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Environment
{
    /// <summary>Scales firefly emission with darkness.</summary>
    public class Fireflies : MonoBehaviour
    {
        [SerializeField] private ParticleSystem system;
        [SerializeField] private float maxRate = 12f;

        public void SetSystem(ParticleSystem ps, float rate)
        {
            system = ps;
            maxRate = rate;
        }

        private void Update()
        {
            if (system == null) return;
            float dark = DayNightCycle.Instance != null ? DayNightCycle.Instance.Darkness : 0f;
            // Only the forest has fireflies (not the desert or the snow).
            float rainDamp = Regions.Current != 0 ? 0f : Weather.Instance != null ? 1f - Weather.Instance.RainAmount : 1f;
            var em = system.emission;
            em.rateOverTime = maxRate * Mathf.InverseLerp(0.5f, 0.9f, dark) * rainDamp;
        }
    }
}
