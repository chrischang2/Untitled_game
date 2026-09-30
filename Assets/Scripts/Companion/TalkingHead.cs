using UnityEngine;
using UntitledGame.Player;

namespace UntitledGame.Companion
{
    /// <summary>Idle animation plus a head-bob driven by the character's voice (used by shopkeepers).</summary>
    public class TalkingHead : MonoBehaviour
    {
        [SerializeField] private CharacterAnimator animator;
        [SerializeField] private CharacterVoice voice;

        private Transform _head;
        private Quaternion _lastFinal = Quaternion.identity;
        private Quaternion _lastOffset = Quaternion.identity;
        private float _nextGesture;

        public void Configure(CharacterAnimator anim, CharacterVoice v)
        {
            animator = anim;
            voice = v;
        }

        private void Start()
        {
            if (animator != null)
            {
                _head = animator.FindBone("head");
                animator.Play("idle", 0f);
            }
        }

        private void Update()
        {
            if (animator == null || voice == null) return;
            if (voice.IsSpeaking && Time.time > _nextGesture && !animator.IsPlayingOneShot)
            {
                _nextGesture = Time.time + Random.Range(5f, 10f);
                if (Random.value < 0.5f) animator.PlayOnce(Random.value < 0.5f ? "interact-right" : "emote-yes", "idle", 0.2f);
            }
        }

        private void LateUpdate()
        {
            if (_head == null || voice == null) return;
            Quaternion current = _head.localRotation;
            Quaternion baseRot = Quaternion.Angle(current, _lastFinal) < 0.01f ? _lastFinal * Quaternion.Inverse(_lastOffset) : current;
            float a = voice.Amplitude;
            float t = Time.time;
            Quaternion offset = Quaternion.Euler(a * 8f * Mathf.Sin(t * 9f), a * 5f * Mathf.Sin(t * 4.1f), a * 3f * Mathf.Sin(t * 6.3f));
            _head.localRotation = baseRot * offset;
            _lastFinal = _head.localRotation;
            _lastOffset = offset;
        }
    }
}
