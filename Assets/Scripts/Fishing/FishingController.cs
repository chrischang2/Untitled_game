using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UntitledGame.CameraControl;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Player;
using Random = UnityEngine.Random;

namespace UntitledGame.Fishing
{
    public enum FishingState { Idle, Charging, Casting, Waiting, Bite, Reeling, Landing, Retrieving }

    /// <summary>
    /// The fishing loop (after Stardew Valley): hold to charge a cast, wait for nibbles, click when the bobber dives,
    /// then the reeling minigame: hold the mouse to lift the green bar and keep the fish inside it until the catch
    /// meter fills. Only fish whose requirements are all met can bite (FishDatabase.Missing): discovered, far enough
    /// out, a bait they like, a strong enough line, the right time. Stats (PlayerStats) widen the bar, slow escapes,
    /// cast further, speed up bites, make fish heavier and sometimes give a bonus fish.
    /// </summary>
    public class FishingController : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private FishingRod rod;
        [SerializeField] private CameraRig cameraRig;
        [SerializeField] private float minCast = 3.5f;
        [SerializeField] private float biteWindow = 1.15f;

        public FishingState State { get; private set; } = FishingState.Idle;
        public float Power { get; private set; }
        // Reeling minigame (all 0..1 along the vertical track, 0 = bottom).
        public float FishPos { get; private set; }
        public float BarPos { get; private set; }
        public float BarSize { get; private set; }
        /// <summary>Every fish's green bar is this much of its listed size before upgrades (the base bars were too easy).</summary>
        public const float BarScale = 0.7f;
        public float Progress { get; private set; }
        public bool FishInBar { get; private set; }
        public bool Perfect { get; private set; }
        public bool FishPulling => !FishInBar;
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
        private float _length; // weight in kg
        private float _fishVel, _fishTarget, _barVel, _retarget, _grace;
        private bool _nothingToast;
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
            bool canAct = !InputGate.GameplayBlocked && !Home.PlacementController.Active && Companion.ShopConversation.Active == null;
            bool down = (canAct && Input.GetMouseButtonDown(0) && !PointerOverUI) || _simulateDown;
            bool held = (canAct && Input.GetMouseButton(0)) || SimulateHold;
            bool up = canAct && Input.GetMouseButtonUp(0);
            _simulateDown = false;
            bool cancel = canAct && (Input.GetKeyDown(KeyCode.E) || MovementPressed());

            rod.Stowed = State == FishingState.Idle;
            switch (State)
            {
                case FishingState.Idle: UpdateIdle(down); break;
                case FishingState.Charging: UpdateCharging(held, up); break;
                case FishingState.Casting: UpdateCasting(); break;
                case FishingState.Waiting: UpdateWaiting(down, cancel); break;
                case FishingState.Bite: UpdateBite(down, cancel); break;
                case FishingState.Reeling:
                    if (canAct && Input.GetKeyDown(KeyCode.E)) CutLine();
                    else UpdateReeling(held);
                    break;
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
                    _steeringCamera = true;
                }
                else if (_steeringCamera)
                {
                    // Only undo our own nudge (shop conversations also steer the camera).
                    cameraRig.FocusOffset = Vector3.zero;
                    _steeringCamera = false;
                }
            }
        }

        private bool _steeringCamera;

        private static bool MovementPressed() =>
            Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D);

        // ---------------------------------------------------------------- Idle / cast

        private void UpdateIdle(bool down)
        {
            player.ActionAnimation = null;
            rod.TargetPitch = player.IsMoving ? 55f : 40f;
            rod.Tension = 0f;
            rod.Shake = 0f;
            if (down && !player.IsMoving && Progression.Energy.Current < Progression.Energy.CastCost + 1f)
            {
                GameEvents.Toast("You're too tired to fish. Go to bed (or you'll pass out).", 3f);
                return;
            }
            if (down && !player.IsMoving && Progression.Energy.Current < Progression.Energy.CastCost)
            {
                GameEvents.Toast($"Too tired to cast (a cast takes {Progression.Energy.CastCost:0} energy). Go to bed: you can sleep any time.", 3.5f);
                return;
            }
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
                float dist = Mathf.Lerp(minCast, Progression.PlayerStats.CastDistance, Power);
                Vector3 fwd = player.transform.forward;
                fwd.y = 0f;
                fwd.Normalize();
                Vector3 target = player.transform.position + fwd * dist;
                target.y = Mathf.Max(WorldShape.TerrainHeight(target.x, target.z), WorldShape.WaterLevel);
                if (WorldShape.IsOnDock(target.x, target.z)) target.y = WorldShape.DockDeckHeight;
                Progression.Energy.Spend(Progression.Energy.CastCost);
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
                _nothingToast = false;
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
                raining = Weather.Instance != null && Weather.Instance.IsRaining,
                bait = Inventory.Bait,
                lineKg = Inventory.LineKg,
                shoreDistance = Mathf.Max(0f, -WorldShape.ShoreDistance(b.x, b.z)),
            };
        }

        private void ScheduleBite(bool spooked)
        {
            var ctx = Context();
            float t = Random.Range(3.5f, 12f);
            if (ctx.raining) t *= 0.75f;
            t *= Progression.PlayerStats.BiteTimeMultiplier;
            if (spooked) t += Random.Range(2.5f, 5f);
            var bait = Inventory.Bait;
            if (bait != null) t /= Mathf.Max(0.5f, bait.biteSpeed);
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
                if (_species == null)
                {
                    // Nothing that can live here wants this bait / line / time: say so, and keep waiting.
                    if (!_nothingToast)
                    {
                        _nothingToast = true;
                        GameEvents.Toast("Nothing seems to be biting here. Your journal shows what each fish needs (bait, distance, line, time).", 4.5f);
                    }
                    ScheduleBite(false);
                    return;
                }
                _length = FishDatabase.RollWeight(_species, Progression.PlayerStats.QualityLevel);
                if (ctx.bait != null && _species.IsFish) Inventory.ConsumeBait(); // the fish took the bait
                BiteTimeLeft = biteWindow * Mathf.Lerp(1.1f, 0.8f, _species.difficulty / 100f) * (Inventory.HasAccessory("bobber_fancy") ? 1.35f : 1f);
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
            BarSize = Mathf.Clamp(_species.barSize * BarScale * Progression.PlayerStats.BarMultiplier, 0.07f, 0.7f);
            FishPos = Random.Range(0.25f, 0.6f);
            // Like Stardew: the bar starts around the fish, so the fight begins fairly.
            BarPos = Mathf.Clamp(FishPos - BarSize * 0.5f, 0f, 1f - BarSize);
            _barVel = 0f;
            _fishVel = 0f;
            _fishTarget = FishPos;
            _retarget = 0.6f;
            _grace = 0.8f;
            Progress = Progression.PlayerStats.StartProgress;
            Perfect = true;
            AudioManager.Instance?.PlaySfx("SFX/reel", 0.5f);
            SetState(FishingState.Reeling);
        }

        /// <summary>
        /// The reeling minigame. Holding lifts the green bar, letting go lets it sink. The fish darts about the track in
        /// its own way (FishMotion), faster the harder it is. Inside the bar the catch meter fills; outside it drains
        /// (slower with the escape-allowance stat). Full = caught, empty = it got away.
        /// </summary>
        private void UpdateReeling(bool reel)
        {
            player.ActionAnimation = "holding-right";
            float diff = Mathf.Clamp01(_species.difficulty / 100f);
            // Fighting a fish is tiring: the heavier it is, the faster your energy goes.
            Progression.Energy.Spend(Progression.Energy.ReelDrainPerSecond(_length) * Time.deltaTime);
            if (State != FishingState.Reeling) return; // passed out

            // The bar: holding accelerates it up, gravity pulls it down, a little bounce off the bottom.
            _barVel += (reel ? 2.6f : -2.2f) * Time.deltaTime;
            _barVel = Mathf.Clamp(_barVel, -1.5f, 1.5f);
            BarPos += _barVel * Time.deltaTime;
            if (BarPos < 0f) { BarPos = 0f; _barVel = -_barVel * 0.3f; }
            if (BarPos > 1f - BarSize) { BarPos = 1f - BarSize; _barVel = 0f; }

            // The fish: picks a new spot every so often, depending on how it swims.
            _retarget -= Time.deltaTime;
            if (_retarget <= 0f)
            {
                float jump = Mathf.Lerp(0.15f, 0.75f, diff);
                switch (_species.motion)
                {
                    case FishMotion.Smooth: _fishTarget = Mathf.Clamp01(FishPos + Random.Range(-jump, jump) * 0.6f); _retarget = Random.Range(1.2f, 2.4f) - diff * 0.6f; break;
                    case FishMotion.Sinker: _fishTarget = Mathf.Clamp01(Random.Range(0f, 0.7f) * Random.value + Random.Range(-0.05f, 0.1f)); _retarget = Random.Range(0.8f, 1.8f) - diff * 0.5f; break;
                    case FishMotion.Floater: _fishTarget = Mathf.Clamp01(1f - Random.Range(0f, 0.7f) * Random.value); _retarget = Random.Range(0.8f, 1.8f) - diff * 0.5f; break;
                    case FishMotion.Dart: _fishTarget = Mathf.Clamp01(FishPos + (Random.value < 0.5f ? -1f : 1f) * Random.Range(jump * 0.6f, jump * 1.2f)); _retarget = Random.Range(0.35f, 1.1f) - diff * 0.25f; break;
                    default: _fishTarget = Random.Range(0.02f, 0.98f); _retarget = Random.Range(0.7f, 1.7f) - diff * 0.5f; break;
                }
                _retarget = Mathf.Max(0.18f, _retarget);
            }
            float spring = Mathf.Lerp(6f, 28f, diff) * (_species.motion == FishMotion.Dart ? 1.6f : 1f);
            _fishVel += (_fishTarget - FishPos) * spring * Time.deltaTime;
            _fishVel *= Mathf.Exp(-Mathf.Lerp(5f, 3f, diff) * Time.deltaTime);
            FishPos = Mathf.Clamp01(FishPos + _fishVel * Time.deltaTime);

            // Catch meter.
            FishInBar = FishPos >= BarPos && FishPos <= BarPos + BarSize;
            _grace -= Time.deltaTime;
            if (FishInBar) Progress += Time.deltaTime * Mathf.Lerp(0.34f, 0.24f, diff);
            else if (_grace <= 0f)
            {
                Progress -= Time.deltaTime * Mathf.Lerp(0.16f, 0.3f, diff) * Progression.PlayerStats.DrainMultiplier;
                Perfect = false;
            }
            Progress = Mathf.Clamp01(Progress);

            // The rod and bobber show the fight.
            var b = rod.BobberPosition;
            rod.SetBobberRest(new Vector3(b.x, WorldShape.WaterLevel, b.z), FishInBar ? 0.05f : 0.14f);
            player.FaceTowards(b);
            rod.Tension = 1f - Progress;
            rod.Shake = Mathf.Clamp01(Mathf.Abs(_fishVel) * 0.8f + (FishInBar ? 0.1f : 0.4f));
            rod.TargetPitch = Mathf.Lerp(40f, 60f, Progress);
            if (reel && Time.frameCount % 9 == 0) AudioManager.Instance?.PlaySfx("SFX/ui_click_002", 0.12f, 0.2f);
            if (!FishInBar && Random.value < Time.deltaTime * 1.5f)
            {
                Effects.Splash(new Vector3(b.x, 0f, b.z), 0.4f + diff * 0.4f);
                WaterRipples.Spawn(b, 0.7f);
            }

            if (Progress >= 1f)
            {
                Land();
                return;
            }
            if (Progress <= 0f)
            {
                AudioManager.Instance?.PlaySfx("SFX/escape", 0.6f);
                GameEvents.Toast(diff > 0.7f ? "It got away! Big fish fight hard: Coach Wu's training helps." : "It got away... next time!", 3.5f);
                FishEscaped?.Invoke("escaped");
                BeginRetrieve();
            }
        }


        /// <summary>E while reeling: give up on this fish (to save energy) and reel in the empty line.</summary>
        public void CutLine()
        {
            if (State != FishingState.Reeling && State != FishingState.Bite) return;
            AudioManager.Instance?.PlaySfx("SFX/escape", 0.5f);
            GameEvents.Toast("You cut the line and let it go.", 2.5f);
            FishEscaped?.Invoke("cut");
            BeginRetrieve();
        }

        /// <summary>Drop everything at once (passing out).</summary>
        public void ForceStop()
        {
            if (_heldCatch != null) Destroy(_heldCatch);
            _heldCatch = null;
            rod.HideBobber();
            rod.Tension = 0f;
            rod.Shake = 0f;
            player.ActionAnimation = null;
            SimulateHold = false;
            SetState(FishingState.Idle);
        }

        // ---------------------------------------------------------------- Landing

        private void Land()
        {
            rod.HideBobber();
            rod.Tension = 0f;
            rod.Shake = 0f;
            rod.TargetPitch = 95f;
            LastCatch = CatchJournal.Record(_species, _length, player.transform.position);
            LastCatch.perfect = Perfect && State == FishingState.Reeling;
            if (LastCatch.perfect) GameEvents.Toast("Perfect catch!", 2f);
            // Luck training: sometimes a second fish of the same kind comes up on the hook too.
            if (_species.IsFish && Random.value < Progression.PlayerStats.BonusFishChance)
            {
                float extra = FishDatabase.RollWeight(_species, Progression.PlayerStats.QualityLevel);
                var bonus = CatchJournal.Record(_species, extra, player.transform.position);
                bonus.bonus = true;
                GameEvents.Toast($"Bonus! A second {_species.name.ToLower()} ({FishDatabase.WeightText(extra)}) came up too.", 3.5f);
            }
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
            if (!LastCatch.inBucket)
                GameEvents.Toast($"Your bag is full ({Inventory.SlotsUsed}/{Inventory.SlotCapacity} slots), so it swims free. Sell fish at the market, or buy a bigger bucket!", 4f);
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
