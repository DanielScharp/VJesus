using System.Drawing;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace VJApp.Engine;

[StructLayout(LayoutKind.Sequential)]
struct SpriteParams
{
    public float DestX, DestY, DestW, DestH;
    public float SrcX, SrcY, SrcW, SrcH;
    public float TranslateX, TranslateY;
    public float Rotation;
    public float Scale;
    public float PivotX, PivotY;
    public float Opacity;
    public float Brightness;
}

/// <summary>Um slide já na GPU: as três imagens (paradas, animadas, caixa) e quando entrou/saiu.</summary>
sealed class SlideGpu(LyricsFrame frame, double start) : IDisposable
{
    public readonly LyricsFrame Frame = frame;
    public readonly double Start = start;
    public double? OutStart;
    public ID3D11Texture2D? StaticTex, AnimTex, BoxTex;
    public ID3D11ShaderResourceView? StaticSrv, AnimSrv, BoxSrv;

    public void Dispose()
    {
        StaticSrv?.Dispose(); StaticTex?.Dispose();
        AnimSrv?.Dispose(); AnimTex?.Dispose();
        BoxSrv?.Dispose(); BoxTex?.Dispose();
    }
}

/// <summary>Estado da letra numa layer: a imagem montada a cada quadro e os slides que estão entrando/saindo.</summary>
sealed class LyricsGpu : IDisposable
{
    public RenderTexture? Target;
    public SlideGpu? Current;
    public readonly List<SlideGpu> Leaving = [];
    public int Version = -1;
    public bool Drawn;

    public void Dispose()
    {
        Target?.Dispose();
        Current?.Dispose();
        foreach (var s in Leaving) s.Dispose();
    }
}

/// <summary>
/// Letras animadas na GPU: a entrada/saída do slide (fade, subir, zoom, palavra por palavra…) e as
/// palavras marcadas com &lt;anim&gt; (balançar, flutuar, pulsar…) são calculadas a cada quadro, sem
/// redesenhar o texto.
/// </summary>
partial class Renderer
{
    ID3D11VertexShader? _vsSprite;
    ID3D11PixelShader? _psSprite;
    ID3D11Buffer? _spriteCb;
    ID3D11BlendState? _alphaBlend;

    void InitLyrics(string src, string path)
    {
        _vsSprite = _device!.CreateVertexShader(Vortice.D3DCompiler.Compiler.Compile(src, "VSSprite", path, "vs_5_0").Span);
        _psSprite = _device.CreatePixelShader(Vortice.D3DCompiler.Compiler.Compile(src, "PSSprite", path, "ps_5_0").Span);
        _spriteCb = CreateCb<SpriteParams>();
        // mistura "por cima" (resultado fica pré-multiplicado; o PSSource desfaz com ColorMode 3)
        var desc = new BlendDescription();
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha, DestinationBlend = Blend.InverseSourceAlpha, BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.One, DestinationBlendAlpha = Blend.InverseSourceAlpha, BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All,
        };
        _alphaBlend = _device.CreateBlendState(desc);
    }

    void DisposeLyrics()
    {
        _alphaBlend?.Dispose();
        _spriteCb?.Dispose();
        _psSprite?.Dispose();
        _vsSprite?.Dispose();
    }

    unsafe ID3D11Texture2D? Upload(byte[]? pixels, out ID3D11ShaderResourceView? srv)
    {
        srv = null;
        if (pixels == null) return null;
        fixed (byte* p = pixels)
        {
            var tex = _device!.CreateTexture2D(new Texture2DDescription(
                Format.B8G8R8A8_UNorm, LyricsFeed.Width, LyricsFeed.Height, 1, 1, BindFlags.ShaderResource, ResourceUsage.Immutable),
                new SubresourceData((IntPtr)p, LyricsFeed.Width * 4));
            srv = _device.CreateShaderResourceView(tex);
            return tex;
        }
    }

    /// <summary>Monta a imagem da letra desta layer no quadro atual (chamado uma vez por quadro).</summary>
    void UpdateLyrics(LayerSource source, double now, float time)
    {
        var feed = source.Lyrics!;
        var gpu = source.LyricsGpu ??= new LyricsGpu();
        gpu.Target ??= new RenderTexture(_device!, LyricsFeed.Width, LyricsFeed.Height);

        // slide novo: o atual começa a sair e o novo a entrar
        var (version, frame) = feed.Snapshot();
        if (version != gpu.Version)
        {
            gpu.Version = version;
            if (gpu.Current != null)
            {
                gpu.Current.OutStart = now;
                gpu.Leaving.Add(gpu.Current);
            }
            var slide = new SlideGpu(frame, now);
            slide.StaticTex = Upload(frame.Static, out slide.StaticSrv);
            slide.AnimTex = Upload(frame.Animated, out slide.AnimSrv);
            slide.BoxTex = Upload(frame.Box, out slide.BoxSrv);
            gpu.Current = slide;
        }

        var ctx = _ctx!;
        ctx.ClearRenderTargetView(gpu.Target.Rtv, new Color4(0, 0, 0, 0));
        ctx.PSSetShaderResource(0, null!);
        ctx.OMSetRenderTargets(gpu.Target.Rtv);
        ctx.RSSetViewport(new Viewport(LyricsFeed.Width, LyricsFeed.Height));
        ctx.OMSetBlendState(_alphaBlend);
        SetFrameParams(LyricsFeed.Width, LyricsFeed.Height, time);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetShader(_vsSprite);
        ctx.PSSetShader(_psSprite);
        ctx.VSSetConstantBuffer(0, _frameCb);
        ctx.VSSetConstantBuffer(4, _spriteCb);
        ctx.PSSetConstantBuffer(4, _spriteCb);
        ctx.PSSetSampler(0, _sampler);

        // quem está saindo: some (ou sai pelo lado) no mesmo tempo da transição do slide novo
        foreach (var old in gpu.Leaving.ToList())
        {
            float dur = Math.Max(0.001f, (gpu.Current?.Frame.TransitionSeconds ?? old.Frame.TransitionSeconds) * 0.7f);
            float q = (float)((now - old.OutStart!.Value) / dur);
            string type = gpu.Current?.Frame.Transition ?? old.Frame.Transition;
            if (q >= 1 || type == "corte")
            {
                old.Dispose();
                gpu.Leaving.Remove(old);
                continue;
            }
            DrawSlide(old, Ease(1 - q), type, leaving: true, now);
        }
        if (gpu.Current is { } cur && !cur.Frame.Empty)
        {
            float dur = cur.Frame.TransitionSeconds;
            float p = dur <= 0 || cur.Frame.Transition == "corte" ? 1 : (float)Math.Clamp((now - cur.Start) / dur, 0, 1);
            DrawSlide(cur, p, cur.Frame.Transition, leaving: false, now);
        }

        ctx.OMSetBlendState(null);
        ctx.PSSetShaderResource(0, null!);
        gpu.Drawn = true;
    }

    static float Ease(float t) => t * t * (3 - 2 * t);

    void DrawSlide(SlideGpu slide, float p, string type, bool leaving, double now)
    {
        var f = slide.Frame;
        float e = Ease(Math.Clamp(p, 0, 1));
        int words = Math.Max(1, f.WordCount), lines = Math.Max(1, f.LineCount);

        // caixa de fundo: acompanha o slide inteiro
        if (slide.BoxSrv != null)
        {
            var t = SlideTransform(type, e, leaving, perPiece: false);
            DrawSprite(slide.BoxSrv, new RectangleF(0, 0, LyricsFeed.Width, LyricsFeed.Height), t.Dx, t.Dy, 0, t.Scale, t.Opacity, 1);
        }

        foreach (var s in f.Sprites)
        {
            var srv = s.FromAnimLayer ? slide.AnimSrv : slide.StaticSrv;
            if (srv == null) continue;

            // palavra por palavra / linha por linha: cada pedaço tem seu próprio tempo
            float pe = e;
            if (type is "palavra" or "maquina")
            {
                float overlap = type == "maquina" ? 1 : 3;
                pe = Math.Clamp(p * (words + overlap) - s.Index, 0, overlap) / overlap;
                if (type == "maquina") pe = pe > 0 ? 1 : 0;
                pe = Ease(pe);
            }
            else if (type == "linha")
            {
                pe = Ease(Math.Clamp(p * (lines + 1.5f) - s.Line, 0, 1.5f) / 1.5f);
            }
            if (leaving) pe = e; // na saída tudo some junto

            var t = SlideTransform(type, pe, leaving, perPiece: type is "palavra" or "linha");
            float rot = 0, scale = t.Scale, dx = t.Dx, dy = t.Dy, opacity = t.Opacity, bright = 1;
            if (s.Anim is { } a) Animate(a, now, s.Index, ref dx, ref dy, ref rot, ref scale, ref opacity, ref bright);
            DrawSprite(srv, s.Rect, dx, dy, rot, scale, opacity, bright);
        }
    }

    /// <summary>Movimento da transição de slide. e: 0 = começo, 1 = no lugar.</summary>
    static (float Dx, float Dy, float Scale, float Opacity) SlideTransform(string type, float e, bool leaving, bool perPiece)
    {
        float k = 1 - e; // quanto falta
        float dir = leaving ? -1 : 1; // na saída, continua o movimento para o outro lado
        return type switch
        {
            "subir" => (0, dir * k * 90, 1, e),
            "descer" => (0, -dir * k * 90, 1, e),
            "esquerda" => (dir * k * 160, 0, 1, e),
            "direita" => (-dir * k * 160, 0, 1, e),
            "zoom" => (0, 0, leaving ? 1 + k * 0.1f : 0.85f + 0.15f * e, e),
            "aproximar" => (0, 0, leaving ? 1 - k * 0.1f : 1.25f - 0.25f * e, e),
            "palavra" or "linha" when perPiece => (0, k * 30, 1, e),
            "maquina" => (0, 0, 1, e),
            _ => (0, 0, 1, e), // fade
        };
    }

    /// <summary>Animação contínua de um trecho (&lt;anim&gt;).</summary>
    static void Animate(WordAnim a, double now, int seed, ref float dx, ref float dy, ref float rot, ref float scale, ref float opacity, ref float bright)
    {
        double t = now * a.Speed + seed * 0.37;
        float w = (float)Math.Sin(t * Math.PI * 2);
        switch (a.Type)
        {
            case "balancar": rot += w * 0.10f * a.Intensity; break;
            case "flutuar": dy += w * 12 * a.Intensity; break;
            case "pulsar": scale *= 1 + 0.08f * a.Intensity * (0.5f + 0.5f * w); break;
            case "tremer":
            {
                double k = Math.Floor(now * 30 * a.Speed);
                dx += (float)((Hash(k, seed) - 0.5) * 8 * a.Intensity);
                dy += (float)((Hash(k + 17, seed) - 0.5) * 8 * a.Intensity);
                break;
            }
            case "brilhar": bright *= 1 + 0.6f * a.Intensity * (0.5f + 0.5f * w); break;
            case "piscar": opacity *= w > -0.3f ? 1 : Math.Max(0, 1 - a.Intensity); break;
            case "girar": rot += (float)(t * Math.PI * 2 * 0.25 * a.Intensity % (Math.PI * 2)); break;
            case "onda": dy += w * 10 * a.Intensity; rot += (float)Math.Cos(t * Math.PI * 2) * 0.05f * a.Intensity; break;
        }
    }

    static double Hash(double a, int b) { double v = Math.Sin(a * 12.9898 + b * 78.233) * 43758.5453; return v - Math.Floor(v); }

    void DrawSprite(ID3D11ShaderResourceView srv, RectangleF r, float dx, float dy, float rot, float scale, float opacity, float bright)
    {
        if (opacity <= 0.001f) return;
        var p = new SpriteParams
        {
            DestX = r.X, DestY = r.Y, DestW = r.Width, DestH = r.Height,
            SrcX = r.X / LyricsFeed.Width, SrcY = r.Y / LyricsFeed.Height, SrcW = r.Width / LyricsFeed.Width, SrcH = r.Height / LyricsFeed.Height,
            TranslateX = dx, TranslateY = dy, Rotation = rot, Scale = scale,
            PivotX = r.X + r.Width / 2, PivotY = r.Y + r.Height / 2,
            Opacity = Math.Clamp(opacity, 0, 1), Brightness = bright,
        };
        _ctx!.UpdateSubresource(in p, _spriteCb!);
        _ctx.PSSetShaderResource(0, srv);
        _ctx.Draw(6, 0);
    }
}
