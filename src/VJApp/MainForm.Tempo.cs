using System.Diagnostics;
using VJApp.Engine;

namespace VJApp;

/// <summary>BPM (tap tempo) e áudio dos clips.</summary>
sealed partial class MainForm
{
    readonly Stopwatch _tapClock = Stopwatch.StartNew();
    readonly List<double> _taps = [];
    AudioMixer? _audio;
    string? _audioError;

    void StartAudio()
    {
        try
        {
            _audio = new AudioMixer(_renderer!) { MasterVolume = _comp.MasterVolume };
        }
        catch (Exception ex)
        {
            _audioError = "Áudio desligado: " + ex.Message;
            Send(new { type = "error", message = _audioError });
        }
    }

    /// <summary>
    /// Tap tempo: cada toque realinha a batida; dois ou mais toques seguidos (até 2 s entre eles)
    /// definem o BPM pela média dos últimos intervalos.
    /// </summary>
    void Tap()
    {
        double now = _tapClock.Elapsed.TotalSeconds;
        if (_taps.Count > 0 && now - _taps[^1] > 2) _taps.Clear();
        _taps.Add(now);
        if (_taps.Count > 5) _taps.RemoveAt(0);
        if (_taps.Count >= 2)
        {
            double avg = (_taps[^1] - _taps[0]) / (_taps.Count - 1);
            SetBpm((float)Math.Round(60 / avg, 1));
        }
        _renderer?.ResyncBeat();
    }

    void SetBpm(float bpm)
    {
        _renderer?.SetBpm(bpm);
        _comp.Bpm = _renderer?.Bpm ?? bpm;
        ScheduleSave();
        SendTempo();
    }

    void SetMasterVolume(float volume)
    {
        _comp.MasterVolume = Math.Clamp(volume, 0, 1.5f);
        if (_audio != null) _audio.MasterVolume = _comp.MasterVolume;
        ScheduleSave();
    }

    void SendTempo() => Send(new
    {
        type = "tempo",
        bpm = _renderer?.Bpm ?? _comp.Bpm,
        masterVolume = _comp.MasterVolume,
        audioDevice = _audio?.DeviceName,
        audioError = _audioError,
    });
}
