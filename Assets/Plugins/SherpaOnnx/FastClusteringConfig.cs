// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct FastClusteringConfig
    {
        public static FastClusteringConfig Default()
        {
            var self = default(FastClusteringConfig);
            self.NumClusters = -1;
            self.Threshold = 0.5F;
            self.ComputeConfidence = 0;
            return self;
        }

        public int NumClusters;
        public float Threshold;
        public int ComputeConfidence;
    }
}
