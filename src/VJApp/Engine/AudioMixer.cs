using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VJApp.Engine;

/// <summary>
/// Mixer de áudio dos clips: soma o som de cada layer (volume da layer × master) e toca na saída
/// padrão do Windows (WASAPI, ~60 ms de latência). Durante uma transição, o clip que sai abaixa
/// enquanto o que entra sobe.
/// </summary>
sealed class AudioMixer : ISampleProvider, IDisposable
{
    readonly Renderer _renderer;
    readonly WasapiOut _out;

    public volatile float MasterVolume = 1f;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(VideoPlayer.AudioRate, 2);
    public string DeviceName { get; }

    public AudioMixer(Renderer renderer)
    {
        _renderer = renderer;
        _out = new WasapiOut(AudioClientShareMode.Shared, true, 60);
        _out.Init(this);
        _out.Play();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            DeviceName = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).FriendlyName;
        }
        catch { DeviceName = "saída padrão"; }
    }

    public int Read(Span<float> span)
    {
        span.Clear();
        float master = MasterVolume;
        _mixed.Clear();
        foreach (var slot in _renderer.Layers)
        {
            float vol = slot.Volume * slot.ClipVolume * master;
            if (vol <= 0) continue;
            float p = slot.TransitionProgress;
            var outgoing = slot.Outgoing;
            float e = p * p * (3 - 2 * p); // mesma curva suave da imagem
            Mix(slot.Current?.Player, span, vol * (slot.TransitionActive ? e : 1));
            float m = slot.OutgoingMix;
            Mix(outgoing?.Player, span, vol * (1 - e) * m);
            Mix(slot.OutgoingUnder?.Player, span, vol * (1 - e) * (1 - m));
        }
        foreach (var (player, gain) in _mixed) player.MixAudio(span, gain); // sem som também consome, para não atrasar
        // limitador simples para não estourar quando várias layers somam
        for (int i = 0; i < span.Length; i++)
        {
            float s = span[i];
            if (s > 1 || s < -1) span[i] = MathF.Tanh(s);
        }
        return span.Length;
    }

    /// <summary>Players já somados neste pedaço de som (o mesmo vídeo em duas layers toca o som uma vez só).</summary>
    readonly Dictionary<VideoPlayer, float> _mixed = [];

    /// <summary>Anota o volume do player; se ele aparece em mais de uma layer, vale o maior (soma uma vez só no fim).</summary>
    void Mix(VideoPlayer? player, Span<float> span, float gain)
    {
        if (player == null) return;
        _mixed[player] = Math.Max(_mixed.GetValueOrDefault(player), gain);
    }

    public void Dispose()
    {
        _out.Stop();
        _out.Dispose();
    }
}
