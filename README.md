# Willow Lake 🎣

A cozy low-poly fishing game in Unity 6 (URP) for learning Mandarin by talking. You fish off a little dock with your friend and tutor **Mei**, and look after your cat **汤圆 Tangyuan**. At the market you buy and sell things with the locals, speaking Chinese with your real voice. They talk back out loud.

- **All dialogue is in Mandarin.** The menus start in English and turn Chinese as you pass HSK tests.
- **The nine shopkeepers speak only Mandarin.** They don't understand English at all.
- **Mei only speaks when spoken to**, apart from saying hello when you open the game.
- **Mei is your only source of Chinese.** Ask her anything, in English or Chinese, and she teaches you exactly what you need, one phrase at a time: how to greet 老王, ask 多少钱, say 我想卖鱼, or buy 猫粮.
- **Pinyin is added by the game.** It comes from the CC-CEDICT dictionary, never from the AI, so it is always correct. The words Mei teaches go into your notebook.

Everything runs locally on your PC:

```
  hold V, speak ──► SenseVoice (speech → text, Mandarin) ──► llama.cpp + Qwen3.5-4B (streamed)
                                                                │ sentence by sentence
  they talk ◄── Kokoro / Piper voices (sherpa-onnx)  ◄──────────┘
```

## The market (southeast of the cabin)
| Stall | Keeper | Sells / buys |
|---|---|---|
| 渔具店 Tackle shop | 老王 Old Wang | **baits** (worms, dough, shrimp, squid, crabs, live baitfish, glow lure: each fish only bites on certain ones), **lines** (红/蓝/黑/金线: 6/15/40/80 kg, for heavier fish), his rowboat, a big bucket, a fancy float |
| 鱼店 Fish market | 陈阿姨 Auntie Chen | **buys your fish**. Rarer and bigger fish pay more, and passing HSK tests adds +10% / +20% / +35% automatically |
| 家具店 Furniture shop | 李师傅 Master Li | chairs, tables, a sofa, lamps, plants, a rug, a bookcase, a radio, a teddy bear… for your camp |
| 宠物店 Pet shop | 小林 Xiao Lin | cat food, dried-fish treats, a bowl, a bed, a box, yarn, a scratching post, a bell collar for Tangyuan |
| 书店 Bookshop | 周老师 Teacher Zhou | fishing books. **Reading one (bag → Read) teaches you new fish**: how far out they swim, what bait they like, what line they need. A new game only knows the sardine |
| 颜色店 Colour shop | 小方 Xiao Fang | cosmetics: boat paint, hats for Tangyuan, sun hats for Mei, roof colours for the house |
| 礼品店 Gift shop | 刘奶奶 Granny Liu | gifts for the shopkeepers (flowers, tea, coffee, cake, fruit, sweets, a hat, an umbrella, a watch...). She knows what everyone likes, if you ask her |
| 健身房 Fitness trainer | 武教练 Coach Wu | six trainings, five levels each: 力量 (cast further), 眼睛 (wider green bar), 跑步 (fish escape slower), 运气 (bonus fish), 技术 (heavier fish), 安静 (faster bites). Friends can train higher |
| 考试中心 Test centre | 高老师 Teacher Gao | **lessons, free practice and HSK 1–3 tests** (see below). Nothing for sale |

**Unlocking goods takes two things:** enough friendship with the seller, and (for the better goods) a passed HSK test. The shop window shows what each locked item needs.

How shopping works:
1. Walk up to a stall, face the keeper and press **E**. They greet you and the **shop window** opens on the right with everything they sell (or, at the fish market, your catch). Hold **V** and say something like 老板，我想买竹鱼竿 or 阿姨，我想卖鱼, **Everything with shopkeepers is spoken**: the shop window only lists the wares (and your friendship); there are no buy buttons, and typing (T) only goes to Mei.
   - Stuck? Hold **B** to ask Mei quietly, in English if you like: "what did he just say?" or "how do I say I want the cheaper one?". She knows the whole conversation so far. The keeper waits, and the offer stays open.
   - Press **E** again, say 再见, or just walk away to leave.
2. The game works out what you asked for:
   - Clear lines are read by rules. Item names are matched by sound, so a slightly wrong tone still works.
   - Unclear lines go to a small AI classifier.
3. The game applies the real prices and your real money. The keeper tells you the price and asks 要吗？.
4. Say 要 or 可以 to accept, or 不要了 to decline. You can also press **Y** or **X** on the offer card.

The keepers can't invent prices or give things away: the game decides, and they just narrate it.

## Lessons and HSK tests (高老师's test centre)
Everything is spoken. Press **E** at the test centre and say:
- **我想上课**: the next lesson. You repeat each of its words after her (the window shows the characters, pinyin and English), then she quizzes you on 8 of them from English. Get 6 right to pass. **Passing a lesson the first time pays** ¥40 (HSK 1), ¥60 (HSK 2) or ¥70 (HSK 3). Say 第三课 to pick a lesson.
- **我想练习**: free practice with 10 random words, including ones you got wrong before. It never pays.
- **我想考试**: the next HSK test. She asks 20 words from English, and you need 15. Take it as often as you like. After each answer the window shows what it heard (with pinyin): press **Y** if that's what you said, or **N** (or just say it again) to retry.

When a lesson or test ends, a big banner says whether you passed, and Teacher Gao's window keeps the result with the words you missed.

In a lesson, 不知道 skips a word, 不学了 stops, and walking away ends it.

| Level | Lessons | Words | Passing the test unlocks |
|---|---|---|---|
| HSK 1 | 11, by theme | 150 | fish +10%, blue line, squid & crab bait, good oars, huge bucket, *Fish under the Rocks*, double bed, sofa, radio, new cosmetics |
| HSK 2 | 10 | 147 | fish +20%, black line, live baitfish, sail, giant bucket, *Fish of the Open Sea*, big bed, new cosmetics |
| HSK 3 | 20 | 298 | fish +35%, gold line, glow lure, new boat (the island), *Legends of the Sea*, golden cosmetics |

The lessons of each level cover every word of it once. HSK 1's are grouped by theme: hello and thank you, numbers and money, family and people, time and dates, food and shopping, places, things around you, everyday actions, describing things, questions, and little words. Answers are checked by the game, not the AI. The word itself counts, and so does anything heard with the same pinyin ignoring tones, in any of its readings (喝 heard as 和, 菜 as 才). The consonants and vowels still have to be right (书 shū isn't 十 shí). In a lesson you can also say "skip" in English.

**More Chinese as you go.** Each interface label has an HSK level. At that level it shows both languages, and after the next test only the Chinese. From HSK 1 the clock, day, money and bag are in Chinese (上午 10:20, 第3天, 2651块, 鱼 2/8), and later the hints, tabs and window titles follow.

## Quick start

1. **On a fresh clone, run the bootstrap once.** It takes about 15 minutes and needs Unity 6000.0.68f1 from Unity Hub.
   ```powershell
   powershell -ExecutionPolicy Bypass -File Tools/bootstrap.ps1
   ```
   The repo only contains code and settings. The big binaries are gitignored and re-created by this script:
   - `Tools/fetch-assets.ps1` downloads the free models, audio, fonts and the CC-CEDICT dictionary (about 75 MB).
   - `Tools/setup-local-ai.ps1` downloads llama.cpp, Qwen3.5-4B, sherpa-onnx, the SenseVoice recogniser and four voices into `LocalAI/` (about 3.5 GB). Add `-SkipAI` to the bootstrap to skip this.
   - Unity runs headless to set up URP and TextMeshPro and to generate the textures, materials, meshes and the `WillowLake` scene.
2. **Open the project** in Unity **6000.0.68f1**. Open `Assets/Scenes/WillowLake.unity` and press **Play**.
   The HUD dots in the top right turn green when everything is loaded (about 10–20 s).
3. Or play the standalone build: `Builds/WillowLake/WillowLake.exe`. To make a new build, use **Untitled Game → Build Windows Player**.

## Controls
| | |
|---|---|
| **WASD** / **Shift** | walk / jog |
| **Right mouse drag**, **scroll** | look around, zoom |
| **Hold left mouse**, release | charge and cast (aim with the camera) |
| **Click** when the bobber dives | hook the fish |
| **Hold left mouse** (reeling) | lift the green bar; let go to drop it. Keep the fish inside until the meter fills (Stardew Valley style) |
| **E** or move | reel your line back in. **E while reeling cuts the line** (to save energy) |
| **E** (at a stall) | start / end a conversation with the shopkeeper |
| **Hold V** | talk to the shopkeeper you're in a conversation with, otherwise Mei |
| **Hold B** | always talk to Mei, also in hands-free mode. During a shop conversation it's a quiet side chat about it ("what did she say?", "how do I say…?") |
| **T** / **Enter** | type to Mei (shopkeepers are only ever spoken to) |
| **F** | interact: feed Tangyuan by hand (cat food or treats) or pet her, fill her bowl, get in / out of Old Wang's boat |
| **WASD** (in the boat) | row. Big fish live far from the shore |
| **I** | bag: money, bucket, rods (equip), bait (use), camp items (place / put away) |
| **N** | notebook: every word Mei has taught you, with pinyin, and how often you've used it |
| **J** | journal: fish, and **People** (what you've learned about each shopkeeper, likes/dislikes in Chinese) |
| **C** | conversation log, with pinyin |
| **H** | hide the HUD |
| **Esc** | menu: volumes, voices, **what Mei sounds like**, talking speed, **Mei's English**, **pinyin**, hands-free talking, day length, **Saves & logs** |
| **F5** | quick save |

**Energy and days.** Fishing uses energy: 10 per cast, and reeling drains more (heavy fish drain it fast). Sleep in your bed at any time to wake at 6am refreshed. At 2am or at zero energy you pass out: you keep only your 3 most valuable bag slots and wake at 10am at home. Better beds (Master Li) and furniture (comfort) give more energy. The waking day lasts 40 real minutes by default (Esc → Day length).

**The bag holds only fish** (each kind stacks in one slot). Everything else (bait, gifts, books, furniture, cards) is a key item and never takes space. You start with 4 slots; Old Wang's buckets give 8, 12 and 16. Only fish bite: there's no junk to fish up.

**The journal (J)** has four tabs: the fish you've caught, the people you've met (喜欢 / 不喜欢 in Chinese with pinyin only: ask Mei what they mean), a **phrasebook** (how friendship works, phrases for asking about people and giving gifts, and each shopkeeper's favourite topics), and a **transcript** of everything said with Mei and the shopkeepers. The transcript starts fresh each time you open the game.

**The boat and the island.** Past your boat's range the currents push you back; Old Wang sells upgrades. The best boat reaches an island far out where you can camp for several days.

The log cabin by the camp is yours: walk up to the door and it opens. Inside, the roof and the walls nearest the camera fade away so you can see in, and you can furnish it.

Placing furniture: open the bag (**I**) and choose **Place**. Then **left-click** to put it down, **R** or **scroll** to rotate, and **right-click** or **Esc** to cancel. To **move** something, stand next to it and press **F** (or choose **Move** in the bag).

**Gifts:** the shop window shows how to give one (say 这是送给你的… plus the gift), with the gifts you're carrying. One gift a day per shopkeeper.

### Learning settings (Esc)
- **Mei's English:**
  - *Beginner* (she explains in English and teaches Chinese words and phrases)
  - *Intermediate* (simple Mandarin, glossing new words)
  - *Immersion* (Mandarin only)
- **Pinyin:** *Always*, *New words only* or *Off*, for the speech bubbles and the chat log.

## Tangyuan 汤圆
- **Hunger and happiness:** she gets hungry over the day and happier when you pet her or give her treats.
- **Food:** buy cat food or treats at the pet shop. Then walk up to her and press **F**, or say **喂汤圆** ("feed Tangyuan") when she's close. A bowl at the camp (fill it with **F**) lets her eat by herself.
- **Where she goes:** she walks to her bowl when she's hungry and sleeps in her bed or box at night. When she's happy she follows you and plays with her yarn and scratching post. With the bell collar on, you hear her coming.

## Saves
The game saves by itself every 45 seconds, when you buy or sell something, and when you quit. Next time, you're back exactly where you left off:
- where you were standing and the time of day
- money, bag, bucket, placed furniture and Tangyuan
- the words in your notebook and the chat log
- what Mei and the shopkeepers remember of your conversations. Mei welcomes you back and reviews a word with you.

**Esc → Saves & logs** has three save slots:
- **Load** another slot, or start a **New game** in an empty one.
- **Save here** copies your current game into another slot, and you carry on playing there. This is handy before trying something.
- **Start over** replaces the current slot with a new game.

**Restore points** are snapshots of the current slot, taken automatically:
- when you start playing
- at the start of each in-game day
- before anything is overwritten

Choose **Go back to this** to rewind. Anything that replaces a game asks for a second click and makes a restore point first, so nothing is lost for good.

**Crash safety.** Saves are written to a temporary file and then swapped in, with the previous save kept as `.bak`. If a save file is ever damaged, the game loads the `.bak` instead and keeps the damaged file aside.

Files live in `%USERPROFILE%\AppData\LocalLow\Cozy Local Games\Willow Lake\`:
```
settings.json                      preferences (shared by all slots)
Saves/slot1.json … slot3.json      your games (+ .bak)
Saves/RestorePoints/               automatic snapshots (12 newest per slot)
ChatLogs/<date_time>/chat.log      one folder per play session (see below)
```
A save from before slots existed (`willowlake_save.json`) is copied into slot 1 automatically.

## Chat logs (for auditing)
Every play session writes a readable log to `ChatLogs/<date_time>/chat.log`. Open it from **Esc → Saves & logs → Open chat logs**. It records:
- **What the microphone heard.** You get the recognised text, the recogniser's raw output, the recording length, the level and how long recognition took. Each voice line is also saved as a WAV in `audio/`, so you can listen to what was actually said.
- **Exactly what each character was sent.** The system prompt is logged whenever it changes, and each turn with its `[Game: …]` notes.
- **Each reply**, raw and as shown, with the time to the first word and the total time.
- **How shopkeepers understood you**, by rules or by the AI classifier, and every offer, acceptance, decline and sale.
- **Other events:** interruptions, words added to the notebook and words you used, save and load events, AI service status, and any errors.

Voice recordings can be turned off on the same screen. Recordings older than 30 days are deleted; the text logs are kept.

Example:
```
22:06:05.1  MIC→Old Wang  heard "老板我想买猪鱼竿" (recognised in 0.12 s; 1.9 s audio, peak level 0.284, audio/220604-976_to-Old-Wang.wav)
22:06:05.1  Old Wang      understood (rules): buy item=rod_bamboo qty=1
22:06:05.1  Old Wang      OFFER Bamboo Rod — ¥120
22:06:05.1  →Old Wang     turn (history 2 msgs, slot 1)
                      [Game: The customer asked for 一根竹鱼竿. It costs 一百二十块. Tell them the price and ask if they want it (要吗？).]
                      老板我想买猪鱼竿
22:06:06.7  Old Wang→     reply: first token 0.92 s, total 1.64 s
                      竹鱼竿一百二十块。你要吗？
```

## Tuning the AI (`LocalAI/localai.json`)
Any field of `Assets/Scripts/GenAI/LocalAIConfig.cs` can be overridden here. For example:
```json
{
  "llmModel": "models/Qwen3.5-4B-Q4_K_M.gguf",
  "llmThreads": 4,
  "speechThreads": 4,
  "externalLlmUrl": "",
  "externalLlmModel": ""
}
```
- **The 4B model is the default.** It writes much better Chinese than the 2B.
  - For a faster but weaker Mei, run `Tools/setup-local-ai.ps1 -ModelSize 2B` and point `llmModel` at the 2B file.
  - `-ModelSize 9B` is there if you have the VRAM.
- **Use Ollama or LM Studio instead:** set `externalLlmUrl` (e.g. `http://localhost:11434/v1`) and `externalLlmModel`.
- **Latency:** measured on a Ryzen 7 7435HS with an RTX 3050 4 GB, with the game running.
  - Speech recognition takes about 0.1–0.2 s.
  - Mei starts talking about 2 s after you let go of V.
  - Shopkeepers take about 2 s when the rules understand the line, and about 6 s when the AI classifier has to step in.

## Troubleshooting
- **Nobody hears me:** Windows Settings → Privacy → Microphone → allow desktop apps. The game uses the default recording device.
- **The status dots stay grey or red:** read `LocalAI/logs/*.log` and the player log. Make sure nothing else is using port 8765.
- **A shopkeeper misunderstands:** check the chat log (**C**) to see what the recogniser heard. Name the item the way Mei taught it.
- **Something went wrong in a conversation:** open the chat log (Esc → Saves & logs → Open chat logs) and find the moment. It shows what was heard, what the character was told and what it replied.
- **Lost progress or a bad purchase:** Esc → Saves & logs → a restore point → **Go back to this**.

## How it's built
Everything visual is **generated by editor scripts**, so the world can be rebuilt at any time:

| Menu (Untitled Game → …) | Batch equivalent (`Tools/unity-batch.sh <method>`) |
|---|---|
| Setup → Configure Project (URP, TextMeshPro, player settings) | `UntitledGame.EditorTools.ProjectSetup.Run` |
| Build Willow Lake Scene (terrain, lake, dock, cabin, market, forest, characters, systems) | `UntitledGame.EditorTools.SceneBuilder.Build` |
| Capture Preview Screenshots → `Captures/` (add `-captureView market` for one view) | `UntitledGame.EditorTools.CaptureTools.CaptureAll` |
| Build Windows Player | `UntitledGame.EditorTools.BuildTools.BuildWindows` (or `RebuildAll`) |

### End-to-end self-test
`Builds/WillowLake/WillowLake.exe -selftest` plays through the game on its own. Add `-only parser,home,school` to run just those sections (the full run takes about 8 minutes). It uses its own save file, and writes a PASS/FAIL report and screenshots to `Captures/selftest/`. It covers:
- **Voice chat:** it speaks to Mei and the shopkeepers with the game's own voices, round-tripping through the speech recogniser.
- **The market:** it buys a rod and worms, gets refused in English, and sells fish.
- **Home:** it buys and places Tangyuan's bowl and feeds her.
- **Fishing:** it fishes with bait.
- **The shop parser:** unit checks on typical (and mis-heard) lines.
- **The test centre:** it checks that the lessons cover every HSK word, and how answers are graded. It starts a lesson by voice, answers it (some answers spoken), practises, and passes the HSK 1 test. It checks the test's confirm step (Y/N), that the result is kept, and that there's no daily limit. Then it checks that the blue line and the +10% fish bonus unlock, and that the interface turns Chinese.
- **Saves:** it saves into another slot and reloads the scene through the real load path. It checks that money, words, your position and Mei's memory come back (and that the journal's transcript starts fresh), and that the AI isn't restarted. It then damages the save file on purpose to check that recovery works, and checks the chat log's contents.

```
Assets/
  Plugins/SherpaOnnx/  C# bindings for sherpa-onnx (Apache 2.0), adapted to C# 9
  Scripts/
    GenAI/        LocalAIServices (llama-server process + in-process speech), SpeechEngine (sherpa-onnx ASR/TTS on
                  worker threads), OpenAIStreamingClient (streaming + JSON-schema calls), MicRecorder, SpeechText
    Companion/    DialogueAgent (shared: streaming, voice, history, barge-in), CompanionBrain + CompanionPersona (Mei),
                  ShopkeeperBrain + ShopPersona + ShopIntentParser (the locals), CharacterVoice, TalkingHead,
                  CompanionController, VoiceChatController (who you're talking to, push-to-talk / hands-free)
    Language/     Pinyin (CC-CEDICT segmentation + tone marks), VocabNotebook
    Economy/      Catalog (items, shops, fish prices), Inventory (money, bag, bucket), PriceTag
    Home/         PlacementController, HomeItems, ItemVisuals, PetController (Tangyuan), Interactables
    Fishing/      FishingController (cast → bite → tension; rods and bait matter), FishingRod, FishDatabase, CatchJournal
    Environment/  WorldShape (lake + market layout), WorldLocations, DayNightCycle, Weather, …
    UI/           GameUI (HUD, offer card, prompts), Bubbles (speech bubbles with pinyin, shop signs), Panels, BagPanels
    Core/         SaveSystem, AudioManager, InputGate, GameBootstrap, SelfTest
  Shaders/        Comfy/Lit, Comfy/Water, Comfy/Sky, Comfy/Fish, Comfy/Particle (URP HLSL)
  Editor/         SceneBuilder, ComfyAssets, KenneyMaterials, ProjectSetup, CaptureTools, BuildTools
  ThirdParty/     Kenney models, CC0 audio, OFL fonts (downloaded)
  StreamingAssets/cedict_ts.u8   (downloaded)
```

The design notes and roadmap are in [docs/companion-genai-plan.md](docs/companion-genai-plan.md). Licenses are in [CREDITS.md](CREDITS.md).
