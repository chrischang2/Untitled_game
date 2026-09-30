// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioTaggingModelConfig
    {
        public static AudioTaggingModelConfig Default()
        {
            var self = default(AudioTaggingModelConfig);
            self.Zipformer = OfflineZipformerAudioTaggingModelConfig.Default();

            self.CED = "";
            self.NumThreads = 1;
            self.Debug = 0;
            self.Provider = "cpu";
            return self;
        }

        public OfflineZipformerAudioTaggingModelConfig Zipformer;

        [MarshalAs(UnmanagedType.LPStr)]
        public string CED;

        public int NumThreads;

        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;
    }
}
