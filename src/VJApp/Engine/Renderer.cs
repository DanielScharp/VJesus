using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace VJApp.Engine;

[StructLayout(LayoutKind.Sequential)]
struct FrameParams
{
    public float ResolutionX, ResolutionY;
    public float Time;
    public float Speed;
    public float Brightness;
    public float OutputMode;
    public float CompWidth, CompHeight;
    public float Beat;      // batidas desde o início (BPM)
    public float Bpm;
    public float Pad0, Pad1;
}

[StructLayout(LayoutKind.Sequential)]
struct LayerParams
{
    public float Opacity;
    public float Blend;
    public float SourceKind;
    public float VideoAspect;
    public float ColorMode;       // HapColor: 0 RGB, 1 RGBA, 2 YCoCg escalado (HAP Q)
    public float UvScaleX, UvScaleY; // parte útil da textura (HAP arredonda para múltiplos de 4)
    public float TransitionMix;   // 0..1
    public float TransitionKind;
    public float Pad0, Pad1, Pad2;
}

/// <summary>Textura que serve de alvo de render e de entrada de shader.</summary>
sealed class RenderTexture : IDisposable
{
    public readonly ID3D11Texture2D Texture;
    public readonly ID3D11RenderTargetView Rtv;
    public readonly ID3D11ShaderResourceView Srv;

    public RenderTexture(ID3D11Device device, int width, int height)
    {
        Texture = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        Rtv = device.CreateRenderTargetView(Texture);
        Srv = device.CreateShaderResourceView(Texture);
    }

    public void Dispose() { Srv.Dispose(); Rtv.Dispose(); Texture.Dispose(); }
}

/// <summary>
/// Motor de render Direct3D 11. Para cada layer: desenha a fonte (com transição entre o clip anterior
/// e o novo), aplica a cadeia de efeitos e mistura na composição; depois aplica os efeitos do master
/// e apresenta nas telas de saída. Roda numa thread própria.
/// </summary>
sealed partial class Renderer : IDisposable
{
    public const int CompWidth = 1920, CompHeight = 1080;

    public event Action<double>? Stats;
    public event Action<Exception>? Error;
    public event Action<string>? EffectError;

    readonly Thread _thread;
    volatile bool _running = true;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    // parâmetros globais controlados pela UI
    public volatile float Brightness = 1f;
    public volatile float Speed = 1f;
    public volatile bool ShowTestCard;
    public volatile EffectRun[] MasterEffects = [];

    // BPM: batidas contadas a partir de uma origem (o tap tempo realinha a origem)
    volatile float _bpm = 120f;
    double _beatOrigin;
    double _beatAtOrigin;
    readonly object _beatLock = new();

    public LayerSlot[] Layers { get; }

    ID3D11Device? _device;
    ID3D11DeviceContext? _ctx;
    ID3D11VertexShader? _vs;
    ID3D11PixelShader? _psSource, _psBlend, _psTransition;
    ID3D11Buffer? _frameCb, _layerCb, _effectCb;
    ID3D11SamplerState? _sampler;
    readonly RenderTexture?[] _comp = new RenderTexture?[2];  // composição (ping-pong)
    readonly RenderTexture?[] _work = new RenderTexture?[2];  // layer sendo processada (ping-pong)
    RenderTexture? _outgoing;                                 // clip que está saindo numa transição

    // efeitos: shaders compilados e histórico (feedback) por instância
    volatile List<EffectDef>? _pendingDefs;
    readonly Dictionary<string, (EffectDef Def, ID3D11PixelShader Ps)> _effects = [];
    readonly Dictionary<object, RenderTexture> _history = [];
    readonly HashSet<object> _usedThisFrame = [];

    public Renderer(int layerCount, List<EffectDef> effects)
    {
        _pendingDefs = effects;
        Layers = Enumerable.Range(0, layerCount).Select(_ => new LayerSlot()).ToArray();
        _thread = new Thread(Run) { IsBackground = true, Name = "Render" };
        _thread.Start();
    }

    /// <summary>Pede para (re)compilar a biblioteca de efeitos no render thread.</summary>
    public void ReloadEffects(List<EffectDef> defs) => _pendingDefs = defs;

    /// <summary>Troca a fonte de uma layer (null = limpar). O clip anterior sai com a transição da layer.</summary>
    public void SetLayerSource(int layer, SourceKind? kind, VideoPlayer? player = null, string? spoutSender = null, LyricsFeed? lyrics = null)
    {
        var slot = Layers[layer];
        lock (slot.Lock)
        {
            if (slot.HasPending) slot.PendingPlayer?.Dispose();
            slot.PendingKind = kind;
            slot.PendingPlayer = player;
            slot.PendingSpout = spoutSender;
            slot.PendingLyrics = lyrics;
            slot.HasPending = true;
        }
    }

    // ---------------- BPM ----------------

    public float Bpm => _bpm;

    /// <summary>Batida atual (conta contínua; a parte fracionária é a fase dentro da batida).</summary>
    public double Beat
    {
        get { lock (_beatLock) return _beatAtOrigin + (_clock.Elapsed.TotalSeconds - _beatOrigin) * _bpm / 60.0; }
    }

    /// <summary>Muda o BPM sem pular a fase atual.</summary>
    public void SetBpm(float bpm)
    {
        lock (_beatLock)
        {
            double now = _clock.Elapsed.TotalSeconds;
            _beatAtOrigin += (now - _beatOrigin) * _bpm / 60.0;
            _beatOrigin = now;
            _bpm = Math.Clamp(bpm, 20f, 300f);
        }
    }

    /// <summary>Faz a batida atual começar agora (usado pelo tap e pelo botão de ressincronizar).</summary>
    public void ResyncBeat()
    {
        lock (_beatLock)
        {
            double now = _clock.Elapsed.TotalSeconds;
            double beat = _beatAtOrigin + (now - _beatOrigin) * _bpm / 60.0;
            _beatAtOrigin = Math.Round(beat);
            _beatOrigin = now;
        }
    }

    // ---------------- inicialização ----------------

    void Init()
    {
        FeatureLevel[] levels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels,
            out _device, out _ctx).CheckError();

        string path = Path.Combine(AppContext.BaseDirectory, "Shaders", "Engine.hlsl");
        string src = File.ReadAllText(path);
        _vs = _device.CreateVertexShader(Compiler.Compile(src, "VSMain", path, "vs_5_0").Span);
        _psSource = _device.CreatePixelShader(Compiler.Compile(src, "PSSource", path, "ps_5_0").Span);
        _psBlend = _device.CreatePixelShader(Compiler.Compile(src, "PSBlend", path, "ps_5_0").Span);
        _psTransition = _device.CreatePixelShader(Compiler.Compile(src, "PSTransition", path, "ps_5_0").Span);

        _frameCb = CreateCb<FrameParams>();
        _layerCb = CreateCb<LayerParams>();
        _effectCb = _device.CreateBuffer(new BufferDescription(sizeof(float) * EffectDef.MaxParams, BindFlags.ConstantBuffer, ResourceUsage.Default));
        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);

        for (int i = 0; i < 2; i++)
        {
            _comp[i] = new RenderTexture(_device, CompWidth, CompHeight);
            _work[i] = new RenderTexture(_device, CompWidth, CompHeight);
        }
        _outgoing = new RenderTexture(_device, CompWidth, CompHeight);
        InitOutputs(Compiler.Compile(src, "VSSlice", path, "vs_5_0").ToArray(), src, path);
        InitAttacks(src, path);
        InitLyrics(src, path);
    }

    ID3D11Buffer CreateCb<T>() where T : unmanaged =>
        _device!.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<T>(), BindFlags.ConstantBuffer, ResourceUsage.Default));

    /// <summary>Compila os efeitos pendentes. Um efeito com erro mantém a versão anterior.</summary>
    void CompilePendingEffects()
    {
        var defs = Interlocked.Exchange(ref _pendingDefs, null);
        if (defs == null) return;
        foreach (var def in defs)
        {
            try
            {
                var code = Compiler.Compile(def.BuildSource(), "PSMain", def.FilePath, "ps_5_0");
                var ps = _device!.CreatePixelShader(code.Span);
                if (_effects.TryGetValue(def.Id, out var old)) old.Ps.Dispose();
                _effects[def.Id] = (def, ps);
            }
            catch (Exception ex)
            {
                EffectError?.Invoke($"Efeito {def.Name}: {ex.Message.Trim()}");
            }
        }
    }

    // ---------------- fontes e transições ----------------

    void AdoptPendingSources(double now)
    {
        foreach (var slot in Layers)
        {
            LayerSource? incoming;
            lock (slot.Lock)
            {
                if (!slot.HasPending) continue;
                slot.HasPending = false;
                incoming = slot.PendingKind is { } kind ? new LayerSource(kind, slot.PendingPlayer, slot.PendingSpout, slot.PendingLyrics) : null;
                slot.PendingPlayer = null;
            }
            incoming?.CreateGpu(_device!);

            var old = slot.Current;
            bool animate = slot.Transition != (int)TransitionKind.Cut && slot.TransitionSeconds > 0.01f && old != null && old.Ready;
            slot.Outgoing?.Dispose(); // uma transição em andamento é substituída pela nova
            if (animate)
            {
                slot.Outgoing = old;
                slot.TransitionStart = now;
                slot.TransitionLength = slot.TransitionSeconds;
                slot.TransitionProgress = 0;
            }
            else
            {
                old?.Dispose();
                slot.Outgoing = null;
                slot.TransitionProgress = 1;
            }
            slot.Current = incoming;
        }
    }

    // ---------------- desenho ----------------

    /// <summary>Desenha um triângulo de tela cheia em <paramref name="target"/> lendo as texturas dadas.</summary>
    void Pass(ID3D11PixelShader ps, ID3D11RenderTargetView target, ID3D11ShaderResourceView? t0, ID3D11ShaderResourceView? t1 = null)
    {
        var ctx = _ctx!;
        ctx.PSSetShaderResource(0, null!); // solta entradas antes de trocar o alvo
        ctx.PSSetShaderResource(1, null!);
        ctx.OMSetRenderTargets(target);
        if (t0 != null) ctx.PSSetShaderResource(0, t0);
        if (t1 != null) ctx.PSSetShaderResource(1, t1);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.VSSetShader(_vs);
        ctx.PSSetShader(ps);
        ctx.PSSetConstantBuffer(0, _frameCb);
        ctx.PSSetConstantBuffer(1, _layerCb);
        ctx.PSSetConstantBuffer(2, _effectCb);
        ctx.PSSetSampler(0, _sampler);
        ctx.Draw(3, 0);
    }

    void SetFrameParams(int width, int height, float time)
    {
        var f = new FrameParams
        {
            ResolutionX = width,
            ResolutionY = height,
            Time = time,
            Speed = Speed,
            Brightness = Brightness,
            OutputMode = 0,
            CompWidth = CompWidth,
            CompHeight = CompHeight,
            Beat = (float)Beat,
            Bpm = _bpm,
        };
        _ctx!.UpdateSubresource(in f, _frameCb!);
    }

    /// <summary>Desenha uma fonte (vídeo encaixado, HAP, Spout ou gerador) numa textura 1920×1080.</summary>
    void DrawSource(LayerSlot slot, LayerSource? source, RenderTexture target)
    {
        if (source == null || !source.Ready)
        {
            _ctx!.ClearRenderTargetView(target.Rtv, new Color4(0, 0, 0, 0)); // transparente
            return;
        }
        var p = source.Player;
        bool fromPlayer = p != null && source.Spout == null;
        var lp = new LayerParams
        {
            Opacity = slot.Opacity,
            Blend = slot.Blend,
            SourceKind = (float)source.Kind,
            VideoAspect = source.Lyrics != null ? (float)LyricsFeed.Width / LyricsFeed.Height : source.Aspect,
            ColorMode = source.Lyrics != null ? 3 : fromPlayer ? (float)p!.Color : 0,
            UvScaleX = fromPlayer ? (float)p!.Width / p.TextureWidth : 1,
            UvScaleY = fromPlayer ? (float)p!.Height / p.TextureHeight : 1,
        };
        _ctx!.UpdateSubresource(in lp, _layerCb!);
        Pass(_psSource!, target.Rtv, source.InputSrv);
    }

    /// <summary>Aplica a cadeia de efeitos alternando entre as duas texturas; retorna onde ficou o resultado.</summary>
    unsafe int ApplyEffects(EffectRun[] chain, RenderTexture?[] pair, int cur, bool updateHistory = true)
    {
        foreach (var run in chain)
        {
            if (!run.Enabled || !_effects.TryGetValue(run.EffectId, out var fx)) continue;

            fixed (float* v = run.Values)
                _ctx!.UpdateSubresource(_effectCb!, 0, null, (IntPtr)v, 0, 0);

            RenderTexture? history = null;
            if (fx.Def.Feedback)
            {
                if (updateHistory)
                {
                    _usedThisFrame.Add(run.Key);
                    if (!_history.TryGetValue(run.Key, out history))
                        _history[run.Key] = history = new RenderTexture(_device!, CompWidth, CompHeight);
                }
                else _history.TryGetValue(run.Key, out history); // só lê o rastro da passada principal
            }

            int next = 1 - cur;
            Pass(fx.Ps, pair[next]!.Rtv, pair[cur]!.Srv, history?.Srv);
            if (history != null && updateHistory)
            {
                _ctx!.PSSetShaderResource(1, null!);
                _ctx.CopyResource(history.Texture, pair[next]!.Texture);
            }
            cur = next;
        }
        return cur;
    }

    /// <summary>Sobe os quadros novos de cada layer e avança as transições (uma vez por quadro).</summary>
    void UpdateSources(double now)
    {
        foreach (var slot in Layers)
        {
            slot.Current?.Update(_device!, _ctx!);
            slot.Outgoing?.Update(_device!, _ctx!);
            if (slot.Current?.Lyrics != null) UpdateLyrics(slot.Current, now, (float)now);
            if (slot.Outgoing?.Lyrics != null) UpdateLyrics(slot.Outgoing, now, (float)now);

            // fim da transição: o clip anterior é liberado
            if (slot.Outgoing != null)
            {
                float progress = (float)Math.Clamp((now - slot.TransitionStart) / Math.Max(0.001, slot.TransitionLength), 0, 1);
                slot.TransitionProgress = progress;
                if (progress >= 1) { slot.Outgoing.Dispose(); slot.Outgoing = null; }
            }
        }
    }

    /// <summary>
    /// Monta uma composição em <paramref name="pair"/>; retorna o índice da textura com o resultado.
    /// A principal (main) tem todas as layers, efeitos do master e test card; a do retorno só as layers marcadas.
    /// </summary>
    int Compose(float time, RenderTexture?[] pair, Func<LayerSlot, bool> include, bool main)
    {
        var ctx = _ctx!;
        int cur = 0;
        if (main) _usedThisFrame.Clear();
        ctx.ClearRenderTargetView(pair[cur]!.Rtv, new Color4(0, 0, 0, 1));
        SetFrameParams(CompWidth, CompHeight, time);
        ctx.RSSetViewport(new Viewport(CompWidth, CompHeight));

        foreach (var slot in Layers)
        {
            if (!include(slot)) continue;
            bool hasCurrent = slot.Current is { Ready: true };
            if ((!hasCurrent && slot.Outgoing == null) || slot.Opacity <= 0) continue;

            // 1) fonte -> work[0] (com transição: o novo em work[0], o anterior em _outgoing, mistura em work[1])
            DrawSource(slot, slot.Current, _work[0]!);
            int w = 0;
            if (slot.Outgoing != null)
            {
                DrawSource(slot, slot.Outgoing, _outgoing!);
                var lp = new LayerParams { TransitionMix = slot.TransitionProgress, TransitionKind = slot.Transition };
                ctx.UpdateSubresource(in lp, _layerCb!);
                Pass(_psTransition!, _work[1]!.Rtv, _work[0]!.Srv, _outgoing!.Srv);
                w = 1;
            }

            // 2) efeitos da layer   3) mistura na composição
            w = ApplyEffects(slot.Effects, _work, w, updateHistory: main);
            var blend = new LayerParams { Opacity = slot.Opacity, Blend = slot.Blend };
            ctx.UpdateSubresource(in blend, _layerCb!);
            int next = 1 - cur;
            Pass(_psBlend!, pair[next]!.Rtv, pair[cur]!.Srv, _work[w]!.Srv);
            cur = next;
        }
        if (!main) return cur;

        cur = ApplyEffects(MasterEffects, pair, cur);

        // test card no lugar da composição, para alinhar os slices nos projetores
        if (ShowTestCard)
        {
            var lp = new LayerParams { Opacity = 1, SourceKind = (float)SourceKind.TestCard };
            ctx.UpdateSubresource(in lp, _layerCb!);
            cur = 1 - cur;
            Pass(_psSource!, pair[cur]!.Rtv, null);
        }

        // libera o histórico de efeitos de feedback que saíram de uso
        if (_history.Count > _usedThisFrame.Count)
            foreach (var key in _history.Keys.Where(k => !_usedThisFrame.Contains(k)).ToList())
            {
                _history[key].Dispose();
                _history.Remove(key);
            }
        return cur;
    }

    void Run()
    {
        try { Init(); }
        catch (Exception ex) { Error?.Invoke(ex); return; }

        var fpsClock = Stopwatch.StartNew();
        int frames = 0;

        while (_running)
        {
            double now = _clock.Elapsed.TotalSeconds;
            ApplyPendingOutputs();
            ApplyStreaming();
            CompilePendingEffects();
            AdoptPendingSources(now);

            float time = (float)now;
            UpdateSources(now);
            int result = Compose(time, _comp, _ => true, main: true);
            result = ApplyAttacks(_comp, result, time);

            SendStreams(_comp[result]!);
            UpdatePreview(_comp[result]!, time);
            UpdateStageFeed(_comp[result]!, time);
            RenderOutputs(_comp[result]!, ComposeGroups(time), time);

            frames++;
            if (fpsClock.ElapsedMilliseconds >= 1000)
            {
                Stats?.Invoke(frames * 1000.0 / fpsClock.ElapsedMilliseconds);
                frames = 0;
                fpsClock.Restart();
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(2000);
        foreach (var slot in Layers)
        {
            slot.Current?.Dispose();
            slot.Outgoing?.Dispose();
            lock (slot.Lock) slot.PendingPlayer?.Dispose();
        }
        foreach (var h in _history.Values) h.Dispose();
        foreach (var fx in _effects.Values) fx.Ps.Dispose();
        for (int i = 0; i < 2; i++) { _comp[i]?.Dispose(); _work[i]?.Dispose(); }
        _outgoing?.Dispose();
        _sampler?.Dispose();
        _frameCb?.Dispose();
        _layerCb?.Dispose();
        _effectCb?.Dispose();
        _psSource?.Dispose();
        _psBlend?.Dispose();
        _psTransition?.Dispose();
        DisposeStreams();
        DisposeGroups();
        DisposeOutputs();
        DisposeAttacks();
        DisposeLyrics();
        _vs?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
    }
}
