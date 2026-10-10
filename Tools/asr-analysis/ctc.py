"""CTC scoring of an expected word/phrase with homophone classes (shared by the analyses)."""
import numpy as np
import sv
from analysis1 import same_sound

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
