// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineSpeakerSegmentationModelConfig
    {
        public static OfflineSpeakerSegmentationModelConfig Default()
        {
            var self = default(OfflineSpeakerSegmentationModelConfig);
            self.Pyannote = OfflineSpeakerSegmentationPyannoteModelConfig.Default();
            self.NumThreads = 1;
            self.Debug = 0;
            self.Provider = "cpu";
            return self;
        }

        public OfflineSpeakerSegmentationPyannoteModelConfig Pyannote;

        /// Number of threads used to run the neural network model
        public int NumThreads;

        /// true to print debug information of the model
        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;
    }
}


