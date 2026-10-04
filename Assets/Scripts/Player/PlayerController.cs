using UnityEngine;
using UntitledGame.CameraControl;
using UntitledGame.Core;
using UntitledGame.Environment;

namespace UntitledGame.Player
{
    /// <summary>
    /// Camera-relative walking around the lake. Keeps the player on land or the dock (no wading into
    /// deep water), drives walk/idle animations and footsteps. Fishing borrows control via
    /// <see cref="ActionAnimation"/> and the movement gate.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4.2f;
        [SerializeField] private float turnSpeed = 720f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float sprintMultiplier = 1.55f;
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private CharacterAnimator animator;

        private CharacterController _controller;
        private Vector3 _verticalVelocity;
        private float _stepDistance;
        private Vector3 _lastPos;

        /// <summary>When set, overrides idle/walk (e.g. "holding-right" while fishing).</summary>
        public string ActionAnimation { get; set; }
        public bool IsMoving { get; private set; }
        public float Speed { get; private set; }
        public CharacterAnimator Animator => animator;
        public bool OnDock => WorldShape.IsOnDock(transform.position.x, transform.position.z);

        /// <summary>Set while sitting in the rowboat: no walking, gravity or footsteps; the boat moves us.</summary>
        public Transform Seat { get; set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (animator == null) animator = GetComponentInChildren<CharacterAnimator>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraRig>();
            _lastPos = transform.position;
        }

        public void Configure(CameraRig rig, CharacterAnimator anim)
        {
            cameraRig = rig;
            animator = anim;
        }

        private void Update()
        {
            if (Seat != null)
            {
                IsMoving = false;
                Speed = 0f;
                _lastPos = transform.position;
                if (animator != null && !animator.IsPlayingOneShot)
                    animator.Play(string.IsNullOrEmpty(ActionAnimation) ? "sit" : ActionAnimation, 0.2f);
                return;
            }
            Vector2 input = Vector2.zero;
            bool sprint = false;
            if (!InputGate.MovementBlocked)
            {
                input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                sprint = Input.GetKey(KeyCode.LeftShift);
            }

            float yaw = cameraRig != null ? cameraRig.Yaw : 0f;
            Vector3 move = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            move = Vector3.ClampMagnitude(move, 1f);

            if (move.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }

            float speed = moveSpeed * (sprint ? sprintMultiplier : 1f);
            Vector3 horizontal = move * speed;
            horizontal = ConstrainToWalkable(horizontal * Time.deltaTime) / Mathf.Max(Time.deltaTime, 1e-5f);

            if (_controller.isGrounded && _verticalVelocity.y < 0f) _verticalVelocity.y = -2f;
            _verticalVelocity.y += gravity * Time.deltaTime;

            _controller.Move((horizontal + _verticalVelocity) * Time.deltaTime);

            Vector3 delta = transform.position - _lastPos;
            delta.y = 0f;
            _lastPos = transform.position;
            Speed = delta.magnitude / Mathf.Max(Time.deltaTime, 1e-5f);
            IsMoving = Speed > 0.3f && move.sqrMagnitude > 0.01f;

            UpdateAnimation(sprint);
            UpdateFootsteps(delta.magnitude, sprint);
        }

        private Vector3 ConstrainToWalkable(Vector3 step)
        {
            if (step.sqrMagnitude < 1e-8f) return step;
            Vector3 p = transform.position;
            if (IsWalkable(p + step)) return step;
            var sx = new Vector3(step.x, 0f, 0f);
            if (IsWalkable(p + sx)) return sx;
            var sz = new Vector3(0f, 0f, step.z);
            if (IsWalkable(p + sz)) return sz;
            return Vector3.zero;
        }

        public static bool IsWalkable(Vector3 p)
        {
            if (WorldShape.OnIsland(p.x, p.z)) return WorldShape.TerrainHeight(p.x, p.z) > WorldShape.WaterLevel - 0.3f;
            if (new Vector2(p.x, p.z).magnitude > WorldShape.PlayableRadius) return false;
            if (WorldShape.IsOnDock(p.x, p.z, -0.25f)) return true;
            return WorldShape.TerrainHeight(p.x, p.z) > WorldShape.WaterLevel - 0.3f;
        }

        public void FaceTowards(Vector3 worldPoint, bool instant = false)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = instant ? rot : Quaternion.RotateTowards(transform.rotation, rot, turnSpeed * Time.deltaTime);
        }

        public void Teleport(Vector3 position, float yawDegrees)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            _controller.enabled = true;
            _lastPos = position;
        }

        private void UpdateAnimation(bool sprint)
        {
            if (animator == null || animator.IsPlayingOneShot) return;
            if (!string.IsNullOrEmpty(ActionAnimation))
            {
                animator.Play(ActionAnimation, 0.2f);
                return;
            }
            if (IsMoving) animator.Play(sprint ? "sprint" : "walk", 0.15f, sprint ? 1.1f : 1.2f);
            else animator.Play("idle", 0.25f);
        }

        private void UpdateFootsteps(float moved, bool sprint)
        {
            if (!IsMoving) return;
            _stepDistance += moved;
            float stride = sprint ? 0.9f : 0.62f;
            if (_stepDistance < stride) return;
            _stepDistance = 0f;
            var am = AudioManager.Instance;
            if (am == null) return;
            string set = OnDock ? "footstep_wood_00" : "footstep_grass_00";
            am.PlayClip(am.RandomClip("SFX", set), OnDock ? 0.35f : 0.22f, 0.1f);
        }
    }
}
