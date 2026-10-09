using System.Collections;
using UnityEngine;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Fishing;
using UntitledGame.Home;
using UntitledGame.Language;

namespace UntitledGame.Environment
{
    /// <summary>
    /// The bus stop (汽车站) between the camp and the market: the bus is always parked there, but 张师傅 the driver only
    /// turns up once the first HSK test is passed (BusTrip.Running). Riding is arranged by talking to him (he takes the
    /// fare fish and drives you on); F at the bus just explains. The ride is a fade to black: you get off at the same
    /// stop in the next region.
    /// </summary>
    public class BusStop : Interactable
    {
        [SerializeField] private GameObject driver;

        public static BusStop Instance { get; private set; }
        public GameObject Driver => driver;
        public bool Riding { get; private set; }

        public void Configure(GameObject driverGo) => driver = driverGo;

        private void Awake()
        {
            Instance = this;
            radius = 4.2f;
        }

        private void Start()
        {
            ShopSign.CreateAt(transform, WorldShape.BusSignSpot3D(), new Vector3(0f, 0f, 1f), "汽车站");
            Refresh();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            bool on = BusTrip.Running;
            if (driver != null && driver.activeSelf != on) driver.SetActive(on);
        }

        public override bool Available => !Riding;

        public override string Prompt => BusTrip.Running
            ? "汽车站 Bus stop: talk to 张师傅 the driver (E) to ride the bus"
            : "汽车站 Bus stop: the bus starts running once you pass the HSK 1 test";

        public override void Interact()
        {
            AudioManager.Instance?.PlaySfx("SFX/rpg_bookFlip1", 0.4f);
            GameEvents.Toast(BusTrip.Running
                ? $"Talk to the driver 张师傅 (E): say 我想去… or 我要坐车. {BusTrip.Describe()}"
                : "The bus doesn't run yet. Once you pass the HSK 1 test, a driver will be here to take you to the next stop, for a fare paid in fish.", 6f);
        }

        /// <summary>Where you get off: on the path side of the bus door, facing the path.</summary>
        public static Vector3 ArrivalSpot
        {
            get
            {
                Vector2 p = WorldShape.BusArrivalSpot;
                return new Vector3(p.x, WorldShape.TerrainHeight(p.x, p.y) + 0.05f, p.y);
            }
        }

        /// <summary>Rides the bus to a region after a short pause (so the driver's line can start).</summary>
        public void Ride(int region, float delay = 2.5f)
        {
            if (Riding) return;
            StartCoroutine(RideRoutine(region, delay));
        }

        private IEnumerator RideRoutine(int region, float delay)
        {
            Riding = true;
            var from = Regions.Here;
            var to = Regions.Get(region);
            ChatAudit.Write("BUS", $"riding the bus from {from.english} to {to.english}");
            yield return new WaitForSecondsRealtime(delay);
            FindFirstObjectByType<FishingController>()?.ForceStop();
            if (ShopConversation.Active != null) ShopConversation.Instance.End(sayGoodbye: false);
            Rowboat.Instance?.ReturnToDock();

            int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.Day + 1 : SaveSystem.Data.day + 1;
            void Arrive()
            {
                // The ride takes the night: you arrive the next morning, rested as if you'd slept in the house there.
                BusTrip.Arrive(region);
                if (DayNightCycle.Instance != null) DayNightCycle.Instance.SkipTo(day, SleepSystem.WakeHour);
                Progression.Energy.Refill(1f);
                SleepSystem.MovePlayer(ArrivalSpot, 0f);
                ChatAudit.Write("BUS", $"arrived on day {day} at 6:00 AM with {Progression.Energy.Current:0}/{Progression.Energy.Max:0} energy");
                SaveSystem.Save();
            }
            var sleep = SleepSystem.Instance;
            if (sleep != null)
                yield return sleep.Blackout($"The bus pulls away from {from.english}...", Arrive,
                    $"{to.hanzi}  {to.english}\n<size=30>Day {day}, 6:00 AM: you slept on the bus ({Progression.Energy.Max:0} energy)</size>", 2.2f);
            else Arrive();

            AudioManager.Instance?.PlaySfx("SFX/ui_confirmation_002", 0.6f);
            GameEvents.Banner($"Welcome to {to.english}!", $"{to.hanzi} {Pinyin.Of(to.hanzi)}: {to.blurb}. Everyone from the market came along, and your house is here too." +
                              (region > 0 && region == Regions.Last ? " New fish live in this sea." : ""), true);
            Riding = false;
        }
    }
}
