import json, os, sys, collections, random
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
from pypinyin import pinyin, Style
import sv

rows = json.load(open(os.environ['TEMP'] + '/graded_wavs.json', encoding='utf-8'))
SKIPS = ('不知道', 'Skip', 'skip', '跳过', '下一个', '不会')
rows = [r for r in rows if not any(s in r['heard'] for s in SKIPS)]
print(len(rows), 'genuine attempts (skips removed)')

# toneless readings of every CJK token, for homophone matching (the game's grader ignores tones too)
read_cache = {}
def readings(ch):
    if ch not in read_cache:
        try:
            read_cache[ch] = set(pinyin(ch, style=Style.NORMAL, heteronym=True)[0])
        except Exception:
            read_cache[ch] = {ch}
    return read_cache[ch]
def same_sound(a, b): return a == b or bool(readings(a) & readings(b))

def grade(text, word):
    """Mirror of HskSchool.Grade: the word, or a run of characters that sound like it (tones ignored)."""
    t = ''.join(c for c in text if '一' <= c <= '鿿')
    n = len(word)
    if word in t: return True
    if len(t) < n: return False
    if n == 1 and len(t) > 3: return False
    return any(all(same_sound(t[i + k], word[k]) for k in range(n)) for i in range(len(t) - n + 1))

lps = []
for r in rows:
    lps.append(sv.log_softmax(sv.logits_for(sv.read_wav(r['wav']))))
base = [grade(sv.text_of(sv.greedy(lp)), r['word']) for lp, r in zip(lps, rows)]
print(f'baseline (my replay of the game): {sum(base)}/{len(rows)} accepted = {100 * sum(base) / len(rows):.1f}%')
print(f"the game's own log said:            {sum(r['verdict'] == 'RIGHT' for r in rows)}/{len(rows)}")

# --- how close was the right answer when it lost?
def emitted_frames(lp):
    ids = lp.argmax(-1); out = []; prev = -1
    for t, i in enumerate(ids):
        if i != prev and i != 0 and not sv.TOKENS[i].startswith('<|'): out.append(t)
        prev = i
    return out

ranks = []
for lp, r, ok in zip(lps, rows, base):
    if ok or len(r['word']) != 1: continue
    fr = emitted_frames(lp)
    if not fr: ranks.append(('none', None, r)); continue
    t = fr[0]
    order = np.argsort(-lp[t])
    rk = None
    for pos, j in enumerate(order[:50]):
        if sv.TOKENS[j] and same_sound(sv.TOKENS[j][0], r['word']): rk = pos; break
    ranks.append((rk, float(np.exp(lp[t, order[0]])), r))
print('\nRejected single-character answers:', len(ranks))
c = collections.Counter('none' if x[0] == 'none' else (x[0] + 1 if x[0] is not None else '>50') for x in ranks)
print('rank of the right sound at the first emitted frame:', dict(sorted(c.items(), key=lambda kv: str(kv[0]))))
conf = [x[1] for x in ranks if x[1] is not None]
print('winner (wrong) probability at that frame: median %.2f, share above 0.9: %.0f%%' % (np.median(conf), 100 * np.mean([c > 0.9 for c in conf])))
