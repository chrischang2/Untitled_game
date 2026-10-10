"""Offline SenseVoice analysis (read-only: nothing in the game is changed). Replicates sherpa-onnx's frontend + greedy CTC."""
import json, math, os, sys, wave
import numpy as np
import onnxruntime as ort
import kaldi_native_fbank as knf

sys.stdout.reconfigure(encoding='utf-8')
MODEL = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), 'LocalAI', 'models', 'sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09')
sess = ort.InferenceSession(os.path.join(MODEL, 'model.int8.onnx'), providers=['CPUExecutionProvider'])
meta = sess.get_modelmeta().custom_metadata_map
NEG_MEAN = np.array([float(x) for x in meta['neg_mean'].split(',')], dtype=np.float32)
INV_STD = np.array([float(x) for x in meta['inv_stddev'].split(',')], dtype=np.float32)
LANG = {'auto': int(meta['lang_auto']), 'zh': int(meta['lang_zh']), 'en': int(meta['lang_en']), 'yue': int(meta['lang_yue'])}
TEXT_NORM = {True: int(meta['with_itn']), False: int(meta['without_itn'])}
TOKENS = [l.rstrip('\n').rsplit(' ', 1)[0] for l in open(os.path.join(MODEL, 'tokens.txt'), encoding='utf-8')]


def read_wav(path):
    with wave.open(path) as w:
        sr, n, ch, sw = w.getframerate(), w.getnframes(), w.getnchannels(), w.getsampwidth()
        raw = w.readframes(n)
    a = np.frombuffer(raw, dtype=np.int16 if sw == 2 else np.int32).astype(np.float32)
    a /= 32768.0 if sw == 2 else 2147483648.0
    if ch > 1:
        a = a[::ch]
    if sr != 16000:  # linear resample (the game records at 16 kHz already)
        x = np.arange(0, len(a), sr / 16000.0)
        a = np.interp(x, np.arange(len(a)), a).astype(np.float32)
    return a


def features(samples, gain=1.0):
    o = knf.FbankOptions()
    o.frame_opts.dither = 0
    o.frame_opts.snip_edges = True
    o.frame_opts.samp_freq = 16000
    o.frame_opts.window_type = 'hamming'
    o.mel_opts.num_bins = 80
    f = knf.OnlineFbank(o)
    f.accept_waveform(16000, (samples * gain * 32768.0).tolist())
    f.input_finished()
    frames = np.stack([f.get_frame(i) for i in range(f.num_frames_ready)])
    m, n = 7, 6
    T = len(frames)
    T_lfr = math.ceil(T / n)
    padded = np.vstack([np.tile(frames[0], ((m - 1) // 2, 1)), frames])
    Tp = T + (m - 1) // 2
    out = []
    for i in range(T_lfr):
        if m <= Tp - i * n:
            out.append(padded[i * n:i * n + m].reshape(-1))
        else:
            part = padded[i * n:]
            part = np.vstack([part, np.tile(padded[-1], (m - len(part), 1))])
            out.append(part.reshape(-1))
    x = np.stack(out).astype(np.float32)
    return (x + NEG_MEAN) * INV_STD


def logits_for(samples, lang='zh', itn=True, gain=1.0):
    x = features(samples, gain)
    lg = sess.run(['logits'], {'x': x[None], 'x_length': np.array([len(x)], dtype=np.int32),
                               'language': np.array([LANG[lang]], dtype=np.int32),
                               'text_norm': np.array([TEXT_NORM[itn]], dtype=np.int32)})[0][0]
    return lg


def log_softmax(l):
    m = l.max(-1, keepdims=True)
    return l - m - np.log(np.exp(l - m).sum(-1, keepdims=True))


def greedy(lp):
    ids = lp.argmax(-1)
    out, prev = [], -1
    for i in ids:
        if i != prev and i != 0:
            out.append(int(i))
        prev = i
    return out


def text_of(ids):
    return ''.join(TOKENS[i] for i in ids if not TOKENS[i].startswith('<|')).replace('▁', ' ').strip()


def transcribe(samples, **kw):
    return text_of(greedy(log_softmax(logits_for(samples, **kw))))
