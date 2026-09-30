// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2026  Xiaomi Corporation (authors: Fangjun Kuang)

namespace SherpaOnnx
{
    public struct OnlineSpeechDenoiserConfig
    {
        public static OnlineSpeechDenoiserConfig Default()
        {
            var self = default(OnlineSpeechDenoiserConfig);
            self.Model = OfflineSpeechDenoiserModelConfig.Default();
            return self;
        }

        public OfflineSpeechDenoiserModelConfig Model;
    }
}
