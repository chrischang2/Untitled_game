// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineQwen3AsrModelConfig
    {
        public static OfflineQwen3AsrModelConfig Default()
        {
            var self = default(OfflineQwen3AsrModelConfig);
            self.ConvFrontend = "";
            self.Encoder = "";
            self.Decoder = "";
            self.Tokenizer = "";
            self.MaxTotalLen = 512;
            self.MaxNewTokens = 128;
            self.Temperature = 1e-6F;
            self.TopP = 0.8F;
            self.Seed = 42;
            self.Hotwords = "";
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string ConvFrontend;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokenizer;

        public int MaxTotalLen;
        public int MaxNewTokens;
        public float Temperature;
        public float TopP;
        public int Seed;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Hotwords;
    }
}
