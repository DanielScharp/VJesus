using System.Text.Json;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Outros computadores da rede acompanhando o culto, pelo navegador (sem instalar nada):
/// /telao (imagem ao vivo), /roteiro (a ordem do culto e o que está no ar) e /letra (o texto
/// do telão com fundo transparente, para a fonte "Navegador" do OBS no computador da transmissão).
/// </summary>
sealed partial class MainForm
{
    /// <summary>Chamado a cada 100 ms: manda o estado ao vivo para quem está com o roteiro ou a letra aberta.</summary>
    void PublishLive()
    {
        if (_remote is not { HasListeners: true } remote) return;
        remote.PublishLive(LiveStateJson());
    }

    string LiveStateJson()
    {
        string? title = null, next = null;
        int slide = -1, slides = 0;
        if (_liveBible is { } b)
            title = RefText(BookName(b.Book), b);
        else if (_liveSongId != null && _lyrics.Songs.FirstOrDefault(s => s.Id == _liveSongId) is { } song)
        {
            title = song.Title;
            var list = LyricsLibrary.Slides(song.Text);
            slide = _liveSlide;
            slides = list.Count;
            if (_liveSlide + 1 < list.Count) next = Plain(list[_liveSlide + 1].Text);
        }
        var plan = _service.Active;
        var t = _stageTimer;
        return JsonSerializer.Serialize(new
        {
            plan = plan.Name,
            date = plan.Date,
            current = _serviceCurrent,
            items = plan.Items.Select(i => new { id = i.Id, kind = i.Kind, title = i.Title, note = i.Note }),
            text = _blank ? null : Plain(_liveText),
            blank = _blank,
            title,
            slide,
            slides,
            next,
            panic = _panicOn,
            timer = t.Running || _overlayMode == "timer" ? FormatTime(TimerSeconds()) : null,
        }, Json);
    }

    static string? Plain(string? text) => text == null ? null : LyricsFeed.StripTags(text).Trim();
}
