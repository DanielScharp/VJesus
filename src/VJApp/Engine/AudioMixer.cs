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
        foreach (var slot in _renderer.Layers)
        {
            float vol = slot.Volume * master;
            if (vol <= 0) continue;
            float p = slot.TransitionProgress;
            var outgoing = slot.Outgoing;
            slot.Current?.Player?.MixAudio(span, vol * (outgoing != null ? p : 1));
            outgoing?.Player?.MixAudio(span, vol * (1 - p));
        }
        // limitador simples para não estourar quando várias layers somam
        for (int i = 0; i < span.Length; i++)
        {
            float s = span[i];
            if (s > 1 || s < -1) span[i] = MathF.Tanh(s);
        }
        return span.Length;
    }

    public void Dispose()
    {
        _out.Stop();
        _out.Dispose();
    }
}
