# Credits & licenses

Everything in Willow Lake is free to use. The art and sound live in `Assets/ThirdParty/`, with each pack's license file next to it.

## 3D models: Kenney (CC0 1.0)
By [Kenney](https://kenney.nl) (public domain, credit appreciated):
Nature Kit, Survival Kit, Pirate Kit, Holiday Kit, Mini Characters, Cube Pets, Fantasy Town Kit 2.0, Food Kit, Furniture Kit.

## Sound effects
| File(s) in `Assets/ThirdParty/Audio/Resources/` | Source | Author | License |
|---|---|---|---|
| `SFX/ui_*`, `SFX/footstep_*`, `SFX/rpg_*` | Interface Sounds, Impact Sounds, RPG Audio | Kenney | CC0 |
| `SFX/reel`, `cast_splash`, `bloop`, `catch_normal`, `catch_special`, `escape` | [Fisheefects](https://opengameart.org/content/fisheefects) | You're Perfect Studio | CC0 (multi-licensed) |
| `SFX/splash_0*`, `SFX/bubble_0*`, `Ambience/loop_water_0*`, `Ambience/loop_rain` | [40 CC0 water / splash / slime SFX](https://opengameart.org/content/40-cc0-water-splash-slime-sfx) | rubberduck | CC0 |
| `SFX/plop`, `SFX/water_small` | [Skippy fish water sound collection](https://opengameart.org/content/skippy-fish-water-sound-collection) | jcpmcdonald | CC0 |
| `SFX/splash_big1`, `SFX/splash_big2` | [Water splash and sand footsteps](https://opengameart.org/content/water-splash-and-sand-footsteps) | Peludo | CC0 |
| `Ambience/crickets` | [Crickets ambient noise (loopable)](https://opengameart.org/content/crickets-ambient-noise-loopable) | Wolfgang_ | CC0 |
| `Ambience/forest` | [Forest Ambience](https://opengameart.org/content/forest-ambience) | TinyWorlds | CC0 |
| `Ambience/birds` | [Ambient Bird Sounds](https://opengameart.org/content/ambient-bird-sounds) | isaiah658 | CC0 |

## Music (CC0)
- [A Small Fire Will Do (calming loop)](https://opengameart.org/content/a-small-fire-will-do-calming-loop), by Trex0n
- [Apple Cider](https://opengameart.org/content/apple-cider), by Zane Little Music
- [Catmint](https://opengameart.org/content/catmint) and [Daisy](https://opengameart.org/content/daisy), by Kistol
- [Cozy Puzzle – In Game 1](https://opengameart.org/content/cozy-puzzle-in-game-1), by MintoDog

## Dictionary
- [CC-CEDICT](https://www.mdbg.net/chinese/dictionary?page=cedict) (CC BY-SA 4.0), by MDBG and contributors. It is used for pinyin and for traditional→simplified conversion, and is downloaded to `Assets/StreamingAssets/cedict_ts.u8`. If you share a build, keep this credit and the license with it.

## Fonts (SIL Open Font License 1.1)
- Varela Round, by Joe Prince (Google Fonts)
- Lilita One, by Juan Montoreano (Google Fonts)
- Chinese text uses a font that ships with Windows (Microsoft YaHei). It is loaded at runtime, not bundled.

## Local AI runtime
`Tools/setup-local-ai.ps1` downloads these into `LocalAI/`. They are gitignored and not part of the repo.

| Component | Purpose | License |
|---|---|---|
| [llama.cpp](https://github.com/ggml-org/llama.cpp) `llama-server` (Vulkan build) | runs the chat model | MIT |
| [Qwen3.5-4B / 2B Instruct](https://huggingface.co/Qwen) (GGUF by Unsloth) | Mei's and the shopkeepers' brains | Apache 2.0 |
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) 1.13.8 (native DLLs + C# bindings in `Assets/Plugins/SherpaOnnx`) | in-process speech recognition and synthesis | Apache 2.0 |
| SenseVoice-Small, int8, 2025-09-09 (from [ASLP-lab/WSYue-ASR](https://huggingface.co/ASLP-lab/WSYue-ASR), based on FunAudioLLM SenseVoice) | speech to text (pinned to Mandarin) | see the model pages; the SenseVoice model license allows commercial use with attribution |
| [Matcha-TTS zh-en](https://modelscope.cn/models/dengcunqin/matcha_tts_zh_en_20251010) by dengcunqin + `vocos-16khz-univ` vocoder | Mei's voice (Mandarin + English) | no license stated by the author; fine for personal use, but check before sharing a build |
| Piper voice `zh_CN-chaowen-medium` | male shopkeepers (老王, 李师傅) | dataset CC0 |
| Piper voice `zh_CN-xiao_ya-medium` | female shopkeepers (陈阿姨, 小林) | trained on the Data Baker BZNSYP corpus: **non-commercial use only** |
| Piper voice `en_US-kristin-medium` | Mei's English | trained on LibriVox recordings (public domain) |

This is fine for a personal learning game. For anything commercial, swap the xiao_ya voice (set `voiceFemale` in `LocalAI/localai.json`) and check the Matcha model's terms.
