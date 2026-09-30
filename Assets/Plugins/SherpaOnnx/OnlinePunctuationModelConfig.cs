// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OnlinePunctuationModelConfig
    {
        public static OnlinePunctuationModelConfig Default()
        {
            var self = default(OnlinePunctuationModelConfig);
            self.CnnBiLstm = "";
            self.BpeVocab = "";
            self.NumThreads = 1;
            self.Debug = 0;
            self.Provider = "cpu";
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string CnnBiLstm;

        [MarshalAs(UnmanagedType.LPStr)]
        public string BpeVocab;

        public int NumThreads;

        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;
    }
}
