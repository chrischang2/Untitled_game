// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineRecognizerConfig
    {
        public static OfflineRecognizerConfig Default()
        {
            var self = default(OfflineRecognizerConfig);
            self.FeatConfig = FeatureConfig.Default();
            self.ModelConfig = OfflineModelConfig.Default();
            self.LmConfig = OfflineLMConfig.Default();

            self.DecodingMethod = "greedy_search";
            self.MaxActivePaths = 4;
            self.HotwordsFile = "";
            self.HotwordsScore = 1.5F;
            self.RuleFsts = "";
            self.RuleFars = "";
            self.BlankPenalty = 0.0F;
            self.Hr = HomophoneReplacerConfig.Default();
            return self;
        }
        public FeatureConfig FeatConfig;
        public OfflineModelConfig ModelConfig;
        public OfflineLMConfig LmConfig;

        [MarshalAs(UnmanagedType.LPStr)]
        public string DecodingMethod;

        public int MaxActivePaths;

        [MarshalAs(UnmanagedType.LPStr)]
        public string HotwordsFile;

        public float HotwordsScore;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFsts;

        [MarshalAs(UnmanagedType.LPStr)]
        public string RuleFars;

        public float BlankPenalty;

        public HomophoneReplacerConfig Hr;
    }
}
