// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    public struct SpokenLanguageIdentificationConfig
    {
        public static SpokenLanguageIdentificationConfig Default()
        {
            var self = default(SpokenLanguageIdentificationConfig);
            self.Whisper = SpokenLanguageIdentificationWhisperConfig.Default();
            self.NumThreads = 1;
            self.Debug = 0;
            self.Provider = "cpu";
            return self;
        }
        public SpokenLanguageIdentificationWhisperConfig Whisper;

        public int NumThreads;
        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;
    }

}