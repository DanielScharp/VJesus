using System.Numerics;
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
    public float FitMode;         // 0 encaixar, 1 preencher, 2 esticar
    public float Zoom;            // 1 = tamanho normal
    public float OffsetX, OffsetY; // deslocamento (-1..1 = meia tela)
    public float ClipRotation, ClipScaleX, ClipScaleY; // giro (rad), largura e altura do clip
    public float ClipRadiusTL, ClipRadiusTR, ClipRadiusBR, ClipRadiusBL; // cantos (px)
    public float LayerX, LayerY, LayerScaleX, LayerScaleY;
    public float LayerRotation, Pad0, Pad1, Pad2;
    public float LayerRadiusTL, LayerRadiusTR, LayerRadiusBR, LayerRadiusBL;
}

/// <summary>Fundo gerado (antigo Plasma): tipo, cores, velocidade, intensidade, escala. Igual ao cbuffer GenParams.</summary>
struct GenParams
{
    public Vector4 Color1, Color2, Color3;
    public float Type, Speed, Intensity, Scale;
    public float Detail, Angle, Pad0, Pad1;

    public static GenParams Default => new()
    {
        Color1 = new(0.02f, 0.05f, 0.18f, 1), Color2 = new(0.10f, 0.25f, 0.60f, 1), Color3 = new(0.55f, 0.75f, 1f, 1),
        Type = 6, Speed = 1, Intensity = 1, Scale = 1, Detail = 0.5f,
    };
}

/// <summary>Textura que serve de alvo de render e de entrada de shader.</summary>
sealed class RenderTexture : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public readonly ID3D11Texture2D Texture;
    public readonly ID3D11RenderTargetView Rtv;
    public readonly ID3D11ShaderResourceView Srv;

    public RenderTexture(ID3D11Device device, int width, int height)
    {
        Width = width;
        Height = height;
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

    volatile LayerSlot[] _layers;
    /// <summary>Layers do motor (a UI pode trocar o arranjo: inserir, remover, mover).</summary>
    public LayerSlot[] Layers => _layers;
    readonly System.Collections.Concurrent.ConcurrentQueue<LayerSlot> _retired = new();

    /// <summary>
    /// Novo arranjo de layers (chamado pela UI). As layers que saíram têm as fontes liberadas no render thread.
    /// </summary>
    public void SetLayers(LayerSlot[] slots)
    {
        var old = _layers;
        _layers = slots;
        foreach (var s in old)
            if (Array.IndexOf(slots, s) < 0) _retired.Enqueue(s);
    }

    void DisposeRetired()
    {
        while (_retired.TryDequeue(out var s))
        {
            lock (s.Lock)
            {
                if (s.HasPending) s.PendingPlayer?.Dispose();
                s.HasPending = false;
                s.PendingPlayer = null;
            }
            s.Outgoing?.Dispose(); s.Outgoing = null;
            s.OutgoingUnder?.Dispose(); s.OutgoingUnder = null;
            s.Current?.Dispose(); s.Current = null;
        }
    }

    ID3D11Device? _device;
    ID3D11DeviceContext? _ctx;
    ID3D11VertexShader? _vs;
    ID3D11PixelShader? _psSource, _psBlend, _psTransition;
    ID3D11Buffer? _frameCb, _layerCb, _effectCb, _genCb;
    ID3D11SamplerState? _sampler;
    readonly RenderTexture?[] _comp = new RenderTexture?[2];  // composição (ping-pong)
    readonly RenderTexture?[] _work = new RenderTexture?[2];  // layer sendo processada (ping-pong)
    RenderTexture? _outgoing;                                 // clip que está saindo numa transição
    RenderTexture? _outgoingUnder, _outgoingMixed;            // troca no meio de outra: os dois que saem, misturados

    // efeitos: shaders compilados e histórico (feedback) por instância
    volatile List<EffectDef>? _pendingDefs;
    readonly Dictionary<string, (EffectDef Def, ID3D11PixelShader Ps)> _effects = [];
    readonly Dictionary<object, RenderTexture> _history = [];
    readonly HashSet<object> _usedThisFrame = [];

    public Renderer(int layerCount, List<EffectDef> effects)
    {
        _pendingDefs = effects;
        _layers = Enumerable.Range(0, layerCount).Select(_ => new LayerSlot()).ToArray();
        _thread = new Thread(Run) { IsBackground = true, Name = "Render" };
        _thread.Start();
    }

    /// <summary>Pede para (re)compilar a biblioteca de efeitos no render thread.</summary>
    public void ReloadEffects(List<EffectDef> defs) => _pendingDefs = defs;

    /// <summary>Enquanto diferente de 0, as trocas pedidas entram no mesmo grupo (coluna inteira, carrossel).</summary>
    public volatile int TriggerGroup;

    /// <summary>Troca a fonte de uma layer (null = limpar). O clip anterior sai com a transição da layer.</summary>
    public void SetLayerSource(int layer, SourceKind? kind, VideoPlayer? player = null, string? spoutSender = null, LyricsFeed? lyrics = null, string? ndiSource = null)
    {
        var slot = Layers[layer];
        lock (slot.Lock)
        {
            if (slot.HasPending) slot.PendingPlayer?.Dispose();
            slot.PendingKind = kind;
            slot.PendingPlayer = player;
            slot.PendingSpout = spoutSender;
            slot.PendingNdi = ndiSource;
            slot.PendingLyrics = lyrics;
            slot.PendingGroup = TriggerGroup;
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
        _genCb = CreateCb<GenParams>();
        _effectCb = _device.CreateBuffer(new BufferDescription(sizeof(float) * EffectDef.MaxParams, BindFlags.ConstantBuffer, ResourceUsage.Default));
        _sampler = _device.CreateSamplerState(SamplerDescription.LinearClamp);

        for (int i = 0; i < 2; i++)
        {
            _comp[i] = new RenderTexture(_device, CompWidth, CompHeight);
            _work[i] = new RenderTexture(_device, CompWidth, CompHeight);
        }
        _outgoing = new RenderTexture(_device, CompWidth, CompHeight);
        _outgoingUnder = new RenderTexture(_device, CompWidth, CompHeight);
        _outgoingMixed = new RenderTexture(_device, CompWidth, CompHeight);
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
                incoming = slot.PendingKind is { } kind ? new LayerSource(kind, slot.PendingPlayer, slot.PendingSpout, slot.PendingLyrics, slot.PendingNdi) : null;
                slot.PendingPlayer = null;
                slot.TransitionGroup = slot.PendingGroup;
            }
            incoming?.CreateGpu(_device!);

            var old = slot.Current;
            var previous = slot.Outgoing;
            var under = slot.OutgoingUnder;
            bool oldReady = old is { Ready: true };
            // o que sai é exatamente o que está na tela: se outra transição estava no meio, os dois clips
            // dela saem juntos, misturados na mesma proporção (nada some de repente)
            LayerSource? leaving = null, leavingUnder = null;
            float mix = 1;
            if (slot.TransitionActive)
            {
                // o que está na tela agora: até três imagens (as duas que já saíam e a que estava entrando);
                // as duas mais visíveis saem juntas, na mesma proporção, e a menos visível é liberada
                float p = slot.TransitionProgress, e = p * p * (3 - 2 * p), m = slot.OutgoingMix;
                var parts = new List<(LayerSource? Src, float W, int Age)>
                {
                    (under, (1 - e) * (1 - m), 0),
                    (previous, (1 - e) * m, 1),
                    (oldReady ? old : null, e, 2),
                };
                var keep = parts.OrderByDescending(x => x.W).Take(2).OrderBy(x => x.Age).ToList();
                leavingUnder = keep[0].Src;
                leaving = keep[1].Src;
                float sum = keep[0].W + keep[1].W;
                mix = sum > 0.0001f ? keep[1].W / sum : 1;
                foreach (var x in parts)
                    if (x.Src != null && x.Src != leaving && x.Src != leavingUnder) x.Src.Dispose();
                if (!oldReady && old != leaving && old != leavingUnder) old?.Dispose();
            }
            else
            {
                if (oldReady) leaving = old; else old?.Dispose();
            }
            bool hasOld = leaving != null || leavingUnder != null, hasNew = incoming != null;
            // entrada (vazio → clip), troca (clip → clip) e saída (clip → vazio) podem ter ou não transição
            bool wanted = hasOld && hasNew ? slot.TransitionBetween : hasNew ? slot.TransitionIn : hasOld && slot.TransitionOut;
            bool animate = slot.Transition != (int)TransitionKind.Cut && slot.TransitionSeconds > 0.01f && wanted;
            if (!animate)
            {
                leaving?.Dispose();
                leavingUnder?.Dispose();
                if (previous != leaving && previous != leavingUnder) previous?.Dispose();
                slot.Outgoing = null;
                slot.OutgoingUnder = null;
                slot.TransitionProgress = 1;
                slot.TransitionActive = false;
            }
            else
            {
                if (previous != leaving && previous != leavingUnder) previous?.Dispose();
                slot.Outgoing = leaving; // na entrada, o que "sai" é a layer vazia (null)
                slot.OutgoingUnder = leavingUnder;
                slot.OutgoingMix = mix;
                slot.TransitionFrom = 0;
                slot.TransitionStart = now;
                slot.TransitionLength = slot.TransitionSeconds;
                slot.TransitionProgress = 0;
                // só começa quando o clip novo aparecer; uma saída disparada junto com a entrada de outra layer
                // (fim do vídeo indo para outra layer) espera essa entrada, para não abrir um buraco preto
                slot.TransitionWaiting = hasNew || slot.TransitionGroup != 0;
                slot.TransitionWaitSince = now;
                slot.TransitionActive = true;
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
        ctx.PSSetConstantBuffer(5, _genCb);
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
        bool fromPlayer = p != null && source.Spout == null && source.Ndi == null;
        // o clip tocando usa o ajuste atual da layer (e guarda); o que está saindo usa o último dele
        var look = source == slot.Current || source.Look == null
            ? source.Look = new ClipLook(slot.ClipFit, slot.ClipZoom, slot.ClipOffsetX, slot.ClipOffsetY, slot.ClipShape)
            : source.Look;
        var lp = new LayerParams
        {
            Opacity = slot.Opacity,
            Blend = slot.Blend,
            SourceKind = (float)source.Kind,
            VideoAspect = source.Lyrics != null ? (float)LyricsFeed.Width / LyricsFeed.Height : source.Aspect,
            ColorMode = source.Lyrics != null ? 3 : fromPlayer ? (float)p!.Color : 0,
            UvScaleX = fromPlayer ? (float)p!.Width / p.TextureWidth : 1,
            UvScaleY = fromPlayer ? (float)p!.Height / p.TextureHeight : 1,
            // ajuste do clip (letras ficam sempre no padrão)
            FitMode = source.Lyrics != null ? 0 : look.Fit,
            Zoom = source.Lyrics != null ? 1 : look.Zoom,
            OffsetX = source.Lyrics != null ? 0 : look.OffsetX,
            OffsetY = source.Lyrics != null ? 0 : look.OffsetY,
        };
        var cs = source.Lyrics != null ? Shape.Identity : look.Shape;
        lp.ClipRotation = cs.Rotation * MathF.PI / 180;
        lp.ClipScaleX = cs.ScaleX;
        lp.ClipScaleY = cs.ScaleY;
        (lp.ClipRadiusTL, lp.ClipRadiusTR, lp.ClipRadiusBR, lp.ClipRadiusBL) = (cs.TopLeft, cs.TopRight, cs.BottomRight, cs.BottomLeft);
        _ctx!.UpdateSubresource(in lp, _layerCb!);
        if (source.Kind == SourceKind.Plasma)
        {
            // o fundo tocando usa os ajustes atuais (e guarda); o que está saindo continua com as cores dele
            var g = source == slot.Current || source.Gen == null ? (source.Gen = slot.Gen).Value : source.Gen.Value;
            _ctx.UpdateSubresource(in g, _genCb!);
        }
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
        // grupos (coluna inteira, carrossel) em que alguma layer ainda espera o primeiro quadro do clip novo
        var waitingGroups = new HashSet<int>();
        foreach (var s in Layers)
            if (s.TransitionWaiting && s.TransitionGroup != 0 && s.Current is { Ready: false } && now - s.TransitionWaitSince < 3)
                waitingGroups.Add(s.TransitionGroup);

        foreach (var slot in Layers)
        {
            slot.Current?.Update(_device!, _ctx!);
            slot.Outgoing?.Update(_device!, _ctx!);
            slot.OutgoingUnder?.Update(_device!, _ctx!);
            if (slot.OutgoingUnder?.Lyrics != null) UpdateLyrics(slot.OutgoingUnder, now, (float)now);
            if (slot.Current?.Lyrics != null) UpdateLyrics(slot.Current, now, (float)now);
            if (slot.Outgoing?.Lyrics != null) UpdateLyrics(slot.Outgoing, now, (float)now);

            if (slot.TransitionActive)
            {
                // abrir um vídeo leva alguns quadros: enquanto o novo não aparece, o anterior fica inteiro
                // (no máximo 3 s, para um arquivo com problema não prender a layer)
                if (slot.TransitionWaiting)
                {
                    bool self = slot.Current is { Ready: false };
                    bool group = slot.TransitionGroup != 0 && waitingGroups.Contains(slot.TransitionGroup);
                    if ((self || group) && now - slot.TransitionWaitSince < 3) slot.TransitionStart = now;
                    else slot.TransitionWaiting = false;
                }
                float progress = (float)Math.Clamp(slot.TransitionFrom + (now - slot.TransitionStart) / Math.Max(0.001, slot.TransitionLength), 0, 1);
                slot.TransitionProgress = progress;
                // fim da transição: o clip anterior é liberado
                if (progress >= 1)
                {
                    slot.Outgoing?.Dispose();
                    slot.Outgoing = null;
                    slot.OutgoingUnder?.Dispose();
                    slot.OutgoingUnder = null;
                    slot.OutgoingMix = 1;
                    slot.TransitionActive = false;
                }
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
            if ((!hasCurrent && !slot.TransitionActive) || slot.Opacity <= 0) continue;

            // 1) fonte -> work[0] (com transição: o novo em work[0], o anterior em _outgoing, mistura em work[1])
            DrawSource(slot, slot.Current, _work[0]!);
            // efeitos só deste clip (antes da transição e dos efeitos da layer)
            int w = slot.ClipEffects.Length > 0 && hasCurrent ? ApplyEffects(slot.ClipEffects, _work, 0, updateHistory: main) : 0;
            if (slot.TransitionActive)
            {
                DrawSource(slot, slot.Outgoing, _outgoing!); // sem clip anterior: fica transparente
                var leavingTex = _outgoing!;
                if (slot.OutgoingUnder != null || slot.OutgoingMix < 0.999f)
                {
                    // o que saía de uma transição interrompida: os dois clips na proporção em que estavam
                    DrawSource(slot, slot.OutgoingUnder, _outgoingUnder!);
                    var mixLp = new LayerParams { TransitionMix = slot.OutgoingMix, TransitionKind = 7 };
                    ctx.UpdateSubresource(in mixLp, _layerCb!);
                    Pass(_psTransition!, _outgoingMixed!.Rtv, _outgoing!.Srv, _outgoingUnder!.Srv);
                    leavingTex = _outgoingMixed!;
                }
                var lp = new LayerParams { TransitionMix = slot.TransitionProgress, TransitionKind = slot.Transition };
                ctx.UpdateSubresource(in lp, _layerCb!);
                Pass(_psTransition!, _work[1 - w]!.Rtv, _work[w]!.Srv, leavingTex.Srv);
                w = 1 - w;
            }

            // 2) efeitos da layer   3) mistura na composição
            w = ApplyEffects(slot.Effects, _work, w, updateHistory: main);
            var ls = slot.LayerShape;
            var blend = new LayerParams
            {
                Opacity = slot.Opacity, Blend = slot.Blend,
                LayerX = ls.X, LayerY = ls.Y, LayerScaleX = ls.ScaleX, LayerScaleY = ls.ScaleY,
                LayerRotation = ls.Rotation * MathF.PI / 180,
                LayerRadiusTL = ls.TopLeft, LayerRadiusTR = ls.TopRight, LayerRadiusBR = ls.BottomRight, LayerRadiusBL = ls.BottomLeft,
            };
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

    /// <summary>VJ_PERF=caminho: grava, a cada segundo, quanto tempo cada etapa do quadro levou (diagnóstico).</summary>
    static readonly string? PerfLog = Environment.GetEnvironmentVariable("VJ_PERF");
    readonly long[] _perf = new long[4];

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
            DisposeRetired();
            ApplyStreaming();
            CompilePendingEffects();
            AdoptPendingSources(now);

            float time = (float)now;
            long p0 = Stopwatch.GetTimestamp();
            SharedVideo.Frame++;
            UpdateSources(now);
            long p1 = Stopwatch.GetTimestamp();
            int result = Compose(time, _comp, _ => true, main: true);
            result = ApplyAttacks(_comp, result, time);
            DrawOverlay(_comp[result]!, now, time);

            SendStreams(_comp[result]!);
            long p2 = Stopwatch.GetTimestamp();
            UpdatePreview(_comp[result]!, time);
            UpdateStageFeed(_comp[result]!, time);
            long p3 = Stopwatch.GetTimestamp();
            RenderOutputs(_comp[result]!, ComposeGroups(time, now), time);
            long p4 = Stopwatch.GetTimestamp();
            if (PerfLog != null) { _perf[0] += p1 - p0; _perf[1] += p2 - p1; _perf[2] += p3 - p2; _perf[3] += p4 - p3; }

            frames++;
            if (fpsClock.ElapsedMilliseconds >= 1000)
            {
                if (PerfLog != null && frames > 0)
                {
                    double ms(long t) => t * 1000.0 / Stopwatch.Frequency / frames;
                    File.AppendAllText(PerfLog, $"{DateTime.Now:HH:mm:ss} fps {frames} | por quadro (ms): videos {ms(_perf[0]):0.0}  composicao {ms(_perf[1]):0.0}  previa {ms(_perf[2]):0.0}  saidas {ms(_perf[3]):0.0}  | faltas de audio {Interlocked.Exchange(ref VideoPlayer.AudioUnderruns, 0)} {VideoPlayer.LastUnderrun}\n");
                    Array.Clear(_perf);
                }
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
            slot.OutgoingUnder?.Dispose();
            lock (slot.Lock) slot.PendingPlayer?.Dispose();
        }
        foreach (var h in _history.Values) h.Dispose();
        foreach (var fx in _effects.Values) fx.Ps.Dispose();
        for (int i = 0; i < 2; i++) { _comp[i]?.Dispose(); _work[i]?.Dispose(); }
        _outgoing?.Dispose();
        _outgoingUnder?.Dispose();
        _outgoingMixed?.Dispose();
        _sampler?.Dispose();
        _frameCb?.Dispose();
        _layerCb?.Dispose();
        _genCb?.Dispose();
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
