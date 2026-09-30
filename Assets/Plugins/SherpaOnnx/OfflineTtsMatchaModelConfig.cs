// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineTtsMatchaModelConfig
    {
        public static OfflineTtsMatchaModelConfig Default()
        {
            var self = default(OfflineTtsMatchaModelConfig);
            self.AcousticModel = "";
            self.Vocoder = "";
            self.Lexicon = "";
            self.Tokens = "";
            self.DataDir = "";

            self.NoiseScale = 0.667F;
            self.LengthScale = 1.0F;

            self.DictDir = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string AcousticModel;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Vocoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Lexicon;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokens;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DataDir;

        public float NoiseScale;
        public float LengthScale;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DictDir;
    }
}
