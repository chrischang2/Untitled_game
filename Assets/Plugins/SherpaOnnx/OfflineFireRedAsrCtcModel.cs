// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineFireRedAsrCtcModelConfig
    {
        public static OfflineFireRedAsrCtcModelConfig Default()
        {
            var self = default(OfflineFireRedAsrCtcModelConfig);
            self.Model = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;
    }
}
