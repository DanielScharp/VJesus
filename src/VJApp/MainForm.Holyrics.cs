using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace VJApp;

/// <summary>
/// Holyrics → VJesus: o VJesus lê o slide que o Holyrics está projetando (letra, versículo ou texto) pela
/// API Server do Holyrics e mostra no telão do VJesus, com a aparência das Letras (versículo: a da Bíblia).
/// O operador passa os slides no Holyrics como sempre; os vídeos e efeitos continuam no VJesus.
/// API: POST http://IP:PORTA/api/GetCurrentPresentation?token=TOKEN  {"include_slides": true}
/// </summary>
sealed partial class MainForm
{
    sealed class HolyricsSettings
    {
        public bool Enabled { get; set; }
        /// <summary>IP do computador do Holyrics (127.0.0.1 = este mesmo computador).</summary>
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 8091;
        public string Token { get; set; } = "";
    }

    static string HolyricsPath => Path.Combine(AppDataDir, "holyrics.json");
    HolyricsSettings _holyrics = LoadHolyrics();
    CancellationTokenSource? _holyricsLoop;
    string _holyricsStatus = "desligado";
    bool _holyricsOk;
    string? _holyricsShown;   // último estado aplicado (para só mexer quando muda)
    string? _holyricsText;    // texto que veio do Holyrics e está no telão
    bool _holyricsVerse;      // é versículo (usa a aparência da Bíblia)
    string? _holyricsName;    // nome do item no Holyrics (música, referência)

    static readonly HttpClient HolyricsHttp = new() { Timeout = TimeSpan.FromSeconds(2) };
    static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    static HolyricsSettings LoadHolyrics()
    {
        try { return File.Exists(HolyricsPath) ? JsonSerializer.Deserialize<HolyricsSettings>(File.ReadAllText(HolyricsPath)) ?? new() : new(); }
        catch { return new(); }
    }

    void OnHolyricsMessage(string type, JsonNode msg)
    {
        if (type != "holyricsSet" || msg["settings"].Deserialize<HolyricsSettings>(JsonRead) is not { } s) { SendHolyrics(); return; }
        s.Host = string.IsNullOrWhiteSpace(s.Host) ? "127.0.0.1" : s.Host.Trim();
        s.Token = s.Token?.Trim() ?? "";
        s.Port = Math.Clamp(s.Port, 1, 65535);
        _holyrics = s;
        try { File.WriteAllText(HolyricsPath, JsonSerializer.Serialize(s)); } catch { }
        StartHolyrics();
    }

    /// <summary>Liga (ou desliga) a leitura do Holyrics conforme a configuração.</summary>
    void StartHolyrics()
    {
        _holyricsLoop?.Cancel();
        _holyricsLoop = null;
        _holyricsShown = null;
        if (!_holyrics.Enabled) { SetHolyricsStatus(false, "desligado"); return; }
        if (string.IsNullOrEmpty(_holyrics.Token)) { SetHolyricsStatus(false, "falta o token (veja como criar abaixo)"); return; }
        var cts = _holyricsLoop = new CancellationTokenSource();
        var cfg = _holyrics;
        SetHolyricsStatus(false, "conectando…");
        _ = Task.Run(() => HolyricsLoop(cfg, cts.Token));
    }

    async Task HolyricsLoop(HolyricsSettings cfg, CancellationToken stop)
    {
        string url = $"http://{cfg.Host}:{cfg.Port}/api/GetCurrentPresentation?token={Uri.EscapeDataString(cfg.Token)}";
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var body = new StringContent("{\"include_slides\":true}", Encoding.UTF8, "application/json");
                using var res = await HolyricsHttp.PostAsync(url, body, stop);
                var json = JsonNode.Parse(await res.Content.ReadAsStringAsync(stop));
                if ((string?)json?["status"] != "ok")
                {
                    string err = (string?)json?["error"] ?? $"resposta {(int)res.StatusCode}";
                    BeginInvoke(() => SetHolyricsStatus(false, $"o Holyrics recusou: {err} (confira o token e a permissão GetCurrentPresentation)"));
                }
                else
                {
                    var (key, text, verse, name) = ReadPresentation(json["data"]);
                    BeginInvoke(() =>
                    {
                        if (stop.IsCancellationRequested) return;
                        SetHolyricsStatus(true, "conectado");
                        if (key != _holyricsShown) { _holyricsShown = key; ApplyHolyrics(text, verse, name); }
                    });
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                string why = ex is HttpRequestException or TaskCanceledException
                    ? $"sem resposta de {cfg.Host}:{cfg.Port} (o Holyrics está aberto com o API Server ligado?)"
                    : ex.Message;
                BeginInvoke(() => SetHolyricsStatus(false, why));
            }
            try { await Task.Delay(_holyricsOk ? 250 : 2000, stop); } catch { return; }
        }
    }

    /// <summary>O que o Holyrics está mostrando: o texto do slide atual (null = nada, tela em branco ou papel de parede).</summary>
    static (string Key, string? Text, bool Verse, string? Name) ReadPresentation(JsonNode? data)
    {
        if (data == null) return ("nada", null, false, null);
        string type = (string?)data["type"] ?? "";
        string slideType = (string?)data["slide_type"] ?? "default";
        int number = (int?)data["slide_number"] ?? 1;
        string? name = (string?)data["name"];
        string? text = null;
        if (type is "song" or "verse" or "text" or "announcement" or "quick_presentation" && slideType == "default")
        {
            var slide = (data["slides"] as JsonArray)?.FirstOrDefault(s => (int?)s?["number"] == number)
                ?? (data["slides"] as JsonArray)?.ElementAtOrDefault(number - 1);
            text = (string?)slide?["text"];
            if (text != null) text = HtmlTag.Replace(text.Replace("<br>", "\n").Replace("<br/>", "\n"), "").Trim();
            if (string.IsNullOrWhiteSpace(text)) text = null;
        }
        return ($"{type}|{data["id"]}|{number}|{slideType}|{text}", text, type == "verse", name);
    }

    /// <summary>Põe no telão do VJesus o que veio do Holyrics (ou tira, se o Holyrics limpou).</summary>
    void ApplyHolyrics(string? text, bool verse, string? name)
    {
        if (text == null)
        {
            // só tira do telão se o que está lá veio do Holyrics (não apaga o que o operador projetou no VJesus)
            if (_holyricsText != null && _liveText == _holyricsText && _liveSongId == null && _liveBible == null) ClearLyrics();
            _holyricsText = null;
            return;
        }
        _holyricsText = text;
        _holyricsVerse = verse;
        _holyricsName = name;
        _liveBible = null;
        _liveSongId = null;
        _liveSlide = -1;
        _liveText = text;
        _blank = false;
        UpdateLyricsOutput();
    }

    /// <summary>O texto no ar veio do Holyrics e é versículo: usa a aparência da Bíblia.</summary>
    bool LiveIsHolyricsVerse => _holyricsVerse && _holyricsText != null && _liveText == _holyricsText && _liveSongId == null && _liveBible == null;

    void SetHolyricsStatus(bool ok, string status)
    {
        if (ok == _holyricsOk && status == _holyricsStatus) return;
        _holyricsOk = ok;
        _holyricsStatus = status;
        SendHolyrics();
    }

    void SendHolyrics() => Send(new { type = "holyrics", settings = _holyrics, ok = _holyricsOk, status = _holyricsStatus });
}
