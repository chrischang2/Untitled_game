"""Repeat-after-me answers only: how much does "any of the top 3 readings counts" add on top of direct scoring at -3?
(Peak-frame definition, the same as SenseVoiceCtc.Analyze's topMatch.)"""
import sys, random
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
import sv
from analysis1 import rows, grade, same_sound
from analysis4 import lps, texts, base, pos, words_all

random.seed(3)

def segments(lp):
    """Emitted tokens: (peak frame) per segment, like the game."""
    ids = lp.argmax(-1)
    segs, prev = [], -1
    for f in range(4, len(lp)):
        a = int(ids[f])
        if a != 0 and a != prev: segs.append([a, f, lp[f, a]])
        elif a != 0 and segs and a == segs[-1][0] and lp[f, a] > segs[-1][2]: segs[-1][1:] = [f, lp[f, a]]
        prev = a
    return segs

def top_match(lp, word, K=3):
    segs = segments(lp)
    n = len(word)
    if n == 1 and len(segs) > 3: return False
    tops = []
    for a, f, _ in segs:
        order = [int(j) for j in np.argsort(-lp[f])[:K + 6] if j != 0 and not sv.TOKENS[j].startswith('<|')][:K]
        tops.append(order)
    for i in range(len(tops) - n + 1):
        if all(any(len(sv.TOKENS[j]) == 1 and same_sound(sv.TOKENS[j], word[k]) for j in tops[i + k]) for k in range(n)): return True
    return False

rep = [i for i, r in enumerate(rows) if r['kind'] == 'repeat']
rej = [i for i in rep if not base[i]]
print(f'\nrepeat-after-me answers: {len(rep)}, rejected by the plain transcript: {len(rej)}')
tm = {i: top_match(lps[i], rows[i]['word']) for i in rep}
dm = {i: pos[i] > -3 for i in rep}
print(f'  rescued by direct scoring (> -3):        {sum(dm[i] for i in rej)}/{len(rej)}')
print(f'  rescued by top-3 readings:               {sum(tm[i] for i in rej)}/{len(rej)}')
print(f'  rescued by either:                       {sum(dm[i] or tm[i] for i in rej)}/{len(rej)}')
# wrong-word audio accepted: audio the game heard right, graded against another word of the same length
neg = []
good = [i for i in rep if base[i]]
for i in good:
    said = rows[i]['word']
    others = [w for w in words_all if len(w) == len(said) and not any(same_sound(a, b) for a, b in zip(w, said))]
    for w in random.sample(others, min(6, len(others))): neg.append((i, w))
from analysis4 import margin
dn = np.mean([margin(lps[i], w) > -3 for i, w in neg]) * 100
tn = np.mean([top_match(lps[i], w) for i, w in neg]) * 100
en = np.mean([(margin(lps[i], w) > -3) or top_match(lps[i], w) for i, w in neg]) * 100
print(f'  other word accepted ({len(neg)} checks): direct {dn:.1f}%, top-3 {tn:.1f}%, either {en:.1f}%')
for i in rej:
    print(f"   {rows[i]['word']:4} heard {texts[i] or '(nothing)':8} margin {pos[i]:6.1f} top3 {'yes' if tm[i] else 'no'}")
