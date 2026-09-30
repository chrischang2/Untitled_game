# Willow Lake: Mandarin immersion design notes

## Goal
A cozy fishing game where every conversation happens in Mandarin, spoken aloud and running locally. Menus stay in English.
- **Mei** is your friend and tutor, and your only source of Chinese vocabulary.
- **The locals** are four market shopkeepers who speak only Mandarin. They give you real reasons to use what Mei teaches: buying rods, bait and furniture, selling your fish, and looking after 汤圆 Tangyuan the cat.

## Architecture

### Speech (in process, sherpa-onnx 1.13.8)
- **Loading.** `SpeechEngine` loads the native DLLs from `LocalAI/sherpa`, then runs recognition and synthesis on worker threads. Results are pumped back to the main thread.
- **Recognition.** SenseVoice-Small int8, pinned to `zh` with inverse text normalisation, takes about 0.1–0.2 s per line. English speech still comes through, in uppercase, and is normalised to sentence case.
- **Voices.**
  - Mei uses Matcha zh-en + vocos; it handles mixed Chinese and English sentences.
  - Her pure-English runs use Piper Kristin.
  - The keepers use Piper chaowen (male) or xiao_ya (female), each with its own pitch.
- **Playback.** `CharacterVoice` splits text by script, reads `[gloss]` brackets as pauses, and plays sentence by sentence while the LLM is still streaming.

### LLM (llama-server, Vulkan)
- **Model:** Qwen3.5-4B Q4_K_M, with `--reasoning off`.
- **Slots:** three parallel slots (`-np 3`), so each agent keeps a warm prompt cache: Mei in slot 0, the keepers in slot 1, and the intent classifier in slot 2.

### Dialogue
- **DialogueAgent** is the shared base. It handles streaming to voice, history trimming, barge-in and the conversation log.
- **Mei** (`CompanionBrain` + `CompanionPersona`):
  - **Prompt.** The instructions are in English. She writes taught items as `汉字 [meaning]` and never writes pinyin.
  - **Level.** The level rules (Beginner / Intermediate / Immersion) come last in the system prompt and are repeated as a one-line reminder on every turn. Small models drift towards all-Chinese otherwise.
  - **Pinyin filter.** Any pinyin the model writes anyway is stripped (`SpeechText.ModelPinyin`).
  - **Unprompted remarks.** She speaks up for the first greeting, arriving at the market, a full bucket, a hungry cat, sunset, rain, catches, quiet moments, and praise after a purchase.
  - **Staying quiet.** She never talks over a shopkeeper, or within 15 s of the player talking to one.
- **Shopkeepers** (`ShopkeeperBrain`, one turn at a time):
  1. **Understand.** `ShopIntentParser` reads clear lines with rules. It matches items by per-character toneless pinyin plus aliases, and parses Chinese numerals, 要 / 不要, 卖鱼 and 多少钱. Unclear lines fall back to a JSON-schema-constrained LLM call.
  2. **Resolve.** The game applies the real rules (price, money, ownership, bucket contents) and makes an offer if appropriate.
  3. **Narrate.** The keeper is told the outcome in a `[Game: …]` note and narrates it. If nothing was understood, the note forbids naming prices.
  4. **Confirm.** The offer is accepted or declined by voice (要 / 不要) or with the Y/X card.
- **Pinyin** (`Language/Pinyin`): CC-CEDICT longest-match segmentation, single-character polyphone overrides, tone marks, and traditional→simplified conversion. It is shown above every Chinese bubble and in the chat log.
- **Vocab notebook:** it captures every `汉字 [meaning]` Mei teaches and counts each time the player uses a word, with a toast.

## Status
- ✅ Speech stack, Mandarin-only locals, Mei as tutor with immersion levels, deterministic pinyin, vocab notebook.
- ✅ Economy: money, rods with stats, bait that changes bites, accessories, a fish bucket, selling.
- ✅ Home: placeable furniture, and Tangyuan with needs, a bowl, a bed, toys and a collar.
- ✅ Self-test (`-selftest`) covering voice → shop → purchase → home → pet → fishing, plus unit checks on the parser.

## Next ideas
1. **Pronunciation feedback.** Compare the recognised text with what Mei asked the player to say, then have her praise or re-model it.
2. **Spaced repetition.** Mei re-uses notebook words the player hasn't said in a while. The notebook already has `said` counts and timestamps.
3. **More locals.** Examples are a fisherman on the dock who trades tips for rare bait, or a noodle stand whose meals give temporary luck.
4. **Keeper memory.** Keepers could remember regulars ("又来了！") and give small discounts for polite phrases (请, 谢谢).
5. **Tones.** A simple pitch-contour check on single words.

## Known limitations
- **Mis-hearings.** SenseVoice sometimes mishears tones (竹 → 猪). The shop parser tolerates same-sound characters, but Mei's lessons see the raw transcript.
- **Slow fallback.** The 4B model runs partly on the CPU with 4 GB of VRAM. When the LLM classifier is needed, shopkeepers take about 6 s to answer.
- **Voice licenses.** The xiao_ya voice is non-commercial, and the Matcha voice has no stated license (see CREDITS.md).
