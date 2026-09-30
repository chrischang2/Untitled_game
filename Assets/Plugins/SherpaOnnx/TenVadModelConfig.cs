// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct TenVadModelConfig
    {
        public static TenVadModelConfig Default()
        {
            var self = default(TenVadModelConfig);
            self.Model = "";
            self.Threshold = 0.5F;
            self.MinSilenceDuration = 0.5F;
            self.MinSpeechDuration = 0.25F;
            self.WindowSize = 256;
            self.MaxSpeechDuration = 5.0F;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;

        public float Threshold;

        public float MinSilenceDuration;

        public float MinSpeechDuration;

        public int WindowSize;

        public float MaxSpeechDuration;
    }
}
