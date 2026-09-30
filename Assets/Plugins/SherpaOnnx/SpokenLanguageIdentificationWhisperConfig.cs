// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SpokenLanguageIdentificationWhisperConfig
    {
        public static SpokenLanguageIdentificationWhisperConfig Default()
        {
            var self = default(SpokenLanguageIdentificationWhisperConfig);
            self.Encoder = "";
            self.Decoder = "";
            self.TailPaddings = -1;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        public int TailPaddings;
    }

}