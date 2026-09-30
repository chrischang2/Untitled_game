// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VadModelConfig
    {
        public static VadModelConfig Default()
        {
            var self = default(VadModelConfig);
            self.SileroVad = SileroVadModelConfig.Default();
            self.SampleRate = 16000;
            self.NumThreads = 1;
            self.Provider = "cpu";
            self.Debug = 0;
            self.TenVad = TenVadModelConfig.Default();
            return self;
        }

        public SileroVadModelConfig SileroVad;

        public int SampleRate;

        public int NumThreads;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;

        public int Debug;

        public TenVadModelConfig TenVad;
    }
}

