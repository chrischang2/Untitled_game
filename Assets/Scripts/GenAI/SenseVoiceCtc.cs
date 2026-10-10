using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UntitledGame.Language;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// SenseVoice run directly (no sherpa-onnx), so we can see the per-frame probabilities behind the transcript and
    /// offer the runner-up as "did you mean ...?". Frontend = Kaldi fbank (80 bins, 25/10 ms) -> stack 7 frames every 6
    /// -> normalise with the model's own constants, exactly what sherpa-onnx does (checked against it offline).
    /// </summary>
    public sealed class SenseVoiceCtc : IDisposable
    {
        private readonly OrtModel _model;
        private readonly string[] _tokens;
        private readonly float[] _negMean, _invStd;
        private readonly int _langZh, _normItn;
        private readonly float[][] _melBank;   // [80][256]
        private readonly double[] _window = new double[400];

        public const int TagFrames = 4;        // language, event, emotion, text-norm come out first

        public SenseVoiceCtc(string modelDir, int threads)
        {
            _model = new OrtModel(Path.Combine(modelDir, "model.int8.onnx"), threads);
            _negMean = ParseFloats(_model.Metadata("neg_mean"));
            _invStd = ParseFloats(_model.Metadata("inv_stddev"));
            _langZh = int.Parse(_model.Metadata("lang_zh"));
            _normItn = int.Parse(_model.Metadata("with_itn"));
            var toks = new List<string>();
            foreach (string line in File.ReadAllLines(Path.Combine(modelDir, "tokens.txt"), Encoding.UTF8))
            {
                int sp = line.LastIndexOf(' ');
                toks.Add(sp > 0 ? line.Substring(0, sp) : line);
            }
            _tokens = toks.ToArray();
            for (int i = 0; i < 400; i++) _window[i] = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / 399.0);
            _melBank = BuildMelBank();
        }

        private static float[] ParseFloats(string csv)
        {
            string[] p = csv.Split(',');
            var r = new float[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
            return r;
        }

        private static double Mel(double hz) => 1127.0 * Math.Log(1.0 + hz / 700.0);

        private static float[][] BuildMelBank()
        {
            const int bins = 80, fftBins = 256;
            double binWidth = 16000.0 / 512, melLow = Mel(20), melHigh = Mel(8000), delta = (melHigh - melLow) / (bins + 1);
            var bank = new float[bins][];
            for (int b = 0; b < bins; b++)
            {
                double left = melLow + b * delta, center = left + delta, right = center + delta;
                bank[b] = new float[fftBins];
                for (int i = 0; i < fftBins; i++)
                {
                    double mel = Mel(binWidth * i);
                    if (mel > left && mel < right)
                        bank[b][i] = (float)(mel <= center ? (mel - left) / (center - left) : (right - mel) / (right - center));
                }
            }
            return bank;
        }

        private static void Fft(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len, wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }

        /// <summary>Log-mel filterbank frames, one [80] row per 10 ms (Kaldi-compatible, snip_edges).</summary>
        private float[][] Fbank(float[] samples)
        {
            int count = samples.Length < 400 ? 0 : 1 + (samples.Length - 400) / 160;
            var frames = new float[count][];
            var re = new double[512];
            var im = new double[512];
            var w = new double[400];
            for (int f = 0; f < count; f++)
            {
                int start = f * 160;
                double mean = 0;
                for (int i = 0; i < 400; i++) { w[i] = samples[start + i] * 32768.0; mean += w[i]; }
                mean /= 400;
                for (int i = 0; i < 400; i++) w[i] -= mean;
                for (int i = 399; i > 0; i--) w[i] -= 0.97 * w[i - 1];
                w[0] -= 0.97 * w[0];
                for (int i = 0; i < 512; i++) { re[i] = i < 400 ? w[i] * _window[i] : 0; im[i] = 0; }
                Fft(re, im);
                var power = new float[256];
                for (int i = 0; i < 256; i++) power[i] = (float)(re[i] * re[i] + im[i] * im[i]);
                var row = new float[80];
                for (int b = 0; b < 80; b++)
                {
                    double sum = 0;
                    float[] wts = _melBank[b];
                    for (int i = 0; i < 256; i++) if (wts[i] != 0) sum += wts[i] * power[i];
                    row[b] = (float)Math.Log(Math.Max(sum, 1.1920929e-7));
                }
                frames[f] = row;
            }
            return frames;
        }

        private float[] Features(float[] samples, out int lfrFrames)
        {
            float[][] fb = Fbank(samples);
            const int m = 7, n = 6;
            int t = fb.Length;
            lfrFrames = (t + n - 1) / n;
            int padLeft = (m - 1) / 2;
            int tp = t + padLeft;
            var x = new float[lfrFrames * m * 80];
            for (int i = 0; i < lfrFrames; i++)
            {
                for (int k = 0; k < m; k++)
                {
                    int idx = i * n + k;                      // index into the left-padded sequence
                    int src = Math.Min(Math.Max(idx - padLeft, 0), t - 1);
                    float[] row = fb[src];
                    int o = (i * m + k) * 80;
                    for (int d = 0; d < 80; d++)
                    {
                        int col = k * 80 + d;
                        x[o + d] = (row[d] + _negMean[col]) * _invStd[col];
                    }
                }
            }
            return x;
        }

        /// <summary>What the model heard, and where it hesitated.</summary>
        public sealed class Analysis
        {
            public string text;          // best path
            public string alternative;   // best path with its least certain character swapped for the runner-up (or null)
            public string altChar, bestChar;
            public float altProb;        // the runner-up's probability at that character
            public bool? topMatch;       // the expected word is among the model's top 3 guesses at each of its characters, if one was given
            public float? margin;        // log-likelihood of the expected word vs the best reading (0 = as likely), if one was given
            public float[] optionMargins; // the same for each spoken option given (null entries can't be scored: -100)
        }

        // Toneless readings of every single-character token, so sound-alikes (是/四/死) can be told apart from real rivals.
        // Built on the main thread (Pinyin isn't thread-safe), then only read by the recognition thread.
        private HashSet<string>[] _sounds;

        public void PrepareSounds()
        {
            if (_sounds != null || !Pinyin.Ready) return;
            var sounds = new HashSet<string>[_tokens.Length];
            for (int i = 0; i < _tokens.Length; i++)
            {
                string t = _tokens[i];
                if (t.Length != 1 || t[0] < 0x4E00 || t[0] > 0x9FFF) continue;
                var r = Pinyin.PlainReadings(t[0]);
                if (r.Count > 0) sounds[i] = new HashSet<string>(r);
            }
            _sounds = sounds;
        }

        private bool Alike(int a, int b)
        {
            if (_sounds == null || a < 0 || b < 0) return false;
            var x = _sounds[a];
            var y = _sounds[b];
            return x != null && y != null && x.Overlaps(y);
        }

        private readonly Dictionary<char, int[]> _charClasses = new Dictionary<char, int[]>();

        /// <summary>Per character of the expected word (or phrase): every token that sounds like it (tones ignored, like the grader). Main thread.</summary>
        public int[][] ClassesFor(string word)
        {
            PrepareSounds();
            if (_sounds == null || string.IsNullOrEmpty(word) || word.Length > 16) return null;
            var classes = new int[word.Length][];
            for (int k = 0; k < word.Length; k++)
            {
                char ch = word[k];
                if (!_charClasses.TryGetValue(ch, out var cls))
                {
                    var want = Pinyin.PlainReadings(ch);
                    var ids = new List<int>();
                    for (int i = 0; i < _sounds.Length; i++)
                    {
                        var set = _sounds[i];
                        if (set == null) continue;
                        bool hit = _tokens[i][0] == ch;
                        if (!hit) foreach (var r in want) if (set.Contains(r)) { hit = true; break; }
                        if (hit) ids.Add(i);
                    }
                    cls = ids.ToArray();
                    _charClasses[ch] = cls;
                }
                if (cls.Length == 0) return null;
                classes[k] = cls;
            }
            return classes;
        }

        private static double Lse(double a, double b)
        {
            if (a < b) { double t = a; a = b; b = t; }
            return b <= -1e8 ? a : a + Math.Log(1 + Math.Exp(b - a));
        }

        /// <summary>
        /// How likely the audio is the expected word (summing every way of lining it up, homophones allowed), compared with the
        /// model's own best reading: 0 = just as likely, -3 = e^-3 (5%) as likely.
        /// </summary>
        private static float Margin(float[] lg, int total, int vocab, double[] logZ, float[] frameMax, int[][] classes)
        {
            int T = total - TagFrames, L = classes.Length, S = 2 * L + 1;
            if (T < L) return -100f;
            double bestPath = 0;
            for (int f = 0; f < T; f++) bestPath += -logZ[f];
            var emit = new double[T, L];
            var blank = new double[T];
            for (int f = 0; f < T; f++)
            {
                int off = (f + TagFrames) * vocab;
                double max = frameMax[f];
                blank[f] = lg[off] - max - logZ[f];
                for (int k = 0; k < L; k++)
                {
                    double acc = -1e9;
                    foreach (int id in classes[k]) acc = Lse(acc, lg[off + id] - max - logZ[f]);
                    emit[f, k] = acc;
                }
            }
            var skipOk = new bool[L];
            for (int k = 1; k < L; k++) skipOk[k] = !classes[k].SequenceEqual(classes[k - 1]);
            var prev = new double[S];
            var cur = new double[S];
            for (int s = 0; s < S; s++) prev[s] = -1e9;
            prev[0] = blank[0];
            prev[1] = emit[0, 0];
            for (int f = 1; f < T; f++)
            {
                for (int s = 0; s < S; s++)
                {
                    double a = prev[s];
                    if (s >= 1) a = Lse(a, prev[s - 1]);
                    if (s >= 2 && s % 2 == 1 && skipOk[s / 2]) a = Lse(a, prev[s - 2]);
                    cur[s] = a + (s % 2 == 0 ? blank[f] : emit[f, s / 2]);
                }
                (prev, cur) = (cur, prev);
            }
            return (float)(Lse(prev[S - 1], prev[S - 2]) - bestPath);
        }

        /// <summary>
        /// The expected word is "in the top k": at each of its characters (in a row, among the emitted characters) one of the
        /// model's k best guesses at that spot sounds right. A lone character must be nearly all that was said.
        /// </summary>
        private bool TopMatch(float[] lg, int vocab, List<int> peakFrames, int[][] classes, int k)
        {
            int n = classes.Length, count = peakFrames.Count;
            if (count < n || (n == 1 && count > 3)) return false;
            var tops = new int[count][];
            for (int i = 0; i < count; i++)
            {
                int off = peakFrames[i] * vocab;
                var best = new int[k];
                var score = new float[k];
                for (int j = 0; j < k; j++) { best[j] = -1; score[j] = float.MinValue; }
                for (int v = 1; v < vocab; v++)
                {
                    if (IsTag(v)) continue;
                    float x = lg[off + v];
                    if (x <= score[k - 1]) continue;
                    int at = k - 1;
                    while (at > 0 && x > score[at - 1]) { score[at] = score[at - 1]; best[at] = best[at - 1]; at--; }
                    score[at] = x; best[at] = v;
                }
                tops[i] = best;
            }
            for (int start = 0; start + n <= count; start++)
            {
                bool all = true;
                for (int c = 0; c < n && all; c++)
                {
                    bool hit = false;
                    foreach (int id in tops[start + c]) if (id > 0 && Array.IndexOf(classes[c], id) >= 0) { hit = true; break; }
                    all = hit;
                }
                if (all) return true;
            }
            return false;
        }

        private bool IsTag(int id) => id < 0 || id >= _tokens.Length || _tokens[id].StartsWith("<|");

        private static bool IsWordToken(string t)
        {
            if (string.IsNullOrEmpty(t)) return false;
            foreach (char c in t) if (char.IsLetterOrDigit(c)) return true;
            return false;
        }

        public Analysis Analyze(float[] samples16k, int[][] expectedClasses = null, IList<int[][]> options = null, float minAltProb = 0.02f)
        {
            if (samples16k == null || samples16k.Length < 800) return null;
            float[] x = Features(samples16k, out int frames);
            var outp = _model.Run(new[]
            {
                new OrtModel.Input { name = "x", data = x, shape = new long[] { 1, frames, 560 } },
                new OrtModel.Input { name = "x_length", data = new[] { frames }, shape = new long[] { 1 } },
                new OrtModel.Input { name = "language", data = new[] { _langZh }, shape = new long[] { 1 } },
                new OrtModel.Input { name = "text_norm", data = new[] { _normItn }, shape = new long[] { 1 } },
            }, "logits");
            int total = (int)outp.shape[1], vocab = (int)outp.shape[2];
            float[] lg = outp.data;

            // Greedy path, with the strongest probability of each emitted character and the best rival frame-wise.
            var ids = new List<int>();
            var conf = new List<float>();
            var rival = new List<int>();
            var rivalP = new List<float>();
            var peak = new List<int>();   // the frame where each emitted character was strongest
            int prev = -1;
            var logZ = new double[Math.Max(0, total - TagFrames)];
            var frameMax = new float[Math.Max(0, total - TagFrames)];
            for (int f = TagFrames; f < total; f++)
            {
                int off = f * vocab;
                float max = float.MinValue;
                int arg = 0;
                for (int v = 0; v < vocab; v++) if (lg[off + v] > max) { max = lg[off + v]; arg = v; }
                double sum = 0;
                for (int v = 0; v < vocab; v++) sum += Math.Exp(lg[off + v] - max);
                float pBest = (float)(1.0 / sum);
                logZ[f - TagFrames] = Math.Log(sum);
                frameMax[f - TagFrames] = max;
                if (arg != 0 && arg != prev)
                {
                    ids.Add(arg); conf.Add(pBest); rival.Add(-1); rivalP.Add(0); peak.Add(f);
                }
                if (arg != 0 && ids.Count > 0)
                {
                    int s = ids.Count - 1;
                    if (arg == ids[s] && pBest > conf[s]) { conf[s] = pBest; peak[s] = f; }
                    // best different, non-blank, non-tag, wordlike token at this frame
                    float bestOther = 0; int bestId = -1;
                    for (int v = 1; v < vocab; v++)
                    {
                        if (v == arg || IsTag(v) || Alike(v, arg)) continue;
                        float p = (float)(Math.Exp(lg[off + v] - max) / sum);
                        if (p > bestOther && IsWordToken(_tokens[v])) { bestOther = p; bestId = v; }
                    }
                    if (bestId >= 0 && bestOther > rivalP[s]) { rival[s] = bestId; rivalP[s] = bestOther; }
                }
                prev = arg;
            }

            var res = new Analysis { text = Join(ids, -1, -1) };
            if (options != null)
            {
                res.optionMargins = new float[options.Count];
                for (int i = 0; i < options.Count; i++)
                    res.optionMargins[i] = options[i] == null ? -100f : Margin(lg, total, vocab, logZ, frameMax, options[i]);
            }
            if (expectedClasses != null)
            {
                res.margin = Margin(lg, total, vocab, logZ, frameMax, expectedClasses);
                res.topMatch = TopMatch(lg, vocab, peak, expectedClasses, 3);
            }
            int weakest = -1;
            float weakConf = 2f;
            for (int i = 0; i < ids.Count; i++)
            {
                if (IsTag(ids[i]) || !IsWordToken(_tokens[ids[i]]) || rival[i] < 0 || rivalP[i] < minAltProb) continue;
                if (conf[i] < weakConf) { weakConf = conf[i]; weakest = i; }
            }
            if (weakest >= 0)
            {
                res.alternative = Join(ids, weakest, rival[weakest]);
                res.bestChar = _tokens[ids[weakest]];
                res.altChar = _tokens[rival[weakest]];
                res.altProb = rivalP[weakest];
                if (res.alternative == res.text) res.alternative = null;
            }
            return res;
        }

        private string Join(List<int> ids, int swapAt, int swapTo)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                int id = i == swapAt ? swapTo : ids[i];
                if (IsTag(id)) continue;
                sb.Append(_tokens[id].Replace('▁', ' '));
            }
            return sb.ToString().Trim();
        }

        public void Dispose() => _model.Dispose();
    }
}
