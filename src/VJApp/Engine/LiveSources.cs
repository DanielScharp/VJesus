using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>Uma imagem ao vivo que vira textura no motor (tela espelhada, página da internet). Só o render thread chama Update.</summary>
interface ILiveSource : IDisposable
{
    ID3D11ShaderResourceView? Srv { get; }
    int Width { get; }
    int Height { get; }
    void Update(ID3D11Device device, ID3D11DeviceContext ctx);
}

/// <summary>Quadros BGRA entregues por qualquer thread (o último vence) e subidos para a placa no quadro seguinte.</summary>
class FrameFeedSource : ILiveSource
{
    readonly object _lock = new();
    byte[]? _pixels;
    int _w, _h;
    bool _fresh;
    ID3D11Texture2D? _texture;
    ID3D11ShaderResourceView? _srv;
    int _width, _height;
    public virtual ID3D11ShaderResourceView? Srv => _srv;
    public virtual int Width => _width;
    public virtual int Height => _height;
    /// <summary>Chamado ao liberar (fechar a janela da página, parar a captura).</summary>
    public Action? OnDispose;

    public void Push(byte[] bgra, int width, int height)
    {
        lock (_lock) { _pixels = bgra; _w = width; _h = height; _fresh = true; }
    }

    public virtual void Update(ID3D11Device device, ID3D11DeviceContext ctx)
    {
        byte[] px; int w, h;
        lock (_lock)
        {
            if (!_fresh || _pixels == null) return;
            _fresh = false;
            px = _pixels; w = _w; h = _h;
        }
        if (_texture == null || w != _width || h != _height)
        {
            _srv?.Dispose(); _texture?.Dispose();
            _width = w; _height = h;
            _texture = device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8X8_UNorm, (uint)w, (uint)h, 1, 1,
                BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None));
            _srv = device.CreateShaderResourceView(_texture);
        }
        unsafe
        {
            fixed (byte* p = px) ctx.UpdateSubresource(_texture, 0, null, (IntPtr)p, (uint)(w * 4), 0);
        }
    }

    public virtual void Dispose()
    {
        _srv?.Dispose(); _srv = null;
        _texture?.Dispose(); _texture = null;
        OnDispose?.Invoke();
    }
}

/// <summary>
/// Espelho de um monitor do Windows. Usa a duplicação de tela do DirectX (a imagem fica na placa de vídeo,
/// sem gastar processador); se o monitor estiver ligado em outra placa, copia pela tela do Windows (GDI), ~20 qps.
/// </summary>
sealed class ScreenSource(int display) : FrameFeedSource
{
    IDXGIOutputDuplication? _dup;
    ID3D11Texture2D? _dupTexture;
    ID3D11ShaderResourceView? _dupSrv;
    int _dupW, _dupH;
    bool _useGdi;
    double _retryAt;
    Thread? _gdi;
    volatile bool _running = true;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public override ID3D11ShaderResourceView? Srv => _useGdi ? base.Srv : _dupSrv;
    public override int Width => _useGdi ? base.Width : _dupW;
    public override int Height => _useGdi ? base.Height : _dupH;

    public override void Update(ID3D11Device device, ID3D11DeviceContext ctx)
    {
        if (_useGdi) { base.Update(device, ctx); return; }
        if (_dup == null)
        {
            if (_clock.Elapsed.TotalSeconds < _retryAt) return;
            _retryAt = _clock.Elapsed.TotalSeconds + 2;
            if (!OpenDuplication(device)) return;
        }
        var r = _dup!.AcquireNextFrame(0, out _, out IDXGIResource? res);
        if (r.Failure)
        {
            if (r.Code != Vortice.DXGI.ResultCode.WaitTimeout.Code) { _dup.Dispose(); _dup = null; } // tela trocou de modo, bloqueio do Windows...
            return;
        }
        try
        {
            using var tex = res!.QueryInterface<ID3D11Texture2D>();
            var d = tex.Description;
            if (_dupTexture == null || d.Width != _dupW || d.Height != _dupH)
            {
                _dupSrv?.Dispose(); _dupTexture?.Dispose();
                _dupW = (int)d.Width; _dupH = (int)d.Height;
                _dupTexture = device.CreateTexture2D(new Texture2DDescription(d.Format, d.Width, d.Height, 1, 1,
                    BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None));
                _dupSrv = device.CreateShaderResourceView(_dupTexture);
            }
            ctx.CopyResource(_dupTexture, tex);
        }
        finally
        {
            res?.Dispose();
            _dup.ReleaseFrame();
        }
    }

    bool OpenDuplication(ID3D11Device device)
    {
        var screens = Screen.AllScreens;
        if (display < 0 || display >= screens.Length) return false;
        string name = screens[display].DeviceName;
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            for (uint i = 0; adapter.EnumOutputs(i, out IDXGIOutput? output).Success; i++)
            {
                using (output)
                {
                    if (!string.Equals(output!.Description.DeviceName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    using var o1 = output.QueryInterface<IDXGIOutput1>();
                    _dup = o1.DuplicateOutput(device);
                    return true;
                }
            }
        }
        catch { /* sem permissão (ex.: tela de bloqueio) ou placa sem suporte: tenta pelo Windows */ }
        StartGdi(screens[display].Bounds);
        return false;
    }

    /// <summary>Plano B: copia a tela pelo Windows numa thread própria.</summary>
    void StartGdi(Rectangle bounds)
    {
        if (_useGdi) return;
        _useGdi = true;
        _gdi = new Thread(() =>
        {
            using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            using var g = Graphics.FromImage(bmp);
            while (_running)
            {
                var t0 = _clock.Elapsed.TotalMilliseconds;
                try
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    var data = bmp.LockBits(new Rectangle(Point.Empty, bounds.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                    var px = new byte[bounds.Width * bounds.Height * 4];
                    for (int y = 0; y < bounds.Height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * bounds.Width * 4, bounds.Width * 4);
                    bmp.UnlockBits(data);
                    Push(px, bounds.Width, bounds.Height);
                }
                catch { }
                int wait = 50 - (int)(_clock.Elapsed.TotalMilliseconds - t0);
                if (wait > 0) Thread.Sleep(wait);
            }
        }) { IsBackground = true, Name = "Espelho de tela" };
        _gdi.Start();
    }

    public override void Dispose()
    {
        _running = false;
        _gdi?.Join(500);
        _dup?.Dispose(); _dup = null;
        _dupSrv?.Dispose(); _dupTexture?.Dispose();
        base.Dispose();
    }
}
