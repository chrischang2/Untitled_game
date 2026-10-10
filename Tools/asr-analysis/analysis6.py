"""Stall requests: score recordings said to Teacher Gao against her requests (我想上课 / 我想练习 / 我想考试 ...),
the same way the game now does (SpeechEngine options -> SenseVoiceCtc margins), and see what would be taken as said."""
import glob, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
import numpy as np
import sv
from ctc import ctc_logp

logs = os.path.join(os.environ['USERPROFILE'], 'AppData', 'LocalLow', 'Cozy Local Games', 'Willow Lake', 'ChatLogs')
OPTIONS = [p + w for w in ('上课', '练习', '考试') for p in ('我想', '我要', '老师我想', '老师我要')] + ['你好', '谢谢', '再见', '你喜欢什么', '你不喜欢什么']
rows = []
for f in sorted(glob.glob(os.path.join(logs, '*', 'chat.log'))):
    d = os.path.dirname(f)
    for line in open(f, encoding='utf-8', errors='ignore'):
        m = re.search(r'MIC→(.*?) heard "([^"]*)" \(.*?(audio/[^)]+\.wav)\)', line)
        if m and re.search('gao|school|teacher', m[1], re.I) and os.path.exists(os.path.join(d, m[3])):
            rows.append((m[2], os.path.join(d, m[3])))
print(len(rows), 'recordings said to the teacher')
taken = []
for heard, wav in rows:
    lp = sv.log_softmax(sv.logits_for(sv.read_wav(wav)))
    best = lp[4:].max(-1).sum()
    scores = [(ctc_logp(lp, o) - best, o) for o in OPTIONS]
    m, o = max(scores)
    clean = ''.join(c for c in heard if '一' <= c <= '鿿')
    if m > -3 and clean != o: taken.append((heard, o, m))
    if clean.startswith('我想') or clean.startswith('我要') or m > -6:
        print(f'  heard {heard:10} best request {o:8} score {m:6.1f} {"-> taken as " + o if m > -3 and clean != o else ""}')
print(f'\nreplaced: {len(taken)} of {len(rows)}')
