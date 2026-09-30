// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineTtsModelConfig
    {
        public static OfflineTtsModelConfig Default()
        {
            var self = default(OfflineTtsModelConfig);
            self.Vits = OfflineTtsVitsModelConfig.Default();
            self.Matcha = OfflineTtsMatchaModelConfig.Default();
            self.Kokoro = OfflineTtsKokoroModelConfig.Default();
            self.Kitten = OfflineTtsKittenModelConfig.Default();
            self.ZipVoice = OfflineTtsZipVoiceModelConfig.Default();
            self.Pocket = OfflineTtsPocketModelConfig.Default();
            self.Supertonic = OfflineTtsSupertonicModelConfig.Default();
            self.NumThreads = 1;
            self.Debug = 0;
            self.Provider = "cpu";
            return self;
        }

        public OfflineTtsVitsModelConfig Vits;
        public int NumThreads;
        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;

        public OfflineTtsMatchaModelConfig Matcha;
        public OfflineTtsKokoroModelConfig Kokoro;
        public OfflineTtsKittenModelConfig Kitten;
        public OfflineTtsZipVoiceModelConfig ZipVoice;
        public OfflineTtsPocketModelConfig Pocket;
        public OfflineTtsSupertonicModelConfig Supertonic;
    }
}
