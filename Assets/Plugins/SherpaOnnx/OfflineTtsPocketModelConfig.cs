// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineTtsPocketModelConfig
    {
        // Default constructor for convenience
        public static OfflineTtsPocketModelConfig Default()
        {
            var self = default(OfflineTtsPocketModelConfig);
            self.LmFlow = "";
            self.LmMain = "";
            self.Encoder = "";
            self.Decoder = "";
            self.TextConditioner = "";
            self.VocabJson = "";
            self.TokenScoresJson = "";
            self.VoiceEmbeddingCacheCapacity = 50;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string LmFlow;

        [MarshalAs(UnmanagedType.LPStr)]
        public string LmMain;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TextConditioner;

        [MarshalAs(UnmanagedType.LPStr)]
        public string VocabJson;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TokenScoresJson;

        public int VoiceEmbeddingCacheCapacity;
    }
}

