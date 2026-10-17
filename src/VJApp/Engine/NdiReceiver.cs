using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>
/// Recebe vídeo NDI da rede (OBS, vMix, Holyrics, NDI Tools, câmeras NDI…) como fonte de uma layer.
/// Usa o NDI Runtime instalado no Windows (gratuito: ndi.video/tools), igual ao envio.
/// Uma thread recebe os quadros (BGRA) e o render thread sobe o mais novo para a GPU.
/// </summary>
sealed unsafe class NdiReceiver : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct FindCreate
    {
        [MarshalAs(UnmanagedType.U1)] public bool ShowLocalSources;
        public IntPtr Groups;
        public IntPtr ExtraIps;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Source
    {
        public IntPtr Name;
        public IntPtr Url;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RecvCreate
    {
        public Source SourceToConnectTo;
        public int ColorFormat;   // 0 = BGRX/BGRA
        public int Bandwidth;     // 100 = máxima qualidade
        [MarshalAs(UnmanagedType.U1)] public bool AllowVideoFields;
        public IntPtr RecvName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct VideoFrame
    {
        public int XRes, YRes;
        public uint FourCC;
        public int FrameRateN, FrameRateD;
        public float AspectRatio;
        public int FrameFormat;
        public long Timecode;
        public IntPtr Data;
        public int LineStride;
        public IntPtr Metadata;
        public long Timestamp;
    }

    delegate bool InitializeFn();
    delegate IntPtr FindCreateFn(ref FindCreate settings);
    delegate IntPtr FindSourcesFn(IntPtr instance, out uint count);
    delegate IntPtr RecvCreateFn(ref RecvCreate settings);
    delegate void RecvDestroyFn(IntPtr instance);
    delegate int RecvCaptureFn(IntPtr instance, VideoFrame* video, IntPtr audio, IntPtr metadata, uint timeoutMs);
    delegate void RecvFreeVideoFn(IntPtr instance, VideoFrame* video);

    const int FrameTypeVideo = 1;
    const uint FourCC_BGRX = 'B' | ('G' << 8) | ('R' << 16) | ('X' << 24);

    static IntPtr _lib;
    static FindCreateFn? _findCreate;
    static FindSourcesFn? _findSources;
    static RecvCreateFn? _recvCreate;
    static RecvDestroyFn? _recvDestroy;
    static RecvCaptureFn? _capture;
    static RecvFreeVideoFn? _freeVideo;
    static IntPtr _finder;
    static readonly object FinderLock = new();

    public static bool Available => NdiSender.FindRuntime() != null;

    static void Load()
    {
        if (_lib != IntPtr.Zero) return;
        string path = NdiSender.FindRuntime() ?? throw new DllNotFoundException(
            "NDI Runtime não encontrado. Instale o NDI Tools ou o NDI Runtime (gratuito, ndi.video/tools) e reabra o VJesus.");
        _lib = NativeLibrary.Load(path);
        T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));
        if (!Fn<InitializeFn>("NDIlib_initialize")()) throw new InvalidOperationException("Este processador não suporta NDI.");
        _findCreate = Fn<FindCreateFn>("NDIlib_find_create_v2");
        _findSources = Fn<FindSourcesFn>("NDIlib_find_get_current_sources");
        _recvCreate = Fn<RecvCreateFn>("NDIlib_recv_create_v3");
        _recvDestroy = Fn<RecvDestroyFn>("NDIlib_recv_destroy");
        _capture = Fn<RecvCaptureFn>("NDIlib_recv_capture_v2");
        _freeVideo = Fn<RecvFreeVideoFn>("NDIlib_recv_free_video_v2");
    }

    /// <summary>Fontes NDI vistas na rede agora (a busca fica ligada em segundo plano depois da primeira vez).</summary>
    public static List<string> ListSources()
    {
        if (!Available) return [];
        try
        {
            lock (FinderLock)
            {
                Load();
                if (_finder == IntPtr.Zero)
                {
                    var fc = new FindCreate { ShowLocalSources = true };
                    _finder = _findCreate!(ref fc);
                    Thread.Sleep(1500); // primeira busca: dá tempo de as fontes responderem
                }
                var ptr = _findSources!(_finder, out uint n);
                var list = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    var src = Marshal.PtrToStructure<Source>(ptr + i * sizeof(Source));
                    if (Marshal.PtrToStringUTF8(src.Name) is { Length: > 0 } name) list.Add(name);
                }
                return list;
            }
        }
        catch { return []; }
    }

    public string Name { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public ID3D11ShaderResourceView? Srv { get; private set; }

    IntPtr _recv;
    readonly IntPtr _namePtr, _recvNamePtr;
    readonly Thread _thread;
    volatile bool _running = true;

    // último quadro recebido (trocado pela thread de rede, lido pelo render thread)
    readonly object _frameLock = new();
    byte[]? _pixels;
    int _w, _h;
    bool _bgrx, _fresh;

    ID3D11Texture2D? _texture;
    bool _textureBgrx;

    public NdiReceiver(string name)
    {
        Load();
        Name = name;
        _namePtr = Marshal.StringToCoTaskMemUTF8(name);
        _recvNamePtr = Marshal.StringToCoTaskMemUTF8("VJesus");
        var rc = new RecvCreate
        {
            SourceToConnectTo = new Source { Name = _namePtr },
            ColorFormat = 0,
            Bandwidth = 100,
            AllowVideoFields = false,
            RecvName = _recvNamePtr,
        };
        _recv = _recvCreate!(ref rc);
        if (_recv == IntPtr.Zero) throw new InvalidOperationException($"Não consegui conectar na fonte NDI {name}.");
        _thread = new Thread(Loop) { IsBackground = true, Name = "NDI " + name };
        _thread.Start();
    }

    void Loop()
    {
        while (_running)
        {
            VideoFrame f;
            int type = _capture!(_recv, &f, IntPtr.Zero, IntPtr.Zero, 200);
            if (type != FrameTypeVideo) continue;
            try
            {
                int row = f.XRes * 4;
                lock (_frameLock)
                {
                    if (_pixels == null || _pixels.Length != row * f.YRes) _pixels = new byte[row * f.YRes];
                    fixed (byte* dst = _pixels)
                        for (int y = 0; y < f.YRes; y++)
                            Buffer.MemoryCopy((byte*)f.Data + (long)y * f.LineStride, dst + y * row, row, row);
                    _w = f.XRes; _h = f.YRes;
                    _bgrx = f.FourCC == FourCC_BGRX;
                    _fresh = true;
                }
            }
            finally { _freeVideo!(_recv, &f); }
        }
    }

    /// <summary>Sobe o quadro mais novo para a GPU (render thread).</summary>
    public void Update(ID3D11Device device, ID3D11DeviceContext ctx)
    {
        lock (_frameLock)
        {
            if (!_fresh || _pixels == null) return;
            _fresh = false;
            if (_texture == null || Width != _w || Height != _h || _textureBgrx != _bgrx)
            {
                Srv?.Dispose(); _texture?.Dispose();
                Width = _w; Height = _h; _textureBgrx = _bgrx;
                // BGRX: o alfa não vem preenchido, então a textura ignora o alfa (imagem opaca)
                _texture = device.CreateTexture2D(new Texture2DDescription(
                    _bgrx ? Format.B8G8R8X8_UNorm : Format.B8G8R8A8_UNorm, (uint)_w, (uint)_h, 1, 1,
                    BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write));
                Srv = device.CreateShaderResourceView(_texture);
            }
            var mapped = ctx.Map(_texture, 0, MapMode.WriteDiscard);
            int row = _w * 4;
            fixed (byte* src = _pixels)
                for (int y = 0; y < _h; y++)
                    Buffer.MemoryCopy(src + y * row, (byte*)mapped.DataPointer + y * mapped.RowPitch, mapped.RowPitch, row);
            ctx.Unmap(_texture, 0);
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(1000);
        if (_recv != IntPtr.Zero) { _recvDestroy!(_recv); _recv = IntPtr.Zero; }
        Marshal.FreeCoTaskMem(_namePtr);
        Marshal.FreeCoTaskMem(_recvNamePtr);
        Srv?.Dispose();
        _texture?.Dispose();
    }
}
