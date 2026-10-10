import json, os, sys, random, time
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
import sherpa_onnx
import sv
from analysis1 import rows, grade, same_sound

Q = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), 'LocalAI', 'models', 'sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25')
def make(hot=''):
    return sherpa_onnx.OfflineRecognizer.from_qwen3_asr(
        conv_frontend=os.path.join(Q, 'conv_frontend.onnx'), encoder=os.path.join(Q, 'encoder.int8.onnx'), decoder=os.path.join(Q, 'decoder.int8.onnx'),
        tokenizer=os.path.join(Q, 'tokenizer'), num_threads=4, hotwords=hot)
def dec(rec, w):
    s = rec.create_stream(); s.accept_waveform(16000, w); rec.decode_stream(s); return s.result.text

wavs = {i: sv.read_wav(r['wav']) for i, r in enumerate(rows)}
base = [grade(sv.transcribe(wavs[i]), r['word']) for i, r in enumerate(rows)]
rej = [i for i, ok in enumerate(base) if not ok]
random.seed(2)
acc = random.sample([i for i, ok in enumerate(base) if ok], 40)
import gc
# context: tell it the expected word (cache one recogniser per word)
def one(word, wav):
    rec = make(word); out = dec(rec, wav); del rec; gc.collect(); return out
t0 = time.time()
acc = acc[:20]
r_hot = {i: one(rows[i]['word'], wavs[i]) for i in rej + acc}
print(f'Qwen3-ASR told the expected word as context ({time.time() - t0:.0f}s):')
print(f'   on the {len(rej)} answers SenseVoice got wrong: {sum(grade(r_hot[i], rows[i]["word"]) for i in rej)} accepted')
print(f'   on {len(acc)} that SenseVoice got right:      {sum(grade(r_hot[i], rows[i]["word"]) for i in acc)} accepted')
# the danger of context: does it "hear" the expected word in audio of a different word?
neg = []
random.seed(9)
for _ in range(25):
    i = random.choice(acc); j = random.choice(acc)
    if same_sound(rows[i]['word'][0], rows[j]['word'][0]): continue
    neg.append(grade(one(rows[j]['word'], wavs[i]), rows[j]['word']))
print(f'   but with a different word as context, other-word audio is accepted {sum(neg)}/{len(neg)} times')
print('   examples (rejected set):', [(rows[i]['word'], r_hot[i]) for i in rej[:10]])
