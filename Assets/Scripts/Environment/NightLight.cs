using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Environment
{
    /// <summary>Fades a light (and optional glow renderer) in at night; optional fire flicker.</summary>
    public class NightLight : MonoBehaviour
    {
        [SerializeField] private Light targetLight;
        [SerializeField] private Renderer glow;
        [SerializeField] private float nightIntensity = 2f;
        [SerializeField] private float dayIntensity;
        [SerializeField] private bool flicker;
        [SerializeField] private float threshold = 0.35f;

        private float _seed;
        private Vector3 _glowScale;
        private MaterialPropertyBlock _mpb;

        public void Configure(Light l, Renderer g, float night, float day, bool flick)
        {
            targetLight = l;
            glow = g;
            nightIntensity = night;
            dayIntensity = day;
            flicker = flick;
        }

        private void Awake()
        {
            _seed = Random.value * 100f;
            if (glow != null) _glowScale = glow.transform.localScale;
            _mpb = new MaterialPropertyBlock();
        }

        private void Update()
        {
            float dark = DayNightCycle.Instance != null ? DayNightCycle.Instance.Darkness : 0f;
            float on = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold - 0.15f, threshold + 0.2f, dark));
            float f = 1f;
            if (flicker)
            {
                f = 0.85f + 0.15f * Mathf.PerlinNoise(_seed, Time.time * 6f) + 0.05f * Mathf.Sin(Time.time * 23f + _seed);
            }
            float intensity = Mathf.Lerp(dayIntensity, nightIntensity, on) * f;
            if (targetLight != null)
            {
                targetLight.intensity = intensity;
                targetLight.enabled = intensity > 0.01f;
            }
            if (glow != null)
            {
                float g = Mathf.Lerp(flicker ? 0.35f : 0f, 1f, on) * f;
                glow.enabled = g > 0.01f;
                glow.transform.localScale = _glowScale * (0.6f + 0.4f * g);
                glow.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", glow.sharedMaterial.GetColor("_BaseColor") * g);
                glow.SetPropertyBlock(_mpb);
            }
        }
    }
}
