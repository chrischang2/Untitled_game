using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UntitledGame.Companion;
using UntitledGame.Core;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.Player;
using UntitledGame.Progression;

namespace UntitledGame.Home
{
    /// <summary>
    /// How a day ends. Sleep in your bed (any time) and you wake at 6am with full energy. Stay up until 2am, or run
    /// out of energy, and you pass out: everything in your bag except the three most valuable slots is lost, and
    /// you wake up at 10am in the house with three-quarters of your energy.
    /// </summary>
    public class SleepSystem : MonoBehaviour
    {
        public const float WakeHour = 6f;
        public const float PassOutWakeHour = 10f;
        public const float LateHour = 2f;
        public const int KeepWhenPassingOut = 3;

        public static SleepSystem Instance { get; private set; }
        public bool Busy { get; private set; }

        private CanvasGroup _fade;
        private TextMeshProUGUI _fadeText;
        private bool _warnedTired, _warnedMidnight;

        private void Awake()
        {
            Instance = this;
            BuildOverlay();
            Energy.Exhausted += OnExhausted;
        }

        private void OnDestroy()
        {
            Energy.Exhausted -= OnExhausted;
            if (Instance == this) Instance = null;
        }

        private void BuildOverlay()
        {
            var canvasGo = new GameObject("SleepOverlay");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var bg = new GameObject("Black", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)bg.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0.04f, 0.04f, 0.08f, 1f);
            _fade = bg.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
            var txt = new GameObject("Text", typeof(RectTransform));
            txt.transform.SetParent(bg.transform, false);
            _fadeText = txt.AddComponent<TextMeshProUGUI>();
            _fadeText.font = UI.UITheme.Title;
            _fadeText.fontSize = 46;
            _fadeText.alignment = TextAlignmentOptions.Center;
            _fadeText.color = new Color(1f, 0.96f, 0.9f);
            var trt = (RectTransform)txt.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(100, 100);
            trt.offsetMax = new Vector2(-100, -100);
        }

        private static float Hour => DayNightCycle.Instance != null ? DayNightCycle.Instance.TimeOfDay : 12f;

        /// <summary>You can go to bed at any time (you always wake at 6am).</summary>
        public static bool SleepyTime => true;

        private void Update()
        {
            if (Busy || DayNightCycle.Instance == null) return;
            float h = Hour;
            if (h >= LateHour && h < WakeHour)
            {
                StartCoroutine(PassOut("It's 2am... you fall asleep where you stand."));
                return;
            }
            if (h >= 0f && h < 0.25f && !_warnedMidnight)
            {
                _warnedMidnight = true;
                GameEvents.Toast("It's midnight. Get to bed before 2am, or you'll pass out!", 4.5f);
            }
            if (h >= 6f && h < 7f) _warnedMidnight = false;
            if (Energy.Fraction < 0.15f && !_warnedTired)
            {
                _warnedTired = true;
                GameEvents.Toast("You're very tired. Rest soon, or you'll pass out (and lose most of what's in your bag).", 4.5f);
            }
            if (Energy.Fraction > 0.3f) _warnedTired = false;
        }

        private void OnExhausted()
        {
            if (!Busy) StartCoroutine(PassOut("You're completely exhausted... everything goes dark."));
        }

        /// <summary>Sleep in a bed: wake at 6am with full energy. False if it's too early.</summary>
        public bool TrySleep(Vector3 wakeSpot, float wakeYaw, string where)
        {
            if (Busy) return false;
            StartCoroutine(Sleep(wakeSpot, wakeYaw, where));
            return true;
        }

        private IEnumerator Sleep(Vector3 wakeSpot, float yaw, string where)
        {
            Busy = true;
            InputGate.BlockGameplay(this);
            ChatAudit.Write("DAY", $"went to sleep ({where}) at {DayNightCycle.Instance.ClockText}");
            yield return FadeTo(1f, "Good night...");
            int day = DayNightCycle.Instance.Day + (Hour >= WakeHour ? 1 : 0);
            DayNightCycle.Instance.SkipTo(day, WakeHour);
            Energy.Refill(1f);
            PutPlayer(wakeSpot, yaw);
            SaveSystem.Save();
            ChatAudit.Write("DAY", $"woke up: day {day}, 6:00 AM, energy {Energy.Current:0}/{Energy.Max:0}");
            _fadeText.text = $"Day {day}\nYou slept well. Energy {Energy.Max:0}.";
            yield return new WaitForSecondsRealtime(1.2f);
            yield return FadeTo(0f, null);
            InputGate.UnblockGameplay(this);
            Busy = false;
            CompanionBrain.Current?.SendGameEvent("It's a new morning. The player just woke up after a good night's sleep.", "Say good morning in one short sentence. No lesson.");
        }

        private IEnumerator PassOut(string why)
        {
            Busy = true;
            InputGate.BlockGameplay(this);
            FindFirstObjectByType<FishingController>()?.ForceStop();
            if (ShopConversation.Active != null) ShopConversation.Instance.End(sayGoodbye: false);
            ChatAudit.Write("DAY", $"passed out at {DayNightCycle.Instance.ClockText} (energy {Energy.Current:0}): {why}");
            yield return FadeTo(1f, why);
            yield return new WaitForSecondsRealtime(1.2f);

            var lost = Inventory.LoseAllBut(KeepWhenPassingOut);
            int day = DayNightCycle.Instance.Day + (Hour >= WakeHour ? 1 : 0);
            DayNightCycle.Instance.SkipTo(day, PassOutWakeHour);
            Energy.Refill(0.75f);
            var bed = BedInteractable.Instance;
            PutPlayer(bed != null ? bed.WakeSpot : FallbackSpot(), bed != null ? bed.WakeYaw : 0f);
            Rowboat.Instance?.ReturnToDock();
            SaveSystem.Save();
            string lostText = lost.Count == 0 ? "Nothing was lost." : "Lost: " + string.Join(", ", lost.Select(s => $"{s.count} x {s.label}"));
            ChatAudit.Write("DAY", $"woke up in the house: day {day}, 10:00 AM. {lostText}");
            _fadeText.text = $"Day {day}, 10:00 AM\nYou wake up at home, feeling rough.\n<size=30>{lostText}</size>";
            yield return new WaitForSecondsRealtime(3f);
            yield return FadeTo(0f, null);
            InputGate.UnblockGameplay(this);
            Busy = false;
            GameEvents.Toast(lost.Count == 0 ? "You passed out, but your bag was safe." : $"You passed out and lost {lost.Count} slot{(lost.Count == 1 ? "" : "s")} of things. {lostText}", 6f);
            CompanionBrain.Current?.SendGameEvent(
                "The player passed out last night from exhaustion and woke up at home late in the morning, having lost most of what they were carrying.",
                "Show you were worried, and gently remind them to sleep before 2am and watch their energy. One or two short sentences. No lesson.");
        }

        /// <summary>
        /// Fades to black with a caption, runs <paramref name="whileDark"/>, holds, then fades back (the bus ride uses it).
        /// Gameplay is blocked throughout.
        /// </summary>
        public IEnumerator Blackout(string text, System.Action whileDark, string darkText = null, float hold = 1.5f)
        {
            Busy = true;
            InputGate.BlockGameplay(this);
            yield return FadeTo(1f, text);
            whileDark?.Invoke();
            if (darkText != null) _fadeText.text = darkText;
            yield return new WaitForSecondsRealtime(hold);
            yield return FadeTo(0f, null);
            InputGate.UnblockGameplay(this);
            Busy = false;
        }

        /// <summary>Moves the player (out of any seat) and brings Mei along.</summary>
        public static void MovePlayer(Vector3 spot, float yaw) => PutPlayer(spot, yaw);

        private static Vector3 FallbackSpot()
        {
            Vector2 c = WorldShape.CabinCenter;
            return new Vector3(c.x, Cabin.GroundHeight(new Vector3(c.x, 0f, c.y)) + 0.1f, c.y);
        }

        private static void PutPlayer(Vector3 spot, float yaw)
        {
            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;
            if (player.Seat != null)
            {
                player.Seat = null;
                player.transform.SetParent(null, true);
            }
            player.Teleport(spot, yaw);
            FindFirstObjectByType<CameraControl.CameraRig>()?.SetYaw(yaw);
            FindFirstObjectByType<CompanionController>()?.Warp();
        }

        private IEnumerator FadeTo(float target, string text)
        {
            if (text != null) _fadeText.text = text;
            _fade.blocksRaycasts = target > 0.5f;
            while (!Mathf.Approximately(_fade.alpha, target))
            {
                _fade.alpha = Mathf.MoveTowards(_fade.alpha, target, Time.unscaledDeltaTime * 1.4f);
                yield return null;
            }
            if (target <= 0f) _fadeText.text = "";
        }
    }
}
