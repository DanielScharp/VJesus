using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>Fonte de uma layer no motor (a ordem bate com SourceKind no shader).</summary>
enum SourceKind { Video = 0, Plasma = 1, TestCard = 2 }

/// <summary>Tipo de transição ao trocar o clip de uma layer (a ordem bate com o shader).</summary>
enum TransitionKind { Cut = 0, Crossfade = 1, Dip = 2, Wipe = 3, Push = 4, Zoom = 5, Blur = 6 }

/// <summary>Uma fonte tocando numa layer: vídeo/câmera (com textura), Spout, letra ou gerador. Só o render thread mexe.</summary>
sealed class LayerSource(SourceKind kind, VideoPlayer? player, string? spoutSender, LyricsFeed? lyrics = null, string? ndiSource = null)
{
    public readonly SourceKind Kind = kind;
    public readonly VideoPlayer? Player = player;
    public readonly LyricsFeed? Lyrics = lyrics;
    /// <summary>Letra animada desta layer (montada pelo Renderer a cada quadro).</summary>
    public LyricsGpu? LyricsGpu;
    public readonly SpoutReceiver? Spout = spoutSender != null ? new SpoutReceiver(spoutSender) : null;
    /// <summary>Fonte NDI da rede (null se não conseguiu abrir: sem NDI Runtime, por exemplo).</summary>
    public readonly NdiReceiver? Ndi = OpenNdi(ndiSource);

    static NdiReceiver? OpenNdi(string? name)
    {
        if (name == null) return null;
        try { return new NdiReceiver(name); } catch { return null; }
    }
    /// <summary>Como o clip estava na tela no último quadro (para sair igual, mesmo que o próximo clip mude o ajuste).</summary>
    public ClipLook? Look;
    public VideoFrame? ShownFrame;
    public ID3D11Texture2D? Texture;
    public ID3D11ShaderResourceView? Srv;

    /// <summary>Textura de entrada para o shader (do vídeo ou do Spout).</summary>
    public ID3D11ShaderResourceView? InputSrv => LyricsGpu?.Target?.Srv ?? Spout?.Srv ?? Ndi?.Srv ?? Srv;

    /// <summary>Pronta para desenhar? (vídeo precisa do primeiro quadro, Spout precisa do sender)</summary>
    public bool Ready => Kind != SourceKind.Video || ShownFrame != null || Spout?.Srv != null || Ndi?.Srv != null || LyricsGpu?.Drawn == true;

    public float Aspect => Spout is { Srv: not null } s ? (float)s.Width / s.Height
        : Ndi is { Srv: not null } n ? (float)n.Width / n.Height
        : Player != null ? (float)Player.Width / Player.Height : 16f / 9;

    public void CreateGpu(ID3D11Device device)
    {
        if (Player is not { } p) return;
        Texture = device.CreateTexture2D(new Texture2DDescription(
            p.TextureFormat, (uint)p.TextureWidth, (uint)p.TextureHeight, 1, 1,
            BindFlags.ShaderResource, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        Srv = device.CreateShaderResourceView(Texture);
    }

    /// <summary>Sobe o quadro de vídeo novo (BGRA ou blocos HAP) e confere o sender Spout.</summary>
    public unsafe void Update(ID3D11Device device, ID3D11DeviceContext ctx)
    {
        Spout?.Update(device);
        Ndi?.Update(device, ctx);
        if (Player is not { } player || Texture == null) return;
        var frame = player.TakeFrame(ShownFrame);
        if (frame == null) return;
        ShownFrame = frame;

        var mapped = ctx.Map(Texture, 0, MapMode.WriteDiscard);
        fixed (byte* src = frame.Pixels)
        {
            for (int y = 0; y < frame.Rows; y++)
                Buffer.MemoryCopy(src + y * frame.Stride, (byte*)mapped.DataPointer + y * mapped.RowPitch, mapped.RowPitch, frame.Stride);
        }
        ctx.Unmap(Texture, 0);
    }

    /// <summary>Libera a GPU agora e o player em segundo plano (fechar o decoder pode demorar).</summary>
    bool _disposed;

    public void Dispose()
    {
        if (_disposed) return; // a troca de clips pode pedir para liberar a mesma fonte por dois caminhos
        _disposed = true;
        Spout?.Dispose();
        if (Ndi is { } ndi) Task.Run(ndi.Dispose); // desconectar da rede pode demorar
        LyricsGpu?.Dispose();
        Srv?.Dispose();
        Texture?.Dispose();
        if (Player is { } p) Task.Run(p.Dispose);
    }
}

/// <summary>Posição, tamanho, giro e cantos de um clip ou de uma layer, pronto para o shader.</summary>
sealed record Shape(float X, float Y, float ScaleX, float ScaleY, float Rotation, float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    public static readonly Shape Identity = new(0, 0, 1, 1, 0, 0, 0, 0, 0);
}

/// <summary>Ajuste de tela de um clip (encaixe, zoom, posição e forma) num instante.</summary>
sealed record ClipLook(int Fit, float Zoom, float OffsetX, float OffsetY, Shape Shape);

/// <summary>Estado de uma layer do lado do motor.</summary>
sealed class LayerSlot
{
    public volatile float Opacity = 1f;
    public volatile int Blend;
    public volatile EffectRun[] Effects = [];
    public volatile int Transition;          // TransitionKind
    public volatile float TransitionSeconds;
    /// <summary>Quando a transição acontece: de vazio para clip, entre dois clips, de clip para vazio.</summary>
    public volatile bool TransitionIn = true, TransitionBetween = true, TransitionOut = true;
    /// <summary>Há uma transição em andamento (o que sai pode ser "nada", na entrada).</summary>
    public volatile bool TransitionActive;
    /// <summary>A transição só começa a contar quando o clip novo tem o primeiro quadro.</summary>
    public bool TransitionWaiting;
    public double TransitionWaitSince;
    /// <summary>De onde a transição parte (0..1): ao trocar no meio de outra, continua do ponto em que a tela estava.</summary>
    public float TransitionFrom;
    /// <summary>Layers disparadas juntas (coluna, carrossel) esperam umas pelas outras para começar a transição.</summary>
    public int TransitionGroup, PendingGroup;
    public volatile float Volume = 1f;
    /// <summary>Esta layer aparece no item "Telão" da tela de retorno (quando está tocando).</summary>
    public volatile bool ToStage;
    /// <summary>Parâmetros do fundo gerado desta layer (troca na hora, sem reiniciar o clip).</summary>
    public GenParams Gen = GenParams.Default;
    /// <summary>Ajustes do clip tocando: encaixe, zoom, posição, efeitos e volume próprios.</summary>
    public volatile int ClipFit;
    public volatile float ClipZoom = 1, ClipOffsetX, ClipOffsetY, ClipVolume = 1;
    public volatile EffectRun[] ClipEffects = [];
    /// <summary>Giro, largura/altura e cantos do clip tocando; e a forma da layer inteira.</summary>
    public volatile Shape ClipShape = Shape.Identity;
    public volatile Shape LayerShape = Shape.Identity;

    // troca de fonte pedida pela UI, adotada pelo render thread
    public readonly object Lock = new();
    public bool HasPending;
    public SourceKind? PendingKind;
    public VideoPlayer? PendingPlayer;
    public string? PendingSpout;
    public string? PendingNdi;
    public LyricsFeed? PendingLyrics;

    // estado do render thread (o mixer de áudio só lê as referências)
    public volatile LayerSource? Current;
    public volatile LayerSource? Outgoing;   // clip anterior, saindo durante a transição
    /// <summary>Troca no meio de outra transição: o clip que já estava saindo, misturado com Outgoing por OutgoingMix.</summary>
    public volatile LayerSource? OutgoingUnder;
    public volatile float OutgoingMix = 1;
    public double TransitionStart;
    public double TransitionLength;
    /// <summary>Progresso da transição 0..1 (1 = terminada). Lido pelo mixer de áudio.</summary>
    public volatile float TransitionProgress = 1f;
}
