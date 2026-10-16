using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Janela de controle: hospeda a UI React no WebView2 e faz a ponte entre a UI, o modelo
/// da composição e o motor de render.
/// </summary>
sealed partial class MainForm : Form
{
    const string UiHost = "app.vj";
    const string VideoFilter =
        "Vídeos e imagens|*.mov;*.mp4;*.avi;*.mkv;*.webm;*.m4v;*.wmv;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos os arquivos|*.*";

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer _playbackTimer = new() { Interval = 100 };
    Composition _comp = Composition.LoadOrDefault(Composition.DefaultPath);
    readonly VideoPlayer?[] _players;
    readonly ConcurrentDictionary<string, string?> _thumbs = new();
    Renderer? _renderer;

    // efeitos: biblioteca carregada da pasta Shaders/Effects (recarrega ao editar um .hlsl)
    readonly string _effectDir = EffectLibrary.FindDirectory();
    List<EffectDef> _effectDefs = [];
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<EffectInstance, EffectRun> _runs = [];
    readonly FileSystemWatcher? _effectWatcher;
    readonly System.Windows.Forms.Timer _effectReloadTimer = new() { Interval = 300 };
    readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 500 };

    public MainForm()
    {
        Text = "VJApp";
        ClientSize = new Size(1400, 900);
        Controls.Add(_web);
        _players = new VideoPlayer?[_comp.Layers.Count];
        _playbackTimer.Tick += (_, _) => SendPlayback();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _comp.Save(Composition.DefaultPath); };
        _effectReloadTimer.Tick += (_, _) => { _effectReloadTimer.Stop(); LoadEffects(); };

        if (Directory.Exists(_effectDir))
        {
            _effectWatcher = new FileSystemWatcher(_effectDir, "*.hlsl") { EnableRaisingEvents = true };
            FileSystemEventHandler restart = (_, _) => BeginInvoke(() => { _effectReloadTimer.Stop(); _effectReloadTimer.Start(); });
            _effectWatcher.Changed += restart;
            _effectWatcher.Created += restart;
            _effectWatcher.Deleted += restart;
            _effectWatcher.Renamed += (s, e) => restart(s, e);
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        var errors = new List<string>();
        _effectDefs = EffectLibrary.Load(_effectDir, errors);

        _renderer = new Renderer(_comp.Layers.Count, _effectDefs);
        _renderer.Stats += fps => Send(new { type = "stats", fps = Math.Round(fps, 1) });
        _renderer.Preview += OnPreview;
        _renderer.StagePreview += OnStagePreview;
        _renderer.Error += ex => Send(new { type = "error", message = "Motor de vídeo: " + ex.Message });
        _renderer.EffectError += msg => Send(new { type = "error", message = msg });
        for (int i = 0; i < _comp.Layers.Count; i++) ApplyLayerParams(i);
        _renderer.SetBpm(_comp.Bpm);
        RebuildEffectChains();
        ApplyMapping();
        StartAudio();
        ApplyStageWindow();

        await _web.EnsureCoreWebView2Async();
        var core = _web.CoreWebView2;
        core.WebMessageReceived += OnWebMessage;
        // links externos (ex.: endereço do controle remoto) abrem no navegador padrão
        core.NewWindowRequested += (_, ev) =>
        {
            ev.Handled = true;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ev.Uri) { UseShellExecute = true });
        };

        // VJ_UI_DEV=1 usa o servidor do Vite (hot reload); senão, a UI compilada em ui/
        if (Environment.GetEnvironmentVariable("VJ_UI_DEV") == "1")
        {
            core.Navigate("http://localhost:5173");
        }
        else
        {
            string dist = Path.Combine(AppContext.BaseDirectory, "ui");
            core.SetVirtualHostNameToFolderMapping(UiHost, dist, CoreWebView2HostResourceAccessKind.Allow);
            // fontes importadas (para a prévia das letras usar as mesmas fontes do telão)
            Directory.CreateDirectory(VJApp.Engine.FontStore.Dir);
            core.SetVirtualHostNameToFolderMapping("fonts.vj", VJApp.Engine.FontStore.Dir, CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate($"https://{UiHost}/index.html");
        }
        _playbackTimer.Start();
        StartAutosave();
        EnsureDesktopShortcut();
        RegisterProjectFileType();
        StartControl();
    }

    // ---------------- mensagens da UI ----------------

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = JsonNode.Parse(e.WebMessageAsJson);
        if (msg is null || _renderer is null) return;

        int L() => (int)msg["layer"]!;
        int C() => (int)msg["column"]!;

        switch ((string?)msg["type"])
        {
            case "hello":
                if (StartupProject is { } startup)
                {
                    StartupProject = null; // VJApp.exe "show.vjproj" (duplo clique no arquivo)
                    BeginInvoke(() => OpenProjectFile(startup));
                }
                Send(new
                {
                    type = "displays",
                    list = Screen.AllScreens.Select((s, i) => new
                    {
                        index = i,
                        name = s.DeviceName.Replace(@"\\.\", ""),
                        width = s.Bounds.Width,
                        height = s.Bounds.Height,
                        primary = s.Primary,
                    }),
                });
                SendShortcuts();
                SendEffectLibrary();
                SendComposition();
                SendMapping();
                SendProjectInfo();
                SendPresets();
                SendControlInfo();
                SendMaster();
                SendStreaming();
                SendTempo();
                SendLyricsLibrary();
                SendLyricsState();
                SendFonts();
                SendStage();
                SendAttacks();
                break;

            case "tap": Tap(); break;
            case var lyricsType when lyricsType?.StartsWith("lyrics") == true: OnLyricsMessage(lyricsType, msg); break;
            case var stageType when stageType?.StartsWith("stage") == true: OnStageMessage(stageType, msg); break;
            case var bibleType when bibleType?.StartsWith("bible") == true: OnBibleMessage(bibleType, msg); break;
            case var fontType when fontType?.StartsWith("fonts") == true: OnFontMessage(fontType, msg); break;
            case var scType when scType?.StartsWith("shortcuts") == true: OnShortcutsMessage(scType, msg); break;
            case var attackType when attackType?.StartsWith("attack") == true: OnAttackMessage(attackType, msg); break;
            case "setBpm": SetBpm((float)msg["bpm"]!); break;
            case "beatResync": _renderer.ResyncBeat(); break;
            case "setMasterVolume": SetMasterVolume((float)msg["volume"]!); break;

            case "learnStart": _midi?.StartLearn((string)msg["address"]!); break;
            case "learnCancel": _midi?.CancelLearn(); break;
            case "setStreaming": SetStreaming((string?)msg["spout"], (string?)msg["ndi"]); break;
            case "midiUnbind": _midi?.RemoveBinding((string)msg["address"]!); break;
            case "midiClear": _midi?.ClearBindings(); break;

            case "setMapping":
                SetMapping(msg["mapping"]!);
                break;

            case "projectNew": NewProject(); break;
            case "projectOpen": OpenProject(); break;
            case "projectSave": SaveProject(saveAs: false); break;
            case "projectSaveAs": SaveProject(saveAs: true); break;
            case "presetSave": SaveMappingPreset((string?)msg["name"]); break;
            case "presetLoad": LoadMappingPreset((string?)msg["name"]); break;
            case "presetDelete": DeleteMappingPreset((string?)msg["name"]); break;

            case "effectAdd":
            {
                var def = _effectDefs.FirstOrDefault(d => d.Id == (string?)msg["effect"]);
                if (def == null) break;
                Chain((int)msg["target"]!).Add(new EffectInstance
                {
                    Effect = def.Id,
                    Values = def.Params.ToDictionary(p => p.Id, p => p.Default),
                });
                EffectsChanged();
                break;
            }

            case "effectRemove":
            {
                var chain = Chain((int)msg["target"]!);
                int i = (int)msg["index"]!;
                if (i >= 0 && i < chain.Count) chain.RemoveAt(i);
                EffectsChanged();
                break;
            }

            case "effectMove":
            {
                var chain = Chain((int)msg["target"]!);
                int i = (int)msg["index"]!, j = i + (int)msg["direction"]!;
                if (i < 0 || j < 0 || i >= chain.Count || j >= chain.Count) break;
                (chain[i], chain[j]) = (chain[j], chain[i]);
                EffectsChanged();
                break;
            }

            case "effectToggle":
            {
                var inst = Chain((int)msg["target"]!)[(int)msg["index"]!];
                inst.Enabled = (bool)msg["enabled"]!;
                GetRun(inst).Enabled = inst.Enabled;
                ScheduleSave();
                break;
            }

            case "effectParam":
            {
                // atualiza só o valor (sem reconstruir a cadeia) para o slider ficar fluido
                var inst = Chain((int)msg["target"]!)[(int)msg["index"]!];
                string param = (string)msg["param"]!;
                float value = (float)msg["value"]!;
                inst.Values[param] = value;
                var def = _effectDefs.FirstOrDefault(d => d.Id == inst.Effect);
                int pi = def?.Params.ToList().FindIndex(p => p.Id == param) ?? -1;
                if (pi >= 0) GetRun(inst).Values[pi] = value;
                ScheduleSave();
                break;
            }

            case "setMaster":
                if (msg["brightness"] is JsonNode b) _renderer.Brightness = (float)b;
                if (msg["speed"] is JsonNode s) _renderer.Speed = (float)s;
                if (msg["testCard"] is JsonNode t) _renderer.ShowTestCard = (bool)t;
                break;

            case "browseClip":
                using (var dlg = new OpenFileDialog { Filter = VideoFilter, Title = "Escolher vídeo(s)", Multiselect = true })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK) PlaceFiles(L(), C(), dlg.FileNames);
                }
                break;

            case "dropFiles":
                // arquivos arrastados do Explorer chegam como objetos adicionais (CoreWebView2File)
                var paths = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToArray() ?? [];
                PlaceFiles(L(), C(), paths);
                break;

            case "listLiveSources":
                Task.Run(() =>
                {
                    var cameras = VideoPlayer.ListCameras();
                    var spout = SpoutReceiver.ListSenders().Where(n => n != _streamActive.Spout).ToList();
                    Send(new { type = "liveSources", cameras, spout });
                });
                break;

            case "setLiveClip":
                _comp.Layers[L()].Clips[C()] = Clip.Live((string?)msg["kind"] == "spout" ? ClipKind.Spout : ClipKind.Camera, (string)msg["name"]!);
                if (_comp.ActiveHere(_comp.Layers[L()]) == C()) ClearLayer(L());
                CompositionChanged();
                break;

            case "setGenerator":
                _comp.Layers[L()].Clips[C()] = (string?)msg["kind"] switch
                {
                    "testCard" => Clip.Generator(ClipKind.TestCard),
                    "lyrics" => new Clip { Kind = ClipKind.Lyrics, Name = "Letras" },
                    _ => Clip.Generator(ClipKind.Plasma),
                };
                CompositionChanged();
                break;

            case "moveClip":
            {
                int fl = (int)msg["fromLayer"]!, fc = (int)msg["fromColumn"]!, tl = L(), tc = C();
                if (fl == tl && fc == tc) break;
                var a = _comp.Layers[fl].Clips[fc];
                _comp.Layers[fl].Clips[fc] = _comp.Layers[tl].Clips[tc];
                _comp.Layers[tl].Clips[tc] = a;
                if (_comp.ActiveHere(_comp.Layers[fl]) == fc) ClearLayer(fl);
                if (_comp.ActiveHere(_comp.Layers[tl]) == tc) ClearLayer(tl);
                CompositionChanged();
                break;
            }

            case "removeClip":
                if (_comp.ActiveHere(_comp.Layers[L()]) == C()) ClearLayer(L());
                _comp.Layers[L()].Clips[C()] = null;
                CompositionChanged();
                break;

            case "trigger":
                Trigger(L(), C());
                SendComposition();
                break;

            case "triggerColumn":
                for (int i = 0; i < _comp.Layers.Count; i++)
                {
                    if (_comp.Layers[i].Clips[C()] != null) Trigger(i, C());
                    else ClearLayer(i);
                }
                SendComposition();
                break;

            case "clearLayer":
                ClearLayer(L());
                SendComposition();
                break;

            case "clearAll":
                for (int i = 0; i < _comp.Layers.Count; i++) ClearLayer(i);
                SendComposition();
                break;

            case var gridType when gridType is "columnAdd" or "columnRemove" or "deckAdd" or "deckSelect" or "deckRename" or "deckRemove" or "deckMove":
                OnGridMessage(gridType, msg);
                break;

            case "layerParams":
            {
                var layer = _comp.Layers[L()];
                if (msg["opacity"] is JsonNode o) layer.Opacity = (float)o;
                if (msg["name"] is JsonNode nm && ((string?)nm)?.Trim() is { Length: > 0 } newName) { layer.Name = newName; SendComposition(withThumbs: false); }
                if (msg["blend"] is JsonNode bl && Enum.TryParse<BlendMode>((string?)bl, true, out var mode)) layer.Blend = mode;
                if (msg["transition"] is JsonNode tr && Enum.TryParse<TransitionType>((string?)tr, true, out var tt)) layer.Transition = tt;
                if (msg["transitionSeconds"] is JsonNode ts) layer.TransitionSeconds = Math.Clamp((float)ts, 0, 10);
                if (msg["volume"] is JsonNode vo) layer.Volume = Math.Clamp((float)vo, 0, 1.5f);
                if (msg["sendToStage"] is JsonNode st) { layer.SendToStage = (bool)st; SendStage(); }
                ApplyLayerParams(L());
                ScheduleSave();
                BroadcastRemoteState();
                break;
            }

            case "transport":
                var player = _players[L()];
                switch ((string?)msg["action"])
                {
                    case "play": player?.Play(); break;
                    case "pause": player?.Pause(); break;
                }
                SendPlayback();
                break;

            case "seek":
                _players[L()]?.Seek((double)(msg["position"] ?? 0));
                break;
        }
    }

    // ---------------- ações ----------------

    /// <summary>Coloca um ou mais arquivos na grade a partir da célula indicada (seguindo pela linha).</summary>
    void PlaceFiles(int layer, int column, IEnumerable<string> paths)
    {
        int c = column;
        foreach (var path in paths)
        {
            if (c >= _comp.Columns) break;
            _comp.Layers[layer].Clips[c] = Clip.FromFile(path);
            if (_comp.Layers[layer].Active == c) ClearLayer(layer);
            c++;
        }
        CompositionChanged();
    }

    void Trigger(int layer, int column)
    {
        var clip = _comp.Layers[layer].Clips[column];
        if (clip == null) return;

        switch (clip.Kind)
        {
            case ClipKind.Video:
                VideoPlayer player;
                try
                {
                    player = new VideoPlayer(clip.Path!);
                }
                catch (Exception ex)
                {
                    Send(new { type = "error", message = $"Não consegui abrir {clip.Name}: {ex.Message}" });
                    return;
                }
                _players[layer] = player;
                _renderer!.SetLayerSource(layer, SourceKind.Video, player); // o player anterior é liberado pelo motor
                break;
            case ClipKind.Camera:
                // abrir a câmera pode levar um segundo: abre fora da thread da UI
                string device = clip.Path!;
                _comp.Layers[layer].Active = column;
                _comp.Layers[layer].ActiveDeck = _comp.Current.Id;
                _comp.Layers[layer].PlayingClip = clip;
                Task.Run(async () =>
                {
                    // tenta de novo a cada 2 s enquanto o clip estiver ativo
                    // (ex.: a Câmera Virtual do OBS só entrega imagem depois de "Iniciar câmera virtual")
                    bool warned = false;
                    while (true)
                    {
                        bool stillActive = (bool)Invoke(() => _comp.Layers[layer].PlayingClip == clip);
                        if (!stillActive) return;
                        try
                        {
                            var cam = new VideoPlayer(device, liveDevice: true);
                            BeginInvoke(() =>
                            {
                                if (_comp.Layers[layer].PlayingClip != clip) { cam.Dispose(); return; } // trocou de clip no meio
                                _players[layer] = cam;
                                _renderer!.SetLayerSource(layer, SourceKind.Video, cam);
                            });
                            return;
                        }
                        catch (Exception ex)
                        {
                            if (!warned)
                            {
                                string hint = device.Contains("OBS", StringComparison.OrdinalIgnoreCase)
                                    ? " No OBS, clique em \"Iniciar câmera virtual\"; a imagem aparece sozinha."
                                    : " Confira se a câmera não está em uso por outro programa; vou continuar tentando.";
                                Send(new { type = "error", message = $"Câmera {device} ainda não está entregando imagem.{hint} ({ex.Message})" });
                                warned = true;
                            }
                            await Task.Delay(2000);
                        }
                    }
                });
                return;
            case ClipKind.Lyrics:
                _players[layer] = null;
                _renderer!.SetLayerSource(layer, SourceKind.Video, null, null, _lyricsFeed);
                break;
            case ClipKind.Spout:
                _players[layer] = null;
                _renderer!.SetLayerSource(layer, SourceKind.Video, null, clip.Path);
                break;
            case ClipKind.Plasma:
                _players[layer] = null;
                _renderer!.SetLayerSource(layer, SourceKind.Plasma);
                break;
            case ClipKind.TestCard:
                _players[layer] = null;
                _renderer!.SetLayerSource(layer, SourceKind.TestCard);
                break;
        }
        _comp.Layers[layer].Active = column;
        _comp.Layers[layer].ActiveDeck = _comp.Current.Id;
        _comp.Layers[layer].PlayingClip = clip;
    }

    void ClearLayer(int layer)
    {
        _players[layer] = null;
        _renderer?.SetLayerSource(layer, null);
        _comp.Layers[layer].Active = -1;
        _comp.Layers[layer].ActiveDeck = null;
        _comp.Layers[layer].PlayingClip = null;
    }

    void ApplyLayerParams(int layer)
    {
        var slot = _renderer!.Layers[layer];
        slot.Opacity = _comp.Layers[layer].Opacity;
        slot.Blend = (int)_comp.Layers[layer].Blend;
        slot.Transition = (int)_comp.Layers[layer].Transition;
        slot.TransitionSeconds = _comp.Layers[layer].TransitionSeconds;
        slot.Volume = _comp.Layers[layer].Volume;
        slot.ToStage = _comp.Layers[layer].SendToStage;
    }

    void CompositionChanged()
    {
        _comp.Save(Composition.DefaultPath);
        SendComposition();
    }

    // ---------------- efeitos ----------------

    /// <summary>Cadeia de efeitos de uma layer, ou do master quando target &lt; 0.</summary>
    List<EffectInstance> Chain(int target) => target < 0 ? _comp.MasterEffects : _comp.Layers[target].Effects;

    void LoadEffects()
    {
        var errors = new List<string>();
        _effectDefs = EffectLibrary.Load(_effectDir, errors);
        foreach (var err in errors) Send(new { type = "error", message = "Efeito " + err });
        _renderer?.ReloadEffects(_effectDefs);
        RebuildEffectChains(resync: true);
        SendEffectLibrary();
    }

    /// <summary>Instância do motor ligada a uma instância do modelo (criada na primeira vez).</summary>
    EffectRun GetRun(EffectInstance inst) => _runs.GetValue(inst, i =>
    {
        var run = new EffectRun(i.Effect, i);
        SyncRun(i, run);
        return run;
    });

    /// <summary>Copia os valores do modelo para a instância do motor, na ordem dos parâmetros.</summary>
    void SyncRun(EffectInstance inst, EffectRun run)
    {
        run.Enabled = inst.Enabled;
        var def = _effectDefs.FirstOrDefault(d => d.Id == inst.Effect);
        if (def == null) return;
        for (int i = 0; i < def.Params.Count; i++)
            run.Values[i] = inst.Values.TryGetValue(def.Params[i].Id, out var v) ? v : def.Params[i].Default;
    }

    void RebuildEffectChains(bool resync = false)
    {
        if (_renderer == null) return;
        EffectRun[] Build(List<EffectInstance> chain) => chain.Select(inst =>
        {
            var run = GetRun(inst);
            if (resync) SyncRun(inst, run);
            return run;
        }).ToArray();

        for (int i = 0; i < _comp.Layers.Count; i++) _renderer.Layers[i].Effects = Build(_comp.Layers[i].Effects);
        _renderer.MasterEffects = Build(_comp.MasterEffects);
    }

    void EffectsChanged()
    {
        RebuildEffectChains();
        CompositionChanged();
    }

    void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void SendEffectLibrary()
    {
        Send(new
        {
            type = "effects",
            list = _effectDefs.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                feedback = d.Feedback,
                @params = d.Params.Select(p => new { id = p.Id, label = p.Label, min = p.Min, max = p.Max, @default = p.Default, step = p.Step }),
            }),
        });
    }

    // ---------------- envio para a UI ----------------

    void SendComposition(bool withThumbs = true)
    {
        Send(new
        {
            type = "composition",
            columns = _comp.Columns,
            layers = _comp.Layers.Select(l => new
            {
                name = l.Name,
                opacity = l.Opacity,
                blend = l.Blend,
                active = _comp.ActiveHere(l),
                playing = l.PlayingClip,
                playingDeck = l.ActiveDeck,
                clips = l.Clips,
                effects = l.Effects,
                transition = l.Transition,
                transitionSeconds = l.TransitionSeconds,
                volume = l.Volume,
                sendToStage = l.SendToStage,
                hasAudio = _players[_comp.Layers.IndexOf(l)]?.HasAudio ?? false,
            }),
            masterEffects = _comp.MasterEffects,
            decks = _comp.Decks.Select(d => new { id = d.Id, name = d.Name }),
            currentDeck = _comp.CurrentDeck,
        });
        BroadcastRemoteState();
        SendStage(); // a imagem do retorno depende do que está tocando

        // miniaturas que ainda não foram geradas (em segundo plano)
        foreach (var path in _comp.Layers.SelectMany(l => l.Clips).Where(c => c?.Kind == ClipKind.Video).Select(c => c!.Path!).Distinct())
        {
            if (_thumbs.TryGetValue(path, out var cached))
            {
                if (cached != null && withThumbs) Send(new { type = "thumb", path, data = cached });
                continue;
            }
            _thumbs[path] = null;
            Task.Run(() =>
            {
                var data = Thumbnailer.Create(path);
                _thumbs[path] = data;
                if (data != null) Send(new { type = "thumb", path, data });
            });
        }
    }

    void SendPlayback()
    {
        Send(new
        {
            type = "playback",
            beat = _renderer?.Beat ?? 0,
            bpm = _renderer?.Bpm ?? 120,
            layers = _players.Select(p => p == null ? null : new { position = p.Position, duration = p.Duration, playing = p.Playing }),
        });
    }

    void Send(object payload)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Send(payload)); return; }
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _playbackTimer.Stop();
        StopControl();
        _audio?.Dispose();
        _renderer?.Dispose();
        CloseOutputWindows();
        CloseStageWindow();
        base.OnFormClosed(e);
    }
}
