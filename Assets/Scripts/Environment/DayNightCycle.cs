using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UntitledGame.Environment
{
    public enum DayPhase { Night, Dawn, Morning, Day, Evening, Dusk }

    /// <summary>
    /// Drives time of day: sun/moon light, sky palette (shader globals), ambient probe and fog.
    /// One in-game hour lasts <see cref="realSecondsPerHour"/> real seconds.
    /// </summary>
    [ExecuteAlways]
    public class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        [SerializeField, Range(0f, 24f)] private float timeOfDay = 7.5f;
        [SerializeField] private float realSecondsPerHour = 60f;
        [SerializeField] private bool paused;
        [SerializeField] private Light sun;
        [SerializeField] private Material skyMaterial;

        [Header("Palette (evaluated over 0..24h)")]
        [SerializeField] private PaletteTrack skyZenith;
        [SerializeField] private PaletteTrack skyHorizon;
        [SerializeField] private PaletteTrack sunColor;
        [SerializeField] private PaletteTrack ambientColor;
        [SerializeField] private PaletteTrack fogColor;
        [SerializeField] private PaletteTrack cloudColor;
        [SerializeField] private AnimationCurve sunIntensity;

        public float TimeOfDay { get => timeOfDay; set => timeOfDay = Mathf.Repeat(value, 24f); }
        public float RealSecondsPerHour { get => realSecondsPerHour; set => realSecondsPerHour = Mathf.Max(1f, value); }
        public bool Paused { get => paused; set => paused = value; }
        public int Day { get; set; } = 1;

        /// <summary>0 at full day, 1 at full night.</summary>
        public float Darkness { get; private set; }
        public float CloudCover { get; set; } = 0.25f;

        public event Action<int> NewDay;

        public DayPhase Phase
        {
            get
            {
                float t = timeOfDay;
                if (t < 4.5f || t >= 21f) return DayPhase.Night;
                if (t < 6.5f) return DayPhase.Dawn;
                if (t < 10f) return DayPhase.Morning;
                if (t < 17f) return DayPhase.Day;
                if (t < 19.5f) return DayPhase.Evening;
                return DayPhase.Dusk;
            }
        }

        public string ClockText
        {
            get
            {
                int h = Mathf.FloorToInt(timeOfDay);
                int m = Mathf.FloorToInt((timeOfDay - h) * 60f / 10f) * 10;
                string ampm = h < 12 ? "AM" : "PM";
                int h12 = h % 12 == 0 ? 12 : h % 12;
                return $"{h12}:{m:00} {ampm}";
            }
        }

        public string PhaseDescription => Phase switch
        {
            DayPhase.Night => "night",
            DayPhase.Dawn => "dawn",
            DayPhase.Morning => "morning",
            DayPhase.Day => "afternoon",
            DayPhase.Evening => "evening",
            _ => "dusk",
        };

        private static readonly int SkyHorizonId = Shader.PropertyToID("_ComfySkyHorizon");
        private static readonly int SkyZenithId = Shader.PropertyToID("_ComfySkyZenith");
        private static readonly int SkyGroundId = Shader.PropertyToID("_ComfySkyGround");
        private static readonly int SunColorId = Shader.PropertyToID("_ComfySunColor");
        private static readonly int SunDirId = Shader.PropertyToID("_ComfySunDir");
        private static readonly int MoonDirId = Shader.PropertyToID("_ComfyMoonDir");
        private static readonly int CloudColorId = Shader.PropertyToID("_ComfyCloudColor");
        private static readonly int StarsId = Shader.PropertyToID("_ComfyStars");
        private static readonly int CloudCoverId = Shader.PropertyToID("_ComfyCloudCover");

        private void OnEnable()
        {
            Instance = this;
            if (skyZenith == null || skyZenith.Count < 2 || sunIntensity == null) ResetPalette();
            Apply();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Application.isPlaying && !paused)
            {
                float before = timeOfDay;
                timeOfDay += Time.deltaTime / realSecondsPerHour;
                if (timeOfDay >= 24f)
                {
                    timeOfDay -= 24f;
                    Day++;
                    NewDay?.Invoke(Day);
                }
                else if (before < 6f && timeOfDay >= 6f)
                {
                    // Nothing special; hook for future "morning" events.
                }
            }
            Apply();
        }

        private void OnValidate() => Apply();

        /// <summary>Jump to an hour on a given day (sleeping, passing out). Fires NewDay if the day changed.</summary>
        public void SkipTo(int day, float hour)
        {
            bool newDay = day != Day;
            Day = Mathf.Max(1, day);
            timeOfDay = Mathf.Repeat(hour, 24f);
            if (newDay) NewDay?.Invoke(Day);
            Apply();
        }

        public void Apply()
        {
            if (skyZenith == null || skyZenith.Count < 2) return;
            float t01 = timeOfDay / 24f;

            // Sun arcs east -> south -> west; rises ~5:30, sets ~19:30.
            float sunAngle = (timeOfDay - 6f) / 14f * 180f; // 0 at 6:00 horizon, 180 at 20:00
            Vector3 sunDir = Quaternion.Euler(0f, -35f, 0f) * (Quaternion.AngleAxis(sunAngle, Vector3.forward) * Vector3.right);
            sunDir = new Vector3(sunDir.x, sunDir.y, sunDir.z - 0.35f).normalized; // tilt towards the south
            Vector3 moonDir = new Vector3(-sunDir.x, Mathf.Abs(sunDir.y) * 0.8f + 0.25f, -sunDir.z).normalized;

            float rain = Application.isPlaying && Weather.Instance != null ? Weather.Instance.RainAmount : 0f;
            float sunHeight = sunDir.y;
            Darkness = Mathf.Clamp01(1f - Mathf.InverseLerp(-0.12f, 0.15f, sunHeight));

            Color zenith = skyZenith.Evaluate(t01);
            Color horizon = skyHorizon.Evaluate(t01);
            Color sunCol = sunColor.Evaluate(t01);
            Color amb = ambientColor.Evaluate(t01);
            Color fog = fogColor.Evaluate(t01);
            Color grey = new Color(0.62f, 0.66f, 0.7f) * Mathf.Lerp(1f, 0.35f, Darkness);
            fog = Color.Lerp(fog, grey, rain * 0.7f);
            horizon = Color.Lerp(horizon, grey, rain * 0.6f);
            zenith = Color.Lerp(zenith, grey * 0.85f, rain * 0.6f);
            Color cloud = cloudColor.Evaluate(t01);

            if (sun != null)
            {
                bool useMoon = Darkness > 0.5f;
                Vector3 lightFrom = useMoon ? moonDir : sunDir;
                sun.transform.rotation = Quaternion.LookRotation(-lightFrom, Vector3.up);
                float intensity = useMoon
                    ? Mathf.Lerp(0f, 0.28f, Mathf.InverseLerp(0.5f, 0.85f, Darkness))
                    : sunIntensity.Evaluate(t01);
                sun.intensity = intensity * Mathf.Lerp(1f, 0.4f, rain);
                sun.color = useMoon ? new Color(0.62f, 0.72f, 1f) : sunCol;
                sun.shadowStrength = useMoon ? 0.55f : 0.85f;
            }

            Shader.SetGlobalColor(SkyZenithId, zenith);
            Shader.SetGlobalColor(SkyHorizonId, horizon);
            Shader.SetGlobalColor(SkyGroundId, Color.Lerp(horizon, fog, 0.5f) * 0.8f);
            Shader.SetGlobalColor(SunColorId, sunCol * Mathf.Clamp01(1.2f - Darkness));
            Shader.SetGlobalVector(SunDirId, sunDir);
            Shader.SetGlobalVector(MoonDirId, moonDir);
            Shader.SetGlobalColor(CloudColorId, cloud);
            Shader.SetGlobalFloat(StarsId, Mathf.InverseLerp(0.55f, 1f, Darkness));
            Shader.SetGlobalFloat(CloudCoverId, CloudCover);

            RenderSettings.ambientMode = AmbientMode.Custom;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(amb * 0.75f);
            sh.AddDirectionalLight(Vector3.up, Color.Lerp(amb, zenith, 0.5f) * 0.55f, 1f);
            sh.AddDirectionalLight(Vector3.down, amb * 0.25f, 1f);
            RenderSettings.ambientProbe = sh;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = Mathf.Lerp(Mathf.Lerp(45f, 25f, Darkness), 12f, rain);
            RenderSettings.fogEndDistance = Mathf.Lerp(Mathf.Lerp(190f, 120f, Darkness), 90f, rain);

            if (skyMaterial != null) RenderSettings.skybox = skyMaterial;
        }

        [ContextMenu("Reset Palette")]
        public void ResetPalette()
        {
            // Keys are in 0..1 over the day. Warm dawn, clear blue day, peachy sunset, deep blue night.
            skyZenith = Grad(
                (0.00f, "#0B1330"), (0.21f, "#1B2A5A"), (0.26f, "#5C7FC0"), (0.35f, "#4F9BE0"),
                (0.65f, "#4A95DD"), (0.78f, "#5C6FB8"), (0.83f, "#2A2F66"), (0.90f, "#101a3c"), (1.00f, "#0B1330"));
            skyHorizon = Grad(
                (0.00f, "#1A2748"), (0.21f, "#3A3F6E"), (0.25f, "#F2A477"), (0.30f, "#FFD6A8"), (0.38f, "#CFE8F5"),
                (0.66f, "#D2EAF5"), (0.76f, "#FFC08A"), (0.80f, "#F58B6A"), (0.85f, "#5B4A7A"), (0.90f, "#22305A"), (1.00f, "#1A2748"));
            sunColor = Grad(
                (0.00f, "#6F7FB8"), (0.23f, "#FF9E6A"), (0.30f, "#FFE0B8"), (0.40f, "#FFF6E6"),
                (0.65f, "#FFF3E0"), (0.76f, "#FFC48A"), (0.81f, "#FF8A5C"), (0.86f, "#6F7FB8"), (1.00f, "#6F7FB8"));
            ambientColor = Grad(
                (0.00f, "#2B3560"), (0.22f, "#43487A"), (0.28f, "#A89AA8"), (0.36f, "#B9C7D6"),
                (0.66f, "#BCC9D6"), (0.77f, "#C79A88"), (0.83f, "#5B5580"), (0.90f, "#2F3866"), (1.00f, "#2B3560"));
            fogColor = Grad(
                (0.00f, "#1B2544"), (0.22f, "#3D4270"), (0.27f, "#E8B394"), (0.34f, "#D9E5EC"),
                (0.66f, "#D6E6EE"), (0.77f, "#F2B792"), (0.83f, "#5E4E7C"), (0.90f, "#222D52"), (1.00f, "#1B2544"));
            cloudColor = Grad(
                (0.00f, "#2A3358"), (0.22f, "#5A5A86"), (0.27f, "#FFC6A6"), (0.34f, "#FFFFFF"),
                (0.66f, "#FFFFFF"), (0.77f, "#FFC29E"), (0.82f, "#C6809A"), (0.88f, "#34385F"), (1.00f, "#2A3358"));
            sunIntensity = new AnimationCurve(
                new Keyframe(0.00f, 0f), new Keyframe(0.22f, 0f), new Keyframe(0.27f, 0.7f), new Keyframe(0.35f, 1.25f),
                new Keyframe(0.65f, 1.25f), new Keyframe(0.78f, 0.8f), new Keyframe(0.83f, 0f), new Keyframe(1f, 0f));
        }

        private static PaletteTrack Grad(params (float t, string hex)[] keys)
        {
            var track = new PaletteTrack { times = new float[keys.Length], colors = new Color[keys.Length] };
            for (int i = 0; i < keys.Length; i++)
            {
                ColorUtility.TryParseHtmlString(keys[i].hex, out Color c);
                track.times[i] = keys[i].t;
                track.colors[i] = c;
            }
            return track;
        }
    }

    /// <summary>Piecewise-linear colour track (Unity Gradients are limited to 8 keys).</summary>
    [Serializable]
    public class PaletteTrack
    {
        public float[] times = new float[0];
        public Color[] colors = new Color[0];

        public int Count => times?.Length ?? 0;

        public Color Evaluate(float t)
        {
            if (Count == 0) return Color.magenta;
            if (t <= times[0]) return colors[0];
            for (int i = 1; i < times.Length; i++)
            {
                if (t <= times[i])
                {
                    float k = Mathf.InverseLerp(times[i - 1], times[i], t);
                    return Color.Lerp(colors[i - 1], colors[i], k);
                }
            }
            return colors[colors.Length - 1];
        }
    }
}
