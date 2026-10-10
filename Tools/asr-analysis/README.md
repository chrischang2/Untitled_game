# Offline speech-recognition analysis

Read-only experiments on the recordings the game keeps (`keepVoiceRecordings`). Nothing here changes the game.

1. `py -m pip install onnxruntime numpy kaldi-native-fbank pypinyin` (plus sherpa-onnx for `analysis3b.py`).
2. `py Tools/asr-analysis/extract_graded.py` pairs each graded answer in the chat logs with its recording.
3. `sv.py` replays SenseVoice (the game's model) in Python with the same frontend (an exact match for about 93% of transcripts).
   - `analysis1.py` ranks the right character among the model's guesses.
   - `analysis2.py` tries the knobs (language, ITN, gain, padding), top-K acceptance, and CTC scoring of the expected word.
   - `analysis3b.py` tries Qwen3-ASR with and without the expected word as context.
   - `analysis4.py` compares, on the same recordings: transcript grading, + the "did you mean" second choice, a sound-alike rule, and direct scoring.
