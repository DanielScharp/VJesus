using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Roteiro do culto: a lista em ordem (música, versículo, vídeo, aviso, contagem…). Uma tecla avança:
/// dentro da música passa o slide; no fim da música vai para o próximo item.
/// </summary>
sealed partial class MainForm
{
    string? _serviceCurrent; // id do item no ar
    int _serviceVerse;       // versículo no ar (item de Bíblia com vários versículos)

    void OnServiceMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "serviceSave":
                if (msg["plan"].Deserialize<ServicePlan>(JsonRead) is { } plan)
                {
                    _service.Active.Name = plan.Name;
                    _service.Active.Date = plan.Date;
                    _service.Active.Items = plan.Items;
                    if (_service.Active.Items.All(i => i.Id != _serviceCurrent)) _serviceCurrent = null;
                    SaveService();
                    SendService();
                }
                break;
            case "servicePlanNew":
            {
                // novo culto vazio, ou uma cópia do aberto (com novos itens para não misturar quem está no ar)
                var active = _service.Active;
                var created = new ServicePlan { Name = (string?)msg["name"] ?? "Culto", Date = (string?)msg["date"] };
                if ((bool?)msg["copy"] == true)
                    created.Items = [.. active.Items.Select(i => { var c = JsonSerializer.Deserialize<ServiceItem>(JsonSerializer.Serialize(i))!; c.Id = Guid.NewGuid().ToString("N")[..10]; return c; })];
                _service.Plans.Add(created);
                OpenPlan(created.Id);
                break;
            }
            case "servicePlanSelect":
                if (_service.Plans.Any(p => p.Id == (string?)msg["id"])) OpenPlan((string)msg["id"]!);
                break;
            case "servicePlanDelete":
            {
                string? id = (string?)msg["id"];
                _ = _service.Active;
                if (_service.Plans.Count > 1) _service.Plans.RemoveAll(p => p.Id == id);
                OpenPlan(_service.Active.Id);
                break;
            }
            case "serviceGo":
                if (_service.Active.Items.FirstOrDefault(i => i.Id == (string?)msg["id"]) is { } item) ServiceGo(item);
                break;
            case "serviceStep":
                ServiceStep((int?)msg["direction"] ?? 1);
                break;
            case "serviceReset":
                ServiceLeave(null);
                _serviceCurrent = null;
                SendService();
                break;
        }
    }

    /// <summary>Abre outro culto. O que está no telão continua lá até o próximo comando.</summary>
    void OpenPlan(string id)
    {
        _service.ActivePlan = id;
        _serviceCurrent = null;
        SaveService();
        SendService();
    }

    /// <summary>Próximo/anterior: anda dentro do item (slide, versículo) e, no fim dele, muda de item.</summary>
    void ServiceStep(int direction)
    {
        var items = _service.Active.Items;
        if (items.Count == 0) return;
        int cur = items.FindIndex(i => i.Id == _serviceCurrent);
        if (cur < 0)
        {
            if (direction > 0) ServiceGo(items[0]);
            return;
        }
        var item = items[cur];
        if (item.Kind == "song" && _liveSongId == item.SongId && _lyrics.Songs.FirstOrDefault(s => s.Id == item.SongId) is { } song)
        {
            int next = _liveSlide + direction;
            if (next >= 0 && next < LyricsLibrary.Slides(song.Text).Count)
            {
                ShowSlide(song.Id, next);
                SendService();
                return;
            }
        }
        else if (item.Kind == "bible" && _serviceVerse + direction >= item.Verse && _serviceVerse + direction <= item.VerseEnd)
        {
            _serviceVerse += direction;
            _ = ShowBibleAsync(new BiblePassage(item.Book!, item.Chapter, _serviceVerse, _serviceVerse));
            SendService();
            return;
        }
        int target = cur + direction;
        if (target >= items.Count) { Send(new { type = "notice", message = "Fim do roteiro" }); return; }
        if (target < 0) return;
        ServiceGo(items[target], fromEnd: direction < 0);
    }

    /// <summary>Tira do telão o que o item anterior deixou (contagem e aviso) antes de entrar o próximo.</summary>
    void ServiceLeave(ServiceItem? next)
    {
        var prev = _service.Active.Items.FirstOrDefault(i => i.Id == _serviceCurrent);
        if (prev?.Kind is "notice" or "timer" && next?.Kind is not ("notice" or "timer")) SetOverlay("off");
    }

    void ServiceGo(ServiceItem item, bool fromEnd = false)
    {
        ServiceLeave(item);
        _serviceCurrent = item.Id;
        switch (item.Kind)
        {
            case "song":
            {
                var song = _lyrics.Songs.FirstOrDefault(s => s.Id == item.SongId);
                var slides = song != null ? LyricsLibrary.Slides(song.Text) : [];
                if (slides.Count == 0)
                {
                    Send(new { type = "error", message = song == null ? $"A música \"{item.Title}\" não está mais na biblioteca." : $"A música \"{item.Title}\" está sem letra." });
                    break;
                }
                EnsureLyricsPlaying();
                ShowSlide(song!.Id, fromEnd ? slides.Count - 1 : 0);
                break;
            }
            case "bible":
                EnsureLyricsPlaying();
                _serviceVerse = fromEnd ? item.VerseEnd : item.Verse;
                _ = ShowBibleAsync(new BiblePassage(item.Book!, item.Chapter, _serviceVerse, _serviceVerse));
                break;
            case "text":
                EnsureLyricsPlaying();
                _liveSongId = null;
                _liveBible = null;
                _liveSlide = -1;
                _liveText = item.Text;
                _blank = false;
                UpdateLyricsOutput();
                break;
            case "clip":
                ClearLyrics();
                if (ServiceDeck(item) && item.Layer < _comp.Layers.Count && item.Column < _comp.Columns && _comp.Layers[item.Layer].Clips[item.Column] != null)
                {
                    Trigger(item.Layer, item.Column);
                    CompositionChanged();
                }
                else Send(new { type = "error", message = $"O clip \"{item.Title}\" não está mais nessa posição da grade." });
                break;
            case "column":
                ClearLyrics();
                if (ServiceDeck(item) && item.Column < _comp.Columns)
                {
                    for (int i = 0; i < _comp.Layers.Count; i++)
                    {
                        if (_comp.Layers[i].Clips[item.Column] != null) Trigger(i, item.Column);
                        else ClearLayer(i);
                    }
                    CompositionChanged();
                }
                else Send(new { type = "error", message = $"A coluna de \"{item.Title}\" não existe mais." });
                break;
            case "notice":
                ClearLyrics();
                SetOverlay("notice", null, item.Text);
                break;
            case "timer":
                ClearLyrics();
                StageTimerAction("reset", "countdown", Math.Max(1, item.Seconds));
                StageTimerAction("start", null, null);
                SetOverlay("timer");
                break;
            case "clear":
                ClearLyrics();
                SetOverlay("off");
                break;
        }
        SendService();
    }

    /// <summary>Abre a aba de clips onde o item foi criado. Falso se ela não existe mais.</summary>
    bool ServiceDeck(ServiceItem item)
    {
        if (item.DeckId == null || _comp.Current.Id == item.DeckId) return true;
        int i = _comp.Decks.FindIndex(d => d.Id == item.DeckId);
        if (i < 0) return false;
        _comp.StoreDeck();
        _comp.LoadDeck(i);
        return true;
    }

    /// <summary>A letra só aparece com o clip Letras tocando: se ele está na grade mas parado, toca.</summary>
    void EnsureLyricsPlaying()
    {
        if (_comp.Layers.Any(l => l.Active >= 0 && l.PlayingClip?.Kind == ClipKind.Lyrics)) return;
        for (int l = _comp.Layers.Count - 1; l >= 0; l--)
        {
            int c = Array.FindIndex(_comp.Layers[l].Clips, x => x?.Kind == ClipKind.Lyrics);
            if (c < 0) continue;
            Trigger(l, c);
            CompositionChanged();
            return;
        }
    }

    void SendService() => Send(new
    {
        type = "service",
        planId = _service.Active.Id,
        name = _service.Active.Name,
        date = _service.Active.Date,
        items = _service.Active.Items,
        plans = _service.Plans.Select(p => new { id = p.Id, name = p.Name, date = p.Date, count = p.Items.Count }),
        current = _serviceCurrent,
        verse = _serviceVerse,
    });
}
