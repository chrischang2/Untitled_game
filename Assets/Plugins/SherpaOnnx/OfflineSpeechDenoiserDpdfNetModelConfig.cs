// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineSpeechDenoiserDpdfNetModelConfig
    {
        public static OfflineSpeechDenoiserDpdfNetModelConfig Default()
        {
            var self = default(OfflineSpeechDenoiserDpdfNetModelConfig);
            self.Model = "";
            self.AttenuationLimitDb = 0.0f;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;
        public float AttenuationLimitDb;
    }
}
