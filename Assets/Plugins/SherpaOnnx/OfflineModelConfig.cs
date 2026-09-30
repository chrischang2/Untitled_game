// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024.5 by 东风破

using System.Runtime.InteropServices;

namespace SherpaOnnx
{

    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineModelConfig
    {
        public static OfflineModelConfig Default()
        {
            var self = default(OfflineModelConfig);
            self.Transducer = OfflineTransducerModelConfig.Default();
            self.Paraformer = OfflineParaformerModelConfig.Default();
            self.NeMoCtc = OfflineNemoEncDecCtcModelConfig.Default();
            self.Whisper = OfflineWhisperModelConfig.Default();
            self.Tdnn = OfflineTdnnModelConfig.Default();
            self.Tokens = "";
            self.NumThreads = 1;
            self.Debug = 0;
            self.Provider = "cpu";
            self.ModelType = "";
            self.ModelingUnit = "cjkchar";
            self.BpeVocab = "";
            self.TeleSpeechCtc = "";
            self.SenseVoice = OfflineSenseVoiceModelConfig.Default();
            self.Moonshine = OfflineMoonshineModelConfig.Default();
            self.FireRedAsr = OfflineFireRedAsrModelConfig.Default();
            self.Dolphin = OfflineDolphinModelConfig.Default();
            self.ZipformerCtc = OfflineZipformerCtcModelConfig.Default();
            self.Canary = OfflineCanaryModelConfig.Default();
            self.WenetCtc = OfflineWenetCtcModelConfig.Default();
            self.Omnilingual = OfflineOmnilingualAsrCtcModelConfig.Default();
            self.MedAsr = OfflineMedAsrCtcModelConfig.Default();
            self.FunAsrNano = OfflineFunAsrNanoModelConfig.Default();
            self.FireRedAsrCtc = OfflineFireRedAsrCtcModelConfig.Default();
            self.Qwen3Asr = OfflineQwen3AsrModelConfig.Default();
            self.CohereTranscribe = OfflineCohereTranscribeModelConfig.Default();
            return self;
        }
        public OfflineTransducerModelConfig Transducer;
        public OfflineParaformerModelConfig Paraformer;
        public OfflineNemoEncDecCtcModelConfig NeMoCtc;
        public OfflineWhisperModelConfig Whisper;
        public OfflineTdnnModelConfig Tdnn;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Tokens;

        public int NumThreads;

        public int Debug;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Provider;

        [MarshalAs(UnmanagedType.LPStr)]
        public string ModelType;

        [MarshalAs(UnmanagedType.LPStr)]
        public string ModelingUnit;

        [MarshalAs(UnmanagedType.LPStr)]
        public string BpeVocab;

        [MarshalAs(UnmanagedType.LPStr)]
        public string TeleSpeechCtc;

        public OfflineSenseVoiceModelConfig SenseVoice;
        public OfflineMoonshineModelConfig Moonshine;
        public OfflineFireRedAsrModelConfig FireRedAsr;
        public OfflineDolphinModelConfig Dolphin;
        public OfflineZipformerCtcModelConfig ZipformerCtc;
        public OfflineCanaryModelConfig Canary;
        public OfflineWenetCtcModelConfig WenetCtc;
        public OfflineOmnilingualAsrCtcModelConfig Omnilingual;
        public OfflineMedAsrCtcModelConfig MedAsr;
        public OfflineFunAsrNanoModelConfig FunAsrNano;
        public OfflineFireRedAsrCtcModelConfig FireRedAsrCtc;
        public OfflineQwen3AsrModelConfig Qwen3Asr;
        public OfflineCohereTranscribeModelConfig CohereTranscribe;
    }
}
