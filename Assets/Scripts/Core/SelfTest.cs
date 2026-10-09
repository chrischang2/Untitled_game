using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UntitledGame.CameraControl;
using UntitledGame.Companion;
using UntitledGame.Economy;
using UntitledGame.Environment;
using UntitledGame.Fishing;
using UntitledGame.GenAI;
using UntitledGame.Home;
using UntitledGame.Language;
using UntitledGame.Minigames;
using UntitledGame.Player;
using UntitledGame.Progression;
using UntitledGame.UI;

namespace UntitledGame.Core
{
    /// <summary>
    /// Automated end-to-end check, run with the command-line flag <c>-selftest</c>. Speech is synthesised
    /// with the game's own voices and fed through the real recognition path (SenseVoice -> LLM -> TTS):
    /// talks to Mei in English and Mandarin, buys a rod from 老王 in spoken Mandarin, sells fish to 陈阿姨,
    /// buys and places a cat bowl, feeds and pets Tangyuan, fishes, captures screenshots, then quits.
    /// </summary>
    public class SelfTest : MonoBehaviour
    {
        private readonly StringBuilder _log = new StringBuilder();
        private string _outDir;
        private float _t0;
        private int _failures;
        private static bool _started;

        private void Start()
        {
            if (!System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == "-selftest")) return;
            // The scene is reloaded during the save/load checks; its fresh copy of this component must not start over.
            if (_started)
            {
                enabled = false;
                return;
            }
            _started = true;
            string root = LocalAIConfig.FindRoot();
            _outDir = root != null ? Path.Combine(Path.GetDirectoryName(root), "Captures", "selftest") : Path.Combine(Application.persistentDataPath, "selftest");
            Directory.CreateDirectory(_outDir);
            _t0 = Time.realtimeSinceStartup;
            SaveSystem.SwitchTo(1, fresh: true); // always a new game in slot 1 of the test profile
            // GameBootstrap may already have restored the previous test run's chat; start clean.
            ConversationLog.Clear();
            foreach (var agent in FindObjectsByType<DialogueAgent>(FindObjectsSortMode.None)) agent.ClearConversation();
            SaveSystem.Settings.handsFree = false;
            SaveSystem.Settings.immersion = ImmersionLevel.Beginner;
            SaveSystem.Settings.pinyin = PinyinMode.Always;
            // -only parser,home,school runs just those sections (the full run takes about 8 minutes).
            var args = System.Environment.GetCommandLineArgs();
            int only = System.Array.IndexOf(args, "-only");
            if (only >= 0 && only + 1 < args.Length) StartCoroutine(RunOnly(args[only + 1].Split(',')));
            else StartCoroutine(Run());
        }

        /// <summary>Scoped run: "parser" (shop parser, HSK list and grading), "home" (stranded furniture), "school" (the test centre).</summary>
        private IEnumerator RunOnly(string[] sections)
        {
            Log("Scoped self-test: " + string.Join(", ", sections));
            yield return RunSections(sections);
            Log(_failures == 0 ? "SELFTEST COMPLETE: all checks passed" : $"SELFTEST COMPLETE: {_failures} check(s) failed");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>The newer sections that aren't part of the original run (the full suite runs them at the end).</summary>
        public static readonly string[] ExtraSections = { "mastery", "requests", "market", "sale", "fish", "balance", "hud", "crabs", "pitchpot", "bus", "home" };

        private IEnumerator RunSections(string[] sections)
        {
            var services = LocalAIServices.Instance;
            var vc = FindFirstObjectByType<VoiceChatController>();
            var player = FindFirstObjectByType<PlayerController>();
            var cam = FindFirstObjectByType<CameraRig>();
            var ui = FindFirstObjectByType<GameUI>();
            while (!Pinyin.Ready) yield return null;
            yield return new WaitForSeconds(1f);
            foreach (var section in sections.Select(s => s.Trim().ToLowerInvariant()))
            {
                // Each section starts from a clean slate (after the full run a line may still be out, or a shop open).
                FindFirstObjectByType<FishingController>()?.ForceStop();
                if (ShopConversation.Active != null) ShopConversation.Instance.End(sayGoodbye: false);
                PitchPotGame.Active?.Stop();
                yield return null;
                switch (section)
                {
                    case "parser":
                        ParserChecks();
                        break;
                    case "requests":
                    {
                        float wait = Time.realtimeSinceStartup + 240f;
                        while (Time.realtimeSinceStartup < wait && services.LlmStatus == ServiceStatus.Starting) yield return null;
                        Check(services.LlmStatus == ServiceStatus.Ready, "the shopkeepers' AI is ready");
                        yield return RequestChecks();
                        break;
                    }
                    case "mastery":
                        MasteryChecks();
                        break;
                    case "balance":
                        BalanceChecks();
                        break;
                    case "pitchpot":
                        yield return AimChecks(player, cam);
                        yield return PitchPotChecks(player, cam);
                        break;
                    case "crabs":
                    {
                        float wait = Time.realtimeSinceStartup + 240f;
                        while (Time.realtimeSinceStartup < wait && services.LlmStatus == ServiceStatus.Starting) yield return null;
                        Check(services.LlmStatus == ServiceStatus.Ready, "the shopkeepers' AI is ready");
                        yield return CrabChecks(player, cam, ui);
                        break;
                    }
                    case "hud":
                        yield return HudChecks(ui);
                        break;
                    case "bus":
                    {
                        float wait = Time.realtimeSinceStartup + 240f;
                        while (Time.realtimeSinceStartup < wait && services.LlmStatus == ServiceStatus.Starting) yield return null;
                        Check(services.LlmStatus == ServiceStatus.Ready, "the shopkeepers' AI is ready");
                        yield return BusChecks(player, cam);
                        break;
                    }
                    case "market":
                        yield return MarketChecks(player, cam);
                        break;
                    case "mei":
                    {
                        float wait = Time.realtimeSinceStartup + 240f;
                        while (Time.realtimeSinceStartup < wait &&
                               (services.LlmStatus == ServiceStatus.Starting || services.SttStatus == ServiceStatus.Starting || services.TtsStatus == ServiceStatus.Starting))
                            yield return null;
                        Check(services.LlmStatus == ServiceStatus.Ready && services.SttStatus == ServiceStatus.Ready, "Mei's AI and speech recognition are ready");
                        yield return LongConversationChecks();
                        break;
                    }
                    case "sale":
                    {
                        float wait = Time.realtimeSinceStartup + 240f;
                        while (Time.realtimeSinceStartup < wait && services.LlmStatus == ServiceStatus.Starting) yield return null;
                        Check(services.LlmStatus == ServiceStatus.Ready, "the shopkeepers' AI is ready");
                        yield return SaleChecks(player, cam, ui);
                        break;
                    }
                    case "fish":
                        StarterAndReelChecks();
                        yield return CatchCardShot(ui);
                        FishUnitChecks();
                        yield return TrophyChecks(player, cam);
                        break;
                    case "home":
                        yield return StrandedItemChecks(player, cam);
                        ui.OpenPhrasebook();
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot("20_phrasebook");
                        ui.CloseJournal();
                        break;
                    case "school":
                    {
                        float deadline = Time.realtimeSinceStartup + 240f;
                        while (Time.realtimeSinceStartup < deadline &&
                               (services.LlmStatus == ServiceStatus.Starting || services.SttStatus == ServiceStatus.Starting || services.TtsStatus == ServiceStatus.Starting))
                            yield return null;
                        Check(services.LlmStatus == ServiceStatus.Ready && services.SttStatus == ServiceStatus.Ready && services.TtsStatus == ServiceStatus.Ready, "all AI services ready");
                        foreach (var k in ShopkeeperBrain.Keepers)
                        {
                            var agent = k;
                            agent.SentenceSpoken += s => Log($"   {agent.DisplayNameEnglish} says: {s}");
                        }
                        yield return SchoolChecks(vc, player, cam, ui);
                        break;
                    }
                    default:
                        Log($"(unknown section \"{section}\": use parser, home or school)");
                        break;
                }
            }
        }

        /// <summary>Every stall has a painted sign; screenshot from the plaza.</summary>
        private IEnumerator MarketChecks(PlayerController player, CameraRig cam)
        {
            yield return new WaitForSeconds(0.5f);
            var stallKeepers = ShopkeeperBrain.Keepers.Where(k => !k.Shop.busDriver).ToList();
            int stalls = Catalog.StallShops(Regions.Current).Count();
            int signs = stallKeepers.Count(k => k.transform.parent != null && k.transform.parent.Find("ShopSign") != null);
            Check(signs == stallKeepers.Count && signs == stalls, $"every stall has a sign ({signs}/{stalls})");
            Vector2 c = WorldShape.MarketCenter;
            player.Teleport(new Vector3(c.x - 3f, WorldShape.TerrainHeight(c.x - 3f, c.y) + 0.05f, c.y), 90f);
            cam.Configure(player.transform, 90f, 14f, 6f);
            yield return new WaitForSeconds(5f);
            yield return Shot("22_market_signs");
            var fish = ShopkeeperBrain.Keepers.First(k => k.Shop.buysFish);
            int i = fish.Shop.stall;
            Vector2 spot = WorldShape.CustomerSpot(i), stall = WorldShape.StallPosition(i);
            Vector2 dir = (stall - spot).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            player.Teleport(new Vector3(spot.x, WorldShape.TerrainHeight(spot.x, spot.y) + 0.05f, spot.y), yaw);
            cam.Configure(player.transform, yaw, 12f, 5f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("23_sushi_stall");
        }

        /// <summary>Word mastery boxes (spaced repetition) and practice that picks due and weak words first.</summary>
        private void MasteryChecks()
        {
            var d = SaveSystem.Data;
            d.mastery.Clear();
            int day = d.day;
            Hsk.RecordAnswer("苹果", true, recall: false);
            Check(Hsk.Box("苹果") == 1, $"repeating a word after the teacher marks it as seen (box {Hsk.Box("苹果")})");
            Hsk.RecordAnswer("苹果", true, recall: true);
            Hsk.RecordAnswer("苹果", true, recall: true);
            Check(Hsk.Box("苹果") == 2, $"saying it from English moves it up one box a day, not more (box {Hsk.Box("苹果")})");
            d.day = day + 1; Hsk.RecordAnswer("苹果", true, true);
            d.day = day + 2; Hsk.RecordAnswer("苹果", true, true);
            Check(Hsk.Known("苹果") && Hsk.KnownCount(1) == 1, $"after a few days of right answers it counts as known (box {Hsk.Box("苹果")}; HSK 1 known: {Hsk.KnownCount(1)}/150)");
            Hsk.RecordAnswer("苹果", false, true);
            Check(Hsk.Box("苹果") == 2, $"a wrong answer drops it two boxes (box {Hsk.Box("苹果")})");
            Check(!Hsk.Due("苹果") && Hsk.Seen("苹果"), "it isn't due again the same day");
            d.day = day + 4;
            Check(Hsk.Due("苹果"), "...but it is after its interval (2 days for box 2)");

            // Using a word of your own accord counts too (up to 'known').
            d.day = day + 5;
            Hsk.RecordAnswer("喝", true, false);
            Hsk.ObserveSpoken("我想喝茶");
            Check(Hsk.Box("喝") == 2 && Hsk.Box("茶") == 0, "saying 喝 to someone moves it up; 茶 (never practised) isn't counted yet");

            // Practice: the weak and due words come first.
            d.day = day + 10;
            foreach (var w in new[] { "猫", "狗", "飞机" }) Hsk.RecordAnswer(w, false, true);
            var picks = Hsk.PracticeWords(1, Hsk.PracticeQuestions, new System.Random(3));
            Check(picks.Count == Hsk.PracticeQuestions && new[] { "猫", "狗", "飞机", "苹果", "喝" }.All(w => picks.Any(p => p.hanzi == w)),
                $"practice picks the due and weak words first ({string.Join(" ", picks.Select(p => p.hanzi))})");
            d.day = day;
            d.mastery.Clear();
        }

        /// <summary>Auntie Chen's fish of the day, and surprise presents for new words.</summary>
        private IEnumerator RequestChecks()
        {
            var d = SaveSystem.Data;
            if (!d.discoveredFish.Contains("seabass")) d.discoveredFish.Add("seabass");
            d.requestDay = -1;
            DailyRequest.Ensure();
            Check(DailyRequest.Active && DailyRequest.Fish != null && DailyRequest.Chinese.StartsWith("今天我想要"),
                $"Auntie Chen has a fish of the day: {DailyRequest.Chinese} ({DailyRequest.Count} x {DailyRequest.Fish?.name})");
            // Make it two sea bass, bring two, and sell them to her.
            d.requestFish = "seabass";
            d.requestCount = 2;
            d.requestMinKg = 0f;
            Inventory.SellAllFish();
            var bass = FishDatabase.Get("seabass");
            Inventory.AddToBucket(bass, 2f);
            Check(!DailyRequest.CanFulfil && DailyRequest.Matching == 1, "one sea bass isn't enough for a request of two");
            Inventory.AddToBucket(bass, 3f);
            int money = Inventory.Money, items = Inventory.Owned().Sum(x => x.count);
            var fish = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "fish");
            fish.HandlePlayerUtterance("阿姨，我想卖鱼。");
            Check(fish.PendingOffer != null && fish.PendingOffer.selling, "she offers to buy the fish");
            fish.HandlePlayerUtterance("可以。");
            fish.Interrupt();
            int value = Inventory.Money - money;
            Check(DailyRequest.Done && Inventory.BucketCount == 0 && (Inventory.Owned().Sum(x => x.count) > items || value > 0),
                $"selling them fulfils her request, and there's a surprise on top (money +¥{value}, items {items} -> {Inventory.Owned().Sum(x => x.count)})");
            Check(!DailyRequest.Active, "...and there's no second request today");

            // Using a new word with a keeper can earn a little present (forced here; normally 15%, once a day each).
            var pet = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "pet");
            var state = Affinity.State("pet");
            state.surpriseDay = -1;
            ShopkeeperBrain.ForceSurprise = true;
            pet.HandlePlayerUtterance("你的猫非常漂亮。");
            pet.Interrupt();
            ShopkeeperBrain.ForceSurprise = false;
            Check(state.wordsHeard.Contains("漂亮") && state.surpriseDay == d.day, "a new word (漂亮) with Xiao Lin earned a little surprise");
            pet.HandlePlayerUtterance("今天天气很好。");
            pet.Interrupt();
            Check(state.surpriseDay == d.day && state.wordsHeard.Contains("天气"), "new words are still noted, but only one surprise a day");
            yield return null;
        }

        /// <summary>
        /// Fishing balance with a simulated human player (0.2 s reaction, a little aiming error): with no training the
        /// starter fish are easy; with every training level of a tier its commonest fish are trivial and its rarest
        /// moderately hard; and the next tier's fish are near impossible until you train further.
        /// </summary>
        private void BalanceChecks()
        {
            var rng = new System.Random(7);
            (float rate, float time) Run(FishSpecies f, int level, int n = 160)
            {
                var p = FishPower.For(f, level, level, level);
                int caught = 0;
                float t = 0f;
                for (int i = 0; i < n; i++)
                {
                    var r = ReelSim.SimulateBot(p, f.motion, rng);
                    if (r.caught) { caught++; t += r.time; }
                }
                return (caught / (float)n, caught > 0 ? t / caught : 0f);
            }
            // Calibration table: catch rate by power gap for each way of swimming (with its motion bias).
            foreach (FishMotion m in System.Enum.GetValues(typeof(FishMotion)))
            {
                var row = new System.Text.StringBuilder($"   calib {m,-8}");
                foreach (float d in new[] { -2f, -1.5f, -1.25f, -1f, -0.75f, -0.5f, 0f, 0.5f, 1f, 1.5f })
                {
                    int caught = 0;
                    float b = FishPower.MotionBias(m);
                    var p = FishPower.FromGaps(d + b, d + b, d + b);
                    for (int i = 0; i < 200; i++) if (ReelSim.SimulateBot(p, m, rng).caught) caught++;
                    row.Append($"  {d:+0.00;-0.00}:{caught / 2f,3:0}%");
                }
                Log(row.ToString());
            }
            var fish = FishDatabase.All.Where(f => f.IsFish).ToList();
            FishSpecies Commonest(int tier) => fish.Where(f => FishPower.TierOf(f) == tier).OrderBy(FishPower.RarityRank).First();
            FishSpecies Rarest(int tier) => fish.Where(f => FishPower.TierOf(f) == tier).OrderByDescending(FishPower.RarityRank).First();
            foreach (var f in fish) Log($"   {f.id,-14} tier {FishPower.TierOf(f)}  rank {FishPower.RarityRank(f):0.00}  power {FishPower.Power(f):0.00}  base ¥{FishPower.BasePrice(f):0}");

            // A beginner against every tier-0 fish (for reference), then the checks.
            Log("   beginner (no training): " + string.Join("  ", fish.Where(f => FishPower.TierOf(f) == 0).Select(f => $"{f.id} {Run(f, 0, 100).rate:P0}")));
            Log("   tier 0 trained (level 5): " + string.Join("  ", fish.Where(f => FishPower.TierOf(f) == 0).Select(f => $"{f.id} {Run(f, 5, 100).rate:P0}")));
            var starters = PlayerStats.StarterFishes.Select(id => (id, r: Run(FishDatabase.Get(id), 0))).ToList();
            Check(starters.All(s => s.r.rate >= 0.8f), $"no training: the starter fish are easy enough ({string.Join(", ", starters.Select(s => $"{s.id} {s.r.rate:P0}"))})");
            for (int tier = 0; tier <= 3; tier++)
            {
                int full = (tier + 1) * PlayerStats.LevelsPerTier;
                var c = Commonest(tier); var r = Rarest(tier);
                var common = Run(c, full); var rare = Run(r, full);
                Check(common.rate >= 0.97f, $"tier {tier} fully trained (level {full}): its commonest fish, {c.id}, is trivial ({common.rate:P0}, {common.time:0.0} s)");
                Check(rare.rate >= 0.5f && rare.rate <= 0.95f, $"tier {tier} fully trained: its rarest, {r.id}, is a fair challenge ({rare.rate:P0}, {rare.time:0.0} s)");
                if (tier < 3)
                {
                    var n = Commonest(tier + 1);
                    var next = Run(n, full);
                    Check(next.rate <= 0.1f, $"tier {tier} fully trained: the next tier's {n.id} nearly always gets away ({next.rate:P0})");
                }
            }
            // Training prices and HSK locks.
            int[] prices = Enumerable.Range(1, PlayerStats.MaxLevel).Select(l => { PlayerStats.SetLevel("luck", l - 1); return PlayerStats.NextPrice("luck"); }).ToArray();
            int luck = 0;
            PlayerStats.SetLevel("luck", luck);
            Check(prices.Take(5).SequenceEqual(new[] { 10, 20, 40, 70, 100 }) && prices[5] == 100 && prices[9] == 1000 && prices[19] == 100000,
                $"training costs 10, 20, 40, 70, 100, then x10 each tier ({string.Join(", ", prices)})");
            var lessonsBefore = SaveSystem.Data.lessonsDone.ToList();
            int reachedBefore = SaveSystem.Data.regionReached, hskBefore = SaveSystem.Data.hskLevel;
            SaveSystem.Data.regionReached = 0;
            PlayerStats.SetLevel("luck", 0);
            SetLessons(1, 0);
            bool noneYet = !PlayerStats.CanTrainNext("luck");
            SetLessons(1, 1);
            bool first = PlayerStats.CanTrainNext("luck");
            PlayerStats.SetLevel("luck", 1);
            SetLessons(1, 3);
            bool secondLocked = !PlayerStats.CanTrainNext("luck") && PlayerStats.NextNeeds("luck") == "1 more HSK 1 lesson";
            SetLessons(1, 4);
            bool secondOpen = PlayerStats.CanTrainNext("luck");
            PlayerStats.SetLevel("luck", 4);
            SetLessons(1, 10);
            bool fifthLocked = !PlayerStats.CanTrainNext("luck");
            SetLessons(1, 11);
            bool fifthOpen = PlayerStats.CanTrainNext("luck");
            // Tier 1 stays a surprise in Willow Bay, then opens with HSK 2 lessons in the desert.
            PlayerStats.SetLevel("luck", 5);
            SetLessons(1, 11, 2, 10);
            bool hiddenHome = !PlayerStats.CanTrainNext("luck") && PlayerStats.NextHidden("luck");
            SaveSystem.Data.regionReached = 1;
            SetLessons(1, 11, 2, 0);
            bool tier1Locked = !PlayerStats.CanTrainNext("luck") && !PlayerStats.NextHidden("luck");
            SetLessons(1, 11, 2, 1);
            bool tier1Open = PlayerStats.CanTrainNext("luck");
            // The last tier: the HSK 3 test, in the snow.
            PlayerStats.SetLevel("luck", 15);
            SaveSystem.Data.regionReached = Regions.Last;
            SetLessons(1, 11, 2, 10, 3, 20);
            SaveSystem.Data.hskLevel = 2;
            bool lastLocked = !PlayerStats.CanTrainNext("luck") && PlayerStats.NextNeeds("luck") == "the HSK 3 test";
            SaveSystem.Data.hskLevel = 3;
            bool lastOpen = PlayerStats.CanTrainNext("luck");
            string Thresholds(int tier) => string.Join("/", Enumerable.Range(tier * 5 + 1, 5).Select(Hsk.LessonsForUpgrade));
            Check(noneYet && first && secondLocked && secondOpen && fifthLocked && fifthOpen && hiddenHome && tier1Locked && tier1Open && lastLocked && lastOpen &&
                  Thresholds(0) == "1/4/6/9/11" && Thresholds(1) == "1/4/6/8/10" && Thresholds(2) == "1/6/11/16/20",
                $"training opens level by level with each HSK level's lessons (HSK 1: {Thresholds(0)}; HSK 2: {Thresholds(1)}; HSK 3: {Thresholds(2)}; last tier: the HSK 3 test), and later tiers wait for their region");
            SaveSystem.Data.lessonsDone.Clear();
            SaveSystem.Data.lessonsDone.AddRange(lessonsBefore);
            SaveSystem.Data.regionReached = reachedBefore;
            SaveSystem.Data.hskLevel = hskBefore;
            PlayerStats.SetLevel("luck", luck);

            // Eye training: a tier's five levels double its fish's bar, and the next tier starts where the first did.
            var mullet = FishDatabase.Get("mullet");
            var octopus = FishDatabase.Get("octopus");
            float fresh = FishPower.For(mullet, 0, 0, 0).barSize, trained = FishPower.For(mullet, 5, 0, 0).barSize;
            float nextFresh = FishPower.For(octopus, 5, 0, 0).barSize, nextTrained = FishPower.For(octopus, 10, 0, 0).barSize;
            float octopusAtStart = FishPower.For(octopus, 0, 0, 0).barSize;
            Check(trained / fresh > 1.85f && trained / fresh < 2.15f && nextTrained / nextFresh > 1.85f && nextTrained / nextFresh < 2.15f,
                $"5 eye-training levels double the bar for that tier's fish (mullet {fresh * 100f:0}% -> {trained * 100f:0}%, octopus {nextFresh * 100f:0}% -> {nextTrained * 100f:0}%)");
            // The commonest fish of tier 0 and tier 1, compared for the same swimming style.
            var firstCommon = fish.Where(f => FishPower.TierOf(f) == 0).OrderBy(FishPower.RarityRank).First();
            var nextCommon = fish.Where(f => FishPower.TierOf(f) == 1).OrderBy(FishPower.RarityRank).First();
            float gobyFresh = FishPower.FromGaps(FishPower.BarGap(firstCommon, 0), 0, 0).barSize, octopusAfterTier0 = FishPower.FromGaps(FishPower.BarGap(nextCommon, 5), 0, 0).barSize;
            Check(Mathf.Abs(octopusAfterTier0 - gobyFresh) < 0.005f && octopusAtStart < nextFresh,
                $"finishing tier 0's eye training gives the next tier's commonest fish ({nextCommon.name}) the bar a beginner has on tier 0's {firstCommon.name} ({octopusAfterTier0 * 100f:0}% vs {gobyFresh * 100f:0}%)");

            // Casting: 10 m for everyone; a perfect cast starts the meter 15% fuller.
            Check(Mathf.Approximately(PlayerStats.CastDistance, 10f), $"everyone casts up to {PlayerStats.CastDistance:0} m (strength no longer adds distance)");
            var pp = FishPower.For(mullet, 0, 0, 0);
            var plain = new ReelSim(pp, mullet.motion, new System.Random(1));
            var perfect = new ReelSim(pp, mullet.motion, new System.Random(1), FishingController.PerfectCastBonus);
            Check(Mathf.Abs(perfect.Progress - plain.Progress - 0.15f) < 0.001f && FishingController.PerfectCastPower >= 0.97f,
                $"a 97%+ cast starts the catch meter at {perfect.Progress:P0} instead of {plain.Progress:P0}");

            // Casting further out catches bigger fish: a 50% weight window slides from the bottom half to the top half.
            float far = FishingController.WindowFar;
            float Lo(float d) => FishingController.WeightWindow(d);
            var rolls = new System.Func<float, (float min, float max)>(d =>
            {
                float lo = 1f, hi = 0f;
                for (int i = 0; i < 400; i++)
                {
                    float t = Mathf.InverseLerp(mullet.minWeight, mullet.maxWeight, FishDatabase.RollWeight(mullet, 0, Lo(d)));
                    lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t);
                }
                return (lo, hi);
            });
            var near = rolls(1f); var mid = rolls((2f + far) / 2f); var top = rolls(far + 5f);
            Check(near.max <= 0.51f && top.min >= 0.49f && mid.min >= 0.24f && mid.max <= 0.76f && Mathf.Abs(Lo((2f + far) / 2f) - 0.25f) < 0.01f,
                $"cast distance sets the weight window: within 2 m {near.min:P0}-{near.max:P0}, halfway {mid.min:P0}-{mid.max:P0}, a 95% cast from the dock end ({far:0.0} m out) {top.min:P0}-{top.max:P0}");
        }

        /// <summary>Aiming a cast: A/D swing it, a ring shows where it lands, and the bobber lands there.</summary>
        private IEnumerator AimChecks(PlayerController player, CameraRig cam)
        {
            var fishing = FindFirstObjectByType<FishingController>();
            Vector2 start = WorldShape.DockShorePoint - WorldShape.DockDirection * (WorldShape.DockLength - 0.5f);
            player.Teleport(new Vector3(start.x, WorldShape.DockDeckHeight + 0.05f, start.y), 0f);
            cam.Configure(player.transform, 0f, 30f, 8f);
            yield return new WaitForSeconds(0.8f);
            fishing.SimulateHold = true;
            fishing.SimulateClick();
            yield return null;
            yield return null;
            Vector3 straight = fishing.PredictedLanding;
            fishing.AimOffset = 35f;
            yield return new WaitForSeconds(0.5f);
            var reticle = GameObject.Find("CastTarget");
            Vector3 aimed = fishing.PredictedLanding;
            Vector3 toA = aimed - player.transform.position, toS = straight - player.transform.position;
            toA.y = toS.y = 0f;
            float angle = Vector3.SignedAngle(toS, toA, Vector3.up);
            yield return Shot("29_cast_aim");
            Check(reticle != null && reticle.activeSelf && Vector3.Distance(reticle.transform.position, aimed) < 0.3f && angle > 25f && angle < 45f,
                $"aiming swings the cast {angle:0}° right, and a ring marks where it will land ({(fishing.PredictedOnWater ? "on water" : "on land")})");
            Vector3 target = fishing.PredictedLanding;
            fishing.SimulateHold = false;
            float until = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < until && fishing.State != FishingState.Waiting && fishing.State != FishingState.Idle) yield return null;
            Vector3 bob = fishing.BobberWorld;
            float off = new Vector2(bob.x - target.x, bob.z - target.z).magnitude;
            Check(fishing.State == FishingState.Waiting && off < 1f && (reticle == null || !reticle.activeSelf),
                $"the cast lands where the ring was ({off:0.00} m off) and the ring goes away");
            fishing.ForceStop();
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>投壶 pitch-pot: opens at HSK 1, aiming and power matter, a round scores hits, and a good one wins a prize.</summary>
        private IEnumerator PitchPotChecks(PlayerController player, CameraRig cam)
        {
            int hsk = SaveSystem.Data.hskLevel;
            var stall = FindObjectsByType<MinigameStall>(FindObjectsSortMode.None).FirstOrDefault(g => g.gameId == "pitchpot");
            SaveSystem.Data.hskLevel = 0;
            yield return null;
            Check(stall != null && stall.hsk == 1 && !stall.Open, "pitch-pot (投壶) is the HSK 1 game, closed before the test");
            SaveSystem.Data.hskLevel = 1;
            yield return null;
            yield return null;
            Check(stall.Open && stall.transform.Find("Built/Pot") != null && stall.transform.Find("Built").gameObject.activeSelf, "...and open after HSK 1, with its pot");
            stall.Interact();
            yield return new WaitForSeconds(0.5f);
            var game = PitchPotGame.Active;
            Check(game != null && Vector3.Distance(player.transform.position, game.ThrowSpot) < 0.3f, "F starts a round at the throwing line");
            if (game == null) { SaveSystem.Data.hskLevel = hsk; yield break; }

            // Somewhere between too short and too long the arrow drops in; off-line it misses.
            var hitsAt = Enumerable.Range(0, 201).Select(i => i / 200f).Where(p => game.Predict(p, 0f) == PitchPotGame.Outcome.In).ToList();
            float best = hitsAt.Count > 0 ? hitsAt[hitsAt.Count / 2] : 0.5f;
            float window = hitsAt.Count / 200f;
            bool offLine = game.Predict(best, 6f) != PitchPotGame.Outcome.In && game.Predict(best, -6f) != PitchPotGame.Outcome.In;
            bool shortLong = game.Predict(0f, 0f) != PitchPotGame.Outcome.In && game.Predict(1f, 0f) != PitchPotGame.Outcome.In;
            Check(hitsAt.Count > 0 && window > 0.03f && window < 0.25f && offLine && shortLong,
                $"the power that lands it is a {window * 100f:0}% window around {best:0.00}; 6° off line or full/no power misses");

            // A round: five good throws, three bad ones.
            int money = Inventory.Money, itemsBefore = SaveSystem.Data.items.Sum(i => i.count);
            SaveSystem.Data.pitchPotPrizeDay = -1;
            int bestBefore = SaveSystem.Data.pitchPotBest;
            for (int n = 0; n < PitchPotGame.Arrows; n++)
            {
                game.AimOffset = n < 5 ? 0f : 9f;
                game.Throw(n < 5 ? best : 0.2f);
                float until = Time.realtimeSinceStartup + 4f;
                while (Time.realtimeSinceStartup < until && game.Flying) yield return null;
                if (n == 2) yield return Shot("30_pitchpot");
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(0.3f);
            bool prize = Inventory.Money > money || SaveSystem.Data.items.Sum(i => i.count) > itemsBefore;
            Check(PitchPotGame.Active == null && game.Hits == 5 && game.Thrown == 8 && SaveSystem.Data.pitchPotBest == Mathf.Max(bestBefore, 5) && prize,
                $"a round of eight: {game.Hits} in the pot, best {SaveSystem.Data.pitchPotBest}, and 4+ hits wins today's prize");
            yield return Shot("31_pitchpot_done");
            SaveSystem.Data.hskLevel = hsk;
        }

        /// <summary>
        /// 海叔's crab pots: the money a maxed tier brings in, HSK-gated upgrades, collecting (the cooler's limit), fish as
        /// bait, the stall down the beach, and the games stalls (building sites until their HSK test).
        /// </summary>
        private IEnumerator CrabChecks(PlayerController player, CameraRig cam, GameUI ui)
        {
            var saved = SaveSystem.Data.crabLevels.Select(s => new StatLevel { id = s.id, level = s.level }).ToList();
            int hsk = SaveSystem.Data.hskLevel;
            void SetAll(int l) { foreach (var s in CrabPots.Stats) PlayerStats.SetLevel(s, l); }

            // Maxed tier t: a day's crabs are worth about the tier's best fish at its biggest.
            var rows = new System.Collections.Generic.List<string>();
            bool matches = true;
            for (int t = 0; t <= 3; t++)
            {
                SetAll(5 * (t + 1));
                float daily = CrabPots.Daily, target = CrabPots.TierTarget(t);
                rows.Add($"tier {t}: ¥{daily:0} a day vs ¥{target:0}");
                matches &= Mathf.Abs(daily / target - 1f) < 0.05f;
            }
            SetAll(0);
            float start = CrabPots.Daily;
            Check(matches && start > 0f && start < 5f, $"crab pots with every upgrade of a tier earn about one top fish a day ({string.Join("; ", rows)}; at the start ¥{start:0.0})");
            // Each upgrade on its own helps.
            bool each = CrabPots.Stats.Where(s => s != "crab_bait" && s != "crab_cooler").All(s =>
            {
                PlayerStats.SetLevel(s, 1);
                bool up = CrabPots.Daily > start;
                PlayerStats.SetLevel(s, 0);
                return up;
            });
            Check(each, "every income upgrade raises the daily haul");
            var crabLessons = SaveSystem.Data.lessonsDone.ToList();
            int crabReached = SaveSystem.Data.regionReached;
            PlayerStats.SetLevel("crab_pots", 5);
            SaveSystem.Data.regionReached = 1;
            SetLessons(1, 11, 2, 0);
            bool locked = !PlayerStats.CanTrainNext("crab_pots") && PlayerStats.NextNeeds("crab_pots") == "1 more HSK 2 lesson";
            Inventory.CanBuy("crab_pots", 1, out var why, out _);
            SetLessons(1, 11, 2, 1);
            bool opens = PlayerStats.CanTrainNext("crab_pots") && Catalog.PriceOf(Catalog.Get("crab_pots")) == 100;
            SaveSystem.Data.lessonsDone.Clear();
            SaveSystem.Data.lessonsDone.AddRange(crabLessons);
            SaveSystem.Data.regionReached = crabReached;
            Check(locked && why == BuyResult.Locked && opens, "crab upgrades open level by level with lessons like training (the 6th pot needs an HSK 2 lesson), priced like training");
            SetAll(5);

            // Days: one haul each morning; uncollected crabs keep only as long as the cooler allows.
            int dayBefore = DayNightCycle.Instance.Day;
            DayNightCycle.Instance.Day = Mathf.Max(10, dayBefore);
            int today = DayNightCycle.Instance.Day;
            SaveSystem.Data.crabPending = 0f;
            SaveSystem.Data.crabBaitDay = -1;
            SaveSystem.Data.crabLastDay = today - 1;
            int one = CrabPots.Pending;
            SaveSystem.Data.crabPending = 0f;
            SaveSystem.Data.crabLastDay = today - 6;
            int six = CrabPots.Pending;
            Check(Mathf.Abs(one - CrabPots.Daily) <= 1f && Mathf.Abs(six - CrabPots.Daily * CrabPots.KeepDaysNow) <= 1f && CrabPots.KeepDaysNow == 2,
                $"one day brings ¥{one}; six days away keeps only {CrabPots.KeepDaysNow} days' worth (¥{six}) with cooler level 5");

            // Talking to 海叔 pays it out; giving him fish makes tomorrow's haul bigger.
            var crabber = ShopkeeperBrain.Keepers.First(k => k.Shop.crabber);
            Vector2 cp = WorldShape.StallPosition(WorldShape.CrabberStall);
            float marketEast = Enumerable.Range(0, 9).Max(i => WorldShape.StallPosition(i).x);
            Check(cp.x > marketEast + 10f && WorldShape.ShoreDistance(cp.x, cp.y) > 2f && WorldShape.ShoreDistance(cp.x, cp.y) < 8f,
                $"海叔's stall is on the beach further along than the market ({cp.x - marketEast:0} m east, {WorldShape.ShoreDistance(cp.x, cp.y):0.0} m from the water)");
            SaveSystem.Data.crabPending = 0f;
            SaveSystem.Data.crabLastDay = today - 1;
            int money = Inventory.Money;
            yield return GoToShop(player, cam, WorldShape.CrabberStall, crabber);
            Check(Inventory.Money - money == one && CrabPots.Pending == 0, $"starting to talk to 海叔 collects the crab money (+¥{Inventory.Money - money})");
            yield return WaitIdle(crabber, 30f);
            yield return Shot("27_crabber");
            Inventory.SellAllFish();
            var goby = FishDatabase.Get("goby");
            for (int n = 0; n < 3; n++) Inventory.AddToBucket(goby, goby.maxWeight);
            crabber.HandlePlayerUtterance("海叔，我想给你鱼。");
            yield return WaitIdle(crabber);
            Check(crabber.PendingOffer != null && crabber.PendingOffer.crabBait, $"offering him fish gets a crab-bait offer ({crabber.PendingOffer?.english})");
            int bonus = crabber.PendingOffer?.price ?? 0;
            crabber.HandlePlayerUtterance("好的，放吧。");
            yield return WaitIdle(crabber);
            Check(Inventory.BucketCount == 0 && CrabPots.BaitToday >= bonus - 1 && bonus > 0, $"the fish go in the pots: +¥{CrabPots.BaitToday:0} tomorrow");
            SaveSystem.Data.crabLastDay = today;
            DayNightCycle.Instance.Day = today + 1;
            int tomorrow = CrabPots.Pending;
            DayNightCycle.Instance.Day = today;
            Check(Mathf.Abs(tomorrow - (CrabPots.Daily + bonus)) <= 1.5f, $"...and the next morning's haul includes it (¥{tomorrow} = ¥{CrabPots.Daily:0} + ¥{bonus})");
            SaveSystem.Data.crabPending = 0f;
            SaveSystem.Data.crabLastDay = today;
            ShopConversation.Instance.End(sayGoodbye: false);

            // The games stalls: building sites until their test.
            var games = FindObjectsByType<MinigameStall>(FindObjectsSortMode.None).OrderBy(g => g.hsk).ToList();
            SaveSystem.Data.hskLevel = 0;
            yield return null;
            yield return null;
            bool sites = games.Count == 3 && games.All(g => !g.Open && g.transform.Find("Construction").gameObject.activeSelf && !g.transform.Find("Built").gameObject.activeSelf);
            var g0 = games.FirstOrDefault();
            if (g0 != null)
            {
                Vector3 gp = g0.transform.position + g0.transform.forward * 7f;
                player.Teleport(new Vector3(gp.x, WorldShape.TerrainHeight(gp.x, gp.z) + 0.05f, gp.z), g0.transform.eulerAngles.y + 180f);
                cam.Configure(player.transform, g0.transform.eulerAngles.y + 160f, 14f, 9f);
                yield return new WaitForSeconds(1f);
                yield return Shot("28a_games_construction");
            }
            SaveSystem.Data.hskLevel = 2;
            yield return null;
            yield return null;
            bool mixed = games.Count == 3 && games[0].Open && games[1].Open && !games[2].Open && games[0].transform.Find("Built").gameObject.activeSelf;
            yield return new WaitForSeconds(0.5f);
            yield return Shot("28b_games_hsk2");
            Check(sites && mixed, $"the games stalls ({string.Join(", ", games.Select(g => g.hanzi + " HSK " + g.hsk))}) are building sites until their HSK test, then open");

            SaveSystem.Data.hskLevel = hsk;
            SaveSystem.Data.crabLevels.Clear();
            SaveSystem.Data.crabLevels.AddRange(saved);
            DayNightCycle.Instance.Day = dayBefore;
            SaveSystem.Data.crabLastDay = dayBefore;
        }

        /// <summary>The HUD's fish grid, furniture comfort counting each kind once, and newest-first transcripts.</summary>
        private IEnumerator HudChecks(GameUI ui)
        {
            Inventory.SellAllFish();
            foreach (var id in new[] { "goby", "goby", "sardine", "mullet" }) Inventory.AddToBucket(FishDatabase.Get(id), FishDatabase.Get(id).maxWeight * 0.8f);
            yield return new WaitForSeconds(0.3f);
            Check(ui.FishGridCells == Inventory.SlotCapacity && ui.FishGridFilled == Inventory.SlotsUsed && ui.FishGridText == $"{Inventory.SlotsUsed}/{Inventory.SlotCapacity}",
                $"the HUD shows the bag as a grid: {ui.FishGridFilled} of {ui.FishGridCells} slots with fish, labelled {ui.FishGridText}");
            yield return Shot("26_hud_fish_grid");
            Inventory.SellAllFish();

            var fishing = FindFirstObjectByType<FishingController>();
            if (fishing != null && fishing.State == FishingState.Idle)
            {
                ui.ShowCatchCard(CatchJournal.Record(FishDatabase.Get("goby"), 0.05f, Vector3.zero));
                yield return new WaitForSeconds(0.5f);
                bool shown = ui.CatchCardVisible;
                fishing.SimulateHold = true;
                fishing.SimulateClick();
                yield return null;
                yield return null;
                bool charging = fishing.State == FishingState.Charging;
                Check(shown && charging && !ui.CatchCardVisible, $"starting a cast hides the last catch's card at once (shown {shown}, charging {charging}, still visible {ui.CatchCardVisible})");
                fishing.SimulateHold = false;
                float wait = Time.realtimeSinceStartup + 6f;
                while (Time.realtimeSinceStartup < wait && fishing.State != FishingState.Idle && fishing.State != FishingState.Waiting) yield return null;
                fishing.ForceStop();
                Inventory.SellAllFish();
            }
            else Check(false, $"the fishing controller is idle for the cast test ({fishing?.State})");

            var frenzy = FishFrenzy.Instance;
            float frenzyOut = frenzy != null ? -WorldShape.ShoreDistance(frenzy.Position.x, frenzy.Position.z) : -1f;
            Check(frenzy != null && FishFrenzy.Contains(frenzy.Position) && WorldShape.IsWater(frenzy.Position.x, frenzy.Position.z) && frenzyOut > 3f && frenzyOut < 14f,
                $"a fish frenzy bubbles in open water within casting reach ({frenzyOut:0.0} m out)");

            var placed = SaveSystem.Data.placed;
            var before = placed.ToList();
            placed.Clear();
            placed.Add(new PlacedItem { id = "chair" });
            int one = Energy.ComfortPoints;
            placed.Add(new PlacedItem { id = "chair" });
            int two = Energy.ComfortPoints;
            placed.Add(new PlacedItem { id = "table" });
            int mixed = Energy.ComfortPoints;
            placed.Clear();
            placed.AddRange(before);
            Check(one > 0 && two == one && mixed > two, $"only one of each kind of furniture adds comfort (chair {one}, two chairs {two}, chair + table {mixed})");
        }

        /// <summary>The starter fish, how lively hooked fish are, and the new body shapes.</summary>
        private void StarterAndReelChecks()
        {
            var catchable = new CatchContext { hour = 9f, shoreDistance = 4f, lineKg = Catalog.StarterLineKg, bait = null };
            var known = FishDatabase.Available(catchable).Select(f => f.id).ToList();
            Check(known.Count(id => PlayerStats.StarterFishes.Contains(id)) >= 3,
                $"at the start, several fish bite from the dock with the plain line and hook ({string.Join(", ", known)})");
            float easy = FishPower.For(FishDatabase.Get("goby"), 0, 0, 0).moveRate;
            float hard = FishPower.For(FishDatabase.Get("tuna"), 0, 0, 0).moveRate;
            float calmed = FishPower.For(FishDatabase.Get("tuna"), 0, 0, PlayerStats.MaxLevel).moveRate;
            Check(hard > easy * 2f && calmed < hard * 0.5f, $"stronger fish thrash more (goby x{easy:0.00}, tuna x{hard:0.00}); full strength training calms the tuna to x{calmed:0.00}");
            var hairtail = CatchVisuals.Spawn(FishDatabase.Get("hairtail"), 1f);
            var puffer = CatchVisuals.Spawn(FishDatabase.Get("pufferfish"), 1f);
            Bounds B(GameObject g) { var rs = g.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b; }
            var hb = B(hairtail); var pb = B(puffer);
            float hairRatio = Mathf.Max(hb.size.x, hb.size.z) / hb.size.y, pufRatio = Mathf.Max(pb.size.x, pb.size.z) / pb.size.y;
            Check(hairRatio > pufRatio * 2f, $"fish have their own shapes (hairtail length/height {hairRatio:0.0}, pufferfish {pufRatio:0.0})");
            Destroy(hairtail);
            Destroy(puffer);
        }

        private IEnumerator CatchCardShot(GameUI ui)
        {
            var r = CatchJournal.Record(FishDatabase.Get("sailfish"), 42f, Vector3.zero);
            ui.ShowCatchCard(r);
            yield return new WaitForSeconds(1.2f);
            yield return Shot("25_catch_card");
            Inventory.SellAllFish();
        }

        /// <summary>The sushi chef's scale: stages, the multiplier, a real sale, and the animation.</summary>
        private IEnumerator SaleChecks(PlayerController player, CameraRig cam, GameUI ui)
        {
            var goby = FishDatabase.Get("goby");
            var bass = FishDatabase.Get("seabass");
            float Kg(FishSpecies s, float size) => Mathf.Lerp(s.minWeight, s.maxWeight, size);
            float common80 = FishSale.Points(goby, Kg(goby, 0.8f)), uncommon80 = FishSale.Points(bass, Kg(bass, 0.8f));
            Check(FishSale.Fills(common80 * 3f) == 0 && FishSale.Fills(common80 * 4f) == 1 && FishSale.Fills(common80 * 4f + uncommon80 * 7.9f) == 1 &&
                  FishSale.Fills(common80 * 4f + uncommon80 * 8f) == 2,
                $"the first bar takes 4 common fish at 80% size, the second 8 uncommon ones ({FishSale.BarCapacity(0):0.0} and {FishSale.BarCapacity(1):0.0} points)");
            float biggestCommon = FishSale.Points(goby, goby.maxWeight), averageUncommon = FishSale.Points(bass, Kg(bass, 0.5f));
            Check(Mathf.Abs(biggestCommon - averageUncommon) < 0.01f, $"the biggest common fish counts like an average uncommon one ({biggestCommon:0.00} vs {averageUncommon:0.00} points)");
            Check(FishSale.BarRecipe(2) == (16, Rarity.Rare) && Mathf.Abs(FishSale.BarCapacity(2) - 16f * FishSale.Points(FishDatabase.Get("conger"), Kg(FishDatabase.Get("conger"), 0.8f))) < 0.01f,
                "the third bar takes 16 rare fish at 80%");
            FishSpecies Commonest(int t) => FishDatabase.All.Where(f => f.IsFish && FishPower.TierOf(f) == t).OrderBy(FishPower.RarityRank).First();
            var prices = Enumerable.Range(0, 4).Select(t => (f: Commonest(t), price: Catalog.FishPrice(Commonest(t), Kg(Commonest(t), 0.5f)))).ToList();
            Check(Enumerable.Range(1, 3).All(t => prices[t].price >= prices[t - 1].price * 8),
                $"each fish tier sells for about ten times the last ({string.Join(", ", prices.Select(x => $"{x.f.name} ¥{x.price}"))})");
            int hsk = SaveSystem.Data.hskLevel;
            var done = SaveSystem.Data.lessonsDone.ToList();
            SaveSystem.Data.hskLevel = 0;
            SaveSystem.Data.lessonsDone.Clear();
            Check(Mathf.Abs(FishSale.PerFill - 0.10f) < 0.001f, $"a new player gets +10% per bar ({FishSale.PerFill:P0})");
            SaveSystem.Data.hskLevel = 1;
            SaveSystem.Data.lessonsDone.AddRange(new[] { "1-1", "1-2", "1-3" });
            Check(Mathf.Abs(FishSale.PerFill - 0.31f) < 0.001f, $"3 lessons (+2% each) and HSK 1 (+15%) make it +{FishSale.PerFill * 100f:0}% per bar");

            // Sell 12 sea bass at 80% size: 23.4 points fill two bars (5.2 + 15.6) at +31% each = x1.62.
            Inventory.SellAllFish();
            for (int n = 0; n < 12; n++) Inventory.AddToBucket(bass, Kg(bass, 0.8f));
            var quote = FishSale.For(FishSale.Selection("all"), Affinity.Level("fish"));
            Check(quote.fills == 2 && Mathf.Abs(quote.multiplier - 1.62f) < 0.01f && quote.total > quote.baseValue,
                $"12 good sea bass fill 2 bars: x{quote.multiplier:0.00}, ¥{quote.baseValue} -> ¥{quote.total}");
            var chen = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "fish");
            Check(chen.Shop.hanzi == "寿司店", $"Auntie Chen runs a sushi bar ({chen.Shop.hanzi} {chen.Shop.english})");
            int i = chen.Shop.stall;
            Vector2 spot = WorldShape.CustomerSpot(i), stall = WorldShape.StallPosition(i);
            Vector2 dir = (stall - spot).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            player.Teleport(new Vector3(spot.x, WorldShape.TerrainHeight(spot.x, spot.y) + 0.05f, spot.y), yaw);
            cam.Configure(player.transform, yaw, 12f, 5f);
            yield return new WaitForSeconds(1f);
            int money = Inventory.Money;
            chen.HandlePlayerUtterance("阿姨，我想卖鱼。");
            Check(chen.PendingOffer != null && chen.PendingOffer.price == quote.total, $"she offers the scale price (¥{chen.PendingOffer?.price})");
            chen.HandlePlayerUtterance("可以。");
            chen.Interrupt();
            Check(Inventory.Money - money == quote.total && Inventory.BucketCount == 0, $"the sale pays exactly that (+¥{Inventory.Money - money})");
            Check(ui.Scale != null && ui.Scale.Playing, "the scale animation plays");
            // The bar fills gradually with a tone that rises as it goes (and starts again a little higher each bar).
            float until = Time.realtimeSinceStartup + 20f, firstFillAt = -1f, started = Time.realtimeSinceStartup;
            int midFrames = 0, rising = 0, falling = 0;
            float lastPitch = -1f;
            int lastFills = 0;
            bool shot = false, toneHeard = false;
            while (Time.realtimeSinceStartup < until && ui.Scale.Playing && ui.Scale.FillsShown < quote.fills)
            {
                float f = ui.Scale.BarShown;
                if (f > 0.2f && f < 0.8f) midFrames++;
                if (ui.Scale.ToneOn)
                {
                    toneHeard = true;
                    if (lastPitch > 0f && ui.Scale.FillsShown == lastFills)
                    {
                        if (ui.Scale.TonePitch > lastPitch + 0.0001f) rising++;
                        else if (ui.Scale.TonePitch < lastPitch - 0.0001f) falling++;
                    }
                    lastPitch = ui.Scale.TonePitch;
                }
                lastFills = ui.Scale.FillsShown;
                if (firstFillAt < 0f && ui.Scale.FillsShown >= 1) firstFillAt = Time.realtimeSinceStartup - started;
                if (!shot && f > 0.5f) { shot = true; yield return Shot("24a_scale_filling"); }
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
            yield return Shot("24b_scale_done");
            Check(ui.Scale.FillsShown == quote.fills, $"the animation fills the bar {ui.Scale.FillsShown} times, like the price");
            Check(firstFillAt >= 0.8f && midFrames >= 20, $"the bar fills gradually (first bar after {firstFillAt:0.0} s, {midFrames} frames part-full)");
            Check(toneHeard && rising > 10 && falling == 0, $"a tone plays while it fills, rising in pitch within each bar ({rising} rising steps, {falling} falling)");
            SaveSystem.Data.hskLevel = hsk;
            SaveSystem.Data.lessonsDone.Clear();
            SaveSystem.Data.lessonsDone.AddRange(done);
        }

        /// <summary>Medals by weight, golden fish (worth 5x), and the trophy wall at home.</summary>
        private IEnumerator TrophyChecks(PlayerController player, CameraRig cam)
        {
            var bass = FishDatabase.Get("seabass");
            float lo = bass.minWeight, hi = bass.maxWeight;
            Check(CatchJournal.MedalFor(bass, lo) == 1 && CatchJournal.MedalFor(bass, Mathf.Lerp(lo, hi, 0.65f)) == 2 && CatchJournal.MedalFor(bass, Mathf.Lerp(lo, hi, 0.95f)) == 3,
                "medals by weight: bronze for any sea bass, silver from 60% of its range, gold from 90%");
            Inventory.SellAllFish();
            var small = CatchJournal.Record(bass, lo + 0.1f, Vector3.zero);
            var big = CatchJournal.Record(bass, Mathf.Lerp(lo, hi, 0.95f), Vector3.zero);
            var again = CatchJournal.Record(bass, Mathf.Lerp(lo, hi, 0.92f), Vector3.zero);
            Check(small.newMedal == 1 && big.newMedal == 3 && again.newMedal == 0 && CatchJournal.Medal("seabass") == 3,
                $"catches earn medals once: bronze, then gold, then nothing new ({small.newMedal}, {big.newMedal}, {again.newMedal})");
            Inventory.SellAllFish();
            var mack = FishDatabase.Get("mackerel");
            CatchJournal.Record(mack, 1f, Vector3.zero);
            int plain = Inventory.BucketValue;
            Inventory.SellAllFish();
            FishingController.ForceGolden = true;
            var gold = CatchJournal.Record(mack, 1f, Vector3.zero, golden: FishingController.ForceGolden);
            FishingController.ForceGolden = false;
            Check(gold.golden && Inventory.BucketValue == plain * CatchJournal.GoldenValue && CatchJournal.Get("mackerel").goldenCount == 1,
                $"a golden mackerel is worth {CatchJournal.GoldenValue}x (¥{plain} -> ¥{Inventory.BucketValue}) and is counted in the journal");
            Check(FishingController.GoldenChance >= 0.03f && FishingController.GoldenChance < 0.1f, $"golden fish are rare ({FishingController.GoldenChance * 100f:0}% a catch)");
            Inventory.SellAllFish();
            var wall = TrophyWall.Instance;
            Check(wall != null, "the house has a trophy wall");
            if (wall != null)
            {
                wall.Rebuild();
                var trophies = TrophyWall.Trophies();
                Check(trophies.Any(t => t.species.id == "seabass" && !t.golden) && trophies.Any(t => t.species.id == "mackerel" && t.golden) && wall.MountCount == trophies.Count,
                    $"the wall shows the gold-medal sea bass and the golden mackerel ({wall.MountCount} mounted)");
                var bed = BedInteractable.Instance;
                if (bed != null)
                {
                    player.Teleport(bed.WakeSpot, 0f);
                    Vector3 to = wall.transform.position - player.transform.position;
                    float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    cam.Configure(player.transform, yaw, 20f, 4f);
                    yield return new WaitForSeconds(5f); // let the title card fade
                    yield return Shot("21_trophy_wall");
                }
            }
        }

        /// <summary>Bait is a bonus (not a requirement), except for the oarfish.</summary>
        private void FishUnitChecks()
        {
            var discovered = SaveSystem.Data.discoveredFish;
            var saved = discovered.ToList();
            foreach (var f in FishDatabase.All) if (!discovered.Contains(f.id)) discovered.Add(f.id);
            var seabass = FishDatabase.Get("seabass");
            var squid = Catalog.Get("bait_squid");          // the sea bass doesn't like squid
            var shrimp = Catalog.Get("bait_shrimp");        // ...but loves shrimp
            var wrong = new CatchContext { hour = 9f, shoreDistance = 30f, lineKg = 80f, bait = squid };
            var liked = new CatchContext { hour = 9f, shoreDistance = 30f, lineKg = 80f, bait = shrimp };
            Check(!seabass.baits.Contains("bait_squid") && FishDatabase.Missing(seabass, wrong) == null, "a fish bites on a bait it doesn't especially like (sea bass on squid)");
            // Two evening fish that like different baits: the sea bass (shrimp) and the hairtail (squid).
            discovered.Clear();
            discovered.Add("seabass");
            discovered.Add("hairtail");
            wrong.hour = liked.hour = 21f;
            var rng = new System.Random(7);
            int onWrong = 0, onLiked = 0;
            for (int i = 0; i < 4000; i++)
            {
                if (FishDatabase.Roll(wrong, rng)?.id == "seabass") onWrong++;
                if (FishDatabase.Roll(liked, rng)?.id == "seabass") onLiked++;
            }
            Check(onLiked > onWrong * 2, $"...but its favourite bait makes it far more likely (sea bass vs hairtail, 4000 rolls each: {onLiked} sea bass on shrimp, {onWrong} on squid)");
            foreach (var f in FishDatabase.All) if (!discovered.Contains(f.id)) discovered.Add(f.id);
            var oar = FishDatabase.Get("oarfish");
            var night = new CatchContext { hour = 23f, shoreDistance = 80f, lineKg = 80f, bait = squid };
            var glow = new CatchContext { hour = 23f, shoreDistance = 80f, lineKg = 80f, bait = Catalog.Get("bait_glow") };
            int here = SaveSystem.Data.location;
            SaveSystem.Data.location = Regions.HomeOfTier(FishPower.TierOf(oar)); // where the oarfish lives
            Check(FishDatabase.Missing(oar, night) != null && FishDatabase.Missing(oar, glow) == null, "the oarfish still only bites on the glow lure");
            SaveSystem.Data.location = here;
            discovered.Clear();
            discovered.AddRange(saved);
        }

        /// <summary>Furniture left over the water or on the old pier (placed before the coast changed) moves into the house.</summary>
        private IEnumerator StrandedItemChecks(PlayerController player, CameraRig cam)
        {
            var home = HomeItems.Instance;
            // Where the player's plant really ended up: deck height, next to where the old pier used to be.
            var plant = new PlacedItem { id = "plant", x = 0.96f, y = 0.55f, z = -16.06f, yaw = 0f };
            var fine = new PlacedItem { id = "bench", x = WorldShape.CampCenter.x + 3f, z = WorldShape.CampCenter.y - 2f };
            fine.y = WorldShape.TerrainHeight(fine.x, fine.z);
            SaveSystem.Data.placed.Add(plant);
            SaveSystem.Data.placed.Add(fine);
            int moved = home.RescueStranded();
            var at = new Vector3(plant.x, plant.y, plant.z);
            Check(moved == 1 && Cabin.IsInside(at) && Mathf.Abs(plant.y - Cabin.GroundHeight(at)) < 0.05f,
                $"a plant stranded off the pier moved into the house, onto the floor ({moved} moved, now at {at})");
            Check(Mathf.Abs(fine.x - (WorldShape.CampCenter.x + 3f)) < 0.01f, "furniture standing on dry land stays where it is");
            // Moving furniture: F on it (or Move in the bag) picks it up; it goes down where you click.
            home.Move(fine, new Vector3(fine.x, fine.y, fine.z), 0f); // spawns it where it is
            var benchObj = home.ObjectFor(fine);
            Check(benchObj != null && benchObj.GetComponent<FurnitureInteractable>() != null, "placed furniture can be picked up with F");
            Vector3 to = new Vector3(fine.x + 2f, 0f, fine.z + 1f);
            to.y = WorldShape.TerrainHeight(to.x, to.z);
            home.StartMoving(fine);
            Check(PlacementController.Active && (benchObj == null || !benchObj.gameObject.activeSelf), "moving hides the bench and shows it following the mouse");
            PlacementController.Instance.Cancel();
            Check(benchObj != null && benchObj.gameObject.activeSelf && Mathf.Abs(fine.x - (WorldShape.CampCenter.x + 3f)) < 0.01f, "Esc leaves it where it was");
            home.Move(fine, to, 90f);
            var moved2 = home.ObjectFor(fine);
            Check(Mathf.Abs(fine.x - to.x) < 0.01f && fine.yaw == 90f && moved2 != null && Vector3.Distance(moved2.transform.position, to) < 0.05f, "...and clicking puts it down in the new spot (saved)");
            // Sleep any time, and a cast costs 10 energy.
            DayNightCycle.Instance.TimeOfDay = 12f;
            Check(SleepSystem.SleepyTime && BedInteractable.Instance != null && BedInteractable.Instance.Prompt.StartsWith("[F] Sleep"), "you can go to bed at noon");
            float gobyBar = FishPower.For(FishDatabase.Get("goby"), 0, 0, 0).barSize;
            Check(Mathf.Approximately(Energy.CastCost, 10f) && gobyBar < 0.3f, $"a cast costs {Energy.CastCost:0} energy; a new player's green bar for a goby is {gobyBar * 100f:0}% of the track");
            var bed = BedInteractable.Instance;
            if (bed != null)
            {
                player.Teleport(bed.WakeSpot, 0f);
                cam.Configure(player.transform, 180f, 55f, 6f);
                yield return new WaitForSeconds(1.5f);
                yield return Shot("19_plant_in_house");
            }
        }

        private void Log(string msg)
        {
            string line = $"[{Time.realtimeSinceStartup - _t0,6:0.0}s] {msg}";
            _log.AppendLine(line);
            Debug.Log("[SelfTest] " + msg);
            File.WriteAllText(Path.Combine(_outDir, "report.txt"), _log.ToString());
        }

        private void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Log((ok ? "PASS " : "FAIL ") + what);
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, name + ".png"));
            yield return null;
            Log($"screenshot {name}.png");
        }

        private SpeechEngine Speech => LocalAIServices.Instance.Speech;

        /// <summary>The rule-based shop parser on typical (and mis-heard) customer lines.</summary>
        private void ParserChecks()
        {
            var tackle = Catalog.Shop("tackle");
            var fish = Catalog.Shop("fish");
            var pet = Catalog.Shop("pet");
            string P(ShopDef shop, string line, bool pending = false) => ShopIntentParser.Parse(shop, line, pending)?.ToString() ?? "null";
            void Expect(ShopDef shop, string line, string expected, bool pending = false)
            {
                string got = P(shop, line, pending);
                Check(got.StartsWith(expected), $"parser: \"{line}\" -> {got}");
            }
            Expect(tackle, "老板，我想买红线", "buy item=line_red qty=1");
            Expect(tackle, "我要鱿鱼", "buy item=bait_squid qty=1");
            Expect(tackle, "我要两包蚯蚓", "buy item=bait_worm qty=2");
            Expect(tackle, "蓝线多少钱", "ask_price item=line_blue");
            Expect(tackle, "好的咯", "confirm", pending: true);
            Expect(tackle, "不要了，谢谢", "decline", pending: true);
            Expect(fish, "大爷我想卖鱼", "sell_fish");
            Expect(fish, "阿姨，我想卖三条鲈鱼", "sell_fish item= qty=1 fish=seabass");
            Expect(pet, "给我十二包猫粮", "buy item=cat_food qty=12");
            Expect(pet, "我想看一下猫碗", "buy item=cat_bowl qty=1");
            // Bugs found in a real session's chat log (2026-09-29):
            Expect(pet, "你卖什么", "browse");
            Expect(pet, "这里有什么", "browse");
            Expect(pet, "还有什么", "browse");
            Expect(fish, "我想卖蓝晒油", "sell_fish");
            Expect(fish, "谢谢", "thanks");
            Expect(fish, "谢谢再见", "goodbye");
            Check(Pinyin.ToSimplified("我想买什么") == "我想买什么" && Pinyin.Of("什么") == "shénme", $"什么 stays 什么 ({Pinyin.ToSimplified("什么")}, {Pinyin.Of("什么")})");
            Check(Pinyin.ToSimplified("這裡有什麼") == "这里有什么", $"traditional still converts ({Pinyin.ToSimplified("這裡有什麼")})");
            Check(PhraseMatcher.SoundsLikeFeedingTheCat("喂汤圆") && PhraseMatcher.SoundsLikeFeedingTheCat("未天员") && PhraseMatcher.SoundsLikeFeedingTheCat("喂猫")
                  && !PhraseMatcher.SoundsLikeFeedingTheCat("为什么"), "喂汤圆 is recognised (also as 未天员), 为什么 isn't");
            Check(PhraseMatcher.Similarity(PhraseMatcher.Syllables("未天员"), PhraseMatcher.Syllables("喂汤圆")) >= 0.6f, "未天员 sounds like 喂汤圆");
            Check(PhraseMatcher.SoundsLikeFeedingTheCat("煨汤人"), "煨汤人 (heard in a real test) counts as 喂汤圆");
            string lone = $"{Pinyin.Of("汤")} {Pinyin.Of("上")} {Pinyin.Of("台")} {Pinyin.Of("与")} {Pinyin.Of("那")} {Pinyin.Of("乐")}";
            Check(lone == "tāng shàng tái yǔ nà lè", $"single characters use their common reading ({lone})");
            Check(Pinyin.Of("我喝汤") == "wǒ hē tāng", $"我喝汤 -> {Pinyin.Of("我喝汤")}");
            Expect(tackle, "再见", "goodbye");
            string worms = Catalog.Counted(Catalog.Get("bait_worm"), 2), line = Catalog.Counted(Catalog.Get("line_red"), 1);
            Check(worms == "两包蚯蚓" && line == "一卷红线", $"counting words: {worms}, {line}");
            HskUnitChecks();
            FriendshipUnitChecks();
            // Mei's context is 4096 tokens: her standing prompt must leave room for the conversation (details come per turn).
            int meiPrompt = DialogueAgent.EstimateTokens(CompanionPersona.BuildSystemPrompt(null));
            Check(meiPrompt < 2000, $"Mei's system prompt stays small (~{meiPrompt} tokens of a 4096 context)");
            string note = Encyclopedia.NoteFor("Mei, how do I ask Old Wang for worms in Chinese?");
            Check(note.Contains("蚯蚓") && note.Contains("块"), "asking about Old Wang brings in his goods and prices for that turn");
            Check(Encyclopedia.NoteFor("Ay what does old wine llike").Contains("LIKES"), "a misheard 'llike' still counts as asking what someone likes");
            Affinity.State("tackle").facts.RemoveAll(f => f.StartsWith("like:") || f.StartsWith("dislike:")); // that lookup records them; undo it
            VocabNotebook.ObserveTutorLine("You can say 我要蚯蚓 (I want worms) or 老板，有蚯蚓吗？ [Boss, do you have worms?]");
            bool round = VocabNotebook.Entries.Any(v => v.hanzi == "我要蚯蚓"), square = VocabNotebook.Entries.Any(v => v.hanzi == "老板有蚯蚓吗");
            Check(round && square, "the notebook picks up words Mei glosses with [square] or (round) brackets");
            var gymShop = Catalog.Shop("gym");
            var misheard = ShopIntentParser.Parse(gymShop, "张面我想买力尿训练", false);
            var vague = ShopIntentParser.Parse(gymShop, "我想买训练", false);
            Check(misheard?.item == "up_cast" && (vague == null || vague.item != "up_cast"),
                $"one misheard syllable in a long item name still finds it (力尿训练 -> {misheard?.item}), but a vague 训练 doesn't pick one");
            SaveSystem.Data.vocab.RemoveAll(v => v.hanzi == "你好" || v.hanzi == "天气");
            VocabNotebook.ObserveTutorLine("He might smile if you greet him first with 你好, and talk about 今天的天气.");
            bool unglossed = VocabNotebook.Entries.Any(v => v.hanzi == "你好" && v.meaning.Length > 0) && VocabNotebook.Entries.Any(v => v.hanzi == "天气");
            VocabNotebook.ObserveTutorLine("你好！今天天气很好。");
            Check(unglossed, "...and HSK words she uses without a gloss in an English line (你好, 天气), with the HSK meaning");
            SaveSystem.Data.vocab.RemoveAll(v => v.hanzi == "我要蚯蚓" || v.hanzi == "老板有蚯蚓吗");
        }

        /// <summary>Friendship levels: the facts and the gift for each level, and the HSK cap (tested on 小方's state, then reset).</summary>
        private void FriendshipUnitChecks()
        {
            string Q(string line) => ShopIntentParser.FactQuestion(line);
            Check(Q("你是哪里人？") == "hometown" && Q("你有哥哥姐姐吗？") == "siblings" && Q("你的爱好是什么？") == "hobby" && Q("你喜欢吃什么？") == "food" &&
                  Q("你结婚了吗？") == "family" && Q("你的生日是几月几号？") == "birthday" && Q("你以后想做什么？") == "dream" &&
                  Q("你喜欢什么？") == "like" && Q("你喜欢做什么？") == "hobby" && Q("我想买鱼竿") == null,
                "the seven questions about a keeper (and 你喜欢什么) are recognised");
            Check(KeeperProfiles.All.All(p => Affinity.FactQuestions.Keys.All(k => p.Fact(k) != null)), $"all {KeeperProfiles.All.Count} keepers have an answer to every question");

            var s = Affinity.State("colours");
            int hsk = SaveSystem.Data.hskLevel;
            s.facts.Clear();
            s.giftLevels.Clear();
            s.lastGiftDay = -1;
            SaveSystem.Data.hskLevel = 0;
            Affinity.Evaluate("colours");
            Affinity.Learn("colours", "hometown", "self-test");
            bool factOnly = Affinity.Level("colours") == 0;
            Affinity.RecordGift("colours", disliked: true);
            bool dislikedNoCount = Affinity.Level("colours") == 0 && !s.giftLevels.Contains(1);
            s.lastGiftDay = -1;
            Affinity.RecordGift("colours", disliked: false);
            Check(factOnly && dislikedNoCount && Affinity.Level("colours") == 1, "认识 needs his hometown AND a gift (a gift he dislikes doesn't count)");
            Affinity.Learn("colours", "siblings", "self-test");
            Affinity.Learn("colours", "hobby", "self-test");
            Affinity.RecordGift("colours", disliked: false);
            Check(Affinity.Level("colours") == 1 && Affinity.WaitingForHsk("colours"), "朋友: siblings, hobby and a gift done, but it waits for HSK 1");
            SaveSystem.Data.hskLevel = 1;
            Affinity.EvaluateAll();
            Check(Affinity.Level("colours") == 2, "...and passing HSK 1 makes it 朋友");
            Affinity.Learn("colours", "food", "self-test");
            Affinity.Learn("colours", "family", "self-test");
            Affinity.RecordGift("colours", disliked: false);
            Check(Affinity.Level("colours") == 3 && Affinity.HskNeeded("colours", 4) == 1 && Affinity.HskNeeded("colours_desert", 3) == 2 &&
                  Affinity.HskNeeded("colours_snow", 4) == 3 && Affinity.HskNeeded("colours_mars", 4) == 3,
                "in Willow Bay the closest friendship only needs Willow Bay's test (HSK 1); the desert's needs HSK 2, Snow Bay's and Mars's HSK 3");
            s.facts.Clear();
            s.giftLevels.Clear();
            s.lastGiftDay = -1;
            SaveSystem.Data.hskLevel = hsk;
            Affinity.Evaluate("colours");
        }

        /// <summary>The HSK word list, the lessons covering it, and how answers are graded.</summary>
        private void HskUnitChecks()
        {
            int[] counts = { 0, HskVocab.Words(1).Count, HskVocab.Words(2).Count, HskVocab.Words(3).Count };
            Check(counts[1] == 150 && counts[2] >= 145 && counts[3] >= 295, $"HSK word list: {counts[1]} / {counts[2]} / {counts[3]} words");
            Check(HskVocab.All.All(w => w.pinyin.Length > 0 && w.meaning.Length > 0), "every HSK word has pinyin and an English meaning");
            Check(HskVocab.Get("都").pinyin == "dōu" && HskVocab.Get("还").pinyin == "hái" && HskVocab.Get("看").meaning.StartsWith("to look"),
                $"common readings and meanings (都 {HskVocab.Get("都").pinyin}, 还 {HskVocab.Get("还").pinyin}, 看 = {HskVocab.Get("看").meaning})");
            for (int l = 1; l <= Hsk.MaxLevel; l++)
            {
                var covered = Enumerable.Range(1, Hsk.LessonCounts[l]).SelectMany(n => Hsk.LessonWords(l, n)).Select(w => w.hanzi).ToList();
                bool whole = covered.Count == counts[l] && covered.Distinct().Count() == counts[l];
                int smallest = Enumerable.Range(1, Hsk.LessonCounts[l]).Min(n => Hsk.LessonWords(l, n).Count);
                Check(Hsk.LessonCounts[l] >= 10 && whole, $"HSK {l}: {Hsk.LessonCounts[l]} lessons cover all {counts[l]} words once (smallest lesson {smallest} words)");
            }
            bool G(string heard, string word, bool recall = true) => HskSchool.Grade(HskSchool.Clean(heard), HskVocab.Get(word), recall);
            Check(G("卖", "卖") && G("迈", "卖") && G("买", "卖") && G("和", "喝") && G("才", "菜") && G("环", "还"),
                "grading ignores tones: 迈/买 count for 卖, 和 for 喝, 才 for 菜, and any reading counts (环 huán for 还, which can be huán)");
            Check(!G("十", "书") && !G("睡", "岁") && !G("错", "坐") && !G("我们去买东西吧", "卖", false),
                "grading still needs the right sounds (十 isn't 书, 睡 isn't 岁), and a lone sound inside a long line doesn't count");
            Check(G("苹果。", "苹果") && G("平果", "苹果") && G("我想吃苹果", "苹果") && !G("我想吃", "苹果"), "grading: 苹果, misheard 平果, and in a sentence");
            Check(G("8", "八") && G("100", "百") && G("2个", "两") && G("十", "十"), $"grading: digits from speech recognition count (8 -> {HskSchool.Clean("8")}, 100 -> {HskSchool.Clean("100")})");
            Check(!G("", "八") && !G("hello", "你好"), "grading: silence and English don't count");
            if (Hsk.Level == 0)
                Check(UiText.Plain("Day 4", "第4天", 0) == "Day 4" && UiText.Plain("Bag", "鱼", 1) == "Bag", "before any HSK test the interface is all English (no half-Chinese labels)");
        }

        /// <summary>Synthesises a line with one of the game voices and "says" it into the mic pipeline.</summary>
        private IEnumerator Say(VoiceChatController vc, DialogueAgent target, string voice, string text)
        {
            WavUtility.PcmData? pcm = null;
            bool done = false;
            Speech.Synthesize(voice, text, 1f, null, p => { pcm = p; done = true; });
            while (!done) yield return null;
            if (!pcm.HasValue)
            {
                Log($"(could not synthesise \"{text}\")");
                yield break;
            }
            var samples = WavUtility.Resample(pcm.Value.samples, pcm.Value.sampleRate, MicRecorder.TargetRate);
            float t = Time.realtimeSinceStartup;
            target.BeginListening();
            yield return vc.Process(samples, target);
            Log($"You -> {target.DisplayNameEnglish}: \"{text}\" heard as \"{vc.LastTranscript}\" ({vc.LastTranscriptionSeconds:0.00}s)");
            while (target.State == CompanionState.Thinking || target.State == CompanionState.Transcribing) yield return null;
            Log($"   {target.DisplayNameEnglish} started talking {Time.realtimeSinceStartup - t:0.00}s after the line ended");
        }

        private IEnumerator WaitIdle(DialogueAgent a, float timeout = 60f)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            yield return null;
            while (Time.realtimeSinceStartup < deadline && a.State != CompanionState.Idle) yield return null;
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>Marks lessons passed, as (HSK level, how many) pairs, and no others: SetLessons(1, 11, 2, 3).</summary>
        private static void SetLessons(params int[] levelCounts)
        {
            var done = SaveSystem.Data.lessonsDone;
            done.Clear();
            for (int i = 0; i + 1 < levelCounts.Length; i += 2)
                for (int k = 1; k <= Mathf.Min(levelCounts[i + 1], Hsk.LessonCounts[levelCounts[i]]); k++)
                    done.Add($"{levelCounts[i]}-{k}");
        }

        /// <summary>
        /// The bus: always there, the driver only after HSK 1; a ticket costs what ten of the region's best fish at their
        /// biggest sell for; riding switches the region's look, fish, goods and everyone's clothes; desert to snow needs
        /// HSK 2 and the next ticket, Snow Bay to Mars HSK 3 and the last; going back is free; paid legs stay paid.
        /// </summary>
        private IEnumerator BusChecks(PlayerController player, CameraRig cam)
        {
            var d = SaveSystem.Data;
            int hsk = d.hskLevel, location = d.location, reached = d.regionReached, money = d.money;
            var discovered = d.discoveredFish.ToList();
            d.location = 0;
            d.regionReached = 0;
            Regions.Apply();

            // Tickets and which sea each fish lives in.
            int fare0 = BusTrip.Fare(0), fare1 = BusTrip.Fare(1), fare2 = BusTrip.Fare(2);
            var best0 = BusTrip.FareFish(0);
            Check(FishPower.TierOf(best0) == 0 && fare0 == 10 * Catalog.FishPrice(best0, best0.maxWeight) &&
                  FishDatabase.All.Where(f => f.IsFish && FishPower.TierOf(f) == 0).All(f => Catalog.FishPrice(f, f.maxWeight) <= Catalog.FishPrice(best0, best0.maxWeight)) &&
                  fare1 == fare0 * 10 && fare2 == fare1 * 10,
                $"tickets cost ten top-size best fish: Willow Bay ¥{fare0} ({BusTrip.FareFishEnglish(0)}), the desert ¥{fare1} ({BusTrip.FareFishEnglish(1)}), Snow Bay ¥{fare2} ({BusTrip.FareFishEnglish(2)})");
            Check(Enumerable.Range(0, 4).All(t => FishDatabase.All.Count(f => f.IsFish && FishPower.TierOf(f) == t) == 20),
                $"every region's sea has 20 kinds of fish ({string.Join(", ", Enumerable.Range(0, 4).Select(t => FishDatabase.All.Count(f => f.IsFish && FishPower.TierOf(f) == t)))})");
            var octopus = FishDatabase.Get("octopus");
            var shark = FishDatabase.Get("cod");
            var martian = FishDatabase.Get("dustminnow");
            foreach (var f in new[] { "octopus", "cod", "dustminnow" }) if (!d.discoveredFish.Contains(f)) d.discoveredFish.Add(f);
            var ctx = new CatchContext { hour = 20f, lineKg = 100f, shoreDistance = 100f, bait = Catalog.Get("bait_squid") };
            string atHome = FishDatabase.Missing(octopus, ctx);
            Check(atHome != null && atHome.StartsWith("doesn't live here") && Enumerable.Range(0, 4).All(r => Regions.FishTier(r) == r),
                $"each region's sea has its own tier (octopus at home: {atHome})");

            // Goods of later tiers are a surprise: not listed or displayed in Willow Bay.
            var blue = Catalog.Get("line_blue");
            var blueTag = PriceTag.Known.FirstOrDefault(t => t.itemId == "line_blue");
            var tackleShop = Catalog.Shop("tackle");
            Check(!ShopStock.Revealed(blue) && !ShopStock.Goods(tackleShop).Contains(blue) && (blueTag == null || !blueTag.gameObject.activeSelf) &&
                  !Encyclopedia.NoteFor("what fishing lines are there").Contains("蓝线"),
                $"HSK 1 goods are hidden in Willow Bay (Old Wang shows {string.Join(", ", ShopStock.Goods(tackleShop).Select(i => i.id))})");

            // The bus is always there; the driver turns up with HSK 1.
            var stop = BusStop.Instance;
            Check(stop != null && stop.transform.Find("Bus") != null, "the bus is parked at the bus stop");
            if (stop == null) yield break;
            d.hskLevel = 0;
            yield return null;
            yield return null;
            Check(!stop.Driver.activeSelf && !ShopkeeperBrain.Keepers.Any(k => k.Shop.busDriver), "before the HSK 1 test there's no driver");
            Vector2 view = WorldShape.BusStopCenter + new Vector2(-2f, 8f);
            player.Teleport(new Vector3(view.x, WorldShape.TerrainHeight(view.x, view.y) + 0.05f, view.y), 180f);
            cam.Configure(player.transform, 180f, 16f, 8f);
            yield return new WaitForSeconds(1f);
            yield return Shot("29a_bus_stop_closed");
            d.hskLevel = 1;
            yield return null;
            yield return null;
            var driver = ShopkeeperBrain.Keepers.FirstOrDefault(k => k.Shop.busDriver);
            Check(stop.Driver.activeSelf && driver != null, "after HSK 1, 张师傅 the driver is at the bus");
            if (driver == null) yield break;

            var busShop = driver.Shop;
            var p1 = ShopIntentParser.Parse(busShop, "我想去沙漠", false);
            var p2 = ShopIntentParser.Parse(busShop, "车票多少钱", false);
            var p3 = ShopIntentParser.Parse(busShop, "我要回去", false);
            var p4 = ShopIntentParser.Parse(busShop, "我想去雪湾", false);
            Check(p1?.intent == "travel" && p1.item == "next" && p2?.intent == "ask_price" && p3?.intent == "travel" && p3.item == "back" && p4?.intent == "travel",
                $"bus lines are understood (去沙漠 -> {p1}; 车票多少钱 -> {p2}; 回去 -> {p3}; 去雪湾 -> {p4})");

            yield return GoToShop(player, cam, WorldShape.BusStall, driver);
            yield return WaitIdle(driver, 30f);
            yield return Shot("29b_bus_driver");

            // Not enough money: he won't sell a ticket.
            d.money = fare0 - 1;
            driver.HandlePlayerUtterance("师傅，我想去金沙湾。");
            yield return WaitIdle(driver);
            Check(driver.PendingOffer == null && Regions.Current == 0 && BusTrip.CannotGoOn() == "money", $"with ¥{d.money} (the ticket is ¥{fare0}) he won't sell a ticket");

            // Buy the ticket and ride.
            d.money = fare0 + 100;
            driver.HandlePlayerUtterance("我想去金沙湾。");
            yield return WaitIdle(driver);
            Check(driver.PendingOffer != null && driver.PendingOffer.busTo == 1 && driver.PendingOffer.price == fare0, $"he offers a ticket ({driver.PendingOffer?.english})");
            int paidFrom = d.money; // (other sections may have added a little money: compare with just before)
            // Willow Bay's house: a double bed and a chair there (they stay in that house).
            Inventory.Add("bed_double", 1);
            int placedBefore = d.placed.Count;
            d.placed.Add(new PlacedItem { id = "chair", region = 0, x = 0, y = 0, z = 0 });
            float homeEnergy = Energy.Max;
            int dayBefore = DayNightCycle.Instance.Day;
            Energy.Spend(30f);
            driver.HandlePlayerUtterance("好，走吧。");
            yield return WaitForRide(stop);
            Check(DayNightCycle.Instance.Day == dayBefore + 1 && Mathf.Abs(DayNightCycle.Instance.TimeOfDay - 6f) < 0.3f,
                $"the bus ride takes the night: day {dayBefore} -> {DayNightCycle.Instance.Day}, arriving at {DayNightCycle.Instance.ClockText}");
            Check(Energy.BedLevel == 0 && Mathf.Approximately(Energy.Max, Energy.BedEnergy[0]) && Mathf.Approximately(Energy.Current, Energy.Max) && homeEnergy > Energy.Max,
                $"...rested as if you'd slept in the desert house: {Energy.Current:0}/{Energy.Max:0} energy (Willow Bay's house, with its double bed and chair, gives {homeEnergy:0})");
            Check(!FindObjectsByType<FurnitureInteractable>(FindObjectsSortMode.None).Any(fi => fi.name.Contains("chair")),
                "...and Willow Bay's furniture stays in Willow Bay's house");
            Inventory.Add("bed_desert2", 1);
            Check(Mathf.Approximately(Energy.Max, Energy.BedEnergy[2]) && Energy.Bed.id == "bed_desert2", $"a desert bed counts in the desert house ({Energy.Bed.english}: {Energy.Max:0})");
            Check(Regions.Current == 1 && d.regionReached == 1 && d.money == paidFrom - fare0 && RegionShown(1) && Vector3.Distance(player.transform.position, BusStop.ArrivalSpot) < 2f,
                $"the ticket is paid (¥{paidFrom} -> ¥{d.money}) and the bus takes you to {Regions.Here.english}: the desert look is on, you get off at the stop " +
                $"(region {Regions.Current}, reached {d.regionReached}, ¥{d.money}, look {RegionShown(1)}, {Vector3.Distance(player.transform.position, BusStop.ArrivalSpot):0.0} m from the stop)");
            Check(FishDatabase.Missing(octopus, ctx) == null && FishDatabase.Missing(shark, ctx) != null && FishDatabase.Missing(FishDatabase.Get("goby"), ctx) != null,
                "...the desert's sea has its own fish (the octopus; not Willow Bay's goby or Snow Bay's cod)");
            Check(ShopStock.Revealed(blue) && (blueTag == null || blueTag.gameObject.activeSelf) && !ShopStock.Revealed("line_black"),
                "...Old Wang now puts out the blue line (the black line is still a surprise)");
            // Perks: the cook here as an old friend pays 20% more; the furniture maker gives his masterpiece.
            var cook = Catalog.ShopFor("fish", 1);
            var carpenter = Catalog.ShopFor("furniture", 1);
            float perkBefore = FishSale.PerkMultiplier;
            d.hskLevel = 2; // (the desert's closest friendships need its test, HSK 2)
            MakeOldFriends(cook.id);
            MakeOldFriends(carpenter.id);
            Check(Mathf.Approximately(perkBefore, 1f) && Mathf.Approximately(FishSale.PerkMultiplier, 1.2f) && Inventory.Owns("sig_desert") && Perks.Has("furniture") && !Perks.Has("tackle"),
                $"old friends here give their perks ({cook.keeperName}: x{FishSale.PerkMultiplier:0.00} for fish; {carpenter.keeperName}: {Catalog.Get("sig_desert").english})");
            d.hskLevel = 1;
            var outfit = player.GetComponent<RegionOutfit>();
            var meiOutfit = FindFirstObjectByType<CompanionBrain>()?.GetComponent<RegionOutfit>();
            int dressed = FindObjectsByType<RegionOutfit>(FindObjectsSortMode.None).Count(o => o.Shown == 1);
            Check(outfit != null && outfit.Shown == 1 && outfit.Body.sharedMesh.name.Contains("desert") && outfit.Hat(1) != null && outfit.Hat(1).activeSelf && meiOutfit != null && dressed >= 12,
                $"...and everyone is dressed for the desert ({dressed} characters; the player has a sun hat and {outfit?.Body.sharedMesh.name})");
            var hereKeepers = ShopkeeperBrain.Keepers.Where(k => !k.Shop.busDriver).ToList();
            Check(hereKeepers.Count == 10 && hereKeepers.All(k => k.Shop.region == 1) && FindObjectsByType<MinigameStall>(FindObjectsSortMode.None).Length == 3,
                $"every stall is here too, with new people ({string.Join(", ", hereKeepers.OrderBy(k => k.Shop.stall).Select(k => k.DisplayName))})");
            cam.Configure(player.transform, 200f, 16f, 9f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("30a_desert_bus_stop");
            yield return CloseUp(player, cam, "30b_desert_outfits");

            // On to the snow: HSK 2 and the next ticket.
            yield return GoToShop(player, cam, WorldShape.BusStall, driver);
            yield return WaitIdle(driver, 30f);
            d.money = fare1 + 50;
            driver.HandlePlayerUtterance("师傅，我想去雪湾。");
            yield return WaitIdle(driver);
            Check(driver.PendingOffer == null && BusTrip.CannotGoOn() == "hsk", "riding on to the snow needs the HSK 2 test");
            d.hskLevel = 2;
            driver.HandlePlayerUtterance("我想去雪湾。");
            yield return WaitIdle(driver);
            Check(driver.PendingOffer != null && driver.PendingOffer.busTo == 2 && driver.PendingOffer.price == fare1, $"after HSK 2 he offers the next ticket ({driver.PendingOffer?.english})");
            int paidFrom1 = d.money; // (other sections may have added a little money: compare with just before)
            driver.HandlePlayerUtterance("好的，走吧。");
            yield return WaitForRide(stop);
            Check(Regions.Current == 2 && d.regionReached == 2 && d.money == paidFrom1 - fare1 && RegionShown(2) && Weather.Snowy, $"the bus takes you to {Regions.Here.english}: snow on the ground and in the air");
            Check(FishDatabase.Missing(shark, ctx) == null && FishDatabase.Missing(octopus, ctx) != null && ShopStock.Revealed("line_black") && !ShopStock.Revealed("line_gold"),
                "...the snowy sea has its own fish (the cod), and HSK 2 goods are out (HSK 3 goods wait for the next stop)");
            Check(outfit.Shown == 2 && outfit.Body.sharedMesh.name.Contains("snow") && outfit.Hat(2).activeSelf && outfit.Extra(2) != null && outfit.Extra(2).activeSelf && !outfit.Hat(1).activeSelf,
                "...and everyone wears coats, beanies and scarves");
            cam.Configure(player.transform, 200f, 16f, 9f);
            yield return new WaitForSeconds(2f);
            yield return Shot("31a_snow_bus_stop");
            yield return CloseUp(player, cam, "31b_snow_outfits");
            Vector2 mc = WorldShape.MarketCenter;
            player.Teleport(new Vector3(mc.x - 13f, WorldShape.TerrainHeight(mc.x - 13f, mc.y) + 0.05f, mc.y), 90f);
            cam.Configure(player.transform, 90f, 18f, 10f);
            yield return new WaitForSeconds(2f);
            yield return Shot("31c_snow_market");

            // On to Mars: HSK 3 and the last ticket.
            yield return GoToShop(player, cam, WorldShape.BusStall, driver);
            yield return WaitIdle(driver, 30f);
            d.money = fare2 + 10;
            driver.HandlePlayerUtterance("师傅，我想去火星。");
            yield return WaitIdle(driver);
            Check(driver.PendingOffer == null && BusTrip.CannotGoOn() == "hsk", "riding on to Mars needs the HSK 3 test");
            Hsk.PassTest(3);
            Check(!ShopStock.Revealed("line_gold"), "...and passing it doesn't bring HSK 3 goods out in Snow Bay");
            driver.HandlePlayerUtterance("我想去火星。");
            yield return WaitIdle(driver);
            Check(driver.PendingOffer != null && driver.PendingOffer.busTo == 3 && driver.PendingOffer.price == fare2, $"after HSK 3 he offers a ticket to Mars ({driver.PendingOffer?.english})");
            int paidFrom2 = d.money; // (other sections may have added a little money: compare with just before)
            driver.HandlePlayerUtterance("好的，走吧。");
            yield return WaitForRide(stop);
            Check(Regions.Current == 3 && Regions.Here.id == "mars" && d.regionReached == 3 && d.money == paidFrom2 - fare2 && RegionShown(3) && !Weather.Snowy,
                $"the bus takes you to {Regions.Here.english}: red ground and a purple sea");
            Check(FishDatabase.Missing(martian, ctx) == null && FishDatabase.Missing(shark, ctx) != null && ShopStock.Revealed("line_gold") && BusTrip.Next < 0 && BusTrip.CannotGoOn() == "end",
                "...the purple sea has the Martian fish, HSK 3 goods are out, and it's the end of the line");
            Check(outfit.Shown == 3 && outfit.Body.sharedMesh.name.Contains("mars") && outfit.Extra(3) != null && outfit.Extra(3).activeSelf && !outfit.Extra(2).activeSelf,
                "...and everyone wears a space suit and helmet");
            cam.Configure(player.transform, 200f, 16f, 9f);
            yield return new WaitForSeconds(2f);
            yield return Shot("32a_mars_bus_stop");
            yield return CloseUp(player, cam, "32b_mars_outfits");
            player.Teleport(new Vector3(mc.x - 13f, WorldShape.TerrainHeight(mc.x - 13f, mc.y) + 0.05f, mc.y), 90f);
            cam.Configure(player.transform, 90f, 18f, 10f);
            yield return new WaitForSeconds(2f);
            yield return Shot("32c_mars_market");
            Vector2 dock = WorldShape.DockShorePoint + WorldShape.DockDirection * 5f;
            player.Teleport(new Vector3(dock.x, WorldShape.TerrainHeight(dock.x, dock.y) + 0.05f, dock.y), 0f);
            cam.Configure(player.transform, 160f, 20f, 12f);
            yield return new WaitForSeconds(2f);
            yield return Shot("32d_mars_sea");
            d.money = 50;

            // Back for free, and the paid legs stay paid.
            yield return GoToShop(player, cam, WorldShape.BusStall, driver);
            yield return WaitIdle(driver, 30f);
            driver.HandlePlayerUtterance("师傅，我想回雪湾。");
            yield return WaitIdle(driver);
            Check(driver.PendingOffer != null && driver.PendingOffer.busTo == 2 && driver.PendingOffer.price == 0, $"going back is free ({driver.PendingOffer?.english})");
            driver.HandlePlayerUtterance("好的。");
            yield return WaitForRide(stop);
            Check(Regions.Current == 2 && RegionShown(2) && d.money == 50, "...back in Snow Bay, nothing paid");
            d.money = 0;
            Check(BusTrip.NextPaid && BusTrip.CannotGoOn() == null, "riding to Mars again needs no ticket, even with no money");

            // Put everything back.
            if (ShopConversation.Active != null) ShopConversation.Instance.End(sayGoodbye: false);
            d.placed.RemoveAll(pl => pl.id == "chair" && pl.region == 0 && pl.x == 0 && pl.z == 0);
            Inventory.Remove("bed_double", 1);
            Inventory.Remove("bed_desert2", 1);
            Inventory.Remove("sig_desert", 1);
            foreach (var id in new[] { cook.id, carpenter.id }) { var ks = Affinity.State(id); ks.facts.Clear(); ks.giftLevels.Clear(); ks.level = 0; }
            d.perksGranted.Clear();
            d.hskLevel = hsk;
            d.location = location;
            d.regionReached = reached;
            d.money = money;
            d.discoveredFish.Clear();
            d.discoveredFish.AddRange(discovered);
            Regions.Apply();
        }

        /// <summary>Everything done for the highest friendship with a keeper (facts, a gift per level, tests), then evaluated.</summary>
        private static void MakeOldFriends(string shopId)
        {
            var ks = Affinity.State(shopId);
            foreach (var f in new[] { "hometown", "siblings", "hobby", "food", "family", "birthday", "dream" }) if (!ks.facts.Contains(f)) ks.facts.Add(f);
            for (int l = 1; l <= Affinity.MaxLevel; l++) if (!ks.giftLevels.Contains(l)) ks.giftLevels.Add(l);
            Affinity.Evaluate(shopId);
        }

        private IEnumerator WaitForRide(BusStop stop)
        {
            float until = Time.realtimeSinceStartup + 30f;
            yield return new WaitForSeconds(0.5f);
            while ((stop.Riding || SleepSystem.Instance.Busy) && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
        }

        private static bool RegionShown(int region)
        {
            var looks = FindObjectsByType<RegionStyle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return looks.Length > 0 && looks.All(l => l.gameObject.activeSelf == (l.region == region));
        }

        /// <summary>The player, Mei and a shopkeeper side by side in the plaza, to see their clothes.</summary>
        private IEnumerator CloseUp(PlayerController player, CameraRig cam, string shot)
        {
            if (ShopConversation.Active != null) ShopConversation.Instance.End(sayGoodbye: false);
            var keeper = ShopkeeperBrain.Keepers.First(k => k.Shop.role == "fish");
            int i = keeper.Shop.stall;
            Vector2 spot = WorldShape.CustomerSpot(i), stall = WorldShape.StallPosition(i);
            Vector2 dir = (stall - spot).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            player.Teleport(new Vector3(spot.x, WorldShape.TerrainHeight(spot.x, spot.y) + 0.05f, spot.y), yaw + 180f);
            FindFirstObjectByType<CompanionController>()?.Warp();
            cam.Configure(player.transform, yaw, 12f, 4.5f); // from the plaza, looking at their faces
            yield return new WaitForSeconds(1.5f);
            yield return Shot(shot);
        }

        /// <summary>Walks up to a stall and "presses E" to start talking with its keeper.</summary>
        private IEnumerator GoToShop(PlayerController player, CameraRig cam, int stall, ShopkeeperBrain keeper)
        {
            Vector2 spot = WorldShape.CustomerSpot(stall);
            Vector2 stallPos = WorldShape.StallPosition(stall);
            Vector3 p = new Vector3(spot.x, WorldShape.TerrainHeight(spot.x, spot.y) + 0.05f, spot.y);
            Vector2 dir = (stallPos - spot).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            player.Teleport(p, yaw);
            cam.Configure(player.transform, yaw, 18f, 7f);
            yield return new WaitForSeconds(0.6f);
            var convo = ShopConversation.Instance;
            Check(ShopConversation.Active == null, $"walking to {keeper.DisplayNameEnglish} ended any earlier shop conversation");
            Check(convo != null && convo.Candidate == keeper, $"[E] prompt offers to talk to {keeper.DisplayName} ({(convo?.Candidate != null ? convo.Candidate.DisplayName : "nobody")})");
            convo?.Begin(keeper);
            Check(ShopConversation.Active == keeper, $"E starts a conversation with {keeper.DisplayName}");
            yield return new WaitForSeconds(0.9f);
        }

        /// <summary>
        /// Fish discovery, the bookshop and the trainer, strength vs heavy fish, and the shop window (E).
        /// </summary>
        private IEnumerator StatsChecks(VoiceChatController vc, PlayerController player, CameraRig cam, GameUI ui)
        {
            // A new game knows the four starter fish.
            Check(PlayerStats.DiscoveredFishCount == PlayerStats.StarterFishes.Length && PlayerStats.StarterFishes.All(id => PlayerStats.IsDiscovered(FishDatabase.Get(id))),
                $"a new game knows the starter fish ({PlayerStats.DiscoveredFishCount}: {string.Join(", ", PlayerStats.StarterFishes)})");
            var ctx = new CatchContext { hour = 8f, shoreDistance = 20f, lineKg = 80f, bait = Catalog.Get("bait_worm") };
            bool onlyKnown = Enumerable.Range(0, 300).Select(_ => FishDatabase.Roll(ctx)).All(f => f != null && PlayerStats.StarterFishes.Contains(f.id));
            Check(onlyKnown, "only discovered fish bite, and only fish (300 rolls: all starter fish)");
            Check(FishDatabase.All.All(f => f.IsFish), "nothing but fish can be caught (no junk)");
            var sardine = FishDatabase.Get("sardine");
            var weights = Enumerable.Range(0, 200).Select(_ => FishDatabase.RollWeight(sardine)).ToList();
            Check(weights.All(w => w >= sardine.minWeight && w <= sardine.maxWeight), $"sardine weights stay in their real range ({weights.Min():0.000}-{weights.Max():0.000} kg)");

            Inventory.Earn(300);
            var books = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "books");
            yield return GoToShop(player, cam, 4, books);
            yield return new WaitForSeconds(0.5f);
            Check(ui.Shop != null && ui.Shop.IsOpen && ui.Shop.Keeper == books, "E opens the bookshop's shop window");
            yield return Shot("05b_shop_window");
            yield return WaitIdle(books, 30f);
            yield return Say(vc, books, SpeechEngine.VoiceMale, "老师，我想买钓鱼入门。");
            yield return WaitIdle(books);
            Check(books.PendingOffer != null && books.PendingOffer.itemId == "book_basics", "asking for 钓鱼入门 makes Teacher Zhou offer it");
            yield return Say(vc, books, SpeechEngine.VoiceMale, "好的，要。");
            yield return WaitIdle(books);
            Check(Inventory.Owns("book_basics"), "bought Fishing for Beginners by saying 要");
            var found = PlayerStats.ReadBook("book_basics");
            Check(found.Count == Catalog.Get("book_basics").teachesFish.Length && PlayerStats.IsDiscovered(FishDatabase.Get("mackerel")), $"reading it discovered {found.Count} fish ({string.Join(", ", found.Select(f => f.name))})");
            // Every requirement has to be met: distance, bait, line, time.
            var mackerel = FishDatabase.Get("mackerel");
            var near = new CatchContext { hour = 9f, shoreDistance = 3f, lineKg = 3f, bait = Catalog.Get("bait_worm") };
            var noBait = new CatchContext { hour = 9f, shoreDistance = 10f, lineKg = 3f, bait = null };
            var thinLine = new CatchContext { hour = 9f, shoreDistance = 10f, lineKg = 1f, bait = Catalog.Get("bait_worm") };
            var night = new CatchContext { hour = 23f, shoreDistance = 10f, lineKg = 3f, bait = Catalog.Get("bait_worm") };
            var ok = new CatchContext { hour = 9f, shoreDistance = 10f, lineKg = 3f, bait = Catalog.Get("bait_worm") };
            Check(FishDatabase.Missing(mackerel, near) != null && FishDatabase.Missing(mackerel, noBait) == null && FishDatabase.Missing(mackerel, thinLine) != null &&
                  FishDatabase.Missing(mackerel, night) != null && FishDatabase.Missing(mackerel, ok) == null,
                $"mackerel needs 6 m out, a 2 kg line and daylight; any bait works ({FishDatabase.Missing(mackerel, near)}; {FishDatabase.Missing(mackerel, thinLine)})");
            Check(PlayerStats.Knowledge == 1, "fishing knowledge is 1 book");

            var gym = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "gym");
            yield return GoToShop(player, cam, 5, gym);
            Check(ui.Shop.Keeper == gym, "walking to Coach Wu switches the shop window to the trainer");
            yield return WaitIdle(gym, 30f);
            float calmBefore = FishPower.ForPlayer(FishDatabase.Get("mackerel")).moveRate;
            bool addedLesson = !SaveSystem.Data.lessonsDone.Contains("1-1");
            if (addedLesson) SaveSystem.Data.lessonsDone.Add("1-1"); // the first training level needs one HSK 1 lesson
            yield return Say(vc, gym, SpeechEngine.VoiceMale, "教练，我想买力量训练。");
            yield return WaitIdle(gym);
            Check(gym.PendingOffer != null && gym.PendingOffer.itemId == "up_cast", "asking for 力量训练 gets a strength-training offer");
            yield return Say(vc, gym, SpeechEngine.VoiceMale, "好的，我要。");
            yield return WaitIdle(gym);
            float calmAfter = FishPower.ForPlayer(FishDatabase.Get("mackerel")).moveRate;
            Check(PlayerStats.Level("cast") == 1 && calmAfter < calmBefore, $"strength training calms fish (mackerel x{calmBefore:0.00} -> x{calmAfter:0.00})");
            Check(Catalog.PriceOf(Catalog.Get("up_cast")) == 20, $"the next level costs more (¥{Catalog.PriceOf(Catalog.Get("up_cast"))})");
            if (addedLesson) SaveSystem.Data.lessonsDone.Remove("1-1");
            ShopConversation.Instance.End(sayGoodbye: false);
            yield return FriendshipChecks(vc, player, cam, ui);
        }

        /// <summary>Friendship scoring, the gift shop, giving a liked gift, asking about a keeper, locked goods, and Mei as encyclopedia.</summary>
        private IEnumerator FriendshipChecks(VoiceChatController vc, PlayerController player, CameraRig cam, GameUI ui)
        {

            // Gift shop: buy tea by voice.
            Inventory.Earn(200);
            var gifts = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "gifts");
            yield return GoToShop(player, cam, 7, gifts);
            yield return WaitIdle(gifts, 30f);
            yield return Say(vc, gifts, SpeechEngine.VoiceMale, "奶奶，我想买茶。");
            yield return WaitIdle(gifts);
            Check(gifts.PendingOffer != null && gifts.PendingOffer.itemId == "gift_tea", "Granny Liu offers tea");
            yield return Say(vc, gifts, SpeechEngine.VoiceMale, "好的，我要。");
            yield return WaitIdle(gifts);
            Check(Inventory.Count("gift_tea") > 0, "bought tea at the gift shop");
            yield return Shot("05c_gift_shop");

            // Old Wang: ask where he's from (shared even with strangers), give him the tea he likes, then he shares more.
            var tackle = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "tackle");
            yield return GoToShop(player, cam, 0, tackle);
            yield return WaitIdle(tackle, 30f);
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "你是哪里人？");
            yield return WaitIdle(tackle);
            Check(Affinity.Knows("tackle", "hometown"), "asking 你是哪里人 puts Old Wang's hometown in the journal");
            Check(!Affinity.Knows("tackle", "like:茶"), "Old Wang's likes aren't known yet");
            int teaBefore = Inventory.Count("gift_tea");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "老王，这是送给你的茶。");
            yield return WaitIdle(tackle);
            Check(Inventory.Count("gift_tea") == teaBefore - 1 && Affinity.State("tackle").giftLevels.Contains(1), "giving tea counted as his gift for 认识");
            Check(Affinity.Knows("tackle", "like:茶"), "his delight put 茶 in the journal");
            Check(Affinity.Level("tackle") >= 1, $"friendship with Old Wang is now {Affinity.LevelHanzi[Affinity.Level("tackle")]}");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "我想买碳素鱼竿。");
            yield return WaitIdle(tackle);
            Check(tackle.PendingOffer == null || tackle.PendingOffer.itemId != "rod_carbon", "the carbon rod stays locked until you're 朋友");
            yield return Shot("05d_friendship");

            // Mei knows everyone: "what does Old Wang like?"
            ShopConversation.Instance.End(sayGoodbye: false);
            var mei = FindFirstObjectByType<CompanionBrain>();
            yield return WaitIdle(mei, 30f);
            yield return Say(vc, mei, SpeechEngine.VoiceEnglish, "Mei, what does Old Wang like?");
            yield return WaitIdle(mei);
            string reply = ConversationLog.Entries.LastOrDefault(e => e.speaker == mei.DisplayName).text ?? "";
            Check(ReadShared(ChatAudit.SessionFile).Contains("What you know about 老王"), "Mei was given what she knows about Old Wang");
            Check(reply.Contains("茶") || reply.ToLowerInvariant().Contains("tea"), $"Mei says what Old Wang likes: \"{reply}\"");
            Check(Affinity.Knows("tackle", "like:帽子"), "what Mei said went into the journal too");
            ui.ToggleJournal(true);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("05e_journal_people");
            ui.ToggleJournal(false);
            yield return BoatChecks(vc, player, cam);
            yield return SchoolChecks(vc, player, cam, ui);
        }

        /// <summary>
        /// 高老师's test centre: start a lesson by voice, answer it (some spoken), practise, pass the HSK 1 test, and
        /// see goods unlock (both friendship and the test count), the membership card, and the interface turn Chinese.
        /// </summary>
        private IEnumerator SchoolChecks(VoiceChatController vc, PlayerController player, CameraRig cam, GameUI ui)
        {
            var teacher = ShopkeeperBrain.Keepers.FirstOrDefault(k => k.Shop.school);
            Check(teacher != null && teacher.Shop.stall == 8 && WorldShape.StallCount == 10, "the test centre is the 9th stall, with 高老师 (海叔's crab stall is the 10th)");
            if (teacher == null) yield break;

            // Before any test: Old Wang won't sell the blue line even to a friend.
            var tackle = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "tackle");
            Affinity.DebugGrant("tackle", Affinity.MaxLevel); // everything learned and given: only the HSK tests hold it back
            Inventory.Earn(2000);
            tackle.HandlePlayerUtterance("老板，我要蓝线。");
            Check(tackle.PendingOffer == null && Hsk.Level == 0, $"no HSK test yet: Old Wang won't sell the blue line, even as {Affinity.LevelHanzi[Affinity.Level("tackle")]}");
            tackle.Interrupt();
            Check(UiText.Plain("Bag", "鱼", 1) == "Bag" && UiText.Plain("Day 2", "第2天", 1) == "Day 2", "the interface is in English before any test");

            yield return GoToShop(player, cam, 8, teacher);
            yield return WaitIdle(teacher, 30f);
            yield return Say(vc, teacher, SpeechEngine.VoiceMale, "老师，我想上课。");
            var lesson = HskSchool.Current;
            Check(lesson != null && lesson.kind == LessonKind.Lesson && lesson.level == 1 && lesson.lesson == 1,
                $"我想上课 starts lesson 1 of HSK 1 ({lesson?.questions.Count ?? 0} questions: {lesson?.questions.Count(q => !q.recall)} to repeat, {lesson?.questions.Count(q => q.recall)} from English)");
            if (lesson == null) yield break;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("17_lesson");

            // Three answers are spoken (through speech recognition), the rest typed in directly. Only words of two or more
            // characters are spoken: the test voice garbles a lone character said on its own (呢 and 三 both came out as 呀).
            int spoken = 0, spokenRight = 0;
            while (spoken < 3 && HskSchool.Current == lesson)
            {
                var q = lesson.Current;
                if (q.word.hanzi.Length < 2)
                {
                    teacher.HandlePlayerUtterance(q.word.hanzi);
                    yield return null;
                    continue;
                }
                // Let the teacher finish (her lines are queued for speech before she starts talking).
                yield return new WaitForSeconds(0.3f);
                for (float t = 0f; t < 15f && teacher.Voice != null && teacher.Voice.IsSpeaking; t += Time.deltaTime) yield return null;
                yield return Say(vc, teacher, SpeechEngine.VoiceMale, q.word.hanzi);
                spoken++;
                if (q.correct) spokenRight++;
                Log($"   lesson: {q.word.hanzi} ({q.word.pinyin}) heard \"{q.heard}\" -> {(q.correct ? "right" : "wrong")}");
            }
            Check(spokenRight >= 2, $"spoken answers are graded ({spokenRight}/{spoken} words heard right)");
            HskSchool.ForceSurprise = false;
            int money = Inventory.Money;
            int at = lesson.index;
            teacher.HandlePlayerUtterance("错的");
            Check(lesson.index == at && lesson.retryHeard == "错的", "in a lesson a wrong answer doesn't move on: say it again (or skip)");
            while (HskSchool.Current == lesson)
            {
                teacher.HandlePlayerUtterance(lesson.Current.word.hanzi);
                if (lesson.pending != null)
                {
                    Check(lesson.Current.recall, "only the quiz part asks to confirm what was heard");
                    HskSchool.SubmitPending(); // Y
                }
                yield return null;
            }
            yield return WaitIdle(teacher, 20f);
            Check(Hsk.LessonDone(1, 1) && Inventory.Money == money, $"passed lesson 1 (quiz {lesson.RecallRight}/{lesson.RecallAsked}); no fixed pay (surprises only, switched off here)");
            Check(ConversationLog.Entries.Any(e => e.speaker == teacher.DisplayName && e.text.Contains("跟我说")), "the teacher's lesson lines are in the journal's transcript");

            // Free practice: no money; wrong answers come back later.
            teacher.HandlePlayerUtterance("我想练习");
            var practice = HskSchool.Current;
            Check(practice != null && practice.kind == LessonKind.Practice && practice.questions.Count == Hsk.PracticeQuestions, "我想练习 starts free practice (10 random words)");
            if (practice != null)
            {
                string missed = practice.Current.word.hanzi;
                money = Inventory.Money;
                teacher.HandlePlayerUtterance("跳过");
                Check(practice.last != null && !practice.last.correct && Hsk.MissedWords.Contains(missed), $"跳过 skips a word, and it's noted for later ({missed})");
                while (HskSchool.Current == practice)
                {
                    teacher.HandlePlayerUtterance(practice.Current.word.hanzi);
                    yield return null;
                }
                Check(Inventory.Money == money, "practice doesn't pay");
            }

            // The HSK 1 test: 15 right and 5 skipped passes. In a test each answer waits for the player to confirm it (Y).
            var sea = FishDatabase.Get("seabass");
            int plain = Catalog.FishPrice(sea, 3f);
            teacher.HandlePlayerUtterance("我想考试");
            var test = HskSchool.Current;
            Check(test != null && test.kind == LessonKind.Test && test.level == 1 && test.questions.Count == Hsk.TestQuestions && test.questions.All(q => q.recall),
                $"我想考试 starts the HSK 1 test ({test?.questions.Count ?? 0} words from English)");
            if (test != null)
            {
                teacher.HandlePlayerUtterance("错的");
                Check(test.pending == "错的" && test.index == 0, "in a test, what was heard is shown first and isn't graded yet");
                HskSchool.DiscardPending();
                Check(test.pending == null && test.index == 0, "N throws it away to say it again");
                int n = 0;
                while (HskSchool.Current == test)
                {
                    teacher.HandlePlayerUtterance(n++ < 5 ? "跳过" : test.Current.word.hanzi);
                    if (test.pending != null) HskSchool.SubmitPending(); // Y
                    yield return null;
                }
                Check(test.Right == 15 && Hsk.Level == 1, $"15/20 passes the HSK 1 test (HSK level now {Hsk.Level})");
                Check(HskSchool.LastFinished == test && HskSchool.LastPassed, "the result (PASSED) stays in Teacher Gao's window");
            }
            yield return WaitIdle(teacher, 20f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("18_after_hsk1");
            teacher.HandlePlayerUtterance("我想考试");
            Check(HskSchool.Current != null && HskSchool.Current.level == 2, "no daily limit: the HSK 2 test can start straight away");
            HskSchool.Abandon();
            teacher.Interrupt();
            Check(UiText.Plain("Day 2", "第2天", 1) == "第2天 Day 2" && UiText.Plain("Fish", "鱼", 0) == "鱼" && UiText.Plain("Bag", "包", 3) == "Bag",
                "after HSK 1: HSK 1 labels show both languages, easier ones only Chinese, HSK 3 ones stay English");
            float expected = FishSale.BasePerFill + FishSale.PerLesson * FishSale.LessonsPassed + FishSale.PerTest * Hsk.Level;
            Check(Mathf.Abs(FishSale.PerFill - expected) < 0.001f && FishSale.PerFill >= 0.25f,
                $"after HSK 1 (and {FishSale.LessonsPassed} lessons) each bar on Auntie Chen's scale is worth +{FishSale.PerFill * 100f:0}%");

            // HSK 1 goods stay a surprise in Willow Bay; from the desert on, Old Wang sells the blue line.
            tackle.HandlePlayerUtterance("老板，我要蓝线。");
            bool hiddenHere = tackle.PendingOffer == null;
            int reachedNow = SaveSystem.Data.regionReached;
            SaveSystem.Data.regionReached = 1;
            tackle.HandlePlayerUtterance("老板，我要蓝线。");
            Check(hiddenHere && tackle.PendingOffer != null && tackle.PendingOffer.itemId == "line_blue", "the blue line isn't sold in Willow Bay, but is once you've reached the desert");
            SaveSystem.Data.regionReached = reachedNow;
            tackle.HandlePlayerUtterance("不要了。");
            tackle.Interrupt();
            ShopConversation.Instance.End(sayGoodbye: false);
        }

        /// <summary>Rent Old Wang's boat by voice, row out to deep water, and come back to the dock.</summary>
        private IEnumerator BoatChecks(VoiceChatController vc, PlayerController player, CameraRig cam)
        {
            Check(FishDatabase.Get("sardine").minDistance == 0f && FishDatabase.Get("tuna").minDistance >= 50f, "big fish live far from shore (sardine 0 m, tuna 60 m)");
            var close = new CatchContext { hour = 12f, shoreDistance = 5f, lineKg = 80f, bait = Catalog.Get("bait_fish") };
            Check(!FishDatabase.Available(close).Any(f => f.minDistance > 5f), "nothing from further out bites 5 m from shore");

            Inventory.Earn(400);
            var tackle = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "tackle");
            yield return GoToShop(player, cam, 0, tackle);
            yield return WaitIdle(tackle, 30f);
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "老王，我想租船。");
            yield return WaitIdle(tackle);
            Check(tackle.PendingOffer != null && tackle.PendingOffer.itemId == "boat", "Old Wang offers his boat (we're 认识 now)");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "好的，我要。");
            yield return WaitIdle(tackle);
            Check(Inventory.Owns("boat"), "rented Old Wang's boat by saying 要");
            ShopConversation.Instance.End(sayGoodbye: false);

            var boat = Rowboat.Instance;
            Check(boat != null, "the rowboat is at the dock");
            if (boat == null) yield break;
            Vector2 end = WorldShape.DockShorePoint - WorldShape.DockDirection * (WorldShape.DockLength - 0.5f);
            player.Teleport(new Vector3(end.x, WorldShape.DockDeckHeight + 0.05f, end.y), 0f);
            yield return new WaitForSeconds(0.4f);
            boat.Interact();
            yield return null;
            Check(Rowboat.PlayerAboard, "F at the boat gets you in");

            // Row towards the middle of the lake.
            Vector3 toCentre = new Vector3(WorldShape.OpenSea.x, 0f, WorldShape.OpenSea.y) - boat.transform.position;
            toCentre.y = 0f;
            float camYaw = cam.Yaw * Mathf.Deg2Rad;
            Vector3 dir = toCentre.normalized;
            // SimulatedInput is camera-relative: turn the world direction into camera space.
            boat.SimulatedInput = new Vector2(dir.x * Mathf.Cos(camYaw) - dir.z * Mathf.Sin(camYaw), dir.x * Mathf.Sin(camYaw) + dir.z * Mathf.Cos(camYaw));
            yield return new WaitForSeconds(7f);
            boat.SimulatedInput = Vector2.zero;
            float out_ = -WorldShape.ShoreDistance(boat.transform.position.x, boat.transform.position.z);
            Check(out_ > 14f && Rowboat.PlayerAboard, $"rowed out to {out_:0} m from shore");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("10b_rowboat");

            // Back to the dock and out.
            for (float t = 0; t < 20f && !boat.TryDisembarkQuiet(); t += Time.deltaTime)
            {
                Vector3 back = new Vector3(end.x, 0f, end.y) - boat.transform.position;
                back.y = 0f;
                camYaw = cam.Yaw * Mathf.Deg2Rad;
                Vector3 d = back.normalized;
                boat.SimulatedInput = new Vector2(d.x * Mathf.Cos(camYaw) - d.z * Mathf.Sin(camYaw), d.x * Mathf.Sin(camYaw) + d.z * Mathf.Cos(camYaw));
                yield return null;
            }
            boat.SimulatedInput = Vector2.zero;
            Check(!Rowboat.PlayerAboard && PlayerController.IsWalkable(player.transform.position), "rowed back and got out on dry land");
        }

        /// <summary>
        /// Energy, the longer day, sleeping in bed, passing out (keeping only 3 slots), the bag limit, currents and cosmetics.
        /// </summary>
        private IEnumerator DayChecks(PlayerController player, CameraRig cam)
        {
            var dn = DayNightCycle.Instance;
            Check(Mathf.Approximately(dn.RealSecondsPerHour, 120f), $"a day (6am-2am) lasts {dn.RealSecondsPerHour * 20f / 60f:0} real minutes");
            Check(Energy.BedLevel == 0 && Energy.Max >= 100f, $"the house comes with a sleeping mat ({Energy.Max:0} energy with comfort level {Energy.ComfortLevel})");
            Check(Energy.ReelDrainPerSecond(150f) > Energy.ReelDrainPerSecond(0.1f) * 5f,
                $"big fish tire you out faster (sardine {Energy.ReelDrainPerSecond(0.1f):0.0}/s, 150 kg tuna {Energy.ReelDrainPerSecond(150f):0.0}/s)");

            // Bag limit: a full bag refuses more.
            int free = Inventory.SlotsFree;
            var carried = Inventory.Bucket.Select(b => b.species.id).Distinct().ToList();
            foreach (var sp in FishDatabase.All.Where(s => !carried.Contains(s.id)).Take(free)) Inventory.AddToBucket(sp, sp.minWeight);
            var another = FishDatabase.All.First(s => !Inventory.Bucket.Any(b => b.species.id == s.id));
            Check(Inventory.SlotsFree == 0 && !Inventory.HasRoomForFish(another), $"a full bag ({Inventory.SlotsUsed}/{Inventory.SlotCapacity}) has no room for another kind of fish");
            Inventory.Earn(500);
            Inventory.CanBuy("gift_tea", 1, out var why, out _);
            Check(why == BuyResult.Ok, "...but key items (bait, gifts, furniture) never need bag space");

            // Sleep in the bed: wake at 6am, full energy, next day.
            var bed = BedInteractable.Instance;
            Check(bed != null, "there's a bed in the house");
            if (bed == null) yield break;
            Energy.Spend(30f);
            dn.TimeOfDay = 21f;
            int day = dn.Day;
            player.Teleport(bed.WakeSpot, 0f);
            yield return new WaitForSeconds(0.3f);
            bed.Interact();
            yield return new WaitForSeconds(0.5f);
            while (SleepSystem.Instance.Busy) yield return null;
            Check(dn.Day == day + 1 && Mathf.Abs(dn.TimeOfDay - 6f) < 0.2f && Mathf.Approximately(Energy.Current, Energy.Max),
                $"sleeping in bed: day {day} -> {dn.Day}, woke at {dn.ClockText} with {Energy.Current:0}/{Energy.Max:0} energy");

            // Pass out: keep only the 3 most valuable slots, wake at 10am in the house.
            int before = Inventory.SlotsUsed;
            var top3 = Inventory.Slots().OrderByDescending(s => s.value).Take(3).Select(s => s.label).ToList();
            player.Teleport(new Vector3(WorldShape.MarketCenter.x, WorldShape.MarketHeight + 0.1f, WorldShape.MarketCenter.y), 0f);
            yield return new WaitForSeconds(0.3f);
            day = dn.Day;
            Energy.Spend(Energy.Current + 1f);
            yield return new WaitForSeconds(0.5f);
            while (SleepSystem.Instance.Busy) yield return null;
            yield return Shot("14_after_passing_out");
            var left = Inventory.Slots().Select(s => s.label).ToList();
            Check(Inventory.SlotsUsed <= 3 && top3.All(left.Contains), $"passing out: {before} slots -> {Inventory.SlotsUsed}, kept the most valuable ({string.Join(", ", left)})");
            Check(dn.Day == day + 1 && Mathf.Abs(dn.TimeOfDay - 10f) < 0.2f && Cabin.IsInside(player.transform.position),
                $"...and woke at {dn.ClockText} on day {dn.Day}, inside the house");

            // Currents: the rented boat can't get past 25 m.
            Inventory.Add("boat", 1);
            var boat = Rowboat.Instance;
            Vector2 end = WorldShape.DockShorePoint - WorldShape.DockDirection * (WorldShape.DockLength - 0.5f);
            player.Teleport(new Vector3(end.x, WorldShape.DockDeckHeight + 0.05f, end.y), 0f);
            cam.Configure(player.transform, 0f, 24f, 9f);
            yield return new WaitForSeconds(0.3f);
            boat.Interact();
            yield return null;
            float camYaw = cam.Yaw * Mathf.Deg2Rad;
            boat.SimulatedInput = new Vector2(-Mathf.Sin(camYaw), Mathf.Cos(camYaw)); // straight out to sea (+z)
            yield return new WaitForSeconds(16f);
            float out_ = -WorldShape.ShoreDistance(boat.transform.position.x, boat.transform.position.z);
            Check(Rowboat.Range <= 25f && out_ < Rowboat.Range + 5f && out_ > Rowboat.Range - 6f, $"currents hold the rented boat near its {Rowboat.Range:0} m range (rowed out for 16 s, reached {out_:0} m)");
            boat.SimulatedInput = Vector2.zero;
            boat.ReturnToDock();

            // The best boat goes past any current.
            Inventory.Add("boat_new", 1);
            Check(Rowboat.Range >= 140f, $"the new boat can go {Rowboat.Range:0} m out");

            // Cosmetics: buying puts them on.
            Inventory.Add("cos_boat_red", 1);
            Cosmetics.OnBought(Catalog.Get("cos_boat_red"));
            Inventory.Add("cos_mei_white", 1);
            Cosmetics.OnBought(Catalog.Get("cos_mei_white"));
            Check(Cosmetics.Chosen("boat") == "cos_boat_red" && Cosmetics.Chosen("mei") == "cos_mei_white", "cosmetics are put on when bought (red boat, Mei's white hat)");
            var mei = FindFirstObjectByType<CompanionBrain>();
            Check(mei.GetComponentsInChildren<Transform>().Any(t => t.name == "Hat_cos_mei_white"), "Mei is wearing her new hat");
            var meiBody = FindFirstObjectByType<CompanionController>();
            player.Teleport(bed.WakeSpot, 0f);
            meiBody.Warp();
            cam.Configure(player.transform, 180f, 20f, 5f);
            yield return new WaitForSeconds(1f);
            yield return Shot("16_mei_hat");
        }

        /// <summary>Walk into the cabin through the door, check the walls hold and the inside is visible.</summary>
        private IEnumerator CabinChecks(PlayerController player, CameraRig cam)
        {
            var cabin = FindFirstObjectByType<Cabin>();
            Check(cabin != null, "the cabin has its Cabin component");
            if (cabin == null) yield break;
            // No terrain pokes through the floor anywhere in the cabin.
            float worst = float.MinValue;
            for (float u = -1.45f; u <= 1.45f; u += 0.1f)
            for (float v = -0.95f; v <= 0.95f; v += 0.1f)
            {
                Vector3 w = cabin.transform.TransformPoint(new Vector3(u, 0f, v));
                worst = Mathf.Max(worst, WorldShape.TerrainHeight(w.x, w.z));
            }
            Check(worst < cabin.FloorY - 0.1f, $"no terrain inside the cabin reaches the floor (highest ground {worst:0.00}, floor {cabin.FloorY:0.00})");

            var cc = player.GetComponent<CharacterController>();
            Vector3 outside = cabin.DoorPoint(false) + cabin.transform.forward * 1.6f;
            outside.y = WorldShape.TerrainHeight(outside.x, outside.z) + 0.1f;
            Vector3 inward = -cabin.transform.forward;
            player.Teleport(outside, Quaternion.LookRotation(inward).eulerAngles.y);
            cam.Configure(player.transform, Quaternion.LookRotation(inward).eulerAngles.y, 24f, 8f);
            yield return new WaitForSeconds(0.5f);

            // Walk straight in through the doorway (about 4 m).
            for (int i = 0; i < 80; i++)
            {
                cc.Move(inward * 0.05f + Vector3.down * 0.02f);
                yield return null;
            }
            bool inside = Cabin.IsInside(player.transform.position);
            Check(inside, $"walked into the cabin through the door (at {cabin.transform.InverseTransformPoint(player.transform.position):F2} in cabin units)");
            yield return new WaitForSeconds(1.2f);
            Check(cabin.PlayerInside, "the cabin knows the player is inside (roof hidden, camera looks down)");
            float floorY = cabin.FloorY;
            Check(Mathf.Abs(player.transform.position.y - floorY) < 0.25f, $"standing on the cabin floor (feet {player.transform.position.y:0.00}, floor {floorY:0.00})");
            yield return Shot("13_cabin_inside");

            // Keep walking: the back wall must stop the player.
            for (int i = 0; i < 100; i++)
            {
                cc.Move(inward * 0.05f);
                yield return null;
            }
            Check(Cabin.IsInside(player.transform.position), "the back wall stops the player (still inside)");

            // ...and back out again.
            for (int i = 0; i < 140; i++)
            {
                Vector3 to = cabin.DoorPoint(false) + cabin.transform.forward * 1.5f - player.transform.position;
                to.y = 0f;
                if (to.magnitude < 0.2f) break;
                Vector3 local = cabin.transform.InverseTransformPoint(player.transform.position);
                // Line up with the door first, then walk out.
                Vector3 step = Mathf.Abs(local.x) > 0.08f ? -cabin.transform.right * Mathf.Sign(local.x) : cabin.transform.forward;
                cc.Move(step * 0.05f + Vector3.down * 0.02f);
                yield return null;
            }
            yield return new WaitForSeconds(0.6f);
            Check(!Cabin.IsInside(player.transform.position) && !cabin.PlayerInside, "walked back out of the cabin");
        }

        /// <summary>Mei's natural (Kokoro) voice loads, is intelligible to the recogniser and keeps up with real time.</summary>
        private IEnumerator NaturalVoiceChecks()
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!Speech.HasVoice(SpeechEngine.VoiceNatural) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(Speech.HasVoice(SpeechEngine.VoiceNatural), "Mei's natural voice (Kokoro) loaded");
            if (!Speech.HasVoice(SpeechEngine.VoiceNatural)) yield break;
            string voice = SpeechEngine.Speaker(SpeechEngine.VoiceNatural, SaveSystem.Settings.meiVoice < 0 ? 3 : SaveSystem.Settings.meiVoice);
            WavUtility.PcmData? pcm = null;
            bool done = false;
            float t = Time.realtimeSinceStartup;
            Speech.Synthesize(voice, "汤圆饿了，我们去买猫粮吧。", 0.9f, null, p => { pcm = p; done = true; });
            while (!done) yield return null;
            float gen = Time.realtimeSinceStartup - t;
            float secs = pcm.HasValue ? pcm.Value.samples.Length / (float)pcm.Value.sampleRate : 0f;
            Log($"Natural voice: {secs:0.0}s of audio in {gen:0.00}s (RTF {gen / Mathf.Max(0.01f, secs):0.00})");
            Check(secs > 1f && gen < secs * 1.2f, $"natural voice keeps up (RTF {gen / Mathf.Max(0.01f, secs):0.00})");
            if (!pcm.HasValue) yield break;
            string heard = null;
            done = false;
            Speech.Transcribe(WavUtility.Resample(pcm.Value.samples, pcm.Value.sampleRate, MicRecorder.TargetRate), (text, e) => { heard = text; done = true; });
            while (!done) yield return null;
            Check(heard != null && heard.Contains("猫粮"), $"natural voice is intelligible (recogniser heard \"{heard}\")");
            Check(CharacterVoice.SplitClauses("你好！今天天气真好，我们去钓鱼吧。").Count == 2, "long lines are spoken in clause-sized chunks");
        }

        private IEnumerator Run()
        {
            var services = LocalAIServices.Instance;
            var mei = FindFirstObjectByType<CompanionBrain>();
            var vc = FindFirstObjectByType<VoiceChatController>();
            var fishing = FindFirstObjectByType<FishingController>();
            var player = FindFirstObjectByType<PlayerController>();
            var cam = FindFirstObjectByType<CameraRig>();
            var ui = FindFirstObjectByType<GameUI>();
            Log($"LocalAI root: {services?.Root ?? "(none)"}  model: {services?.Config?.llmModel}");
            foreach (var a in new DialogueAgent[] { mei }.Concat(ShopkeeperBrain.Keepers))
            {
                var agent = a;
                agent.SentenceSpoken += s => Log($"   {agent.DisplayNameEnglish} says: {s}");
            }

            yield return new WaitForSeconds(1f);
            yield return Shot("01_start");

            float deadline = Time.realtimeSinceStartup + 240f;
            while (Time.realtimeSinceStartup < deadline &&
                   (services.LlmStatus == ServiceStatus.Starting || services.SttStatus == ServiceStatus.Starting || services.TtsStatus == ServiceStatus.Starting))
                yield return null;
            Log($"Services: LLM={services.LlmStatus} STT={services.SttStatus} TTS={services.TtsStatus} {services.LastError}");
            Check(services.LlmStatus == ServiceStatus.Ready && services.SttStatus == ServiceStatus.Ready && services.TtsStatus == ServiceStatus.Ready, "all AI services ready");
            while (!Pinyin.Ready) yield return null;
            ParserChecks();
            yield return NaturalVoiceChecks();

            // Mei greets by herself.
            deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline && mei.State == CompanionState.Idle && ConversationLog.Entries.Count == 0) yield return null;
            yield return new WaitForSeconds(2f);
            yield return Shot("02_greeting");
            yield return WaitIdle(mei);
            Log($"Mei's first-token latency: {mei.LastResponseLatency:0.00}s");

            // Ask Mei for help in English, then try some Mandarin.
            yield return Say(vc, mei, SpeechEngine.VoiceEnglish, "Mei, how do I ask Old Wang for worms in Chinese?");
            yield return new WaitForSeconds(1f);
            yield return Shot("03_mei_teaches");
            yield return WaitIdle(mei);
            yield return Say(vc, mei, SpeechEngine.VoiceMale, "美，你好！今天天气很好。");
            yield return WaitIdle(mei);
            Check(VocabNotebook.Entries.Count > 0, $"Mei taught words ({VocabNotebook.Entries.Count} in the notebook)");

            // Market: buy a bamboo rod from 老王 in spoken Mandarin.
            Inventory.Earn(400);
            var tackle = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "tackle");
            Check(vc.CurrentTarget == (DialogueAgent)mei, "V talks to Mei until a shop conversation is started");
            yield return GoToShop(player, cam, 0, tackle);
            Check(vc.CurrentTarget == tackle, "V talks to 老王 during the conversation");
            yield return WaitIdle(tackle, 30f);
            Check(ConversationLog.Entries.Skip(ShopConversation.StartedAtLogIndex).Any(e => e.speaker == tackle.DisplayName), "老王 greets you when you start talking");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "老板，我想买红线。");
            yield return new WaitForSeconds(1.2f);
            yield return Shot("04_shop_offer");
            Check(tackle.PendingOffer != null && tackle.PendingOffer.itemId == "line_red", "老王 offers the red line");
            yield return WaitIdle(tackle);

            // B: a quiet side chat with Mei about what 老王 just said; the conversation carries on afterwards.
            yield return Say(vc, mei, SpeechEngine.VoiceEnglish, "Mei, what did he just say?");
            yield return new WaitForSeconds(1f);
            yield return Shot("04b_ask_mei");
            yield return WaitIdle(mei);
            string aside = ConversationLog.Entries.LastOrDefault(e => e.speaker == mei.DisplayName).text ?? "";
            Check(aside.Length > 0, $"Mei answers the side question: \"{aside}\"");
            Check(ReadShared(ChatAudit.SessionFile).Contains("side chat while talking with"), "Mei got the shop conversation as context");
            Check(ShopConversation.Active == tackle && tackle.PendingOffer != null, "asking Mei doesn't end the conversation or the offer");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "好的，要！");
            yield return WaitIdle(tackle);
            Check(Inventory.Owns("line_red") && Inventory.LineKg >= 6f, $"bought the red line: fishing with a {Inventory.LineKg:0} kg line (money now ¥{Inventory.Money})");

            yield return Say(vc, tackle, SpeechEngine.VoiceEnglish, "Can I also buy some worms please?");
            yield return WaitIdle(tackle);
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "我要两包蚯蚓。");
            yield return new WaitForSeconds(0.5f);
            Check(tackle.PendingOffer != null && tackle.PendingOffer.itemId == "bait_worm", "老王 offers two packs of worms");
            yield return WaitIdle(tackle);
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "好的，要。");
            yield return WaitIdle(tackle);
            Check(Inventory.Count("bait_worm") >= 20, $"worms in the bag ({Inventory.Count("bait_worm")})");
            Check(Inventory.SlotCapacity == 4 && Inventory.SlotsUsed == 0, $"the bag starts with 4 slots, and worms don't use one ({Inventory.SlotsUsed} used)");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "老板，我想买大水桶。");
            yield return WaitIdle(tackle);
            Check(tackle.PendingOffer != null && tackle.PendingOffer.itemId == "bucket_big", "Old Wang offers the big bucket");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "好的，我要。");
            yield return WaitIdle(tackle);
            Check(Inventory.SlotCapacity == 8, $"the big bucket gives 8 bag slots (now {Inventory.SlotCapacity})");

            // Fish shop: sell a bucket of fish.
            Inventory.AddToBucket(FishDatabase.Get("seabass"), 3.2f);
            Inventory.AddToBucket(FishDatabase.Get("seabass"), 1.4f);
            Inventory.AddToBucket(FishDatabase.Get("mackerel"), 0.8f);
            int moneyBefore = Inventory.Money;
            var fishShop = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "fish");
            yield return GoToShop(player, cam, 1, fishShop);
            yield return WaitIdle(fishShop, 30f);
            yield return Say(vc, fishShop, SpeechEngine.VoiceMale, "阿姨，我想卖鱼。");
            yield return new WaitForSeconds(1.2f);
            yield return Shot("05_sell_fish");
            yield return WaitIdle(fishShop);
            yield return Say(vc, fishShop, SpeechEngine.VoiceMale, "可以。");
            yield return WaitIdle(fishShop);
            Check(Inventory.BucketCount == 0 && Inventory.Money > moneyBefore, $"sold the fish (+¥{Inventory.Money - moneyBefore})");

            // Pet shop: food bowl + cat food.
            var petShop = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "pet");
            yield return GoToShop(player, cam, 3, petShop);
            yield return WaitIdle(petShop, 30f);
            yield return Say(vc, petShop, SpeechEngine.VoiceMale, "你好，我要买一个猫碗。");
            yield return WaitIdle(petShop);
            yield return Say(vc, petShop, SpeechEngine.VoiceMale, "好的，我要。");
            yield return WaitIdle(petShop);
            Check(Inventory.Count("cat_bowl") > 0, "bought a cat bowl");

            yield return StatsChecks(vc, player, cam, ui);
            Inventory.Buy("cat_food", 1);
            Inventory.Buy("cat_treat", 1);
            yield return new WaitForSeconds(1f);
            ui.ToggleBag();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("06_bag");
            ui.ToggleBag();

            // Home: place the bowl by the camp, fill it, pet Tangyuan.
            Vector2 c = WorldShape.CampCenter + new Vector2(2.5f, 6f);
            var homeSpot = new Vector3(c.x, WorldShape.TerrainHeight(c.x, c.y), c.y);
            player.Teleport(homeSpot + new Vector3(-1.5f, 0.1f, -1.5f), 45f);
            cam.Configure(player.transform, 45f, 30f, 7f);
            yield return new WaitForSeconds(0.5f);
            if (Inventory.Count("cat_bowl") > 0) HomeItems.Instance.Place("cat_bowl", homeSpot, 0f);
            var bowl = HomeItems.Instance.Find("cat_bowl");
            Check(bowl != null, "placed the cat bowl at the camp");
            if (bowl != null) bowl.GetComponent<BowlInteractable>()?.Interact();
            Check(SaveSystem.Data.pet.bowlFilled, "filled Tangyuan's bowl");
            var pet = FindFirstObjectByType<PetController>();
            pet.transform.position = homeSpot + new Vector3(1.2f, 0f, 1.2f);
            float hungerBefore = SaveSystem.Data.pet.hunger = 0.8f;
            yield return new WaitForSeconds(0.3f);
            pet.Interact(); // feeds her (cat food) until she's full
            Check(SaveSystem.Data.pet.hunger < hungerBefore, "fed Tangyuan");
            Check(SaveSystem.Data.pet.fullDay == DayNightCycle.Instance.Day, "feeding her until full is remembered");
            int baitBefore = Inventory.Owned().Where(x => x.def.category == ItemCategory.Bait).Sum(x => x.count);
            SaveSystem.Data.pet.fullDay = DayNightCycle.Instance.Day - 1; // pretend that was yesterday
            yield return new WaitForSeconds(0.6f);
            int baitAfter = Inventory.Owned().Where(x => x.def.category == ItemCategory.Bait).Sum(x => x.count);
            Check(baitAfter > baitBefore && SaveSystem.Data.pet.giftDay == DayNightCycle.Instance.Day, $"the next day Tangyuan brought bait ({baitAfter - baitBefore} pieces)");
            var picks = Enumerable.Range(0, 400).Select(_ => PetController.PickBaitGift().bait?.id).ToList();
            Check(picks.Count(p => p == "bait_worm") > picks.Count(p => p == "bait_shrimp"), $"cheaper baits are brought more often (worms {picks.Count(p => p == "bait_worm")}, shrimp {picks.Count(p => p == "bait_shrimp")} of 400)");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("07_tangyuan");

            ui.ToggleNotebook();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("08_notebook");
            ui.ToggleNotebook();

            yield return CabinChecks(player, cam);

            // Market overview.
            Vector2 m = WorldShape.MarketCenter;
            player.Teleport(new Vector3(m.x - 9f, WorldShape.TerrainHeight(m.x - 9f, m.y) + 0.05f, m.y), 90f);
            cam.Configure(player.transform, 90f, 24f, 9f);
            yield return new WaitForSeconds(1.5f);
            yield return Shot("09_market");

            yield return FishingRound(fishing, player, cam);

            DayNightCycle.Instance.TimeOfDay = 22.5f;
            yield return new WaitForSeconds(3f);
            yield return Shot("12_night");
            DayNightCycle.Instance.TimeOfDay = 10f; // back to morning (at 2am the player would pass out)

            Log($"Notebook ({VocabNotebook.Entries.Count} words): " + string.Join(", ", VocabNotebook.Entries.Select(v => $"{v.hanzi} {v.pinyin} [{v.meaning}] used {v.said}x")));
            Log($"Conversation log ({ConversationLog.Entries.Count} lines):");
            foreach (var e in ConversationLog.Entries) Log($"   {e.speaker}: {e.text}");
            yield return LongConversationChecks();
            Log("--- newer sections: " + string.Join(", ", ExtraSections) + " ---");
            yield return RunSections(ExtraSections);
            DayNightCycle.Instance.TimeOfDay = 10f;
            yield return DayChecks(player, cam);
            yield return SaveLoadChecks();
            Log(_failures == 0 ? "SELFTEST COMPLETE: all checks passed" : $"SELFTEST COMPLETE: {_failures} check(s) failed");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>
        /// Regression tests for a real session: after a long chat Mei's prompt passed the model's 4096-token context,
        /// every request failed (HTTP 400) and she only said "Hm? Could you say that again?". Also: saying 喂汤圆
        /// must actually feed Tangyuan, even without a bowl.
        /// </summary>
        private IEnumerator LongConversationChecks()
        {
            Log("--- long conversation / feeding ---");
            var vc = FindFirstObjectByType<VoiceChatController>();
            var mei = FindFirstObjectByType<CompanionBrain>();
            var player = FindFirstObjectByType<PlayerController>();
            yield return WaitIdle(mei);

            // ~40 turns with long notes, far more than 4096 tokens.
            var memory = new AgentMemory { agent = mei.MemoryKey };
            string filler = string.Concat(System.Linq.Enumerable.Repeat("今天天气很好，我们去钓鱼吧。我想买鱼竿，也想卖鱼。汤圆在哪里？", 8));
            for (int i = 0; i < 11; i++)
            {
                memory.messages.Add(new SavedMessage { role = "user", content = $"[Game: The player is chatting while fishing on a calm afternoon, turn {i}.] {filler} Tell me more about the lake, the market, the fish and the shopkeepers, please." });
                memory.messages.Add(new SavedMessage { role = "assistant", content = "The lake is lovely today! At the market, 老王 sells rods and bait, and 陈阿姨 buys fish. You can say 我想卖鱼 [I want to sell fish]. Tangyuan is napping by the cabin." });
            }
            mei.ImportMemory(memory);
            int est = 0;
            foreach (var m in memory.messages) est += DialogueAgent.EstimateTokens(m.content);
            Log($"   stuffed Mei's memory with {memory.messages.Count} messages (~{est} tokens)");
            int before = ConversationLog.Entries.Count;
            yield return Say(vc, mei, SpeechEngine.VoiceEnglish, "Mei, what should we do next?");
            yield return WaitIdle(mei, 90f);
            string reply = ConversationLog.Entries.Count > before ? ConversationLog.Entries[ConversationLog.Entries.Count - 1].text : "";
            Check(reply.Length > 0 && !reply.Contains("Could you say that again"), $"Mei still answers after a very long conversation: \"{reply}\"");
            Check(ReadShared(ChatAudit.SessionFile).Contains("to fit the context"), "the oldest messages were forgotten to fit the context");
            // Trimming happens in one chunk (down to ~40% of the room), so the next turns can reuse the server's prompt cache.
            var trim = System.Text.RegularExpressions.Regex.Matches(ReadShared(ChatAudit.SessionFile), @"Mei +forgot the \d+ oldest messages to fit the context \(~(\d+) -> ~(\d+) tokens, budget (\d+)\)");
            if (trim.Count > 0)
            {
                var g = trim[trim.Count - 1].Groups;
                int after = int.Parse(g[2].Value), budget = int.Parse(g[3].Value);
                Check(after < budget * 0.75f, $"the history was trimmed in one chunk, well under the budget ({g[1].Value} -> {after} tokens, budget {budget})");
            }

            // Feeding by voice, with cat food and no bowl.
            var cat = UntitledGame.Home.PetController.Instance;
            if (cat == null)
            {
                Check(false, "Tangyuan found");
                yield break;
            }
            foreach (var placed in SaveSystem.Data.placed.ToArray())
                if (placed.id == "cat_bowl") HomeItems.Instance?.PutAway(placed);
            SaveSystem.Data.pet.bowlFilled = false;
            SaveSystem.Data.pet.hunger = 0.95f;
            Inventory.Add("cat_food", 2);
            // Bring Tangyuan to the player (the player stays on known-walkable ground).
            Vector3 near = player.transform.position + player.transform.forward * 1.5f;
            if (!PlayerController.IsWalkable(near)) near = player.transform.position - player.transform.forward * 1.5f;
            cat.transform.position = new Vector3(near.x, WorldShape.TerrainHeight(near.x, near.z), near.z);
            yield return new WaitForSeconds(1f);
            int food = Inventory.Count("cat_food");
            yield return Say(vc, mei, SpeechEngine.VoiceMale, "我想喂汤圆。");
            yield return WaitIdle(mei, 60f);
            Check(SaveSystem.Data.pet.hunger < 0.5f && Inventory.Count("cat_food") == food - 1,
                $"saying 喂汤圆 fed Tangyuan (hunger {SaveSystem.Data.pet.hunger:0.00}, cat food {food} -> {Inventory.Count("cat_food")})");
            Log("   Mei: " + ConversationLog.Entries[ConversationLog.Entries.Count - 1].text);
            yield return Shot("12b_fed_by_voice");
        }

        /// <summary>Save slots, a real scene reload, crash recovery, restore points and the chat log.</summary>
        private IEnumerator SaveLoadChecks()
        {
            Log("--- saves ---");
            var mei = FindFirstObjectByType<CompanionBrain>();
            yield return WaitIdle(mei);
            var player = FindFirstObjectByType<PlayerController>();
            Vector3 pos = player.transform.position;
            Log($"   player at ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}), walkable: {PlayerController.IsWalkable(pos)}");
            int money = Inventory.Money, words = VocabNotebook.Entries.Count, lines = ConversationLog.Entries.Count;
            int meiMemory = mei.ExportMemory().messages.Count;
            string slot1 = SaveSystem.SlotPath(1);

            SaveSystem.Save();
            SaveSystem.Save();
            Check(File.Exists(slot1) && File.Exists(slot1 + ".bak"), "slot 1 written, with a .bak of the previous save");
            var info = SaveSystem.Describe(1);
            Check(info.exists && info.money == money && info.words == words, $"slot 1 summary matches (¥{info.money}, {info.words} words, day {info.day})");

            SaveSystem.SaveInto(2);
            Check(SaveSystem.ActiveSlot == 2 && SaveSystem.Describe(2).exists, "'Save here' into slot 2 (now playing slot 2)");

            // Reload the whole scene through the same path as the Saves menu; this object and the AI survive it.
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            var services = LocalAIServices.Instance;
            var oldBoot = GameBootstrap.Instance;
            oldBoot.LoadSlot(2, fresh: false);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline && (GameBootstrap.Instance == null || GameBootstrap.Instance == oldBoot)) yield return null;
            yield return new WaitForSeconds(2f);

            player = FindFirstObjectByType<PlayerController>();
            mei = FindFirstObjectByType<CompanionBrain>();
            Check(GameBootstrap.Instance != null && GameBootstrap.Instance != oldBoot, "scene reloaded into slot 2");
            Check(LocalAIServices.Instance == services && services.LlmStatus == ServiceStatus.Ready && services.SttStatus == ServiceStatus.Ready,
                "AI services survived the reload (not restarted)");
            Check(Inventory.Money == money && VocabNotebook.Entries.Count == words, $"money and words restored (¥{Inventory.Money}, {VocabNotebook.Entries.Count} words)");
            float moved = player != null ? Vector3.Distance(player.transform.position, pos) : 999f;
            Check(moved < 1f, $"player is back where they were ({moved:0.00} m away)");
            Check(ConversationLog.Entries.Count == 0, $"the journal transcript starts fresh after loading ({ConversationLog.Entries.Count} lines)");
            Check(mei != null && mei.HasMemory && mei.ExportMemory().messages.Count > 0, $"Mei remembers the conversation ({mei.ExportMemory().messages.Count} of {meiMemory} messages)");

            // Mei should welcome the player back.
            deadline = Time.realtimeSinceStartup + 60f;
            int before = ConversationLog.Entries.Count;
            while (Time.realtimeSinceStartup < deadline && ConversationLog.Entries.Count == before) yield return null;
            yield return WaitIdle(mei);
            if (ConversationLog.Entries.Count > before) Log("   Mei (after loading): " + ConversationLog.Entries[ConversationLog.Entries.Count - 1].text);
            yield return Shot("13_after_load");

            // A crash mid-write: the slot file is garbage. The previous copy should be used, and the damaged file kept.
            SaveSystem.Save();
            SaveSystem.Save();
            string slot2 = SaveSystem.SlotPath(2);
            File.WriteAllText(slot2, "{ \"money\": 12, \"vocab\": [ {");
            SaveSystem.Load();
            Check(SaveSystem.LoadWarning != null && Inventory.Money == money, $"a damaged save falls back to the .bak (¥{Inventory.Money}; {SaveSystem.LoadWarning})");
            Check(Directory.GetFiles(SaveSystem.SavesFolder, "slot2.json.damaged-*").Length > 0, "the damaged file was kept aside, not deleted");
            SaveSystem.Save();

            var points = SaveSystem.RestorePoints(1);
            Check(points.Count > 0, $"restore points exist for slot 1 ({points.Count}, newest: {(points.Count > 0 ? points[0].reason : "-")})");

            string logFile = ChatAudit.SessionFile;
            string text = ReadShared(logFile);
            Check(text.Contains("MIC→") && text.Contains("heard \""), "chat log records what the microphone heard");
            Check(text.Contains("SYSTEM PROMPT") && text.Contains("reply: first token"), "chat log records prompts and replies with timings");
            Check(text.Contains("understood (rules)") && text.Contains("OFFER") && text.Contains("SALE"), "chat log records how keepers understood lines, offers and sales");
            Check(text.Contains("Loaded slot 2"), "chat log marks the slot load");
            string audio = ChatAudit.SessionFolder != null ? Path.Combine(ChatAudit.SessionFolder, "audio") : null;
            int wavs = audio != null && Directory.Exists(audio) ? Directory.GetFiles(audio, "*.wav").Length : 0;
            Check(wavs > 0, $"voice recordings saved next to the log ({wavs})");
            Log("Chat log: " + logFile);

            var ui = FindFirstObjectByType<GameUI>();
            ui.OpenSaves();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("14_saves_menu");
            ui.CloseSaves();
        }

        /// <summary>Reads a file the game still has open for writing (the chat log).</summary>
        private static string ReadShared(string path)
        {
            if (path == null || !File.Exists(path)) return "";
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var r = new StreamReader(fs);
            return r.ReadToEnd();
        }

        private IEnumerator FishingRound(FishingController f, PlayerController player, CameraRig cam)
        {
            Vector2 end = WorldShape.DockShorePoint - WorldShape.DockDirection * (WorldShape.DockLength - 1f);
            player.Teleport(new Vector3(end.x, WorldShape.DockDeckHeight + 0.05f, end.y), 0f);
            cam.Configure(player.transform, 0f, 24f, 8.5f);
            yield return new WaitForSeconds(2f);

            int wormsBefore = Inventory.Count("bait_worm");
            int meiBefore = ConversationLog.Entries.Count(e => e.speaker == CompanionPersona.Name);
            f.SimulateHold = true;
            f.SimulateClick();
            yield return new WaitForSeconds(0.55f);
            f.SimulateHold = false;
            float t = Time.time;
            while (f.State != FishingState.Waiting && f.State != FishingState.Idle && Time.time - t < 5f) yield return null;
            yield return new WaitForSeconds(1.5f);
            yield return Shot("10_waiting");
            f.DebugNextSpecies = "goby"; // the easiest starter fish: the test bot is crude, and this checks the flow
            f.DebugBiteNow();
            t = Time.time;
            while (f.State != FishingState.Bite && Time.time - t < 5f) yield return null;
            f.SimulateClick();
            yield return null;
            t = Time.time;
            bool shotTaken = false;
            while (f.State == FishingState.Reeling && Time.time - t < 45f)
            {
                // Play the minigame: hold to lift the green bar when the fish is above its middle.
                f.SimulateHold = f.FishPos > f.BarPos + f.BarSize * 0.5f;
                if (!shotTaken && Time.time - t > 1.2f)
                {
                    shotTaken = true;
                    yield return Shot("10c_minigame");
                }
                yield return null;
            }
            f.SimulateHold = false;
            Check(f.LastCatch != null && f.LastCatch.inBucket, $"won the reeling minigame: a {f.LastCatch?.species.name} of {FishDatabase.WeightText(f.LastCatch?.length ?? 0f)} into the bucket");
            Check(Inventory.Count("bait_worm") == wormsBefore - 1, "a worm was used as bait");
            yield return new WaitForSeconds(6f);
            int meiAfter = ConversationLog.Entries.Count(e => e.speaker == CompanionPersona.Name);
            Check(meiAfter == meiBefore, $"Mei stays quiet when you catch a fish (only speaks when spoken to: {meiAfter - meiBefore} new lines)");
            if (f.LastCatch != null && f.LastCatch.species.IsFish)
                Check(f.LastCatch.length >= f.LastCatch.species.minWeight && f.LastCatch.length <= f.LastCatch.species.maxWeight, "the catch weighs within its kind's range");
            yield return new WaitForSeconds(0.9f);
            yield return Shot("11_landed");
            yield return new WaitForSeconds(2f);
            f.SimulateClick();
            yield return new WaitForSeconds(1f);
        }
    }
}
