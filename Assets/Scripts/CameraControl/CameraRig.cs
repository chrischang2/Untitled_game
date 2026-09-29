using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Environment;

namespace UntitledGame.CameraControl
{
    /// <summary>
    /// Relaxed third-person orbit camera. Right mouse drag to look around, scroll to zoom.
    /// Gameplay can nudge the focus point (e.g. towards the bobber) via <see cref="FocusOffset"/>.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float distance = 8.5f;
        [SerializeField] private float minDistance = 3.5f;
        [SerializeField] private float maxDistance = 18f;
        [SerializeField] private float yaw;
        [SerializeField] private float pitch = 24f;
        [SerializeField] private float minPitch = 5f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float focusHeight = 1.1f;
        [SerializeField] private float followSmooth = 0.18f;

        private Vector3 _focus;
        private Vector3 _focusVel;
        private float _targetDistance;
        private Vector3 _focusOffset;
        private bool _dragging;

        public float Yaw => yaw;
        public Transform Target => target;

        /// <summary>Additional offset for the look-at point, smoothly applied.</summary>
        public Vector3 FocusOffset { get; set; }

        /// <summary>Temporary zoom override (e.g. closer while talking); null to release.</summary>
        public float? DistanceOverride { get; set; }

        public void Configure(Transform t, float startYaw, float startPitch, float startDistance)
        {
            target = t;
            yaw = startYaw;
            pitch = startPitch;
            distance = startDistance;
            _targetDistance = startDistance;
            Snap();
        }

        private void Awake()
        {
            _targetDistance = distance;
            Snap();
        }

        public void Snap()
        {
            if (target == null) return;
            _focus = target.position + Vector3.up * focusHeight;
            _focusOffset = FocusOffset;
            Apply();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            bool allowInput = !InputGate.GameplayBlocked;
            float sens = SaveSystem.Settings.mouseSensitivity;
            if (allowInput && Input.GetMouseButton(1))
            {
                if (!_dragging)
                {
                    _dragging = true;
                    Cursor.lockState = CursorLockMode.Locked;
                }
                yaw += Input.GetAxis("Mouse X") * 3.2f * sens;
                pitch -= Input.GetAxis("Mouse Y") * 2.4f * sens;
            }
            else if (_dragging)
            {
                _dragging = false;
                Cursor.lockState = CursorLockMode.None;
            }

            if (allowInput)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.0001f) _targetDistance = Mathf.Clamp(_targetDistance * (1f - scroll * 1.2f), minDistance, maxDistance);
            }
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            float wantedDistance = DistanceOverride ?? _targetDistance;
            distance = Mathf.Lerp(distance, wantedDistance, 1f - Mathf.Exp(-4f * Time.deltaTime));

            _focusOffset = Vector3.Lerp(_focusOffset, FocusOffset, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
            Vector3 wanted = target.position + Vector3.up * focusHeight;
            _focus = Vector3.SmoothDamp(_focus, wanted, ref _focusVel, followSmooth);
            Apply();
        }

        private void Apply()
        {
            Vector3 focus = _focus + _focusOffset;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = focus - rot * Vector3.forward * distance;

            float ground = Mathf.Max(WorldShape.TerrainHeight(pos.x, pos.z), WorldShape.WaterLevel) + 0.6f;
            if (pos.y < ground) pos.y = ground;

            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(focus - pos, Vector3.up);
        }
    }
}
