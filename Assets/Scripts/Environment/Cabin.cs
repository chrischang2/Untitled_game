using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UntitledGame.CameraControl;
using UntitledGame.Core;
using UntitledGame.Player;

namespace UntitledGame.Environment
{
    /// <summary>
    /// The log cabin you can walk into. The door swings open when someone comes near it. Inside, the roof
    /// and the walls between the camera and the player are hidden (they still cast shadows), the camera looks
    /// down over the walls, and a warm light comes on. Also tells walkers where the floor is and how to get
    /// in and out through the door (the cat walks in straight lines otherwise).
    /// Built by SceneBuilder.BuildCabin; positions below are in the cabin's local units (one floor tile = 1).
    /// </summary>
    public class Cabin : MonoBehaviour
    {
        public const float HalfWidth = 1.5f;   // x: three floor tiles
        public const float HalfDepth = 1.0f;   // z: two floor tiles; the front (door) wall is at +z
        public const float FloorTop = 0.075f;
        public const float DoorX = 0f;

        [SerializeField] private float doorOpenAngle = -100f;
        [SerializeField] private float doorTriggerDistance = 2.4f;
        [SerializeField] private float insideCameraDistance = 6f;
        [SerializeField] private float insideMinPitch = 50f;

        private static readonly List<Cabin> All = new List<Cabin>();
        /// <summary>The player's house (the first cabin), or null.</summary>
        public static Cabin PlayerHouse => All.Count > 0 ? All[0] : null;

        private readonly List<Transform> _doors = new List<Transform>(); // each region's house has its own leaf
        private float _doorAngle;
        private bool _doorWasOpen;
        private readonly Dictionary<string, Renderer[]> _sides = new Dictionary<string, Renderer[]>();
        private Renderer[] _roof;
        private Light _interiorLight;
        private Transform _player;
        private CameraRig _rig;
        private bool _playerInside;

        public bool PlayerInside => _playerInside;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            foreach (var side in new[] { "Front", "Back", "Left", "Right" })
            {
                var t = transform.Find(side);
                _sides[side] = t != null ? t.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            }
            var roof = transform.Find("Roof");
            _roof = roof != null ? roof.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            var front = transform.Find("Front");
            if (front != null)
                foreach (var t in front.GetComponentsInChildren<Transform>(true))
                    if (t.name == "door") _doors.Add(t);
            var light = transform.Find("InteriorLight");
            if (light != null)
            {
                _interiorLight = light.GetComponent<Light>();
                if (_interiorLight != null) _interiorLight.enabled = false;
            }
        }

        private void Start()
        {
            var pc = FindFirstObjectByType<PlayerController>();
            if (pc != null) _player = pc.transform;
            _rig = FindFirstObjectByType<CameraRig>();
        }

        // ------------------------------------------------------------------ geometry helpers

        private Vector3 Local(Vector3 world) => transform.InverseTransformPoint(world);

        private bool Contains(Vector3 world, float margin = 0f)
        {
            Vector3 p = Local(world);
            return Mathf.Abs(p.x) < HalfWidth - margin && Mathf.Abs(p.z) < HalfDepth - margin;
        }

        /// <summary>A point just inside (or outside) the doorway.</summary>
        public Vector3 DoorPoint(bool inside) => transform.TransformPoint(new Vector3(DoorX, FloorTop, inside ? HalfDepth - 0.45f : HalfDepth + 0.45f));

        public float FloorY => transform.TransformPoint(new Vector3(0f, FloorTop, 0f)).y;

        /// <summary>A point on the floor, from -1..1 across the width (x) and depth (z, +1 = the door wall).</summary>
        public Vector3 FloorPoint(float x01, float z01) => transform.TransformPoint(new Vector3(x01 * HalfWidth, FloorTop, z01 * HalfDepth));

        /// <summary>The cabin floor's height when <paramref name="p"/> is inside a cabin.</summary>
        public static bool TryFloorHeight(Vector3 p, out float y)
        {
            foreach (var c in All)
            {
                if (c.Contains(p, -0.05f))
                {
                    y = c.FloorY;
                    return true;
                }
            }
            y = 0f;
            return false;
        }

        /// <summary>Where someone standing at <paramref name="p"/> has their feet: cabin floor, dock deck or terrain.</summary>
        public static float GroundHeight(Vector3 p)
        {
            if (TryFloorHeight(p, out float floor)) return floor;
            if (WorldShape.IsOnDock(p.x, p.z)) return WorldShape.DockDeckHeight;
            return Mathf.Max(WorldShape.TerrainHeight(p.x, p.z), WorldShape.WaterLevel - 0.3f);
        }

        public static bool IsInside(Vector3 p)
        {
            foreach (var c in All)
                if (c.Contains(p)) return true;
            return false;
        }

        /// <summary>
        /// Where to walk next to get from <paramref name="from"/> to <paramref name="goal"/> without going through
        /// a cabin wall: the doorway when one is inside and the other isn't, otherwise the goal itself.
        /// </summary>
        public static Vector3 Waypoint(Vector3 from, Vector3 goal)
        {
            foreach (var c in All)
            {
                bool a = c.Contains(from, -0.25f), b = c.Contains(goal, -0.25f);
                if (a == b) continue;
                Vector3 near = c.DoorPoint(a), far = c.DoorPoint(!a);
                Vector3 flat = near - from;
                flat.y = 0f;
                return flat.magnitude > 0.3f ? near : far;
            }
            return goal;
        }

        // ------------------------------------------------------------------ behaviour

        private void Update()
        {
            UpdateDoor();
            bool inside = _player != null && Contains(_player.position, 0.05f);
            if (inside != _playerInside) SetPlayerInside(inside);
            if (_playerInside) UpdateCutaway();
        }

        private void UpdateDoor()
        {
            if (_doors.Count == 0) return;
            Vector3 doorway = transform.TransformPoint(new Vector3(DoorX, 0f, HalfDepth));
            bool open = false;
            // Anyone nearby: the player, Mei or Tangyuan.
            if (_player != null && Flat(_player.position - doorway) < doorTriggerDistance) open = true;
            var mei = Companion.CompanionBrain.Current;
            if (!open && mei != null && Flat(mei.transform.position - doorway) < doorTriggerDistance) open = true;
            var cat = Home.PetController.Instance;
            if (!open && cat != null && Flat(cat.transform.position - doorway) < doorTriggerDistance * 0.7f) open = true;

            if (open != _doorWasOpen)
            {
                _doorWasOpen = open;
                AudioManager.Instance?.PlayAt(open ? "SFX/rpg_creak1" : "SFX/rpg_creak2", doorway, 0.35f, 0.1f);
            }
            _doorAngle = Mathf.MoveTowards(_doorAngle, open ? doorOpenAngle : 0f, 260f * Time.deltaTime);
            foreach (var d in _doors) d.localRotation = Quaternion.Euler(0f, _doorAngle, 0f);
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private void SetPlayerInside(bool inside)
        {
            _playerInside = inside;
            ChatAudit.Write("WORLD", inside ? "player went into the cabin" : "player left the cabin");
            if (_interiorLight != null) _interiorLight.enabled = inside;
            foreach (var r in _roof) r.shadowCastingMode = inside ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            if (!inside)
                foreach (var side in _sides.Values)
                    foreach (var r in side) r.shadowCastingMode = ShadowCastingMode.On;
            if (_rig != null)
            {
                _rig.MinPitchOverride = inside ? insideMinPitch : (float?)null;
                // Don't fight a shop conversation's zoom (there are no shops in here, but be polite).
                if (inside && _rig.DistanceOverride == null) _rig.DistanceOverride = insideCameraDistance;
                else if (!inside && _rig.DistanceOverride == insideCameraDistance) _rig.DistanceOverride = null;
            }
        }

        /// <summary>Hide (shadows only) the walls on the camera's side so the player stays visible.</summary>
        private void UpdateCutaway()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 toCam = transform.InverseTransformDirection(cam.transform.position - transform.position);
            toCam.y = 0f;
            toCam.Normalize();
            SetSide("Front", toCam.z > 0.2f);
            SetSide("Back", toCam.z < -0.2f);
            SetSide("Right", toCam.x > 0.2f);
            SetSide("Left", toCam.x < -0.2f);
        }

        private void SetSide(string side, bool hidden)
        {
            var mode = hidden ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in _sides[side]) r.shadowCastingMode = mode;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();
    }
}
