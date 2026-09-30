// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineWenetCtcModelConfig
    {
        public static OfflineWenetCtcModelConfig Default()
        {
            var self = default(OfflineWenetCtcModelConfig);
            self.Model = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;
    }
}
