// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2023  Xiaomi Corporation (authors: Fangjun Kuang)
/// Copyright (c)  2023 by manyeyes
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineModelConfig
    {
        public static OnlineModelConfig Default()
        {
            var self = default(OnlineModelConfig);
            self.Transducer = OnlineTransducerModelConfig.Default();
            self.Paraformer = OnlineParaformerModelConfig.Default();
            self.Zipformer2Ctc = OnlineZipformer2CtcModelConfig.Default();
            self.Tokens = "";
            self.NumThreads = 1;
            self.Provider = "cpu";
            self.Debug = 0;
            self.ModelType = "";
            self.ModelingUnit = "cjkchar";
            self.BpeVocab = "";
            self.TokensBuf = "";
            self.TokensBufSize = 0;
            self.NemoCtc = OnlineNemoCtcModelConfig.Default();
            self.ToneCtc = OnlineToneCtcModelConfig.Default();
            return self;
        }

        public OnlineTransducerModelConfig Transducer;
        public OnlineParaformerModelConfig Paraformer;
        public OnlineZipformer2CtcModelConfig Zipformer2Ctc;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokens;

        /// Number of threads used to run the neural network model
        public int NumThreads;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;

        /// true to print debug information of the model
        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string ModelType;

        [MarshalAs(UnmanagedType.LPStr)]
        public string ModelingUnit;

        [MarshalAs(UnmanagedType.LPStr)]
        public string BpeVocab;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TokensBuf;

        public int TokensBufSize;

        public OnlineNemoCtcModelConfig NemoCtc;

        public OnlineToneCtcModelConfig ToneCtc;
    }

}
