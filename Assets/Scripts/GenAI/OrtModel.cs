using System;
using System.Runtime.InteropServices;

namespace UntitledGame.GenAI
{
    /// <summary>
    /// Just enough of onnxruntime's C API to run one model and read back a float tensor: the sherpa-onnx recogniser only
    /// hands back its single best transcript, and we want the model's per-frame probabilities (the runners-up).
    /// It uses the same onnxruntime.dll sherpa-onnx loads (SherpaNative.TryLoad has to have run). The API is a table of
    /// function pointers; the entries used here are looked up by their position in that table (OrtApi in
    /// onnxruntime_c_api.h, whose first ~120 entries are fixed for good: only new ones are appended).
    /// </summary>
    public sealed class OrtModel : IDisposable
    {
        [DllImport("onnxruntime", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr OrtGetApiBase();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetApiFn(uint version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ErrMsgFn(IntPtr status);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateEnvFn(int level, [MarshalAs(UnmanagedType.LPStr)] string logId, out IntPtr env);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateOptsFn(out IntPtr opts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr SetThreadsFn(IntPtr opts, int n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate IntPtr CreateSessionFn(IntPtr env, [MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr opts, out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetAllocFn(out IntPtr alloc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetMetaFn(IntPtr session, out IntPtr meta);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr LookupMetaFn(IntPtr meta, IntPtr alloc, [MarshalAs(UnmanagedType.LPStr)] string key, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FreeFn(IntPtr alloc, IntPtr p);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CpuInfoFn(int allocType, int memType, out IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr TensorFn(IntPtr info, IntPtr data, UIntPtr bytes, long[] shape, UIntPtr rank, int type, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr RunFn(IntPtr session, IntPtr runOpts, IntPtr[] inNames, IntPtr[] inputs, UIntPtr nIn, IntPtr[] outNames, UIntPtr nOut, IntPtr[] outputs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr DataFn(IntPtr value, out IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr ShapeInfoFn(IntPtr value, out IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr DimCountFn(IntPtr info, out UIntPtr n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr DimsFn(IntPtr info, long[] dims, UIntPtr n);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseFn(IntPtr obj);

        // Positions in OrtApi (checked against onnxruntime_c_api.h for the shipped 1.28 DLL).
        private const int IxGetErrorMessage = 2, IxCreateEnv = 3, IxCreateSession = 7, IxRun = 9, IxCreateSessionOptions = 10,
            IxSetIntraOpNumThreads = 24, IxSetInterOpNumThreads = 25, IxCreateTensorWithData = 49, IxGetTensorMutableData = 51,
            IxGetTensorTypeAndShape = 65, IxGetDimensionsCount = 61, IxGetDimensions = 62, IxCreateCpuMemoryInfo = 69,
            IxAllocatorFree = 76, IxGetAllocatorWithDefaultOptions = 78, IxReleaseEnv = 92, IxReleaseStatus = 93,
            IxReleaseMemoryInfo = 94, IxReleaseSession = 95, IxReleaseValue = 96, IxReleaseTensorShapeInfo = 99,
            IxReleaseSessionOptions = 100, IxSessionGetModelMetadata = 111, IxLookupCustomMetadata = 116, IxReleaseModelMetadata = 118;

        private IntPtr _api, _env, _session, _memInfo, _allocator;
        private readonly ErrMsgFn _errMsg;
        private readonly ReleaseFn _releaseStatus, _releaseValue, _releaseShape, _releaseMeta;
        private readonly TensorFn _createTensor;
        private readonly RunFn _run;
        private readonly DataFn _getData;
        private readonly ShapeInfoFn _getShape;
        private readonly DimCountFn _dimCount;
        private readonly DimsFn _dims;
        private readonly LookupMetaFn _lookup;
        private readonly FreeFn _free;
        private readonly GetMetaFn _getMeta;

        private T Fn<T>(int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(_api, index * IntPtr.Size));

        public OrtModel(string modelPath, int threads)
        {
            IntPtr apiBase = OrtGetApiBase();
            var getApi = Marshal.GetDelegateForFunctionPointer<GetApiFn>(Marshal.ReadIntPtr(apiBase, 0));
            _api = getApi(17); // an API level every onnxruntime since 1.14 has
            if (_api == IntPtr.Zero) throw new InvalidOperationException("onnxruntime: API level 17 not available");
            _errMsg = Fn<ErrMsgFn>(IxGetErrorMessage);
            _releaseStatus = Fn<ReleaseFn>(IxReleaseStatus);
            _releaseValue = Fn<ReleaseFn>(IxReleaseValue);
            _releaseShape = Fn<ReleaseFn>(IxReleaseTensorShapeInfo);
            _releaseMeta = Fn<ReleaseFn>(IxReleaseModelMetadata);
            _createTensor = Fn<TensorFn>(IxCreateTensorWithData);
            _run = Fn<RunFn>(IxRun);
            _getData = Fn<DataFn>(IxGetTensorMutableData);
            _getShape = Fn<ShapeInfoFn>(IxGetTensorTypeAndShape);
            _dimCount = Fn<DimCountFn>(IxGetDimensionsCount);
            _dims = Fn<DimsFn>(IxGetDimensions);
            _lookup = Fn<LookupMetaFn>(IxLookupCustomMetadata);
            _free = Fn<FreeFn>(IxAllocatorFree);
            _getMeta = Fn<GetMetaFn>(IxSessionGetModelMetadata);

            Check(Fn<CreateEnvFn>(IxCreateEnv)(3, "untitled-game", out _env));
            Check(Fn<CreateOptsFn>(IxCreateSessionOptions)(out IntPtr opts));
            try
            {
                Check(Fn<SetThreadsFn>(IxSetIntraOpNumThreads)(opts, Math.Max(1, threads)));
                Check(Fn<SetThreadsFn>(IxSetInterOpNumThreads)(opts, 1));
                Check(Fn<CreateSessionFn>(IxCreateSession)(_env, modelPath, opts, out _session));
            }
            finally { Fn<ReleaseFn>(IxReleaseSessionOptions)(opts); }
            Check(Fn<CpuInfoFn>(IxCreateCpuMemoryInfo)(1, 0, out _memInfo)); // arena allocator, default memory type
            Check(Fn<GetAllocFn>(IxGetAllocatorWithDefaultOptions)(out _allocator));
        }

        private void Check(IntPtr status)
        {
            if (status == IntPtr.Zero) return;
            string msg = Marshal.PtrToStringAnsi(_errMsg(status));
            _releaseStatus(status);
            throw new InvalidOperationException("onnxruntime: " + msg);
        }

        /// <summary>A custom metadata entry of the model (SenseVoice keeps its normalisation constants there), or null.</summary>
        public string Metadata(string key)
        {
            Check(_getMeta(_session, out IntPtr meta));
            try
            {
                Check(_lookup(meta, _allocator, key, out IntPtr value));
                if (value == IntPtr.Zero) return null;
                string s = Marshal.PtrToStringAnsi(value);
                Check(_free(_allocator, value));
                return s;
            }
            finally { _releaseMeta(meta); }
        }

        /// <summary>One float tensor, read back from a run.</summary>
        public struct Output
        {
            public float[] data;
            public long[] shape;
        }

        /// <summary>A model input: a float or int32 tensor of a given shape.</summary>
        public struct Input
        {
            public string name;
            public Array data;     // float[] or int[]
            public long[] shape;
        }

        public Output Run(Input[] inputs, string outputName)
        {
            int n = inputs.Length;
            var handles = new GCHandle[n];
            var values = new IntPtr[n];
            var names = new IntPtr[n];
            IntPtr outName = Marshal.StringToHGlobalAnsi(outputName);
            var outputs = new IntPtr[1];
            try
            {
                for (int i = 0; i < n; i++)
                {
                    names[i] = Marshal.StringToHGlobalAnsi(inputs[i].name);
                    handles[i] = GCHandle.Alloc(inputs[i].data, GCHandleType.Pinned);
                    bool isFloat = inputs[i].data is float[];
                    int bytes = inputs[i].data.Length * 4;
                    Check(_createTensor(_memInfo, handles[i].AddrOfPinnedObject(), (UIntPtr)bytes, inputs[i].shape, (UIntPtr)inputs[i].shape.Length, isFloat ? 1 : 6, out values[i]));
                }
                Check(_run(_session, IntPtr.Zero, names, values, (UIntPtr)n, new[] { outName }, (UIntPtr)1, outputs));
                Check(_getData(outputs[0], out IntPtr dataPtr));
                Check(_getShape(outputs[0], out IntPtr info));
                long[] shape;
                try
                {
                    Check(_dimCount(info, out UIntPtr rank));
                    shape = new long[(int)rank];
                    Check(_dims(info, shape, rank));
                }
                finally { _releaseShape(info); }
                long count = 1;
                foreach (long d in shape) count *= d;
                var data = new float[count];
                Marshal.Copy(dataPtr, data, 0, (int)count);
                return new Output { data = data, shape = shape };
            }
            finally
            {
                for (int i = 0; i < n; i++)
                {
                    if (handles[i].IsAllocated) handles[i].Free();
                    if (values[i] != IntPtr.Zero) _releaseValue(values[i]);
                    if (names[i] != IntPtr.Zero) Marshal.FreeHGlobal(names[i]);
                }
                if (outputs[0] != IntPtr.Zero) _releaseValue(outputs[0]);
                Marshal.FreeHGlobal(outName);
            }
        }

        public void Dispose()
        {
            if (_api == IntPtr.Zero) return;
            if (_memInfo != IntPtr.Zero) Fn<ReleaseFn>(IxReleaseMemoryInfo)(_memInfo);
            if (_session != IntPtr.Zero) Fn<ReleaseFn>(IxReleaseSession)(_session);
            if (_env != IntPtr.Zero) Fn<ReleaseFn>(IxReleaseEnv)(_env);
            _memInfo = _session = _env = IntPtr.Zero;
            _api = IntPtr.Zero;
        }
    }
}
