// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineFunAsrNanoModelConfig
    {
        public static OfflineFunAsrNanoModelConfig Default()
        {
            var self = default(OfflineFunAsrNanoModelConfig);
            self.EncoderAdaptor = "";
            self.LLM = "";
            self.Embedding = "";
            self.Tokenizer = "";
            self.SystemPrompt = "You are a helpful assistant.";
            self.UserPrompt = "语音转写：";
            self.MaxNewTokens = 512;
            self.Temperature = 1e-6F;
            self.TopP = 0.8F;
            self.Seed = 42;
            self.Language = "";
            self.Itn = 0;
            self.Hotwords = "";
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string EncoderAdaptor;

        [MarshalAs(UnmanagedType.LPStr)]
        public string LLM;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Embedding;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokenizer;

        [MarshalAs(UnmanagedType.LPStr)]
        public string SystemPrompt;

        [MarshalAs(UnmanagedType.LPStr)]
        public string UserPrompt;

        public int MaxNewTokens;
        public float Temperature;
        public float TopP;
        public int Seed;
        [MarshalAs(UnmanagedType.LPStr)]
        public string Language;

        public int Itn;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Hotwords;
    }
}
