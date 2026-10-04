using System.Collections.Generic;
using UnityEngine;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.Player;

namespace UntitledGame.Companion
{
    /// <summary>
    /// Mei's body: follows the player along their footsteps (so she never walks into the lake),
    /// sits down beside them while they fish, turns to face them when chatting and bobs her head
    /// in time with her voice.
    /// </summary>
    public class CompanionController : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float followDistance = 2.4f;
        [SerializeField] private float moveSpeed = 3.8f;
        [SerializeField] private float turnSpeed = 400f;
        [SerializeField] private CharacterAnimator animator;
        [SerializeField] private CharacterVoice voice;
        [SerializeField] private FishingController playerFishing;

        private readonly List<Vector3> _crumbs = new List<Vector3>();
        private Vector3 _lastCrumb;
        private Vector3? _sitSpot;
        private bool _sitting;
        private bool _moving;
        private float _facePlayerUntil;
        private Transform _head;
        private Quaternion _lastFinal = Quaternion.identity;
        private Quaternion _lastOffset = Quaternion.identity;
        private float _idleLookTimer;
        private float _stuckTime;
        private bool _wantedToMove;

        public bool IsSitting => _sitting;

        public void SetTarget(Transform newTarget) => target = newTarget;

        public void Configure(Transform player, CharacterAnimator anim, CharacterVoice v, FishingController fishing)
        {
            target = player;
            animator = anim;
            voice = v;
            playerFishing = fishing;
        }

        private void Start()
        {
            if (animator == null) animator = GetComponentInChildren<CharacterAnimator>();
            if (voice == null) voice = GetComponent<CharacterVoice>();
            if (animator != null) _head = animator.FindBone("head");
            if (target != null) _lastCrumb = target.position;
        }

        public void FacePlayerFor(float seconds) => _facePlayerUntil = Time.time + seconds;

        public void Celebrate()
        {
            if (animator == null) return;
            string back = _sitting ? "sit" : "idle";
            animator.PlayOnce("emote-yes", back, 0.15f, 1.1f);
            FacePlayerFor(4f);
        }

        private void Update()
        {
            if (target == null) return;

            // Breadcrumbs of where the player actually walked.
            if (Vector3.Distance(target.position, _lastCrumb) > 0.5f)
            {
                _lastCrumb = target.position;
                _crumbs.Add(_lastCrumb);
                if (_crumbs.Count > 200) _crumbs.RemoveAt(0);
            }

            Vector3 toPlayer = target.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            // The player is out in the boat or on the island: wait on the mainland (no following across the water).
            if (Fishing.Rowboat.PlayerAboard || WorldShape.OnIsland(target.position.x, target.position.z, 6f))
            {
                _crumbs.Clear();
                _lastCrumb = target.position;
                _moving = false;
                _sitting = false;
                _sitSpot = null;
                Face(toPlayer);
                SnapToGround();
                UpdateAnimation(voice != null && voice.IsSpeaking);
                return;
            }
            if (dist > 32f) TeleportNearPlayer();

            bool fishing = playerFishing != null && playerFishing.State != FishingState.Idle;
            _moving = false;
            _wantedToMove = false;

            if (fishing)
            {
                if (_sitSpot == null) _sitSpot = ChooseSitSpot();
                Vector3 spot = _sitSpot.Value;
                Vector3 to = spot - transform.position;
                to.y = 0f;
                if (to.magnitude > 0.25f)
                {
                    _sitting = false;
                    Step(to, Mathf.Clamp(to.magnitude, 1.5f, moveSpeed));
                }
                else
                {
                    _sitting = true;
                    Vector3 look = target.forward;
                    look.y = 0f;
                    Face(look);
                }
            }
            else
            {
                _sitSpot = null;
                _sitting = false;
                FollowCrumbs(dist);
            }

            // Blocked by water (e.g. the player got ahead via a path she can't follow): hop over.
            _stuckTime = _wantedToMove && !_moving ? _stuckTime + Time.deltaTime : 0f;
            if (_stuckTime > 2.5f && dist > 3f)
            {
                _stuckTime = 0f;
                TeleportNearPlayer();
            }

            bool talking = voice != null && voice.IsSpeaking;
            if (!_moving && !_sitting && (talking || Time.time < _facePlayerUntil) && dist < 12f) Face(toPlayer);
            else if (!_moving && !_sitting && !talking)
            {
                _idleLookTimer -= Time.deltaTime;
                if (_idleLookTimer <= 0f)
                {
                    _idleLookTimer = Random.Range(4f, 9f);
                    _facePlayerUntil = Random.value < 0.5f ? Time.time + 2.5f : 0f;
                }
            }

            SnapToGround();
            UpdateAnimation(talking);
        }

        private void FollowCrumbs(float distToPlayer)
        {
            // Drop crumbs we've reached or passed.
            while (_crumbs.Count > 0 && FlatDistance(_crumbs[0], transform.position) < 0.45f) _crumbs.RemoveAt(0);
            if (distToPlayer <= followDistance)
            {
                return;
            }
            // Path length remaining along crumbs to the player.
            Vector3 next = _crumbs.Count > 0 ? _crumbs[0] : target.position;
            Vector3 to = next - transform.position;
            to.y = 0f;
            float speed = moveSpeed * Mathf.Lerp(1f, 1.8f, Mathf.InverseLerp(5f, 14f, distToPlayer));
            if (to.sqrMagnitude > 0.0001f) Step(to, speed);
        }

        private void Step(Vector3 direction, float speed)
        {
            _wantedToMove = true;
            Vector3 dir = direction.normalized;
            Vector3 next = transform.position + dir * speed * Time.deltaTime;
            if (!PlayerController.IsWalkable(next)) return;
            transform.position = next;
            Face(dir);
            _moving = true;
        }

        private void Face(Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, turnSpeed * Time.deltaTime);
        }

        private Vector3 ChooseSitSpot()
        {
            Vector3 right = target.right;
            Vector3 back = -target.forward;
            Vector3[] candidates =
            {
                target.position + right * 0.95f + back * 0.35f,
                target.position - right * 0.95f + back * 0.35f,
                target.position + right * 1.5f + back * 0.3f,
                target.position - right * 1.5f + back * 0.3f,
                target.position + right * 1.2f + back * 1.0f,
                target.position - right * 1.2f + back * 1.0f,
                target.position + back * 1.6f,
            };
            Vector3 best = transform.position;
            float bestScore = float.MaxValue;
            foreach (var c in candidates)
            {
                if (!PlayerController.IsWalkable(c)) continue;
                // Prefer spots beside the player (earlier candidates), then closeness.
                float score = FlatDistance(c, transform.position) + System.Array.IndexOf(candidates, c) * 1.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        private void SnapToGround()
        {
            Vector3 p = transform.position;
            float y = Cabin.GroundHeight(p);
            p.y = Mathf.Lerp(p.y, y, 1f - Mathf.Exp(-20f * Time.deltaTime));
            transform.position = p;
        }

        /// <summary>Puts Mei right next to the player (used when a save puts the player somewhere else).</summary>
        public void Warp()
        {
            if (target == null) return;
            _sitting = false;
            _sitSpot = null;
            _moving = false;
            TeleportNearPlayer();
            Vector3 p = transform.position;
            p.y = Cabin.GroundHeight(p);
            transform.position = p;
            Vector3 face = target.position - p;
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face.normalized);
        }

        private void TeleportNearPlayer()
        {
            Vector3[] options = { target.position - target.forward * 2f, target.position - target.forward * 1.2f + target.right * 0.9f, target.position - target.forward * 1.2f - target.right * 0.9f };
            Vector3 p = target.position;
            bool playerInside = Cabin.IsInside(target.position);
            foreach (var o in options)
            {
                // Stay on the player's side of the cabin walls.
                if (PlayerController.IsWalkable(o) && Cabin.IsInside(o) == playerInside) { p = o; break; }
            }
            transform.position = p;
            _crumbs.Clear();
            _lastCrumb = target.position;
        }

        private void UpdateAnimation(bool talking)
        {
            if (animator == null || animator.IsPlayingOneShot) return;
            if (_moving) animator.Play(moveSpeedNow > 4.5f ? "sprint" : "walk", 0.15f, 1.2f);
            else if (_sitting) animator.Play("sit", 0.35f);
            else animator.Play("idle", 0.3f);
        }

        private float moveSpeedNow => _moving ? moveSpeed * 1.2f : 0f;

        private void LateUpdate()
        {
            if (_head == null || voice == null) return;
            // Talking head-bob driven by voice amplitude (layered on top of the animation pose).
            Quaternion current = _head.localRotation;
            Quaternion baseRot = Quaternion.Angle(current, _lastFinal) < 0.01f ? _lastFinal * Quaternion.Inverse(_lastOffset) : current;
            float a = voice.Amplitude;
            float t = Time.time;
            Quaternion offset = Quaternion.Euler(a * 9f * Mathf.Sin(t * 10f), a * 5f * Mathf.Sin(t * 4.3f), a * 3f * Mathf.Sin(t * 6.1f));
            _head.localRotation = baseRot * offset;
            _lastFinal = _head.localRotation;
            _lastOffset = offset;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
