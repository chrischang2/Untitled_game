using UnityEngine;
using UntitledGame.Core;

namespace UntitledGame.Environment
{
    /// <summary>Occasional fish jumping out of the lake near the player: splash, ripple, plop.</summary>
    public class LakeLife : MonoBehaviour
    {
        [SerializeField] private Transform focus;
        [SerializeField] private float minInterval = 6f;
        [SerializeField] private float maxInterval = 16f;

        private float _next;

        public void SetFocus(Transform t) => focus = t;

        private void Start() => _next = Time.time + Random.Range(minInterval, maxInterval);

        private void Update()
        {
            if (Time.time < _next || focus == null) return;
            _next = Time.time + Random.Range(minInterval, maxInterval);

            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 r = Random.insideUnitCircle * 22f;
                Vector3 p = new Vector3(focus.position.x + r.x, 0f, focus.position.z + r.y);
                if (!WorldShape.IsWater(p.x, p.z) || WorldShape.WaterDepth(p.x, p.z) < 1f) continue;
                if (WorldShape.IsOnDock(p.x, p.z, 1f)) continue;
                Effects.Splash(p, 0.6f);
                WaterRipples.Spawn(p, 0.8f);
                AudioManager.Instance?.PlayClipAt(AudioManager.Instance.RandomClip("SFX", "splash_0"), p, 0.35f);
                break;
            }
        }
    }
}
