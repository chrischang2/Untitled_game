using UnityEngine;
using UntitledGame.Player;

namespace UntitledGame.Environment
{
    /// <summary>Idle-animates a little animal (Mochi the dock cat, a deer in the woods) with the odd flourish.</summary>
    public class AmbientCritter : MonoBehaviour
    {
        [SerializeField] private CharacterAnimator animator;
        [SerializeField] private string idleClip = "idle";
        [SerializeField] private string[] flourishes = { "eat", "gesture-positive" };
        [SerializeField] private float minInterval = 8f;
        [SerializeField] private float maxInterval = 20f;

        private float _next;

        public void Configure(CharacterAnimator anim, string idle, params string[] extra)
        {
            animator = anim;
            idleClip = idle;
            flourishes = extra;
        }

        private void Start()
        {
            if (animator == null) animator = GetComponentInChildren<CharacterAnimator>();
            animator?.Play(idleClip, 0f);
            _next = Time.time + Random.Range(minInterval, maxInterval);
        }

        private void Update()
        {
            if (animator == null || Time.time < _next) return;
            _next = Time.time + Random.Range(minInterval, maxInterval);
            if (flourishes == null || flourishes.Length == 0) return;
            string clip = flourishes[Random.Range(0, flourishes.Length)];
            if (animator.Has(clip)) animator.PlayOnce(clip, idleClip, 0.25f);
        }
    }
}
