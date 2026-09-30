// sherpa-onnx C# API (Apache-2.0, https://github.com/k2-fsa/sherpa-onnx, v1.13.8),
// converted to C# 9 for Unity: struct constructors -> static Default() factories.
/// Copyright (c)  2024  Xiaomi Corporation
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SherpaOnnx
{

    public class OfflineSpeakerDiarizationSegment
    {
        public OfflineSpeakerDiarizationSegment(IntPtr handle)
        {
          Impl impl = (Impl)Marshal.PtrToStructure(handle, typeof(Impl));

          Start = impl.Start;
          End = impl.End;
          Speaker = impl.Speaker;
          Confidence = impl.Confidence;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Impl
        {
            public float Start;
            public float End;
            public int Speaker;
            public float Confidence;
        }

        public float Start;
        public float End;
        public int Speaker;
        public float Confidence;
    }
}

