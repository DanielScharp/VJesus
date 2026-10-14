using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VJApp.Control;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Controle externo: OSC (UDP 7000), MIDI (com MIDI learn) e controle remoto pelo celular (http :8080).
/// Tudo passa por endereços no estilo do Resolume (veja ApplyControl), com valores 0..1.
/// </summary>
sealed partial class MainForm
{
    const int OscPort = 7000, RemotePort = 8080;

    OscServer? _osc;
    MidiInput? _midi;
    RemoteServer? _remote;
    string? _controlError;
    System.Windows.Forms.Timer? _syncTimer;

    // ---------------- Spout / NDI ----------------

    sealed class StreamSettings
    {
        public string? Spout { get; set; }
        public string? Ndi { get; set; }
    }

    static string StreamSettingsPath => Path.Combine(AppDataDir, "streaming.json");
    StreamSettings _stream = new();
    (string? Spout, string? Ndi) _streamActive;

    void StartStreaming()
    {
        try
        {
            if (File.Exists(StreamSettingsPath))
                _stream = JsonSerializer.Deserialize<StreamSettings>(File.ReadAllText(StreamSettingsPath)) ?? new();
        }
        catch { }
        _renderer!.StreamState += (spout, ndi) => BeginInvoke(() => { _streamActive = (spout, ndi); SendStreaming(); });
        _renderer.StreamError += (what, msg) => BeginInvoke(() =>
        {
            if (what == "NDI") _stream.Ndi = null; else _stream.Spout = null;
            SaveStreamSettings();
            Send(new { type = "error", message = $"{what}: {msg}" });
        });
        _renderer.SetStreaming(_stream.Spout, _stream.Ndi);
    }

    void SetStreaming(string? spout, string? ndi)
    {
        _stream = new StreamSettings { Spout = spout, Ndi = ndi };
        SaveStreamSettings();
        _renderer?.SetStreaming(spout, ndi);
    }

    void SaveStreamSettings()
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(StreamSettingsPath, JsonSerializer.Serialize(_stream));
    }

    void SendStreaming() => Send(new
    {
        type = "streaming",
        spout = _streamActive.Spout,
        ndi = _streamActive.Ndi,
        ndiAvailable = Engine.NdiSender.FindRuntime() != null,
    });

    async void StartControl()
    {
        StartStreaming();
        var problems = new List<string>();
        try { _osc = new OscServer(OscPort, (a, v) => BeginInvoke(() => ApplyControl(a, v))); }
        catch (Exception ex) { problems.Add($"OSC na porta {OscPort}: {ex.Message}"); }

        try
        {
            _midi = new MidiInput((a, v) => BeginInvoke(() => ApplyControl(a, v)));
            _midi.Changed += () => BeginInvoke(SendControlInfo);
        }
        catch (Exception ex) { problems.Add($"MIDI: {ex.Message}"); }

        try
        {
            _remote = await RemoteServer.StartAsync(RemotePort,
                json => BeginInvoke(() => OnRemoteMessage(json)),
                () => (string)Invoke(() => RemoteState()));
        }
        catch (Exception ex) { problems.Add($"Controle remoto na porta {RemotePort}: {ex.Message}"); }

        _controlError = problems.Count > 0 ? string.Join(" · ", problems) : null;
        SendControlInfo();
    }

    void StopControl()
    {
        _osc?.Dispose();
        _midi?.Dispose();
        if (_remote != null) _ = _remote.DisposeAsync().AsTask();
    }

    // ---------------- endereços ----------------

    static readonly Regex RxClip = new(@"^/composition/layers/(\d+)/clips/(\d+)/connect$");
    static readonly Regex RxColumn = new(@"^/composition/columns/(\d+)/connect$");
    static readonly Regex RxLayerClear = new(@"^/composition/layers/(\d+)/clear$");
    static readonly Regex RxOpacity = new(@"^/composition/layers/(\d+)/video/opacity$");
    static readonly Regex RxAttack = new(@"^/attack/(\d+)$");
    static readonly Regex RxVolume = new(@"^/composition/layers/(\d+)/audio/volume$");
    static readonly Regex RxLayerFx = new(@"^/composition/layers/(\d+)/effects/(\d+)/(\w+)$");
    static readonly Regex RxMasterFx = new(@"^/composition/effects/(\d+)/(\w+)$");

    /// <summary>
    /// Aplica um comando de controle. Índices começam em 1, como no Resolume. Valores 0..1;
    /// botões disparam com valor ≥ 0.5.
    /// </summary>
    void ApplyControl(string address, float value)
    {
        if (_renderer == null) return;
        bool press = value >= 0.5f;
        Match m;

        if ((m = RxClip.Match(address)).Success)
        {
            int l = I(m, 1), c = I(m, 2);
            if (press && l < _comp.Layers.Count && c < _comp.Columns) Trigger(l, c);
        }
        else if ((m = RxColumn.Match(address)).Success)
        {
            int c = I(m, 1);
            if (press && c < _comp.Columns)
                for (int i = 0; i < _comp.Layers.Count; i++)
                    if (_comp.Layers[i].Clips[c] != null) Trigger(i, c); else ClearLayer(i);
        }
        else if ((m = RxLayerClear.Match(address)).Success)
        {
            if (press && I(m, 1) < _comp.Layers.Count) ClearLayer(I(m, 1));
        }
        else if ((m = RxOpacity.Match(address)).Success)
        {
            int l = I(m, 1);
            if (l >= _comp.Layers.Count) return;
            _comp.Layers[l].Opacity = Math.Clamp(value, 0, 1);
            ApplyLayerParams(l);
            ScheduleSave();
        }
        else if ((m = RxAttack.Match(address)).Success)
        {
            AttackByIndex(I(m, 1), press);
            return;
        }
        else if ((m = RxVolume.Match(address)).Success)
        {
            int l = I(m, 1);
            if (l >= _comp.Layers.Count) return;
            _comp.Layers[l].Volume = Math.Clamp(value, 0, 1.5f);
            ApplyLayerParams(l);
            ScheduleSave();
        }
        else if ((m = RxLayerFx.Match(address)).Success)
        {
            int l = I(m, 1);
            if (l < _comp.Layers.Count) SetEffectParamNormalized(_comp.Layers[l].Effects, I(m, 2), m.Groups[3].Value, value);
        }
        else if ((m = RxMasterFx.Match(address)).Success)
        {
            SetEffectParamNormalized(_comp.MasterEffects, I(m, 1), m.Groups[2].Value, value);
        }
        else switch (address)
        {
            case "/composition/video/brightness": _renderer.Brightness = Math.Max(0, value); break;
            case "/composition/speed": _renderer.Speed = Math.Max(0, value) * 4; break;
            case "/composition/testcard": _renderer.ShowTestCard = press; break;
            case "/stage/timer/start": if (press) StageTimerAction("start", null, null); break;
            case "/stage/timer/pause": if (press) StageTimerAction("pause", null, null); break;
            case "/stage/timer/toggle": if (press) StageTimerAction("toggle", null, null); break;
            case "/stage/timer/reset": if (press) StageTimerAction("reset", null, null); break;
            case "/stage/mirror": SetStageMirror(press); break;
            case "/stage/alert/clear": if (press) { _stageAlert = null; SendStage(); } break;
            case "/lyrics/clear": if (press) ClearLyrics(); break;
            case "/lyrics/next": if (press) StepSlide(1); break;
            case "/lyrics/prev": if (press) StepSlide(-1); break;
            case "/lyrics/blank": _blank = press; UpdateLyricsOutput(); break;
            case "/composition/tempocontroller/tempotap": if (press) Tap(); break;
            case "/composition/tempocontroller/resync": if (press) _renderer.ResyncBeat(); break;
            case "/composition/tempocontroller/tempo": SetBpm(value > 1 ? value : 20 + value * 280); break; // 0..1 → 20..300, ou BPM direto
            case "/composition/audio/volume": SetMasterVolume(value); break;
            case "/composition/clear": for (int i = 0; i < _comp.Layers.Count; i++) ClearLayer(i); break;
            default: return; // endereço desconhecido
        }
        ScheduleSync();
    }

    static int I(Match m, int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture) - 1;

    /// <summary>Muda um parâmetro de efeito a partir de um valor 0..1 (convertido para mín..máx do parâmetro).</summary>
    void SetEffectParamNormalized(List<EffectInstance> chain, int index, string param, float value)
    {
        if (index < 0 || index >= chain.Count) return;
        var inst = chain[index];
        if (param == "bypassed") { inst.Enabled = value < 0.5f; GetRun(inst).Enabled = inst.Enabled; ScheduleSave(); return; }

        var def = _effectDefs.FirstOrDefault(d => d.Id == inst.Effect);
        int pi = def?.Params.ToList().FindIndex(p => p.Id == param) ?? -1;
        if (pi < 0) return;
        var p = def!.Params[pi];
        float v = p.Min + Math.Clamp(value, 0, 1) * (p.Max - p.Min);
        if (p.Step > 0) v = MathF.Round((v - p.Min) / p.Step) * p.Step + p.Min;
        inst.Values[param] = v;
        GetRun(inst).Values[pi] = v;
        ScheduleSave();
    }

    // ---------------- sincronização com a UI e os remotos ----------------

    /// <summary>Junta várias mudanças (ex.: girar um knob MIDI) numa atualização a cada 50 ms.</summary>
    void ScheduleSync()
    {
        if (_syncTimer == null)
        {
            _syncTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _syncTimer.Tick += (_, _) =>
            {
                _syncTimer.Stop();
                SendComposition(withThumbs: false);
                SendMaster();
            };
        }
        if (!_syncTimer.Enabled) _syncTimer.Start();
    }

    void SendMaster()
    {
        if (_renderer == null) return;
        Send(new { type = "master", brightness = _renderer.Brightness, speed = _renderer.Speed, testCard = _renderer.ShowTestCard });
    }

    void SendControlInfo()
    {
        Send(new
        {
            type = "control",
            oscPort = _osc?.Port,
            remoteUrl = _remote?.Url,
            midiDevices = _midi?.DeviceNames ?? [],
            midiBindings = _midi?.Bindings.Select(b => new { address = b.Address, label = b.Label, device = b.Device }) ?? [],
            learning = _midi?.Learning,
            error = _controlError,
        });
    }

    /// <summary>Estado resumido para o controle remoto.</summary>
    string RemoteState() => JsonSerializer.Serialize(new
    {
        type = "state",
        columns = _comp.Columns,
        brightness = _renderer?.Brightness ?? 1,
        attacks = _attackLib.Attacks.Select(a => a.Name),
        layers = _comp.Layers.Select(l => new
        {
            name = l.Name,
            active = l.Active,
            opacity = l.Opacity,
            clips = l.Clips.Select(c => c?.Name),
        }),
    });

    void BroadcastRemoteState() => _remote?.Broadcast(RemoteState());

    void OnRemoteMessage(string json)
    {
        try
        {
            var msg = JsonNode.Parse(json);
            if ((string?)msg?["address"] is { } address)
                ApplyControl(address, (float?)msg["value"] ?? 1f);
        }
        catch { /* mensagem inválida */ }
    }
}
