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
            StartCoroutine(Run());
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
            Expect(tackle, "老板我想买猪鱼竿", "buy item=rod_bamboo qty=1");
            Expect(tackle, "我要两包蚯蚓", "buy item=bait_worm qty=2");
            Expect(tackle, "碳素鱼竿多少钱", "ask_price item=rod_carbon");
            Expect(tackle, "好的咯", "confirm", pending: true);
            Expect(tackle, "不要了，谢谢", "decline", pending: true);
            Expect(fish, "大爷我想卖鱼", "sell_fish");
            Expect(fish, "阿姨，我想卖三条鲤鱼", "sell_fish item= qty=1 fish=carp");
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
            string worms = Catalog.Counted(Catalog.Get("bait_worm"), 2), rod = Catalog.Counted(Catalog.Get("rod_bamboo"), 1);
            Check(worms == "两包蚯蚓" && rod == "一根竹鱼竿", $"counting words: {worms}, {rod}");
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

        private IEnumerator GoToShop(PlayerController player, CameraRig cam, int stall)
        {
            Vector2 spot = WorldShape.CustomerSpot(stall);
            Vector2 stallPos = WorldShape.StallPosition(stall);
            Vector3 p = new Vector3(spot.x, WorldShape.TerrainHeight(spot.x, spot.y) + 0.05f, spot.y);
            Vector2 dir = (stallPos - spot).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            player.Teleport(p, yaw);
            cam.Configure(player.transform, yaw, 18f, 7f);
            yield return new WaitForSeconds(1.5f);
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
            yield return GoToShop(player, cam, 0);
            Check(vc.CurrentTarget == tackle, "V targets 老王 when standing at his stall");
            yield return WaitIdle(tackle, 30f);
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "老板，我想买竹鱼竿。");
            yield return new WaitForSeconds(1.2f);
            yield return Shot("04_shop_offer");
            Check(tackle.PendingOffer != null && tackle.PendingOffer.itemId == "rod_bamboo", "老王 offers the bamboo rod");
            yield return WaitIdle(tackle);
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "好的，要！");
            yield return WaitIdle(tackle);
            Check(Inventory.Owns("rod_bamboo") && Inventory.Rod.id == "rod_bamboo", $"bought and equipped the bamboo rod (money now ¥{Inventory.Money})");

            yield return Say(vc, tackle, SpeechEngine.VoiceEnglish, "Can I also buy some worms please?");
            yield return WaitIdle(tackle);
            yield return Say(vc, tackle, SpeechEngine.VoiceMale, "我要两包蚯蚓。");
            yield return new WaitForSeconds(0.5f);
            Check(tackle.PendingOffer != null && tackle.PendingOffer.itemId == "bait_worm", "老王 offers two packs of worms");
            if (tackle.PendingOffer != null) tackle.AnswerOffer(true); // nod via the offer card
            yield return WaitIdle(tackle);
            Check(Inventory.Count("bait_worm") >= 20, $"worms in the bag ({Inventory.Count("bait_worm")})");

            // Fish shop: sell a bucket of fish.
            Inventory.AddToBucket(FishDatabase.Get("carp"), 55f);
            Inventory.AddToBucket(FishDatabase.Get("carp"), 40f);
            Inventory.AddToBucket(FishDatabase.Get("perch"), 28f);
            int moneyBefore = Inventory.Money;
            var fishShop = ShopkeeperBrain.Keepers.First(k => k.Shop.id == "fish");
            yield return GoToShop(player, cam, 1);
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
            yield return GoToShop(player, cam, 3);
            yield return WaitIdle(petShop, 30f);
            yield return Say(vc, petShop, SpeechEngine.VoiceMale, "你好，我要买一个猫碗。");
            yield return WaitIdle(petShop);
            if (petShop.PendingOffer != null) petShop.AnswerOffer(true);
            yield return WaitIdle(petShop);
            Check(Inventory.Count("cat_bowl") > 0, "bought a cat bowl");
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
            pet.Interact(); // gives a treat
            Check(SaveSystem.Data.pet.hunger < hungerBefore, "gave Tangyuan a treat");
            yield return new WaitForSeconds(1.5f);
            yield return Shot("07_tangyuan");

            ui.ToggleNotebook();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("08_notebook");
            ui.ToggleNotebook();

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

            Log($"Notebook ({VocabNotebook.Entries.Count} words): " + string.Join(", ", VocabNotebook.Entries.Select(v => $"{v.hanzi} {v.pinyin} [{v.meaning}] used {v.said}x")));
            Log($"Conversation log ({ConversationLog.Entries.Count} lines):");
            foreach (var e in ConversationLog.Entries) Log($"   {e.speaker}: {e.text}");
            yield return LongConversationChecks();
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
            Check(ConversationLog.Entries.Count == Mathf.Min(lines, 150), $"chat log restored ({ConversationLog.Entries.Count} lines)");
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
            while (f.State == FishingState.Reeling && Time.time - t < 45f)
            {
                f.SimulateHold = !f.FishPulling && f.Tension < 0.75f;
                yield return null;
            }
            f.SimulateHold = false;
            Check(f.LastCatch != null && f.LastCatch.inBucket, $"caught a {f.LastCatch?.species.name} into the bucket");
            Check(Inventory.Count("bait_worm") == wormsBefore - 1, "a worm was used as bait");
            yield return new WaitForSeconds(0.9f);
            yield return Shot("11_landed");
            yield return new WaitForSeconds(2f);
            f.SimulateClick();
            yield return new WaitForSeconds(1f);
        }
    }
}
