// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineSpeakerDiarizationConfig
    {
        public static OfflineSpeakerDiarizationConfig Default()
        {
            var self = default(OfflineSpeakerDiarizationConfig);
            self.Segmentation = OfflineSpeakerSegmentationModelConfig.Default();
            self.Embedding = SpeakerEmbeddingExtractorConfig.Default();
            self.Clustering = FastClusteringConfig.Default();

            self.MinDurationOn = 0.3F;
            self.MinDurationOff = 0.5F;
            return self;
        }

        public OfflineSpeakerSegmentationModelConfig Segmentation;
        public SpeakerEmbeddingExtractorConfig Embedding;
        public FastClusteringConfig Clustering;

        public float MinDurationOn;
        public float MinDurationOff;
    }
}



