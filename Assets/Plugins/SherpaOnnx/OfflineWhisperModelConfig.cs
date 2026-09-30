// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineWhisperModelConfig
    {
        public static OfflineWhisperModelConfig Default()
        {
            var self = default(OfflineWhisperModelConfig);
            self.Encoder = "";
            self.Decoder = "";
            self.Language = "";
            self.Task = "transcribe";
            self.TailPaddings = -1;
            self.EnableTokenTimestamps = 0;
            self.EnableSegmentTimestamps = 0;
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Decoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Language;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Task;

        public int TailPaddings;
        public int EnableTokenTimestamps;
        public int EnableSegmentTimestamps;
    }

}
