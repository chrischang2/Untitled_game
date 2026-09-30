// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineSenseVoiceModelConfig
    {
        public static OfflineSenseVoiceModelConfig Default()
        {
            var self = default(OfflineSenseVoiceModelConfig);
            self.Model = "";
            self.Language = "";
            self.UseInverseTextNormalization = 0;
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Language;

        public int UseInverseTextNormalization;
    }
}
