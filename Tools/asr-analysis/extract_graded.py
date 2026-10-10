"""Pairs every graded lesson/test answer in the chat logs with its saved recording -> %TEMP%/graded_wavs.json."""
import glob, re, os, json, sys
sys.stdout.reconfigure(encoding='utf-8')
logs = os.path.join(os.environ['USERPROFILE'], 'AppData', 'LocalLow', 'Cozy Local Games', 'Willow Lake', 'ChatLogs')
out = []
for f in sorted(glob.glob(os.path.join(logs, '*', 'chat.log'))):
    d = os.path.dirname(f)
    last = None
    for line in open(f, encoding='utf-8', errors='ignore'):
        m = re.search(r'MIC→[^"]*? heard "([^"]*)" \(.*?(audio/[^)]+\.wav)\)', line)
        if m:
            last = (m[1], m[2])
            continue
        g = re.search(r'Q(\d+): (\S+) \((repeat|from English: [^)]*)\) heard "([^"]*)" -> (RIGHT|wrong|skipped)', line)
        if g and last and last[0] == g[4]:
            p = os.path.join(d, last[1])
            if os.path.exists(p):
                out.append(dict(word=g[2], kind=g[3][:6], heard=g[4], verdict=g[5], wav=os.path.abspath(p)))
json.dump(out, open(os.path.join(os.environ['TEMP'], 'graded_wavs.json'), 'w', encoding='utf-8'), ensure_ascii=False)
print(len(out), 'graded answers with audio')
