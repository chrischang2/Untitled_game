// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineTtsSupertonicModelConfig
    {
        public static OfflineTtsSupertonicModelConfig Default()
        {
            var self = default(OfflineTtsSupertonicModelConfig);
            self.DurationPredictor = "";
            self.TextEncoder = "";
            self.VectorEstimator = "";
            self.Vocoder = "";
            self.TtsJson = "";
            self.UnicodeIndexer = "";
            self.VoiceStyle = "";
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string DurationPredictor;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TextEncoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string VectorEstimator;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Vocoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TtsJson;

        [MarshalAs(UnmanagedType.LPStr)]
        public string UnicodeIndexer;

        [MarshalAs(UnmanagedType.LPStr)]
        public string VoiceStyle;
    }
}
