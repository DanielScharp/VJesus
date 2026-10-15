using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>Fonte de uma layer no motor (a ordem bate com SourceKind no shader).</summary>
enum SourceKind { Video = 0, Plasma = 1, TestCard = 2 }

/// <summary>Tipo de transição ao trocar o clip de uma layer (a ordem bate com o shader).</summary>
enum TransitionKind { Cut = 0, Crossfade = 1, Dip = 2, Wipe = 3 }

/// <summary>Uma fonte tocando numa layer: vídeo/câmera (com textura), Spout, letra ou gerador. Só o render thread mexe.</summary>
sealed class LayerSource(SourceKind kind, VideoPlayer? player, string? spoutSender, LyricsFeed? lyrics = null)
{
    public readonly SourceKind Kind = kind;
    public readonly VideoPlayer? Player = player;
    public readonly LyricsFeed? Lyrics = lyrics;
    /// <summary>Letra animada desta layer (montada pelo Renderer a cada quadro).</summary>
    public LyricsGpu? LyricsGpu;
    public readonly SpoutReceiver? Spout = spoutSender != null ? new SpoutReceiver(spoutSender) : null;
    public VideoFrame? ShownFrame;
    public ID3D11Texture2D? Texture;
    public ID3D11ShaderResourceView? Srv;

    /// <summary>Textura de entrada para o shader (do vídeo ou do Spout).</summary>
    public ID3D11ShaderResourceView? InputSrv => LyricsGpu?.Target?.Srv ?? Spout?.Srv ?? Srv;

    /// <summary>Pronta para desenhar? (vídeo precisa do primeiro quadro, Spout precisa do sender)</summary>
    public bool Ready => Kind != SourceKind.Video || ShownFrame != null || Spout?.Srv != null || LyricsGpu?.Drawn == true;

    public float Aspect => Spout is { Srv: not null } s ? (float)s.Width / s.Height
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
    public void Dispose()
    {
        Spout?.Dispose();
        LyricsGpu?.Dispose();
        Srv?.Dispose();
        Texture?.Dispose();
        if (Player is { } p) Task.Run(p.Dispose);
    }
}

/// <summary>Estado de uma layer do lado do motor.</summary>
sealed class LayerSlot
{
    public volatile float Opacity = 1f;
    public volatile int Blend;
    public volatile EffectRun[] Effects = [];
    public volatile int Transition;          // TransitionKind
    public volatile float TransitionSeconds;
    public volatile float Volume = 1f;
    /// <summary>Esta layer aparece no item "Telão" da tela de retorno (quando está tocando).</summary>
    public volatile bool ToStage;

    // troca de fonte pedida pela UI, adotada pelo render thread
    public readonly object Lock = new();
    public bool HasPending;
    public SourceKind? PendingKind;
    public VideoPlayer? PendingPlayer;
    public string? PendingSpout;
    public LyricsFeed? PendingLyrics;

    // estado do render thread (o mixer de áudio só lê as referências)
    public volatile LayerSource? Current;
    public volatile LayerSource? Outgoing;   // clip anterior, saindo durante a transição
    public double TransitionStart;
    public double TransitionLength;
    /// <summary>Progresso da transição 0..1 (1 = terminada). Lido pelo mixer de áudio.</summary>
    public volatile float TransitionProgress = 1f;
}
