using System.Runtime.InteropServices;

namespace VJApp.Engine;

/// <summary>
/// Envia a composição pela rede via NDI (OBS, vMix, NDI Studio Monitor...).
/// Usa o NDI Runtime instalado no Windows (gratuito: ndi.video/tools); não é empacotado com o app.
/// </summary>
sealed unsafe class NdiSender : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct SendCreate
    {
        public IntPtr Name;
        public IntPtr Groups;
        [MarshalAs(UnmanagedType.U1)] public bool ClockVideo;
        [MarshalAs(UnmanagedType.U1)] public bool ClockAudio;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct VideoFrame
    {
        public int XRes, YRes;
        public uint FourCC;
        public int FrameRateN, FrameRateD;
        public float AspectRatio;
        public int FrameFormat;   // 1 = progressivo
        public long Timecode;
        public IntPtr Data;
        public int LineStride;
        public IntPtr Metadata;
        public long Timestamp;
    }

    delegate bool InitializeFn();
    delegate IntPtr SendCreateFn(ref SendCreate settings);
    delegate void SendDestroyFn(IntPtr instance);
    delegate void SendVideoFn(IntPtr instance, ref VideoFrame frame);
    delegate void SendSyncFn(IntPtr instance, IntPtr frame);

    const uint FourCC_BGRA = 'B' | ('G' << 8) | ('R' << 16) | ('A' << 24);
    const long TimecodeSynthesize = long.MaxValue;

    static IntPtr _lib;
    static SendCreateFn? _create;
    static SendDestroyFn? _destroy;
    static SendVideoFn? _sendAsync;
    static SendSyncFn? _sendSync;

    readonly IntPtr _instance;
    readonly IntPtr _namePtr;
    // dois buffers: o NDI usa o buffer do envio assíncrono até a próxima chamada
    readonly byte[][] _buffers = new byte[2][];
    readonly GCHandle[] _pins = new GCHandle[2];
    int _next;

    public string Name { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>Procura o NDI Runtime (variáveis de ambiente do instalador ou VJ_NDI_DLL).</summary>
    public static string? FindRuntime()
    {
        const string dll = "Processing.NDI.Lib.x64.dll";
        var candidates = new List<string?> { Environment.GetEnvironmentVariable("VJ_NDI_DLL") };
        foreach (var v in new[] { "NDI_RUNTIME_DIR_V6", "NDI_RUNTIME_DIR_V5", "NDI_RUNTIME_DIR_V4" })
            if (Environment.GetEnvironmentVariable(v) is { } dir) candidates.Add(Path.Combine(dir, dll));
        return candidates.FirstOrDefault(p => p != null && File.Exists(p));
    }

    static void Load()
    {
        if (_lib != IntPtr.Zero) return;
        string path = FindRuntime() ?? throw new DllNotFoundException(
            "NDI Runtime não encontrado. Instale o NDI Tools ou o NDI Runtime (gratuito, ndi.video/tools) e reabra o app.");
        _lib = NativeLibrary.Load(path);
        T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));
        if (!Fn<InitializeFn>("NDIlib_initialize")()) throw new InvalidOperationException("Este processador não suporta NDI.");
        _create = Fn<SendCreateFn>("NDIlib_send_create");
        _destroy = Fn<SendDestroyFn>("NDIlib_send_destroy");
        _sendAsync = Fn<SendVideoFn>("NDIlib_send_send_video_async_v2");
        _sendSync = Fn<SendSyncFn>("NDIlib_send_send_video_async_v2");
    }

    public NdiSender(string name, int width, int height)
    {
        Load();
        Name = name;
        Width = width;
        Height = height;
        _namePtr = Marshal.StringToCoTaskMemUTF8(name);
        var settings = new SendCreate { Name = _namePtr, ClockVideo = false };
        _instance = _create!(ref settings);
        if (_instance == IntPtr.Zero) throw new InvalidOperationException("Não consegui criar o sender NDI.");
        for (int i = 0; i < 2; i++)
        {
            _buffers[i] = new byte[width * height * 4];
            _pins[i] = GCHandle.Alloc(_buffers[i], GCHandleType.Pinned);
        }
    }

    /// <summary>Envia um quadro BGRA (copiado de um mapeamento de textura com o pitch dado).</summary>
    public void Send(IntPtr pixels, int pitch, int fpsN)
    {
        var buf = _buffers[_next];
        int row = Width * 4;
        fixed (byte* dst = buf)
            for (int y = 0; y < Height; y++)
                Buffer.MemoryCopy((byte*)pixels + y * pitch, dst + y * row, row, row);

        var frame = new VideoFrame
        {
            XRes = Width, YRes = Height, FourCC = FourCC_BGRA,
            FrameRateN = fpsN, FrameRateD = 1, AspectRatio = (float)Width / Height,
            FrameFormat = 1, Timecode = TimecodeSynthesize,
            Data = _pins[_next].AddrOfPinnedObject(), LineStride = row,
        };
        _sendAsync!(_instance, ref frame);
        _next = 1 - _next;
    }

    public void Dispose()
    {
        if (_instance != IntPtr.Zero)
        {
            _sendSync!(_instance, IntPtr.Zero); // espera o último envio assíncrono terminar
            _destroy!(_instance);
        }
        foreach (var p in _pins) if (p.IsAllocated) p.Free();
        Marshal.FreeCoTaskMem(_namePtr);
    }
}
