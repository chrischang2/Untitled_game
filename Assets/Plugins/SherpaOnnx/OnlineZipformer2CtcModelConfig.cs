// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2023  Xiaomi Corporation (authors: Fangjun Kuang)
/// Copyright (c)  2023 by manyeyes
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OnlineZipformer2CtcModelConfig
    {
        public static OnlineZipformer2CtcModelConfig Default()
        {
            var self = default(OnlineZipformer2CtcModelConfig);
            self.Model = "";
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;
    }
}