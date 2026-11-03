using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Carrossel: cada aba de clips pode passar sozinha de coluna em coluna, num tempo próprio, em todas
/// as layers da aba ou só nas escolhidas. Volta para a primeira coluna depois da última.
/// </summary>
sealed partial class MainForm
{
    sealed class CarouselRun
    {
        public int Column = -1;
        public DateTime Next;
    }

    readonly Dictionary<string, CarouselRun> _carousels = [];
    readonly System.Windows.Forms.Timer _carouselTimer = new() { Interval = 100 };

    void OnCarouselMessage(string type, JsonNode msg)
    {
        var deck = _comp.Decks.FirstOrDefault(d => d.Id == (string?)msg["deckId"]);
        if (deck == null) return;
        switch (type)
        {
            case "carouselSet":
                if (msg["settings"].Deserialize<CarouselSettings>(JsonRead) is { } s)
                {
                    s.Seconds = Math.Clamp(s.Seconds, 0.5f, 3600);
                    deck.Carousel = s;
                    if (_carousels.TryGetValue(deck.Id, out var run))
                        run.Next = DateTime.Now.AddSeconds(s.Seconds); // o tempo novo vale a partir de agora
                    ScheduleSave();
                    SendComposition(withThumbs: false);
                    SendCarousels();
                }
                break;
            case "carouselRun":
                if ((bool?)msg["on"] == true)
                {
                    deck.Carousel ??= new CarouselSettings();
                    _carousels[deck.Id] = new CarouselRun();
                    StepCarousel(deck, _carousels[deck.Id]); // a primeira coluna entra na hora
                    if (!_carouselTimer.Enabled) _carouselTimer.Start();
                }
                else _carousels.Remove(deck.Id);
                SendCarousels();
                break;
            case "carouselStep":
                if (_carousels.TryGetValue(deck.Id, out var r)) { StepCarousel(deck, r, (int?)msg["direction"] ?? 1); SendCarousels(); }
                break;
        }
    }

    void StartCarousels() => _carouselTimer.Tick += (_, _) =>
    {
        if (_carousels.Count == 0) { _carouselTimer.Stop(); return; }
        bool changed = false;
        foreach (var (id, run) in _carousels.ToList())
        {
            var deck = _comp.Decks.FirstOrDefault(d => d.Id == id);
            if (deck == null) { _carousels.Remove(id); changed = true; continue; }
            if (DateTime.Now < run.Next) continue;
            StepCarousel(deck, run);
            changed = true;
        }
        if (changed) SendCarousels();
    };

    /// <summary>Layers que o carrossel desta aba move (todas, ou só as escolhidas que ainda existem).</summary>
    List<int> CarouselLayers(CarouselSettings s) =>
        s.AllLayers ? [.. Enumerable.Range(0, _comp.Layers.Count)] : [.. s.Layers.Where(l => l >= 0 && l < _comp.Layers.Count).Distinct().Order()];

    /// <summary>Vai para a próxima coluna que tem algum clip nas layers do carrossel e toca essa coluna nelas.</summary>
    void StepCarousel(Deck deck, CarouselRun run, int direction = 1)
    {
        var s = deck.Carousel ??= new CarouselSettings();
        run.Next = DateTime.Now.AddSeconds(Math.Max(0.5f, s.Seconds));
        _comp.StoreDeck();
        var layers = CarouselLayers(s);
        Clip? At(int l, int c) => l < deck.Clips.Count && c < deck.Clips[l].Length ? deck.Clips[l][c] : null;
        int columns = deck.Columns;
        if (layers.Count == 0 || columns == 0) return;

        int col = run.Column;
        for (int k = 0; k < columns; k++)
        {
            col = ((col + direction) % columns + columns) % columns;
            if (!s.SkipEmpty || layers.Any(l => At(l, col) != null)) break;
            if (k == columns - 1) return; // nenhuma coluna com clip nessas layers
        }
        run.Column = col;
        foreach (int l in layers)
        {
            if (At(l, col) is { } clip) Trigger(l, col, clip, deck.Id);
            else ClearLayer(l);
        }
        SendComposition(withThumbs: false);
    }

    void SendCarousels() => Send(new
    {
        type = "carousels",
        running = _carousels.Select(kv => new
        {
            deckId = kv.Key,
            column = kv.Value.Column,
            nextAt = new DateTimeOffset(kv.Value.Next).ToUnixTimeMilliseconds(),
        }),
    });
}
