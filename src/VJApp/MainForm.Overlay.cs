using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Contagem regressiva e avisos no telão: desenhados por cima de toda a composição, sem precisar de
/// clip na grade e sem atrapalhar a letra. A contagem é o mesmo temporizador da tela de retorno.
/// </summary>
sealed partial class MainForm
{
    readonly LyricsFeed _overlayFeed = new();
    readonly System.Windows.Forms.Timer _overlayTimer = new() { Interval = 200 };
    ServiceLibrary _service = ServiceLibrary.LoadOrDefault(ServiceLibrary.DefaultPath);

    string _overlayMode = "off"; // off, timer, notices (em sequência), notice (um só)
    string? _overlayNoticeId;    // aviso da lista no ar
    string? _overlayText;        // aviso avulso (vindo do roteiro)
    DateTime _overlayNextNotice;
    string? _overlayShown;
    bool _overlayDirty;

    void OnOverlayMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "overlaySave":
                if (msg["settings"].Deserialize<OverlaySettings>(JsonRead) is { } settings)
                {
                    _service.Overlay = settings;
                    SaveService();
                    _overlayDirty = true; // redesenha com o estilo novo
                    ApplyOverlayTargets();
                    UpdateOverlay();
                    SendOverlay();
                }
                break;
            case "overlayTimer":
                SetOverlay((bool?)msg["on"] == true ? "timer" : "off");
                break;
            case "overlayNotices":
                SetOverlay((bool?)msg["on"] == true ? "notices" : "off");
                break;
            case "overlayNotice":
                SetOverlay("notice", (string?)msg["id"]);
                break;
        }
    }

    void SetOverlay(string mode, string? noticeId = null, string? text = null)
    {
        _overlayMode = mode;
        _overlayNoticeId = noticeId;
        _overlayText = text;
        if (mode == "notices")
        {
            _overlayNoticeId = _service.Overlay.Notices.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.Text))?.Id;
            _overlayNextNotice = DateTime.Now.AddSeconds(Math.Max(1, _service.Overlay.NoticeSeconds));
        }
        _overlayDirty = true;
        UpdateOverlay();
        SendOverlay();
    }

    void StartOverlay()
    {
        if (_renderer != null) _renderer.Overlay = _overlayFeed;
        _overlayTimer.Tick += (_, _) => UpdateOverlay();
        _overlayTimer.Start();
    }

    /// <summary>Tempo do temporizador em segundos (restante na contagem regressiva; passado no cronômetro).</summary>
    double TimerSeconds()
    {
        var t = _stageTimer;
        double elapsed = t.Accumulated + (t.Running ? (DateTimeOffset.Now.ToUnixTimeMilliseconds() - t.StartedAt) / 1000 : 0);
        return t.Mode == "countdown" ? Math.Max(0, t.DurationSeconds - elapsed) : elapsed;
    }

    static string FormatTime(double seconds)
    {
        int s = (int)Math.Ceiling(seconds);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }

    /// <summary>Texto da contagem: a frase em tamanho menor e o tempo; no zero, a frase final (se houver).</summary>
    string TimerText(string label, string end, double seconds)
    {
        if (_stageTimer.Mode == "countdown" && seconds <= 0 && !string.IsNullOrWhiteSpace(end)) return end.Trim();
        return string.IsNullOrWhiteSpace(label) ? FormatTime(seconds) : $"<tam 30%>{label.Trim()}</tam>\n{FormatTime(seconds)}";
    }

    /// <summary>Confere o que deveria estar no telão. Só redesenha quando o texto muda.</summary>
    void UpdateOverlay()
    {
        var o = _service.Overlay;
        string? text = null;
        LyricStyle style = o.NoticeStyle;
        bool background = false;
        switch (_overlayMode)
        {
            case "timer":
                text = TimerText(o.TimerLabel, o.TimerEndText, TimerSeconds());
                style = o.TimerStyle;
                background = o.TimerBackground;
                break;
            case "notices":
                if (DateTime.Now >= _overlayNextNotice)
                {
                    var list = o.Notices.Where(n => !string.IsNullOrWhiteSpace(n.Text)).ToList();
                    if (list.Count > 0)
                    {
                        int i = list.FindIndex(n => n.Id == _overlayNoticeId);
                        _overlayNoticeId = list[(i + 1) % list.Count].Id;
                        SendOverlay();
                    }
                    _overlayNextNotice = DateTime.Now.AddSeconds(Math.Max(1, o.NoticeSeconds));
                }
                goto case "notice";
            case "notice":
                text = _overlayText ?? o.Notices.FirstOrDefault(n => n.Id == _overlayNoticeId)?.Text;
                background = o.NoticeBackground;
                break;
        }
        if (string.IsNullOrWhiteSpace(text)) text = null;
        if (_renderer != null) _renderer.OverlayBackground = background && text != null;
        if (text == _overlayShown && !_overlayDirty) return;
        _overlayDirty = false;
        _overlayShown = text;
        _overlayFeed.Show(text, style);
    }

    /// <summary>Fontes do mapeamento (na ordem dos grupos do motor), da última vez que o mapeamento foi aplicado.</summary>
    List<string> _overlaySources = [];

    void ApplyOverlayTargets()
    {
        if (_renderer == null) return;
        _renderer.OverlayGroups = _service.Overlay.Screens is { Count: > 0 } only
            ? [.. only.Select(id => _overlaySources.IndexOf(id)).Where(i => i >= 0)]
            : null;
    }

    /// <summary>Os telões possíveis (cada fonte usada nos slices e no LED), com nome, para escolher onde os avisos aparecem.</summary>
    object[] OverlayTargets()
    {
        var used = _mapping.Screens.SelectMany(s => s.Slices.Select(x => x.Source)
            .Concat(s.Led is { } led ? led.Parts.Select(p => p.Source).Append(led.Source) : []))
            .Where(x => x != null).Select(x => x!).Distinct().ToList();
        return [.. used.Select(id => new
        {
            id,
            name = _mapping.Groups.FirstOrDefault(g => g.Id == id)?.Name
                ?? (id.StartsWith("layer:") && int.TryParse(id[6..], out var n) && n < _comp.Layers.Count ? $"Layer {_comp.Layers[n].Name}" : id),
        })];
    }

    void SendOverlay() => Send(new { type = "overlay", settings = _service.Overlay, mode = _overlayMode, noticeId = _overlayNoticeId, targets = OverlayTargets() });

    void SaveService()
    {
        try { _service.Save(ServiceLibrary.DefaultPath); }
        catch (Exception ex) { Send(new { type = "error", message = $"Não consegui salvar o roteiro: {ex.Message}" }); }
    }
}