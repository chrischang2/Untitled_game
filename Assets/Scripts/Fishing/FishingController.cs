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
        public float FishPos => _reel?.FishPos ?? 0.5f;
        public float BarPos => _reel?.BarPos ?? 0f;
        public float BarSize => _reel?.P.barSize ?? 0.2f;
        public float Progress => _reel?.Progress ?? 0f;
        public bool FishInBar => _reel != null && _reel.FishInBar;
        public bool Perfect => _reel != null && !_reel.LeftBarAfterGrace;
        /// <summary>The fish's fight against your training (see FishPower).</summary>
        public ReelParams Reel => _reel?.P;
        /// <summary>How far the last cast went (m), and how far the bobber is from you now.</summary>
        public float LastCastDistance { get; private set; }
        public float ChargeDistance => Mathf.Lerp(minCast, Progression.PlayerStats.CastDistance, Power);
        /// <summary>A cast released at 97% power or more: the fight starts with the catch meter 15% fuller.</summary>
        public const float PerfectCastPower = 0.97f, PerfectCastBonus = 0.15f;
        public bool PerfectCast { get; private set; }
        public bool ChargeIsPerfect => Power >= PerfectCastPower;

        /// <summary>
        /// Casting far out catches bigger fish: the weight comes from a window half the species' range wide. Within
        /// 2 m of the shore it's the bottom half; a 95% cast from the end of the dock (or anything further) gets the top
        /// half; in between the window slides evenly. Returns where the window starts (0 to 0.5).
        /// </summary>
        public static float WeightWindow(float shoreDistance) =>
            0.5f * Mathf.InverseLerp(WindowNear, WindowFar, shoreDistance);
        public const float WindowNear = 2f;
        public static float WindowFar => WorldShape.DockLength + Mathf.Lerp(3.5f, Progression.PlayerStats.CastDistance, 0.95f);
        public Vector3 BobberWorld => rod.BobberPosition;
        /// <summary>How far out from the shoreline the bobber is (fish need a minimum distance).</summary>
        public float ShoreDistance => Mathf.Max(0f, -WorldShape.ShoreDistance(rod.BobberPosition.x, rod.BobberPosition.z));
        public float BobberDistance
        {
            get
            {
                var d = rod.BobberPosition - player.transform.position;
                d.y = 0f;
                return d.magnitude;
            }
        }
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
        private ReelSim _reel;
        private float _reelStart, _window;
        private bool _inFrenzy;

        /// <summary>Fish landed in a row without one getting away (or the line being cut).</summary>
        public static int Streak { get; private set; }
        /// <summary>A streak of 5 or more makes golden fish a little more likely.</summary>
        public const int HotStreak = 5;
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
                AimOffset = 0f;
                SetState(FishingState.Charging);
                AudioManager.Instance?.PlaySfx("SFX/rpg_cloth1", 0.4f);
            }
        }

        // ---------------------------------------------------------------- Aiming
        /// <summary>A/D (or the arrow keys) swing the cast left and right of where the camera looks, while charging.</summary>
        public float AimOffset { get; set; }
        public const float AimSpeed = 70f, AimLimit = 75f;
        /// <summary>Which way the cast will go (flat, normalised).</summary>
        public Vector3 AimDirection
        {
            get
            {
                float yaw = (cameraRig != null ? cameraRig.Yaw : player.transform.eulerAngles.y) + AimOffset;
                return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            }
        }
        /// <summary>Where a cast released now would land.</summary>
        public Vector3 PredictedLanding
        {
            get
            {
                Vector3 target = player.transform.position + AimDirection * ChargeDistance;
                target.y = Mathf.Max(WorldShape.TerrainHeight(target.x, target.z), WorldShape.WaterLevel);
                if (WorldShape.IsOnDock(target.x, target.z)) target.y = WorldShape.DockDeckHeight;
                return target;
            }
        }
        public bool PredictedOnWater
        {
            get
            {
                var t = PredictedLanding;
                return WorldShape.IsWater(t.x, t.z) && !WorldShape.IsOnDock(t.x, t.z, 0.2f) && WorldShape.WaterDepth(t.x, t.z) > 0.25f;
            }
        }
        public float PredictedShoreDistance => Mathf.Max(0f, -WorldShape.ShoreDistance(PredictedLanding.x, PredictedLanding.z));

        private GameObject _reticle;
        private MaterialPropertyBlock _reticleMpb;

        /// <summary>A ring on the water where the cast will land: white on water, gold in a frenzy, red over land.</summary>
        private void UpdateReticle(bool show)
        {
            if (_reticle == null)
            {
                if (!show || GameAssets.Instance == null) return;
                _reticle = new GameObject("CastTarget");
                _reticleMpb = new MaterialPropertyBlock();
                for (int k = 0; k < 2; k++)
                {
                    var part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(part.GetComponent<Collider>());
                    part.name = k == 0 ? "Ring" : "Dot";
                    part.transform.SetParent(_reticle.transform, false);
                    part.transform.localScale = k == 0 ? new Vector3(0.9f, 0.01f, 0.9f) : new Vector3(0.22f, 0.02f, 0.22f);
                    var r = part.GetComponent<Renderer>();
                    r.sharedMaterial = GameAssets.Instance.bobberWhite;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            _reticle.SetActive(show);
            if (!show) return;
            Vector3 p = PredictedLanding;
            _reticle.transform.position = p + Vector3.up * 0.03f;
            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 8f);
            _reticle.transform.localScale = Vector3.one * pulse * (ChargeIsPerfect ? 1.25f : 1f);
            Color c = !PredictedOnWater ? new Color(0.88f, 0.33f, 0.28f) : FishFrenzy.Contains(p) ? new Color(1f, 0.8f, 0.25f) : Color.white;
            _reticleMpb.SetColor("_BaseColor", c);
            foreach (var r in _reticle.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(_reticleMpb);
        }

        private void UpdateCharging(bool held, bool up)
        {
            player.ActionAnimation = "holding-right";
            bool canAim = !InputGate.GameplayBlocked;
            float turn = (canAim && (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) ? 1f : 0f)
                       - (canAim && (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) ? 1f : 0f);
            AimOffset = Mathf.Clamp(AimOffset + turn * AimSpeed * Time.deltaTime, -AimLimit, AimLimit);
            player.FaceTowards(player.transform.position + AimDirection);
            _chargeTime += Time.deltaTime;
            Power = Mathf.PingPong(_chargeTime * 0.85f, 1f);
            rod.TargetPitch = Mathf.Lerp(80f, 125f, Power);
            UpdateReticle(true);

            if (up || !held)
            {
                float dist = ChargeDistance;
                LastCastDistance = dist;
                PerfectCast = ChargeIsPerfect;
                UpdateReticle(false);
                if (PerfectCast)
                {
                    GameEvents.Toast("Perfect cast! The fight starts 15% ahead.", 2.5f);
                    AudioManager.Instance?.PlaySfx("SFX/rpg_handleCoins", 0.35f);
                }
                Vector3 fwd = AimDirection; // exactly where the reticle showed
                player.FaceTowards(player.transform.position + fwd, instant: true);
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
                Progression.Energy.Spend(Progression.Energy.CastCost); // only a cast that lands in the water costs energy
                if (FishFrenzy.Contains(rest)) GameEvents.Toast("Right in the frenzy! Fish bite fast here, and they're bigger.", 3f);
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
            if (FishFrenzy.Contains(rod.BobberPosition)) t /= FishFrenzy.BiteSpeedUp; // a feeding shoal
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
                        GameEvents.Toast("Nothing seems to be biting here. Your journal shows where and when each fish swims and what line it needs.", 4.5f);
                    }
                    ScheduleBite(false);
                    return;
                }
                _window = WeightWindow(ctx.shoreDistance);
                _inFrenzy = FishFrenzy.Contains(rod.BobberPosition);
                if (_inFrenzy) _window = Mathf.Min(0.5f, _window + FishFrenzy.WeightBoost);
                _length = FishDatabase.RollWeight(_species, Progression.PlayerStats.QualityLevel, _window);
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
            // Like Stardew: the bar starts around the fish, so the fight begins fairly.
            _reel = new ReelSim(FishPower.ForPlayer(_species), _species.motion, new System.Random(Random.Range(0, int.MaxValue)),
                PerfectCast ? PerfectCastBonus : 0f);
            _reelStart = Time.time;
            LogFight("hooked");
            AudioManager.Instance?.PlaySfx("SFX/reel", 0.5f);
            SetState(FishingState.Reeling);
        }

        /// <summary>
        /// The reeling minigame. Holding lifts the green bar, letting go lets it sink. The fish darts about the track in
        /// its own way (FishMotion), faster the harder it is. Inside the bar the catch meter fills; outside it drains
        /// (slower with the escape-allowance stat). Full = caught, empty = it got away.
        /// </summary>
        /// <summary>How lively the hooked fish is: stronger fish move more, strength training calms them (1 = calm).</summary>
        public float MoveRate => _reel?.P.moveRate ?? 1f;
        /// <summary>How much the fish icon shakes in the reel meter (fraction of the track).</summary>
        public float Vibration => 0.006f + 0.01f * MoveRate;

        /// <summary>How lively a fish would be against your current training.</summary>
        public static float FishMoveRate(FishSpecies s) => FishPower.ForPlayer(s).moveRate;

        /// <summary>
        /// Every fight goes in the chat log (FISHING lines), so the difficulty can be tuned from real play: the fish,
        /// its tier and power gap, the minigame it got, your training, and how it went.
        /// </summary>
        private void LogFight(string result)
        {
            if (_reel == null || _species == null) return;
            var p = _reel.P;
            string stats = $"bar {Progression.PlayerStats.Level("bar")} grip {Progression.PlayerStats.Level("grip")} strength {Progression.PlayerStats.Level("cast")}";
            string what = result == "hooked"
                ? $"hooked {_species.id} ({_species.rarity}, tier {FishPower.TierOf(_species)}, power {FishPower.Power(_species):0.00}, gap {p.gap:+0.00;-0.00}) {FishDatabase.WeightText(_length)}" +
                  $" · cast {LastCastDistance:0.0} m{(PerfectCast ? " PERFECT" : "")}{(_inFrenzy ? " FRENZY" : "")}, {ShoreDistance:0.0} m out, weight window {_window * 100f:0}-{_window * 100f + 50f:0}%, streak {Streak}"
                : $"{result} {_species.id} after {Time.time - _reelStart:0.0} s, in bar {_reel.TimeInBar / Mathf.Max(0.01f, _reel.Time) * 100f:0}%, meter {_reel.Progress:0.00}";
            ChatAudit.Write("FISHING", what, result == "hooked"
                ? $"{stats} · bar size {p.barSize:0.000} move x{p.moveRate:0.00} gain {p.gain:0.000}/s drain {p.drain:0.000}/s · {_species.motion}"
                : null);
        }

        private void UpdateReeling(bool reel)
        {
            player.ActionAnimation = "holding-right";
            // Fighting a fish is tiring: the heavier it is, the faster your energy goes.
            Progression.Energy.Spend(Progression.Energy.ReelDrainPerSecond(_length) * Time.deltaTime);
            if (State != FishingState.Reeling) return; // passed out

            _reel.Step(Time.deltaTime, reel);
            float diff = _reel.Wildness;

            // The rod and bobber show the fight.
            var b = rod.BobberPosition;
            rod.SetBobberRest(new Vector3(b.x, WorldShape.WaterLevel, b.z), FishInBar ? 0.05f : 0.14f);
            player.FaceTowards(b);
            rod.Tension = 1f - Progress;
            rod.Shake = Mathf.Clamp01(Mathf.Abs(_reel.FishVel) * 0.8f + (FishInBar ? 0.1f : 0.4f));
            rod.TargetPitch = Mathf.Lerp(40f, 60f, Progress);
            if (reel && Time.frameCount % 9 == 0) AudioManager.Instance?.PlaySfx("SFX/ui_click_002", 0.12f, 0.2f);
            if (!FishInBar && Random.value < Time.deltaTime * 1.5f)
            {
                Effects.Splash(new Vector3(b.x, 0f, b.z), 0.4f + diff * 0.4f);
                WaterRipples.Spawn(b, 0.7f);
            }

            if (Progress >= 1f)
            {
                LogFight("caught");
                Land();
                return;
            }
            if (Progress <= 0f)
            {
                LogFight("escaped");
                if (Streak >= 3) GameEvents.Toast($"Your streak of {Streak} ends here.", 2.5f);
                Streak = 0;
                AudioManager.Instance?.PlaySfx("SFX/escape", 0.6f);
                GameEvents.Toast(_reel.P.gap > 0.5f ? "It got away! That fish is stronger than your training: Coach Wu can help." : "It got away... next time!", 3.5f);
                FishEscaped?.Invoke("escaped");
                BeginRetrieve();
            }
        }


        /// <summary>E while reeling: give up on this fish (to save energy) and reel in the empty line.</summary>
        public void CutLine()
        {
            if (State != FishingState.Reeling && State != FishingState.Bite) return;
            if (State == FishingState.Reeling) { LogFight("cut"); Streak = 0; }
            AudioManager.Instance?.PlaySfx("SFX/escape", 0.5f);
            GameEvents.Toast("You cut the line and let it go.", 2.5f);
            FishEscaped?.Invoke("cut");
            BeginRetrieve();
        }

        /// <summary>Drop everything at once (passing out).</summary>
        public void ForceStop()
        {
            UpdateReticle(false);
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
            if (_species.IsFish && State == FishingState.Reeling)
            {
                Streak++;
                if (Streak == 3 || Streak == HotStreak || (Streak >= 10 && Streak % 5 == 0))
                    GameEvents.Toast(Streak >= HotStreak ? $"{Streak} in a row! You're on a hot streak: golden fish are more likely." : $"{Streak} in a row!", 3f);
            }
            bool golden = _species.IsFish && (ForceGolden || Random.value < GoldenChance);
            LastCatch = CatchJournal.Record(_species, _length, player.transform.position, golden);
            LastCatch.perfect = Perfect && State == FishingState.Reeling;
            if (LastCatch.perfect) GameEvents.Toast("Perfect catch!", 2f);
            AnnounceCatch(LastCatch);
            // Luck training: sometimes a second fish of the same kind comes up on the hook too.
            if (_species.IsFish && Random.value < Progression.PlayerStats.BonusFishChance)
            {
                float extra = FishDatabase.RollWeight(_species, Progression.PlayerStats.QualityLevel, _window);
                var bonus = CatchJournal.Record(_species, extra, player.transform.position);
                bonus.bonus = true;
                GameEvents.Toast($"Bonus! A second {_species.name.ToLower()} ({FishDatabase.WeightText(extra)}) came up too.", 3.5f);
            }
            _heldCatch = CatchVisuals.Spawn(_species, _length, golden);
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

        /// <summary>A rare golden fish: 3%, plus 0.25% per level of luck training (8% at level 20).</summary>
        public static float GoldenChance => 0.03f + 0.0025f * Progression.PlayerStats.Level("luck") + (Streak >= HotStreak ? 0.02f : 0f);
        /// <summary>Self-test: the next catches are golden.</summary>
        public static bool ForceGolden;

        /// <summary>Golden fish and new medals get a moment of their own (what was achieved, not a demand).</summary>
        public static void AnnounceCatch(CatchResult r)
        {
            if (r == null || !r.species.IsFish) return;
            string weight = FishDatabase.WeightText(r.length);
            if (r.golden)
                GameEvents.Banner($"A golden {r.species.name.ToLower()}!", $"{r.species.hanzi} {r.species.pinyin}, {weight}. Rare and shining: it's worth {CatchJournal.GoldenValue}× as much." +
                                  (r.newMedal == 3 ? "\nAnd it's a gold-medal weight too!" : ""), true);
            else if (r.newMedal == 3)
                GameEvents.Banner($"Gold medal: {r.species.name}!", $"{weight}, one of the biggest {r.species.name.ToLower()} there is. It's on your trophy wall at home now.", true);
            else if (r.newMedal == 2)
                GameEvents.Toast($"Silver medal: your best {r.species.name.ToLower()} yet ({weight}). Gold is for the biggest ones.", 4f);
            else if (r.newMedal == 1 && !r.isNewSpecies)
                GameEvents.Toast($"Bronze medal: {r.species.name}.", 2.5f);
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
