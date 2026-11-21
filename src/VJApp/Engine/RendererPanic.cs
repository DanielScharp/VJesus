using System.Drawing;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace VJApp.Engine;

/// <summary>
/// Fundo da tela de pânico em movimento: um vídeo ou um fundo animado (gerado), desenhado por cima de tudo,
/// com o logo (se houver) por cima dele. Entra e sai com o mesmo esmaecer da tela de pânico.
/// </summary>
partial class Renderer
{
    readonly object _panicLock = new();
    bool _panicPending;
    SourceKind? _panicPendingKind;
    VideoPlayer? _panicPendingPlayer;
    GenParams _panicPendingGen = GenParams.Default;

    LayerSource? _panicSource;
    readonly LayerSlot _panicSlot = new() { ClipFit = 1 }; // preencher a tela
    RenderTexture? _panicTex;
    float _panicAlpha;
    double _panicLast;

    /// <summary>Tela de pânico ligada (o fundo em movimento aparece).</summary>
    public volatile bool PanicMediaOn;
    /// <summary>Tempo do esmaecer (segundos).</summary>
    public volatile float PanicMediaFade = 0.4f;

    /// <summary>Troca o fundo em movimento (null = nenhum: só cor/logo). O player passa a ser do motor.</summary>
    public void SetPanicMedia(SourceKind? kind, VideoPlayer? player, GenParams gen)
    {
        lock (_panicLock)
        {
            _panicPendingPlayer?.Dispose();
            _panicPending = true;
            _panicPendingKind = kind;
            _panicPendingPlayer = player;
            _panicPendingGen = gen;
        }
    }

    /// <summary>Uma vez por quadro, antes de compor: troca a fonte se pediram, avança o esmaecer e desenha o fundo.</summary>
    void UpdatePanicMedia(double now)
    {
        lock (_panicLock)
        {
            if (_panicPending)
            {
                _panicPending = false;
                _panicSource?.Dispose();
                _panicSource = _panicPendingKind is { } kind ? new LayerSource(kind, _panicPendingPlayer, null) : null;
                _panicPendingPlayer = null;
                _panicSource?.CreateGpu(_device!);
                _panicSlot.Gen = _panicPendingGen;
                _panicSlot.Current = _panicSource;
            }
        }
        float dt = (float)Math.Clamp(now - _panicLast, 0, 0.1);
        _panicLast = now;
        float target = PanicMediaOn && _panicSource != null ? 1 : 0;
        float fade = PanicMediaFade;
        _panicAlpha = fade <= 0.01f ? target : _panicAlpha < target ? Math.Min(target, _panicAlpha + dt / fade) : Math.Max(target, _panicAlpha - dt / fade);
        if (_panicSource == null) return;
        _panicSource.Update(_device!, _ctx!); // o vídeo continua andando mesmo escondido: volta sem travar
        if (_panicAlpha <= 0) return;
        _panicTex ??= new RenderTexture(_device!, CompWidth, CompHeight);
        DrawSource(_panicSlot, _panicSource, _panicTex);
    }

    /// <summary>Cobre a imagem pronta com o fundo da tela de pânico (chamado antes do logo).</summary>
    void DrawPanicMedia(RenderTexture target)
    {
        if (_panicAlpha <= 0 || _panicTex == null || _panicSource is not { Ready: true }) return;
        var ctx = _ctx!;
        ctx.PSSetShaderResource(0, null!);
        ctx.OMSetRenderTargets(target.Rtv);
        ctx.RSSetViewport(new Viewport(CompWidth, CompHeight));
        ctx.OMSetBlendState(_alphaBlend);
        SetFrameParams(CompWidth, CompHeight, 0);
        ctx.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
        ctx.VSSetShader(_vsSprite);
        ctx.PSSetShader(_psSprite);
        ctx.VSSetConstantBuffer(0, _frameCb);
        ctx.VSSetConstantBuffer(4, _spriteCb);
        ctx.PSSetConstantBuffer(4, _spriteCb);
        ctx.PSSetSampler(0, _sampler);
        DrawSprite(_panicTex.Srv, new RectangleF(0, 0, CompWidth, CompHeight), 0, 0, 0, 1, _panicAlpha, 1);
        ctx.OMSetBlendState(null);
        ctx.PSSetShaderResource(0, null!);
    }

    void DisposePanicMedia()
    {
        lock (_panicLock) { _panicPendingPlayer?.Dispose(); _panicPendingPlayer = null; }
        _panicSource?.Dispose();
        _panicSource = null;
        _panicTex?.Dispose();
    }
}
