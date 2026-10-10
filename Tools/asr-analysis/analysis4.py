"""Compares, on the same recordings, how answers are graded now (transcript, plus the new "did you mean" second choice)
with scoring the expected word directly, and with a sound-alike grading rule for common learner confusions."""
import os, sys, random, collections
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from pypinyin import pinyin, Style
import sv
from analysis1 import rows, grade, same_sound
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

lps = [sv.log_softmax(sv.logits_for(sv.read_wav(r['wav']))) for r in rows]

random.seed(5)
words_all = sorted({r['word'] for r in rows})
texts = [sv.text_of(sv.greedy(lp)) for lp in lps]
base = [grade(t, r['word']) for t, r in zip(texts, rows)]

# ---------------------------------------------------------------- the game's "did you mean" (mirror of SenseVoiceCtc.Analyze)
def is_word(t): return any(c.isalnum() for c in t)
def is_tag(i): return sv.TOKENS[i].startswith('<|')
def second_choice(lp, skip_homophones=False, min_p=0.02):
    p = np.exp(lp)
    ids, conf, rival, rivalp = [], [], [], []
    prev = -1
    for f in range(4, len(lp)):
        arg = int(lp[f].argmax())
        if arg != 0 and arg != prev:
            ids.append(arg); conf.append(p[f, arg]); rival.append(-1); rivalp.append(0.0)
        if arg != 0 and ids:
            s = len(ids) - 1
            if arg == ids[s]: conf[s] = max(conf[s], p[f, arg])
            for v in np.argsort(-p[f])[:40]:
                v = int(v)
                if v == 0 or v == arg or is_tag(v) or not is_word(sv.TOKENS[v]): continue
                if skip_homophones and len(sv.TOKENS[v]) == 1 and len(sv.TOKENS[arg]) == 1 and same_sound(sv.TOKENS[v], sv.TOKENS[arg]): continue
                if p[f, v] > rivalp[s]: rival[s], rivalp[s] = v, float(p[f, v])
                break
        prev = arg
    weakest, wc = -1, 2.0
    for i in range(len(ids)):
        if is_tag(ids[i]) or not is_word(sv.TOKENS[ids[i]]) or rival[i] < 0 or rivalp[i] < min_p: continue
        if conf[i] < wc: wc, weakest = conf[i], i
    if weakest < 0: return None
    alt = [rival[weakest] if i == weakest else t for i, t in enumerate(ids)]
    return sv.text_of(alt)

alts = [second_choice(lp) for lp in lps]
alts_nh = [second_choice(lp, skip_homophones=True) for lp in lps]

# ---------------------------------------------------------------- direct scoring
def margin(lp, word): return ctc_logp(lp, word) - lp[4:].max(-1).sum()
pos = np.array([margin(lp, r['word']) for lp, r in zip(lps, rows)])

# ---------------------------------------------------------------- sound-alike grading (common learner / regional confusions)
def ini_fin(ch):
    i = pinyin(ch, style=Style.INITIALS, strict=False, heteronym=True)[0]
    f = pinyin(ch, style=Style.FINALS, strict=False, heteronym=True)[0]
    return set(zip(i, f)) if len(i) == len(f) else {(i[0], f[0])}
PAIRS = [('zh', 'z'), ('ch', 'c'), ('sh', 's'), ('n', 'l'), ('h', 'f'), ('r', 'l')]
FPAIRS = [('in', 'ing'), ('en', 'eng'), ('an', 'ang'), ('ian', 'iang'), ('uan', 'uang')]
def norm(x, pairs):
    for a, b in pairs:
        if x == a: return b
    return x
def near(a, b):
    if same_sound(a, b): return True
    for ia, fa in ini_fin(a):
        for ib, fb in ini_fin(b):
            if norm(ia, PAIRS) == norm(ib, PAIRS) and norm(fa, FPAIRS) == norm(fb, FPAIRS): return True
    return False
def grade_near(text, word):
    t = ''.join(c for c in text if '一' <= c <= '鿿')
    n = len(word)
    if grade(text, word): return True
    if len(t) < n or (n == 1 and len(t) > 3): return False
    return any(all(near(t[i + k], word[k]) for k in range(n)) for i in range(len(t) - n + 1))

# ---------------------------------------------------------------- negatives: audio of one word, graded as if another was asked
def initials(w): return [x for s in pinyin(w, style=Style.NORMAL) for x in s]
negs_rand, negs_close = [], []
for i, r in enumerate(rows):
    if not base[i]: continue                            # use audio the game heard right, so we know what was said
    said = r['word']
    others = [w for w in words_all if len(w) == len(said) and not any(same_sound(a, b) for a, b in zip(w, said))]
    for w in random.sample(others, min(3, len(others))): negs_rand.append((i, w))
    close = [w for w in others if sum(near(a, b) or (initials(a)[0][1:] == initials(b)[0][1:]) for a, b in zip(w, said)) >= 1]
    for w in random.sample(close, min(2, len(close))): negs_close.append((i, w))

def methods():
    yield 'now: transcript only', lambda i, w: grade(texts[i], w)
    yield 'now + "did you mean" (if clicked)', lambda i, w: grade(texts[i], w) or (alts[i] is not None and grade(alts[i], w))
    yield '  ...second choice skips homophones', lambda i, w: grade(texts[i], w) or (alts_nh[i] is not None and grade(alts_nh[i], w))
    yield 'sound-alike grading (zh=z, n=l, in=ing...)', lambda i, w: grade_near(texts[i], w)
    for th in (-2, -3, -4, -6):
        yield f'direct scoring, margin > {th}', (lambda th: lambda i, w: grade(texts[i], w) or margin(lps[i], w) > th)(th)
    yield 'direct: >-4 one char, >-6 longer', lambda i, w: grade(texts[i], w) or margin(lps[i], w) > (-4 if len(w) == 1 else -6)

rej = [i for i, ok in enumerate(base) if not ok]
rej_rep = [i for i in rej if rows[i]['kind'] == 'repeat']
rej_rec = [i for i in rej if rows[i]['kind'] != 'repeat']
print(f'\n{len(rows)} attempts: transcript accepted {sum(base)}, rejected {len(rej)}'
      f' ({len(rej_rep)} while repeating the word just heard = almost surely said right; {len(rej_rec)} recalled from English = some may be real mistakes)')
print(f'"did you mean" shown on {sum(a is not None for a in alts)}/{len(rows)} answers; it was a homophone of the first choice (useless) on '
      f'{sum(1 for t, a in zip(texts, alts) if a and len(t) == len(a) and all(same_sound(x, y) for x, y in zip(t, a)))}')
print(f'{len(negs_rand)} random wrong-word checks, {len(negs_close)} close-sounding wrong-word checks\n')
print(f'{"method":46} {"rescued(repeat)":>16} {"rescued(recall)":>16} {"false OK random":>16} {"false OK close":>15}')
for name, fn in methods():
    a = sum(fn(i, rows[i]['word']) for i in rej_rep)
    b = sum(fn(i, rows[i]['word']) for i in rej_rec)
    fr = np.mean([fn(i, w) for i, w in negs_rand]) * 100
    fc = np.mean([fn(i, w) for i, w in negs_close]) * 100
    print(f'{name:46} {a:>9}/{len(rej_rep):<6} {b:>9}/{len(rej_rec):<6} {fr:>15.1f}% {fc:>14.1f}%')

print('\nEvery rejected answer: asked / heard / second choice / direct-score margin (0 = as likely as the best path)')
for i in rej:
    r = rows[i]
    print(f"  {r['kind']:6} {r['word']:4} heard {texts[i] or '(nothing)':8} 2nd {alts[i] or '-':8} 2nd-nohomo {alts_nh[i] or '-':8} margin {pos[i]:6.1f}")
