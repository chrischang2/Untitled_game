// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineTtsVitsModelConfig
    {
        public static OfflineTtsVitsModelConfig Default()
        {
            var self = default(OfflineTtsVitsModelConfig);
            self.Model = "";
            self.Lexicon = "";
            self.Tokens = "";
            self.DataDir = "";

            self.NoiseScale = 0.667F;
            self.NoiseScaleW = 0.8F;
            self.LengthScale = 1.0F;

            self.DictDir = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Lexicon;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokens;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DataDir;

        public float NoiseScale;
        public float NoiseScaleW;
        public float LengthScale;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DictDir;
    }
}