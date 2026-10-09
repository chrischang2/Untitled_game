using System.Linq;
using UnityEngine;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Player;

namespace UntitledGame.Home
{
    /// <summary>
    /// Tangyuan (汤圆), the camp cat. Hunger rises over the day and happiness slowly fades; she eats
    /// from her bowl, sleeps in her bed at night, plays with toys, follows the player when happy and
    /// loves pets and dried-fish treats (F). A bell collar makes her jingle as she walks.
    /// </summary>
    public class PetController : Interactable
    {
        private enum Mode { Idle, Walking, Eating, Sleeping, Playing, Following }

        [SerializeField] private CharacterAnimator animator;
        [SerializeField] private Transform player;
        [SerializeField] private float walkSpeed = 1.3f;

        private Mode _mode = Mode.Idle;
        private Vector3 _target;
        private Transform _targetItem;
        private float _modeUntil;
        private float _nextDecision;
        private float _lastPet = -99f;
        private float _jingleTimer;
        private Vector3 _homeSpot;
        private GameObject _collar;
        private float _lastHour = -1f;

        public const string DisplayName = "Tangyuan";

        public void Configure(CharacterAnimator anim, Transform playerTransform)
        {
            animator = anim;
            player = playerTransform;
            radius = 1.8f;
        }

        private PetState Pet => SaveSystem.Data.pet;

        public enum FeedResult { FedFood, FedTreat, NoFood, NotHungry, TooFar }

        /// <summary>The cat in the scene (there is one).</summary>
        public static PetController Instance { get; private set; }

        protected override void OnEnable()
        {
            base.OnEnable();
            Instance = this;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Instance == this) Instance = null;
        }

        private bool Hungry => Pet.hunger > 0.15f;

        public override string Prompt =>
            Hungry && Inventory.Count("cat_food") > 0 ? "[F] Feed Tangyuan some cat food" :
            Hungry && Inventory.Count("cat_treat") > 0 ? "[F] Give Tangyuan a dried-fish treat" :
            "[F] Pet Tangyuan";

        public float DistanceToPlayer => player == null ? 999f : Vector3.Distance(player.position, transform.position);

        /// <summary>
        /// Feeds her from your hand: cat food if you have it, otherwise a treat. No bowl needed (before, cat food only
        /// worked through a placed bowl, so a player without one had no way to feed her). Also used by the spoken 喂汤圆.
        /// </summary>
        public FeedResult Feed(float maxDistance = 3f)
        {
            if (DistanceToPlayer > maxDistance) return FeedResult.TooFar;
            if (!Hungry) return FeedResult.NotHungry;
            FeedResult result;
            if (Inventory.Remove("cat_food", 1))
            {
                Pet.hunger = Mathf.Clamp01(Pet.hunger - 0.7f);
                Pet.happiness = Mathf.Clamp01(Pet.happiness + 0.12f);
                result = FeedResult.FedFood;
                GameEvents.Toast("Tangyuan gobbled up some cat food.");
            }
            else if (Inventory.Remove("cat_treat", 1))
            {
                Pet.hunger = Mathf.Clamp01(Pet.hunger - 0.35f);
                Pet.happiness = Mathf.Clamp01(Pet.happiness + 0.25f);
                result = FeedResult.FedTreat;
                GameEvents.Toast("Tangyuan loved her dried-fish treat.");
            }
            else return FeedResult.NoFood;

            animator?.PlayOnce("eat", "idle", 0.15f);
            AudioManager.Instance?.PlaySfx("SFX/bubble_02", 0.35f, 0.2f);
            CheckFull();
            SaveSystem.Save();
            Effects.Sparkle(transform.position + Vector3.up * 0.5f, 10);
            if (player != null) FacePoint(player.position);
            _mode = Mode.Idle;
            _modeUntil = Time.time + 3f;
            return result;
        }

        private void Start()
        {
            _homeSpot = transform.position;
            animator?.Play("idle", 0f);
            _nextDecision = Time.time + 3f;
        }

        public override void Interact()
        {
            if (Hungry && Feed(float.MaxValue) != FeedResult.NoFood) return;
            if (Time.time - _lastPet > 5f)
            {
                _lastPet = Time.time;
                Pet.happiness = Mathf.Clamp01(Pet.happiness + 0.08f);
                animator?.PlayOnce("gesture-positive", "idle", 0.15f);
                AudioManager.Instance?.PlaySfx("SFX/ui_pluck_002", 0.3f, 0.3f);
            }
            SaveSystem.Save();
            Effects.Sparkle(transform.position + Vector3.up * 0.5f, 10);
            FacePoint(player.position);
            _mode = Mode.Idle;
            _modeUntil = Time.time + 3f;
        }

        private void Update()
        {
            UpdateNeeds();
            UpdateCollar();
            UpdateBaitGift();

            switch (_mode)
            {
                case Mode.Walking:
                case Mode.Following:
                    Walk();
                    break;
                case Mode.Eating:
                    if (Time.time > _modeUntil)
                    {
                        Pet.hunger = 0f;
                        Pet.bowlFilled = false;
                        Pet.happiness = Mathf.Clamp01(Pet.happiness + 0.15f);
                        CheckFull();
                        SaveSystem.Save();
                        HomeItems.Instance?.RefreshBowl();
                        SetMode(Mode.Idle, 4f);
                    }
                    break;
                default:
                    if (Time.time > _modeUntil && Time.time > _nextDecision) Decide();
                    break;
            }
        }

        private static int Today => DayNightCycle.Instance != null ? DayNightCycle.Instance.Day : SaveSystem.Data.day;

        /// <summary>Fed until full: remember the day; she'll bring a present tomorrow.</summary>
        private void CheckFull()
        {
            if (Pet.hunger > 0.12f || Pet.fullDay == Today) return;
            Pet.fullDay = Today;
            ChatAudit.Write("PET", $"Tangyuan is full (day {Today}): she'll bring some bait tomorrow");
            GameEvents.Toast("Tangyuan is full and purring. She looks like she's planning something for tomorrow...", 3.5f);
        }

        /// <summary>
        /// The day after being fed until full, Tangyuan brings the player a little bait: a random kind Old Wang would sell
        /// you (unlocked by friendship), cheaper kinds more often.
        /// </summary>
        private void UpdateBaitGift()
        {
            if (Pet.fullDay < 0 || Today <= Pet.fullDay || Pet.giftDay >= Today) return;
            if (player == null || DistanceToPlayer > 10f || Fishing.Rowboat.PlayerAboard) return;
            var (bait, count) = PickBaitGift();
            if (bait != null && !Inventory.HasRoomFor(bait.id, count))
            {
                if (Time.frameCount % 600 == 0) GameEvents.Toast("Tangyuan has something for you, but your bag is full!", 3f);
                return; // she waits until you have room
            }
            Pet.giftDay = Today;
            Pet.fullDay = -1;
            if (bait == null) return;
            Inventory.Add(bait.id, count);
            ChatAudit.Write("PET", $"Tangyuan brought {count} x {bait.english}");
            animator?.PlayOnce("gesture-positive", "idle", 0.15f);
            AudioManager.Instance?.PlaySfx("SFX/ui_pluck_002", 0.4f, 0.2f);
            Effects.Sparkle(transform.position + Vector3.up * 0.5f, 16);
            FacePoint(player.position);
            GameEvents.Toast($"Tangyuan brought you {count} {bait.english}! A thank-you for feeding her so well yesterday.", 5f);
            UntitledGame.Companion.CompanionBrain.Current?.SendGameEvent(
                $"{UntitledGame.Companion.CompanionPersona.PetName} just brought the player {count} {bait.hanzi} [{bait.english.ToLower()}] as a thank-you for being fed so well yesterday.",
                "React with delight in one short sentence. No lesson.");
            SaveSystem.Save();
        }

        /// <summary>A bait Old Wang would sell you right now, weighted towards cheap ones (weight = 1 / price).</summary>
        /// <summary>Twice the bait when the pet-shop keeper here is an old friend (Perks).</summary>
        private static int PerkFactor => Progression.Perks.Has("pet") ? 2 : 1;

        public static (ItemDef bait, int count) PickBaitGift()
        {
            var shop = Catalog.ShopFor("tackle", Regions.Current);
            int friendship = Progression.Affinity.Level(shop.id);
            var baits = shop.items.Select(Catalog.Get).Where(i => i != null && i.category == ItemCategory.Bait && i.minAffinity <= friendship).ToList();
            if (baits.Count == 0) return (null, 0);
            float total = baits.Sum(b => 1f / Mathf.Max(1, b.price));
            float roll = Random.value * total;
            foreach (var b in baits)
            {
                roll -= 1f / Mathf.Max(1, b.price);
                if (roll <= 0f) return (b, (b.price <= 30 ? 5 : 3) * PerkFactor);
            }
            var last = baits[baits.Count - 1];
            return (last, (last.price <= 30 ? 5 : 3) * PerkFactor);
        }

        private void UpdateNeeds()
        {
            var dn = DayNightCycle.Instance;
            if (dn == null) return;
            float hour = dn.TimeOfDay;
            if (_lastHour < 0f) { _lastHour = hour; return; }
            float delta = Mathf.Repeat(hour - _lastHour, 24f);
            _lastHour = hour;
            if (delta > 2f) return; // time jump (debug / loading)
            Pet.hunger = Mathf.Clamp01(Pet.hunger + delta * 0.035f);
            Pet.happiness = Mathf.Clamp01(Pet.happiness - delta * (Pet.hunger > 0.7f ? 0.03f : 0.012f));
        }

        private void Decide()
        {
            _nextDecision = Time.time + Random.Range(4f, 9f);
            var home = HomeItems.Instance;
            var dn = DayNightCycle.Instance;
            bool night = dn != null && dn.Darkness > 0.6f;

            var bowl = home != null ? home.Find("cat_bowl") : null;
            if (Pet.hunger > 0.45f && Pet.bowlFilled && bowl != null)
            {
                GoTo(bowl, Mode.Eating);
                return;
            }

            var bed = home != null ? (home.Find("cat_bed") ?? home.Find("cat_box")) : null;
            if (night && bed != null)
            {
                GoTo(bed, Mode.Sleeping);
                return;
            }

            float dPlayer = player != null ? Vector3.Distance(player.position, transform.position) : 99f;
            if (Pet.happiness > 0.65f && dPlayer < 25f && Random.value < 0.45f)
            {
                _mode = Mode.Following;
                _modeUntil = Time.time + Random.Range(15f, 35f);
                return;
            }

            var toy = home != null ? (Random.value < 0.5f ? home.Find("yarn") : home.Find("scratcher")) : null;
            if (toy != null && Random.value < 0.35f)
            {
                GoTo(toy, Mode.Playing);
                return;
            }

            if (Random.value < 0.5f)
            {
                Vector3 anchor = bed != null ? bed.position : _homeSpot;
                Vector2 r = Random.insideUnitCircle * 4f;
                Vector3 p = anchor + new Vector3(r.x, 0f, r.y);
                if (PlayerController.IsWalkable(p))
                {
                    _target = p;
                    _targetItem = null;
                    _mode = Mode.Walking;
                    _modeUntil = Time.time + 20f;
                    return;
                }
            }
            SetMode(Mode.Idle, Random.Range(4f, 10f));
        }

        private Mode _arriveMode;

        private void GoTo(Transform item, Mode then)
        {
            _targetItem = item;
            _target = item.position;
            _arriveMode = then;
            _mode = Mode.Walking;
            _modeUntil = Time.time + 25f;
        }

        private void Walk()
        {
            Vector3 goal = _mode == Mode.Following && player != null
                ? player.position - player.forward * 1.2f + player.right * 0.8f
                : _target;
            Vector3 to = goal - transform.position;
            to.y = 0f;
            float stop = _targetItem != null ? 0.55f : 0.35f;

            if (_mode == Mode.Following)
            {
                if (Fishing.Rowboat.PlayerAboard) { SetMode(Mode.Idle, 3f); return; } // cats don't do boats
                if (Time.time > _modeUntil) { SetMode(Mode.Idle, 3f); return; }
                if (to.magnitude < 1.2f) { animator?.Play("idle", 0.2f); return; }
                if (to.magnitude > 30f) { SetMode(Mode.Idle, 3f); return; }
            }
            else if (to.magnitude < stop || Time.time > _modeUntil)
            {
                Arrive();
                return;
            }

            float speed = _mode == Mode.Following && to.magnitude > 5f ? walkSpeed * 2.2f : walkSpeed;
            // In or out of the cabin: go via the door instead of through the wall.
            Vector3 via = Cabin.Waypoint(transform.position, goal) - transform.position;
            via.y = 0f;
            Vector3 dir = via.sqrMagnitude > 0.0001f ? via.normalized : to.normalized;
            Vector3 next = transform.position + dir * speed * Time.deltaTime;
            if (!PlayerController.IsWalkable(next))
            {
                SetMode(Mode.Idle, 2f);
                return;
            }
            next.y = Cabin.GroundHeight(next);
            transform.position = next;
            FacePoint(goal);
            animator?.Play(speed > walkSpeed ? "run" : "walk", 0.2f);
            _jingleTimer -= Time.deltaTime;
            if (Pet.collar && _jingleTimer <= 0f)
            {
                _jingleTimer = 0.45f;
                AudioManager.Instance?.PlayAt("SFX/ui_glass_002", transform.position, 0.25f, 0.15f);
            }
        }

        private void Arrive()
        {
            var then = _targetItem != null ? _arriveMode : Mode.Idle;
            _targetItem = null;
            switch (then)
            {
                case Mode.Eating:
                    animator?.Play("eat", 0.2f);
                    _mode = Mode.Eating;
                    _modeUntil = Time.time + 4f;
                    break;
                case Mode.Sleeping:
                    animator?.Play("idle", 0.4f);
                    SetMode(Mode.Sleeping, 30f);
                    break;
                case Mode.Playing:
                    animator?.PlayOnce("dance", "idle", 0.2f);
                    Pet.happiness = Mathf.Clamp01(Pet.happiness + 0.05f);
                    SetMode(Mode.Playing, 4f);
                    break;
                default:
                    SetMode(Mode.Idle, Random.Range(3f, 8f));
                    break;
            }
        }

        private void SetMode(Mode m, float seconds)
        {
            _mode = m;
            _modeUntil = Time.time + seconds;
            if (m == Mode.Idle || m == Mode.Sleeping) animator?.Play("idle", 0.3f);
        }

        private void FacePoint(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d.normalized), 360f * Time.deltaTime);
        }

        private void UpdateCollar()
        {
            if (Pet.collar && _collar == null)
            {
                _collar = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(_collar.GetComponent<Collider>());
                _collar.name = "Bell";
                _collar.transform.SetParent(transform, false);
                _collar.transform.localPosition = new Vector3(0f, 0.2f, 0.17f);
                _collar.transform.localScale = Vector3.one * 0.07f;
                var r = _collar.GetComponent<Renderer>();
                r.sharedMaterial = GameAssets.Instance.bobberWhite;
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", new Color(1f, 0.8f, 0.2f));
                mpb.SetColor("_EmissionColor", new Color(0.2f, 0.15f, 0f));
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
