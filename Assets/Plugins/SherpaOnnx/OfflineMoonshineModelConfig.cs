// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024-2026  Xiaomi Corporation (authors: Fangjun Kuang)

using System.Runtime.InteropServices;

// For Moonshine v1, you need four models:
//  - preprocessor, encoder, cached_decoder, uncached_decoder
//
// For Moonshine v2, you need 2 models:
//  - encoder, merged_decoder
namespace SherpaOnnx
{
    [StructLayout(LayoutKind.Sequential)]
    public struct OfflineMoonshineModelConfig
    {
        public static OfflineMoonshineModelConfig Default()
        {
            var self = default(OfflineMoonshineModelConfig);
            self.Preprocessor = "";
            self.Encoder = "";
            self.UncachedDecoder = "";
            self.CachedDecoder = "";
            self.MergedDecoder = "";
            return self;
        }
        [MarshalAs(UnmanagedType.LPStr)]
        public string Preprocessor;

        [MarshalAs(UnmanagedType.LPStr)]
        public string Encoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string UncachedDecoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string CachedDecoder;

        [MarshalAs(UnmanagedType.LPStr)]
        public string MergedDecoder;
    }
}
