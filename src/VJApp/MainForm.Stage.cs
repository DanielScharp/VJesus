using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>Tela de retorno: janela no monitor dos músicos e o estado que ela mostra.</summary>
sealed partial class MainForm
{
    StageConfig _stage = StageConfig.LoadOrDefault(StageConfig.DefaultPath);
    readonly StageTimer _stageTimer = new();
    string? _stageAlert;
    string? _stageMessage;
    bool _stageMirror;
    StageWindow? _stageWindow;
    (int Display, bool Fullscreen)? _stagePlacement;
    System.Windows.Forms.Timer? _stageSaveTimer;

    void OnStageMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "stageConfig":
                if (msg["config"].Deserialize<StageConfig>(JsonRead) is { } cfg)
                {
                    if (cfg.Elements.Count == 0) cfg.Elements = StageConfig.DefaultElements(); // "Layout padrão"
                    foreach (var (k, v) in StageConfig.DefaultElements()) cfg.Elements.TryAdd(k, v);
                    _stage = cfg;
                    ApplyStageWindow();
                    _stageSaveTimer ??= NewSaveTimer(() => _stage.Save(StageConfig.DefaultPath));
                    _stageSaveTimer.Stop();
                    _stageSaveTimer.Start();
                    SendStage();
                }
                break;
            case "stageTimer":
                StageTimerAction((string?)msg["action"], (string?)msg["mode"], (double?)msg["durationSeconds"]);
                break;
            case "stageAlert":
                _stageAlert = string.IsNullOrWhiteSpace((string?)msg["text"]) ? null : ((string)msg["text"]!).Trim();
                SendStage();
                break;
            case "stageMessage":
                _stageMessage = string.IsNullOrWhiteSpace((string?)msg["text"]) ? null : ((string)msg["text"]!).Trim();
                SendStage();
                break;
            case "stageMirror":
                SetStageMirror((bool?)msg["on"] ?? !_stageMirror);
                break;
            case "stageHello":
                SendStage();
                break;
        }
    }

    System.Windows.Forms.Timer NewSaveTimer(Action save)
    {
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (_, _) => { t.Stop(); save(); };
        return t;
    }

    void StageTimerAction(string? action, string? mode, double? duration)
    {
        double now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var t = _stageTimer;
        if (mode is "stopwatch" or "countdown" && mode != t.Mode) { t.Mode = mode; t.Running = false; t.Accumulated = 0; }
        if (duration is > 0) t.DurationSeconds = duration.Value;
        switch (action)
        {
            case "start":
                if (!t.Running) { t.Running = true; t.StartedAt = now; }
                break;
            case "pause":
                if (t.Running) { t.Accumulated += (now - t.StartedAt) / 1000; t.Running = false; }
                break;
            case "toggle":
                StageTimerAction(t.Running ? "pause" : "start", null, null);
                return;
            case "reset":
                t.Running = false;
                t.Accumulated = 0;
                break;
        }
        SendStage();
    }

    void SetStageMirror(bool on)
    {
        _stageMirror = on;
        if (_renderer != null) _renderer.StageMirror = on;
        SendStage();
    }

    /// <summary>Abre, fecha ou reposiciona a janela de retorno conforme a configuração.</summary>
    void ApplyStageWindow()
    {
        // a imagem do retorno só é montada se o item "Telão" estiver visível no layout
        if (_renderer != null)
            _renderer.StageFeedWanted = _stage.Elements.TryGetValue("program", out var prog) && prog.Visible;
        if (!_stage.Enabled)
        {
            _stageWindow?.Hide();
            _stagePlacement = null;
            return;
        }
        if (_stageWindow == null)
        {
            _stageWindow = new StageWindow(() => BeginInvoke(SendStage));
            _stageWindow.Location = new Point(160, 160);
        }
        var place = (_stage.Display, _stage.Fullscreen);
        if (_stagePlacement != place || !_stageWindow.Visible)
        {
            _stageWindow.PlaceOn(_stage.Display, _stage.Fullscreen);
            _stagePlacement = place;
        }
    }

    /// <summary>Estado completo da tela de retorno, para a janela de retorno e para a prévia na aba Retorno.</summary>
    void SendStage()
    {
        // slide atual e o próximo da música no telão
        string? current = _liveText, next = null, title = null, label = null;
        if (_liveSongId != null && _lyrics.Songs.FirstOrDefault(s => s.Id == _liveSongId) is { } song)
        {
            var slides = LyricsLibrary.Slides(song.Text);
            title = song.Title;
            if (_liveSlide >= 0 && _liveSlide < slides.Count) label = slides[_liveSlide].Label;
            if (_liveSlide + 1 < slides.Count) next = slides[_liveSlide + 1].Text;
        }
        var payload = new
        {
            type = "stage",
            config = _stage,
            live = new { current, next, title, label, blank = _blank },
            timer = _stageTimer,
            alert = _stageAlert,
            message = _stageMessage,
            // a imagem do telão aparece no retorno quando espelhando ou quando uma layer marcada está tocando
            program = new
            {
                active = _stageMirror || _comp.Layers.Any(l => l.SendToStage && l.Active >= 0 && l.Opacity > 0),
                mirror = _stageMirror,
                layers = _comp.Layers.Where(l => l.SendToStage).Select(l => l.Name),
            },
        };
        Send(payload);
        _stageWindow?.Post(JsonSerializer.Serialize(payload, Json));
    }

    void CloseStageWindow()
    {
        if (_stageWindow == null) return;
        _stageWindow.AllowClose = true;
        _stageWindow.Close();
    }
}
