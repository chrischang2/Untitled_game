// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineSpeakerSegmentationPyannoteModelConfig
    {
        public static OfflineSpeakerSegmentationPyannoteModelConfig Default()
        {
            var self = default(OfflineSpeakerSegmentationPyannoteModelConfig);
            self.Model = "";
            self.WindowShiftRatio = 0.1f;
            return self;
        }

        [MarshalAs(UnmanagedType.LPStr)]
        public string Model;
        public float WindowShiftRatio;
    }
}
