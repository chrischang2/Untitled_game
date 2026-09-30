// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2025  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioTaggingConfig
    {
        public static AudioTaggingConfig Default()
        {
            var self = default(AudioTaggingConfig);
            self.Model = AudioTaggingModelConfig.Default();

            self.Labels = "";
            self.TopK = 5;
            return self;
        }

        public AudioTaggingModelConfig Model;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Labels;

        public int TopK;
    }
}
