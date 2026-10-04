using UnityEngine;
using UntitledGame.CameraControl;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Home;
using UntitledGame.Player;
using System.Linq;

namespace UntitledGame.Fishing
{
    /// <summary>
    /// Old Wang's rowboat, tied up at the dock. Once you've rented it from him (spoken, at the tackle shop), press F
    /// next to it to get in, row with WASD (camera-relative), fish from it as usual, and press F near the shore or the
    /// dock to get out. Big fish live far from the shore, so the boat is how you reach them. Mei and Tangyuan wait on land.
    /// </summary>
    public class Rowboat : Interactable
    {
        [SerializeField] private Transform model;
        [SerializeField] private float speed = 3.4f;
        [SerializeField] private float turnSpeed = 110f;

        public static Rowboat Instance { get; private set; }

        /// <summary>How far from the shore your boat can go before the currents push it back (best boat upgrade owned).</summary>
        public static float Range => Catalog.Items.Where(i => i.category == ItemCategory.Boat && i.boatRange > 0f && Inventory.Owns(i.id))
            .Select(i => i.boatRange).DefaultIfEmpty(25f).Max();

        public bool InCurrent { get; private set; }
        private float _lastCurrentToast = -999f;
        public static bool PlayerAboard => Instance != null && Instance._player != null;

        /// <summary>Lets the self-test row without a keyboard.</summary>
        public Vector2 SimulatedInput { get; set; }

        private PlayerController _player;
        private FishingController _fishing;
        private CameraRig _rig;
        private Vector3 _velocity;
        private Transform _seat;
        private float _modelYawOffset;
        private Vector3 _boardedFrom;

        public override string Prompt => Inventory.Owns("boat")
            ? "[F] Get in Old Wang's boat"
            : "Old Wang's boat: ask him at the tackle shop if you can rent it (租船)";
        public override bool Available => _player == null;

        private void Awake()
        {
            Instance = this;
            radius = 3.4f;
            if (model == null && transform.childCount > 0) model = transform.GetChild(0);
        }

        private void Start()
        {
            _homePos = transform.position;
            _homeRot = transform.rotation;
            // Seat in the middle of the hull; work out which way the model's bow points (its long axis).
            var rs = GetComponentsInChildren<Renderer>();
            Bounds b = rs.Length > 0 ? rs[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            _seat = new GameObject("Seat").transform;
            _seat.SetParent(transform, false);
            _seat.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.center.z);
            Vector3 localSize = transform.InverseTransformVector(b.size);
            _modelYawOffset = Mathf.Abs(localSize.x) > Mathf.Abs(localSize.z) ? 90f : 0f;
            _rig = FindFirstObjectByType<CameraRig>();
        }

        public void Configure(Transform boatModel) => model = boatModel;

        public override void Interact()
        {
            if (!Inventory.Owns("boat"))
            {
                GameEvents.Toast("This is Old Wang's boat. Ask him in Chinese if you can rent it (租船). He only lends it to people he knows.", 4.5f);
                return;
            }
            Board(FindFirstObjectByType<PlayerController>());
        }

        public void Board(PlayerController player)
        {
            if (player == null || _player != null) return;
            _player = player;
            _fishing = player.GetComponent<FishingController>();
            _boardedFrom = player.transform.position;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetParent(_seat, false);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.Euler(0f, -_modelYawOffset, 0f);
            player.Seat = _seat;
            _velocity = Vector3.zero;
            ChatAudit.Write("WORLD", "got into the rowboat");
            AudioManager.Instance?.PlaySfx("SFX/water_small", 0.5f);
            GameEvents.Toast("Row with WASD. Big fish live far from the shore. Press F near the shore or the dock to get out.", 4.5f);
        }

        /// <summary>Gets out at the nearest dry spot (dock or shore) within reach; false if there's none.</summary>
        public bool TryDisembark()
        {
            if (_player == null) return false;
            if (!FindLanding(out Vector3 landing))
            {
                GameEvents.Toast("Row closer to the shore or the dock to get out.");
                return false;
            }
            var p = _player;
            _player = null;
            p.Seat = null;
            p.transform.SetParent(null, true);
            p.Teleport(landing, p.transform.eulerAngles.y);
            ChatAudit.Write("WORLD", "got out of the rowboat");
            AudioManager.Instance?.PlaySfx("SFX/footstep_wood_001", 0.5f);
            return true;
        }

        public Vector3 SafeSpot => _player != null ? _boardedFrom : transform.position;

        private Vector3 _homePos;
        private Quaternion _homeRot;

        /// <summary>Back to its mooring by the dock (after passing out at sea, someone tows it home).</summary>
        public void ReturnToDock()
        {
            if (_player != null)
            {
                _player.Seat = null;
                _player.transform.SetParent(null, true);
                _player = null;
            }
            _velocity = Vector3.zero;
            transform.SetPositionAndRotation(_homePos, _homeRot);
        }

        /// <summary>Gets out if there's somewhere to land, without the "row closer" message (self-test).</summary>
        public bool TryDisembarkQuiet() => _player != null && FindLanding(out _) && TryDisembark();

        private bool FindLanding(out Vector3 landing)
        {
            landing = Vector3.zero;
            float best = float.MaxValue;
            Vector3 c = transform.position;
            for (float r = 1f; r <= 3.6f; r += 0.4f)
            for (int k = 0; k < 24; k++)
            {
                float a = k * Mathf.PI * 2f / 24f;
                Vector3 q = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                bool dock = WorldShape.IsOnDock(q.x, q.z, -0.3f);
                bool land = !WorldShape.IsWater(q.x, q.z) && WorldShape.TerrainHeight(q.x, q.z) > WorldShape.WaterLevel + 0.05f;
                if (!(dock || land) || !PlayerController.IsWalkable(q)) continue;
                if (r < best)
                {
                    best = r;
                    landing = new Vector3(q.x, (dock ? WorldShape.DockDeckHeight : WorldShape.TerrainHeight(q.x, q.z)) + 0.05f, q.z);
                }
            }
            return best < float.MaxValue;
        }

        private static bool Rowable(Vector3 p) =>
            WorldShape.WaterDepth(p.x, p.z) > 0.4f && WorldShape.ShoreDistance(p.x, p.z) < -1.0f && !WorldShape.IsOnDock(p.x, p.z, 0.9f);

        private void Update()
        {
            if (_player == null) return;
            if (!InputGate.GameplayBlocked && Input.GetKeyDown(KeyCode.F))
            {
                TryDisembark();
                return;
            }

            // No rowing while the line is out.
            bool fishing = _fishing != null && _fishing.State != FishingState.Idle;
            Vector2 input = SimulatedInput;
            if (input == Vector2.zero && !fishing && !InputGate.MovementBlocked)
                input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (fishing) input = Vector2.zero;

            float yaw = _rig != null ? _rig.Yaw : 0f;
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            wish = Vector3.ClampMagnitude(wish, 1f) * speed;
            _velocity = Vector3.MoveTowards(_velocity, wish, (wish.sqrMagnitude > 0.01f ? 3f : 1.6f) * Time.deltaTime);

            // Currents: past the boat's range the sea pushes you back towards the shore.
            float out_ = -WorldShape.ShoreDistance(transform.position.x, transform.position.z);
            InCurrent = out_ > Range && !WorldShape.OnIsland(transform.position.x, transform.position.z, 10f);
            if (InCurrent)
            {
                float push = Mathf.Clamp(out_ - Range, 0f, 6f) * 1.2f + 1.2f;
                _velocity += Vector3.back * push * Time.deltaTime; // the shore is to the south (-z)
                if (Time.time - _lastCurrentToast > 12f)
                {
                    _lastCurrentToast = Time.time;
                    GameEvents.Toast($"The currents are too strong past {Range:0} m. Old Wang sells boat upgrades to go further.", 4f);
                }
                if (Random.value < Time.deltaTime * 2f) WaterRipples.Spawn(transform.position + Random.insideUnitSphere * 2f, 0.8f);
            }

            Vector3 next = transform.position + _velocity * Time.deltaTime;
            if (-WorldShape.ShoreDistance(next.x, next.z) > Range + 4f && !WorldShape.OnIsland(next.x, next.z, 10f) && next.z > transform.position.z)
                next = new Vector3(next.x, next.y, transform.position.z); // can't get any further out
            if (!Rowable(next))
            {
                _velocity = Vector3.zero; // bumped the shore / dock
            }
            else
            {
                transform.position = new Vector3(next.x, transform.position.y, next.z);
            }

            if (_velocity.sqrMagnitude > 0.05f)
            {
                // The bow follows the direction of travel.
                Quaternion target = Quaternion.LookRotation(_velocity.normalized, Vector3.up) * Quaternion.Euler(0f, _modelYawOffset, 0f);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
                if (Time.frameCount % 50 == 0) AudioManager.Instance?.PlayAt("SFX/water_small", transform.position, 0.18f, 0.2f);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
