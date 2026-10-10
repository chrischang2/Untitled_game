import json, os, sys, collections, random
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from pypinyin import pinyin, Style
import sv
from analysis1 import rows, grade, same_sound, readings   # (re-runs analysis1's printout once)

random.seed(11)
wavs = [sv.read_wav(r['wav']) for r in rows]

def accepted(samples, word, **kw):
    return grade(sv.transcribe(samples, **kw), word)

print('\n=== 1. Which knobs change the outcome? (all %d genuine attempts, replayed offline) ===' % len(rows))
def trial(name, fn):
    ok = sum(grade(sv.text_of(sv.greedy(sv.log_softmax(fn(w)))), r['word']) for w, r in zip(wavs, rows))
    print(f'  {name:46} {ok}/{len(rows)} accepted ({100 * ok / len(rows):.1f}%)')
    return ok
trial('as the game does it (zh, ITN on)', lambda w: sv.logits_for(w))
trial('language = auto', lambda w: sv.logits_for(w, lang='auto'))
trial('language = en', lambda w: sv.logits_for(w, lang='en'))
trial('inverse text normalisation off', lambda w: sv.logits_for(w, itn=False))
trial('louder (x2 gain)', lambda w: sv.logits_for(w, gain=2.0))
trial('quieter (x0.5 gain)', lambda w: sv.logits_for(w, gain=0.5))
def norm(w, peak=0.7): return w * (peak / max(1e-4, np.abs(w).max()))
trial('peak-normalised to 0.7', lambda w: sv.logits_for(norm(w)))
def pad(w, a=0.3, b=0.3): return np.concatenate([np.zeros(int(16000 * a), np.float32), w, np.zeros(int(16000 * b), np.float32)])
trial('0.3 s of silence added before and after', lambda w: sv.logits_for(pad(w)))
def trim(w, thr=0.02):
    idx = np.where(np.abs(w) > thr)[0]
    return w[max(0, idx[0] - 1600): idx[-1] + 1600] if len(idx) else w
trial('leading/trailing silence trimmed (+0.1 s)', lambda w: sv.logits_for(trim(w)))
trial('trimmed, then 0.3 s silence each side', lambda w: sv.logits_for(pad(trim(w))))

print('\n=== 2. Using more than the single best guess ===')
# Per-frame competitors: does the target (or a homophone) appear in the model's top-K at any emitted frame?
def topk_hit(lp, word, K):
    ids = lp.argmax(-1); prev = -1
    for t, i in enumerate(ids):
        if i != prev and i != 0 and not sv.TOKENS[i].startswith('<|'):
            top = [j for j in np.argsort(-lp[t])[:K + 1] if j != 0]
            if any(len(sv.TOKENS[j]) == 1 and same_sound(sv.TOKENS[j], word[0]) for j in top[:K]): return True
        prev = i
    return False
lps = [sv.log_softmax(sv.logits_for(w)) for w in wavs]
base = [grade(sv.text_of(sv.greedy(lp)), r['word']) for lp, r in zip(lps, rows)]
rej = [i for i, ok in enumerate(base) if not ok]
single = [i for i in rej if len(rows[i]['word']) == 1]
print(f'  currently rejected: {len(rej)} (single-character words: {len(single)})')
for K in (2, 3, 5):
    hit = sum(topk_hit(lps[i], rows[i]['word'], K) for i in single)
    # false accepts: audio of word A scored against a different word B
    fa = tot = 0
    for _ in range(600):
        i = random.randrange(len(rows)); j = random.randrange(len(rows))
        a, b = rows[i]['word'], rows[j]['word']
        if len(b) != 1 or same_sound(a[0], b[0]) or len(a) != 1: continue
        tot += 1; fa += topk_hit(lps[i], b, K)
    print(f'  accept if the right sound is in the top {K} guesses: rescues {hit}/{len(single)} rejected single-character answers; wrongly accepts {fa}/{tot} ({100 * fa / max(tot, 1):.0f}%) of other-word audio')

print('\n=== 3. Scoring the expected word directly (CTC likelihood with homophone classes) ===')
TOK_ID = {t: i for i, t in enumerate(sv.TOKENS)}
cls_cache = {}
def sound_class(ch):
    if ch not in cls_cache:
        cls_cache[ch] = [i for t, i in TOK_ID.items() if len(t) == 1 and '一' <= t <= '鿿' and same_sound(t, ch)]
    return cls_cache[ch]
def lse(a, axis=None):
    m = np.max(a, axis=axis, keepdims=True); return (m + np.log(np.exp(a - m).sum(axis=axis, keepdims=True))).squeeze(axis) if axis is not None else (m + np.log(np.exp(a - m).sum())).item()
def ctc_logp(lp, word):
    lp = lp[4:]                                   # drop the 4 tag frames (language / event / emotion / text-norm)
    T = len(lp); L = len(word)
    cls = [sound_class(c) for c in word]
    emit = np.stack([lse(lp[:, c], axis=1) for c in cls], axis=1)      # T x L
    blank = lp[:, 0]
    S = 2 * L + 1
    NEG = -1e9
    alpha = np.full((T, S), NEG)
    alpha[0, 0] = blank[0]
    if S > 1: alpha[0, 1] = emit[0, 0]
    for t in range(1, T):
        for s in range(S):
            cands = [alpha[t - 1, s]]
            if s >= 1: cands.append(alpha[t - 1, s - 1])
            if s >= 2 and s % 2 == 1 and cls[s // 2] != cls[s // 2 - 1]: cands.append(alpha[t - 1, s - 2])
            e = blank[t] if s % 2 == 0 else emit[t, s // 2]
            alpha[t, s] = lse(np.array(cands)) + e
    return lse(np.array([alpha[T - 1, S - 1], alpha[T - 1, S - 2]]))
def margin(lp, word): return ctc_logp(lp, word) - lp[4:].max(-1).sum()   # 0 = as good as the single best path
pos = [margin(lps[i], rows[i]['word']) for i in range(len(rows))]
neg = []
for _ in range(500):
    i = random.randrange(len(rows)); j = random.randrange(len(rows))
    a, b = rows[i]['word'], rows[j]['word']
    if same_sound(a[0], b[0]) or abs(len(a) - len(b)) > 0: continue
    neg.append(margin(lps[i], b))
pos = np.array(pos); neg = np.array(neg)
print(f'  margin (log-prob of the expected word vs the best possible path): matches median {np.median(pos[np.array(base)]):.1f}, rejected-by-game median {np.median(pos[np.array([not b for b in base])]):.1f}, other-word audio median {np.median(neg):.1f}')
for th in (-2, -4, -6, -8):
    rescued = sum(1 for i in rej if pos[i] > th)
    print(f'  accept if margin > {th:>3}: rescues {rescued}/{len(rej)} rejected answers; wrongly accepts {100 * np.mean(neg > th):.1f}% of other-word audio ({int((neg > th).sum())}/{len(neg)})')
