# Willow Lake 🎣

A cozy low-poly fishing game in Unity 6 (URP) for learning Mandarin by talking. You fish off a little dock with your friend and tutor **Mei**, and look after your cat **汤圆 Tangyuan**. At the market you buy and sell things with the locals, speaking Chinese with your real voice. They talk back out loud.

- **All dialogue is in Mandarin.** The menus stay in English.
- **The four shopkeepers speak only Mandarin.** They don't understand English at all.
- **Mei is your only source of Chinese.** Ask her anything, in English or Chinese, and she teaches you exactly what you need, one phrase at a time: how to greet 老王, ask 多少钱, say 我想卖鱼, or buy 猫粮.
- **Pinyin is added by the game.** It comes from the CC-CEDICT dictionary, never from the AI, so it is always correct. The words Mei teaches go into your notebook.

Everything runs locally on your PC:

```
  hold V, speak ──► SenseVoice (speech → text, Mandarin) ──► llama.cpp + Qwen3.5-4B (streamed)
                                                                │ sentence by sentence
  they talk ◄── Matcha / Piper voices (sherpa-onnx) ◄──────────┘
```

## The market (southeast of the cabin)
| Stall | Keeper | Sells / buys |
|---|---|---|
| 渔具店 Tackle shop | 老王 Old Wang | rods (bamboo, carbon, golden), bait (worms, shrimp, dough, glow lure), a big bucket, strong line, a fancy float |
| 鱼店 Fish market | 陈阿姨 Auntie Chen | **buys your fish**. Rarer and bigger fish pay more |
| 家具店 Furniture shop | 李师傅 Master Li | chairs, tables, a sofa, lamps, plants, a rug, a bookcase, a radio, a teddy bear… for your camp |
| 宠物店 Pet shop | 小林 Xiao Lin | cat food, dried-fish treats, a bowl, a bed, a box, yarn, a scratching post, a bell collar for Tangyuan |

How shopping works:
1. Walk up to a stall, face the keeper, hold **V** and say something like 老板，我想买竹鱼竿 or 阿姨，我想卖鱼.
2. The game works out what you asked for:
   - Clear lines are read by rules. Item names are matched by sound, so a slightly wrong tone still works.
   - Unclear lines go to a small AI classifier.
3. The game applies the real prices and your real money. The keeper tells you the price and asks 要吗？.
4. Say 要 or 可以 to accept, or 不要了 to decline. You can also press **Y** or **X** on the offer card.

The keepers can't invent prices or give things away: the game decides, and they just narrate it.

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
| **Hold left mouse** | reel in. **Let go when the fish pulls**, or the line snaps |
| **E** or move | reel your line back in |
| **Hold V** | talk to whoever you're facing: a shopkeeper at their stall, otherwise Mei |
| **Hold B** | always talk to Mei, even at a stall ("Mei, how do I say…?") |
| **T** / **Enter** | type instead (**Tab** switches between the shopkeeper and Mei) |
| **Y** / **X** | accept / decline the offer on the card |
| **F** | interact: feed Tangyuan by hand (cat food or treats) or pet her, fill her bowl |
| **I** | bag: money, bucket, rods (equip), bait (use), camp items (place / put away) |
| **N** | notebook: every word Mei has taught you, with pinyin, and how often you've used it |
| **J** | fishing journal |
| **C** | conversation log, with pinyin |
| **H** | hide the HUD |
| **Esc** | menu: volumes, voices, talking speed, **Mei's English**, **pinyin**, hands-free talking, day length, **Saves & logs** |
| **F5** | quick save |

Placing furniture: open the bag (**I**) and choose **Place**. Then **left-click** to put it down, **R** or **scroll** to rotate, and **right-click** or **Esc** to cancel.

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
`Builds/WillowLake/WillowLake.exe -selftest` plays through the game on its own. It uses its own save file, and writes a PASS/FAIL report and screenshots to `Captures/selftest/`. It covers:
- **Voice chat:** it speaks to Mei and the shopkeepers with the game's own voices, round-tripping through the speech recogniser.
- **The market:** it buys a rod and worms, gets refused in English, and sells fish.
- **Home:** it buys and places Tangyuan's bowl and feeds her.
- **Fishing:** it fishes with bait.
- **The shop parser:** unit checks on typical (and mis-heard) lines.
- **Saves:** it saves into another slot and reloads the scene through the real load path. It checks that money, words, your position, the chat log and Mei's memory come back, and that the AI isn't restarted. It then damages the save file on purpose to check that recovery works, and checks the chat log's contents.

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
