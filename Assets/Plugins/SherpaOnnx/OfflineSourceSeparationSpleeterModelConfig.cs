// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineSourceSeparationSpleeterModelConfig
    {
        public static OfflineSourceSeparationSpleeterModelConfig Default()
        {
            var self = default(OfflineSourceSeparationSpleeterModelConfig);
            self.Vocals = "";
            self.Accompaniment = "";
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Vocals;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Accompaniment;
    }
}
