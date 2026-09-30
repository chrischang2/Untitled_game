// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineCohereTranscribeModelConfig
    {
        public static OfflineCohereTranscribeModelConfig Default()
        {
            var self = default(OfflineCohereTranscribeModelConfig);
            self.Encoder = "";
            self.Decoder = "";
            self.Language = "";
            self.UsePunct = 1;
            self.UseItn = 1;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Language;

        public int UsePunct;
        public int UseItn;
    }
}
