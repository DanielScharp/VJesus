using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Vortice.Direct3D11;
using Vortice.DXGI;
using VJApp.Engine;

namespace VJApp;

/// <summary>
/// Imagem da animação de letra ao vivo: a página desenha o quadro num buffer compartilhado e avisa;
/// o motor sobe direto desse buffer para a placa e responde "pode o próximo". Sem cópias no meio.
/// </summary>
sealed class KineticLiveSource : ILiveSource
{
    readonly object _lock = new();
    IntPtr _ptr;
    int _w, _h;
    bool _fresh;
    ID3D11Texture2D? _tex;
    ID3D11ShaderResourceView? _srv;
    int _tw, _th;
    /// <summary>Chamado (no render thread) depois de subir o quadro: a página pode escrever o próximo.</summary>
    public Action? Uploaded;

    public ID3D11ShaderResourceView? Srv => _srv;
    public int Width => _tw;
    public int Height => _th;

    public void Frame(IntPtr ptr, int w, int h)
    {
        lock (_lock) { _ptr = ptr; _w = w; _h = h; _fresh = true; }
    }

    public void Update(ID3D11Device device, ID3D11DeviceContext ctx)
    {
        IntPtr p; int w, h;
        lock (_lock)
        {
            if (!_fresh || _ptr == IntPtr.Zero) return;
            _fresh = false;
            p = _ptr; w = _w; h = _h;
        }
        if (_tex == null || w != _tw || h != _th)
        {
            _srv?.Dispose(); _tex?.Dispose();
            _tw = w; _th = h;
            _tex = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, (uint)w, (uint)h, 1, 1,
                BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None));
            _srv = device.CreateShaderResourceView(_tex);
        }
        ctx.UpdateSubresource(_tex, 0, null, p, (uint)(w * 4), 0);
        Uploaded?.Invoke();
    }

    public void Dispose()
    {
        _srv?.Dispose(); _srv = null;
        _tex?.Dispose(); _tex = null;
    }
}

/// <summary>
/// Janela escondida (fora da tela) com a página que desenha a animação de letra (index.html#kinetic).
/// Recebe cada slide (texto + estilo) e devolve os quadros pelo buffer compartilhado.
/// </summary>
sealed class KineticWindow : Form
{
    const int MaxW = Renderer.CompWidth, MaxH = Renderer.CompHeight;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    CoreWebView2SharedBuffer? _buffer;
    bool _ready, _closing;
    string? _lastScene;
    readonly Action _onHello;
    public readonly KineticLiveSource Source = new();

    public KineticWindow(Action onHello)
    {
        _onHello = onHello;
        Text = $"{AppInfo.Name} - animação de letra";
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-MaxW - 4000, -MaxH - 4000); // fora da tela
        ClientSize = new Size(320, 180);
        Controls.Add(_web);
        Source.Uploaded = () =>
        {
            if (_closing || !IsHandleCreated) return;
            try { BeginInvoke(() => { if (!_closing) _web.CoreWebView2?.PostWebMessageAsJson("{\"type\":\"kineticAck\"}"); }); } catch { }
        };
        Show();
        _ = Start();
    }

    async Task Start()
    {
        var env = await AppInfo.WebViewEnvironment();
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        _buffer = env.CreateSharedBuffer((ulong)(MaxW * MaxH * 4));
        core.WebMessageReceived += (_, ev) =>
        {
            var json = ev.WebMessageAsJson;
            if (json.Contains("\"kineticFrame\""))
            {
                using var doc = JsonDocument.Parse(json);
                int w = doc.RootElement.GetProperty("w").GetInt32(), h = doc.RootElement.GetProperty("h").GetInt32();
                if (w > 0 && h > 0 && w * h <= MaxW * MaxH) Source.Frame(_buffer.Buffer, w, h);
            }
            else if (json.Contains("\"kineticHello\""))
            {
                _ready = true;
                core.PostSharedBufferToScript(_buffer, CoreWebView2SharedBufferAccess.ReadWrite, $"{{\"max\":{MaxW * MaxH * 4}}}");
                _onHello();
                if (_lastScene != null) core.PostWebMessageAsJson(_lastScene);
            }
        };
        if (Environment.GetEnvironmentVariable("VJ_UI_DEV") == "1") core.Navigate("http://localhost:5173/#kinetic");
        else
        {
            string dist = Path.Combine(AppContext.BaseDirectory, "ui");
            core.SetVirtualHostNameToFolderMapping("app.vj", dist, CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(FontStore.Dir);
            core.SetVirtualHostNameToFolderMapping("fonts.vj", FontStore.Dir, CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://app.vj/index.html#kinetic");
        }
    }

    /// <summary>Manda uma mensagem para a página (o slide atual é guardado para quando ela recarregar).</summary>
    public void Post(string json, bool isScene = false)
    {
        if (isScene) _lastScene = json;
        if (_ready && _web.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        base.OnFormClosing(e);
    }
}
