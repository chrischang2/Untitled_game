using System;
using System.Collections;
using UnityEngine;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// Push-to-talk capture. The microphone is opened on first use and kept warm (ring buffer) so the
    /// start of each sentence isn't clipped; it's released again after a while of silence.
    /// </summary>
    public class MicRecorder : MonoBehaviour
    {
        public const int TargetRate = 16000;
        private const int BufferSeconds = 30;
        private const float PreRollSeconds = 0.2f;
        private const float TailSeconds = 0.3f;
        private const float IdleReleaseSeconds = 90f;

        private string _device;
        private AudioClip _clip;
        private int _rate = TargetRate;
        private int _startPos;
        private float _lastUse;
        private readonly float[] _levelBuf = new float[512];

        public bool HasMicrophone => Microphone.devices.Length > 0;
        public bool IsRecording { get; private set; }
        public bool IsFinishing { get; private set; }
        public float Level { get; private set; }
        /// <summary>Unsmoothed RMS of the latest ~30 ms (for voice activity detection).</summary>
        public float RawRms { get; private set; }
        /// <summary>Keep the microphone open even when idle (hands-free mode).</summary>
        public bool KeepOpen { get; set; }
        public bool IsOpen => _clip != null && Microphone.IsRecording(_device);
        private float _tail = TailSeconds;
        public float RecordingSeconds => IsRecording ? Time.realtimeSinceStartup - _recordStart : 0f;
        public string DeviceName => string.IsNullOrEmpty(_device) ? (HasMicrophone ? Microphone.devices[0] : "none") : _device;

        private float _recordStart;

        private bool EnsureMic()
        {
            if (!HasMicrophone) return false;
            if (_clip != null && Microphone.IsRecording(_device)) return true;
            _device = Microphone.devices[0];
            Microphone.GetDeviceCaps(_device, out int minRate, out int maxRate);
            _rate = (minRate == 0 && maxRate == 0) ? TargetRate : Mathf.Clamp(TargetRate, minRate, maxRate);
            _clip = Microphone.Start(_device, true, BufferSeconds, _rate);
            return _clip != null;
        }

        /// <summary>Opens the mic without recording (hands-free listening).</summary>
        public bool Open() => EnsureMic();

        public bool Begin() => Begin(PreRollSeconds, TailSeconds);

        public bool Begin(float preRollSeconds, float tailSeconds)
        {
            if (IsRecording || IsFinishing) return false;
            if (!EnsureMic()) return false;
            _tail = tailSeconds;
            int pos = Microphone.GetPosition(_device);
            int pre = Mathf.RoundToInt(preRollSeconds * _rate);
            _startPos = (pos - pre + _clip.samples) % _clip.samples;
            IsRecording = true;
            _recordStart = Time.realtimeSinceStartup;
            _lastUse = Time.realtimeSinceStartup;
            return true;
        }

        /// <summary>Stops after a short tail and returns 16 kHz mono samples.</summary>
        public void End(Action<float[]> onSamples)
        {
            if (!IsRecording) return;
            StartCoroutine(Finish(onSamples));
        }

        public void CancelRecording()
        {
            IsRecording = false;
        }

        private IEnumerator Finish(Action<float[]> onSamples)
        {
            IsFinishing = true;
            if (_tail > 0f) yield return new WaitForSecondsRealtime(_tail);
            IsRecording = false;
            IsFinishing = false;
            _lastUse = Time.realtimeSinceStartup;
            if (_clip == null)
            {
                onSamples?.Invoke(Array.Empty<float>());
                yield break;
            }
            int end = Microphone.GetPosition(_device);
            int total = _clip.samples;
            int length = (end - _startPos + total) % total;
            if (length <= 0)
            {
                onSamples?.Invoke(Array.Empty<float>());
                yield break;
            }
            var data = new float[length * _clip.channels];
            _clip.GetData(data, _startPos);
            if (_clip.channels > 1)
            {
                var mono = new float[length];
                for (int i = 0; i < length; i++) mono[i] = data[i * _clip.channels];
                data = mono;
            }
            onSamples?.Invoke(WavUtility.Resample(data, _rate, TargetRate));
        }

        private void Update()
        {
            if (_clip != null && Microphone.IsRecording(_device))
            {
                int pos = Microphone.GetPosition(_device) - _levelBuf.Length;
                if (pos < 0) pos += _clip.samples;
                _clip.GetData(_levelBuf, pos);
                float sum = 0f;
                foreach (float s in _levelBuf) sum += s * s;
                float rms = Mathf.Sqrt(sum / _levelBuf.Length);
                RawRms = rms;
                float target = Mathf.Clamp01(rms * 12f);
                Level = Mathf.Lerp(Level, target, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));

                if (!KeepOpen && !IsRecording && !IsFinishing && Time.realtimeSinceStartup - _lastUse > IdleReleaseSeconds)
                {
                    Microphone.End(_device);
                    _clip = null;
                    Level = 0f;
                }
            }
            else
            {
                Level = 0f;
                RawRms = 0f;
            }
        }

        private void OnDisable()
        {
            if (_clip != null) Microphone.End(_device);
            _clip = null;
        }

        /// <summary>Peak RMS over 30 ms windows - used to reject silent recordings.</summary>
        public static float PeakRms(float[] samples)
        {
            const int win = 480;
            float peak = 0f;
            for (int i = 0; i + win <= samples.Length; i += win)
            {
                float sum = 0f;
                for (int j = 0; j < win; j++) sum += samples[i + j] * samples[i + j];
                peak = Mathf.Max(peak, Mathf.Sqrt(sum / win));
            }
            return peak;
        }
    }
}
