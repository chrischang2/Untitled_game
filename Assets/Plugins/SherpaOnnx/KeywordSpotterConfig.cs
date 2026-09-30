// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct KeywordSpotterConfig
    {
        public static KeywordSpotterConfig Default()
        {
            var self = default(KeywordSpotterConfig);
            self.FeatConfig = FeatureConfig.Default();
            self.ModelConfig = OnlineModelConfig.Default();

            self.MaxActivePaths = 4;
            self.NumTrailingBlanks = 1;
            self.KeywordsScore = 1.0F;
            self.KeywordsThreshold = 0.25F;
            self.KeywordsFile = "";
            self.KeywordsBuf = "";
            self.KeywordsBufSize = 0;
            return self;
        }
        public FeatureConfig FeatConfig;
        public OnlineModelConfig ModelConfig;

        public int MaxActivePaths;
        public int NumTrailingBlanks;
        public float KeywordsScore;
        public float KeywordsThreshold;

        [MarshalAs(UnmanagedType.LPStr)]
        public string KeywordsFile;

        [MarshalAs(UnmanagedType.LPStr)]
        public string KeywordsBuf;

        public int KeywordsBufSize;
    }
}
