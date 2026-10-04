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
            var services = LocalAIServices.Instance;
            var vc = FindFirstObjectByType<VoiceChatController>();
            var player = FindFirstObjectByType<PlayerController>();
            var cam = FindFirstObjectByType<CameraRig>();
            var ui = FindFirstObjectByType<GameUI>();
            while (!Pinyin.Ready) yield return null;
            yield return new WaitForSeconds(1f);
            foreach (var section in sections.Select(s => s.Trim().ToLowerInvariant()))
            {
                switch (section)
                {
                    case "parser":
                        ParserChecks();
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
            Log(_failures == 0 ? "SELFTEST COMPLETE: all checks passed" : $"SELFTEST COMPLETE: {_failures} check(s) failed");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
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
            Check(Mathf.Approximately(Energy.CastCost, 10f) && FishingController.BarScale < 1f, $"a cast costs {Energy.CastCost:0} energy; green bars are {FishingController.BarScale * 100f:0}% of their old size");
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
            // A new game only knows the sardine (junk can always be fished up).
            Check(PlayerStats.DiscoveredFishCount == 1 && PlayerStats.IsDiscovered(FishDatabase.Get("sardine")), $"a new game knows only the sardine ({PlayerStats.DiscoveredFishCount} fish known)");
            var ctx = new CatchContext { hour = 8f, shoreDistance = 20f, lineKg = 80f, bait = Catalog.Get("bait_worm") };
            bool onlyKnown = Enumerable.Range(0, 300).Select(_ => FishDatabase.Roll(ctx)).All(f => f != null && f.id == "sardine");
            Check(onlyKnown, "only discovered fish bite, and only fish (300 rolls: all sardines)");
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
            Check(found.Count == 4 && PlayerStats.IsDiscovered(FishDatabase.Get("mackerel")), $"reading it discovered {found.Count} fish ({string.Join(", ", found.Select(f => f.name))})");
            // Every requirement has to be met: distance, bait, line, time.
            var mackerel = FishDatabase.Get("mackerel");
            var near = new CatchContext { hour = 9f, shoreDistance = 3f, lineKg = 3f, bait = Catalog.Get("bait_worm") };
            var noBait = new CatchContext { hour = 9f, shoreDistance = 10f, lineKg = 3f, bait = null };
            var thinLine = new CatchContext { hour = 9f, shoreDistance = 10f, lineKg = 1f, bait = Catalog.Get("bait_worm") };
            var night = new CatchContext { hour = 23f, shoreDistance = 10f, lineKg = 3f, bait = Catalog.Get("bait_worm") };
            var ok = new CatchContext { hour = 9f, shoreDistance = 10f, lineKg = 3f, bait = Catalog.Get("bait_worm") };
            Check(FishDatabase.Missing(mackerel, near) != null && FishDatabase.Missing(mackerel, noBait) != null && FishDatabase.Missing(mackerel, thinLine) != null &&
                  FishDatabase.Missing(mackerel, night) != null && FishDatabase.Missing(mackerel, ok) == null,
                $"mackerel needs 6 m out, worms or shrimp, a 2 kg line and daylight ({FishDatabase.Missing(mackerel, near)}; {FishDatabase.Missing(mackerel, noBait)}; {FishDatabase.Missing(mackerel, thinLine)})");
            Check(PlayerStats.Knowledge == 1, "fishing knowledge is 1 book");

            var gym = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "gym");
            yield return GoToShop(player, cam, 5, gym);
            Check(ui.Shop.Keeper == gym, "walking to Coach Wu switches the shop window to the trainer");
            yield return WaitIdle(gym, 30f);
            float castBefore = PlayerStats.CastDistance;
            yield return Say(vc, gym, SpeechEngine.VoiceMale, "教练，我要力量训练。");
            yield return WaitIdle(gym);
            Check(gym.PendingOffer != null && gym.PendingOffer.itemId == "up_cast", "asking for 力量训练 gets a strength-training offer");
            yield return Say(vc, gym, SpeechEngine.VoiceMale, "好的，我要。");
            yield return WaitIdle(gym);
            Check(PlayerStats.Level("cast") == 1 && PlayerStats.CastDistance > castBefore, $"strength training: cast distance {castBefore:0.#} -> {PlayerStats.CastDistance:0.#} m");
            Check(Catalog.PriceOf(Catalog.Get("up_cast")) == Catalog.Get("up_cast").price * 2, "the next level costs more");
            ShopConversation.Instance.End(sayGoodbye: false);
            yield return FriendshipChecks(vc, player, cam, ui);
        }

        /// <summary>Friendship scoring, the gift shop, giving a liked gift, asking about a keeper, locked goods, and Mei as encyclopedia.</summary>
        private IEnumerator FriendshipChecks(VoiceChatController vc, PlayerController player, CameraRig cam, GameUI ui)
        {
            // Scoring: harder, longer lines are worth more; repeats are worth nothing.
            var easy = Affinity.ScoreLine("furniture", "你好", 0);
            var hard = Affinity.ScoreLine("furniture", "我觉得你的椅子非常漂亮，因为颜色很好看", 0);
            var again = Affinity.ScoreLine("furniture", "我觉得你的椅子非常漂亮，因为颜色很好看", 0);
            Check(hard.points > easy.points && easy.points >= 1 && again.points == 0,
                $"friendship scoring: 你好 +{easy.points} ({easy.why}); a long HSK 3 line about chairs +{hard.points} ({hard.why}); repeating it +{again.points}");
            Check(ShopIntentParser.FactQuestion("你是哪里人？") == "hometown" && ShopIntentParser.FactQuestion("你喜欢什么？") == "like" &&
                  ShopIntentParser.FactQuestion("你喜欢做什么？") == "hobby" && ShopIntentParser.FactQuestion("我想买鱼竿") == null, "questions about a keeper are recognised");

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
            int before = Affinity.Points("tackle");
            Check(!Affinity.Knows("tackle", "like:茶"), "Old Wang's likes aren't known yet");
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "老王，这是送给你的茶。");
            yield return WaitIdle(tackle);
            Check(Inventory.Count("gift_tea") == 0 && Affinity.Points("tackle") >= before + Affinity.GiftLiked, $"giving tea (he likes it) raised friendship {before} -> {Affinity.Points("tackle")}");
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
            Check(teacher != null && Catalog.Shops.IndexOf(teacher.Shop) == 8 && WorldShape.StallCount == 9, "the test centre is the 9th stall, with 高老师");
            if (teacher == null) yield break;

            // Before any test: Old Wang won't sell the blue line even to a friend.
            var tackle = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "tackle");
            Affinity.Add("tackle", 40, "self-test");
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
            int money = Inventory.Money;
            while (HskSchool.Current == lesson)
            {
                teacher.HandlePlayerUtterance(lesson.Current.word.hanzi);
                yield return null;
            }
            yield return WaitIdle(teacher, 20f);
            Check(Hsk.LessonDone(1, 1) && Inventory.Money == money + Hsk.LessonReward[1], $"passed lesson 1 (quiz {lesson.RecallRight}/{lesson.RecallAsked}) and got ¥{Inventory.Money - money}");
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
            int bonus = Catalog.FishPrice(sea, 3f);
            Check(Mathf.Abs(Catalog.FishHskBonus - 0.1f) < 0.001f && bonus > plain, $"after HSK 1 fish sell for 10% more, automatically (a 3 kg sea bass: ¥{plain} -> ¥{bonus})");

            // Unlocked: the blue line (friend + HSK 1).
            tackle.HandlePlayerUtterance("老板，我要蓝线。");
            Check(tackle.PendingOffer != null && tackle.PendingOffer.itemId == "line_blue", "after HSK 1, Old Wang offers the blue line");
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
        /// Energy, the longer day, sleeping in bed, passing out (keeping only 3 slots), the bag limit, currents, the island
        /// camp and cosmetics.
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

            // The island: with the best boat, camp and sleep there.
            Inventory.Add("boat_new", 1);
            Check(Rowboat.Range >= 140f, $"the new boat can go {Rowboat.Range:0} m out (the island is at {WorldShape.IslandCenter.y - WorldShape.CoastZ:0} m)");
            var camp = IslandCamp.Instance;
            Check(camp != null, "the island has a camp spot");
            if (camp != null)
            {
                player.Teleport(camp.transform.position + Vector3.up * 0.2f, 0f);
                cam.Configure(player.transform, 200f, 25f, 9f);
                yield return new WaitForSeconds(0.5f);
                Check(PlayerController.IsWalkable(player.transform.position), "you can walk on the island");
                camp.Interact(); // set up camp
                Check(camp.IsSetUp, "set up camp on the island");
                yield return new WaitForSeconds(1f);
                yield return Shot("15_island_camp");
                dn.TimeOfDay = 22f;
                day = dn.Day;
                camp.Interact(); // sleep in the tent
                yield return new WaitForSeconds(0.5f);
                while (SleepSystem.Instance.Busy) yield return null;
                Check(dn.Day == day + 1 && WorldShape.OnIsland(player.transform.position.x, player.transform.position.z), $"slept in the tent and woke on the island (day {dn.Day}, {dn.ClockText})");
            }

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
            yield return Say(vc, mei, SpeechEngine.VoiceEnglish, "Mei, how do I ask Old Wang for a fishing rod in Chinese?");
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
