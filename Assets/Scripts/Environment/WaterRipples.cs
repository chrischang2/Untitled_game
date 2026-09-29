using UnityEngine;

namespace UntitledGame.Environment
{
    /// <summary>Feeds expanding ripple rings (bobber plops, bites, fish jumps) to the water shader.</summary>
    public static class WaterRipples
    {
        private const int Max = 8;
        private static readonly Vector4[] Ripples = new Vector4[Max];
        private static int _next;
        private static readonly int RipplesId = Shader.PropertyToID("_ComfyRipples");

        public static void Spawn(Vector3 worldPos, float strength = 1f)
        {
            Ripples[_next] = new Vector4(worldPos.x, worldPos.z, Time.timeSinceLevelLoad, Mathf.Clamp01(strength));
            _next = (_next + 1) % Max;
            Shader.SetGlobalVectorArray(RipplesId, Ripples);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetRipples()
        {
            for (int i = 0; i < Max; i++) Ripples[i] = Vector4.zero;
            _next = 0;
            Shader.SetGlobalVectorArray(RipplesId, Ripples);
        }
    }
}
