// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineTtsConfig
    {
        public static OfflineTtsConfig Default()
        {
            var self = default(OfflineTtsConfig);
            self.Model = OfflineTtsModelConfig.Default();
            self.RuleFsts = "";
            self.MaxNumSentences = 1;
            self.RuleFars = "";
            self.SilenceScale = 0.2F;
            return self;
        }
        public OfflineTtsModelConfig Model;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFsts;

        public int MaxNumSentences;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFars;

        public float SilenceScale;
    }
}
