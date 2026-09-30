// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineTtsZipVoiceModelConfig
    {
        public static OfflineTtsZipVoiceModelConfig Default()
        {
            var self = default(OfflineTtsZipVoiceModelConfig);
            self.Tokens = "";
            self.Encoder = "";
            self.Decoder = "";
            self.Vocoder = "";
            self.DataDir = "";
            self.Lexicon = "";

            self.FeatScale = 0.1F;
            self.Tshift = 0.5F;
            self.TargetRms = 0.1F;
            self.GuidanceScale = 1.0F;
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokens;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Vocoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DataDir;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Lexicon;

        public float FeatScale;
        public float Tshift;
        public float TargetRms;
        public float GuidanceScale;
    }
}
