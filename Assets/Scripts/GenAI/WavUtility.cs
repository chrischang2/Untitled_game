using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UntitledGame.GenAI
{
    /// <summary>Minimal PCM16 WAV encode/decode for mic capture (to Whisper) and Piper output.</summary>
    public static class WavUtility
    {
        public static byte[] EncodePcm16(float[] samples, int sampleRate, int channels = 1)
        {
            using var ms = new MemoryStream(44 + samples.Length * 2);
            using var w = new BinaryWriter(ms);
            int dataBytes = samples.Length * 2;
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            foreach (float s in samples)
            {
                w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), short.MinValue, short.MaxValue));
            }
            return ms.ToArray();
        }

        public struct PcmData
        {
            public float[] samples; // interleaved
            public int sampleRate;
            public int channels;
        }

        public static bool TryDecode(byte[] bytes, out PcmData pcm)
        {
            pcm = default;
            if (bytes == null || bytes.Length < 44) return false;
            if (Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") return false;

            int pos = 12;
            int channels = 1, rate = 22050, bits = 16;
            int dataStart = -1, dataLen = 0;
            while (pos + 8 <= bytes.Length)
            {
                string id = Encoding.ASCII.GetString(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (id == "data")
                {
                    dataStart = body;
                    dataLen = Math.Min(size, bytes.Length - body);
                    break;
                }
                pos = body + size + (size & 1);
            }
            if (dataStart < 0 || bits != 16) return false;

            int count = dataLen / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                samples[i] = BitConverter.ToInt16(bytes, dataStart + i * 2) / 32768f;
            }
            pcm = new PcmData { samples = samples, sampleRate = rate, channels = Mathf.Max(1, channels) };
            return true;
        }

        public static AudioClip ToClip(PcmData pcm, string name)
        {
            int frames = pcm.samples.Length / pcm.channels;
            if (frames <= 0) return null;
            var clip = AudioClip.Create(name, frames, pcm.channels, pcm.sampleRate, false);
            clip.SetData(pcm.samples, 0);
            return clip;
        }

        /// <summary>Linear resample of mono audio.</summary>
        public static float[] Resample(float[] src, int fromRate, int toRate)
        {
            if (fromRate == toRate || src.Length == 0) return src;
            double ratio = (double)fromRate / toRate;
            int n = (int)(src.Length / ratio);
            var dst = new float[n];
            for (int i = 0; i < n; i++)
            {
                double p = i * ratio;
                int a = (int)p;
                int b = Math.Min(a + 1, src.Length - 1);
                float t = (float)(p - a);
                dst[i] = src[a] * (1 - t) + src[b] * t;
            }
            return dst;
        }
    }
}
