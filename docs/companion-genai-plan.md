# Companion + Local GenAI + Chinese-Learning Plan

## Goal
A cozy fishing game with a companion, Mei, whom you talk to by voice through a locally run LLM.
Later, the game gates progress on learning real Mandarin phrases through those conversations.

## Status

### Phase 1: Core companion ✅
- `CompanionController` follows the player's footsteps (so she never walks into the lake), sits beside them while they fish, faces them when talking, and bobs her head in time with her voice.
- `CompanionBrain` handles conversation state, a history that is trimmed while keeping the prompt cache warm, interruption when you barge in, and unprompted remarks about catches, weather, dusk and dawn, and quiet moments.
- `ChinesePhrase` + `PhraseUnlockSystem` are still in place for phase 3.

### Phase 2: Local GenAI wiring ✅
- **Runtime:** `LocalAIServices` launches and health-checks `llama-server` (Vulkan), `whisper-server` and Piper from `LocalAI/`. `Tools/setup-local-ai.ps1` installs them.
  - It reuses servers that are already running, which keeps editor iteration fast.
  - A Windows job object stops orphan processes if the game crashes.
- **LLM:** Qwen3.5-2B-Instruct Q4_K_M by default, with 4B optional. Thinking is disabled through `chat_template_kwargs`.
  - `OpenAIStreamingClient` streams server-sent events, so it also works with Ollama or LM Studio through `externalLlmUrl`.
  - `llmThreads = 4` is important. With every core in use alongside Unity, prompt processing dropped from about 1,500 to about 6 tokens/sec.
- **Speech to text:** push-to-talk `MicRecorder` (warm ring buffer, 16 kHz) → `whisper-server`. Obvious Whisper hallucinations such as "Thank you." are filtered out.
- **Text to speech:** each finished sentence of the streamed reply goes straight to a persistent Piper process (`CompanionVoice`), so Mei starts talking about 2–3 s after you release V.
- The original `ILocalLLMClient` / `OllamaLLMClient` still work as an optional non-streaming fallback (`CompanionBrain.llmClientBehaviour`).

### Phase 3: Phrase teaching & detection 🟡 (groundwork done)
Done:
- **Mandarin practice mode** (Settings → Language). Mei adds one phrase per reply in the form `汉字 (pīnyīn)` and encourages the player to repeat it.
- Whisper uses `language=auto`. `SpeechText.SplitByScript` sends Chinese runs to the `zh_CN` Piper voice and the rest to the English voice, and joins them into one clip.
- Every fish has hanzi and pinyin (journal and catch card). CJK text renders through a runtime system-font fallback.

Next:
1. **A curated phrase list** of about 20–40 `ChinesePhrase` assets (greetings, numbers, fish names, 钓鱼, 鱼上钩了, weather, food), grouped into lessons.
2. **A lesson state in the prompt.** Tell Mei which one or two phrases are "in scope" today instead of letting her choose freely.
3. **Detection.** The transcript arrives as text, so:
   - (a) Normalise it (convert to simplified characters, strip tones) and match against the phrase's hanzi or pinyin.
   - (b) Have Mei emit a hidden `[[LEARNED:phrase_id]]` tag. `SpeechText.CleanForDisplay` already strips `[...]` notes, so it would never be shown or spoken.
   - Whisper `small` or `large-v3-turbo` recognises Mandarin far better than `base` (`setup-local-ai.ps1 -WhisperModel small-q5_1`).
4. **Pronunciation feedback (stretch goal).** Compare the Whisper transcript with the target, or use a phoneme model.
5. **A better Mandarin voice.** The Piper `huayan` voice is basic and its dataset license is unclear. Kokoro-82M (Apache 2.0, has Mandarin voices) through sherpa-onnx is the likely upgrade, and it can sit behind the same `PiperTTS`-style process wrapper.

### Phase 4: Gameplay unlocks ⏳
Ideas that fit the existing systems:
- Learning a fish's Chinese name "unlocks" its hint text in the journal.
- Phrases unlock bait or lure types that bias `FishDatabase.Roll` (a `CatchContext` modifier), night fishing with lanterns, or the rowboat to reach deep water.
- Mei's `companionNotes` (already in the save file and injected into her prompt) remember what the player has learned across days.

## Known limitations
- 2B model: occasionally inaccurate about fish facts, or it invents details. The 4B model is noticeably better but slower on 4 GB of VRAM.
- Push-to-talk only; there's no always-on voice activity detection.
- Piper `huayan` (Mandarin) quality is basic.
