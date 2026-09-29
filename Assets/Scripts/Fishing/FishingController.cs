using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UntitledGame.CameraControl;
using UntitledGame.Core;
using UntitledGame.Environment;
using UntitledGame.Player;
using Random = UnityEngine.Random;

namespace UntitledGame.Fishing
{
    public enum FishingState { Idle, Charging, Casting, Waiting, Bite, Reeling, Landing, Retrieving }

    /// <summary>
    /// The fishing loop: hold to charge a cast, wait for nibbles, click when the bobber dives,
    /// then reel with a gentle tension minigame (ease off when the fish pulls!).
    /// </summary>
    public class FishingController : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private FishingRod rod;
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private float minCast = 3.5f;
        [SerializeField] private float maxCast = 17f;
        [SerializeField] private float biteWindow = 1.15f;

        public FishingState State { get; private set; } = FishingState.Idle;
        public float Power { get; private set; }
        public float Tension { get; private set; }
        public float FishDistance { get; private set; }
        public float MaxFishDistance => 22f;
        public bool FishPulling { get; private set; }
        public float BiteTimeLeft { get; private set; }
        public CatchResult LastCatch { get; private set; }
        public FishSpecies HookedSpecies => _species;

        /// <summary>Human-readable activity for the companion's context.</summary>
        public string ActivityDescription => State switch
        {
            FishingState.Idle => "",
            FishingState.Charging => "winding up a cast",
            FishingState.Casting => "casting their line",
            FishingState.Waiting => "watching their bobber, waiting for a bite",
            FishingState.Bite => "a fish is biting right now!",
            FishingState.Reeling => "reeling in a fish",
            FishingState.Landing => "admiring the fish they just caught",
            _ => "",
        };

        public static event Action<CatchResult> FishCaught;
        public static event Action<string> FishEscaped;
        public static event Action<FishingState> StateChanged;

        private Vector3 _castStart, _castTarget;
        private float _castT, _castDuration;
        private float _biteTimer;
        private float _nextNibble;
        private FishSpecies _species;
        private float _length;
        private float _phaseTimer;
        private float _overTension;
        private GameObject _heldCatch;
        private float _landingTimer;
        private float _retrieveT;
        private Vector3 _retrieveFrom;
        private bool _landedOnWater;
        private float _chargeTime;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (rod == null) rod = GetComponent<FishingRod>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<CameraRig>();
        }

        public void Configure(PlayerController p, FishingRod r, CameraRig cam)
        {
            player = p;
            rod = r;
            cameraRig = cam;
        }

        private void SetState(FishingState s)
        {
            if (State == s) return;
            State = s;
            bool locked = s != FishingState.Idle;
            if (locked) InputGate.BlockMovement(this); else InputGate.UnblockMovement(this);
            StateChanged?.Invoke(s);
        }

        // Test hooks (automated self-test drives the real input path through these).
        public bool SimulateHold { get; set; }
        private bool _simulateDown;
        public void SimulateClick() => _simulateDown = true;
        public void DebugBiteNow() => _biteTimer = Mathf.Min(_biteTimer, 0.05f);

        private bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private void Update()
        {
            bool canAct = !InputGate.GameplayBlocked;
            bool down = (canAct && Input.GetMouseButtonDown(0) && !PointerOverUI) || _simulateDown;
            bool held = (canAct && Input.GetMouseButton(0)) || SimulateHold;
            bool up = canAct && Input.GetMouseButtonUp(0);
            _simulateDown = false;
            bool cancel = canAct && (Input.GetKeyDown(KeyCode.E) || MovementPressed());

            switch (State)
            {
                case FishingState.Idle: UpdateIdle(down); break;
                case FishingState.Charging: UpdateCharging(held, up); break;
                case FishingState.Casting: UpdateCasting(); break;
                case FishingState.Waiting: UpdateWaiting(down, cancel); break;
                case FishingState.Bite: UpdateBite(down, cancel); break;
                case FishingState.Reeling: UpdateReeling(held); break;
                case FishingState.Landing: UpdateLanding(down || (canAct && Input.anyKeyDown && !Input.GetKeyDown(KeyCode.V))); break;
                case FishingState.Retrieving: UpdateRetrieving(); break;
            }

            if (cameraRig != null)
            {
                bool lineOut = State == FishingState.Waiting || State == FishingState.Bite || State == FishingState.Reeling;
                if (lineOut && rod.Bobber != null)
                {
                    Vector3 toBobber = rod.BobberPosition - player.transform.position;
                    toBobber.y = 0f;
                    cameraRig.FocusOffset = toBobber * 0.35f;
                }
                else cameraRig.FocusOffset = Vector3.zero;
            }
        }

        private static bool MovementPressed() =>
            Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D);

        // ---------------------------------------------------------------- Idle / cast

        private void UpdateIdle(bool down)
        {
            player.ActionAnimation = null;
            rod.TargetPitch = player.IsMoving ? 55f : 40f;
            rod.Tension = 0f;
            rod.Shake = 0f;
            if (down && !player.IsMoving)
            {
                _chargeTime = 0f;
                Power = 0f;
                SetState(FishingState.Charging);
                AudioManager.Instance?.PlaySfx("SFX/rpg_cloth1", 0.4f);
            }
        }

        private void UpdateCharging(bool held, bool up)
        {
            player.ActionAnimation = "holding-right";
            if (cameraRig != null)
            {
                Vector3 aim = Quaternion.Euler(0f, cameraRig.Yaw, 0f) * Vector3.forward;
                player.FaceTowards(player.transform.position + aim);
            }
            _chargeTime += Time.deltaTime;
            Power = Mathf.PingPong(_chargeTime * 0.85f, 1f);
            rod.TargetPitch = Mathf.Lerp(80f, 125f, Power);

            if (up || !held)
            {
                float dist = Mathf.Lerp(minCast, maxCast, Power);
                Vector3 fwd = player.transform.forward;
                fwd.y = 0f;
                fwd.Normalize();
                Vector3 target = player.transform.position + fwd * dist;
                target.y = Mathf.Max(WorldShape.TerrainHeight(target.x, target.z), WorldShape.WaterLevel);
                if (WorldShape.IsOnDock(target.x, target.z)) target.y = WorldShape.DockDeckHeight;
                _castStart = rod.TipPosition;
                _castTarget = target;
                _castT = 0f;
                _castDuration = 0.45f + dist * 0.035f;
                rod.ShowBobber(_castStart);
                rod.TargetPitch = 20f;
                AudioManager.Instance?.PlaySfx("SFX/reel", 0.25f, 0.1f);
                AudioManager.Instance?.PlaySfx("SFX/rpg_cloth2", 0.5f);
                SetState(FishingState.Casting);
            }
        }

        private void UpdateCasting()
        {
            _castT += Time.deltaTime / _castDuration;
            float t = Mathf.Clamp01(_castT);
            float arc = Mathf.Sin(t * Mathf.PI) * (1.5f + Vector3.Distance(_castStart, _castTarget) * 0.18f);
            Vector3 p = Vector3.Lerp(_castStart, _castTarget, t) + Vector3.up * arc;
            rod.MoveBobber(p);
            rod.TargetPitch = Mathf.Lerp(20f, 35f, t);
            if (t < 1f) return;

            _landedOnWater = WorldShape.IsWater(_castTarget.x, _castTarget.z)
                             && !WorldShape.IsOnDock(_castTarget.x, _castTarget.z, 0.2f)
                             && WorldShape.WaterDepth(_castTarget.x, _castTarget.z) > 0.25f;
            if (_landedOnWater)
            {
                var rest = new Vector3(_castTarget.x, WorldShape.WaterLevel, _castTarget.z);
                rod.SetBobberRest(rest, 0f);
                Effects.Splash(rest, 0.35f);
                WaterRipples.Spawn(rest, 0.7f);
                AudioManager.Instance?.PlayAt("SFX/plop", rest, 0.8f);
                ScheduleBite(false);
                SetState(FishingState.Waiting);
            }
            else
            {
                AudioManager.Instance?.PlayAt("SFX/footstep_grass_001", _castTarget, 0.6f);
                GameEvents.Toast("Oops, that's dry land! Aim for the water.");
                BeginRetrieve();
            }
        }

        // ---------------------------------------------------------------- Waiting / bite

        private CatchContext Context()
        {
            var b = rod.BobberPosition;
            return new CatchContext
            {
                hour = DayNightCycle.Instance != null ? DayNightCycle.Instance.TimeOfDay : 12f,
                depth = WorldShape.WaterDepth(b.x, b.z),
                raining = Weather.Instance != null && Weather.Instance.IsRaining,
                nearLilies = Physics.CheckSphere(b, 2.5f, 1 << 4, QueryTriggerInteraction.Collide), // layer 4 = Water props
            };
        }

        private void ScheduleBite(bool spooked)
        {
            var ctx = Context();
            float t = Random.Range(3.5f, 12f);
            if (ctx.raining) t *= 0.75f;
            if (ctx.depth < 0.6f) t *= 1.3f;
            if (spooked) t += Random.Range(2.5f, 5f);
            _biteTimer = t;
            _nextNibble = t - Random.Range(0.8f, Mathf.Min(4f, t - 0.5f));
        }

        private void UpdateWaiting(bool down, bool cancel)
        {
            player.ActionAnimation = "holding-right";
            rod.TargetPitch = 32f;
            rod.Tension = 0.05f;
            player.FaceTowards(rod.BobberPosition);

            if (cancel)
            {
                BeginRetrieve();
                return;
            }

            _biteTimer -= Time.deltaTime;
            if (_biteTimer <= _nextNibble && _nextNibble > 0f)
            {
                // A little nibble: tiny dip + plop.
                var b = rod.BobberPosition;
                rod.SetBobberRest(new Vector3(b.x, WorldShape.WaterLevel, b.z), 0.05f);
                Invoke(nameof(ResetDip), 0.18f);
                WaterRipples.Spawn(new Vector3(b.x, 0f, b.z), 0.3f);
                AudioManager.Instance?.PlayAt("SFX/water_small", b, 0.35f, 0.2f);
                _nextNibble = _biteTimer > 2f && Random.value < 0.6f ? _biteTimer - Random.Range(0.7f, 2f) : -1f;
            }

            if (down)
            {
                GameEvents.Toast("Too early... patience!");
                ScheduleBite(true);
                return;
            }

            if (_biteTimer <= 0f)
            {
                var ctx = Context();
                _species = FishDatabase.Roll(ctx);
                _length = FishDatabase.RollLength(_species);
                BiteTimeLeft = biteWindow * Mathf.Lerp(1.1f, 0.8f, _species.difficulty);
                var b = rod.BobberPosition;
                var rest = new Vector3(b.x, WorldShape.WaterLevel, b.z);
                rod.SetBobberRest(rest, 0.16f);
                Effects.Splash(rest, 0.6f);
                WaterRipples.Spawn(rest, 1f);
                AudioManager.Instance?.PlayClipAt(AudioManager.Instance.RandomClip("SFX", "splash_0"), rest, 0.7f);
                AudioManager.Instance?.PlaySfx("SFX/bloop", 0.5f);
                SetState(FishingState.Bite);
            }
        }

        private void ResetDip()
        {
            if (State != FishingState.Waiting) return;
            var b = rod.BobberPosition;
            rod.SetBobberRest(new Vector3(b.x, WorldShape.WaterLevel, b.z), 0f);
        }

        private void UpdateBite(bool down, bool cancel)
        {
            rod.Shake = 0.6f;
            if (cancel)
            {
                BeginRetrieve();
                return;
            }
            BiteTimeLeft -= Time.deltaTime;
            if (down)
            {
                StartReeling();
                return;
            }
            if (BiteTimeLeft <= 0f)
            {
                rod.Shake = 0f;
                var b = rod.BobberPosition;
                rod.SetBobberRest(new Vector3(b.x, WorldShape.WaterLevel, b.z), 0f);
                GameEvents.Toast("It nibbled the bait and left... wait for another!");
                FishEscaped?.Invoke("missed");
                ScheduleBite(false);
                SetState(FishingState.Waiting);
            }
        }

        // ---------------------------------------------------------------- Reeling

        private void StartReeling()
        {
            Vector3 d = rod.BobberPosition - player.transform.position;
            d.y = 0f;
            FishDistance = Mathf.Max(4f, d.magnitude);
            Tension = 0.2f;
            FishPulling = true;
            _phaseTimer = Random.Range(0.6f, 1.1f);
            _overTension = 0f;
            AudioManager.Instance?.PlaySfx("SFX/reel", 0.5f);
            SetState(FishingState.Reeling);
        }

        private void UpdateReeling(bool reel)
        {
            player.ActionAnimation = "holding-right";
            float d = _species != null ? _species.difficulty : 0.3f;
            if (!_species.IsFish) d = 0.05f;

            _phaseTimer -= Time.deltaTime;
            if (_phaseTimer <= 0f)
            {
                FishPulling = !FishPulling && _species.IsFish;
                _phaseTimer = FishPulling ? Random.Range(0.6f, 1.1f + d) : Random.Range(1.1f, 2.7f - d);
                if (FishPulling)
                {
                    var b = rod.BobberPosition;
                    Effects.Splash(new Vector3(b.x, 0f, b.z), 0.5f + d * 0.5f);
                    WaterRipples.Spawn(b, 0.9f);
                    AudioManager.Instance?.PlayClipAt(AudioManager.Instance.RandomClip("SFX", "splash_0"), b, 0.5f);
                }
            }

            if (reel)
            {
                Tension += Time.deltaTime * (FishPulling ? 0.5f + d * 0.55f : 0.16f);
                FishDistance -= Time.deltaTime * (FishPulling ? 0.35f : 1.9f + (1f - d) * 0.9f);
            }
            else
            {
                Tension -= Time.deltaTime * 0.5f;
                if (FishPulling) FishDistance += Time.deltaTime * (0.9f + d * 1.3f);
            }
            Tension = Mathf.Clamp01(Tension);

            // Reel clicking while winding.
            if (reel && Time.frameCount % 9 == 0) AudioManager.Instance?.PlaySfx("SFX/ui_click_002", 0.12f, 0.2f);

            if (Tension >= 0.999f)
            {
                _overTension += Time.deltaTime;
                if (_overTension > 0.35f)
                {
                    AudioManager.Instance?.PlaySfx("SFX/escape", 0.6f);
                    GameEvents.Toast("Snap! The line broke. Ease off when it pulls!");
                    FishEscaped?.Invoke("snapped");
                    BeginRetrieve();
                    return;
                }
            }
            else _overTension = 0f;

            if (FishDistance > MaxFishDistance)
            {
                AudioManager.Instance?.PlaySfx("SFX/escape", 0.6f);
                GameEvents.Toast("It swam away... next time!");
                FishEscaped?.Invoke("escaped");
                BeginRetrieve();
                return;
            }

            // Move the bobber along the line towards the player.
            Vector3 toBobber = rod.BobberPosition - player.transform.position;
            toBobber.y = 0f;
            Vector3 dir = toBobber.sqrMagnitude > 0.01f ? toBobber.normalized : player.transform.forward;
            Vector3 wobble = new Vector3(Mathf.Sin(Time.time * 3f), 0f, Mathf.Cos(Time.time * 2.3f)) * (FishPulling ? 0.35f : 0.1f);
            Vector3 pos = player.transform.position + dir * FishDistance + wobble;
            if (!WorldShape.IsWater(pos.x, pos.z))
            {
                // Pulled up onto the shallows/dock edge: it's basically landed.
                FishDistance = 0f;
            }
            rod.SetBobberRest(new Vector3(pos.x, WorldShape.WaterLevel, pos.z), FishPulling ? 0.12f : 0.05f);
            player.FaceTowards(pos);
            rod.Tension = Tension;
            rod.Shake = FishPulling ? 0.8f : 0.2f;
            rod.TargetPitch = Mathf.Lerp(55f, 40f, Tension);

            if (FishDistance <= 1.4f) Land();
        }

        // ---------------------------------------------------------------- Landing

        private void Land()
        {
            rod.HideBobber();
            rod.Tension = 0f;
            rod.Shake = 0f;
            rod.TargetPitch = 95f;
            Tension = 0f;
            LastCatch = CatchJournal.Record(_species, _length, player.transform.position);
            _heldCatch = CatchVisuals.Spawn(_species, _length);
            _landingTimer = 0f;
            player.ActionAnimation = "holding-both";
            player.Animator?.Play("holding-both", 0.1f);
            if (Camera.main != null) player.FaceTowards(Camera.main.transform.position, instant: true);
            bool special = _species.rarity >= Rarity.Rare || LastCatch.isNewSpecies;
            AudioManager.Instance?.PlaySfx(special ? "SFX/catch_special" : "SFX/catch_normal", 0.55f, 0f);
            AudioManager.Instance?.PlayAt("SFX/splash_big1", player.transform.position + player.transform.forward, 0.5f);
            SetState(FishingState.Landing);
            FishCaught?.Invoke(LastCatch);
        }

        private void UpdateLanding(bool dismiss)
        {
            _landingTimer += Time.deltaTime;
            if (_heldCatch != null)
            {
                Transform t = player.transform;
                Vector3 p = t.position + t.forward * 0.5f + Vector3.up * (0.78f + Mathf.Sin(_landingTimer * 5f) * 0.02f);
                // Model length runs along local X: hold it sideways across the chest so it reads clearly.
                float wiggle = Mathf.Sin(_landingTimer * 9f) * 10f * Mathf.Clamp01(2f - _landingTimer);
                _heldCatch.transform.SetPositionAndRotation(p, Quaternion.LookRotation(t.forward, Vector3.up) * Quaternion.Euler(0f, wiggle, 8f));
                if (_landingTimer < 0.1f) Effects.Sparkle(p, _species.rarity >= Rarity.Rare ? 30 : 14);
            }
            if ((_landingTimer > 0.8f && dismiss) || _landingTimer > 6f)
            {
                if (_heldCatch != null) Destroy(_heldCatch);
                _heldCatch = null;
                AudioManager.Instance?.PlaySfx("SFX/rpg_dropLeather", 0.4f);
                player.ActionAnimation = null;
                SetState(FishingState.Idle);
            }
        }

        // ---------------------------------------------------------------- Retrieve

        private void BeginRetrieve()
        {
            _retrieveFrom = rod.BobberPosition;
            _retrieveT = 0f;
            rod.Shake = 0f;
            rod.Tension = 0.3f;
            FishPulling = false;
            Tension = 0f;
            AudioManager.Instance?.PlaySfx("SFX/reel", 0.3f, 0.15f);
            SetState(FishingState.Retrieving);
        }

        private void UpdateRetrieving()
        {
            _retrieveT += Time.deltaTime / 0.4f;
            float t = Mathf.Clamp01(_retrieveT);
            Vector3 p = Vector3.Lerp(_retrieveFrom, rod.TipPosition, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.6f;
            rod.MoveBobber(p);
            rod.TargetPitch = 60f;
            if (t >= 1f)
            {
                rod.HideBobber();
                rod.Tension = 0f;
                player.ActionAnimation = null;
                SetState(FishingState.Idle);
            }
        }

        private void OnDisable() => InputGate.UnblockMovement(this);

        /// <summary>Test hook: instantly land a specific catch (used by the automated self-test).</summary>
        public void DebugCatch(string speciesId, float length)
        {
            _species = FishDatabase.Get(speciesId) ?? FishDatabase.All[0];
            _length = length;
            Land();
        }
    }
}
