// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2023  Xiaomi Corporation (authors: Fangjun Kuang)
/// Copyright (c)  2023 by manyeyes
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    /// It expects 16 kHz 16-bit single channel wave format.
    [StructLayout(LayoutKind.Sequential)]
    public struct FeatureConfig
    {
        public static FeatureConfig Default()
        {
            var self = default(FeatureConfig);
            self.SampleRate = 16000;
            self.FeatureDim = 80;
            return self;
        }
        /// Sample rate of the input data. MUST match the one expected
        /// by the model. For instance, it should be 16000 for models provided
        /// by us.
        public int SampleRate;

        /// Feature dimension of the model.
        /// For instance, it should be 80 for models provided by us.
        public int FeatureDim;
    }

}