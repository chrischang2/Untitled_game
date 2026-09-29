using System.Collections;
using UnityEngine;

namespace UntitledGame.Player
{
    /// <summary>Thin wrapper over the legacy Animation clips that ship inside Kenney's character FBXs.</summary>
    public class CharacterAnimator : MonoBehaviour
    {
        [SerializeField] private Animation anim;

        private string _current;
        private Coroutine _oneShot;

        public string Current => _current;
        public Animation Animation => anim;

        private void Awake()
        {
            if (anim == null) anim = GetComponentInChildren<Animation>();
            if (anim != null) anim.cullingType = AnimationCullingType.AlwaysAnimate;
        }

        public void SetAnimation(Animation a) => anim = a;

        public bool Has(string clip) => anim != null && anim[clip] != null;

        public void Play(string clip, float fade = 0.2f, float speed = 1f)
        {
            if (!Has(clip)) return;
            anim[clip].speed = speed;
            if (_current == clip) return;
            if (_oneShot != null)
            {
                StopCoroutine(_oneShot);
                _oneShot = null;
            }
            anim.CrossFade(clip, fade);
            _current = clip;
        }

        /// <summary>Plays a non-looping clip, then crossfades to <paramref name="then"/>.</summary>
        public void PlayOnce(string clip, string then, float fade = 0.15f, float speed = 1f)
        {
            if (!Has(clip)) return;
            if (_oneShot != null) StopCoroutine(_oneShot);
            anim[clip].speed = speed;
            anim[clip].time = 0f;
            anim.CrossFade(clip, fade);
            _current = clip;
            _oneShot = StartCoroutine(ReturnAfter(clip, then, anim[clip].length / Mathf.Max(0.01f, speed)));
        }

        public bool IsPlayingOneShot => _oneShot != null;

        private IEnumerator ReturnAfter(string clip, string then, float seconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, seconds - 0.1f));
            _oneShot = null;
            if (_current == clip)
            {
                _current = null;
                Play(then, 0.25f);
            }
        }

        public Transform FindBone(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == boneName) return t;
            }
            return null;
        }
    }
}
