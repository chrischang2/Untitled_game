// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineSpeechDenoiserModelConfig
    {
        public static OfflineSpeechDenoiserModelConfig Default()
        {
            var self = default(OfflineSpeechDenoiserModelConfig);
            self.Gtcrn = OfflineSpeechDenoiserGtcrnModelConfig.Default();
            self.Dpdfnet = OfflineSpeechDenoiserDpdfNetModelConfig.Default();
            self.NumThreads = 1;
            self.Debug = 0;
            self.Provider = "cpu";
            return self;
        }

        public OfflineSpeechDenoiserGtcrnModelConfig Gtcrn;

        public int NumThreads;

        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;

        public OfflineSpeechDenoiserDpdfNetModelConfig Dpdfnet;
    }
}
